// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/mainwindow.py (class ActionCollection)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The window's own commands: everything on the File, Edit, View, Window and
/// Help menus that the main window itself performs, rather than a tool.
/// </summary>
/// <remarks>
/// The set matches upstream's <c>main</c> collection, minus the actions the
/// rulings drop: <c>file_print_source</c> (FR5.5 — no printing, ever),
/// <c>file_restart</c> and <c>help_whatsthis</c> (developer/Qt affordances),
/// and <c>help_bugreport</c> (post-v1). Actions whose menus arrive with a
/// later wave are here from the start, disabled, so the shortcut registry and
/// the preferences page see the whole set at once.
/// </remarks>
public sealed class MainActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "main";

    /// <summary>Creates the window's commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public MainActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <inheritdoc/>
    public override string Title => I18n.Get("Main Window");

    #region | File |

    /// <summary>File &gt; New Document.</summary>
    public AppAction FileNew { get; private set; }

    /// <summary>File &gt; Open.</summary>
    public AppAction FileOpen { get; private set; }

    /// <summary>File &gt; Open Recent (the submenu's own entry).</summary>
    public AppAction FileOpenRecent { get; private set; }

    /// <summary>File &gt; Insert from File.</summary>
    public AppAction FileInsertFile { get; private set; }

    /// <summary>Tools &gt; Directories &gt; Open Current Directory.</summary>
    public AppAction FileOpenCurrentDirectory { get; private set; }

    /// <summary>Tools &gt; Directories &gt; Open Command Prompt.</summary>
    public AppAction FileOpenCommandPrompt { get; private set; }

    /// <summary>File &gt; Save Document.</summary>
    public AppAction FileSave { get; private set; }

    /// <summary>File &gt; Save &gt; Save As.</summary>
    public AppAction FileSaveAs { get; private set; }

    /// <summary>File &gt; Save &gt; Save Copy or Selection As.</summary>
    public AppAction FileSaveCopyAs { get; private set; }

    /// <summary>File &gt; Save &gt; Rename/Move File.</summary>
    public AppAction FileRename { get; private set; }

    /// <summary>File &gt; Save &gt; Save All.</summary>
    public AppAction FileSaveAll { get; private set; }

    /// <summary>File &gt; Reload.</summary>
    public AppAction FileReload { get; private set; }

    /// <summary>File &gt; Reload All.</summary>
    public AppAction FileReloadAll { get; private set; }

    /// <summary>File &gt; Check for External Changes.</summary>
    public AppAction FileExternalChanges { get; private set; }

    /// <summary>File &gt; Close Document.</summary>
    public AppAction FileClose { get; private set; }

    /// <summary>File &gt; Close &gt; Close Other Documents.</summary>
    public AppAction FileCloseOther { get; private set; }

    /// <summary>File &gt; Close &gt; Close All Documents.</summary>
    public AppAction FileCloseAll { get; private set; }

    /// <summary>File &gt; Close &gt; Close All Documents and Session.</summary>
    public AppAction FileCloseAllAndSession { get; private set; }

    /// <summary>File &gt; Quit.</summary>
    public AppAction FileQuit { get; private set; }

    /// <summary>File &gt; Import/Export &gt; Export Source as Colored HTML.</summary>
    public AppAction ExportColoredHtml { get; private set; }

    #endregion

    #region | Edit |

    /// <summary>Edit &gt; Undo.</summary>
    public AppAction EditUndo { get; private set; }

    /// <summary>Edit &gt; Redo.</summary>
    public AppAction EditRedo { get; private set; }

    /// <summary>Edit &gt; Cut.</summary>
    public AppAction EditCut { get; private set; }

    /// <summary>Edit &gt; Copy.</summary>
    public AppAction EditCopy { get; private set; }

    /// <summary>Edit &gt; Cut/Copy (advanced) &gt; Copy as Colored HTML.</summary>
    public AppAction EditCopyColoredHtml { get; private set; }

    /// <summary>Edit &gt; Paste.</summary>
    public AppAction EditPaste { get; private set; }

    /// <summary>Edit &gt; Select All.</summary>
    public AppAction EditSelectAll { get; private set; }

    /// <summary>Edit &gt; Select Block.</summary>
    public AppAction EditSelectCurrentToplevel { get; private set; }

    /// <summary>Edit &gt; Select None.</summary>
    public AppAction EditSelectNone { get; private set; }

    /// <summary>Select whole lines upwards.</summary>
    public AppAction EditSelectFullLinesUp { get; private set; }

    /// <summary>Select whole lines downwards.</summary>
    public AppAction EditSelectFullLinesDown { get; private set; }

    /// <summary>Edit &gt; Find.</summary>
    public AppAction EditFind { get; private set; }

    /// <summary>Edit &gt; Find Next.</summary>
    public AppAction EditFindNext { get; private set; }

    /// <summary>Edit &gt; Find Previous.</summary>
    public AppAction EditFindPrevious { get; private set; }

    /// <summary>Edit &gt; Replace.</summary>
    public AppAction EditReplace { get; private set; }

    /// <summary>Edit &gt; Preferences.</summary>
    public AppAction EditPreferences { get; private set; }

    #endregion

    #region | View |

    /// <summary>View &gt; Next Document.</summary>
    public AppAction ViewNextDocument { get; private set; }

    /// <summary>View &gt; Previous Document.</summary>
    public AppAction ViewPreviousDocument { get; private set; }

    /// <summary>View &gt; Wrap Lines.</summary>
    public AppAction ViewWrapLines { get; private set; }

    /// <summary>Scroll the view up without moving the caret.</summary>
    public AppAction ViewScrollUp { get; private set; }

    /// <summary>Scroll the view down without moving the caret.</summary>
    public AppAction ViewScrollDown { get; private set; }

    /// <summary>View &gt; Go to Line.</summary>
    public AppAction ViewGotoLine { get; private set; }

    #endregion

    #region | Window and Help |

    /// <summary>Window &gt; New Window.</summary>
    public AppAction WindowNew { get; private set; }

    /// <summary>Window &gt; Fullscreen.</summary>
    public AppAction WindowFullscreen { get; private set; }

    /// <summary>Help &gt; User Guide.</summary>
    public AppAction HelpManual { get; private set; }

    /// <summary>Help &gt; About.</summary>
    public AppAction HelpAbout { get; private set; }

    #endregion

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        FileNew = Add("file_new").WithIcon("document-new")
            .WithShortcuts(StandardKeys.New);
        FileOpen = Add("file_open").WithIcon("document-open")
            .WithShortcuts(StandardKeys.Open);
        FileOpenRecent = Add("file_open_recent").WithIcon("document-open-recent");
        FileInsertFile = Add("file_insert_file");
        FileOpenCurrentDirectory = Add("file_open_current_directory")
            .WithIcon("folder-open");
        FileOpenCommandPrompt = Add("file_open_command_prompt")
            .WithIcon("utilities-terminal");
        FileSave = Add("file_save").WithIcon("document-save")
            .WithShortcuts(StandardKeys.Save);
        FileSaveAs = Add("file_save_as").WithIcon("document-save-as")
            .WithShortcuts(StandardKeys.SaveAs);
        FileSaveCopyAs = Add("file_save_copy_as").WithIcon("document-save-as");
        FileRename = Add("file_rename").WithIcon("document-rename");
        FileSaveAll = Add("file_save_all").WithIcon("document-save-all");
        FileReload = Add("file_reload").WithIcon("reload");
        FileReloadAll = Add("file_reload_all").WithIcon("reload-all");
        FileExternalChanges = Add("file_external_changes");
        FileClose = Add("file_close").WithIcon("document-close")
            .WithShortcuts(StandardKeys.Close);
        FileCloseOther = Add("file_close_other");
        FileCloseAll = Add("file_close_all");
        FileCloseAllAndSession = Add("file_close_all_and_session");
        FileQuit = Add("file_quit").WithIcon("application-exit")
            .WithShortcuts(StandardKeys.Quit);
        ExportColoredHtml = Add("export_colored_html");

        EditUndo = Add("edit_undo").WithIcon("edit-undo")
            .WithShortcuts(StandardKeys.Undo);
        EditRedo = Add("edit_redo").WithIcon("edit-redo")
            .WithShortcuts(StandardKeys.Redo);
        EditCut = Add("edit_cut").WithIcon("edit-cut")
            .WithShortcuts(StandardKeys.Cut);
        EditCopy = Add("edit_copy").WithIcon("edit-copy")
            .WithShortcuts(StandardKeys.Copy);
        EditCopyColoredHtml = Add("edit_copy_colored_html");
        EditPaste = Add("edit_paste").WithIcon("edit-paste")
            .WithShortcuts(StandardKeys.Paste);
        EditSelectAll = Add("edit_select_all").WithIcon("edit-select-all")
            .WithShortcuts(StandardKeys.SelectAll);
        EditSelectCurrentToplevel = Add("edit_select_current_toplevel")
            .WithIcon("edit-select").WithShortcut("Ctrl+Shift+B");
        EditSelectNone = Add("edit_select_none").WithShortcut("Ctrl+Shift+A");
        EditSelectFullLinesUp = Add("edit_select_full_lines_up")
            .WithShortcut("Ctrl+Shift+Up");
        EditSelectFullLinesDown = Add("edit_select_full_lines_down")
            .WithShortcut("Ctrl+Shift+Down");
        EditFind = Add("edit_find").WithIcon("edit-find")
            .WithShortcuts(StandardKeys.Find);
        EditFindNext = Add("edit_find_next").WithIcon("go-down-search")
            .WithShortcuts(StandardKeys.FindNext);
        EditFindPrevious = Add("edit_find_previous").WithIcon("go-up-search")
            .WithShortcuts(StandardKeys.FindPrevious);
        EditReplace = Add("edit_replace").WithIcon("edit-find-replace")
            .WithShortcuts(StandardKeys.Replace);

        //Upstream forces Ctrl+, because Qt's Preferences standard key is empty
        //on X11 and Windows; the same reasoning applies here.
        EditPreferences = Add("edit_preferences").WithIcon("preferences-system")
            .WithShortcut("Ctrl+,");

        ViewNextDocument = Add("view_next_document").WithIcon("go-next")
            .WithShortcuts(StandardKeys.Forward);
        ViewPreviousDocument = Add("view_previous_document").WithIcon("go-previous")
            .WithShortcuts(StandardKeys.Back);
        ViewWrapLines = Add("view_wrap_lines").AsToggle();
        ViewScrollUp = Add("view_scroll_up").WithShortcut("Ctrl+Up");
        ViewScrollDown = Add("view_scroll_down").WithShortcut("Ctrl+Down");
        ViewGotoLine = Add("view_goto_line").WithShortcut("Ctrl+Alt+G");

        WindowNew = Add("window_new").WithIcon("window-new");
        WindowFullscreen = Add("window_fullscreen").WithIcon("view-fullscreen")
            .AsToggle()
            .WithShortcuts(new[]
            {
                KeySequence.Parse("Ctrl+Shift+F"),
                KeySequence.Parse("F11"),
            });

        HelpManual = Add("help_manual").WithIcon("help-contents")
            .WithShortcuts(StandardKeys.HelpContents);
        HelpAbout = Add("help_about").WithIcon("help-about");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        FileNew.Text = I18n.Get("action: new document", "&New Document");
        FileNew.IconText = I18n.Get("action: new document", "New");
        FileOpen.Text = I18n.Get("&Open...");
        FileOpenRecent.Text = I18n.Get("Open &Recent");
        FileInsertFile.Text = I18n.Get("Insert from &File...");
        FileInsertFile.ToolTip =
            I18n.Get("Insert the contents of a file at the current cursor position.");
        FileOpenCurrentDirectory.Text = I18n.Get("Open Current Directory");
        FileOpenCommandPrompt.Text = I18n.Get("Open Command Prompt");
        FileSave.Text = I18n.Get("&Save Document");
        FileSave.IconText = I18n.Get("Save");
        FileSaveAs.Text = I18n.Get("Save &As...");
        FileSaveCopyAs.Text = I18n.Get("Save Copy or Selection As...");
        FileRename.Text = I18n.Get("&Rename/Move File...");
        FileSaveAll.Text = I18n.Get("Save All");
        FileReload.Text = I18n.Get("Re&load");
        FileReloadAll.Text = I18n.Get("Reload All");
        FileExternalChanges.Text = I18n.Get("Check for External Changes...");
        FileExternalChanges.ToolTip = I18n.Get(
            "Opens a window to check whether open documents were changed or "
            + "deleted by other programs.");
        FileClose.Text = I18n.Get("&Close Document");
        FileClose.IconText = I18n.Get("Close");
        FileCloseOther.Text = I18n.Get("Close Other Documents");
        FileCloseAll.Text = I18n.Get("Close All Documents");
        FileCloseAll.ToolTip =
            I18n.Get("Closes all documents but preserves the current session.");
        FileCloseAllAndSession.Text = I18n.Get("Close All Documents and Session");
        FileCloseAllAndSession.ToolTip =
            I18n.Get("Closes all documents and leaves the current session.");
        FileQuit.Text = I18n.Get("&Quit");

        ExportColoredHtml.Text = I18n.Get("Export Source as Colored &HTML...");

        EditUndo.Text = I18n.Get("&Undo");
        EditRedo.Text = I18n.Get("Re&do");
        EditCut.Text = I18n.Get("Cu&t");
        EditCopy.Text = I18n.Get("&Copy");
        EditCopyColoredHtml.Text = I18n.Get("Copy as Colored &HTML");
        EditPaste.Text = I18n.Get("&Paste");
        EditSelectAll.Text = I18n.Get("Select &All");
        EditSelectCurrentToplevel.Text = I18n.Get("Select &Block");
        EditSelectNone.Text = I18n.Get("Select &None");
        EditSelectFullLinesUp.Text = I18n.Get("Select Whole Lines Up");
        EditSelectFullLinesDown.Text = I18n.Get("Select Whole Lines Down");
        EditFind.Text = I18n.Get("&Find...");
        EditFindNext.Text = I18n.Get("Find Ne&xt");
        EditFindPrevious.Text = I18n.Get("Find Pre&vious");
        EditReplace.Text = I18n.Get("&Replace...");
        EditPreferences.Text = I18n.Get("Pr&eferences...");

        ViewNextDocument.Text = I18n.Get("&Next Document");
        ViewPreviousDocument.Text = I18n.Get("&Previous Document");
        ViewWrapLines.Text = I18n.Get("Wrap &Lines");
        ViewScrollUp.Text = I18n.Get("Scroll Up");
        ViewScrollDown.Text = I18n.Get("Scroll Down");
        ViewGotoLine.Text = I18n.Get("&Go to Line...");

        WindowNew.Text = I18n.Get("New &Window");
        WindowFullscreen.Text = I18n.Get("&Fullscreen");

        HelpManual.Text = I18n.Get("&User Guide");
        HelpAbout.Text = I18n.Format(
            I18n.Get("&About {appname}..."), ("appname", AppInfo.AppName));
    }
}
