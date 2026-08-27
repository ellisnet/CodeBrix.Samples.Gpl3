// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.MusicView;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/copy2image.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The dialog that turns a page of music — or the part of one the user
/// rubberbanded — into a picture.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>copy2image.Dialog</c>, control for control: the resolution
/// combo with its five standard values, the paper-colour checkbox, grey,
/// auto-crop, antialias and Scale 2x, a live preview of exactly what would be
/// written, and Save As.
/// </para>
/// <para>
/// ⚠ FOUR OF UPSTREAM'S BUTTONS ARE NOT HERE — Copy, Copy File, Drag and Drag
/// File. Three of them want a clipboard that carries IMAGE data or a FILE URL
/// and one wants a drag-and-drop source; the platform's clipboard reaches text
/// and the heads have no drag source, so a button that did nothing would be
/// worse than no button. Save As does the job all four were for, and the
/// omission is written into the wave's STATUS file for the W13 audit rather
/// than left to be discovered.
/// </para>
/// <para>
/// The preview redraws whenever a setting changes, which upstream does on a
/// background thread through <c>qpageview.backgroundjob.SingleRun</c>. It is
/// synchronous here because the render is a Skia draw of a parsed page — 3 to
/// 15 milliseconds, board §4.5 — and a thread to hide that would be a thread
/// to get wrong.
/// </para>
/// </remarks>
public sealed class CopyToImageDialog
{
    private static readonly string[] Resolutions = { "96", "192", "300", "600", "1200" };

    private readonly SettingsStore _settings;
    private readonly ScorePage _page;
    private readonly SKRect? _rect;
    private readonly string _fileName;

    private ComboBox _dpi;
    private CheckBox _colorCheck;
    private CheckBox _grayscale;
    private CheckBox _crop;
    private CheckBox _antialias;
    private CheckBox _scaleUp;
    private TextBlock _summary;
    private Image _preview;
    private ImageExporter _exporter;
    private bool _loading;

    /// <summary>Creates the dialog over a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="rect">The region, in page coordinates, or null for all of it.</param>
    /// <param name="fileName">The file the page came from, or null.</param>
    /// <param name="settings">Where the choices are remembered.</param>
    public CopyToImageDialog(
        ScorePage page, SKRect? rect, string fileName, SettingsStore settings)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _rect = rect;
        _fileName = fileName;
        _settings = settings;
    }

    /// <summary>Gets or sets who asks the user where to save.</summary>
    public Func<string, Task<string>> PickSavePathAsync { get; set; }

    /// <summary>Shows the dialog.</summary>
    /// <param name="xamlRoot">The root to attach it to.</param>
    /// <returns>The file that was saved, or null.</returns>
    public async Task<string> ShowAsync(XamlRoot xamlRoot)
    {
        string saved = null;

        var panel = new StackPanel { Spacing = 10, MinWidth = 720 };
        var row = new Grid { MinWidth = 700 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _preview = new Image
        {
            Stretch = Stretch.Uniform,
            MinHeight = 320,
            MaxHeight = 460,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var previewBorder = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x50, 0x50, 0x50)),
            Padding = new Thickness(8),
            Child = _preview,
        };
        Grid.SetColumn(previewBorder, 0);
        row.Children.Add(previewBorder);

        StackPanel controls = BuildControls();
        Grid.SetColumn(controls, 1);
        row.Children.Add(controls);
        panel.Children.Add(row);

        _summary = new TextBlock { Opacity = 0.8 };
        panel.Children.Add(_summary);

        var dialog = new ContentDialog
        {
            Title = I18n.Get("Image from {filename}").Replace(
                "{filename}",
                string.IsNullOrEmpty(_fileName)
                    ? I18n.Get("<unknown>")
                    : System.IO.Path.GetFileName(_fileName)),
            Content = panel,
            PrimaryButtonText = I18n.Get("&Save As...").Replace("&", string.Empty),
            CloseButtonText = I18n.Get("Close"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        //Board traps 43 and 50: the theme binds the dialog's width to a
        //RESOURCE, and overriding it only PERMITS the width — something inside
        //still has to ask for it, which the panel's MinWidth does.
        dialog.Resources["ContentDialogMaxWidth"] = 1100.0;
        dialog.Resources["ContentDialogMaxHeight"] = 900.0;

        ReadSettings();
        UpdateExport();

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            //The dialog must not close while the picker is up, and a deferral
            //is the only way to await inside this handler.
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();
            args.Cancel = true;
            try
            {
                saved = await SaveAsAsync();
            }
            finally
            {
                deferral.Complete();
            }

            if (saved != null) { dialog.Hide(); }
        };

        await dialog.ShowAsync();
        WriteSettings();
        _exporter?.Dispose();
        _exporter = null;
        return saved;
    }

    private StackPanel BuildControls()
    {
        var controls = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 0, 0) };
        controls.Children.Add(new TextBlock { Text = I18n.Get("DPI:") });

        _dpi = new ComboBox { IsEditable = true, MinWidth = 120 };
        foreach (string value in Resolutions) { _dpi.Items.Add(value); }

        _dpi.TextSubmitted += (_, _) => UpdateExport();
        _dpi.SelectionChanged += (_, _) => UpdateExport();
        controls.Children.Add(_dpi);

        _colorCheck = new CheckBox { Content = I18n.Get("Background:") };
        _colorCheck.Checked += (_, _) => UpdateExport();
        _colorCheck.Unchecked += (_, _) => UpdateExport();
        controls.Children.Add(_colorCheck);

        _grayscale = Check(I18n.Get("Gray"), I18n.Get("Convert image to grayscale."), controls);
        _crop = Check(I18n.Get("Auto-crop"), null, controls);
        _antialias = Check(I18n.Get("Antialias"), null, controls);
        _scaleUp = Check(
            I18n.Get("Scale 2x"),
            I18n.Get("Render twice as large and scale back down\n"
                + "(recommended for small DPI values)."),
            controls);
        return controls;
    }

    //⚠ StackPanel, not Panel — board trap 19, FOURTH sighting. `Panel` in this
    //namespace is the DOCK panel, and it shadows Microsoft.UI.Xaml.Controls.Panel
    //for every file in Fresco.Brix.Shell.
    private CheckBox Check(string text, string tooltip, StackPanel parent)
    {
        var box = new CheckBox { Content = text };
        if (tooltip != null) { ToolTipService.SetToolTip(box, tooltip); }

        box.Checked += (_, _) => UpdateExport();
        box.Unchecked += (_, _) => UpdateExport();
        parent.Children.Add(box);
        return box;
    }

    private void ReadSettings()
    {
        _loading = true;
        _dpi.Text = _settings?.GetString("copy_image/dpi", "300") ?? "300";
        _colorCheck.IsChecked = _settings?.GetBool("copy_image/papercolor", true) ?? true;
        _grayscale.IsChecked = _settings?.GetBool("copy_image/grayscale", false) ?? false;
        _crop.IsChecked = _settings?.GetBool("copy_image/autocrop", false) ?? false;
        _antialias.IsChecked = _settings?.GetBool("copy_image/antialias", true) ?? true;
        _scaleUp.IsChecked = _settings?.GetBool("copy_image/scaleup", false) ?? false;
        _loading = false;
    }

    private void WriteSettings()
    {
        if (_settings == null) { return; }

        _settings.SetString("copy_image/dpi", _dpi.Text);
        _settings.SetBool("copy_image/papercolor", _colorCheck.IsChecked == true);
        _settings.SetBool("copy_image/grayscale", _grayscale.IsChecked == true);
        _settings.SetBool("copy_image/autocrop", _crop.IsChecked == true);
        _settings.SetBool("copy_image/antialias", _antialias.IsChecked == true);
        _settings.SetBool("copy_image/scaleup", _scaleUp.IsChecked == true);
    }

    private void UpdateExport()
    {
        if (_loading) { return; }

        _exporter?.Dispose();
        _exporter = new ImageExporter(_page, _rect)
        {
            FileName = _fileName,
            Resolution = ParseResolution(_dpi.Text),
            PaperColor = _colorCheck.IsChecked == true ? SKColors.White : null,
            Grayscale = _grayscale.IsChecked == true,
            AutoCrop = _crop.IsChecked == true,
            Antialiasing = _antialias.IsChecked == true,
            Oversample = _scaleUp.IsChecked == true ? 2 : 1,
        };

        SKImage image = _exporter.Image();
        _preview.Source = ToImageSource(image);
        _summary.Text = image == null
            ? string.Empty
            : string.Format(
                CultureInfo.InvariantCulture, "{0} x {1}", image.Width, image.Height);
    }

    private async Task<string> SaveAsAsync()
    {
        Func<string, Task<string>> pick = PickSavePathAsync;
        if (pick == null || _exporter == null) { return null; }

        string path = await pick(_exporter.SuggestedFileName());
        if (string.IsNullOrEmpty(path)) { return null; }

        try
        {
            _exporter.Save(path);
            return path;
        }
        catch (Exception exception) when (
            exception is System.IO.IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            _summary.Text = I18n.Get("Could not save the image.") + " " + exception.Message;
            return null;
        }
    }

    private static double ParseResolution(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && value >= 10.0 && value <= 1200.0
            ? value
            : 300.0;

    /// <summary>Turns a Skia picture into something the platform can show.</summary>
    /// <param name="image">The picture.</param>
    /// <returns>The source, or null.</returns>
    /// <remarks>
    /// The pixels are copied straight into the platform's buffer, which is the
    /// route the Quick Insert icons already take.
    /// </remarks>
    private static Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap ToImageSource(SKImage image)
    {
        if (image == null) { return null; }

        using SKBitmap source = SKBitmap.FromImage(image);
        if (source == null) { return null; }

        //The same route the Quick Insert icons take: copy the pixels into the
        //platform's own buffer rather than encoding a PNG and decoding it
        //again. A page at 1200 dpi is a lot of bytes to spend twice.
        using SKPixmap pixels = image.PeekPixels();
        if (pixels == null) { return null; }

        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(
            image.Width, image.Height);
        int size = image.Width * image.Height * 4;
        byte[] buffer = new byte[size];
        System.Runtime.InteropServices.Marshal.Copy(pixels.GetPixels(), buffer, 0, size);
        using (System.IO.Stream target
            = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions
                .AsStream(bitmap.PixelBuffer))
        {
            target.Write(buffer, 0, buffer.Length);
        }

        bitmap.Invalidate();
        return bitmap;
    }
}
