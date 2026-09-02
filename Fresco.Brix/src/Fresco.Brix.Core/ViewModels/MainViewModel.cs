// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.Simple;
using Fresco.Brix.Commands;
using Fresco.Brix.Completion;
using Fresco.Brix.Documentation;
using Fresco.Brix.Documents;
using Fresco.Brix.Engrave;
using Fresco.Brix.Midi;
using Fresco.Brix.Services;
using Fresco.Brix.Sessions;
using Fresco.Brix.Shell;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.ViewModels; //was previously: frescobaldi/mainwindow.py and app.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What the window can do that only the view knows how to do: put a file
/// dialog in front of the user, reach the editor they are working in, go
/// fullscreen, and close.
/// </summary>
/// <remarks>Each head fills in what it has; the FrameBuffer head has no file
/// dialogs, and every delegate is allowed to be null.</remarks>
public interface IWindowBridge
{
    /// <summary>Gets or sets the "pick a file to open" dialog.</summary>
    Func<Task<string>> PickOpenPathAsync { get; set; }

    /// <summary>Gets or sets the "pick a save path" dialog.</summary>
    Func<string, Task<string>> PickSavePathAsync { get; set; }

    /// <summary>
    /// Gets or sets the "pick a path to export to" dialog: a suggested name, the
    /// file type's label and its suffix.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PickSavePathAsync"/> because that one always
    /// offers a LilyPort source file, and an export is never one.
    /// </remarks>
    Func<string, string, string, Task<string>> PickExportPathAsync { get; set; }

    /// <summary>
    /// Gets or sets the "pick files to import" dialog: the suffixes to offer,
    /// and whether more than one file may be chosen.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PickOpenPathAsync"/>, which always offers a
    /// LilyPort source file, and separate again because the GENERIC import
    /// takes several files at once — upstream's
    /// <c>QFileDialog.getOpenFileNames</c>.
    /// </remarks>
    Func<IReadOnlyList<string>, bool, Task<IReadOnlyList<string>>> PickImportPathsAsync
    { get; set; }

    /// <summary>
    /// Gets or sets the import options dialog, which answers what the user
    /// chose or null when they cancelled.
    /// </summary>
    /// <remarks>Upstream's <c>configure_import</c> plus <c>conf_dlg.exec()</c>:
    /// the dialog belongs to the view, and what it decides is data.</remarks>
    Func<Import.ImportFormat, Task<Import.ImportSettings>> ConfigureImportAsync
    { get; set; }

    /// <summary>Gets or sets the accessor for the editor in focus.</summary>
    Func<EditorView> ActiveView { get; set; }

    /// <summary>Gets or sets the fullscreen switch.</summary>
    Action<bool> SetFullScreen { get; set; }

    /// <summary>Gets or sets the "close the window" action.</summary>
    Action Quit { get; set; }

    /// <summary>Gets or sets the yes/no question, used before losing edits.</summary>
    Func<string, string, Task<bool>> ConfirmAsync { get; set; }

    /// <summary>
    /// Gets or sets the "say this and wait for OK" message, used for anything
    /// that is a REPORT rather than a question.
    /// </summary>
    /// <remarks>
    /// //was previously: these went through <see cref="ConfirmAsync"/>, whose
    /// buttons are Discard and Cancel — so "Could not open {filename}" was
    /// offered under a Discard button that reads as an offer to throw work
    /// away. Upstream says them with <c>QMessageBox.critical</c> /
    /// <c>.information</c>, which has one OK button, and this is that.
    /// </remarks>
    Func<string, string, Task> AlertAsync { get; set; }

    /// <summary>
    /// Gets or sets the save-or-discard question a modified document is closed
    /// with — upstream's three-button <c>QMessageBox.warning</c>.
    /// </summary>
    Func<string, string, Task<CloseAnswer>> AskSaveDiscardAsync { get; set; }
}

/// <summary>
/// What the user answered when asked about a modified document that is being
/// closed.
/// </summary>
/// <remarks>Upstream's <c>QMessageBox.StandardButton.Save | Discard |
/// Cancel</c> in <c>MainWindow.queryCloseDocument</c>.</remarks>
public enum CloseAnswer
{
    /// <summary>Close without saving.</summary>
    Discard,

    /// <summary>Save first, then close.</summary>
    Save,

    /// <summary>Do not close.</summary>
    Cancel,
}

/// <summary>
/// The window: the open documents, the commands that act on them, the tool
/// panels, and the state the title and status bar show.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel
{
    private SettingsStore _settings;
    private RecentFiles _recentFiles;
    private Backup _backup;
    private AutoCompiler _autoCompiler;
    private MidiPlayerService _midiPlayer;
    private ScoreWizardDialog _scoreWizard;
    private DocumentFontsDialog _documentFonts;
    private UserGuideDialog _userGuide;
    private ManualLibrary _manuals;

    /// <summary>Creates the window's state.</summary>
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Fresco.Brix main view model startup.");

        _settings = GetService<SettingsStore>();

        //THE INTERFACE LANGUAGE GOES IN FIRST, before a single command, panel
        //or dialog has built a caption. Upstream installs it from @app.oninit,
        //which is the same moment: nothing user-visible exists yet.
        LanguageSetup.Setup(_settings);

        _recentFiles = GetService<RecentFiles>();
        _backup = new Backup(_settings);

        Documents = new DocumentManager();
        Actions = new MainActions(_settings);
        ViewActions = new ViewActions(_settings);
        SideBarActions = new SideBarActions(_settings);
        EngraveActions = new EngraveActions(_settings);
        LogActions = new LogActions(_settings);
        MusicViewActions = new MusicViewActions(_settings);
        DocumentActions = new DocumentActions(_settings);
        CompletionActions = new CompletionActions(_settings);
        BookmarkActions = new BookmarkActions(_settings);
        MatchingPairActions = new MatchingPairActions(_settings);
        PitchActions = new PitchActions(_settings);
        RestActions = new RestActions(_settings);
        RhythmActions = new RhythmActions(_settings);
        LyricsActions = new LyricsActions(_settings);
        ScoreWizardActions = new ScoreWizardActions(_settings);
        FontsActions = new FontsActions(_settings);
        FileImportActions = new FileImportActions(_settings);
        MidiActions = new MidiActions(_settings);
        DocumentationActions = new DocumentationActions(_settings);
        EditorCommandActions = new EditorCommandActions(_settings);

        //The desktop's own viewers, file manager and terminal. It reads the
        //user's configured helper commands out of the same store W12's
        //preferences page will write them to.
        Helpers = new HelperApplications(_settings);

        //Watching the open files for changes made by other programs, and the
        //"Modified Files" experience over it. The watcher does not start until
        //ExternalChanges.Setup() is called, which the window does once it can
        //supply a thread to come back on and a window to show.
        DocumentWatcher = new DocumentWatchService(Documents);
        ExternalChanges = new ExternalChanges(Documents, DocumentWatcher, _settings);

        //The editor tools. Each is a service the window's panels and menus
        //reach through; what only a view can do arrives as a delegate.
        History = new HistoryManager(Documents);
        Browser = new BrowserInterface(Documents, _settings);
        SnippetLibrary = new SnippetLibrary(_settings);
        SnippetShortcuts = new SnippetShortcuts(SnippetLibrary, _settings);
        SessionStore = new SessionStore(_settings);
        SessionManager = new SessionManager(SessionStore, Documents, _settings);

        ActionManager = new ActionCollectionManager();
        ActionManager.Add(Actions);
        ActionManager.Add(ViewActions);
        ActionManager.Add(SideBarActions);
        ActionManager.Add(EngraveActions);
        ActionManager.Add(LogActions);
        ActionManager.Add(MusicViewActions);
        ActionManager.Add(DocumentActions);
        ActionManager.Add(CompletionActions);
        ActionManager.Add(BookmarkActions);
        ActionManager.Add(MatchingPairActions);
        ActionManager.Add(PitchActions);
        ActionManager.Add(RestActions);
        ActionManager.Add(RhythmActions);
        ActionManager.Add(LyricsActions);
        ActionManager.Add(ScoreWizardActions);
        ActionManager.Add(FontsActions);
        ActionManager.Add(FileImportActions);
        ActionManager.Add(MidiActions);
        ActionManager.Add(DocumentationActions);
        ActionManager.Add(EditorCommandActions);
        ActionManager.Add(Browser.Actions);
        ActionManager.Add(SnippetShortcuts);
        ActionManager.Add(SessionManager.Actions);

        //What the app remembers per document. Declared before any document is
        //opened, because a value not declared is a value not stored.
        //The store goes on FIRST: a per-document state is built once, by
        //whichever caller asks for it first, and most of them pass no store —
        //see DocumentEditorState.DefaultSettings for what that cost.
        DocumentEditorState.DefaultSettings = _settings;

        //The application-wide include path (upstream's
        //`lilypond_settings/include_path'), which a session's own path is
        //prepended to. The Tools preferences page writes it; this is the read
        //at startup, before any document has asked where \include looks.
        DocumentInfo.ApplicationIncludePath
            = Fresco.Brix.Engrave.Engraver.IncludePath(_settings);
        MetaInfo.Define(EditorView.RememberedPositionName, "0");
        EngraveProgress.Define();
        Bookmarks.Define();
        DocumentActions.Define();

        //The engine is one per process and starts loading NOW, in the
        //background: it takes seconds, and the first thing a user does after
        //opening a file is press Engrave.
        //(The window subscribes to StateChanged itself: the engine raises it on
        //its own thread and only the view knows how to get back onto the UI's.)
        Engine = GetService<LilyPortEngine>();
        _ = Engine.BeginLoadingAsync();

        //The error references need to know which documents are open, so a
        //message about a scratch copy lands in the document it was made from.
        EngraveErrors.Documents = Documents;

        Engraver = new Engraver(Documents, Engine, EngraveActions, _settings)
        {
            OpenMaster = path => Documents.OpenDocument(path),
            SaveDocument = document => SaveQuietly(document),
        };

        WireDocumentEvents();
        WireActions();
    }

    /// <summary>Gets the open documents.</summary>
    public DocumentManager Documents { get; }

    /// <summary>Gets the window's own commands.</summary>
    public MainActions Actions { get; }

    /// <summary>Gets the Window menu's view commands.</summary>
    public ViewActions ViewActions { get; }

    /// <summary>Gets the View menu's editor-margin commands.</summary>
    public SideBarActions SideBarActions { get; }

    /// <summary>Gets the LilyPond menu's engraving commands.</summary>
    public EngraveActions EngraveActions { get; }

    /// <summary>Gets the log panel's own commands.</summary>
    public LogActions LogActions { get; }

    /// <summary>Gets the Music View's own commands.</summary>
    public MusicViewActions MusicViewActions { get; }

    /// <summary>Gets the document-transforming commands.</summary>
    public DocumentActions DocumentActions { get; }

    /// <summary>Gets the automatic-completion commands.</summary>
    public CompletionActions CompletionActions { get; }

    /// <summary>Gets the marked-line commands.</summary>
    public BookmarkActions BookmarkActions { get; }

    /// <summary>Gets the matching-token commands.</summary>
    public MatchingPairActions MatchingPairActions { get; }

    /// <summary>Gets the pitch commands.</summary>
    public PitchActions PitchActions { get; }

    /// <summary>Gets the rest commands.</summary>
    public RestActions RestActions { get; }

    /// <summary>Gets the rhythm commands.</summary>
    public RhythmActions RhythmActions { get; }

    /// <summary>Gets the lyric commands.</summary>
    public LyricsActions LyricsActions { get; }

    /// <summary>Gets the Score Wizard's commands.</summary>
    public ScoreWizardActions ScoreWizardActions { get; }

    /// <summary>Gets the Document Fonts command.</summary>
    public FontsActions FontsActions { get; }

    /// <summary>Gets the File &gt; Import commands.</summary>
    public FileImportActions FileImportActions { get; }

    /// <summary>Gets the MIDI player's transport commands.</summary>
    public MidiActions MidiActions { get; }

    /// <summary>Gets the documentation browser's commands.</summary>
    public DocumentationActions DocumentationActions { get; }

    /// <summary>Gets the service that hands a file or URL to the desktop.</summary>
    public HelperApplications Helpers { get; }

    /// <summary>
    /// Gets the bundled manuals, opened the first time they are asked for.
    /// </summary>
    /// <remarks>One per window. Opening it reads nothing — a manual's own
    /// index is read when that manual is first shown.</remarks>
    public ManualLibrary Manuals => _manuals ??= new ManualLibrary();

    /// <summary>
    /// Gets the MIDI player, built the first time it is asked for.
    /// </summary>
    /// <remarks>One per window, and it opens no audio device until something
    /// is loaded into it — which the MIDI panel only does once it has been
    /// opened.</remarks>
    public IMidiPlayer MidiPlayer => _midiPlayer ??= new MidiPlayerService(_settings);

    /// <summary>
    /// Gets the Score Wizard, built the first time it is asked for.
    /// </summary>
    /// <remarks>One per window, kept: what the user assembled is still there
    /// when they open it again, which is upstream's own arrangement.</remarks>
    public ScoreWizardDialog ScoreWizard
        => _scoreWizard ??= new ScoreWizardDialog(_settings);

    /// <summary>
    /// Gets the Document Fonts dialog, built the first time it is asked for.
    /// </summary>
    /// <remarks>One per window, kept: upstream's five chosen fonts are a CLASS
    /// variable and so survive the window closing, and they survive here for
    /// the same reason.</remarks>
    public DocumentFontsDialog DocumentFonts
        => _documentFonts ??= new DocumentFontsDialog(
            _settings, Engine, new MusicView.LilyPortTypefaceResolver());

    /// <summary>
    /// Gets the user guide, built the first time it is asked for.
    /// </summary>
    /// <remarks>One per window, kept — upstream keeps its browser in a
    /// module-level <c>_browser</c> for the same reason: the history behind
    /// Back should survive closing the guide and opening it again.</remarks>
    public UserGuideDialog UserGuide
        => _userGuide ??= new UserGuideDialog(_settings, ActionManager);

    /// <summary>Gets the file-system watcher over the open documents.</summary>
    public DocumentWatchService DocumentWatcher { get; }

    /// <summary>Gets the "files changed on disk" service.</summary>
    public ExternalChanges ExternalChanges { get; }

    /// <summary>Gets the order documents were last active in.</summary>
    public HistoryManager History { get; }

    /// <summary>Gets the back/forward place history.</summary>
    public BrowserInterface Browser { get; }

    /// <summary>Gets the snippets.</summary>
    public SnippetLibrary SnippetLibrary { get; }

    /// <summary>Gets the snippets' keyboard shortcuts.</summary>
    public SnippetShortcuts SnippetShortcuts { get; }

    /// <summary>Gets the twenty-two native editor commands (FD10).</summary>
    public EditorCommandActions EditorCommandActions { get; }

    /// <summary>Gets the stored named sessions.</summary>
    public SessionStore SessionStore { get; }

    /// <summary>Gets the named-session service.</summary>
    public SessionManager SessionManager { get; }

    /// <summary>Gets the in-process LilyPond engine.</summary>
    public LilyPortEngine Engine { get; }

    /// <summary>Gets the engraving service.</summary>
    public Engraver Engraver { get; }

    /// <summary>Gets the automatic engraver, once the window has built it.</summary>
    public AutoCompiler AutoCompiler => _autoCompiler;

    /// <summary>Gets every command collection, for the shortcut settings.</summary>
    public ActionCollectionManager ActionManager { get; }

    /// <summary>Gets the recently-opened documents.</summary>
    public RecentFiles RecentFiles => _recentFiles;

    /// <summary>Gets the settings store.</summary>
    public SettingsStore Settings => _settings;

    /// <summary>Gets or sets the bridge to what only the view can do.</summary>
    public IWindowBridge Window { get; set; }

    /// <summary>Gets or sets the tool panels, once the page has built them.</summary>
    public PanelManager Panels { get; set; }

    #region | Bindable properties |

    /// <summary>
    /// Gets the window title: the current document's name, a star when it has
    /// unsaved changes, and the application name.
    /// </summary>
    public string WindowTitle
    {
        get
        {
            EditorDocument document = Documents?.CurrentDocument;
            if (document == null) { return AppInfo.AppName; }

            string star = document.IsModified ? "*" : string.Empty;
            string engine = EngineStatusText;
            string suffix = string.IsNullOrEmpty(engine) ? string.Empty : $" [{engine}]";
            return $"{document.DocumentName()}{star} - {AppInfo.AppName}{suffix}";
        }
    }

    /// <summary>
    /// Gets what the title bar says about the engine while it loads.
    /// </summary>
    /// <remarks>The load takes seconds and the window is fully usable
    /// throughout, so this is a note in the title rather than a splash screen
    /// standing in front of the application.</remarks>
    public string EngineStatusText
        => Engine?.State switch
        {
            EngineState.Loading => I18n.Get("loading the LilyPort engine..."),
            EngineState.Failed => I18n.Get("the LilyPort engine failed to load"),
            _ => string.Empty,
        };

    /// <summary>Gets or sets the status bar text.</summary>
    public string StatusText
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    #endregion

    /// <summary>
    /// Opens the documents named on the command line, or an empty one when
    /// there are none — the state the window starts in.
    /// </summary>
    /// <param name="paths">The files to open.</param>
    /// <param name="encoding">The encoding to read them in, or null to detect
    /// it — upstream's <c>--encoding</c>.</param>
    /// <returns>The task.</returns>
    public async Task StartAsync(
        IEnumerable<string> paths = null, string encoding = null)
    {
        bool openedAny = false;
        foreach (var path in paths ?? Array.Empty<string>())
        {
            if (await OpenPathAsync(path, encoding))
            {
                openedAny = true;
            }
        }

        if (!openedAny)
        {
            Documents.CurrentDocument = Documents.CreateDocument();
        }
    }

    /// <summary>Opens a file, or raises the tab already showing it.</summary>
    /// <param name="path">The file.</param>
    /// <param name="encoding">The encoding name, or null to detect it.</param>
    /// <returns>Whether it opened.</returns>
    public async Task<bool> OpenPathAsync(string path, string encoding = null)
    {
        if (string.IsNullOrEmpty(path)) { return false; }

        try
        {
            EditorDocument document = Documents.OpenDocument(
                path, EncodingNamed(encoding));
            Documents.CurrentDocument = document;
            _recentFiles?.Add(path);
            return true;
        }
        catch (IOException error)
        {
            await ReportAsync(I18n.Format(
                I18n.Get("Could not open {filename}:\n{error}"),
                ("filename", path), ("error", error.Message)));
            _recentFiles?.Remove(path);
            return false;
        }
    }

    /// <summary>
    /// Resolves an encoding name, answering null when it names nothing.
    /// </summary>
    /// <param name="name">The name, or null.</param>
    /// <returns>The encoding, or null to let the document detect it.</returns>
    /// <remarks>Upstream hands <c>args.encoding</c> straight to the document,
    /// where an unknown name raises and is caught; the same "an unusable name
    /// means detect it" answer is given here, one step earlier.</remarks>
    private static System.Text.Encoding EncodingNamed(string name)
    {
        if (string.IsNullOrEmpty(name)) { return null; }

        try { return System.Text.Encoding.GetEncoding(name); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// Starts the automatic engraver, once the window can marshal back onto
    /// its own thread.
    /// </summary>
    /// <param name="toUiThread">How to get back onto the UI thread.</param>
    public void StartAutoCompiler(Action<Action> toUiThread)
        => _autoCompiler ??= new AutoCompiler(Engraver, Documents, toUiThread);

    /// <summary>Announces that a bound property of the window changed.</summary>
    /// <param name="propertyName">The property.</param>
    public void Refresh(string propertyName) => NotifyPropertyChanged(propertyName);

    private void WireDocumentEvents()
    {
        void TitleChanged(object sender, DocumentEventArgs e)
            => NotifyPropertyChanged(nameof(WindowTitle));

        Documents.CurrentDocumentChanged += (_, e) =>
        {
            TitleChanged(null, e);
            UpdateEditActions();
        };
        Documents.DocumentModificationChanged += TitleChanged;
        Documents.DocumentUrlChanged += TitleChanged;
        Documents.DocumentSaved += TitleChanged;
        Documents.DocumentLoaded += TitleChanged;
    }

    private void WireActions()
    {
        Actions.FileNew.Handler = () =>
            Documents.CurrentDocument = Documents.CreateDocument();
        Actions.FileOpen.AsyncHandler = DoOpenAsync;
        Actions.FileSave.AsyncHandler = () => SaveAsync(Documents.CurrentDocument, false);
        Actions.FileSaveAs.AsyncHandler = () => SaveAsync(Documents.CurrentDocument, true);
        Actions.FileSaveAll.AsyncHandler = DoSaveAllAsync;
        Actions.FileClose.AsyncHandler = () => CloseAsync(Documents.CurrentDocument);
        Actions.FileCloseOther.AsyncHandler = DoCloseOtherAsync;
        Actions.FileCloseAll.AsyncHandler = DoCloseAllAsync;
        Actions.FileReload.AsyncHandler = DoReloadAsync;
        Actions.FileReloadAll.AsyncHandler = DoReloadAllAsync;

        //Upstream's File > Check for External Changes, which shows the window
        //even when nothing has changed.
        Actions.FileExternalChanges.Handler
            = () => ExternalChanges.DisplayChangedDocuments();
        Actions.FileQuit.AsyncHandler = DoQuitAsync;

        Actions.EditUndo.Handler = () => WithEditor(e => e.Editor.Undo());
        Actions.EditRedo.Handler = () => WithEditor(e => e.Editor.Redo());
        Actions.EditCut.Handler = () => WithEditor(e => e.Editor.Cut());
        Actions.EditCopy.Handler = () => WithEditor(e => e.Editor.Copy());
        Actions.EditPaste.Handler = () => WithEditor(e => e.Editor.Paste());
        Actions.EditSelectAll.Handler = () => WithEditor(e => e.Editor.SelectAll());
        Actions.EditSelectNone.Handler
            = () => WithEditor(e => e.Editor.TextArea.ClearSelection());

        FileImportActions.ImportAny.AsyncHandler = () => DoImportAsync(null);
        FileImportActions.ImportMusicXml.AsyncHandler
            = () => DoImportAsync(Import.ImportFormat.MusicXml);
        FileImportActions.ImportMidi.AsyncHandler
            = () => DoImportAsync(Import.ImportFormat.Midi);
        FileImportActions.ImportAbc.AsyncHandler
            = () => DoImportAsync(Import.ImportFormat.Abc);

        Actions.ExportMusicXml.AsyncHandler = DoExportMusicXmlAsync;
        Actions.ExportAudio.AsyncHandler = DoExportAudioAsync;
        Actions.ExportColoredHtml.AsyncHandler = DoExportColoredHtmlAsync;
        Actions.EditCopyColoredHtml.Handler = DoCopyColoredHtml;

        Actions.ViewNextDocument.Handler = () => StepDocument(1);
        Actions.ViewPreviousDocument.Handler = () => StepDocument(-1);
        Actions.ViewWrapLines.Handler
            = () => WithEditor(e => e.Editor.WordWrap = Actions.ViewWrapLines.IsChecked);
        Actions.ViewScrollUp.Handler = () => WithEditor(e => e.Editor.LineUp());
        Actions.ViewScrollDown.Handler = () => WithEditor(e => e.Editor.LineDown());

        Actions.WindowFullscreen.Handler
            = () => Window?.SetFullScreen?.Invoke(Actions.WindowFullscreen.IsChecked);

        //Tools > Directories. Upstream's own two, and the same helper the
        //documentation panel opens a manual with.
        Actions.FileOpenCurrentDirectory.AsyncHandler
            = () => OpenCurrentDirectoryAsync("directory");
        Actions.FileOpenCommandPrompt.AsyncHandler
            = () => OpenCurrentDirectoryAsync("shell");

        //The commands whose waves have not arrived yet stay visible but inert,
        //so the menu shows the finished shape from the start.
        foreach (var name in PendingActionNames)
        {
            AppAction action = Actions.Action(name);
            if (action != null) { action.IsEnabled = false; }
        }
    }

    /// <summary>
    /// The commands the menus already show but that belong to a later wave.
    /// </summary>
    private static readonly string[] PendingActionNames =
    {
        "file_insert_file", "file_save_copy_as", "file_rename",
        //was previously: "file_external_changes" — W12A ported documentwatcher
        //and externalchanges, and the command opens the "Modified Files" window.
        "file_close_all_and_session",
        //was previously: "export_colored_html", "edit_copy_colored_html" — both
        //waited on the HTML half of ly.colorize, which W11 ported.
        "edit_select_current_toplevel",
        "edit_select_full_lines_up", "edit_select_full_lines_down",
        //was previously: "edit_preferences" and "help_about" — W12A built the
        //Preferences dialog and the About window, and both are wired in
        //MainPage.
        //was previously: "help_manual" — W12B built the user guide (board
        //decision FD8) and the command opens it; the handler is wired in
        //MainPage.
        //was previously: "view_goto_line" — W13's close-out built the number
        //prompt (upstream's `MainWindow.gotoLine'); the handler is wired in
        //MainPage.
        "window_new",
    };

    /// <summary>
    /// Opens the current document's directory with a helper.
    /// </summary>
    /// <param name="type">The helper type — <c>directory</c> or <c>shell</c>.</param>
    /// <returns>The running task.</returns>
    /// <remarks>Upstream's <c>openCurrentDirectory</c> and
    /// <c>openCommandPrompt</c>, which are the same call with a different
    /// helper type.</remarks>
    private async Task OpenCurrentDirectoryAsync(string type)
    {
        string directory = CurrentDirectory();
        if (string.IsNullOrEmpty(directory)) { return; }

        await Helpers.OpenPathAsync(directory, type).ConfigureAwait(true);
    }

    /// <summary>
    /// The directory the current document is in, or the working directory.
    /// </summary>
    /// <returns>The path.</returns>
    /// <remarks>Upstream's <c>MainWindow.currentDirectory</c>: an unsaved
    /// document has no directory of its own, so the process's is used.</remarks>
    public string CurrentDirectory()
    {
        string path = Documents?.CurrentDocument?.Path;
        return string.IsNullOrEmpty(path)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(path));
    }

    private void UpdateEditActions()
    {
        bool hasDocument = Documents.CurrentDocument != null;
        foreach (var name in new[]
        {
            "file_save", "file_save_as", "file_close", "file_reload",
            "edit_undo", "edit_redo", "edit_cut", "edit_copy", "edit_paste",
            "edit_select_all", "edit_select_none",
        })
        {
            AppAction action = Actions.Action(name);
            if (action != null) { action.IsEnabled = hasDocument; }
        }
    }

    private void WithEditor(Action<EditorView> work)
    {
        EditorView view = Window?.ActiveView?.Invoke();
        if (view != null) { work(view); }
    }

    /// <summary>File &gt; Import/Export &gt; one of the four import entries.</summary>
    /// <param name="format">The format, or null for the generic import.</param>
    /// <returns>The task.</returns>
    /// <remarks>
    /// Upstream's <c>do_import</c>: ask for the files, then take them one at a
    /// time. ⚠ The wrong-file-type check is upstream's own, and so is its
    /// reason — the file dialog offers "All Files" as well, so a user can
    /// choose something no converter reads, and saying so is better than a
    /// stack trace. Upstream's own comment calls it a TODO; the same is true
    /// here, and the same check stands.
    /// </remarks>
    private async Task DoImportAsync(Import.ImportFormat? format)
    {
        Func<IReadOnlyList<string>, bool, Task<IReadOnlyList<string>>> pick
            = Window?.PickImportPathsAsync;
        if (pick == null) { return; }

        IReadOnlyList<string> extensions = format == null
            ? Import.ImportFormats.AllExtensions
            : Import.ImportFormats.ExtensionsFor(format.Value);

        //Only the generic import takes more than one file at a time.
        IReadOnlyList<string> files = await pick(extensions, format == null)
            ?? Array.Empty<string>();

        foreach (string file in files)
        {
            if (!Import.ImportFormats.IsImportable(file))
            {
                await ReportAsync(I18n.Format(
                    I18n.Get(
                        "The file {filename} could not be converted: wrong file type."),
                    ("filename", file)));
                continue;
            }

            await ImportFileAsync(file);
        }
    }

    /// <summary>Converts one file and opens the result in a new tab.</summary>
    /// <param name="path">The file to convert.</param>
    /// <returns>The document, or null when the user cancelled or it failed.</returns>
    /// <remarks>
    /// <para>
    /// Upstream's <c>configure_import</c> + <c>run_import</c> + <c>import_done</c>,
    /// with the external command replaced by the in-process converter (ruling
    /// FD1; the shape W11 made for Export Audio).
    /// </para>
    /// <para>
    /// ⚠ ONE STEP IS IN A DIFFERENT ORDER FROM UPSTREAM, and it is the
    /// REPLACE's doing rather than a disagreement. Upstream runs the converter
    /// first and opens the file it wrote afterwards, because the converter is a
    /// subprocess whose output is watched in a job dialog. There is no job
    /// dialog here — the converter's messages belong in the log, and the log
    /// follows a DOCUMENT — so the tab is made first and the conversion runs as
    /// that document's job. The end state is upstream's exactly: the converted
    /// source, in a tab, saved beside the file it came from. A conversion that
    /// fails closes the tab again, which is what upstream's job dialog
    /// declining to auto-accept amounts to.
    /// </para>
    /// </remarks>
    public async Task<EditorDocument> ImportFileAsync(string path)
    {
        Import.ImportFormat? format = Import.ImportFormats.FormatOf(path);
        if (format == null) { return null; }

        Func<Import.ImportFormat, Task<Import.ImportSettings>> configure
            = Window?.ConfigureImportAsync;
        Import.ImportSettings chosen = configure == null
            ? null
            : await configure(format.Value);
        if (chosen == null) { return null; }

        //Upstream's own naming: the converted source goes beside its source
        //under the same name with a `.ly` suffix, stepped past anything on disk
        //or already open.
        string target = FreeImportPath(path);

        EditorDocument document = Documents.CreateDocument();
        Documents.CurrentDocument = document;

        Import.ImportJob job = new Import.ImportJob(
            format.Value, path, chosen.ToOptions(System.IO.Path.GetFileName(path)));

        TaskCompletionSource<bool> finished
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        job.Done += (_, success) => finished.TrySetResult(success);
        StatusText = I18n.Format(
            I18n.Get("Importing {filename}..."),
            ("filename", System.IO.Path.GetFileName(path)));
        JobManager.For(document).StartJob(job);
        bool converted = await finished.Task;

        if (!converted || job.Text == null)
        {
            Documents.CloseDocument(document);
            StatusText = I18n.Format(
                I18n.Get("The file {filename} could not be converted."),
                ("filename", System.IO.Path.GetFileName(path)));
            return null;
        }

        //Upstream saves the dialog's settings in `import_done', which only runs
        //when the conversion went through.
        chosen.Save(_settings);

        document.Document.Text = job.Text;
        try
        {
            document.Save(target);
            _recentFiles?.Add(target);
        }
        catch (Exception error) when (
            error is System.IO.IOException or UnauthorizedAccessException)
        {
            //The document keeps the converted source and its own name, so
            //nothing is lost; the user is told where it could not be written.
            await ReportAsync(I18n.Format(
                I18n.Get("Could not write to {filename}:\n{error}"),
                ("filename", target), ("error", error.Message)));
            return document;
        }

        //Upstream's post_import, in its own order — and, as upstream does, the
        //engrave runs BEFORE the final save, so an "Engrave directly" import
        //engraves the scratch copy of what is on screen.
        PostImport(chosen.Post, document);
        SaveQuietly(document);
        StatusText = I18n.Get("Saved") + ": " + target;
        return document;
    }

    /// <summary>
    /// Applies the "After Import" adaptations to a freshly imported document.
    /// </summary>
    /// <param name="post">What was asked for.</param>
    /// <param name="document">The document.</param>
    /// <remarks>Upstream's <c>FileImport.post_import</c>, whose four steps are
    /// Tools &gt; Format, Tools &gt; Rhythm &gt; Make implicit (per line),
    /// Tools &gt; Rhythm &gt; Remove fraction scaling and Engrave
    /// (preview).</remarks>
    private void PostImport(Import.PostImportSettings post, EditorDocument document)
    {
        if (post == null || document == null) { return; }

        if (post.Reformat)
        {
            Reformatting.Reformat(document, _settings, 0, 0);
        }

        if (post.TrimDurations)
        {
            RhythmTools.ImplicitPerLine(WholeDocumentCursor(document));
        }

        if (post.RemoveScaling)
        {
            RhythmTools.RemoveFractionScaling(WholeDocumentCursor(document));
        }

        if (post.EngraveDirectly)
        {
            //maySave: false — upstream passes False here for the same reason,
            //and the document is saved a moment later anyway.
            Engraver?.Engrave(EngraveMode.Preview, document, maySave: false);
        }
    }

    /// <summary>An ly cursor over a whole document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The cursor.</returns>
    /// <remarks>Upstream selects the document with
    /// <c>cursor.select(SelectionType.Document)</c> before each rhythm
    /// step.</remarks>
    private Fresco.Brix.Ly.Cursor WholeDocumentCursor(EditorDocument document)
        => new Fresco.Brix.Ly.Cursor(
            DocumentEditorState.For(document, _settings).LyDocument,
            0,
            document.Text.Length);

    /// <summary>
    /// The name an imported document is written under: the source's own name
    /// with a <c>.ly</c> suffix, stepped past anything already there.
    /// </summary>
    /// <param name="inputPath">The file being converted.</param>
    /// <returns>The free path.</returns>
    /// <remarks>Upstream's loop in <c>import_done</c>, which tests BOTH the
    /// file system and the open documents — so an import never quietly
    /// overwrites a file, nor collides with a tab.</remarks>
    private string FreeImportPath(string inputPath)
    {
        Services.PathUtil.SplitExtension(inputPath, out string root);
        string target = root + ".ly";
        while (System.IO.File.Exists(target) || Documents.FindDocument(target) != null)
        {
            target = Services.PathUtil.NextFile(target);
        }

        return target;
    }

    /// <summary>File &gt; Import/Export &gt; Export MusicXML.</summary>
    /// <returns>The task.</returns>
    private async Task DoExportMusicXmlAsync()
    {
        EditorDocument document = Documents?.CurrentDocument;
        if (document == null) { return; }

        string path = await PickExport(
            Export.MusicXmlExport.SuggestedName(document.Path),
            I18n.Get("XML Files"), ".xml");
        if (path == null) { return; }

        var warnings = new List<string>();
        try
        {
            //⚠ RULING FR15: the export REFUSES rather than writing a file that
            //does not conform, and the reason is the user's to see.
            Export.MusicXmlExportResult result = Export.MusicXmlExport.Write(
                document.Document.Text, path, document.Path, warnings);
            StatusText = result.Ok
                ? I18n.Get("Saved") + ": " + result.Path
                : result.Reason;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            //Upstream's own message, and its own shape: the destination and the
            //reason, because "could not write" on its own tells nobody anything.
            StatusText = I18n.Get("Can't write to destination:")
                + " " + path + " — " + exception.Message;
        }
    }

    /// <summary>File &gt; Import/Export &gt; Export Audio.</summary>
    /// <returns>The task.</returns>
    /// <remarks>
    /// Upstream needs a MIDI file to exist already and says so; that is kept,
    /// because a document that has never been engraved has no music to render.
    /// </remarks>
    private async Task DoExportAudioAsync()
    {
        EditorDocument document = Documents?.CurrentDocument;
        if (document == null) { return; }

        string midi = FirstMidiFile(document);
        if (midi == null)
        {
            StatusText = I18n.Get(
                "The audio file couldn't be created. Please create midi file first");
            return;
        }

        string path = await PickExport(
            Export.AudioExport.SuggestedName(document.Path), I18n.Get("WAV Files"), ".wav");
        if (path == null) { return; }

        StatusText = I18n.Get("Exporting audio...");
        Export.AudioExportResult result = await Task.Run(
            () => Export.AudioExport.Render(midi, path, _settings));
        StatusText = result.Ok
            ? I18n.Get("Saved") + ": " + result.Path
            : result.Error;
    }

    /// <summary>File &gt; Import/Export &gt; Export Source as Colored HTML.</summary>
    /// <returns>The task.</returns>
    private async Task DoExportColoredHtmlAsync()
    {
        EditorDocument document = Documents?.CurrentDocument;
        if (document == null) { return; }

        string path = await PickExport(
            Export.ColoredHtml.SuggestedName(document.Path),
            I18n.Get("HTML Files"), ".html");
        if (path == null) { return; }

        DocumentEditorState state = DocumentEditorState.For(document, _settings);
        try
        {
            File.WriteAllText(
                path,
                Export.ColoredHtml.FromDocument(state, ReadHtmlOptions(inline: false), _settings),
                new System.Text.UTF8Encoding(false));
            StatusText = I18n.Get("Saved") + ": " + path;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText = I18n.Get("Could not write to: {url}").Replace("{url}", path)
                + " — " + exception.Message;
        }
    }

    /// <summary>Edit &gt; Copy as Colored HTML.</summary>
    private void DoCopyColoredHtml()
    {
        EditorDocument document = Documents?.CurrentDocument;
        EditorView view = Window?.ActiveView?.Invoke();
        if (document == null || view == null) { return; }

        int start = view.Editor.SelectionStart;
        int length = view.Editor.SelectionLength;
        if (length <= 0) { return; }

        DocumentEditorState state = DocumentEditorState.For(document, _settings);
        //⚠ The clipboard's default is INLINE styles, not a stylesheet: what is
        //pasted has nowhere to carry one. That is upstream's inline_copy, and it
        //is a different default from the file export's inline_export.
        string html = Export.ColoredHtml.FromSelection(
            state, start, length, ReadHtmlOptions(inline: true), _settings);

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        //⚠ The platform's clipboard carries TEXT. Upstream offers HTML or plain
        //text by a preference (copy_html_as_plain_text) and puts the markup on
        //the clipboard as HTML when it is off; here the markup goes over as
        //text either way, which is what a paste into an editor wants and is all
        //the heads agree on. Written up for the W13 audit.
        package.SetText(html);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        StatusText = I18n.Get("Copied");
    }

    /// <summary>
    /// Reads the source-export options both colored-HTML commands use.
    /// </summary>
    /// <param name="inline">Whether styles go inline (the clipboard) or into a
    /// stylesheet (the file).</param>
    /// <returns>The options.</returns>
    /// <remarks>
    /// //was previously: <c>Scheme</c> was left at its default "editor", so the
    /// Fonts &amp; Colors page's second-scheme tick wrote a key that a live code
    /// path read and that no command ever asked for. Upstream's only consumer of
    /// that key is <c>printSource()</c>, which ruling FR5.5 removed; the two
    /// output channels that survive are this file export and Edit ▸ Copy as
    /// Colored HTML, so they are what the setting now points at. The stored KEY
    /// stays <c>printer_scheme</c> — upstream's own spelling, so a settings file
    /// still moves between the two applications — and
    /// <c>TextFormatData.PrinterScheme</c> falls back to the editor's scheme
    /// when the user has chosen none, so nothing changes for a user who never
    /// touches the tick.
    /// </remarks>
    private Export.ColoredHtmlOptions ReadHtmlOptions(bool inline)
        => new Export.ColoredHtmlOptions
        {
            Scheme = "printer",
            Inline = _settings?.GetBool(
                inline ? "source_export/inline_copy" : "source_export/inline_export", inline)
                ?? inline,
            NumberLines = _settings?.GetBool("source_export/number_lines", false) ?? false,
            WrapTag = _settings?.GetString("source_export/wrap_tag", "pre") ?? "pre",
            WrapAttribute = _settings?.GetString("source_export/wrap_attrib", "id") ?? "id",
            WrapAttributeName
                = _settings?.GetString("source_export/wrap_attrib_name", "document") ?? "document",
        };

    private Task<string> PickExport(string name, string label, string extension)
    {
        Func<string, string, string, Task<string>> pick = Window?.PickExportPathAsync;
        return pick == null ? Task.FromResult<string>(null) : pick(name, label, extension);
    }

    private string FirstMidiFile(EditorDocument document)
    {
        //`.mid*` is upstream's glob; the port's ResultFiles takes one suffix,
        //so both spellings are asked for in upstream's own order.
        foreach (string extension in new[] { ".midi", ".mid" })
        {
            foreach (string path in ResultFiles.For(document).Files(extension))
            {
                return path;
            }
        }

        return null;
    }

    private void StepDocument(int direction)
    {
        IReadOnlyList<EditorDocument> documents = Documents.Documents;
        if (documents.Count == 0) { return; }

        int index = documents.ToList().IndexOf(Documents.CurrentDocument) + direction;
        Documents.CurrentDocument =
            documents[((index % documents.Count) + documents.Count) % documents.Count];
    }

    private async Task DoOpenAsync()
    {
        Func<Task<string>> pick = Window?.PickOpenPathAsync;
        if (pick == null) { return; }

        string path = await pick();
        if (!string.IsNullOrEmpty(path))
        {
            await OpenPathAsync(path);
        }
    }

    /// <summary>Saves a document, asking for a path when it needs one.</summary>
    /// <param name="document">The document.</param>
    /// <param name="saveAs">Whether to ask for a path even if it has one.</param>
    /// <returns>Whether it was saved.</returns>
    public async Task<bool> SaveAsync(EditorDocument document, bool saveAs)
    {
        if (document == null) { return false; }

        string path = document.Path;
        if (saveAs || path == null)
        {
            Func<string, Task<string>> pick = Window?.PickSavePathAsync;
            if (pick == null) { return false; }

            path = await pick(document.Path == null
                ? document.DocumentName() + ".ly"
                : System.IO.Path.GetFileName(document.Path));
            if (string.IsNullOrEmpty(path)) { return false; }
        }

        try
        {
            //A copy of what is on disk goes aside before it is overwritten,
            //and is removed again once the write has come through.
            _backup?.Create(path);
            document.Save(path);
            _backup?.Remove(path);
            _recentFiles?.Add(path);
            NotifyPropertyChanged(nameof(WindowTitle));
            return true;
        }
        catch (IOException error)
        {
            await ReportAsync(I18n.Format(
                I18n.Get("Could not write to {filename}:\n{error}"),
                ("filename", path), ("error", error.Message)));
            return false;
        }
    }

    private async Task DoSaveAllAsync()
    {
        foreach (var document in Documents.Documents.Where(d => d.IsModified).ToList())
        {
            await SaveAsync(document, saveAs: false);
        }
    }

    /// <summary>
    /// Closes a document, offering to save it first when it has unsaved
    /// changes.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>Whether it closed.</returns>
    public async Task<bool> CloseAsync(EditorDocument document)
    {
        if (document == null) { return false; }

        if (document.IsModified)
        {
            //Upstream offers Save, Discard and Cancel — a ContentDialog has room
            //for exactly three buttons (board trap 43/50), so all three are here.
            //was previously: Window.ConfirmAsync with a "Do you want to discard
            //your changes?" message and Discard/Cancel only, which made the user
            //cancel, save by hand and close again.
            Func<string, string, Task<CloseAnswer>> ask = Window?.AskSaveDiscardAsync;
            if (ask != null)
            {
                CloseAnswer answer = await ask(
                    I18n.Get("dialog title", "Close Document"),
                    I18n.Format(
                        I18n.Get("The document \"{name}\" has been modified.\n"
                            + "Do you want to save your changes or discard them?"),
                        ("name", document.DocumentName())));
                if (answer == CloseAnswer.Cancel) { return false; }

                if (answer == CloseAnswer.Save
                    && !await SaveAsync(document, saveAs: false))
                {
                    return false;
                }
            }
        }

        DocumentEditorState.For(document, _settings)?.MetaInfo?.Save();
        Documents.CloseDocument(document);

        //Upstream never leaves the window with no document at all.
        if (Documents.Documents.Count == 0)
        {
            Documents.CurrentDocument = Documents.CreateDocument();
        }

        return true;
    }

    private Task DoCloseOtherAsync() => CloseOthersAsync(Documents.CurrentDocument);

    /// <summary>Closes every document but one.</summary>
    /// <param name="keep">The document to leave open.</param>
    /// <returns>The task.</returns>
    public async Task CloseOthersAsync(EditorDocument keep)
    {
        foreach (var document in Documents.Documents.Where(d => d != keep).ToList())
        {
            await CloseAsync(document);
        }
    }

    private async Task DoCloseAllAsync()
    {
        foreach (var document in Documents.Documents.ToList())
        {
            if (!await CloseAsync(document)) { return; }
        }
    }

    private async Task DoReloadAsync()
    {
        EditorDocument document = Documents.CurrentDocument;
        if (document?.Path == null) { return; }

        try
        {
            document.Load(keepUndo: true);
        }
        catch (IOException error)
        {
            await ReportAsync(I18n.Format(
                I18n.Get("Could not read from {filename}:\n{error}"),
                ("filename", document.Path), ("error", error.Message)));
        }
    }

    private async Task DoReloadAllAsync()
    {
        foreach (var document in Documents.Documents.Where(d => d.Path != null).ToList())
        {
            try
            {
                document.Load(keepUndo: true);
            }
            catch (IOException error)
            {
                await ReportAsync(I18n.Format(
                    I18n.Get("Could not read from {filename}:\n{error}"),
                    ("filename", document.Path), ("error", error.Message)));
            }
        }
    }

    private async Task DoQuitAsync()
    {
        foreach (var document in Documents.Documents.ToList())
        {
            if (document.IsModified && !await CloseAsync(document)) { return; }

            DocumentEditorState.For(document, _settings)?.MetaInfo?.Save();
        }

        Engraver?.SaveSettings();
        Window?.Quit?.Invoke();
    }

    /// <summary>
    /// Writes a document to its own file without asking anything.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <remarks>The save-before-engraving preference wants a save that cannot
    /// put a dialog in front of a run the user just started.</remarks>
    private void SaveQuietly(EditorDocument document)
    {
        if (document?.Path == null) { return; }

        try
        {
            _backup?.Create(document.Path);
            document.Save();
            _backup?.Remove(document.Path);
        }
        catch (IOException error)
        {
            StatusText = I18n.Format(
                I18n.Get("Could not write to {filename}:\n{error}"),
                ("filename", document.Path), ("error", error.Message))
                .Replace("\n", " ");
        }
    }

    /// <summary>
    /// Says something went wrong, on the status line and in front of the user.
    /// </summary>
    /// <param name="message">What to say.</param>
    /// <returns>The task.</returns>
    /// <remarks>
    /// //was previously: this went through <c>Window.ConfirmAsync</c>, whose
    /// buttons are Discard and Cancel — so an informational message
    /// ("Could not open {filename}") arrived under a Discard button that reads
    /// as an offer to throw the user's work away. Upstream says all of these
    /// with <c>QMessageBox.critical</c> / <c>.information</c>, which has one OK
    /// button; <see cref="IWindowBridge.AlertAsync"/> is that button, and every
    /// caller of this method is unchanged. The real questions —
    /// <see cref="CloseAsync"/>'s save-or-discard and the lose-your-edits
    /// prompts — keep their own buttons, which is right for them.
    /// </remarks>
    private Task ReportAsync(string message)
    {
        StatusText = message.Replace("\n", " ");
        Func<string, string, Task> alert = Window?.AlertAsync;
        return alert == null
            ? Task.CompletedTask
            : alert(AppInfo.AppName, message);
    }
}
