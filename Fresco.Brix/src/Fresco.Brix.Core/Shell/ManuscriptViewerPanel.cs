// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.CommandBar;
using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Manuscripts;
using Fresco.Brix.MusicView;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/viewers/ (__init__.py + pdfwidget.py + toolbar.py) and viewers/manuscript/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Manuscript Viewer: PDF files the user chose, beside the score, page by
/// page.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's is a five-file family — an abstract view panel, an abstract PDF
/// widget, an abstract toolbar, an abstract context menu, and a manuscript
/// subclass of each that mostly re-words captions. The abstraction exists there
/// so a second viewer could be written over it and never was: the manuscript
/// viewer is the only concrete descendant in Frescobaldi 4.0.7 (its own
/// context-menu module says so out loud — "our base class's context menu will
/// form the base for all future viewers' menus … we actually do not need this
/// subclass at all"). So it is ONE panel here, the way
/// <see cref="MusicView.MusicViewPanel"/> is one panel, and the msgids are the
/// concrete class's.
/// </para>
/// <para>
/// The pages are the Documentation Browser's: ruling FR8's rasteriser through
/// <see cref="Manuscripts.PdfManuscript"/>, drawn in exactly the paged view the
/// Music View uses — so zoom, the fit modes, continuous scrolling, the
/// rubber band and the magnifier are the same controls behaving the same way,
/// on all six heads, and this panel writes none of them.
/// </para>
/// <para>
/// ⚠ NO PRINTING, permanently (ruling FR5.5, and Jeremy again on 2026-09-02
/// when he ruled the panel into v1). Upstream's toolbar carries a Print button
/// and its <c>updateActions</c> exists to enable it; there is no such button,
/// no <c>viewer_print</c> action and no printing module here. The guide page
/// this panel opens has had the sentence removed.
/// </para>
/// <para>
/// ⚠ THE PANEL'S NAME IS <c>manuscriptview</c>, which is upstream's
/// <c>viewerName()</c> — the class name lowercased with a trailing "panel"
/// stripped. It is the settings group, the session key's stem, the context
/// menu's name and the user-guide page, in upstream as here, so it is not a
/// spelling to change.
/// </para>
/// </remarks>
public sealed class ManuscriptViewerPanel : Panel
{
    /// <summary>The panel's stable name.</summary>
    /// <remarks>Upstream's <c>viewerName()</c>.</remarks>
    public const string PanelName = "manuscriptview";

    /// <summary>The settings group the panel's own state lives under.</summary>
    public const string SettingsPrefix = PanelName + "/";

    /// <summary>The user guide page the panel's Help opens.</summary>
    /// <remarks>Upstream's <c>slotShowHelp</c> shows <c>viewerName()</c>.</remarks>
    public const string HelpPage = PanelName;

    private readonly SettingsStore _settings;
    private readonly DocumentManager _documents;

    //The colour the objects a caret points at are washed with — the Music
    //View's own highlighter, over this view's pages.
    private readonly Highlighter _highlight = new Highlighter();

    private MusicViewControl _view;
    private ManuscriptViewerContextMenu _contextMenu;
    private ToolBar _toolbar;
    private ComboBox _chooser;
    private ComboBox _zoomChooser;
    private TextBox _pager;
    private PointAndClickLinks _links;
    private EditorView _editorView;
    private IReadOnlyList<ZoomEntry> _zoomEntries = Array.Empty<ZoomEntry>();
    private (int Start, int Length)? _highlightRange;
    private bool _writingChooser;
    private bool _writingZoom;
    private bool _clickingLink;

    /// <summary>Creates the Manuscript Viewer.</summary>
    /// <param name="actions">The panel's commands.</param>
    /// <param name="documents">The open documents, for point and click.</param>
    /// <param name="settings">The settings store, or null.</param>
    public ManuscriptViewerPanel(
        ManuscriptViewerActions actions,
        DocumentManager documents = null,
        SettingsStore settings = null)
        : base(PanelName, DockArea.Right)
    {
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _documents = documents;
        _settings = settings;

        //Upstream's own panel shortcut and dock area
        //(viewers/manuscript/__init__.py:37-38).
        ToggleAction.WithShortcut("Meta+Alt+A");

        Manuscripts = new ManuscriptList();
        Manuscripts.Changed += (_, _) => FillChooser();
        Manuscripts.CurrentChanged += (_, _) => _ = ShowCurrentAsync();
        Manuscripts.Missing += (_, e) => ReportMissing?.Invoke(e.Paths);

        //Upstream: mainwindow().allDocumentsClosed → closeAllViewdocs. Closing
        //every document is what leaving a session does, and the manuscripts
        //belong to the session that was left.
        if (_documents != null)
        {
            _documents.DocumentClosed += (_, _) =>
            {
                if (_documents.Documents.Count == 0) { CloseAll(); }
            };
        }

        WireActions();
    }

    /// <summary>Gets the panel's commands.</summary>
    public ManuscriptViewerActions Actions { get; }

    /// <summary>Gets the manuscripts that are open.</summary>
    public ManuscriptList Manuscripts { get; }

    /// <summary>Gets or sets who asks the user which PDFs to open.</summary>
    /// <remarks>Only the window can put a file picker on the screen; upstream's
    /// <c>openViewdocs</c> calls <c>QFileDialog.getOpenFileNames</c> directly
    /// because a Qt panel is a widget.</remarks>
    public Func<Task<IReadOnlyList<string>>> PickManuscriptsAsync { get; set; }

    /// <summary>Gets or sets who asks the user where to save a picture.</summary>
    public Func<string, string, string, Task<string>> PickExportPathAsync { get; set; }

    /// <summary>Gets or sets what to do with a link that leaves the manuscript.</summary>
    /// <remarks>The window owns <see cref="HelperApplications"/>, which is the
    /// one place this application starts another program.</remarks>
    public Action<string> OpenExternalUrl { get; set; }

    /// <summary>Gets or sets how the caret is put where a link points.</summary>
    public Action<EditorDocument, int> ShowCursor { get; set; }

    /// <summary>Gets or sets how the editor view showing a document is found.</summary>
    public Func<EditorView> CurrentEditorView { get; set; }

    /// <summary>Gets or sets what "Edit in Place" does with a source position.</summary>
    public Action<EditorDocument, int> EditInPlace { get; set; }

    /// <summary>Gets or sets what the Help entry opens.</summary>
    public Action ShowHelp { get; set; }

    /// <summary>Gets or sets how the panel asks whether Shift is held down.</summary>
    /// <remarks>Board trap 38, exactly as the Music View has it.</remarks>
    public Func<bool> IsShiftHeld { get; set; }

    /// <summary>Gets or sets where the panel reports what it did, or null.</summary>
    public Action<string> Report { get; set; }

    /// <summary>
    /// Gets or sets how the user is asked whether to drop a manuscript whose
    /// file has gone.
    /// </summary>
    /// <remarks>
    /// Upstream's FIRST missing-file prompt (<c>pdfwidget.openViewdoc</c>): a
    /// yes/no dialog titled "Missing file", answered Yes to take the name off
    /// the list and No to leave it there so the file can be restored.
    /// </remarks>
    public Func<string, Task<bool>> AskDropMissingAsync { get; set; }

    /// <summary>
    /// Gets or sets how the user is told a session's manuscripts are gone.
    /// </summary>
    /// <remarks>Upstream's SECOND missing-file prompt
    /// (<c>reportMissingViewdocs</c>): a warning listing every file a restored
    /// session asked for and could not find.</remarks>
    public Action<IReadOnlyList<string>> ReportMissing { get; set; }

    /// <inheritdoc/>
    /// <remarks>Upstream's <c>setWindowTitle(_("Manuscript"))</c> — the dock's
    /// own caption, which is shorter than the menu entry because a dock tab has
    /// less room than a menu.</remarks>
    public override string Title => I18n.Get("Manuscript");

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        //Upstream's toggleViewAction text, which is the Tools > Viewers entry.
        ToggleAction.Text = I18n.Get("Manuscript Viewer");

        //The bar's title is the panel's, and it is read out as the tail of
        //every button's accessible name, so it follows the language too.
        if (_toolbar != null) { _toolbar.Title = Title; }

        UpdateViewState();
    }

    /// <summary>Gets the manuscript being shown, or null.</summary>
    /// <returns>The manuscript.</returns>
    public ManuscriptEntry Current() => Manuscripts.Current;

    /// <summary>Opens manuscripts, and shows the last of them.</summary>
    /// <param name="paths">The files.</param>
    public void Open(IEnumerable<string> paths)
    {
        Widget();
        Manuscripts.Load(paths);
    }

    /// <summary>Closes the manuscript being shown.</summary>
    /// <remarks>Upstream's <c>closeViewdoc</c>.</remarks>
    public void Close() => Manuscripts.Remove(Manuscripts.Current);

    /// <summary>Closes every manuscript but the one being shown.</summary>
    /// <remarks>Upstream's <c>closeOtherViewdocs</c>.</remarks>
    public void CloseOthers() => Manuscripts.RemoveOthers(Manuscripts.Current);

    /// <summary>Closes every manuscript.</summary>
    /// <remarks>Upstream's <c>closeAllViewdocs</c>.</remarks>
    public void CloseAll()
    {
        Manuscripts.RemoveAll();
        _view?.Clear();
    }

    /// <summary>Reads the manuscript being shown off the disk again.</summary>
    /// <remarks>
    /// Upstream's <c>ManuscriptViewPanel.reloadView</c>, which replaces the open
    /// document with a freshly loaded one over the same file name rather than
    /// asking the old one to refresh — because a <c>qpageview</c> document holds
    /// the file open. The same is true here: the picture cache and the
    /// rasteriser both belong to the opened PDF, so re-opening is the reload.
    /// </remarks>
    public void Reload()
    {
        ManuscriptEntry entry = Manuscripts.Current;
        if (entry == null) { return; }

        Widget();
        entry.Opened?.Dispose();
        entry.Opened = null;
        entry.IsPresent = System.IO.File.Exists(entry.Path);
        _ = ShowCurrentAsync();
    }

    /// <summary>Answers what a named session should remember.</summary>
    /// <returns>The paths, and which of them was in front.</returns>
    /// <remarks>Upstream's <c>slotSaveSessionData</c>: the file names and which
    /// one is active, under the key <c>&lt;viewerName&gt;-documents</c>.</remarks>
    public (IReadOnlyList<string> Paths, int Active) SessionData()
        => (Manuscripts.Paths(), Manuscripts.CurrentIndex);

    /// <summary>Restores the manuscripts a named session held.</summary>
    /// <param name="paths">The files.</param>
    /// <param name="active">Which of them was in front, or -1.</param>
    /// <remarks>
    /// Upstream's <c>slotSessionChanged</c>: the list is emptied WITHOUT
    /// announcing, refilled from what was stored with each entry's presence
    /// recorded, and only then refreshed — then <c>checkMissingFiles</c> raises
    /// the second prompt for whatever is gone.
    /// </remarks>
    public void RestoreSession(IReadOnlyList<string> paths, int active)
    {
        Manuscripts.RemoveAll(update: false);
        _view?.Clear();

        if (paths == null || paths.Count == 0)
        {
            Manuscripts.RemoveAll();
            return;
        }

        List<ManuscriptEntry> entries = paths
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => new ManuscriptEntry(path, System.IO.File.Exists(path)))
            .ToList();
        string activePath = active >= 0 && active < entries.Count
            ? entries[active].Path
            : null;

        Manuscripts.Load(entries, activePath);
        Manuscripts.CheckMissingFiles();
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        _view = new MusicViewControl { ViewMode = ViewMode.FitWidth };

        //Upstream gives the viewer its rubberband when it BUILDS the widget
        //(`view.setRubberband(...)'), because the selection is what Copy to
        //Image copies and what enables that action at all.
        _view.SetRubberBandEnabled(true);
        _view.RubberBand.SelectionChanged += (_, _) => UpdateSelectionActions();
        _view.LinkClicked += OnLinkClicked;
        _view.LinkHovered += OnLinkHovered;
        _view.LinkLeft += OnLinkLeft;
        _view.CurrentPageChanged += (_, _) => UpdateViewState();
        _view.ZoomChanged += (_, _) => UpdateViewState();
        _view.ViewChanged += (_, _) => UpdateViewState();
        _view.ContextMenuRequested += OnContextMenuRequested;

        _contextMenu = new ManuscriptViewerContextMenu(Actions)
        {
            Panel = this,
            OpenExternalUrl = url => OpenExternalUrl?.Invoke(url),
            EditInPlace = (document, offset) => EditInPlace?.Invoke(document, offset),
            ShowHelp = () => ShowHelp?.Invoke(),
            HasSelection = () => _view?.RubberBand?.HasSelection == true,
        };

        Grid root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildToolBar());
        Grid.SetRow(_view, 1);
        root.Children.Add(_view);

        ReadSettings();
        FillChooser();
        _ = ShowCurrentAsync();

        //The panel measures to nothing without this: its children scroll and a
        //dock tab hands out the DESIRED height (board trap 30).
        return new FillGrid { Children = { root } };
    }

    /// <summary>Builds the panel's toolbar.</summary>
    /// <returns>The bar.</returns>
    /// <remarks>
    /// <para>
    /// ONE bar, in upstream's own order (<c>AbstractViewerToolbar.populate</c>),
    /// with the chooser inline where upstream puts it and Help pushed to the far
    /// end by a filling spacer — which is upstream's own arrangement: a main
    /// toolbar and a right-aligned help toolbar with a stretch between them
    /// (<c>viewers/toolbar.py createLayout</c>).
    /// </para>
    /// <para>
    /// The bar used to be two rows, the second of them scrolling sideways with
    /// its scrollbar hidden, because a dock panel's toolbar had no overflow
    /// chevron and sixteen controls ran off the edge of a narrow dock (board
    /// trap 57). The CommandBar add-in's chevron is that missing affordance, so
    /// the panel is back to upstream's single bar — and the chooser stays near
    /// the front of it, where upstream has it, which is what keeps it the last
    /// control to move behind the chevron rather than the first.
    /// </para>
    /// <para>
    /// Two of upstream's entries are not here: Print, permanently (ruling
    /// FR5.5), and the separator that stood before it. Reload is here and is not
    /// upstream's — the board's W15 row puts it on the bar, because a manuscript
    /// that changed on disk is the one thing a reader of a manuscript most often
    /// wants a button for, and upstream reaches it only through the context menu.
    /// </para>
    /// </remarks>
    internal ToolBar BuildToolBar()
    {
        _toolbar = PanelToolbar.Create(Title, LabelMode.IconOnly);

        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerOpen));
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerClose));
        _toolbar.Items.Add(new ToolBarSeparator());
        _toolbar.Items.Add(BuildChooser());

        //was previously (upstream): a separator, viewer_print, a separator.
        //Printing is ruled out for good (FR5.5), so the button is gone and the
        //two separators it stood between are the one below.
        _toolbar.Items.Add(new ToolBarSeparator());
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerZoomIn));
        _toolbar.Items.Add(BuildZoomChooser());
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerZoomOut));
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerMagnifier));
        _toolbar.Items.Add(new ToolBarSeparator());
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerPreviousPage));
        _toolbar.Items.Add(BuildPager());
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerNextPage));
        _toolbar.Items.Add(new ToolBarSeparator());
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerRotateLeft));
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerRotateRight));
        _toolbar.Items.Add(new ToolBarSeparator());
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerReload));

        //Upstream keeps Help in a SECOND toolbar, right-aligned, "not intended
        //to be configured" (viewers/toolbar.py createLayout/populate). A filling
        //spacer is what pushes it there, and it has something to fill because
        //this bar stretches across a Grid row rather than sharing a tray with
        //another bar (the add-in's pitfall 7).
        _toolbar.Items.Add(new ToolBarSpacer { Fill = true });
        _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerHelp));

        UpdateViewState();
        return _toolbar;
    }

    /// <summary>Builds the chooser naming which manuscript is shown.</summary>
    /// <returns>The chooser.</returns>
    /// <remarks>
    /// Upstream's <c>ViewdocChooser</c> grows to fit its widest file name
    /// (<c>AdjustToContents</c>). A maximum is set here because this one shares
    /// a bar in a dock strip rather than a window's own toolbar: without it, one
    /// long file name would push every control after it behind the chevron.
    /// </remarks>
    private UIElement BuildChooser()
    {
        _chooser = new ComboBox
        {
            MinWidth = 120,
            MaxWidth = 220,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _chooser.SelectionChanged += (_, _) =>
        {
            if (_writingChooser) { return; }

            Manuscripts.SetCurrentIndex(_chooser.SelectedIndex);
        };
        AutomationProperties.SetName(
            _chooser, MenuBuilder.Display(Actions.ViewerDocumentSelect.Text));
        AutomationProperties.SetHelpText(_chooser, Title);
        ToolTipService.SetToolTip(_chooser, Actions.ViewerDocumentSelect.ToolTip);

        //Upstream's ViewdocChooserAction IS the combo, and triggering it drops
        //the list open — ComboBoxAction.showPopup — which is all a chooser can
        //do from the keyboard.
        Actions.ViewerDocumentSelect.Handler = () =>
        {
            Widget();
            Activate();
            if (_chooser == null) { return; }

            _chooser.Focus(FocusState.Programmatic);
            _chooser.IsDropDownOpen = true;
        };

        return _chooser;
    }

    private UIElement BuildZoomChooser()
    {
        _zoomEntries = ZoomLevels.Entries();
        _zoomChooser = new ComboBox
        {
            MinWidth = 92,
            ItemsSource = _zoomEntries.Select(entry => entry.Caption).ToList(),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(
            _zoomChooser, MenuBuilder.Display(Actions.ViewerZoomCombo.Text));
        AutomationProperties.SetHelpText(_zoomChooser, Title);
        _zoomChooser.SelectionChanged += (_, _) =>
        {
            if (_writingZoom) { return; }

            int index = _zoomChooser.SelectedIndex;
            if (index < 0 || index >= _zoomEntries.Count) { return; }

            ZoomEntry entry = _zoomEntries[index];
            if (entry.Mode is { } mode) { SetViewMode(mode); }
            else if (entry.Factor is { } factor) { SetZoomFactor(factor); }
        };

        Actions.ViewerZoomCombo.Handler = () =>
        {
            _zoomChooser.Focus(FocusState.Programmatic);
            _zoomChooser.IsDropDownOpen = true;
        };
        return _zoomChooser;
    }

    private UIElement BuildPager()
    {
        _pager = new TextBox
        {
            MinWidth = 76,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_pager, MenuBuilder.Display(I18n.Get("Page")));
        AutomationProperties.SetHelpText(_pager, Title);
        _pager.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) { return; }

            e.Handled = true;
            int page = PagerDisplay.Parse(_pager.Text, _view?.PageCount ?? 0);
            if (page > 0) { _view?.SetCurrentPageNumber(page); }

            UpdateViewState();
        };
        return _pager;
    }

    private void WireActions()
    {
        Actions.ViewerOpen.AsyncHandler = OpenAsync;
        Actions.ViewerClose.Handler = Close;
        Actions.ViewerCloseOther.Handler = CloseOthers;
        Actions.ViewerCloseAll.Handler = CloseAll;
        Actions.ViewerReload.Handler = Reload;
        Actions.ViewerZoomIn.Handler = () => WithView(v => v.ZoomIn());
        Actions.ViewerZoomOut.Handler = () => WithView(v => v.ZoomOut());
        Actions.ViewerZoomOriginal.Handler = () => WithView(v => v.ZoomOriginal());
        Actions.ViewerFitWidth.Handler = () => SetViewMode(ViewMode.FitWidth);
        Actions.ViewerFitHeight.Handler = () => SetViewMode(ViewMode.FitHeight);
        Actions.ViewerFitBoth.Handler = () => SetViewMode(ViewMode.FitBoth);
        Actions.ViewerRotateLeft.Handler = () => WithView(v =>
        {
            v.PageRotation = (Rotation)(((int)v.PageRotation + 3) & 3);
            WriteSettings();
        });
        Actions.ViewerRotateRight.Handler = () => WithView(v =>
        {
            v.PageRotation = (Rotation)(((int)v.PageRotation + 1) & 3);
            WriteSettings();
        });
        Actions.ViewerNextPage.Handler = () => WithView(v => v.NextPage());
        Actions.ViewerPreviousPage.Handler = () => WithView(v => v.PreviousPage());
        Actions.ViewerMagnifier.Handler
            = () => WithView(v => v.SetMagnifierEnabled(Actions.ViewerMagnifier.IsChecked));
        Actions.ViewerCopyImage.AsyncHandler = CopyToImageAsync;
        Actions.ViewerJumpToCursor.Handler = () =>
        {
            Widget();
            Activate();
            ShowCurrentLinks(true, 10000);
        };
        Actions.ViewerSyncCursor.Handler = () =>
        {
            _settings?.SetBool(
                ManuscriptViewerActions.SyncCursorSettingKey, Actions.ViewerSyncCursor.IsChecked);
            ShowCurrentLinks(false);
        };
        Actions.ViewerShowToolbar.Handler = () => ShowToolbar(Actions.ViewerShowToolbar.IsChecked);
        Actions.ViewerHelp.Handler = () => ShowHelp?.Invoke();

        Actions.ViewerSyncCursor.IsChecked
            = _settings?.GetBool(ManuscriptViewerActions.SyncCursorSettingKey, false) ?? false;
        Actions.ViewerShowToolbar.IsChecked
            = _settings?.GetBool(ManuscriptViewerActions.ShowToolbarSettingKey, true) ?? true;
    }

    /// <summary>Asks for PDFs and opens what the user chose.</summary>
    /// <returns>The task.</returns>
    /// <remarks>Upstream's <c>openViewdocs</c>, with the caption its manuscript
    /// subclass supplies ("Open Manuscript(s)"); the picker itself is the
    /// window's, because only a window can put one on the screen.</remarks>
    public async Task OpenAsync()
    {
        Widget();
        Activate();

        Func<Task<IReadOnlyList<string>>> pick = PickManuscriptsAsync;
        if (pick == null) { return; }

        IReadOnlyList<string> chosen = await pick().ConfigureAwait(true);
        if (chosen == null || chosen.Count == 0) { return; }

        Manuscripts.Load(chosen);
    }

    private void WithView(Action<MusicViewControl> action)
    {
        Widget();
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
        UpdateViewState();
    }

    private void SetZoomFactor(double factor)
    {
        WithView(v =>
        {
            v.ViewMode = ViewMode.FixedScale;
            v.ZoomFactor = factor;
            WriteSettings();
        });
        UpdateViewState();
    }

    private void ShowToolbar(bool shown)
    {
        if (_toolbar != null)
        {
            _toolbar.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        _settings?.SetBool(ManuscriptViewerActions.ShowToolbarSettingKey, shown);
    }

    private void FillChooser()
    {
        if (_chooser == null) { return; }

        _writingChooser = true;
        _chooser.ItemsSource = Manuscripts.Entries.Select(NameOf).ToList();
        _chooser.IsEnabled = Manuscripts.Count > 0;

        //Never an index the list does not have: a ComboBox handed one has no
        //good answer, and the one it picks is not this panel's to guess.
        _chooser.SelectedIndex = Manuscripts.CurrentIndex >= 0
            && Manuscripts.CurrentIndex < Manuscripts.Count
                ? Manuscripts.CurrentIndex
                : -1;
        _writingChooser = false;

        bool many = Manuscripts.Count > 1;
        Actions.ViewerClose.IsEnabled = Manuscripts.Count > 0;
        Actions.ViewerCloseOther.IsEnabled = many;
        Actions.ViewerCloseAll.IsEnabled = Manuscripts.Count > 0;
        Actions.ViewerReload.IsEnabled = Manuscripts.Count > 0;
    }

    /// <summary>Answers what the chooser shows for one manuscript.</summary>
    /// <param name="entry">The manuscript.</param>
    /// <returns>Its file name.</returns>
    private static string NameOf(ManuscriptEntry entry) => entry.Name;

    private async Task ShowCurrentAsync()
    {
        if (_view == null) { return; }

        ManuscriptEntry entry = Manuscripts.Current;
        _links?.Detach();
        _links = null;
        _highlightRange = null;

        if (entry == null)
        {
            //Upstream shows an EMPTY view when nothing is open — no message,
            //no placeholder (viewers/__init__.py closeAllViewdocs → w.clear()).
            _view.Clear();
            _writingChooser = true;
            if (_chooser != null) { _chooser.SelectedIndex = -1; }

            _writingChooser = false;
            UpdateViewState();
            return;
        }

        _writingChooser = true;
        if (_chooser != null) { _chooser.SelectedIndex = Manuscripts.CurrentIndex; }

        _writingChooser = false;

        if (entry.Opened == null)
        {
            entry.Opened = await PdfManuscript.OpenAsync(entry.Path).ConfigureAwait(true);
        }

        if (entry.Opened == null)
        {
            //Upstream's FIRST missing-file prompt: the file the list names is
            //not there, and the user chooses whether to forget it. Answering No
            //"will give you a chance to restore the file without having to
            //re-add it", which is why the entry survives with its flag cleared.
            entry.IsPresent = false;
            _view.Clear();
            await AskAboutMissingAsync(entry).ConfigureAwait(true);
            UpdateViewState();
            return;
        }

        entry.IsPresent = true;

        //Every page announces its own picture; one handler repaints the view.
        foreach (IPageImageSource source in entry.Opened.Pages)
        {
            source.ImageReady -= OnImageReady;
            source.ImageReady += OnImageReady;
        }

        _links = BuildLinks(entry.Opened);
        _view.SetDocument(entry.Opened.Document);
        UpdateViewState();
        UpdateSelectionActions();
    }

    private async Task AskAboutMissingAsync(ManuscriptEntry entry)
    {
        Func<string, Task<bool>> ask = AskDropMissingAsync;
        if (ask == null) { return; }

        if (await ask(entry.Path).ConfigureAwait(true))
        {
            Manuscripts.Remove(entry);
        }
    }

    private PointAndClickLinks BuildLinks(PdfManuscript manuscript)
    {
        //The Music View's own binding, over the raster pages' links instead of
        //the SVG scene graph's `<a>' bounds. A manuscript that is not an
        //engraved score simply contributes nothing here, and every behaviour
        //below then does nothing — which is what upstream's does too.
        if (_documents == null || !manuscript.HasLinks) { return null; }

        PointAndClickLinks links = new PointAndClickLinks();
        foreach (ScorePage page in manuscript.Document.Pages)
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

    private void OnImageReady(object sender, EventArgs e) => _view?.Invalidate();

    private void UpdateSelectionActions()
    {
        Actions.ViewerCopyImage.ToolTip = _view?.RubberBand?.HasSelection == true
            ? I18n.Get("Copy the selected part of the music to a picture.")
            : I18n.Get("Copy the current page to a picture.");
    }

    private void UpdateViewState()
    {
        if (_view == null) { return; }

        Actions.ViewerFitWidth.IsChecked = _view.ViewMode == ViewMode.FitWidth;
        Actions.ViewerFitHeight.IsChecked = _view.ViewMode == ViewMode.FitHeight;
        Actions.ViewerFitBoth.IsChecked = _view.ViewMode == ViewMode.FitBoth;

        int pages = _view.PageCount;
        int number = pages == 0 ? 0 : _view.CurrentPageNumber;

        if (_pager != null)
        {
            _pager.Text = PagerDisplay.Format(number, pages);
            _pager.IsEnabled = pages > 0;
        }

        Actions.ViewerNextPage.IsEnabled = pages > 0 && number < pages;
        Actions.ViewerPreviousPage.IsEnabled = pages > 0 && number > 1;

        if (_zoomChooser != null)
        {
            _writingZoom = true;
            int index = ZoomLevels.IndexFor(_zoomEntries, _view.ViewMode, _view.ZoomFactor);
            if (index >= 0)
            {
                _zoomChooser.ItemsSource = _zoomEntries.Select(entry => entry.Caption).ToList();
                _zoomChooser.SelectedIndex = index;
            }
            else
            {
                //Upstream's combo is editable-but-read-only precisely so it can
                //DISPLAY a factor its list does not carry; this platform's box
                //cannot, so the off-list factor is shown as a transient row —
                //the same answer board wave W14 reached for the window bar.
                List<string> captions = _zoomEntries.Select(entry => entry.Caption).ToList();
                captions.Add(ZoomLevels.CaptionFor(_view.ZoomFactor));
                _zoomChooser.ItemsSource = captions;
                _zoomChooser.SelectedIndex = captions.Count - 1;
            }

            _writingZoom = false;
        }
    }

    private void ReadSettings()
    {
        if (_view == null) { return; }

        _view.PaperColor = SKColors.White;
        ShowToolbar(Actions.ViewerShowToolbar.IsChecked);
        if (_settings == null) { return; }

        string mode = _settings.GetString(SettingsPrefix + "viewmode", "fitwidth");
        _view.ViewMode = mode switch
        {
            "fitheight" => ViewMode.FitHeight,
            "fitboth" => ViewMode.FitBoth,
            "fixed" => ViewMode.FixedScale,
            _ => ViewMode.FitWidth,
        };
        _view.ZoomFactor = _settings.GetDouble(SettingsPrefix + "zoom", 1.0);
        _view.ContinuousMode = _settings.GetBool(SettingsPrefix + "continuous", true);
    }

    private void WriteSettings()
    {
        if (_settings == null || _view == null) { return; }

        _settings.SetString(SettingsPrefix + "viewmode", _view.ViewMode switch
        {
            ViewMode.FitHeight => "fitheight",
            ViewMode.FitBoth => "fitboth",
            ViewMode.FixedScale => "fixed",
            _ => "fitwidth",
        });
        _settings.SetDouble(SettingsPrefix + "zoom", _view.ZoomFactor);
        _settings.SetBool(SettingsPrefix + "continuous", _view.ContinuousMode);
    }

    /// <summary>Gets the page being shown, or null.</summary>
    /// <returns>The page.</returns>
    private ScorePage CurrentPage()
    {
        MusicDocument document = Manuscripts.Current?.Opened?.Document;
        if (document == null || document.Count == 0) { return null; }

        int number = _view?.CurrentPageNumber ?? 1;
        return document.Pages[Math.Clamp(number - 1, 0, document.Count - 1)];
    }

    /// <summary>Shows the Copy to Image dialog over the page or the selection.</summary>
    /// <returns>The task.</returns>
    /// <remarks>
    /// Upstream's <c>copyImage</c>, which hands the rubber-banded page and
    /// region to the very same <c>copy2image</c> dialog the Music View uses.
    /// The dialog here is the same one too: it renders through
    /// <c>ScorePage.Image</c>, the one drawing path every page kind has, so a
    /// RASTER page needed nothing added for this to work.
    /// ⚠ ONE QUALITY NOTE: a raster page is never rendered wider than
    /// <c>PdfManual.MaxRenderWidth</c> (2,048 px, about 250&#160;dpi on an A4
    /// sheet), so an excerpt asked for at 300&#160;dpi or more is scaled up from
    /// that rather than re-rasterised. The cap is deliberate — see the constant.
    /// </remarks>
    private async Task CopyToImageAsync()
    {
        ScorePage page = CurrentPage();
        if (page == null)
        {
            Report?.Invoke(I18n.Get("There is no music to copy."));
            return;
        }

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

        CopyToImageDialog dialog = new CopyToImageDialog(
            page, rect, Manuscripts.Current?.Path, _settings)
        {
            PickSavePathAsync = name => PickExportPathAsync == null
                ? Task.FromResult<string>(null)
                : PickExportPathAsync(name, I18n.Get("PNG Image"), ".png"),
        };

        string saved = await dialog.ShowAsync(_view?.XamlRoot);
        if (saved != null) { Report?.Invoke(I18n.Get("Saved") + ": " + saved); }
    }

    /// <summary>Tells the panel which editor view the caret is now in.</summary>
    /// <param name="view">The view, or null.</param>
    public void SetEditorView(EditorView view)
    {
        if (ReferenceEquals(view, _editorView)) { return; }

        if (_editorView != null) { _editorView.CursorPositionChanged -= OnCursorPositionChanged; }

        _editorView = view;
        if (_editorView != null) { _editorView.CursorPositionChanged += OnCursorPositionChanged; }

        _highlightRange = null;
    }

    /// <summary>Shows the objects the caret currently points at.</summary>
    /// <param name="scroll">Whether to scroll them into view.</param>
    /// <param name="milliseconds">How long to show them; null takes the default.</param>
    /// <remarks>Upstream's <c>showCurrentLinks</c>, which is what
    /// <c>viewer_jump_to_cursor</c> and <c>viewer_sync_cursor</c> both drive.
    /// A manuscript with no point-and-click links has nothing to show and this
    /// returns at the first line, which is the ordinary case.</remarks>
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
            _view.ClearAllHighlights();
            _highlightRange = null;
            return;
        }

        if (!scroll && _highlightRange == range) { return; }

        _highlightRange = range;

        Dictionary<ScorePage, List<SKRect>> areas = new Dictionary<ScorePage, List<SKRect>>();
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

        Dictionary<ScorePage, IReadOnlyList<SKRect>> final
            = areas.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<SKRect>)pair.Value);
        if (scroll)
        {
            _view.EnsureVisible(_view.HighlightRect(final), new PageMargins(20));
        }

        int msec = milliseconds ?? (range.Value.Length > 1 ? 5000 : 2000);
        _view.Highlight(final, _highlight, msec);
    }

    private void OnCursorPositionChanged(object sender, EventArgs e)
        => ShowCurrentLinks(!_clickingLink && Actions.ViewerSyncCursor.IsChecked);

    private void OnLinkClicked(object sender, MusicLinkEventArgs e)
    {
        if (e.Properties is { IsRightButtonPressed: true }) { return; }

        //Upstream's slotLinkClicked, in the order it tests: a textedit link
        //moves the caret (or opens Edit in Place with Shift), and any OTHER
        //URL goes to the desktop.
        if (!TextEditLink.TryParse(e.Link.Url, out TextEditPlace place))
        {
            if (e.Link.IsExternal) { OpenExternalUrl?.Invoke(e.Link.Url); }

            return;
        }

        var target = _links?.Cursor(
            PathUtil.NormPath(place.FileName), place.Line, place.Column, true);
        if (target == null) { return; }

        if (IsShiftHeld?.Invoke() == true)
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

        Color color = view.State.Styler.Scheme.BaseColor("selectionbackground");
        view.Highlighter.Highlight(
            HighlightGroups.MusicHighlight,
            ranges,
            Color.FromArgb(128, color.R, color.G, color.B),
            HighlightGroups.PriorityOf(HighlightGroups.MusicHighlight));
    }

    private void OnLinkLeft(object sender, EventArgs e)
    {
        EditorView view = _editorView ?? CurrentEditorView?.Invoke();
        view?.Highlighter.Clear(HighlightGroups.MusicHighlight);
    }

    private void OnContextMenuRequested(object sender, MusicContextMenuEventArgs e)
    {
        (EditorDocument Document, int Offset)? source = null;
        if (e.Link != null && TextEditLink.TryParse(e.Link.Url, out TextEditPlace place))
        {
            source = _links?.Cursor(PathUtil.NormPath(place.FileName), place.Line, place.Column);
        }

        _contextMenu?.Show(e.Target, e.Position, e.Link, source);
    }
}
