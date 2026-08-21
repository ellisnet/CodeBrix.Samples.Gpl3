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
using Fresco.Brix.Engrave;
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

namespace Fresco.Brix.ViewModels;

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

    /// <summary>Gets or sets the accessor for the editor in focus.</summary>
    Func<EditorView> ActiveView { get; set; }

    /// <summary>Gets or sets the fullscreen switch.</summary>
    Action<bool> SetFullScreen { get; set; }

    /// <summary>Gets or sets the "close the window" action.</summary>
    Action Quit { get; set; }

    /// <summary>Gets or sets the yes/no question, used before losing edits.</summary>
    Func<string, string, Task<bool>> ConfirmAsync { get; set; }
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
    private ScoreWizardDialog _scoreWizard;

    /// <summary>Creates the window's state.</summary>
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Fresco.Brix main view model startup.");

        _settings = GetService<SettingsStore>();
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
        PitchActions = new PitchActions(_settings);
        RestActions = new RestActions(_settings);
        RhythmActions = new RhythmActions(_settings);
        LyricsActions = new LyricsActions(_settings);
        ScoreWizardActions = new ScoreWizardActions(_settings);

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
        ActionManager.Add(PitchActions);
        ActionManager.Add(RestActions);
        ActionManager.Add(RhythmActions);
        ActionManager.Add(LyricsActions);
        ActionManager.Add(ScoreWizardActions);
        ActionManager.Add(Browser.Actions);
        ActionManager.Add(SnippetShortcuts);
        ActionManager.Add(SessionManager.Actions);

        //What the app remembers per document. Declared before any document is
        //opened, because a value not declared is a value not stored.
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

    /// <summary>
    /// Gets the Score Wizard, built the first time it is asked for.
    /// </summary>
    /// <remarks>One per window, kept: what the user assembled is still there
    /// when they open it again, which is upstream's own arrangement.</remarks>
    public ScoreWizardDialog ScoreWizard
        => _scoreWizard ??= new ScoreWizardDialog(_settings);

    /// <summary>Gets the order documents were last active in.</summary>
    public HistoryManager History { get; }

    /// <summary>Gets the back/forward place history.</summary>
    public BrowserInterface Browser { get; }

    /// <summary>Gets the snippets.</summary>
    public SnippetLibrary SnippetLibrary { get; }

    /// <summary>Gets the snippets' keyboard shortcuts.</summary>
    public SnippetShortcuts SnippetShortcuts { get; }

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
    /// <returns>The task.</returns>
    public async Task StartAsync(IEnumerable<string> paths = null)
    {
        bool openedAny = false;
        foreach (var path in paths ?? Array.Empty<string>())
        {
            if (await OpenPathAsync(path))
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
    /// <returns>Whether it opened.</returns>
    public async Task<bool> OpenPathAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) { return false; }

        try
        {
            EditorDocument document = Documents.OpenDocument(path);
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
        Actions.FileQuit.AsyncHandler = DoQuitAsync;

        Actions.EditUndo.Handler = () => WithEditor(e => e.Editor.Undo());
        Actions.EditRedo.Handler = () => WithEditor(e => e.Editor.Redo());
        Actions.EditCut.Handler = () => WithEditor(e => e.Editor.Cut());
        Actions.EditCopy.Handler = () => WithEditor(e => e.Editor.Copy());
        Actions.EditPaste.Handler = () => WithEditor(e => e.Editor.Paste());
        Actions.EditSelectAll.Handler = () => WithEditor(e => e.Editor.SelectAll());
        Actions.EditSelectNone.Handler
            = () => WithEditor(e => e.Editor.TextArea.ClearSelection());

        Actions.ViewNextDocument.Handler = () => StepDocument(1);
        Actions.ViewPreviousDocument.Handler = () => StepDocument(-1);
        Actions.ViewWrapLines.Handler
            = () => WithEditor(e => e.Editor.WordWrap = Actions.ViewWrapLines.IsChecked);
        Actions.ViewScrollUp.Handler = () => WithEditor(e => e.Editor.LineUp());
        Actions.ViewScrollDown.Handler = () => WithEditor(e => e.Editor.LineDown());

        Actions.WindowFullscreen.Handler
            = () => Window?.SetFullScreen?.Invoke(Actions.WindowFullscreen.IsChecked);

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
        "file_external_changes", "file_close_all_and_session",
        "file_open_current_directory", "file_open_command_prompt",
        "export_colored_html", "edit_copy_colored_html",
        "edit_select_current_toplevel",
        "edit_select_full_lines_up", "edit_select_full_lines_down",
        "edit_preferences", "view_goto_line", "window_new",
        "help_manual", "help_about",
    };

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
            Func<string, string, Task<bool>> confirm = Window?.ConfirmAsync;
            if (confirm != null)
            {
                bool discard = await confirm(
                    I18n.Get("Close Document"),
                    I18n.Format(
                        I18n.Get("The document \"{name}\" has been modified.\n"
                            + "Do you want to discard your changes?"),
                        ("name", document.DocumentName())));
                if (!discard) { return false; }
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

    private Task ReportAsync(string message)
    {
        StatusText = message.Replace("\n", " ");
        Func<string, string, Task<bool>> confirm = Window?.ConfirmAsync;
        return confirm == null
            ? Task.CompletedTask
            : confirm(AppInfo.AppName, message);
    }
}
