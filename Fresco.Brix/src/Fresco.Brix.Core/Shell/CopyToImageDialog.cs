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
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

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
/// ⚠ UPSTREAM HAS FOUR BUTTONS BESIDE Save As — Copy, Copy File, Drag and Drag
/// File. <c>&amp;Copy</c> is here; the other three are not. Copy File puts a
/// FILE URL on the clipboard and the two Drag buttons want a drag-and-drop
/// SOURCE, which no head has (already recorded at W11 for
/// <c>gadgets/drag.py</c>).
/// </para>
/// <para>
/// ⚠ AND WHAT &amp;Copy DOES DEPENDS ON THE HEAD, which is a platform limit
/// rather than a choice here. <c>DataPackage.SetBitmap</c> is the platform's
/// own API and the Win32 and macOS clipboard extensions serve it (CF_DIB and
/// the NSPasteboard image respectively); the X11 extension's write path
/// advertises only text targets, so on X11 and Wayland another application
/// asking for an image is told there is none. The button is still upstream's
/// own and still the right one to press — Save As is the way through on those
/// heads, and the platform gap is on
/// <c>~/ClaudeHome/FIXLIST_codebrix_packages_2026-09-01.txt</c>.
/// //was previously: no Copy button at all, while the command that opens this
/// dialog is called "Copy to &amp;Image..." — so the menu entry promised
/// something the dialog could not do.
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
    private Widgets.ColorButton _paperColor;
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

        var panel = new StackPanel { Spacing = 10, MinWidth = 560 };
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
            SecondaryButtonText = MenuBuilder.Display(I18n.Get("&Copy")),
            CloseButtonText = I18n.Get("Close"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        //Board traps 43 and 50: the theme binds the dialog's width to a
        //RESOURCE, and overriding it only PERMITS the width — something inside
        //still has to ask for it, which the panel's MinWidth does.
        //was previously: written out. See Shell/DialogSizing.
        DialogSizing.Clamp(dialog, 1100, 900);

        //The colour button opens the application's own picker, which needs a
        //root of its own.
        _paperColor.DialogRoot = xamlRoot;

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

        //Upstream's `&Copy': the image goes on the clipboard and the dialog
        //closes. See the class remarks for what that reaches on each head.
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = !CopyToClipboard();
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

        //Upstream pairs the checkbox with a ColorButton tooltipped
        //_("Paper Color"), on the same row: the box says whether the paper is
        //painted at all, the button says what colour it is painted.
        //was previously: the checkbox alone, so the colour was always white.
        _colorCheck = new CheckBox { Content = I18n.Get("Background:") };
        _colorCheck.Checked += (_, _) => UpdateExport();
        _colorCheck.Unchecked += (_, _) => UpdateExport();

        _paperColor = new Widgets.ColorButton
        {
            Color = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };
        ToolTipService.SetToolTip(_paperColor, I18n.Get("Paper Color"));
        _paperColor.ColorChanged += (_, _) => UpdateExport();

        StackPanel colorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        colorRow.Children.Add(_colorCheck);
        colorRow.Children.Add(_paperColor);
        controls.Children.Add(colorRow);

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
        _paperColor.Color = ReadColor(
            _settings?.GetString("copy_image/papercolorvalue"),
            Windows.UI.Color.FromArgb(255, 255, 255, 255));
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
        _settings.SetString(
            "copy_image/papercolorvalue", WriteColor(PaperColorValue()));
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
            PaperColor = _colorCheck.IsChecked == true
                ? Skia(PaperColorValue())
                : null,
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

    /// <summary>Puts the rendered image on the clipboard.</summary>
    /// <returns>Whether anything was put there.</returns>
    /// <remarks>Upstream's <c>copyToClipboard</c>, which hands Qt a QImage.
    /// The platform's clipboard takes a stream of encoded bytes, so the PNG the
    /// exporter already knows how to make is what goes over.</remarks>
    private bool CopyToClipboard()
    {
        if (_exporter == null) { return false; }

        try
        {
            byte[] png = _exporter.Data();
            if (png == null || png.Length == 0) { return false; }

            InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
            using (Stream writer = stream.AsStreamForWrite())
            {
                writer.Write(png, 0, png.Length);
                writer.Flush();
            }

            stream.Seek(0);
            DataPackage package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(package);
            Clipboard.Flush();
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or NotSupportedException
            or System.IO.IOException)
        {
            _summary.Text = I18n.Get("Could not save the image.") + " " + exception.Message;
            return false;
        }
    }

    /// <summary>The paper colour the button holds, white when it holds none.</summary>
    /// <returns>The colour.</returns>
    private Windows.UI.Color PaperColorValue()
        => _paperColor?.Color ?? Windows.UI.Color.FromArgb(255, 255, 255, 255);

    /// <summary>Turns a platform colour into the renderer's.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The Skia colour.</returns>
    private static SKColor Skia(Windows.UI.Color color)
        => new SKColor(color.R, color.G, color.B, color.A);

    /// <summary>Reads a stored <c>#rrggbb</c> colour.</summary>
    /// <param name="text">The stored text, or null.</param>
    /// <param name="fallback">What to answer when it names nothing.</param>
    /// <returns>The colour.</returns>
    private static Windows.UI.Color ReadColor(string text, Windows.UI.Color fallback)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '#' || text.Length != 7)
        {
            return fallback;
        }

        return uint.TryParse(
            text.Substring(1),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out uint value)
            ? Windows.UI.Color.FromArgb(
                255, (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : fallback;
    }

    /// <summary>Writes a colour as <c>#rrggbb</c>.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The text.</returns>
    private static string WriteColor(Windows.UI.Color color)
        => string.Format(
            CultureInfo.InvariantCulture,
            "#{0:x2}{1:x2}{2:x2}",
            color.R,
            color.G,
            color.B);

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
