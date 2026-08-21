// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using View = Fresco.Brix.MusicView;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/musicpreview.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Engraves a piece of LilyPond source and shows the result, without it ever
/// being a document the user has open.
/// </summary>
/// <remarks>
/// The log is shown while the run happens and the pages replace it when there
/// are any — upstream's own stacked arrangement, and the reason a failed run
/// leaves the errors in front of the user rather than a blank page.
/// </remarks>
public sealed class MusicPreviewDialog
{
    private readonly LilyPortEngine _engine;
    private readonly View.IScoreTypefaceResolver _typefaces;
    private TextBox _log;
    private View.MusicViewControl _view;
    private Grid _stack;
    private TextBlock _status;

    /// <summary>Creates the preview.</summary>
    /// <param name="engine">The engine to run on.</param>
    /// <param name="typefaces">Who answers the score's font families.</param>
    public MusicPreviewDialog(
        LilyPortEngine engine, View.IScoreTypefaceResolver typefaces = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _typefaces = typefaces;
    }

    /// <summary>Engraves some source and shows it.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="text">The LilyPond source.</param>
    /// <param name="title">What to call the run, or null.</param>
    /// <returns>Nothing; the dialog is closed by the user.</returns>
    public async Task ShowAsync(XamlRoot xamlRoot, string text, string title = null)
    {
        ContentDialog dialog = new ContentDialog
        {
            Title = title ?? I18n.Get("Music Preview"),
            Content = BuildContent(),
            CloseButtonText = I18n.Get("&Close").Replace("&", string.Empty),
            XamlRoot = xamlRoot,
        };

        VolatileTextJob job = new VolatileTextJob(_engine, text, title);
        job.Output += (_, message) => AppendToLog(message.Text);

        //Show the dialog and start the run together: the log fills while the
        //user watches, which is the whole point of showing it first.
        Task<ContentDialogResult> shown = dialog.ShowAsync().AsTask();
        _status.Text = I18n.Get("Engraving...");

        try
        {
            await job.StartAsync();
            ShowResult(job);
        }
        catch (Exception error)
        {
            AppendToLog(error.Message + "\n");
        }

        await shown;
        job.Cleanup();
        _view?.SetDocument(null);
    }

    /// <summary>Builds the dialog's contents.</summary>
    /// <returns>The content.</returns>
    private UIElement BuildContent()
    {
        Grid root = new Grid { MinWidth = 760, Height = 520, RowSpacing = 4 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        _status = new TextBlock { Opacity = 0.8 };
        root.Children.Add(_status);

        _stack = new Grid();
        _log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _view = new View.MusicViewControl
        {
            ViewMode = View.ViewMode.FitWidth,
            LinksEnabled = false,
            Visibility = Visibility.Collapsed,
        };
        _stack.Children.Add(_log);
        _stack.Children.Add(_view);

        Grid.SetRow(_stack, 1);
        root.Children.Add(_stack);
        return root;
    }

    /// <summary>Writes a line into the log.</summary>
    /// <param name="text">The text.</param>
    private void AppendToLog(string text)
    {
        if (_log == null || string.IsNullOrEmpty(text)) { return; }

        _log.Text += text;
    }

    /// <summary>Shows what the run produced.</summary>
    /// <param name="job">The finished job.</param>
    private void ShowResult(VolatileTextJob job)
    {
        IReadOnlyList<string> pages = job.ResultFiles;
        if (pages.Count == 0)
        {
            //Nothing to show: the log is already in front of the user, which is
            //where the reason is.
            _status.Text = I18n.Get("Engraving failed.");
            return;
        }

        _status.Text = string.Empty;
        _view.SetDocument(View.MusicDocument.LoadSvgs(pages, _typefaces));
        _log.Visibility = Visibility.Collapsed;
        _view.Visibility = Visibility.Visible;
    }
}
