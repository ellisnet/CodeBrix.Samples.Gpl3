// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.UI;

namespace Fresco.Brix.MusicView; //was previously: frescobaldi/musicview/__init__.py + musicview/widget.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Music View: the engraved score, beside the source it came from, with the
/// two kept pointing at each other.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's panel and widget are one class here, because the split existed to
/// keep a dock widget's lazily-created contents apart from the dock widget, and
/// <see cref="Shell.Panel"/> already does exactly that.
/// </para>
/// <para>
/// Two-way point and click is the whole point of the thing. Clicking an object
/// in the music puts the caret on the source that produced it — through
/// anchors, so it keeps working after the user has typed. Moving the caret
/// highlights the objects that came from where it now is; that direction is
/// off by default, as upstream has it, and the Jump to Cursor Position command
/// does it once on demand.
/// </para>
/// </remarks>
public sealed class MusicViewPanel : Shell.Panel
{
    /// <summary>The setting remembering the zoom and layout between sessions.</summary>
    public const string ViewSettingsPrefix = "musicview/";

    private readonly DocumentManager _documents;
    private readonly SettingsStore _settings;
    private readonly Highlighter _musicHighlighter = new Highlighter();
    private readonly Highlighter _linkHighlighter = new Highlighter { Color = new SKColor(0x88, 0x88, 0x88) };
    private readonly IScoreTypefaceResolver _typefaces;

    private MusicViewControl _view;
    private MusicViewContextMenu _contextMenu;
    private ComboBox _chooser;
    private TextBlock _pageLabel;
    private EditorDocument _document;
    private EditorView _editorView;
    private PointAndClickLinks _links;
    private IReadOnlyList<MusicDocument> _scores = Array.Empty<MusicDocument>();
    private (int Start, int Length)? _highlightRange;
    private bool _clickingLink;
    private bool _updatingChooser;

    /// <summary>Creates the Music View panel.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="actions">The Music View's own commands.</param>
    /// <param name="typefaces">Who answers the score's font families (trap 9).</param>
    /// <param name="settings">The settings store, or null.</param>
    public MusicViewPanel(
        DocumentManager documents,
        MusicViewActions actions,
        IScoreTypefaceResolver typefaces = null,
        SettingsStore settings = null)
        : base("musicview", DockArea.Right)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _settings = settings;
        _typefaces = typefaces;
        Actions = actions;

        ToggleAction.WithShortcut("Meta+Alt+M");
        ScoreDocuments.Typefaces = typefaces;

        _documents.CurrentDocumentChanged += (_, e) => SetDocument(e.Document);
        _documents.DocumentClosed += (_, e)
            => { if (e.Document == _document) { CloseDocument(); } };

        JobManager.AnyJobFinished += (_, e) => OnJobFinished(e);

        WireActions();
    }

    /// <summary>Gets the Music View's commands.</summary>
    public MusicViewActions Actions { get; }

    /// <summary>Gets or sets how the caret is put where a link points.</summary>
    /// <remarks>
    /// The panel does not know how to focus a view or open a document in one —
    /// that is the window's business, and the window fills this in.
    /// </remarks>
    public Action<EditorDocument, int> ShowCursor { get; set; }

    /// <summary>Gets or sets how the editor view showing a document is found.</summary>
    public Func<EditorView> CurrentEditorView { get; set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("window title", "Music View");

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        ToggleAction.Text = I18n.Get("&Music View");
        UpdatePageLabel();
    }

    /// <summary>Tells the panel which editor view the caret is now in.</summary>
    /// <param name="view">The view, or null.</param>
    public void SetEditorView(EditorView view)
    {
        if (ReferenceEquals(view, _editorView)) { return; }

        if (_editorView != null) { _editorView.CursorPositionChanged -= OnCursorPositionChanged; }

        _editorView = view;
        if (_editorView != null) { _editorView.CursorPositionChanged += OnCursorPositionChanged; }

        _view?.ClearHighlight(_musicHighlighter);
        _highlightRange = null;
    }

    /// <summary>Shows the objects the caret currently points at.</summary>
    /// <param name="scroll">Whether to scroll them into view.</param>
    /// <param name="milliseconds">How long to show them; null takes the default.</param>
    public void ShowCurrentLinks(bool scroll = false, int? milliseconds = null)
    {
        if (!IsVisible || _view == null || _links == null) { return; }

        EditorView view = _editorView ?? CurrentEditorView?.Invoke();
        if (view == null) { return; }

        BoundLinks bound = _links.BoundLinksFor(view.Document);
        if (bound == null) { return; }

        int start = view.Editor.SelectionStart;
        int length = view.Editor.SelectionLength;
        int caret = view.Editor.CaretOffset;
        (int Start, int Length)? range = length > 0
            ? bound.Indices(start, start + length, view.State.LyDocument)
            : bound.Indices(caret, caret, view.State.LyDocument);

        if (range == null) { return; }

        if (range.Value.Length == 0)
        {
            _view.ClearHighlight(_musicHighlighter);
            _highlightRange = null;
            return;
        }

        if (!scroll && _highlightRange == range && _view.IsHighlighting(_musicHighlighter)) { return; }

        _highlightRange = range;

        var areas = new Dictionary<ScorePage, List<SKRect>>();
        for (int i = range.Value.Start; i < range.Value.Start + range.Value.Length; i++)
        {
            if (i < 0 || i >= bound.Destinations.Count) { continue; }

            foreach (object destination in bound.Destinations[i])
            {
                if (destination is not (ScorePage page, Link link)) { continue; }

                if (!areas.TryGetValue(page, out List<SKRect> rects))
                {
                    rects = new List<SKRect>();
                    areas[page] = rects;
                }

                rects.Add(link.Rect());
            }
        }

        if (areas.Count == 0) { return; }

        var final = areas.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<SKRect>)kv.Value);
        if (scroll)
        {
            _view.EnsureVisible(_view.HighlightRect(final), new PageMargins(20));
        }

        int msec = milliseconds ?? (range.Value.Length > 1 ? 5000 : 2000);
        _view.Highlight(final, _musicHighlighter, msec);
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        _view = new MusicViewControl
        {
            LinkHighlighter = _linkHighlighter,
            ViewMode = ViewMode.FitWidth,
        };
        _view.LinkClicked += OnLinkClicked;
        _view.LinkHovered += OnLinkHovered;
        _view.LinkLeft += OnLinkLeft;
        _view.CurrentPageChanged += (_, _) => UpdatePageLabel();
        _view.ContextMenuRequested += OnContextMenuRequested;

        _contextMenu = new MusicViewContextMenu(Actions) { OpenExternalUrl = OpenExternalUrl };

        ReadSettings();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildToolBar());
        Grid.SetRow(_view, 1);
        root.Children.Add(_view);

        if (_document != null) { UpdateScores(); }

        return root;
    }

    private static string DisplayName(MusicDocument score)
        => score?.FileName == null ? string.Empty : Path.GetFileName(score.FileName);

    private UIElement BuildToolBar()
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Padding = new Thickness(4, 2, 4, 2),
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)),
        };

        _chooser = new ComboBox { MinWidth = 140, IsEnabled = false };
        _chooser.SelectionChanged += (_, _) =>
        {
            if (_updatingChooser) { return; }

            ShowScore(_chooser.SelectedIndex);
        };
        bar.Children.Add(_chooser);

        bar.Children.Add(ToolButton(Actions.MusicZoomOut, "-"));
        bar.Children.Add(ToolButton(Actions.MusicZoomOriginal, "1:1"));
        bar.Children.Add(ToolButton(Actions.MusicZoomIn, "+"));
        bar.Children.Add(ToolButton(Actions.MusicFitWidth, I18n.Get("Width")));
        bar.Children.Add(ToolButton(Actions.MusicFitHeight, I18n.Get("Height")));
        bar.Children.Add(ToolButton(Actions.MusicFitBoth, I18n.Get("Page")));
        bar.Children.Add(ToolButton(Actions.MusicPreviousPage, "<"));

        _pageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 70 };
        bar.Children.Add(_pageLabel);

        bar.Children.Add(ToolButton(Actions.MusicNextPage, ">"));
        bar.Children.Add(ToolButton(Actions.MusicJumpToCursor, I18n.Get("Jump")));
        return bar;
    }

    private Button ToolButton(AppAction action, string caption)
    {
        var button = new Button
        {
            Content = caption,
            Padding = new Thickness(6, 1, 6, 1),
            MinWidth = 0,
        };
        button.Click += (_, _) => action.Trigger();
        return button;
    }

    private void WireActions()
    {
        if (Actions == null) { return; }

        Actions.MusicZoomIn.Handler = () => WithView(v => v.ZoomIn());
        Actions.MusicZoomOut.Handler = () => WithView(v => v.ZoomOut());
        Actions.MusicZoomOriginal.Handler = () => WithView(v => v.ZoomOriginal());
        Actions.MusicFitWidth.Handler = () => SetViewMode(ViewMode.FitWidth);
        Actions.MusicFitHeight.Handler = () => SetViewMode(ViewMode.FitHeight);
        Actions.MusicFitBoth.Handler = () => SetViewMode(ViewMode.FitBoth);
        Actions.MusicSinglePages.Handler = () => SetLayoutEngine("single");
        Actions.MusicTwoPagesFirstRight.Handler = () => SetLayoutEngine("double_right");
        Actions.MusicTwoPagesFirstLeft.Handler = () => SetLayoutEngine("double_left");
        Actions.MusicRaster.Handler = () => SetLayoutEngine("raster");
        Actions.MusicHorizontal.Handler = () => SetOrientation(LayoutOrientation.Horizontal);
        Actions.MusicVertical.Handler = () => SetOrientation(LayoutOrientation.Vertical);
        Actions.MusicContinuous.Handler = () => WithView(v =>
        {
            v.ContinuousMode = Actions.MusicContinuous.IsChecked;
            WriteSettings();
        });
        Actions.MusicRotateLeft.Handler = () => WithView(v =>
        {
            v.PageRotation = (Rotation)(((int)v.PageRotation + 3) & 3);
            WriteSettings();
        });
        Actions.MusicRotateRight.Handler = () => WithView(v =>
        {
            v.PageRotation = (Rotation)(((int)v.PageRotation + 1) & 3);
            WriteSettings();
        });
        Actions.MusicNextPage.Handler = () => WithView(v => v.NextPage());
        Actions.MusicPreviousPage.Handler = () => WithView(v => v.PreviousPage());
        Actions.MusicJumpToCursor.Handler = () =>
        {
            Activate();
            ShowCurrentLinks(true, 10000);
        };
        Actions.MusicSyncCursor.Handler = () =>
        {
            _settings?.SetBool(MusicViewActions.SyncCursorSettingKey, Actions.MusicSyncCursor.IsChecked);
            ShowCurrentLinks(false);
        };
        Actions.MusicReload.Handler = () => { Activate(); ReloadView(); };
        Actions.MusicClear.Handler = ClearView;
        Actions.MusicSaveSettings.Handler = WriteSettings;

        Actions.MusicSyncCursor.IsChecked
            = _settings?.GetBool(MusicViewActions.SyncCursorSettingKey, false) ?? false;
    }

    private void WithView(Action<MusicViewControl> action)
    {
        Activate();
        if (_view != null) { action(_view); }
    }

    private void SetViewMode(ViewMode mode)
    {
        WithView(v =>
        {
            v.ViewMode = mode;
            WriteSettings();
        });
        UpdateModeChecks();
    }

    private void SetOrientation(LayoutOrientation orientation)
    {
        WithView(v =>
        {
            v.Layout.Orientation = orientation;
            v.UpdateViewport();
            WriteSettings();
        });
        Actions.MusicHorizontal.IsChecked = orientation == LayoutOrientation.Horizontal;
        Actions.MusicVertical.IsChecked = orientation == LayoutOrientation.Vertical;
    }

    private void SetLayoutEngine(string name)
    {
        WithView(v =>
        {
            v.Layout.Engine = name switch
            {
                "double_right" => new RowLayoutEngine { PagesPerRow = 2, PagesFirstRow = 1 },
                "double_left" => new RowLayoutEngine { PagesPerRow = 2, PagesFirstRow = 2 },
                "raster" => new RasterLayoutEngine(),
                _ => new LayoutEngine(),
            };
            v.UpdateViewport();
            WriteSettings();
        });

        Actions.MusicSinglePages.IsChecked = name == "single";
        Actions.MusicTwoPagesFirstRight.IsChecked = name == "double_right";
        Actions.MusicTwoPagesFirstLeft.IsChecked = name == "double_left";
        Actions.MusicRaster.IsChecked = name == "raster";
    }

    private void UpdateModeChecks()
    {
        if (_view == null) { return; }

        Actions.MusicFitWidth.IsChecked = _view.ViewMode == ViewMode.FitWidth;
        Actions.MusicFitHeight.IsChecked = _view.ViewMode == ViewMode.FitHeight;
        Actions.MusicFitBoth.IsChecked = _view.ViewMode == ViewMode.FitBoth;
    }

    private void SetDocument(EditorDocument document)
    {
        //Upstream only follows the current document once it has something to
        //show for it, so switching to a source that was never engraved leaves
        //the last score up rather than blanking the panel.
        if (_document != null && document != null && ScoreDocuments.For(document).Documents().Count == 0)
        {
            return;
        }

        _document = document;
        UpdateScores();
    }

    private void CloseDocument()
    {
        _document = null;
        _scores = Array.Empty<MusicDocument>();
        _links?.Detach();
        _links = null;
        _view?.Clear();
        UpdateChooser();
    }

    private void OnJobFinished(JobEventArgs e)
    {
        EditorDocument document = e?.Document;
        if (document == null) { return; }

        if (!ScoreDocuments.For(document).Update(settings: _settings)) { return; }

        ScoreDocuments.RaiseScoreUpdated(document);
        if (document == _document || _document == null)
        {
            _document = document;
            UpdateScores();
        }
    }

    private void UpdateScores()
    {
        _scores = _document == null
            ? Array.Empty<MusicDocument>()
            : ScoreDocuments.For(_document).Documents();
        UpdateChooser();
        ShowScore(_scores.Count > 0 ? 0 : -1);
    }

    private void UpdateChooser()
    {
        if (_chooser == null) { return; }

        _updatingChooser = true;
        try
        {
            _chooser.Items.Clear();
            foreach (MusicDocument score in _scores) { _chooser.Items.Add(DisplayName(score)); }

            _chooser.IsEnabled = _scores.Count > 0;
            if (_scores.Count > 0) { _chooser.SelectedIndex = 0; }
        }
        finally
        {
            _updatingChooser = false;
        }
    }

    private void ShowScore(int index)
    {
        if (_view == null) { return; }

        _links?.Detach();
        _links = null;
        _highlightRange = null;

        if (index < 0 || index >= _scores.Count)
        {
            _view.Clear();
            UpdatePageLabel();
            return;
        }

        MusicDocument score = _scores[index];
        _links = BuildLinks(score);
        _view.SetDocument(score);
        UpdatePageLabel();
    }

    private PointAndClickLinks BuildLinks(MusicDocument score)
    {
        var links = new PointAndClickLinks();
        foreach (ScorePage page in score.Pages)
        {
            foreach (Link link in page.Links())
            {
                if (!TextEditLink.TryParse(link.Url, out TextEditPlace place)) { continue; }

                links.AddLink(
                    PathUtil.NormPath(place.FileName), place.Line, place.Column, (page, link));
            }
        }

        links.Finish(_documents);
        return links;
    }

    private void ReloadView()
    {
        if (_document == null) { return; }

        ScoreDocuments group = ScoreDocuments.For(_document);
        if (group.Update(settings: _settings) || group.Update(false, _settings)) { UpdateScores(); }
    }

    private void ClearView()
    {
        if (_document != null) { ScoreDocuments.For(_document).Clear(); }

        _scores = Array.Empty<MusicDocument>();
        UpdateChooser();
        ShowScore(-1);
    }

    /// <summary>Gets or sets what to do with a link that leaves the score.</summary>
    /// <remarks>
    /// Filled in at W10 by the window, which owns the helper service that hands
    /// a URL to the desktop (upstream's <c>helpers.openUrl</c>, which is what
    /// its own context menu calls here).
    /// //was previously: this said the helpers module arrives with the
    /// documentation wave and the entry did nothing meanwhile.
    /// </remarks>
    public Action<string> OpenExternalUrl { get; set; }

    private void OnContextMenuRequested(object sender, MusicContextMenuEventArgs e)
    {
        (EditorDocument Document, int Offset)? source = null;
        if (e.Link != null && TextEditLink.TryParse(e.Link.Url, out TextEditPlace place))
        {
            source = _links?.Cursor(PathUtil.NormPath(place.FileName), place.Line, place.Column);
        }

        _contextMenu?.Show(e.Target, e.Position, e.Link, source);
    }

    private void OnLinkClicked(object sender, MusicLinkEventArgs e)
    {
        if (e.Properties is { IsRightButtonPressed: true }) { return; }

        if (!TextEditLink.TryParse(e.Link.Url, out TextEditPlace place)) { return; }

        var target = _links?.Cursor(PathUtil.NormPath(place.FileName), place.Line, place.Column, true);
        if (target == null) { return; }

        _clickingLink = true;
        try
        {
            ShowCursor?.Invoke(target.Value.Document, target.Value.Offset);
        }
        finally
        {
            _clickingLink = false;
        }
    }

    private void OnLinkHovered(object sender, MusicLinkEventArgs e)
    {
        EditorView view = _editorView ?? CurrentEditorView?.Invoke();
        if (view == null || _links == null) { return; }

        if (!TextEditLink.TryParse(e.Link.Url, out TextEditPlace place)) { return; }

        var target = _links.Cursor(PathUtil.NormPath(place.FileName), place.Line, place.Column);
        if (target == null || target.Value.Document != view.Document) { return; }

        IReadOnlyList<(int Start, int Length)> ranges
            = CursorPositions.Positions(view.State.LyDocument, target.Value.Offset);
        if (ranges.Count == 0) { return; }

        view.Highlighter.Highlight(
            HighlightGroups.MusicHighlight,
            ranges,
            HighlightColor(view),
            HighlightGroups.PriorityOf(HighlightGroups.MusicHighlight));
    }

    private void OnLinkLeft(object sender, EventArgs e)
    {
        EditorView view = _editorView ?? CurrentEditorView?.Invoke();
        view?.Highlighter.Clear(HighlightGroups.MusicHighlight);
    }

    private void OnCursorPositionChanged(object sender, EventArgs e)
        => ShowCurrentLinks(!_clickingLink && Actions.MusicSyncCursor.IsChecked);

    private Color HighlightColor(EditorView view)
    {
        Color color = view.State.Styler.Scheme.BaseColor("selectionbackground");
        return Color.FromArgb(128, color.R, color.G, color.B);
    }

    private void UpdatePageLabel()
    {
        if (_pageLabel == null) { return; }

        _pageLabel.Text = _view == null || _view.PageCount == 0
            ? string.Empty
            : I18n.Format(
                I18n.Get("{num} of {total}"),
                ("num", _view.CurrentPageNumber),
                ("total", _view.PageCount));
    }

    private void ReadSettings()
    {
        if (_view == null) { return; }

        SKColor paper = SKColors.White;
        _view.PaperColor = paper;

        if (_settings == null)
        {
            UpdateModeChecks();
            return;
        }

        _musicHighlighter.Color = ToSkia(new TextFormatData("default", _settings)
            .BaseColor("musichighlight"));

        string mode = _settings.GetString(ViewSettingsPrefix + "viewmode", "fitwidth");
        _view.ViewMode = mode switch
        {
            "fitheight" => ViewMode.FitHeight,
            "fitboth" => ViewMode.FitBoth,
            "fixed" => ViewMode.FixedScale,
            _ => ViewMode.FitWidth,
        };
        _view.ZoomFactor = _settings.GetDouble(ViewSettingsPrefix + "zoom", 1.0);
        _view.ContinuousMode = _settings.GetBool(ViewSettingsPrefix + "continuous", true);
        _view.DropShadowEnabled = _settings.GetBool(ViewSettingsPrefix + "shadow", true);
        _view.Layout.Margins = new PageMargins(_view.DropShadowEnabled ? 6 : 1);
        Actions.MusicContinuous.IsChecked = _view.ContinuousMode;
        UpdateModeChecks();
        SetLayoutEngine(_settings.GetString(ViewSettingsPrefix + "layout", "single"));
    }

    private void WriteSettings()
    {
        if (_settings == null || _view == null) { return; }

        _settings.SetString(ViewSettingsPrefix + "viewmode", _view.ViewMode switch
        {
            ViewMode.FitHeight => "fitheight",
            ViewMode.FitBoth => "fitboth",
            ViewMode.FixedScale => "fixed",
            _ => "fitwidth",
        });
        _settings.SetDouble(ViewSettingsPrefix + "zoom", _view.ZoomFactor);
        _settings.SetBool(ViewSettingsPrefix + "continuous", _view.ContinuousMode);
        _settings.SetString(ViewSettingsPrefix + "layout", _view.Layout.Engine switch
        {
            RasterLayoutEngine => "raster",
            RowLayoutEngine row => row.PagesFirstRow == 2 ? "double_left" : "double_right",
            _ => "single",
        });
    }

    private static SKColor ToSkia(Color color) => new SKColor(color.R, color.G, color.B, color.A);
}
