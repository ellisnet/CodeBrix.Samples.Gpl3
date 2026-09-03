// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.Simple;
using Fresco.Brix.Commands;
using Fresco.Brix.Completion;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Engrave;
using Fresco.Brix.Import;
using Fresco.Brix.Ly.Pitching;
using Fresco.Brix.Midi;
using Fresco.Brix.MusicView;
using Fresco.Brix.ObjectEditor;
using Fresco.Brix.Preferences;
using Fresco.Brix.QuickInsert;
using Fresco.Brix.ScoreWizard;
using Fresco.Brix.Search;
using Fresco.Brix.Services;
using Fresco.Brix.Sessions;
using Fresco.Brix.Shell;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using Fresco.Brix.ViewModels;
using Fresco.Brix.Widgets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System; //Required: the IAsyncOperation GetAwaiter extension (awaiting the pickers) lives here
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Fresco.Brix.Views; //was previously: frescobaldi/mainwindow.py and app.py

public sealed partial class MainPage : Page, IWindowBridge, IRemoteCommandTarget
{
    private DockShell _shell;
    private ViewManager _viewManager;
    private DocumentTabBar _tabBar;
    private ShortcutRegistrar _shortcuts;
    private MainToolbar _toolbar;
    private EditorContextMenu _editorContextMenu;
    private SideBarManager _sideBar;
    private LogPanel _logPanel;
    private MusicViewPanel _musicViewPanel;
    private LayoutControlPanel _layoutControlPanel;
    private CustomEngraveDialog _customEngraveDialog;
    private DocumentListPanel _documentListPanel;
    private OutlinePanel _outlinePanel;
    private CharacterMapPanel _charMapPanel;
    private SnippetPanel _snippetPanel;
    private QuickInsertPanel _quickInsertPanel;
    private MidiPanel _midiPanel;
    private DocumentationPanel _docPanel;
    private ManuscriptViewerPanel _manuscriptPanel;
    private SearchBar _searchBar;
    private Completer _completer;
    private PreferencesDialog _preferences;
    private ObjectEditorPanel _objectEditorPanel;
    private ChangedDocumentsDialog _changedDocuments;
    private bool _showingChangedDocuments;

    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);

            if (DataContext is MainViewModel viewModel && _shell == null)
            {
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.StatusText))
                    {
                        UpdateStatus();
                    }
                    else if (e.PropertyName == nameof(MainViewModel.WindowTitle)
                        && App.Shell != null)
                    {
                        App.Shell.Title = viewModel.WindowTitle;
                    }
                };
                BuildShell(viewModel);
            }
        };

        this.InitializeComponent(); //Leave this line last
    }

    private MainViewModel ViewModel => DataContext as MainViewModel;

    #region | IWindowBridge |

    public Func<Task<string>> PickOpenPathAsync { get; set; }

    public Func<string, Task<string>> PickSavePathAsync { get; set; }

    /// <inheritdoc/>
    public Func<string, string, string, Task<string>> PickExportPathAsync { get; set; }

    /// <inheritdoc/>
    public Func<IReadOnlyList<string>, bool, Task<IReadOnlyList<string>>>
        PickImportPathsAsync { get; set; }

    /// <inheritdoc/>
    public Func<ImportFormat, Task<ImportSettings>> ConfigureImportAsync { get; set; }

    public Func<EditorView> ActiveView { get; set; }

    public Action<bool> SetFullScreen { get; set; }

    public Action Quit { get; set; }

    public Func<string, string, Task<bool>> ConfirmAsync { get; set; }

    /// <inheritdoc/>
    public Func<string, string, Task> AlertAsync { get; set; }

    /// <inheritdoc/>
    public Func<string, string, Task<CloseAnswer>> AskSaveDiscardAsync { get; set; }

    #endregion

    /// <summary>
    /// Builds the window: the editor area, the tool panels around it, the
    /// document tabs and the menu bar, all wired to the view model's commands.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    private void BuildShell(MainViewModel viewModel)
    {
        PickOpenPathAsync = PickOpenAsync;
        PickSavePathAsync = PickSaveAsync;
        PickExportPathAsync = PickExportAsync;
        PickImportPathsAsync = PickImportAsync;
        ConfigureImportAsync
            = format => ImportDialog.ShowAsync(XamlRoot, format, viewModel.Settings);
        ActiveView = () => _viewManager?.ActiveView;
        SetFullScreen = _ => { }; //The heads' fullscreen switch lands with W12's polish.
        Quit = () =>
        {
            //Upstream's closeEvent writes the window settings out before it
            //lets the last window go (mainwindow.py:343-345).
            SaveWindowLayout();

            //Upstream connects app.aboutToQuit to server.close, so the socket
            //goes with the process that made it rather than being left behind
            //for the next launch to find and have to clear away.
            RemoteInstance.Quit();
            Microsoft.UI.Xaml.Application.Current.Exit();
        };

        //...and the desktop's own close button is the same door: upstream
        //reaches writeSettings through closeEvent whichever way the window is
        //asked to go. Saving twice is harmless — it writes the same thing.
        if (App.Shell != null)
        {
            App.Shell.Closed += (_, _) => SaveWindowLayout();
        }
        ConfirmAsync = AskAsync;
        AlertAsync = (title, message)
            => InputDialogs.AlertAsync(XamlRoot, title, message);
        AskSaveDiscardAsync = AskSaveDiscardCancelAsync;
        viewModel.Window = this;

        //Every pane gets the musical position of its caret on its status bar,
        //which is upstream's viewSpaceCreated hook. Subscribed BEFORE the
        //editor area is built, because it creates its first pane in its own
        //constructor.
        //Subscribed BEFORE the editor area is built, because it creates its
        //first pane in its own constructor.
        ViewManager.ViewSpaceCreated += (_, e) =>
        {
            _ = new MusicPosition(e.Space);

            //Every editor gives the commands first refusal on its keystrokes,
            //because an accelerator on the window only ever sees what the
            //editor did not take — and the editor takes Return, Insert and
            //Delete whatever is held down with them.
            e.Space.ViewChanged += (_, _) => UpdateSelectionActions();
            e.Space.ViewCreated += (_, created) =>
            {
                _shortcuts?.Attach(created.View.Editor.TextArea);

                //And the commands that need a selection follow it, so one of
                //them is enabled the moment there IS a selection.
                created.View.SelectionChanged += (_, _) => UpdateSelectionActions();

                //Upstream's contextmenu.py, which view.py opens on a
                //contextMenuEvent. The editor takes the pointer event itself,
                //so the handler is added with handledEventsToo (board trap 26's
                //neighbour — the same reason the log's click handler is).
                EditorView withMenu = created.View;
                withMenu.Editor.TextArea.AddHandler(
                    UIElement.RightTappedEvent,
                    new Microsoft.UI.Xaml.Input.RightTappedEventHandler((_, args) =>
                    {
                        if (_editorContextMenu == null) { return; }

                        _editorContextMenu.Show(
                            withMenu,
                            withMenu.Editor.TextArea,
                            args.GetPosition(withMenu.Editor.TextArea));
                        args.Handled = true;
                    }),
                    handledEventsToo: true);
            };
        };

        //The editor area. Every view of a document shares that document's
        //tokenization, which is what makes split views agree with each other.
        _viewManager = new ViewManager(
            document => DocumentEditorState.For(document, viewModel.Settings),
            viewModel.ViewActions,
            EditorFontFamily());
        _viewManager.ViewChanged += (_, _) => UpdateStatus();

        _sideBar = new SideBarManager(
            _viewManager, viewModel.SideBarActions, viewModel.Settings);

        _shell = new DockShell { Center = _viewManager };
        ShellHost.Content = _shell;

        viewModel.Panels = new PanelManager(_shell, viewModel.Settings);
        viewModel.ActionManager.Add(viewModel.Panels.Actions);

        //The panels are CONSTRUCTED in the order their wiring needs and
        //REGISTERED further down, in upstream's own Tools-submenu order — see
        //the AddPanel block below.
        _logPanel = new LogPanel(
            viewModel.Documents, viewModel.LogActions, viewModel.Settings)
        {
            ShowReference = ShowErrorReference,
        };
        _layoutControlPanel = new LayoutControlPanel(
            viewModel.EngraveActions, viewModel.Settings);
        //The Music View is upstream's first "viewers" panel and its own dock
        //area; it needs the window to be able to put the caret where a click in
        //the music points, and to know which editor view the caret is in.
        _musicViewPanel = new MusicViewPanel(
            viewModel.Documents,
            viewModel.MusicViewActions,
            new LilyPortTypefaceResolver(),
            viewModel.Settings)
        {
            //A click in the score is a JUMP, so it is remembered: upstream's
            //musicview/widget.py:139 sends the very same click through
            //`browseriface.get(mainwindow).setTextCursor(cursor,
            //findOpenView=True)' rather than moving the caret itself, which is
            //what puts an entry in the Back/Forward history. Browser.GoTo ends
            //in ShowMusicCursor through GoToPosition, so the caret still moves
            //exactly as it did.
            //was previously: ShowCursor = ShowMusicCursor,
            ShowCursor = (document, offset) => viewModel.Browser.GoTo(document, offset),
            CurrentEditorView = () => _viewManager?.ActiveView,
            OpenExternalUrl = OpenExternalFile,
            PickExportPathAsync = PickExportAsync,
            Report = message => viewModel.StatusText = message,
        };
        _viewManager.ViewChanged += (_, _) => _musicViewPanel.SetEditorView(_viewManager.ActiveView);
        _musicViewPanel.SetEditorView(_viewManager.ActiveView);

        _documentListPanel = new DocumentListPanel(
            viewModel.Documents, viewModel.Settings);
        _outlinePanel = new OutlinePanel(viewModel.Documents, viewModel.Settings)
        {
            GoTo = (document, offset) => viewModel.Browser.GoTo(document, offset),
            CaretPosition = () => _viewManager?.ActiveView?.Editor.CaretOffset ?? 0,
        };
        _quickInsertPanel = new QuickInsertPanel(viewModel.Settings)
        {
            Insert = InsertQuickItem,
            FocusEditor = () => _viewManager?.ActiveView?.FocusEditor(),
            QuickRemove = viewModel.DocumentActions.QuickRemove,
        };
        viewModel.ActionManager.Add(_quickInsertPanel.Shortcuts);

        _snippetPanel = new SnippetPanel(
            viewModel.SnippetLibrary,
            viewModel.SnippetShortcuts,
            viewModel.Settings,
            EditorFontFamily())
        {
            ApplySnippet = ApplySnippet,
            FocusEditor = () => _viewManager?.ActiveView?.FocusEditor(),
            PickImportPathAsync = () => PickOpenAsync(".xml"),
            PickExportPathAsync = name => PickSaveAsync(name),
        };
        viewModel.ActionManager.Add(_snippetPanel.Actions);

        _charMapPanel = new CharacterMapPanel(viewModel.Settings, EditorFontFamily())
        {
            InsertText = InsertAtCursor,
        };
        //Upstream's panelmanager loads the Object Editor LAST and only behind
        //its own test: "The Object editor is highly experimental and should be
        //commented out for stable releases." Its other half —
        //app.is_git_controlled() — has nothing to ask here (FR5.7 keeps
        //version control out of the application), so the preference is the
        //whole test, and it is read once, which is why upstream's own tool tip
        //says a restart is needed.
        if (viewModel.Settings?.GetBool(GeneralValues.ExperimentalFeaturesKey, false)
            ?? false)
        {
            _objectEditorPanel = new ObjectEditorPanel(viewModel.Settings);
        }

        //The MIDI player. Upstream's own Tools submenu for it, and its own
        //place in that submenu's order.
        _midiPanel = new MidiPanel(
            viewModel.Documents,
            viewModel.MidiActions,
            viewModel.MidiPlayer,
            viewModel.Settings);
        //The manuals. Upstream docks its help browser on the right, hidden,
        //and so does this: opening it is what reads a manual off the disk.
        _docPanel = new DocumentationPanel(
            viewModel.Manuals, viewModel.DocumentationActions, viewModel.Settings)
        {
            OpenExternal = OpenExternalFile,
            WordAtCursor = WordAtCursor,
            ShowStatus = text => viewModel.StatusText = text,
        };
        //The Manuscript Viewer (board wave W15, ruling FR17). Upstream docks it
        //on the right, hidden, and gives it Meta+Alt+A; it shows PDF files the
        //USER chose, in the same paged view the Music View and the
        //Documentation Browser use.
        _manuscriptPanel = new ManuscriptViewerPanel(
            viewModel.ManuscriptViewerActions, viewModel.Documents, viewModel.Settings)
        {
            PickManuscriptsAsync = PickManuscriptsAsync,
            PickExportPathAsync = PickExportAsync,
            OpenExternalUrl = OpenExternalFile,
            ShowCursor = (document, offset) => viewModel.Browser.GoTo(document, offset),
            CurrentEditorView = () => _viewManager?.ActiveView,
            IsShiftHeld = MainToolbar.ShiftHeld,
            Report = message => viewModel.StatusText = message,
        };
        _manuscriptPanel.EditInPlace = (document, offset) => _ = EditInPlaceAsync(
            viewModel, document, offset);
        _manuscriptPanel.ShowHelp = () => _ = viewModel.UserGuide.ShowAsync(
            XamlRoot, ManuscriptViewerPanel.HelpPage);
        _manuscriptPanel.AskDropMissingAsync = AskDropMissingManuscriptAsync;
        _manuscriptPanel.ReportMissing = ReportMissingManuscripts;
        _viewManager.ViewChanged += (_, _)
            => _manuscriptPanel.SetEditorView(_viewManager.ActiveView);
        _manuscriptPanel.SetEditorView(_viewManager.ActiveView);

        //Upstream's panel order within each Tools submenu, kept so a user who
        //knows Frescobaldi finds them where they expect. Registration order is
        //what decides it (panelmanager.py loads them in this sequence), so the
        //panels are added HERE rather than where each is built.
        //was previously: the AddPanel calls sat beside each constructor, which
        //made Viewers read Log, Layout Control, Music View, Documentation and
        //Coding read Quick Insert, Snippets, Special Characters — in
        //contradiction of the comment above the block that said otherwise.
        //Upstream: Music View, [SVG View — W4 merged], Manuscript Viewer,
        //Documentation Browser, Log, Layout Control Options; then
        //Quick Insert, Special Characters, Snippets, [Object Editor];
        //then Documents, Outline; then MIDI Player.
        //was previously: "[Manuscript Viewer — post-v1]". Jeremy ruled it into
        //v1 on 2026-09-02 (ruling FR17), and panelmanager.py:72 loads it in the
        //"viewers" group between the SVG View and the help browser, which is
        //exactly where it goes here.
        viewModel.Panels.AddPanel(_musicViewPanel, "viewers");
        viewModel.Panels.AddPanel(_manuscriptPanel, "viewers");
        viewModel.Panels.AddPanel(_docPanel, "viewers");
        viewModel.Panels.AddPanel(_logPanel, "viewers");
        viewModel.Panels.AddPanel(_layoutControlPanel, "viewers");

        viewModel.Panels.AddPanel(_quickInsertPanel, "coding");
        viewModel.Panels.AddPanel(_charMapPanel, "coding");
        viewModel.Panels.AddPanel(_snippetPanel, "coding");
        if (_objectEditorPanel != null)
        {
            viewModel.Panels.AddPanel(_objectEditorPanel, "coding");
        }

        viewModel.Panels.AddPanel(_documentListPanel, "structure");
        viewModel.Panels.AddPanel(_outlinePanel, "structure");

        viewModel.Panels.AddPanel(_midiPanel, "midi");

        //Upstream's readSettings (mainwindow.py:391-401) — the window comes
        //back the size it was, with the tools that were open still open, in
        //their areas, in their tab order, at the divider positions the user
        //left. Done HERE: the panels exist and nothing has been able to move
        //them yet.
        RestoreWindowLayout(viewModel);

        //Music > Maximize. Upstream floats the Music View's dock widget and
        //shows it maximized (panel.Panel.maximize); this shell has no floating
        //dock widgets, so the panel takes the whole window instead and the same
        //command puts the layout back — see DockShell.MaximizePanel.
        viewModel.MusicViewActions.MusicMaximize.Handler = () =>
        {
            if (_shell.MaximizedPanel == _musicViewPanel)
            {
                _shell.RestoreFromMaximized();
                return;
            }

            _shell.MaximizePanel(_musicViewPanel);
        };

        //The Music View's context menu needs the window for its two dialogs:
        //Edit in Place puts one on screen, and Help opens the user guide.
        _musicViewPanel.EditInPlace = (document, offset) => _ = EditInPlaceAsync(
            viewModel, document, offset);
        _musicViewPanel.ShowHelp = () => _ = viewModel.UserGuide.ShowAsync(
            XamlRoot, "musicview");

        //Shift-clicking an object in the score opens Edit in Place on it —
        //upstream's musicview/widget.py:131-134, and what the guide page this
        //application ships (musicview_editinplace) tells the user to do. The
        //modifier is read from the keyboard source, not from the pointer event
        //(board trap 38), which is why the panel asks for it.
        _musicViewPanel.IsShiftHeld = MainToolbar.ShiftHeld;

        WireEditorTools(viewModel);
        WireMusicTools(viewModel);
        WireEngraving(viewModel);
        WireScoreWizard(viewModel);
        WireDocumentFonts(viewModel);

        _tabBar = new DocumentTabBar(viewModel.Documents)
        {
            ContextMenu = MakeDocumentContextMenu(viewModel),
            TabsClosable = viewModel.Settings?.GetBool(
                GeneralValues.TabsClosableKey, true) ?? true,
        };
        _tabBar.CloseRequested += async (_, e) => await viewModel.CloseAsync(e.Document);
        TabBarHost.Content = _tabBar;

        //The tabs choose the current document; the view manager shows it.
        viewModel.Documents.CurrentDocumentChanged += (_, e) =>
        {
            if (e.Document != null)
            {
                _viewManager.SetCurrentDocument(e.Document, findOpenView: true);
            }

            UpdateStatus();
        };
        viewModel.Documents.DocumentClosed
            += (_, e) => _viewManager.DocumentClosed(e.Document);

        //The commands' shortcuts belong to the WINDOW, not to the menu items:
        //a flyout item is not in the visual tree until its menu is opened.
        _shortcuts = new ShortcutRegistrar(this);
        _shortcuts.RegisterAll(viewModel.ActionManager);


        MenuBuilder.Build(
            MainMenuBar,
            viewModel.Actions,
            viewModel.ViewActions,
            viewModel.Panels,
            viewModel.Documents,
            viewModel.RecentFiles,
            path => _ = viewModel.OpenPathAsync(path),
            viewModel.SideBarActions,
            viewModel.EngraveActions,
            () => viewModel.Engraver.Document(),
            OpenGeneratedFile,
            viewModel.MusicViewActions,
            viewModel.DocumentActions,
            viewModel.BookmarkActions,
            viewModel.CompletionActions,
            viewModel.Browser.Actions,
            viewModel.SnippetLibrary,
            _snippetPanel.Actions,
            ApplySnippet,
            viewModel.SessionStore,
            viewModel.SessionManager.Actions,
            name => _ = viewModel.SessionManager.StartSessionAsync(name),
            viewModel.PitchActions,
            viewModel.RestActions,
            viewModel.RhythmActions,
            viewModel.LyricsActions,
            () => PitchTools.LanguageOf(_viewManager?.ActiveView?.Document),
            language => _ = ChangePitchLanguageAsync(viewModel, language),
            viewModel.ScoreWizardActions,
            viewModel.DocumentationActions,
            viewModel.EditorCommandActions,
            viewModel.FontsActions,
            viewModel.FileImportActions,
            viewModel.MatchingPairActions,
            viewModel.LogActions,
            () => viewModel.Engraver?.StickyDocument,
            () => _viewManager?.ActiveView?.HasSelection ?? false);

        //The two window toolbars (board wave W14, ruling FR16). They are built
        //AFTER the menus, because two of their pull-downs are the File menu's
        //own sub-menus and one is the recent-files menu, and because the
        //toolbar reads the same preference the menus were built from.
        _toolbar = new MainToolbar(
            viewModel.Actions,
            viewModel.Browser.Actions,
            viewModel.ScoreWizardActions,
            viewModel.EngraveActions,
            viewModel.MusicViewActions,
            viewModel.SnippetLibrary,
            _snippetPanel.Actions,
            ApplySnippet,
            viewModel.RecentFiles,
            path => _ = viewModel.OpenPathAsync(path),
            viewModel.Settings)
        {
            MusicView = _musicViewPanel,
        };
        ToolbarHost.Content = _toolbar;

        //Upstream reads QApplication.keyboardModifiers() inside engraveRunner;
        //the engraver is host-free, so the window hands it the read (trap 38).
        viewModel.Engraver.IsShiftHeld = MainToolbar.ShiftHeld;

        WireExternalChanges(viewModel);

        //Upstream installs exception.ExceptionDialog as the process's
        //sys.excepthook (frescobaldi/__main__.py); this is the same moment —
        //the window exists, so a failure has somewhere to be shown.
        InternalErrorDialog.Install(() => XamlRoot, OnUiThread);

        //FD5: start listening for a later launch, now that this window can act
        //on what it is told. Upstream does the same thing at the same moment,
        //with QTimer.singleShot(0, remote.setup).
        RemoteInstance.Setup(viewModel.Settings, this, OnUiThread);

        _ = StartWithSessionAsync(viewModel);
    }

    /// <summary>
    /// Connects the document watcher and the "Modified Files" window to the
    /// window they need.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <remarks>
    /// Upstream's <c>externalchanges</c> module does this at import time,
    /// because its window is a module-level singleton it can make on demand;
    /// here the window supplies the two things the service cannot have on its
    /// own — a thread to come back on, and something to show.
    /// </remarks>
    private void WireExternalChanges(MainViewModel viewModel)
    {
        viewModel.DocumentWatcher.ToUiThread = OnUiThread;
        viewModel.ExternalChanges.ToUiThread = OnUiThread;
        viewModel.ExternalChanges.Display
            = documents => _ = ShowChangedDocumentsAsync(documents);

        //Upstream's four connections: a document that has been dealt with
        //leaves the list, and the window hides when the list empties.
        void Remove(object sender, DocumentEventArgs e)
            => _changedDocuments?.RemoveDocument(e.Document);

        viewModel.Documents.DocumentClosed += Remove;
        viewModel.Documents.DocumentSaved += Remove;
        viewModel.Documents.DocumentUrlChanged += Remove;
        viewModel.Documents.DocumentLoaded += Remove;

        //Upstream's "initial setup" at the bottom of externalchanges/__init__.py.
        viewModel.ExternalChanges.Setup();
    }

    /// <summary>Shows the "Modified Files" window.</summary>
    /// <param name="documents">The documents that changed.</param>
    /// <returns>The running task.</returns>
    /// <remarks>Upstream's window is non-modal and simply re-filled when it is
    /// already up; a <c>ContentDialog</c> cannot be shown twice, so a second
    /// call while it is open re-fills it and returns.</remarks>
    private async Task ShowChangedDocumentsAsync(IReadOnlyList<EditorDocument> documents)
    {
        MainViewModel viewModel = ViewModel;
        if (viewModel == null) { return; }

        _changedDocuments ??= new ChangedDocumentsDialog(viewModel.ExternalChanges);
        _changedDocuments.SetDocuments(documents);
        if (_showingChangedDocuments) { return; }

        _showingChangedDocuments = true;
        try
        {
            await _changedDocuments.ShowAsync(XamlRoot);
        }
        finally
        {
            _showingChangedDocuments = false;
        }
    }

    #region | IRemoteCommandTarget — what a later launch can ask of this window |

    /// <inheritdoc/>
    public void OpenPath(string path, string encoding)
        => _ = ViewModel?.OpenPathAsync(path, encoding);

    /// <inheritdoc/>
    public void SetCurrent(string path)
    {
        //Upstream catches OSError from app.openUrl and does nothing, because
        //`open' has already been sent for the same file; the same "it is only
        //current if it is open" rule is said here by looking it up.
        EditorDocument document = ViewModel?.Documents.FindDocument(path);
        if (document != null) { ViewModel.Documents.CurrentDocument = document; }
    }

    /// <inheritdoc/>
    public void SetCursor(int line, int column)
        => _viewManager?.ActiveView?.GoTo(line, column);

    /// <inheritdoc/>
    public void ActivateWindow()
    {
        //Upstream's activateWindow() + raise_().
        App.Shell?.Activate();
        _viewManager?.ActiveView?.FocusEditor();
    }

    #endregion

    /// <summary>
    /// Connects the editor tools to the view they act on: the completer, the
    /// search bar, and every command whose work needs a caret.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    private void WireEditorTools(MainViewModel viewModel)
    {
        //Autocomplete follows whichever pane has the caret.
        _completer = new Completer
        {
            AutoComplete = viewModel.Settings?.GetBool(Completer.AutoCompleteKey, true)
                ?? true,
        };
        viewModel.CompletionActions.AutoComplete.IsChecked = _completer.AutoComplete;
        viewModel.CompletionActions.AutoComplete.Triggered += (_, _) =>
        {
            _completer.AutoComplete = viewModel.CompletionActions.AutoComplete.IsChecked;
            viewModel.Settings?.SetBool(
                Completer.AutoCompleteKey, _completer.AutoComplete);
        };
        viewModel.CompletionActions.PopupCompletions.Handler
            = () => _completer.ShowCompletionPopup();

        _searchBar = new SearchBar(EditorFontFamily());

        _viewManager.ViewChanged += (_, _) =>
        {
            _completer.SetView(_viewManager.ActiveView);
            UpdateBookmarkState(viewModel);
        };
        _completer.SetView(_viewManager.ActiveView);

        //Back and forward move the caret; only the window can do that.
        viewModel.Browser.CurrentPosition = () =>
        {
            EditorView view = _viewManager?.ActiveView;
            return view == null
                ? new BrowsePosition()
                : new BrowsePosition
                {
                    Document = view.Document,
                    Anchor = view.Editor.Document.CreateAnchor(view.Editor.CaretOffset),
                };
        };
        viewModel.Browser.GoToPosition = position =>
        {
            if (position?.Document == null) { return; }

            viewModel.Documents.CurrentDocument = position.Document;
            int offset = position.Anchor is { IsDeleted: false }
                ? position.Anchor.Offset
                : 0;
            ShowMusicCursor(position.Document, offset);
        };

        MainActions actions = viewModel.Actions;
        actions.EditPreferences.AsyncHandler = () => ShowPreferencesAsync(viewModel);
        actions.HelpAbout.AsyncHandler = () => AboutDialog.ShowAsync(
            XamlRoot, page => viewModel.UserGuide.RenderPage(page));

        //The user guide (board decision FD8). `help_manual' carries the system
        //help key already; this is what upstream's `userguide.show()' does, and
        //GuideHelp is the seam every dialog's Help button reaches it through
        //(upstream's module-level `userguide.show'/`addButton').
        actions.HelpManual.AsyncHandler
            = () => viewModel.UserGuide.ShowAsync(XamlRoot);
        Fresco.Brix.UserGuide.GuideHelp.Show
            = page => viewModel.UserGuide.ShowAsync(XamlRoot, page);
        viewModel.UserGuide.ReportError = message => viewModel.StatusText = message;
        actions.ViewGotoLine.AsyncHandler = () => GoToLineAsync();
        actions.EditFind.Handler = () => WithView(v => _searchBar.Find(v));
        actions.EditReplace.Handler = () => WithView(v => _searchBar.Replace(v));
        actions.EditFindNext.Handler = () => _searchBar.FindNext();
        actions.EditFindPrevious.Handler = () => _searchBar.FindPrevious();

        //The bar highlights every match, so it has to be told when the text or
        //the selection under it moves.
        viewModel.Documents.CurrentDocumentChanged += (_, _) => _searchBar.Invalidate();
        _viewManager.ViewChanged += (_, _) => _searchBar.Invalidate();

        DocumentActions document = viewModel.DocumentActions;
        document.ViewGotoFileOrDefinition.Handler = () => GoToFileOrDefinition(viewModel);
        document.EditCutAssign.AsyncHandler = () => CutAndAssignAsync(viewModel);
        document.EditMoveToIncludeFile.AsyncHandler
            = () => MoveToIncludeFileAsync(viewModel);
        document.ToolsIndentIndent.Handler = () => ReIndentSelection(viewModel);
        document.ViewHighlighting.Handler = () => ToggleHighlighting(viewModel);
        document.ToolsIndentAuto.Handler = () => ToggleAutoIndent(viewModel);

        foreach (var pair in document.QuickRemove)
        {
            string kind = pair.Key;
            pair.Value.Handler = () => RunQuickRemove(kind);
        }

        foreach (var pair in document.ForceDirections)
        {
            string direction = pair.Key;
            pair.Value.Handler = () => WithLyCursor(
                cursor => QuickRemove.ForceDirections(cursor, direction));
        }

        foreach (var name in DocumentActions.PendingActionNames)
        {
            AppAction pending = document.Action(name);
            if (pending != null) { pending.IsEnabled = false; }
        }

        _viewManager.ViewChanged += (_, _) => UpdateSelectionActions();
        UpdateSelectionActions();

        BookmarkActions marks = viewModel.BookmarkActions;
        marks.ViewBookmark.Handler = () => WithView(view =>
        {
            Bookmarks.For(view.Document).ToggleMark(
                view.Line - 1, Bookmarks.MarkType);
            RefreshMarkHighlights(view);
        });
        marks.ViewClearErrorMarks.Handler = () => WithView(view =>
        {
            Bookmarks.For(view.Document).Clear(Bookmarks.ErrorType);
            RefreshMarkHighlights(view);
        });
        marks.ViewClearAllMarks.Handler = () => WithView(view =>
        {
            Bookmarks.For(view.Document).Clear();
            RefreshMarkHighlights(view);
        });
        marks.ViewNextMark.Handler = () => StepMark(viewModel, forward: true);
        marks.ViewPreviousMark.Handler = () => StepMark(viewModel, forward: false);

        //View > Matching Pair / Select Matching Pair: the other half of
        //upstream's matcher.Matcher. The match itself is already computed for
        //the highlight; these two only move or select over the answer.
        MatchingPairActions pairs = viewModel.MatchingPairActions;
        pairs.ViewMatchingPair.Handler = () => GoToMatchingPair(select: false);
        pairs.ViewMatchingPairSelect.Handler = () => GoToMatchingPair(select: true);

        //The document list's context menu acts on whatever is selected there.
        _documentListPanel.ContextMenu = MakeDocumentContextMenu(viewModel);

        //The EDITOR's own right-click menu (upstream's contextmenu.py). Every
        //command it offers was already built; this is the route to them.
        _editorContextMenu = new EditorContextMenu(
            viewModel.Actions, viewModel.DocumentActions, _snippetPanel.Actions)
        {
            OpenFile = path => _ = OpenAndRaiseAsync(viewModel, path),
            DocumentOf = target => target.DocumentIn(viewModel.Documents),
            GoToDefinition = target =>
            {
                EditorDocument document = target.DocumentIn(viewModel.Documents);
                if (document == null && target.Filename != null)
                {
                    _ = viewModel.OpenPathAsync(target.Filename);
                    document = viewModel.Documents.FindDocument(target.Filename);
                }

                if (document != null)
                {
                    viewModel.Browser.GoTo(document, target.Position);
                }
            },
        };

        //Snippets that carry a shortcut apply to the pane with the caret.
        viewModel.SnippetShortcuts.Apply = ApplySnippet;

        //FD10's twenty-two native editor commands do the same.
        viewModel.EditorCommandActions.Apply = name => _ = RunEditorCommandAsync(name);
        _snippetPanel.DialogRoot = XamlRoot;
        _snippetPanel.ActionManager = viewModel.ActionManager;
        _snippetPanel.Actions.Activate.Handler = () => _snippetPanel.Activate();
        _snippetPanel.Actions.ManageTemplates.Handler
            = () => _snippetPanel.ManageTemplates();
        _snippetPanel.Actions.CopyToSnippet.AsyncHandler = () =>
        {
            EditorView view = _viewManager?.ActiveView;
            return view == null || !view.HasSelection
                ? Task.CompletedTask
                : _snippetPanel.AddAsync("-*- menu;\n" + view.SelectedText);
        };
        _snippetPanel.Actions.SaveAsTemplate.AsyncHandler
            = () => SaveAsTemplateAsync(viewModel);

        //Sessions need the window to close what is open and open what is not.
        viewModel.SessionManager.AskForNameAsync
            = name => SessionDialogs.EditAsync(XamlRoot, viewModel.SessionStore, name);
        viewModel.SessionManager.CloseAllAsync = async () =>
        {
            foreach (var open in viewModel.Documents.Documents.ToList())
            {
                if (!await viewModel.CloseAsync(open)) { return false; }
            }

            return true;
        };
        viewModel.SessionManager.OpenPathAsync = path => viewModel.OpenPathAsync(path);

        //The open manuscripts travel with the named session, as the user guide
        //promises (upstream's viewers/pdfwidget.py slotSaveSessionData /
        //slotSessionChanged).
        viewModel.SessionManager.CollectManuscripts
            = () => _manuscriptPanel.SessionData();
        viewModel.SessionManager.RestoreManuscripts
            = (paths, active) => _manuscriptPanel.RestoreSession(paths, active);
        viewModel.SessionManager.Actions.SessionManage.AsyncHandler
            = () => SessionDialogs.ManageAsync(
                XamlRoot,
                viewModel.SessionStore,
                () => PickOpenAsync(".json"),
                name => PickSaveAsync(name),
                name => viewModel.SessionManager.StartSessionAsync(name));
    }

    /// <summary>
    /// Connects the commands that change the music itself: the pitch tools,
    /// the rhythm tools, the rests, the lyrics and the two reformatters.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <remarks>
    /// Every one of these works over the SELECTION when there is one and over
    /// the whole document when there is not — except the rhythm commands,
    /// which upstream turns off entirely without a selection, because a
    /// rhythm applied to a whole file is nobody's intention.
    /// </remarks>
    private void WireMusicTools(MainViewModel viewModel)
    {
        PitchActions pitch = viewModel.PitchActions;
        RestActions rest = viewModel.RestActions;
        RhythmActions rhythm = viewModel.RhythmActions;
        LyricsActions lyrics = viewModel.LyricsActions;
        DocumentActions document = viewModel.DocumentActions;
        SettingsStore settings = viewModel.Settings;

        //The two pitch preferences are remembered between runs, which is what
        //upstream's readSettings/writeSettings pair does.
        pitch.PitchRelativeAssumeFirstPitchAbsolute.IsChecked
            = settings?.GetBool(PitchTools.FirstPitchAbsoluteKey, false) ?? false;
        pitch.PitchRelativeWriteStartPitch.IsChecked
            = settings?.GetBool(PitchTools.WriteStartPitchKey, true) ?? true;
        pitch.PitchRelativeAssumeFirstPitchAbsolute.Triggered += (_, _)
            => settings?.SetBool(
                PitchTools.FirstPitchAbsoluteKey,
                pitch.PitchRelativeAssumeFirstPitchAbsolute.IsChecked);
        pitch.PitchRelativeWriteStartPitch.Triggered += (_, _)
            => settings?.SetBool(
                PitchTools.WriteStartPitchKey,
                pitch.PitchRelativeWriteStartPitch.IsChecked);

        pitch.PitchRel2Abs.Handler = () => WithDocumentRange(
            (doc, start, end) => PitchTools.RelativeToAbsolute(
                doc, start, end, FirstPitchAbsolute(viewModel, doc)));
        pitch.PitchAbs2Rel.Handler = () => WithDocumentRange(
            (doc, start, end) => PitchTools.AbsoluteToRelative(
                doc,
                start,
                end,
                pitch.PitchRelativeWriteStartPitch.IsChecked,
                FirstPitchAbsolute(viewModel, doc)));
        pitch.PitchTranspose.AsyncHandler = () => TransposeAsync(viewModel);
        pitch.PitchModalTranspose.AsyncHandler
            = () => ModalTransposeAsync(viewModel);
        pitch.PitchModeShift.AsyncHandler = () => ModeShiftAsync(viewModel);
        pitch.PitchSimplify.AsyncHandler = () => ApplyTransposerAsync(
            viewModel, new Simplifier(), useFirstPitchAbsolute: true);

        rest.RestFmRestToSpacer.Handler
            = () => WithToolCursor(RestTools.FullMeasureRestToSpacer);
        rest.RestSpacerToFmRest.Handler
            = () => WithToolCursor(RestTools.SpacerToFullMeasureRest);
        rest.RestCommToRest.Handler
            = () => WithToolCursor(RestTools.PositionedRestToRest);

        foreach (var pair in rhythm.Operations)
        {
            string operation = pair.Key;
            if (operation == "apply")
            {
                pair.Value.AsyncHandler = () => ApplyRhythmAsync(viewModel);
                continue;
            }

            pair.Value.Handler = () => RunRhythm(operation);
        }

        lyrics.LyricsHyphenate.AsyncHandler = () => HyphenateAsync(viewModel);
        lyrics.LyricsDehyphenate.Handler = () => Dehyphenate();
        lyrics.LyricsCopyDehyphenated.Handler = () => CopyDehyphenated();

        document.ToolsReformat.Handler = () => WithDocumentRange(
            (doc, start, end) => Reformatting.Reformat(doc, settings, start, end));
        document.ToolsRemoveTrailingWhitespace.Handler = () => WithDocumentRange(
            (doc, start, end)
                => Reformatting.RemoveTrailingWhitespace(doc, start, end));
        document.ToolsConvertLy.AsyncHandler = () => ConvertLyAsync(viewModel);

        UpdateSelectionActions();
    }

    /// <summary>
    /// Answers whether a start-pitch-less <c>\relative</c> begins at f.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <param name="document">The document being changed.</param>
    /// <returns>Whether it does.</returns>
    private static bool FirstPitchAbsolute(
        MainViewModel viewModel, EditorDocument document)
        => PitchTools.FirstPitchAbsolute(
            document,
            viewModel.PitchActions.PitchRelativeAssumeFirstPitchAbsolute.IsChecked);

    /// <summary>Asks for two pitches and transposes between them.</summary>
    private async Task TransposeAsync(MainViewModel viewModel)
    {
        EditorDocument document = _viewManager?.ActiveView?.Document;
        if (document == null) { return; }

        string language = PitchTools.LanguageOf(document);
        string text = await InputDialogs.GetTextAsync(
            XamlRoot,
            I18n.Get("Transpose"),
            I18n.Format(
                I18n.Get("Please enter two absolute pitches, separated by a space, "
                    + "using the pitch name language \"{language}\"."),
                ("language", language)),
            string.Empty,
            validate: entered => PitchTools.IsTransposeInput(entered, language),
            helpPage: "transpose");
        if (string.IsNullOrEmpty(text)) { return; }

        await ApplyTransposerAsync(
            viewModel,
            PitchTools.TransposerFor(text, language),
            useFirstPitchAbsolute: true);
    }

    /// <summary>Asks for a number of steps and a key, and transposes in it.</summary>
    private async Task ModalTransposeAsync(MainViewModel viewModel)
    {
        string text = await InputDialogs.GetTextAsync(
            XamlRoot,
            I18n.Get("Transpose"),
            I18n.Get("Please enter the number of steps to alter by, "
                + "followed by a key signature. (i.e. \"5 F\")"),
            string.Empty,
            validate: PitchTools.IsModalTransposeInput,
            helpPage: "modal_transpose");
        if (string.IsNullOrEmpty(text)) { return; }

        await ApplyTransposerAsync(
            viewModel,
            PitchTools.ModalTransposerFor(text),
            useFirstPitchAbsolute: false);
    }

    /// <summary>Asks for a key and a mode, and shifts the music into it.</summary>
    private async Task ModeShiftAsync(MainViewModel viewModel)
    {
        EditorDocument document = _viewManager?.ActiveView?.Document;
        if (document == null) { return; }

        string language = PitchTools.LanguageOf(document);
        ModeShiftChoice choice = await ModeShiftDialog.ShowAsync(
            XamlRoot, viewModel.Settings, language);
        if (choice == null) { return; }

        await ApplyTransposerAsync(
            viewModel,
            PitchTools.ModeShifterFor(choice.Key, choice.Mode, language),
            useFirstPitchAbsolute: false);
    }

    /// <summary>
    /// Runs a transposer over the selection or the document, and says so when
    /// the result cannot be written in the document's pitch-name language.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <param name="transposer">The transposer, or null when the input was
    /// not usable.</param>
    /// <param name="useFirstPitchAbsolute">Whether the first-pitch preference
    /// applies; upstream passes it for Transpose and Simplify only.</param>
    private async Task ApplyTransposerAsync(
        MainViewModel viewModel,
        TransposerBase transposer,
        bool useFirstPitchAbsolute)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null || transposer == null) { return; }

        string failedLanguage = PitchTools.Transpose(
            view.Document,
            transposer,
            view.SelectionStart,
            view.SelectionEnd,
            useFirstPitchAbsolute && FirstPitchAbsolute(viewModel, view.Document));
        if (failedLanguage == null) { return; }

        await AskAsync(
            I18n.Get("Transpose"),
            I18n.Format(
                I18n.Get("Can't perform the requested transposition.\n\n"
                    + "The transposed music would contain quarter-tone alterations "
                    + "that are not available in the pitch language \"{language}\"."),
                ("language", failedLanguage)));
    }

    /// <summary>Rewrites the pitch names of the document in a language.</summary>
    private async Task ChangePitchLanguageAsync(
        MainViewModel viewModel, string language)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null) { return; }

        LanguageChange result = PitchTools.ChangeLanguage(
            view.Document, language, view.SelectionStart, view.SelectionEnd);

        if (result == LanguageChange.NotAvailable)
        {
            await AskAsync(
                I18n.Get("Pitch Name Language"),
                I18n.Format(
                    I18n.Get("Can't perform the requested translation.\n\n"
                        + "The music contains quarter-tone alterations, but "
                        + "those are not available in the pitch language \"{name}\"."),
                    ("name", language)));
            return;
        }

        if (result != LanguageChange.CommandNeeded) { return; }

        //was previously: "(for LilyPond below 2.14), or" / "(for LilyPond 2.14
        //and higher.)". FR13: a message box is chrome, and no chrome names
        //LilyPond — and what the sentence is really about is the version the
        //DOCUMENT declares. W-I18N: a Fresco.Brix-original msgid.
        await AskAsync(
            I18n.Get("Pitch Name Language"),
            I18n.Get("The pitch language of the selected text has been "
                + "updated, but you need to manually add the following "
                + "command to your document:")
                + "\n\n"
                + I18n.Format(
                    I18n.Get("\\include \"{language}.ly\"  "
                        + "(for documents below version 2.14), or"),
                    ("language", language))
                + "\n"
                + I18n.Format(
                    I18n.Get("\\language \"{language}\"  "
                        + "(for version 2.14 and higher.)"),
                    ("language", language)));
    }

    /// <summary>Asks for a rhythm and writes it over the selection.</summary>
    private async Task ApplyRhythmAsync(MainViewModel viewModel)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null || !view.HasSelection) { return; }

        string text = await InputDialogs.GetTextAsync(
            XamlRoot,
            I18n.Get("Apply Rhythm"),
            I18n.Get("Enter a rhythm:"),
            string.Empty,
            pattern: RhythmTools.ApplyPattern,
            completions: RhythmTools.TypedRhythms,
            helpPage: "rhythm");
        if (string.IsNullOrEmpty(text)) { return; }

        WithToolCursor(cursor => RhythmTools.Apply(cursor, text));
    }

    private void RunRhythm(string operation) => WithToolCursor(cursor =>
    {
        switch (operation)
        {
            case "double": RhythmTools.Double(cursor); break;
            case "halve": RhythmTools.Halve(cursor); break;
            case "dot": RhythmTools.Dot(cursor); break;
            case "undot": RhythmTools.Undot(cursor); break;
            case "remove_scaling": RhythmTools.RemoveScaling(cursor); break;
            case "remove_fraction_scaling":
                RhythmTools.RemoveFractionScaling(cursor);
                break;
            case "remove": RhythmTools.Remove(cursor); break;
            case "implicit": RhythmTools.Implicit(cursor); break;
            case "implicit_per_line": RhythmTools.ImplicitPerLine(cursor); break;
            case "explicit": RhythmTools.Explicit(cursor); break;
            case "copy": RhythmTools.Copy(cursor); break;
            case "paste": RhythmTools.Paste(cursor); break;
        }
    });

    /// <summary>Hyphenates the lyric words of the selection or document.</summary>
    private async Task HyphenateAsync(MainViewModel viewModel)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null) { return; }

        IReadOnlyList<LyricWord> words = LyricsTools.FindWords(
            view.Document, view.SelectionStart, view.SelectionEnd);
        if (words.Count == 0) { return; }

        //The dialog is only opened when there is something to hyphenate,
        //which is upstream's order too.
        Hyphenator hyphenator = await HyphenDialog.ChooseAsync(
            XamlRoot, viewModel.Settings);
        if (hyphenator == null) { return; }

        LyricsTools.Hyphenate(view.Document, words, hyphenator);
    }

    /// <summary>
    /// Brings an old document up to the syntax this engine reads, after showing
    /// the user what will change.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <returns>The running task.</returns>
    /// <remarks>
    /// Upstream selects the whole document and replaces it through
    /// <c>cursordiff.insert_text</c>, which keeps the cursor where it was; the
    /// same thing here is one <c>Replace</c> over the whole range, which is also
    /// ONE undo step — so a user who does not like the result presses Ctrl+Z once.
    /// </remarks>
    private async Task ConvertLyAsync(MainViewModel viewModel)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view?.Document == null) { return; }

        string text = view.Document.Text;
        ConvertLyOutcome outcome = await ConvertLyDialog.ShowAsync(
            XamlRoot,
            text,
            viewModel.Settings,
            name => PickSaveAsync(name),
            view.Document.Path);
        if (outcome == null) { return; }

        string converted = outcome.Text;
        if (outcome.CopyMessages && outcome.Messages.Count > 0)
        {
            //Upstream appends the messages as a block comment at the end, so the
            //user still has them after the dialog is gone.
            converted += "\n\n%{\n"
                + string.Join("\n", outcome.Messages).Trim('\n')
                + "\n%}\n";
        }

        if (string.Equals(converted, text, StringComparison.Ordinal)) { return; }

        view.Editor.Document.Replace(0, view.Editor.Document.TextLength, converted);
    }

    /// <summary>Takes the hyphenation out of the selected lyrics.</summary>
    private void Dehyphenate() => WithView(view =>
    {
        if (!view.HasSelection) { return; }

        string text = view.SelectedText;
        if (!LyricsTools.HasHyphens(text)) { return; }

        //Upstream keeps the selection over the replacement, so the user can
        //carry straight on to Copy.
        int start = view.SelectionStart;
        string replaced = LyricsTools.RemoveHyphens(text);
        view.Editor.Document.Replace(start, text.Length, replaced);
        view.Select(start, replaced.Length);
    });

    /// <summary>Copies the selected lyrics with the hyphenation removed.</summary>
    private void CopyDehyphenated() => WithView(view =>
    {
        string text = LyricsTools.RemoveHyphens(view.SelectedText);
        if (string.IsNullOrEmpty(text)) { return; }

        DataPackage package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    });

    /// <summary>
    /// Runs work over the caret's document and its selection, or over the
    /// whole document when there is no selection.
    /// </summary>
    /// <param name="work">The work: the document, and the range.</param>
    private void WithDocumentRange(Action<EditorDocument, int, int> work)
        => WithView(view => work(
            view.Document, view.SelectionStart, view.SelectionEnd));

    /// <summary>
    /// Runs work over an ly cursor covering the selection, or the whole
    /// document when there is none.
    /// </summary>
    /// <param name="work">The work.</param>
    /// <remarks>This is upstream's <c>lydocument.cursor(cursor,
    /// select_all=True)</c>, which is what every music tool but the rhythm
    /// ones is built on. <see cref="WithLyCursor"/> is the other kind: it
    /// hands over the selection exactly as it stands, which is what Quick
    /// Remove wants.</remarks>
    private void WithToolCursor(Action<Fresco.Brix.Ly.Cursor> work)
        => WithView(view =>
        {
            DocumentEditorState state = view.State;
            bool hasSelection = view.HasSelection;
            work(new Fresco.Brix.Ly.Cursor(
                state.LyDocument,
                hasSelection ? view.SelectionStart : 0,
                hasSelection ? view.SelectionEnd : view.Document.Text.Length));
        });

    /// <summary>Opens the session the settings ask for, then the files.</summary>
    private async Task StartWithSessionAsync(MainViewModel viewModel)
    {
        string session = viewModel.SessionStore.DefaultSessionName();
        if (session != null)
        {
            await viewModel.SessionManager.LoadSessionAsync(session);
        }

        //Files named on the command line open on top of the session, and an
        //empty window still gets its untitled document.
        if (viewModel.Documents.Documents.Count == 0
            || (App.CommandLinePaths?.Count ?? 0) > 0)
        {
            await viewModel.StartAsync(App.CommandLinePaths, App.CommandLine.Encoding);
        }

        //Upstream: "if urls and args.line is not None" — the place to go to is
        //applied to the LAST document loaded, once everything is open.
        if ((App.CommandLinePaths?.Count ?? 0) > 0 && App.CommandLine.Line != null)
        {
            SetCursor(App.CommandLine.Line.Value, App.CommandLine.Column ?? 1);
        }
    }

    /// <summary>
    /// Turns the commands that need a selection on and off.
    /// </summary>
    /// <remarks>Upstream's <c>selectionStateChanged</c>: without it, Cut and
    /// Assign stays disabled until the pane changes, however much the user
    /// has selected.</remarks>
    private void UpdateSelectionActions()
    {
        MainViewModel viewModel = ViewModel;
        if (viewModel == null || _snippetPanel == null) { return; }

        bool hasSelection = _viewManager?.ActiveView?.HasSelection ?? false;
        foreach (var name in DocumentActions.SelectionActionNames)
        {
            AppAction action = viewModel.DocumentActions.Action(name);
            if (action != null && !DocumentActions.PendingActionNames.Contains(name))
            {
                action.IsEnabled = hasSelection;
            }
        }

        //The rhythm commands are off without a selection — all of them, which
        //is how upstream's whole collection behaves — and two of the three
        //lyric commands are, because they work on selected TEXT rather than on
        //lyric tokens the tokenizer found.
        foreach (var name in RhythmActions.SelectionActionNames)
        {
            AppAction rhythmAction = viewModel.RhythmActions.Action(name);
            if (rhythmAction != null) { rhythmAction.IsEnabled = hasSelection; }
        }

        foreach (var name in LyricsActions.SelectionActionNames)
        {
            AppAction lyricAction = viewModel.LyricsActions.Action(name);
            if (lyricAction != null) { lyricAction.IsEnabled = hasSelection; }
        }

        //FD10's four commands that decline without a selection (upstream's
        //`selection: yes`): the menu entry greys out exactly as it does there.
        foreach (var name in EditorCommands.SelectionCommandNames)
        {
            AppAction command = viewModel.EditorCommandActions.Action(name);
            if (command != null) { command.IsEnabled = hasSelection; }
        }

        _snippetPanel.Actions.CopyToSnippet.IsEnabled = hasSelection;
    }

    private void WithView(Action<EditorView> work)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view != null) { work(view); }
    }

    /// <summary>
    /// Moves the caret to the token matching the one under it, selecting from
    /// here to there when asked.
    /// </summary>
    /// <param name="select">Whether to select the range rather than jump.</param>
    /// <remarks>
    /// Upstream's <c>Matcher.goto_match</c>. It answers nothing when the caret
    /// is not on a match token, and nothing when the partner could not be
    /// found — a list of fewer than two cursors, exactly as here.
    /// ⚠ One divergence, forced by the editor's API: upstream leaves the caret
    /// at the FAR end of a backwards selection (the anchor is the token the
    /// caret was on); <c>EditorView.Select</c> always leaves the caret after
    /// the range. The range selected is the same either way.
    /// </remarks>
    private void GoToMatchingPair(bool select)
        => WithView(view =>
        {
            var matches = TokenMatcher.Matches(
                view.State.LyDocument, view.Editor.CaretOffset);
            if (matches.Count < 2) { return; }

            var here = matches[0];
            var there = matches[1];
            if (!select)
            {
                view.GoToOffset(there.Start);
                return;
            }

            int start = Math.Min(here.Start, there.Start);
            int end = Math.Max(here.Start + here.Length, there.Start + there.Length);
            view.Select(start, end - start);
        });

    private void WithLyCursor(Action<Fresco.Brix.Ly.Cursor> work)
        => WithView(view =>
        {
            DocumentEditorState state = view.State;
            work(new Fresco.Brix.Ly.Cursor(
                state.LyDocument, view.SelectionStart, view.SelectionEnd));
        });

    private void RunQuickRemove(string kind) => WithLyCursor(cursor =>
    {
        switch (kind)
        {
            case "comments": QuickRemove.Comments(cursor); break;
            case "articulations": QuickRemove.Articulations(cursor); break;
            case "ornaments": QuickRemove.Ornaments(cursor); break;
            case "instrument_scripts": QuickRemove.InstrumentScripts(cursor); break;
            case "slurs": QuickRemove.Slurs(cursor); break;
            case "beams": QuickRemove.Beams(cursor); break;
            case "ligatures": QuickRemove.Ligatures(cursor); break;
            case "dynamics": QuickRemove.Dynamics(cursor); break;
            case "fingerings": QuickRemove.Fingerings(cursor); break;
            case "markup": QuickRemove.Markup(cursor); break;
        }
    });

    private void ReIndentSelection(MainViewModel viewModel) => WithView(view =>
    {
        DocumentEditorState state = view.State;
        Fresco.Brix.Editor.Indenting.ReIndent(
            state.LyDocument,
            Fresco.Brix.Editor.Indenting.CreateIndenter(
                viewModel.Settings, view.Document.Text),
            view.SelectionStart,
            view.SelectionEnd);
    });

    private void ToggleHighlighting(MainViewModel viewModel) => WithView(view =>
    {
        bool on = viewModel.DocumentActions.ViewHighlighting.IsChecked;
        view.State.MetaInfo?.SetBool(DocumentActions.HighlightingName, on);
        view.State.Highlighter.Styler = on
            ? view.State.Styler
            : new Fresco.Brix.Editor.PlainTokenStyler();
    });

    private void ToggleAutoIndent(MainViewModel viewModel) => WithView(view =>
        view.State.MetaInfo?.SetBool(
            DocumentActions.AutoIndentName,
            viewModel.DocumentActions.ToolsIndentAuto.IsChecked));

    /// <summary>Opens a file and makes it the current document.</summary>
    /// <param name="viewModel">The window's state.</param>
    /// <param name="path">The file.</param>
    /// <returns>The task.</returns>
    /// <remarks>Upstream's context-menu Open entry does exactly this — open,
    /// then <c>browseriface.setCurrentDocument</c>, so the jump is one the
    /// Back command can undo.</remarks>
    private static async Task OpenAndRaiseAsync(MainViewModel viewModel, string path)
    {
        if (!await viewModel.OpenPathAsync(path)) { return; }

        EditorDocument opened = viewModel.Documents.FindDocument(path);
        if (opened != null) { viewModel.Browser.SetCurrentDocument(opened); }
    }

    private void GoToFileOrDefinition(MainViewModel viewModel) => WithView(view =>
    {
        //Upstream tries the file first and falls back to the definition, so
        //that an \include line leads to its file rather than to nothing.
        var names = OpenFileAtCursor.FilenamesAtCursor(
            view.Document, view.SelectionStart, view.SelectionEnd);
        if (names.Count > 0)
        {
            foreach (var path in names)
            {
                _ = viewModel.OpenPathAsync(path);
            }

            EditorDocument opened = viewModel.Documents.FindDocument(names[^1]);
            if (opened != null) { viewModel.Browser.SetCurrentDocument(opened); }

            return;
        }

        DefinitionTarget target = GotoDefinition.Find(
            view.Document, view.Editor.CaretOffset, view.SelectionEnd);
        if (target == null) { return; }

        EditorDocument document = target.DocumentIn(viewModel.Documents);
        if (document == null && target.Filename != null)
        {
            _ = viewModel.OpenPathAsync(target.Filename);
            document = viewModel.Documents.FindDocument(target.Filename);
        }

        if (document != null) { viewModel.Browser.GoTo(document, target.Position); }
    });

    /// <summary>
    /// Builds the right-click menu a document carries — on its tab and on its
    /// row in the Documents panel.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <returns>The menu.</returns>
    /// <remarks>Both places show the same menu upstream, so both are built the
    /// same way here — including the nested Documents submenu and the sticky
    /// pin, which need the document list and the engraver.</remarks>
    private static DocumentContextMenu MakeDocumentContextMenu(MainViewModel viewModel)
        => new DocumentContextMenu(
            document => viewModel.SaveAsync(document, saveAs: false),
            document => viewModel.SaveAsync(document, saveAs: true),
            viewModel.CloseAsync,
            viewModel.CloseOthersAsync)
        {
            Documents = viewModel.Documents,
            StickyDocument = () => viewModel.Engraver?.StickyDocument,
            ToggleSticky = document => viewModel.Engraver?.SetStickyDocument(
                viewModel.Engraver.StickyDocument == document ? null : document),
        };

    /// <summary>
    /// Music View context menu &gt; Edit in Place: the one line the click
    /// resolved to, edited without leaving the music.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <param name="document">The document the click pointed into.</param>
    /// <param name="offset">Where in it.</param>
    /// <returns>The task.</returns>
    /// <remarks>Upstream's <c>editinplace.edit(panel.widget(), cursor,
    /// position)</c>. The dialog itself was ported whole at W5 and had no
    /// caller until now; raising the document first is what upstream's own
    /// cursor-based call does implicitly, and it is what makes the edit land
    /// where the user can see it.</remarks>
    private async Task EditInPlaceAsync(
        MainViewModel viewModel, EditorDocument document, int offset)
    {
        if (document == null) { return; }

        bool changed = await EditInPlace.ShowAsync(
            XamlRoot, document, offset, viewModel.Settings, EditorFontFamily());
        if (changed) { viewModel.Documents.CurrentDocument = document; }
    }

    /// <summary>
    /// View &gt; Go to Line: asks for a line number and puts the caret on that
    /// line, past whatever it is indented by.
    /// </summary>
    /// <returns>The task.</returns>
    /// <remarks>Upstream's <c>MainWindow.gotoLine</c>. Its two behaviours are
    /// kept whole: the range offered is 1 to the document's line count, and the
    /// caret lands on the line's first non-blank character rather than in its
    /// indentation. Upstream shows the box as a popup at the caret; the
    /// platform's only modal is centred, and that is the one divergence.</remarks>
    private async Task GoToLineAsync()
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null) { return; }

        int lineCount = view.Editor.Document.LineCount;
        int current = view.Line;
        //Upstream's popup carries no title at all; the dialog's own caption is
        //the menu entry's, so no new msgid is invented for it.
        int? answer = await InputDialogs.GetIntegerAsync(
            XamlRoot,
            ActionCollectionManager.RemoveAccelerator(
                I18n.Get("&Go to Line...")).TrimEnd('.'),
            I18n.Format(
                I18n.Get("Go to Line Number (1-{num}):"),
                ("num", lineCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))),
            current,
            1,
            lineCount);
        if (answer == null || answer.Value == current) { return; }

        //Upstream steps past the indentation so the caret lands on the text
        //rather than inside the leading whitespace.
        var line = view.Editor.Document.GetLineByNumber(answer.Value);
        string text = view.Editor.Document.GetText(line.Offset, line.Length);
        int indent = 0;
        while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t'))
        {
            indent++;
        }

        view.GoTo(answer.Value, indent + 1);
        view.FocusEditor();
    }

    private async Task CutAndAssignAsync(MainViewModel viewModel)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null || !view.HasSelection) { return; }

        string name = await InputDialogs.GetTextAsync(
            XamlRoot,
            I18n.Get("Cut and Assign"),
            I18n.Get("Please enter the name for the variable to assign the "
                + "selected text to:"),
            string.Empty,
            pattern: "[A-Za-z]+");
        if (string.IsNullOrEmpty(name)) { return; }

        CutAssign.Assign(
            view.Document, name, view.SelectionStart, view.SelectionEnd);
    }

    private async Task MoveToIncludeFileAsync(MainViewModel viewModel)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null || !view.HasSelection) { return; }

        IncludeFileProposal proposal = CutAssign.ProposeIncludeFile(
            view.Document, view.SelectionStart, view.SelectionEnd);
        if (proposal == null) { return; }

        string path = await PickSaveAsync(Path.GetFileName(proposal.Path));
        if (string.IsNullOrEmpty(path)) { return; }

        try
        {
            File.WriteAllBytes(path, proposal.EncodedText());
        }
        catch (IOException error)
        {
            await AskAsync(
                I18n.Get("Error"),
                I18n.Format(
                    I18n.Get("Could not write to {filename}:\n{error}"),
                    ("filename", path), ("error", error.Message)));
            return;
        }

        string command = CutAssign.IncludeCommand(view.Document, path);
        view.Editor.Document.Replace(
            view.SelectionStart, view.SelectionEnd - view.SelectionStart, command);
    }

    private async Task SaveAsTemplateAsync(MainViewModel viewModel)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null) { return; }

        //was previously: a bare name prompt. Upstream's TemplateDialog also
        //carries the run-on-create checkbox and refuses to overwrite a template
        //silently; the mechanism for the checkbox
        //(SnippetTemplate.FromDocument's engraveOnUse, which writes the
        //`template-run;' marker) was already here and was never offered.
        CheckBox runOnCreate = new CheckBox
        {
            Content = I18n.Get(
                "Run LilyPond when creating a new document from this template"),
            //Upstream ticks it when the document would actually produce
            //something: LilyPond mode, complete, and with output.
            IsChecked = WouldEngrave(view.Document),
        };

        TextDialog dialog = new TextDialog(
            I18n.Get("Save as Template"),
            I18n.Get("Please enter a template name:"));
        dialog.SetValidateExpression(@"\w(.*\w)?");
        dialog.AddUnderBox(runOnCreate);

        if (!await dialog.ShowAsync(XamlRoot)) { return; }

        string title = dialog.Text;
        if (string.IsNullOrEmpty(title)) { return; }

        //Upstream matches an existing template by its TITLE and overwrites that
        //snippet, rather than adding a second one with the same title.
        string existing = null;
        foreach (var name in viewModel.SnippetLibrary.NamesByTitle())
        {
            if (!viewModel.SnippetLibrary.Get(name).Variables.ContainsKey("template"))
            {
                continue;
            }

            if (string.Equals(
                viewModel.SnippetLibrary.Title(name), title, StringComparison.Ordinal))
            {
                existing = name;
                break;
            }
        }

        if (existing != null
            && !await InputDialogs.ConfirmAsync(
                XamlRoot,
                I18n.Get("Overwrite Template?"),
                I18n.Format(
                    I18n.Get("A template named \"{name}\" already exists.\n\n"
                        + "Do you want to overwrite it?"),
                    ("name", title)),
                StandardButtons.Overwrite))
        {
            return;
        }

        //The caret (and the selection's anchor) become $CURSOR and $ANCHOR, so
        //a new document from the template opens with the caret where it is now.
        string text = SnippetTemplate.FromDocument(
            view.Document.Text,
            view.SelectionStart,
            view.SelectionEnd,
            runOnCreate.IsChecked == true);
        viewModel.SnippetLibrary.Save(existing, text, title);
    }

    /// <summary>
    /// Whether a new document made from this one would be worth engraving at
    /// once — what upstream ticks the template's run box from.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>Whether it would engrave to something.</returns>
    private static bool WouldEngrave(EditorDocument document)
    {
        if (document == null) { return false; }

        DocumentInfo info = DocumentInfo.For(document);
        return info.Mode() == "lilypond"
            && info.DocInfo().Complete()
            && info.Music().HasOutput();
    }

    private void ApplySnippet(string name)
    {
        MainViewModel viewModel = ViewModel;
        EditorView view = _viewManager?.ActiveView;
        if (viewModel == null || view == null) { return; }

        SnippetInsertion result = SnippetInserter.Insert(
            viewModel.SnippetLibrary,
            name,
            view.Document,
            view.SelectionStart,
            view.SelectionEnd);
        if (!result.Inserted) { return; }

        view.Select(
            Math.Min(result.SelectionStart, result.SelectionEnd),
            Math.Abs(result.SelectionEnd - result.SelectionStart));
        view.FocusEditor();
    }

    /// <summary>
    /// Runs one of FD10's twenty-two native editor commands over the pane the
    /// caret is in.
    /// </summary>
    /// <param name="name">Upstream's own command name.</param>
    /// <returns>The running task.</returns>
    /// <remarks>Only one of the twenty-two asks the user anything, and it is
    /// asked HERE rather than inside the command, so the command itself stays
    /// a plain function of the document and is testable without a window.</remarks>
    private async Task RunEditorCommandAsync(string name)
    {
        MainViewModel viewModel = ViewModel;
        EditorView view = _viewManager?.ActiveView;
        if (viewModel == null || view == null) { return; }

        (int Red, int Green, int Blue)? color = null;
        if (EditorCommands.ByName.TryGetValue(name, out EditorCommandInfo info)
            && info.NeedsColor)
        {
            Windows.UI.Color? chosen = await InputDialogs.GetColorAsync(
                XamlRoot, I18n.Get("Select Color"));
            if (chosen == null) { return; }

            color = (chosen.Value.R, chosen.Value.G, chosen.Value.B);
        }

        EditorCommandResult result = EditorCommands.Run(
            name,
            view.Document,
            view.SelectionStart,
            view.SelectionEnd,
            viewModel.Settings,
            color);
        if (!result.Applied) { view.FocusEditor(); return; }

        view.Select(
            Math.Min(result.SelectionStart, result.SelectionEnd),
            Math.Abs(result.SelectionEnd - result.SelectionStart));
        view.FocusEditor();
    }

    private void InsertAtCursor(string text)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null || string.IsNullOrEmpty(text)) { return; }

        view.Editor.Document.Replace(
            view.SelectionStart, view.SelectionEnd - view.SelectionStart, text);
        view.FocusEditor();
    }

    private void InsertQuickItem(string name)
    {
        EditorView view = _viewManager?.ActiveView;
        if (view == null) { return; }

        QuickInsertActions.Insert(
            view.Document,
            name,
            view.SelectionStart,
            view.SelectionEnd,
            _quickInsertPanel.Direction,
            _quickInsertPanel.AllowShorthands,
            ViewModel?.Settings);
    }

    private void StepMark(MainViewModel viewModel, bool forward)
        => WithView(view =>
        {
            Bookmarks marks = Bookmarks.For(view.Document);
            int line = forward
                ? marks.NextMark(view.Line - 1)
                : marks.PreviousMark(view.Line - 1);
            if (line < 0) { return; }

            viewModel.Browser.GoTo(
                view.Document,
                view.Editor.Document.GetLineByNumber(line + 1).Offset);
        });

    private void UpdateBookmarkState(MainViewModel viewModel)
        => WithView(view =>
        {
            viewModel.BookmarkActions.ViewBookmark.IsChecked
                = Bookmarks.For(view.Document).HasMark(
                    view.Line - 1, Bookmarks.MarkType);
            viewModel.DocumentActions.ToolsIndentAuto.IsChecked
                = view.State.MetaInfo?.GetBool(DocumentActions.AutoIndentName) ?? true;
            viewModel.DocumentActions.ViewHighlighting.IsChecked
                = view.State.MetaInfo?.GetBool(DocumentActions.HighlightingName) ?? true;
            RefreshMarkHighlights(view);
        });

    private static void RefreshMarkHighlights(EditorView view)
    {
        Bookmarks marks = Bookmarks.For(view.Document);
        view.Highlighter.Highlight(
            HighlightGroups.Mark,
            marks.MarkedLines(Bookmarks.MarkType)
                .Where(n => n < view.Editor.Document.LineCount)
                .Select(n =>
                {
                    var line = view.Editor.Document.GetLineByNumber(n + 1);
                    return (line.Offset, Math.Max(line.Length, 1));
                }),
            Windows.UI.Color.FromArgb(0x40, 0x40, 0x80, 0xff),
            HighlightGroups.PriorityOf(HighlightGroups.Mark),
            fullWidth: true);
    }

    /// <summary>
    /// Connects the engraving service to the things only the window can do:
    /// showing dialogs, marshalling the engine's announcements onto this
    /// thread, and putting the caret on an error.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <summary>Puts the Score Wizard behind its two commands.</summary>
    /// <param name="viewModel">The window's state.</param>
    private void WireScoreWizard(MainViewModel viewModel)
    {
        viewModel.ScoreWizardActions.ScoreWizard.AsyncHandler
            = () => ShowScoreWizardAsync(viewModel, fromCurrent: false);
        viewModel.ScoreWizardActions.ScoreWizardFromCurrent.AsyncHandler
            = () => ShowScoreWizardAsync(viewModel, fromCurrent: true);
    }

    /// <summary>Shows the Score Wizard and opens what it writes.</summary>
    /// <param name="viewModel">The window's state.</param>
    /// <param name="fromCurrent">Whether to read the current document first.</param>
    /// <returns>Nothing.</returns>
    private async Task ShowScoreWizardAsync(MainViewModel viewModel, bool fromCurrent)
    {
        ScoreWizardDialog wizard = viewModel.ScoreWizard;
        wizard.PreviewAction = text => ShowMusicPreviewAsync(
            viewModel, text, I18n.Get("Score Preview"));

        if (fromCurrent)
        {
            EditorView view = _viewManager?.ActiveView;
            if (view != null) { ScoreReader.Read(wizard.Model, view.Document.Text); }
        }

        string text = await wizard.ShowAsync(XamlRoot);
        if (string.IsNullOrEmpty(text)) { return; }

        //A wizard-written score is a NEW document, and it starts out unmodified
        //so that closing it straight away asks nothing.
        EditorDocument document = viewModel.Documents.CreateDocument();
        document.Document.Text = text;

        //Upstream hands the new document over unmodified: nothing has been
        //edited yet, and closing it straight away should ask nothing.
        document.IsModified = false;
        viewModel.Documents.CurrentDocument = document;
    }

    /// <summary>Connects Tools &gt; Document Fonts to its dialog.</summary>
    /// <param name="viewModel">The window's state.</param>
    private void WireDocumentFonts(MainViewModel viewModel)
        => viewModel.FontsActions.DocumentFonts.AsyncHandler
            = () => ShowDocumentFontsAsync(viewModel);

    /// <summary>
    /// Shows the Document Fonts dialog and inserts what it wrote at the caret.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <returns>Nothing.</returns>
    /// <remarks>Upstream's <c>Fonts.document_fonts</c>: the generated command
    /// is inserted at the current cursor position, with a trailing newline.
    /// //was previously: upstream chooses between this dialog and the OLD
    /// "Set Document Fonts" one on the document's declared LilyPond version.
    /// There is one engine here (FR5.1), so there is one dialog, and
    /// `oldfontsdialog.py' is unreachable and dropped.</remarks>
    private async Task ShowDocumentFontsAsync(MainViewModel viewModel)
    {
        DocumentFontsDialog dialog = viewModel.DocumentFonts;
        dialog.PickAsync = PickPathAsync;
        dialog.CopyToClipboard = text =>
        {
            DataPackage package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        };
        dialog.CurrentDocumentText = () => _viewManager?.ActiveView?.Document.Document.Text;
        dialog.CurrentDocumentDirectory = () =>
        {
            string path = _viewManager?.ActiveView?.Document.Path;
            return string.IsNullOrEmpty(path)
                ? null
                : System.IO.Path.GetDirectoryName(path);
        };

        string command = await dialog.ShowAsync(XamlRoot);
        if (string.IsNullOrEmpty(command)) { return; }

        EditorView view = _viewManager?.ActiveView;
        if (view == null) { return; }

        int start = Math.Min(view.SelectionStart, view.SelectionEnd);
        int length = Math.Abs(view.SelectionEnd - view.SelectionStart);
        view.Editor.Document.Replace(start, length, command);
        view.Select(start + command.Length, 0);
        view.FocusEditor();
    }

    /// <summary>Engraves a piece of source and shows it.</summary>
    /// <param name="viewModel">The window's state.</param>
    /// <param name="text">The source.</param>
    /// <param name="title">What to call the run.</param>
    /// <returns>Nothing.</returns>
    private Task ShowMusicPreviewAsync(
        MainViewModel viewModel, string text, string title)
        => new MusicPreviewDialog(viewModel.Engine, new LilyPortTypefaceResolver())
            .ShowAsync(XamlRoot, text, title);

    private void WireEngraving(MainViewModel viewModel)
    {
        viewModel.Engraver.LayoutControlOptions
            = () => _layoutControlPanel.PreviewOptions();
        viewModel.Engraver.CustomEngraveRequested += (_, _) => _ = ShowCustomEngraveAsync();
        viewModel.Engraver.EngineInfoRequested
            += (_, _) => _ = EngineInfoDialog.ShowAsync(viewModel.Engine, XamlRoot);

        //The engine announces its state from its own load thread.
        viewModel.Engine.StateChanged += (_, _) => OnUiThread(() =>
        {
            viewModel.Refresh(nameof(MainViewModel.EngineStatusText));
            viewModel.Refresh(nameof(MainViewModel.WindowTitle));
        });

        //A job's messages arrive on whichever thread the engine call returned
        //on; the log writes into a text document, so it has to be here.
        viewModel.StartAutoCompiler(OnUiThread);
    }

    private void OnUiThread(Action work)
    {
        if (DispatcherQueue == null || DispatcherQueue.HasThreadAccess)
        {
            work();
            return;
        }

        DispatcherQueue.TryEnqueue(() => work());
    }

    private async Task ShowCustomEngraveAsync()
    {
        MainViewModel viewModel = ViewModel;
        if (viewModel == null) { return; }

        _customEngraveDialog ??= new CustomEngraveDialog(viewModel.Settings);
        EditorDocument document = viewModel.Engraver.Document();
        LilyPondJob job = await _customEngraveDialog.ShowAsync(
            viewModel.Engine,
            document,
            _layoutControlPanel.PreviewOptions(),
            XamlRoot);

        if (job != null)
        {
            viewModel.Engraver.SaveDocumentIfDesired();
            viewModel.Engraver.RunJob(job, document);
        }
    }

    /// <summary>
    /// Puts the window back the way the last quit left it: its size, the tool
    /// panels that were open, where they were docked, which one was showing in
    /// each area, and the divider positions between them.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <remarks>Upstream's <c>readSettings</c> — <c>mainwindow.py:391-401</c>,
    /// which resizes and then calls <c>restoreState</c>. Nothing stored means
    /// nothing applied, so a first launch opens at the head's own size with no
    /// tool open.</remarks>
    private void RestoreWindowLayout(MainViewModel viewModel)
    {
        Services.SettingsStore settings = viewModel?.Settings;
        if (settings == null) { return; }

        (int width, int height) = DockLayout.LoadWindowSize(settings);
        if (width > 0 && height > 0)
        {
            App.Shell?.AppWindow?.Resize(
                new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        }

        _shell?.ApplyLayout(DockLayout.Load(settings));
    }

    /// <summary>Writes the window arrangement out for the next launch.</summary>
    /// <remarks>Upstream's <c>writeSettings</c> — <c>mainwindow.py:403-411</c>,
    /// reached from <c>closeEvent</c> for the last window.</remarks>
    private void SaveWindowLayout()
    {
        Services.SettingsStore settings = ViewModel?.Settings;
        if (settings == null || _shell == null) { return; }

        _shell.CaptureLayout().Save(settings);

        //The WINDOW's own bounds, not AppWindow.Size: on the X11 head the
        //latter answers the FRAMED size (measured 1220x850 for a 1200x800
        //window), so feeding it back to Resize — which sets the size the
        //window itself gets — would grow the window by the frame on every
        //launch. Bounds is what Resize is the inverse of.
        Windows.Foundation.Rect bounds = App.Shell?.Bounds ?? default;
        DockLayout.SaveWindowSize(
            settings, (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));
    }

    /// <summary>Puts the caret where an engine message pointed.</summary>
    /// <param name="reference">The reference.</param>
    /// <summary>Puts the caret where a click in the Music View points.</summary>
    /// <param name="document">The source document the link points into.</param>
    /// <param name="offset">Where in it.</param>
    /// <remarks>
    /// The offset comes from an anchor the editor has been moving along with
    /// the user's edits, so it is right even when the score is older than the
    /// text — which is the ordinary case while somebody is working.
    /// </remarks>
    private void ShowMusicCursor(EditorDocument document, int offset)
    {
        MainViewModel viewModel = ViewModel;
        if (document == null || viewModel == null) { return; }

        viewModel.Documents.CurrentDocument = document;

        //Upstream's SVG view emits `cursor' for the same click, and the object
        //editor's setObjectFromCursor is what listens to it. The Music View's
        //point-and-click is that signal here.
        _objectEditorPanel?.SetObjectFromCursor(document, offset);

        EditorView view = _viewManager?.ActiveView;
        if (view?.Document != document) { return; }

        var location = view.Editor.Document.GetLocation(
            Math.Clamp(offset, 0, view.Editor.Document.TextLength));
        view.GoTo(location.Line, location.Column);
        view.FocusEditor();
    }

    private void ShowErrorReference(ErrorReference reference)
    {
        MainViewModel viewModel = ViewModel;
        if (reference == null || viewModel == null) { return; }

        EditorDocument document = reference.Document;
        if (document == null)
        {
            //The file is not open yet — open it, which binds the reference.
            if (!File.Exists(reference.FileName)) { return; }

            _ = viewModel.OpenPathAsync(reference.FileName);
            document = viewModel.Documents.FindDocument(reference.FileName);
            if (document == null) { return; }

            reference.Bind(document);
        }

        viewModel.Documents.CurrentDocument = document;
        EditorView view = _viewManager?.ActiveView;
        if (view?.Document != document) { return; }

        //The anchor's offset is the truth once the document has been edited;
        //the reported line and column are only right for the text as engraved.
        int offset = reference.Offset ?? document.OffsetAtPosition(
            reference.Line, reference.Column);
        var location = view.Editor.Document.GetLocation(offset);
        view.GoTo(location.Line, location.Column);
        view.FocusEditor();
    }

    /// <summary>Opens a file an engrave run generated.</summary>
    /// <param name="path">The file.</param>
    /// <remarks>
    /// Text results open in the editor; everything else goes to the desktop's
    /// own viewer, which is upstream's own arrangement — its Generated Files
    /// menu calls <c>helpers.openUrl</c> for every entry, and the helper works
    /// out from the extension which application the user configured for it.
    /// The editor branch is the divergence, and a small one: a <c>.ly</c>
    /// result belongs in the editor that is already open.
    /// //was previously: everything but text went to the status line, waiting
    /// for this wave to bring the helper.
    /// </remarks>
    private void OpenGeneratedFile(string path)
    {
        MainViewModel viewModel = ViewModel;
        if (viewModel == null || string.IsNullOrEmpty(path)) { return; }

        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".ly" or ".ily" or ".lyi" or ".log" or ".txt")
        {
            _ = viewModel.OpenPathAsync(path);
            return;
        }

        OpenExternalFile(path);
    }

    /// <summary>Hands a file or URL to the desktop's own application for it.</summary>
    /// <param name="target">A path or a URL.</param>
    private void OpenExternalFile(string target)
    {
        MainViewModel viewModel = ViewModel;
        if (viewModel == null || string.IsNullOrEmpty(target)) { return; }

        viewModel.Helpers.ReportError = message => viewModel.StatusText = message;
        viewModel.StatusText = target;
        _ = Uri.TryCreate(target, UriKind.Absolute, out Uri url) && !url.IsFile
            ? viewModel.Helpers.OpenUrlAsync(url)
            : viewModel.Helpers.OpenPathAsync(target);
    }

    /// <summary>
    /// The word the caret is on, for contextual help.
    /// </summary>
    /// <returns>The token's text, or null.</returns>
    /// <remarks>
    /// Read from the document's OWN tokenisation rather than by splitting the
    /// line on spaces, so <c>\override</c> is one word and
    /// <c>Staff.NoteHead.color</c> is the tokens the lexer made of it.
    /// </remarks>
    private string WordAtCursor()
    {
        EditorView view = _viewManager?.ActiveView;
        EditorDocument document = view?.Document;
        if (document == null) { return null; }

        LyHighlighter highlighter = DocumentEditorState
            .For(document, ViewModel?.Settings)?.Highlighter;
        if (highlighter == null) { return null; }

        int offset = view.Editor.CaretOffset;
        var (_, middle, right) = TokenIter.Partition(
            highlighter, document.Document, offset);

        //The token the caret is INSIDE, or — when it sits on a boundary — the
        //one it is about to enter, which is what a reader means by "this word"
        //after typing it.
        Fresco.Brix.Ly.Slexing.Token token
            = middle ?? (right.Length > 0 ? right[0] : null);
        return token?.Text;
    }

    /// <summary>The editor's monospace font (FD4: Roboto Mono).</summary>
    /// <returns>The font family, or null when the resource is missing.</returns>
    private FontFamily EditorFontFamily()
        => Microsoft.UI.Xaml.Application.Current.Resources
            .TryGetValue("RobotoMonoFont", out var font)
            ? font as FontFamily
            : null;

    private void UpdateStatus()
    {
        if (ViewModel == null) { return; }

        //The caret position belongs to the pane's own status bar, which is
        //where upstream shows it; this line is the window's message line.
        StatusLine.Visibility = string.IsNullOrEmpty(ViewModel.StatusText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ViewModel.Refresh(nameof(MainViewModel.WindowTitle));

        //The window's title is not a bindable property of the page, so it is
        //pushed rather than bound. It carries the document name, the modified
        //star, and the engine's loading state while that lasts.
        if (App.Shell != null) { App.Shell.Title = ViewModel.WindowTitle; }
    }

    /// <summary>Asks the user which PDF files to open as manuscripts.</summary>
    /// <returns>The paths, or an empty list when the user cancelled.</returns>
    /// <remarks>
    /// Upstream's <c>openViewdocs</c>: <c>getOpenFileNames</c> — multi-select —
    /// filtered to <c>"{} (*.pdf)".format(_("PDF Files"))</c>, captioned with
    /// the manuscript viewer's own "Open Manuscript(s)". Its <c>directory</c>
    /// argument (the current manuscript's folder, then the current document's,
    /// then the application's base directory) has no counterpart: the platform
    /// picker takes a <c>PickerLocationId</c>, not a path, and remembers where
    /// the user last was itself.
    /// </remarks>
    private async Task<IReadOnlyList<string>> PickManuscriptsAsync()
    {
        FileOpenPicker picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".pdf");
        picker.FileTypeFilter.Add("*");

        var files = await picker.PickMultipleFilesAsync();
        return files == null
            ? Array.Empty<string>()
            : files.Select(file => file.Path).ToArray();
    }

    /// <summary>
    /// Asks whether to drop a manuscript whose file is no longer there.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>Whether the user wants the name taken off the list.</returns>
    /// <remarks>
    /// Upstream's FIRST missing-file prompt (<c>viewers/pdfwidget.py:164-177</c>):
    /// a Yes/No dialog titled "Missing file", carrying the question and a tool
    /// tip explaining what No does.
    /// ⚠ THE BUTTONS ARE NOT "Yes" AND "No". Qt fills a standard button's
    /// caption in from ITS OWN catalogs, which this application does not ship,
    /// and <c>Services/StandardButtons</c> already records that decision for
    /// the template-overwrite question: only the ten <c>QPlatformTheme</c>
    /// strings Frescobaldi lists are translated here, and "Yes" is not among
    /// them. The buttons therefore say what they DO, in strings that are
    /// translated — Qt's own "Remove" and "Cancel".
    /// </remarks>
    private async Task<bool> AskDropMissingManuscriptAsync(string path)
    {
        TextBlock message = new TextBlock
        {
            Text = I18n.Format(
                I18n.Get("The file {filename} is missing.\n\n"
                    + "Do you want to remove the filename from the list?"),
                ("filename", path)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
        };

        //Upstream's own tool tip on the dialog, kept verbatim.
        ToolTipService.SetToolTip(message, I18n.Get(
            "Answering 'No' will give you a chance to restore the "
            + "file without having to re-add it."));

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("Missing file"),
            Content = message,
            PrimaryButtonText = I18n.Get("QFileDialog", "Remove"),
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Reports the manuscripts a restored session could not find.</summary>
    /// <param name="paths">The files.</param>
    /// <remarks>Upstream's SECOND missing-file prompt
    /// (<c>viewers/__init__.py:263-272</c>): a warning titled "Missing files in
    /// {name}", where the name is the panel's own display name, over a plural
    /// message and the list of files.</remarks>
    private void ReportMissingManuscripts(IReadOnlyList<string> paths)
    {
        if (paths == null || paths.Count == 0) { return; }

        string report = I18n.Get(
            "The following file is missing and could not be loaded "
            + "when restoring a session:",
            "The following files are missing and could not be loaded "
            + "when restoring a session:",
            paths.Count);

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Format(
                I18n.Get("Missing files in {name}"),
                ("name", MenuBuilder.Display(_manuscriptPanel.ToggleAction.Text))),
            Content = new TextBlock
            {
                Text = report + "\n\n" + string.Join("\n", paths),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
            },
            CloseButtonText = StandardButtons.Ok,
            XamlRoot = XamlRoot,
        };

        _ = dialog.ShowAsync();
    }

    private async Task<bool> AskAsync(string title, string message)
    {
        ContentDialog dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = StandardButtons.Discard,
            CloseButtonText = StandardButtons.Cancel,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Asks whether to save, discard or keep a modified document that is being
    /// closed.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The question.</param>
    /// <returns>What the user chose.</returns>
    /// <remarks>Upstream's three-button <c>QMessageBox.warning</c>
    /// (mainwindow.py, <c>queryCloseDocument</c>). A ContentDialog carries
    /// exactly three buttons (board trap 43/50), which is what this needs and
    /// no more.</remarks>
    private async Task<CloseAnswer> AskSaveDiscardCancelAsync(
        string title, string message)
    {
        ContentDialog dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = StandardButtons.Save,
            SecondaryButtonText = StandardButtons.Discard,
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => CloseAnswer.Save,
            ContentDialogResult.Secondary => CloseAnswer.Discard,
            _ => CloseAnswer.Cancel,
        };
    }

    /// <summary>Opens the Preferences window and applies what it changed.</summary>
    /// <param name="viewModel">The window's state.</param>
    /// <returns>The running task.</returns>
    /// <remarks>
    /// Upstream keeps ONE preferences dialog per window and re-shows it, so it
    /// re-opens on the page the user was last on; the same instance is kept
    /// here for the same reason.
    /// </remarks>
    private async Task ShowPreferencesAsync(MainViewModel viewModel)
    {
        if (_preferences == null)
        {
            _preferences = new PreferencesDialog(new PreferencesContext
            {
                Settings = viewModel.Settings,
                Actions = viewModel.ActionManager,
                Snippets = viewModel.SnippetLibrary,
                MidiPlayer = viewModel.MidiPlayer,
                Manuals = viewModel.Manuals,
                SessionStore = viewModel.SessionStore,
                PickAsync = PickPathAsync,
                PickFileAsync = PickFileOfTypeAsync,
            });

            //Upstream's app.settingsChanged(): the window re-reads what it
            //needs rather than the dialog reaching into it.
            _preferences.SettingsChanged += (_, _) => ApplyChangedSettings(viewModel);
        }

        await _preferences.ShowAsync(XamlRoot);
    }

    /// <summary>
    /// Re-reads the settings a preference change makes visible at once.
    /// </summary>
    /// <param name="viewModel">The window's state.</param>
    /// <remarks>
    /// The colours and the editor's size are re-applied to every open document
    /// so a change is seen without a relaunch. What is read at the moment it is
    /// used — the indent widths, the smart-home key, the source-export options,
    /// the helper commands — needs nothing done to it.
    /// </remarks>
    private void ApplyChangedSettings(MainViewModel viewModel)
    {
        //Upstream connects remote.setup and externalchanges.setup to
        //app.settingsChanged, so both preferences take effect at once rather
        //than at the next launch.
        RemoteInstance.Setup(viewModel.Settings, this, OnUiThread);
        viewModel.ExternalChanges.Setup();

        //Upstream's settingsChanged hangs or unhangs the main toolbar's three
        //pull-down menus the moment `verbose_toolbuttons' changes
        //(mainwindow.settingsChanged).
        _toolbar?.SettingsChanged();

        //Upstream's tabbar reads `tabs_closable' on settingsChanged too.
        if (_tabBar != null)
        {
            _tabBar.TabsClosable = viewModel.Settings?.GetBool(
                GeneralValues.TabsClosableKey, true) ?? true;
        }

        TextFormatData scheme = new TextFormatData(
            TextFormatData.CurrentScheme(viewModel.Settings), viewModel.Settings);

        foreach (var document in viewModel.Documents.Documents)
        {
            DocumentEditorState.For(document, viewModel.Settings).Styler.Scheme = scheme;
        }

        foreach (var space in _viewManager.ViewSpaces)
        {
            EditorView view = space.ActiveView;
            if (view == null) { continue; }

            view.Editor.FontSize = scheme.FontSize > 0
                ? scheme.FontSize
                : TextFormatData.DefaultFontSize;
            view.Editor.TextArea.TextView.Redraw();
        }
    }

    /// <summary>Asks the user for a folder or a file, for a preferences row.</summary>
    /// <param name="mode">What is wanted.</param>
    /// <param name="current">The path to start from.</param>
    /// <returns>The path, or null when the user cancelled.</returns>
    private async Task<string> PickPathAsync(UrlRequesterMode mode, string current)
    {
        if (mode == UrlRequesterMode.Directory)
        {
            var folders = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            folders.FileTypeFilter.Add("*");
            var folder = await folders.PickSingleFolderAsync();
            return folder?.Path;
        }

        return await PickOpenAsync("*");
    }

    /// <summary>Asks the user for a file with one of a list of suffixes.</summary>
    /// <param name="extensions">The suffixes, each with its dot.</param>
    /// <returns>The path, or null when the user cancelled.</returns>
    private async Task<string> PickFileOfTypeAsync(IReadOnlyList<string> extensions)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        foreach (var extension in extensions ?? Array.Empty<string>())
        {
            picker.FileTypeFilter.Add(extension);
        }

        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>Asks the user for one or more files to import.</summary>
    /// <param name="extensions">The suffixes to offer, each with its dot.</param>
    /// <param name="multiple">Whether more than one file may be chosen.</param>
    /// <returns>The paths, or an empty list when the user cancelled.</returns>
    /// <remarks>
    /// Upstream's <c>get_import_file</c>: <c>getOpenFileNames</c> for the
    /// generic import and <c>getOpenFileName</c> for the three specific ones.
    /// ⚠ "All Files" is offered as upstream offers it, which is exactly why the
    /// view model still checks each chosen file's suffix.
    /// </remarks>
    private async Task<IReadOnlyList<string>> PickImportAsync(
        IReadOnlyList<string> extensions, bool multiple)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        foreach (var extension in extensions ?? Array.Empty<string>())
        {
            picker.FileTypeFilter.Add(extension);
        }

        picker.FileTypeFilter.Add("*");

        if (!multiple)
        {
            var one = await picker.PickSingleFileAsync();
            return one == null ? Array.Empty<string>() : new[] { one.Path };
        }

        var files = await picker.PickMultipleFilesAsync();
        return files == null
            ? Array.Empty<string>()
            : files.Select(file => file.Path).ToArray();
    }

    private Task<string> PickOpenAsync() => PickOpenAsync(null);

    private async Task<string> PickOpenAsync(string extension)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        if (extension == null)
        {
            picker.FileTypeFilter.Add(".ly");
            picker.FileTypeFilter.Add(".ily");
        }
        else
        {
            picker.FileTypeFilter.Add(extension);
        }

        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>Asks the user where to write an export.</summary>
    /// <param name="suggestedName">The name to offer.</param>
    /// <param name="label">What to call the file type.</param>
    /// <param name="extension">Its suffix.</param>
    /// <returns>The path, or null when the user cancelled.</returns>
    /// <remarks>
    /// A picker of its own rather than PickSaveAsync's, whose one file type is
    /// a LilyPort source file: a MusicXML export offered as `.ly` would be
    /// offered under the wrong name and filtered by the wrong suffix.
    /// </remarks>
    private async Task<string> PickExportAsync(
        string suggestedName, string label, string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName == null
                ? null
                : Path.GetFileName(suggestedName),
        };
        picker.FileTypeChoices.Add(label, new[] { extension });

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private async Task<string> PickSaveAsync(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        //was previously: "LilyPond source". A picker filter is chrome, and no chrome
        //names LilyPond; the file is what LilyPort reads and writes.
        picker.FileTypeChoices.Add("LilyPort source", new[] { ".ly" });

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
