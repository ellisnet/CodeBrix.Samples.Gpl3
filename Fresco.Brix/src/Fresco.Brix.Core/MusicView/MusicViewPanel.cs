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
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private EditorDocument _document;
    private EditorView _editorView;
    private PointAndClickLinks _links;
    private IReadOnlyList<MusicDocument> _scores = Array.Empty<MusicDocument>();
    private (int Start, int Length)? _highlightRange;
    private bool _clickingLink;

    //was previously: the chooser's own SelectedIndex. The chooser moved to the
    //window's Music View Toolbar (board wave W14 / ruling FR16 — upstream's own
    //arrangement: its panel has NO toolbar), so the panel keeps the index
    //itself and the bar follows it.
    private int _currentScoreIndex = -1;

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

    /// <summary>Raised when the list of engraved scores changed.</summary>
    /// <remarks>Upstream's <c>DocumentChooserAction.documentsChanged</c>. The
    /// chooser that listens to it is on the window's toolbar.</remarks>
    public event EventHandler ScoresChanged;

    /// <summary>
    /// Raised when the page, the zoom or the view mode changed.
    /// </summary>
    /// <remarks>Upstream's view emits <c>currentPageNumberChanged</c>,
    /// <c>pageCountChanged</c>, <c>zoomFactorChanged</c> and
    /// <c>viewModeChanged</c> separately and <c>ViewActions</c> connects to all
    /// four; the toolbar here re-reads all of them at once, which is the same
    /// answer for a great deal less wiring.</remarks>
    public event EventHandler ViewStateChanged;

    /// <summary>Gets the engraved scores' names, for the toolbar's chooser.</summary>
    /// <returns>The names, in order.</returns>
    public IReadOnlyList<string> ScoreNames()
    {
        List<string> names = new List<string>(_scores.Count);
        foreach (MusicDocument score in _scores) { names.Add(DisplayName(score)); }

        return names;
    }

    /// <summary>Gets which score is shown, or -1.</summary>
    public int CurrentScoreIndex => _currentScoreIndex;

    /// <summary>Shows one of the engraved scores.</summary>
    /// <param name="index">Which one, or -1 for none.</param>
    public void SelectScore(int index)
    {
        if (index == _currentScoreIndex) { return; }

        ShowScore(index);
    }

    /// <summary>Gets how many pages the score being shown has.</summary>
    public int PageCount => _view?.PageCount ?? 0;

    /// <summary>Gets which page is shown, one-based, or 0.</summary>
    public int CurrentPageNumber => _view == null || _view.PageCount == 0
        ? 0
        : _view.CurrentPageNumber;

    /// <summary>Shows a page.</summary>
    /// <param name="number">The page, one-based.</param>
    public void GoToPage(int number) => WithView(v => v.SetCurrentPageNumber(number));

    /// <summary>Gets how the view fits its pages.</summary>
    public ViewMode CurrentViewMode => _view?.ViewMode ?? ViewMode.FixedScale;

    /// <summary>Gets the zoom factor, 1.0 being 100%.</summary>
    public double ZoomFactor => _view?.ZoomFactor ?? 1.0;

    /// <summary>Fits the pages the given way, and remembers it.</summary>
    /// <param name="mode">The mode.</param>
    /// <remarks>The toolbar's zoom chooser lists the three fit modes above the
    /// percentages, exactly as upstream's does.</remarks>
    public void ApplyViewMode(ViewMode mode) => SetViewMode(mode);

    /// <summary>Zooms to a factor, and remembers it.</summary>
    /// <param name="factor">The factor, 1.0 being 100%.</param>
    public void ApplyZoomFactor(double factor)
    {
        WithView(v =>
        {
            v.ViewMode = ViewMode.FixedScale;
            v.ZoomFactor = factor;
            WriteSettings();
        });
        UpdateModeChecks();
    }

    /// <summary>Gets or sets how the caret is put where a link points.</summary>
    /// <remarks>
    /// The panel does not know how to focus a view or open a document in one —
    /// that is the window's business, and the window fills this in.
    /// </remarks>
    public Action<EditorDocument, int> ShowCursor { get; set; }

    /// <summary>Gets or sets how the editor view showing a document is found.</summary>
    public Func<EditorView> CurrentEditorView { get; set; }

    /// <summary>Gets or sets how the panel asks whether Shift is held down.</summary>
    /// <remarks>Board trap 38: the answer comes from the keyboard source, which
    /// is the window's to ask, not from the pointer event's arguments. Upstream
    /// reads the click's own <c>ev.modifiers()</c> because Qt puts them there
    /// (<c>musicview/widget.py:131</c>).</remarks>
    public Func<bool> IsShiftHeld { get; set; }

    /// <summary>Says what a click on a link in the score does.</summary>
    /// <param name="rightButton">Whether it was the right button.</param>
    /// <param name="shiftHeld">Whether Shift was held down.</param>
    /// <returns>What the click means.</returns>
    /// <remarks>Upstream's <c>slotLinkClicked</c>, whole
    /// (<c>musicview/widget.py:129-140</c>): the right button does nothing here
    /// because the context menu has already had it; Shift opens Edit in Place
    /// at the place clicked; anything else moves the caret there.</remarks>
    public static MusicLinkAction LinkClickActionFor(bool rightButton, bool shiftHeld)
        => rightButton ? MusicLinkAction.None
            : shiftHeld ? MusicLinkAction.EditInPlace
            : MusicLinkAction.GoToCursor;

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
        //Upstream gives its Music View a rubberband when it BUILDS it
        //(`view.setRubberband(...)`), not on a command: the selection is how
        //Copy to Image knows what to copy, and its selectionChanged is what
        //enables that action at all. The magnifier is the one that is a
        //command, because it is a mode.
        _view.SetRubberBandEnabled(true);
        _view.RubberBand.SelectionChanged += (_, _) => UpdateSelectionActions();
        _view.LinkClicked += OnLinkClicked;
        _view.LinkHovered += OnLinkHovered;
        _view.LinkLeft += OnLinkLeft;
        _view.CurrentPageChanged += (_, _) => UpdatePageLabel();

        //The window's Music View Toolbar shows the page, the zoom and the fit
        //mode, so all three have to say when they move. Upstream connects
        //qpageview's four separate signals to its ViewActions for the same
        //reason (viewactions.ViewActions.setView).
        _view.ZoomChanged += (_, _) => ViewStateChanged?.Invoke(this, EventArgs.Empty);
        _view.ViewChanged += (_, _) => ViewStateChanged?.Invoke(this, EventArgs.Empty);
        _view.ContextMenuRequested += OnContextMenuRequested;

        _contextMenu = new MusicViewContextMenu(Actions)
        {
            OpenExternalUrl = OpenExternalUrl,
            HasSelection = () => _view?.RubberBand?.HasSelection == true,
            EditInPlace = (document, offset) => EditInPlace?.Invoke(document, offset),
            ShowHelp = () => ShowHelp?.Invoke(),
        };

        ReadSettings();

        //was previously: a Grid whose first row was the panel's OWN toolbar —
        //the score chooser plus Width/Height/Page/Jump buttons (audit A EXTRA-03,
        //GAP-26). Upstream's Music View panel has no toolbar of its own: every
        //one of those controls is on the window's Music View Toolbar, which
        //board wave W14 built. The panel is the view now, and nothing else.
        var root = new Grid();
        root.Children.Add(_view);

        if (_document != null) { UpdateScores(); }

        return root;
    }

    private static string DisplayName(MusicDocument score)
        => score?.FileName == null ? string.Empty : Path.GetFileName(score.FileName);

    /// <summary>Gets or sets who asks the user where to save an export.</summary>
    /// <remarks>
    /// The panel does not know how to put a file picker on the screen — that is
    /// the window's business, and the window fills this in, exactly as it does
    /// for <see cref="ShowCursor"/>. The three arguments are the suggested
    /// name, the file type's label and its suffix.
    /// </remarks>
    public Func<string, string, string, Task<string>> PickExportPathAsync { get; set; }

    /// <summary>Gets or sets where an export reports what it did, or null.</summary>
    public Action<string> Report { get; set; }

    /// <summary>Gets the score being shown, or null.</summary>
    /// <returns>The score.</returns>
    public MusicDocument CurrentScore()
        => _currentScoreIndex >= 0 && _currentScoreIndex < _scores.Count
            ? _scores[_currentScoreIndex]
            : null;

    /// <summary>Gets the page being shown, or null.</summary>
    /// <returns>The page.</returns>
    public ScorePage CurrentPage()
    {
        MusicDocument score = CurrentScore();
        if (score == null || score.Count == 0) { return null; }

        int number = _view?.CurrentPageNumber ?? 1;
        return score.Pages[Math.Clamp(number - 1, 0, score.Count - 1)];
    }

    /// <summary>Shows the Copy to Image dialog over the page or the selection.</summary>
    /// <returns>The task.</returns>
    private async Task CopyToImageAsync()
    {
        ScorePage page = CurrentPage();
        if (page == null) { Report?.Invoke(I18n.Get("There is no music to copy.")); return; }

        //Upstream copies the RUBBERBANDED region when there is one and the whole
        //page when there is not, and it picks the page with the biggest share of
        //the selection rather than the page that happens to be current.
        SKRect? rect = null;
        RubberBand band = _view?.RubberBand;
        if (band != null && band.HasSelection)
        {
            var (selected, region) = band.SelectedPage();
            if (selected != null)
            {
                page = selected;
                rect = region;
            }
        }

        var dialog = new Shell.CopyToImageDialog(page, rect, ScoreFileName(page), _settings)
        {
            PickSavePathAsync = name => PickExportPathAsync == null
                ? Task.FromResult<string>(null)
                : PickExportPathAsync(name, I18n.Get("PNG Image"), ".png"),
        };

        string saved = await dialog.ShowAsync(_view?.XamlRoot);
        if (saved != null) { Report?.Invoke(SavedMessage(saved)); }
    }

    private async Task ExportPdfAsync()
    {
        MusicDocument score = CurrentScore();
        if (score == null || score.Count == 0)
        {
            Report?.Invoke(I18n.Get("There is no music to export."));
            return;
        }

        string path = await Pick(
            Export.ScoreExport.SuggestedName(score, ".pdf"), I18n.Get("PDF File"), ".pdf");
        if (path == null) { return; }

        Run(() =>
        {
            var warnings = new List<string>();
            int pages = Export.ScoreExport.WritePdf(score, path, warnings: warnings);
            Report?.Invoke(SavedMessage(path) + " (" + pages + ")"
                + (warnings.Count > 0 ? " — " + warnings.Count + " " + I18n.Get("warnings") : string.Empty));
        });
    }

    private async Task ExportPngAsync()
    {
        ScorePage page = CurrentPage();
        if (page == null) { Report?.Invoke(I18n.Get("There is no music to export.")); return; }

        string path = await Pick(
            PageName(page, ".png"), I18n.Get("PNG Image"), ".png");
        if (path == null) { return; }

        Run(() =>
        {
            Export.ScoreExport.WritePng(page, path);
            Report?.Invoke(SavedMessage(path));
        });
    }

    private async Task ExportSvgAsync()
    {
        ScorePage page = CurrentPage();
        if (page == null) { Report?.Invoke(I18n.Get("There is no music to export.")); return; }

        string path = await Pick(PageName(page, ".svg"), I18n.Get("SVG File"), ".svg");
        if (path == null) { return; }

        Run(() =>
        {
            Export.ScoreExport.WriteSvg(page, path);
            Report?.Invoke(SavedMessage(path));
        });
    }

    private Task<string> Pick(string name, string label, string extension)
        => PickExportPathAsync == null
            ? Task.FromResult<string>(null)
            : PickExportPathAsync(name, label, extension);

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (
            exception is System.IO.IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            Report?.Invoke(exception.Message);
        }
    }

    private string SavedMessage(string path)
        => I18n.Get("Saved") + ": " + path;

    private static string PageName(ScorePage page, string extension)
        => page is SvgPage svg && !string.IsNullOrEmpty(svg.FileName)
            ? System.IO.Path.ChangeExtension(svg.FileName, extension)
            : "page" + extension;

    private static string ScoreFileName(ScorePage page)
        => page is SvgPage svg ? svg.FileName : null;

    /// <summary>
    /// Follows the rubberband: Copy to Image says what it would copy.
    /// </summary>
    /// <remarks>
    /// Upstream disables <c>music_copy_image</c> until there IS a selection.
    /// Here it stays enabled and copies the whole page when nothing is
    /// selected, because the page is a perfectly good thing to want a picture
    /// of and upstream's own dialog offers exactly that when it is opened from
    /// the context menu with no rubberband.
    /// </remarks>
    private void UpdateSelectionActions()
    {
        if (Actions?.MusicCopyImage == null) { return; }

        bool selected = _view?.RubberBand?.HasSelection == true;
        Actions.MusicCopyImage.ToolTip = selected
            ? I18n.Get("Copy the selected part of the music to a picture.")
            : I18n.Get("Copy the current page to a picture.");
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
        Actions.MusicMagnifier.Handler
            = () => WithView(v => v.SetMagnifierEnabled(Actions.MusicMagnifier.IsChecked));
        Actions.MusicCopyImage.AsyncHandler = CopyToImageAsync;
        Actions.MusicExportPdf.AsyncHandler = ExportPdfAsync;
        Actions.MusicExportPng.AsyncHandler = ExportPngAsync;
        Actions.MusicExportSvg.AsyncHandler = ExportSvgAsync;

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

    /// <summary>Answers whether a finished job's scores should take the panel.</summary>
    /// <param name="finished">The document the job ran for.</param>
    /// <param name="bound">The document the panel is bound to, or null.</param>
    /// <param name="current">The application's current document, or null.</param>
    /// <returns>True when the panel should show <paramref name="finished"/>.</returns>
    /// <remarks>
    /// <paramref name="bound"/> deliberately LAGS <paramref name="current"/>:
    /// <see cref="SetDocument"/> refuses to follow a source with nothing to show,
    /// so that switching to a never-engraved document leaves the last score up
    /// rather than blanking the panel. A job that finishes for the document the
    /// user is actually LOOKING at therefore has to be able to take the panel on
    /// its own — otherwise the panel stays bound to whatever came before and
    /// shows nothing at all, which is what engraving a document created by the
    /// Score Wizard (or opened from a file) did.
    /// </remarks>
    internal static bool AdoptsFinishedJob(
        EditorDocument finished, EditorDocument bound, EditorDocument current)
        => bound == null || finished == bound || finished == current;

    private void OnJobFinished(JobEventArgs e)
    {
        EditorDocument document = e?.Document;
        if (document == null) { return; }

        bool adopts = AdoptsFinishedJob(document, _document, _documents.CurrentDocument);

        if (!ScoreDocuments.For(document).Update(settings: _settings))
        {
            //was previously: a bare `return'. Nothing CHANGED about the scores,
            //so there is nothing to re-render — unless this panel was opened
            //AFTER they were registered, in which case it has never looked and
            //is showing nothing at all. That is what a File > Open or an import
            //followed by opening the Music View did, and switching tabs cleared
            //it. The guard is deliberately narrow: a finished job still does
            //not force a re-render for a panel that is already showing them.
            if (adopts && _scores.Count == 0)
            {
                _document = document;
                UpdateScores();
            }

            return;
        }

        ScoreDocuments.RaiseScoreUpdated(document);
        if (adopts)
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

    private void UpdateChooser() => ScoresChanged?.Invoke(this, EventArgs.Empty);

    private void ShowScore(int index)
    {
        if (_view == null)
        {
            _currentScoreIndex = index >= 0 && index < _scores.Count ? index : -1;
            return;
        }

        _links?.Detach();
        _links = null;
        _highlightRange = null;

        if (index < 0 || index >= _scores.Count)
        {
            _currentScoreIndex = -1;
            _view.Clear();
            UpdatePageLabel();
            return;
        }

        _currentScoreIndex = index;
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

    /// <summary>
    /// Gets or sets what the context menu's "Edit in Place" entry does with the
    /// source position under the pointer.
    /// </summary>
    /// <remarks>Only the window can put a dialog on screen and hand the editor
    /// its font, so the panel asks rather than opens — the same seam
    /// <see cref="ShowCursor"/> uses.</remarks>
    public Action<EditorDocument, int> EditInPlace { get; set; }

    /// <summary>
    /// Gets or sets what the context menu's Help entry opens — the user guide's
    /// <c>musicview</c> page.
    /// </summary>
    public Action ShowHelp { get; set; }

    private void OnContextMenuRequested(object sender, MusicContextMenuEventArgs e)
    {
        (EditorDocument Document, int Offset)? source = null;
        if (e.Link != null && TextEditLink.TryParse(e.Link.Url, out TextEditPlace place))
        {
            source = _links?.Cursor(PathUtil.NormPath(place.FileName), place.Line, place.Column);
        }

        _contextMenu?.Show(e.Target, e.Position, e.Link, source);
    }

    //was previously: the right button returned and everything else moved the
    //caret — upstream's Shift branch, which opens Edit in Place on the object
    //clicked, was missing even though the guide page this application ships
    //(musicview_editinplace) tells the user to use it.
    private void OnLinkClicked(object sender, MusicLinkEventArgs e)
    {
        MusicLinkAction action = LinkClickActionFor(
            e.Properties is { IsRightButtonPressed: true },
            IsShiftHeld?.Invoke() == true);
        if (action == MusicLinkAction.None) { return; }

        if (!TextEditLink.TryParse(e.Link.Url, out TextEditPlace place)) { return; }

        var target = _links?.Cursor(PathUtil.NormPath(place.FileName), place.Line, place.Column, true);
        if (target == null) { return; }

        if (action == MusicLinkAction.EditInPlace)
        {
            EditInPlace?.Invoke(target.Value.Document, target.Value.Offset);
            return;
        }

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

    //was previously: this wrote the panel toolbar's own page label. That label
    //is now the window toolbar's pager box, which formats the same msgid
    //("{num} of {total}") in PagerDisplay; the panel just says that something
    //moved.
    private void UpdatePageLabel() => ViewStateChanged?.Invoke(this, EventArgs.Empty);

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

        //was previously: the scheme name "default", written out. The Fonts &
        //Colors preferences page (W12A) lets the user keep more than one, so
        //the name comes from the setting that page writes.
        _musicHighlighter.Color = ToSkia(
            new TextFormatData(TextFormatData.CurrentScheme(_settings), _settings)
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

        //was previously: the orientation was not here and not in WriteSettings
        //either, so choosing Horizontal and then "Save current settings" dropped
        //it silently and the next launch came back vertical — every other part of
        //the view's state round-tripped. It is read and written with the rest
        //now, which is also the default the Music View preferences page sets.
        //The layout engine below calls UpdateViewport(), so the new orientation
        //is laid out without a second pass.
        _view.Layout.Orientation = string.Equals(
            _settings.GetString(ViewSettingsPrefix + "orientation", "vertical"),
            "horizontal",
            StringComparison.Ordinal)
                ? LayoutOrientation.Horizontal
                : LayoutOrientation.Vertical;
        Actions.MusicHorizontal.IsChecked
            = _view.Layout.Orientation == LayoutOrientation.Horizontal;
        Actions.MusicVertical.IsChecked
            = _view.Layout.Orientation == LayoutOrientation.Vertical;

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
        //was previously: missing, though SetOrientation calls this method for the
        //express purpose of remembering the choice. See ReadSettings.
        _settings.SetString(
            ViewSettingsPrefix + "orientation",
            _view.Layout.Orientation == LayoutOrientation.Horizontal
                ? "horizontal"
                : "vertical");
        _settings.SetString(ViewSettingsPrefix + "layout", _view.Layout.Engine switch
        {
            RasterLayoutEngine => "raster",
            RowLayoutEngine row => row.PagesFirstRow == 2 ? "double_left" : "double_right",
            _ => "single",
        });
    }

    private static SKColor ToSkia(Color color) => new SKColor(color.R, color.G, color.B, color.A);
}

/// <summary>What a click on a link in the score means.</summary>
/// <remarks>Upstream's three branches in <c>musicview/widget.py</c>'s
/// <c>slotLinkClicked</c>, named so the rule can be stated once and tested
/// without a window.</remarks>
public enum MusicLinkAction
{
    /// <summary>Nothing: the context menu has already had this click.</summary>
    None,

    /// <summary>Open the Edit in Place dialog at the place clicked.</summary>
    EditInPlace,

    /// <summary>Move the caret to the place clicked.</summary>
    GoToCursor,
}
