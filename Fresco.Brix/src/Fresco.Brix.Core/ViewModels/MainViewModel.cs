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

    /// <summary>
    /// Gets or sets the "pick a path to export to" dialog: a suggested name, the
    /// file type's label and its suffix.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PickSavePathAsync"/> because that one always
    /// offers a LilyPort source file, and an export is never one.
    /// </remarks>
    Func<string, string, string, Task<string>> PickExportPathAsync { get; set; }

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
    private MidiPlayerService _midiPlayer;
    private ScoreWizardDialog _scoreWizard;
    private ManualLibrary _manuals;

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
        MidiActions = new MidiActions(_settings);
        DocumentationActions = new DocumentationActions(_settings);

        //The desktop's own viewers, file manager and terminal. It reads the
        //user's configured helper commands out of the same store W12's
        //preferences page will write them to.
        Helpers = new HelperApplications(_settings);

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
        ActionManager.Add(MidiActions);
        ActionManager.Add(DocumentationActions);
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
        "file_external_changes", "file_close_all_and_session",
        //was previously: "export_colored_html", "edit_copy_colored_html" — both
        //waited on the HTML half of ly.colorize, which W11 ported.
        "edit_select_current_toplevel",
        "edit_select_full_lines_up", "edit_select_full_lines_down",
        "edit_preferences", "view_goto_line", "window_new",
        "help_manual", "help_about",
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

    private Export.ColoredHtmlOptions ReadHtmlOptions(bool inline)
        => new Export.ColoredHtmlOptions
        {
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
