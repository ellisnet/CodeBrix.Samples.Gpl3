// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Completion;
using Fresco.Brix.Documents;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using Fresco.Brix.Sessions;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/menu.py and documentmenu.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Builds the window's menu bar from the command collections.
/// <para>
/// The menus are built once and then follow their actions: an action that is
/// re-translated, disabled or re-checked updates its menu entry in place, so a
/// language change or a state change never needs the menu rebuilt.
/// </para>
/// </summary>
/// <remarks>
/// W5 completed the bar: Snippets, Session, the Tools transformation submenus
/// and the Edit/View entries that go with them are all built here now. The
/// commands that belong to later waves are created and shown but disabled, so
/// the bar has its finished shape from the start; W13 audits it against the
/// upstream source menu by menu.
/// </remarks>
public static class MenuBuilder
{
    /// <summary>Builds the whole menu bar.</summary>
    /// <param name="menuBar">The bar to fill.</param>
    /// <param name="main">The window's own commands.</param>
    /// <param name="views">The Window menu's view commands.</param>
    /// <param name="panels">The tool panels, for the Tools menu.</param>
    /// <param name="documents">The open documents, for the Document menu.</param>
    /// <param name="recentFiles">The recent files, for File &gt; Open Recent.</param>
    /// <param name="openRecent">What to do when a recent file is picked.</param>
    /// <param name="sideBar">The editor-margin commands, for the View menu.</param>
    /// <param name="engrave">The engraving commands, for the LilyPond menu.</param>
    /// <param name="engravedDocument">Which document's generated files the
    /// LilyPond menu lists, or null for no such submenu.</param>
    /// <param name="openGeneratedFile">What to do with a generated file the
    /// user picks.</param>
    /// <param name="musicView">The Music View's commands, for the Music menu.</param>
    /// <param name="documentActions">The transforming commands, for the Tools
    /// and Edit menus.</param>
    /// <param name="bookmarks">The marked-line commands, for the View menu.</param>
    /// <param name="completion">The autocomplete commands, for the Tools menu.</param>
    /// <param name="browser">The Back and Forward commands, for the View menu.</param>
    /// <param name="snippets">The snippet library, for the Snippets menu.</param>
    /// <param name="snippetActions">The Snippets panel's commands.</param>
    /// <param name="applySnippet">What picking a snippet does.</param>
    /// <param name="sessionStore">The stored sessions, for the Session menu.</param>
    /// <param name="sessionActions">The session commands.</param>
    /// <param name="startSession">What picking a session does.</param>
    /// <param name="pitch">The pitch commands, for Tools &gt; Musical
    /// Transformations.</param>
    /// <param name="rest">The rest commands, for the same submenu.</param>
    /// <param name="rhythm">The rhythm commands, for the same submenu.</param>
    /// <param name="lyrics">The lyric commands, for the same submenu.</param>
    /// <param name="pitchLanguage">The current document's pitch-name language,
    /// which the language submenu ticks.</param>
    /// <param name="changePitchLanguage">What picking a pitch-name language
    /// does.</param>
    /// <param name="scoreWizard">The Score Wizard's commands, for File &gt;
    /// New.</param>
    /// <param name="documentation">The documentation browser's commands, for
    /// the Help menu.</param>
    /// <param name="editorCommands">FD10's native editor commands, for the
    /// Snippets menu.</param>
    /// <param name="fonts">The Document Fonts command, for the Tools menu.</param>
    /// <param name="fileImport">The import commands, for File &gt;
    /// Import/Export.</param>
    /// <param name="matchingPair">The matching-token commands, for the View
    /// menu.</param>
    /// <param name="logActions">The log panel's error-stepping commands, which
    /// upstream also puts at the foot of the View menu.</param>
    /// <param name="stickyDocument">Reads the document the engraver is pinned
    /// to, for the Documents menu's "[always engraved]" mark.</param>
    /// <param name="hasSelection">Reads whether the editor has a selection, so
    /// a snippet that declares <c>selection: yes</c> can be greyed without
    /// one.</param>

    public static void Build(
        MenuBar menuBar,
        MainActions main,
        ViewActions views = null,
        PanelManager panels = null,
        DocumentManager documents = null,
        RecentFiles recentFiles = null,
        Action<string> openRecent = null,
        SideBarActions sideBar = null,
        EngraveActions engrave = null,
        Func<EditorDocument> engravedDocument = null,
        Action<string> openGeneratedFile = null,
        MusicViewActions musicView = null,
        DocumentActions documentActions = null,
        BookmarkActions bookmarks = null,
        CompletionActions completion = null,
        BrowserActions browser = null,
        SnippetLibrary snippets = null,
        SnippetToolActions snippetActions = null,
        Action<string> applySnippet = null,
        SessionStore sessionStore = null,
        SessionActions sessionActions = null,
        Action<string> startSession = null,
        PitchActions pitch = null,
        RestActions rest = null,
        RhythmActions rhythm = null,
        LyricsActions lyrics = null,
        Func<string> pitchLanguage = null,
        Action<string> changePitchLanguage = null,
        ScoreWizardActions scoreWizard = null,
        DocumentationActions documentation = null,
        EditorCommandActions editorCommands = null,
        FontsActions fonts = null,
        FileImportActions fileImport = null,
        MatchingPairActions matchingPair = null,
        LogActions logActions = null,
        Func<EditorDocument> stickyDocument = null,
        Func<bool> hasSelection = null)
    {
        if (menuBar == null) { throw new ArgumentNullException(nameof(menuBar)); }

        if (main == null) { throw new ArgumentNullException(nameof(main)); }

        //was previously: File Edit View Music LilyPort Tools Snippets Session
        //Documents Window Help. Upstream's own sequence (menu.py createMenus) is
        //File Edit View Music Snippets LilyPond Tools Documents Window Session
        //[Git] Help — Snippets sits BEFORE the engine menu and Session AFTER
        //Window. The Git menu is ruled out by FR5.7.
        menuBar.Items.Clear();
        menuBar.Items.Add(FileMenu(
            main, recentFiles, openRecent, snippets, snippetActions, applySnippet,
            scoreWizard, fileImport));
        menuBar.Items.Add(EditMenu(main, documentActions, snippetActions));
        menuBar.Items.Add(ViewMenu(
            main, sideBar, bookmarks, browser, documentActions, matchingPair,
            logActions));
        if (musicView != null)
        {
            menuBar.Items.Add(MusicMenu(musicView));
        }

        if (snippets != null && snippetActions != null)
        {
            menuBar.Items.Add(SnippetMenu(
                snippets, snippetActions, applySnippet, editorCommands, hasSelection));
        }

        if (engrave != null)
        {
            menuBar.Items.Add(LilyPortMenu(
                engrave, engravedDocument, openGeneratedFile));
        }

        if (panels != null)
        {
            menuBar.Items.Add(ToolsMenu(
                panels,
                documentActions,
                completion,
                pitch,
                rest,
                rhythm,
                lyrics,
                pitchLanguage,
                changePitchLanguage,
                main,
                fonts));
        }

        if (documents != null)
        {
            menuBar.Items.Add(DocumentMenu(documents, stickyDocument));
        }

        menuBar.Items.Add(WindowMenu(main, views));
        if (sessionStore != null && sessionActions != null)
        {
            menuBar.Items.Add(SessionMenu(sessionStore, sessionActions, startSession));
        }

        menuBar.Items.Add(HelpMenu(main, documentation));
    }

    /// <summary>
    /// The text a menu shows for a label.
    /// <para>
    /// Action texts keep upstream's <c>&amp;</c> accelerator markers verbatim,
    /// because the msgid a translation is keyed by includes them. The platform
    /// has no such convention, so the marker is stripped on the way to the
    /// screen rather than out of the string.
    /// </para>
    /// <para>
    /// ⚠ AND THEREFORE Alt+F DOES NOTHING. Qt turns the marker in "&amp;File"
    /// into a live menu-bar mnemonic for free; the platform cannot, on any
    /// Skia head. <c>MenuBarItem</c> was ported WITH its access-key plumbing,
    /// but the event it hangs on — <c>UIElement.AccessKeyInvoked</c> — is a
    /// compile-time stub whose accessors discard the handler, nothing anywhere
    /// raises it, and <c>AccessKeyManager</c> throws or no-ops throughout. So
    /// the choice was not "strip the marker or keep it": leaving it in would
    /// render a literal "&amp;File", because <c>MenuBarItem.Title</c> does no
    /// marker parsing of its own. Keeping the marker in the msgid is right and
    /// stays — the msgid is the translation key. Three upstream behaviours go
    /// with the mnemonic: the dynamic accelerators
    /// <c>qutil.addAccelerators</c> assigns to recent files, session groups,
    /// generated files and pitch languages, and the per-document accelerator
    /// <c>documentmenu.py</c> keeps for a document's lifetime. All of it is one
    /// finding, recorded as a v1 divergence: a CodeBrix.Platform follow-up asks
    /// for <c>AccessKeyInvoked</c> on Skia (or a public
    /// <c>ShowMenuFlyout</c>), and an app-side substitute — catch Alt+letter on
    /// the window and open the matching flyout — is a post-v1 candidate on
    /// board §9.
    /// </para>
    /// </summary>
    /// <param name="text">The label, with markers.</param>
    /// <returns>The display text.</returns>
    public static string Display(string text)
        => ActionCollectionManager.RemoveAccelerator(text);

    /// <summary>Makes a menu entry for an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The entry, following the action's text and state.</returns>
    public static MenuFlyoutItemBase ItemFor(AppAction action)
    {
        if (action.IsCheckable)
        {
            ToggleMenuFlyoutItem toggle = new ToggleMenuFlyoutItem
            {
                Text = Display(action.Text),
                IsChecked = action.IsChecked,
                IsEnabled = action.IsEnabled,
            };
            toggle.Click += (_, _) => action.Trigger();
            Follow(action, toggle, () =>
            {
                toggle.Text = Display(action.Text);
                toggle.IsChecked = action.IsChecked;
                toggle.IsEnabled = action.IsEnabled;
            });
            return toggle;
        }

        MenuFlyoutItem item = new MenuFlyoutItem
        {
            Text = Display(action.Text),
            IsEnabled = action.IsEnabled,
        };
        item.Click += (_, _) => action.Trigger();
        Follow(action, item, () =>
        {
            item.Text = Display(action.Text);
            item.IsEnabled = action.IsEnabled;
        });
        return item;
    }

    /// <summary>
    /// Fills a menu with upstream's <c>menu_file_save</c> — Save As, Save Copy
    /// or Selection As, Rename/Move, Save as Template and Save All.
    /// </summary>
    /// <param name="items">The menu's items, which are replaced.</param>
    /// <param name="main">The window's own commands.</param>
    /// <param name="snippetActions">The Snippets panel's commands, or null.</param>
    /// <remarks>
    /// It is a filler rather than a menu because it has TWO callers: the File
    /// menu, and the main toolbar's Save button when
    /// <c>verbose_toolbuttons</c> is set — which is exactly why upstream makes
    /// it a function too.
    /// </remarks>
    public static void FillSave(
        IList<MenuFlyoutItemBase> items,
        MainActions main,
        SnippetToolActions snippetActions)
    {
        if (items == null || main == null) { return; }

        Empty(items);
        items.Add(ItemFor(main.FileSaveAs));
        items.Add(ItemFor(main.FileSaveCopyAs));
        items.Add(ItemFor(main.FileRename));
        if (snippetActions != null)
        {
            items.Add(ItemFor(snippetActions.SaveAsTemplate));
        }

        items.Add(ItemFor(main.FileSaveAll));
    }

    /// <summary>
    /// Fills a menu with upstream's <c>menu_file_close</c> — Close Other, Close
    /// All and Close All and Session.
    /// </summary>
    /// <param name="items">The menu's items, which are replaced.</param>
    /// <param name="main">The window's own commands.</param>
    public static void FillClose(IList<MenuFlyoutItemBase> items, MainActions main)
    {
        if (items == null || main == null) { return; }

        Empty(items);
        items.Add(ItemFor(main.FileCloseOther));
        items.Add(ItemFor(main.FileCloseAll));
        items.Add(ItemFor(main.FileCloseAllAndSession));
    }

    /// <summary>Fills a menu with the recently opened files.</summary>
    /// <param name="items">The menu's items, which are replaced.</param>
    /// <param name="recentFiles">The list.</param>
    /// <param name="openRecent">What picking one does.</param>
    /// <remarks>
    /// Upstream's <c>menu_recent_files</c> has two callers as well: the File
    /// menu, and the main toolbar's Open button, which carries it as a
    /// pull-down whatever <c>verbose_toolbuttons</c> says
    /// (<c>mainwindow.createToolBars</c>).
    /// </remarks>
    public static void FillRecent(
        IList<MenuFlyoutItemBase> items,
        RecentFiles recentFiles,
        Action<string> openRecent)
    {
        if (items == null) { return; }

        Empty(items);
        foreach (var path in recentFiles?.Paths() ?? Array.Empty<string>())
        {
            //was previously: the bare file name. Upstream's entry text is
            //"<basename>  (<homified dirname>)" (mainwindow.py), which is
            //what tells two files of the same name apart at a glance; the
            //full path stays on the tooltip, which upstream does not have
            //and which costs nothing.
            MenuFlyoutItem entry = new MenuFlyoutItem
            {
                Text = System.IO.Path.GetFileName(path) + "  ("
                    + PathUtil.Homify(System.IO.Path.GetDirectoryName(path))
                    + ")",
            };
            ToolTipService.SetToolTip(entry, path);
            entry.Click += (_, _) => openRecent?.Invoke(path);
            items.Add(entry);
        }
    }

    /// <summary>
    /// Fills a menu with the New group: the wizard, then the template
    /// snippets, then Manage Templates.
    /// </summary>
    /// <param name="items">The menu's items, which are replaced.</param>
    /// <param name="snippets">The snippet library.</param>
    /// <param name="actions">The Snippets panel's commands.</param>
    /// <param name="apply">What picking a template does.</param>
    /// <param name="scoreWizard">The Score Wizard's commands, or null.</param>
    /// <remarks>
    /// Upstream's <c>snippet.menu.TemplateMenu</c>, which the File menu shows
    /// and which the main toolbar's New button hangs off itself when
    /// <c>verbose_toolbuttons</c> is set.
    /// </remarks>
    public static void FillTemplates(
        IList<MenuFlyoutItemBase> items,
        SnippetLibrary snippets,
        SnippetToolActions actions,
        Action<string> apply,
        ScoreWizardActions scoreWizard)
    {
        if (items == null || snippets == null || actions == null) { return; }

        Empty(items);

        //The wizard opens this menu, exactly as it does upstream: it is
        //the other way of starting a document from nothing.
        if (scoreWizard != null)
        {
            items.Add(ItemFor(scoreWizard.ScoreWizard));
            items.Add(ItemFor(scoreWizard.ScoreWizardFromCurrent));
            items.Add(new MenuFlyoutSeparator());
        }

        foreach (var (_, names) in SnippetFilter.Grouped(snippets, "template"))
        {
            foreach (var name in names)
            {
                items.Add(SnippetItem(snippets, name, apply));
            }

            items.Add(new MenuFlyoutSeparator());
        }

        items.Add(ItemFor(actions.ManageTemplates));
    }

    /// <summary>Empties a menu from the END (board trap 39).</summary>
    /// <param name="items">The items.</param>
    /// <remarks>A menu that has been opened once ignores <c>Clear()</c>;
    /// removing from the end is what it does honour.</remarks>
    private static void Empty(IList<MenuFlyoutItemBase> items)
    {
        while (items.Count > 0)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private static MenuBarItem FileMenu(
        MainActions main,
        RecentFiles recentFiles,
        Action<string> openRecent,
        SnippetLibrary snippets,
        SnippetToolActions snippetActions,
        Action<string> applySnippet,
        ScoreWizardActions scoreWizard,
        FileImportActions fileImport)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&File")),
        };

        menu.Items.Add(ItemFor(main.FileNew));
        if (snippets != null && snippetActions != null)
        {
            menu.Items.Add(TemplateMenu(
                snippets, snippetActions, applySnippet, scoreWizard));
        }
        else if (scoreWizard != null)
        {
            menu.Items.Add(Submenu(
                I18n.Get("New"),
                scoreWizard.ScoreWizard,
                scoreWizard.ScoreWizardFromCurrent));
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.FileOpen));
        menu.Items.Add(RecentMenu(main, recentFiles, openRecent));
        menu.Items.Add(ItemFor(main.FileClose));
        MenuFlyoutSubItem closeMenu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("submenu title", "Close")),
        };
        FillClose(closeMenu.Items, main);
        menu.Items.Add(closeMenu);
        menu.Items.Add(ItemFor(main.FileSave));

        //was previously: four entries, with "Save as Template..." standing on
        //its own further down the File menu. Upstream puts it FOURTH of five in
        //this submenu (menu.py menu_file_save), between Rename and Save All.
        //was previously: the five entries were added here inline. Upstream's
        //`menu_file_save' is a FUNCTION because the main toolbar's Save button
        //hangs the same menu off itself when `verbose_toolbuttons' is set
        //(mainwindow.settingsChanged), so it is one here too — MainToolbar
        //calls the same filler.
        MenuFlyoutSubItem saveMenu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("submenu title", "Save")),
        };
        FillSave(saveMenu.Items, main, snippetActions);
        menu.Items.Add(saveMenu);
        menu.Items.Add(new MenuFlyoutSeparator());
        //was previously: the submenu carried the export half alone, because
        //decision FD1 had put musicxml2ly, midi2ly and abc2ly after v1. W-IMPORT
        //brings them, so the submenu is upstream's own again — its four import
        //entries, its two separators and its exports, in upstream's order.
        //⚠ Upstream hides Export MusicXML and Export Audio behind its
        //experimental-features toggle; here they are plain menu entries — the
        //MusicXML writer is verified against python-ly's own output over 81
        //documents, and the audio export is in-process rather than a TiMidity
        //subprocess that may not be installed. Ruled as FD14.
        menu.Items.Add(ImportExportMenu(main, fileImport));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.FileReload));
        menu.Items.Add(ItemFor(main.FileReloadAll));
        menu.Items.Add(ItemFor(main.FileExternalChanges));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.FileQuit));
        return menu;
    }

    /// <summary>Builds the File &gt; Import/Export submenu.</summary>
    /// <param name="main">The window's commands.</param>
    /// <param name="fileImport">The import commands, or null before they exist.</param>
    /// <returns>The submenu.</returns>
    /// <remarks>
    /// Upstream's <c>menu_file_import_export</c>, entry for entry and separator
    /// for separator. ⚠ The export order is upstream's own: Export Audio comes
    /// before Export MusicXML, and Export Source as Colored HTML is last.
    /// </remarks>
    private static MenuFlyoutSubItem ImportExportMenu(
        MainActions main, FileImportActions fileImport)
    {
        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("submenu title", "&Import/Export")),
        };

        if (fileImport != null)
        {
            submenu.Items.Add(ItemFor(fileImport.ImportAny));
            submenu.Items.Add(new MenuFlyoutSeparator());
            submenu.Items.Add(ItemFor(fileImport.ImportMusicXml));
            submenu.Items.Add(ItemFor(fileImport.ImportMidi));
            submenu.Items.Add(ItemFor(fileImport.ImportAbc));
            submenu.Items.Add(new MenuFlyoutSeparator());
        }

        submenu.Items.Add(ItemFor(main.ExportAudio));
        submenu.Items.Add(ItemFor(main.ExportMusicXml));
        submenu.Items.Add(ItemFor(main.ExportColoredHtml));
        return submenu;
    }

    private static MenuFlyoutSubItem RecentMenu(
        MainActions main, RecentFiles recentFiles, Action<string> openRecent)
    {
        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
        {
            Text = Display(main.FileOpenRecent.Text),
        };

        void Fill()
        {
            FillRecent(submenu.Items, recentFiles, openRecent);
            submenu.IsEnabled = submenu.Items.Count > 0;
        }

        //The list is rebuilt each time the menu opens, so it is right however
        //the recent files changed since the last look.
        submenu.Loaded += (_, _) => Fill();
        Fill();
        return submenu;
    }

    /// <summary>The Edit menu.</summary>
    /// <param name="main">The window's own commands.</param>
    /// <param name="documentActions">The transforming commands.</param>
    /// <param name="snippetActions">The Snippets panel's commands, which own
    /// <c>copy_to_snippet</c>.</param>
    /// <returns>The menu.</returns>
    private static MenuBarItem EditMenu(
        MainActions main,
        DocumentActions documentActions,
        SnippetToolActions snippetActions)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&Edit")),
        };

        menu.Items.Add(ItemFor(main.EditUndo));
        menu.Items.Add(ItemFor(main.EditRedo));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.EditCut));
        menu.Items.Add(ItemFor(main.EditCopy));
        menu.Items.Add(ItemFor(main.EditPaste));
        menu.Items.Add(ItemFor(main.FileInsertFile));

        //was previously: edit_cut_assign and edit_move_to_include_file flattened
        //into the Edit menu, with copy_to_snippet and edit_copy_colored_html
        //having no menu entry at all — both were built, wired and
        //enablement-tracked, and neither was reachable. Upstream's own
        //`menu_edit_cut' submenu holds all four, in this order (menu.py).
        menu.Items.Add(CutCopyMenu(main, documentActions, snippetActions));

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.EditSelectAll));
        menu.Items.Add(ItemFor(main.EditSelectCurrentToplevel));
        menu.Items.Add(ItemFor(main.EditSelectNone));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.EditFind));
        menu.Items.Add(ItemFor(main.EditFindNext));
        menu.Items.Add(ItemFor(main.EditFindPrevious));
        menu.Items.Add(ItemFor(main.EditReplace));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.EditPreferences));
        return menu;
    }

    /// <summary>
    /// Edit &gt; Cut/Copy (advanced) — the four commands that take the
    /// selection somewhere other than the clipboard.
    /// </summary>
    /// <param name="main">The window's own commands.</param>
    /// <param name="documentActions">The transforming commands.</param>
    /// <param name="snippetActions">The Snippets panel's commands.</param>
    /// <returns>The submenu.</returns>
    /// <remarks>Upstream's <c>menu_edit_cut</c>. Note the msgid's context is
    /// "menu title", not "submenu title" — upstream builds it with the former
    /// and the catalogs are keyed by it.</remarks>
    private static MenuFlyoutSubItem CutCopyMenu(
        MainActions main,
        DocumentActions documentActions,
        SnippetToolActions snippetActions)
    {
        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("menu title", "Cut/Copy (advanced)")),
        };

        if (documentActions != null)
        {
            submenu.Items.Add(ItemFor(documentActions.EditCutAssign));
            submenu.Items.Add(ItemFor(documentActions.EditMoveToIncludeFile));
        }

        if (snippetActions != null)
        {
            submenu.Items.Add(ItemFor(snippetActions.CopyToSnippet));
        }

        submenu.Items.Add(ItemFor(main.EditCopyColoredHtml));
        return submenu;
    }

    /// <summary>The View menu.</summary>
    /// <param name="main">The window's own commands.</param>
    /// <param name="sideBar">The editor-margin commands.</param>
    /// <param name="bookmarks">The marked-line commands.</param>
    /// <param name="browser">The Back and Forward commands.</param>
    /// <param name="documentActions">The transforming commands.</param>
    /// <param name="matchingPair">The matching-token commands.</param>
    /// <param name="logActions">The log panel's error-stepping commands.</param>
    /// <returns>The menu.</returns>
    /// <remarks>
    /// //was previously: Back and Forward sat in a block of their own directly
    /// under Next/Previous Document, Matching Pair and Select Matching Pair did
    /// not exist, and Next/Previous Error Message — which do exist, with their
    /// Ctrl+E / Ctrl+Shift+E defaults and live handlers — were on no menu at
    /// all. This is upstream's own order (menu.py <c>menu_view</c>).
    /// </remarks>
    private static MenuBarItem ViewMenu(
        MainActions main,
        SideBarActions sideBar,
        BookmarkActions bookmarks,
        BrowserActions browser,
        DocumentActions documentActions,
        MatchingPairActions matchingPair,
        LogActions logActions)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&View")),
        };

        menu.Items.Add(ItemFor(main.ViewNextDocument));
        menu.Items.Add(ItemFor(main.ViewPreviousDocument));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.ViewWrapLines));
        if (documentActions != null)
        {
            menu.Items.Add(ItemFor(documentActions.ViewHighlighting));
        }

        if (sideBar != null)
        {
            menu.Items.Add(ItemFor(sideBar.ViewLineNumbers));
            menu.Items.Add(FoldingMenu(sideBar));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        if (documentActions != null)
        {
            menu.Items.Add(ItemFor(documentActions.ViewGotoFileOrDefinition));
        }

        menu.Items.Add(ItemFor(main.ViewGotoLine));
        if (browser != null)
        {
            menu.Items.Add(ItemFor(browser.GoBack));
            menu.Items.Add(ItemFor(browser.GoForward));
        }

        if (matchingPair != null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(matchingPair.ViewMatchingPair));
            menu.Items.Add(ItemFor(matchingPair.ViewMatchingPairSelect));
        }

        if (bookmarks != null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(bookmarks.ViewBookmark));
            menu.Items.Add(ItemFor(bookmarks.ViewNextMark));
            menu.Items.Add(ItemFor(bookmarks.ViewPreviousMark));
            menu.Items.Add(ItemFor(bookmarks.ViewClearErrorMarks));
            menu.Items.Add(ItemFor(bookmarks.ViewClearAllMarks));
        }

        if (logActions != null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(logActions.LogNextError));
            menu.Items.Add(ItemFor(logActions.LogPreviousError));
        }

        return menu;
    }

    /// <summary>
    /// The Music menu — upstream's own, between View and the engine's menu.
    /// </summary>
    /// <param name="music">The Music View's commands.</param>
    /// <returns>The menu.</returns>
    /// <remarks>
    /// Upstream's order, with two omissions that are rulings rather than
    /// oversights: <c>music_print</c> is gone for good (FR5.5), and
    /// <c>music_copy_image</c> / <c>music_copy_text</c> arrive with the export
    /// wave that gives them something to copy into.
    /// </remarks>
    private static MenuBarItem MusicMenu(MusicViewActions music)
    {
        var menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&Music")),
        };
        menu.Items.Add(ItemFor(music.MusicReload));
        menu.Items.Add(ItemFor(music.MusicClear));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(music.MusicZoomIn));
        menu.Items.Add(ItemFor(music.MusicZoomOut));
        menu.Items.Add(ItemFor(music.MusicZoomOriginal));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(music.MusicFitWidth));
        menu.Items.Add(ItemFor(music.MusicFitHeight));
        menu.Items.Add(ItemFor(music.MusicFitBoth));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(music.MusicSinglePages));
        menu.Items.Add(ItemFor(music.MusicTwoPagesFirstRight));
        menu.Items.Add(ItemFor(music.MusicTwoPagesFirstLeft));
        menu.Items.Add(ItemFor(music.MusicRaster));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(music.MusicHorizontal));
        menu.Items.Add(ItemFor(music.MusicVertical));
        menu.Items.Add(ItemFor(music.MusicContinuous));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(music.MusicRotateRight));
        menu.Items.Add(ItemFor(music.MusicRotateLeft));
        menu.Items.Add(new MenuFlyoutSeparator());

        //was previously: Copy to Image sat after Synchronize with Cursor
        //Position. Upstream puts it here, in the block BEFORE Jump to Cursor
        //(menu.py menu_music), beside the Copy Selected Text it has and this
        //application does not.
        menu.Items.Add(ItemFor(music.MusicCopyImage));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(music.MusicJumpToCursor));
        menu.Items.Add(ItemFor(music.MusicSyncCursor));
        menu.Items.Add(new MenuFlyoutSeparator());

        //was previously: the magnifier was HERE, with a note saying that
        //upstream has it on the Music View toolbar only (mainwindow.py
        //toolbar_music) and that this application had no window toolbar to put
        //it on, so the menu was the only way to reach it (audit A EXTRA-01).
        //Board wave W14 built both bars, so the note's own condition — "if a
        //toolbar is ever built it moves back" — has been met and it has: the
        //magnifier is on the Music View Toolbar and nowhere else, exactly as
        //upstream has it.

        //Where upstream's Print sat, which ruling FR5.5 removed permanently.
        menu.Items.Add(Submenu(
            I18n.Get("submenu title", "&Export"),
            music.MusicExportPdf,
            music.MusicExportPng,
            music.MusicExportSvg));
        menu.Items.Add(new MenuFlyoutSeparator());

        //Upstream's last group is Maximize then Save current View settings as
        //default, in that order (menu.py menu_music) — the two extras above sit
        //where Print used to.
        menu.Items.Add(ItemFor(music.MusicMaximize));
        menu.Items.Add(ItemFor(music.MusicSaveSettings));
        return menu;
    }

    private static MenuFlyoutSubItem FoldingMenu(SideBarActions sideBar)
    {
        MenuFlyoutSubItem menu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("submenu title", "&Folding")),
        };

        menu.Items.Add(ItemFor(sideBar.FoldingEnable));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(sideBar.FoldingFoldCurrent));
        menu.Items.Add(ItemFor(sideBar.FoldingFoldTop));
        menu.Items.Add(ItemFor(sideBar.FoldingUnfoldCurrent));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(sideBar.FoldingFoldAll));
        menu.Items.Add(ItemFor(sideBar.FoldingUnfoldAll));
        return menu;
    }

    /// <summary>
    /// The Tools menu, in upstream's own order: the completion commands, the
    /// two submenus that transform the document, convert-ly, and then the
    /// panels.
    /// </summary>
    /// <param name="panels">The tool panels.</param>
    /// <param name="documentActions">The transforming commands.</param>
    /// <param name="completion">The autocomplete commands.</param>
    /// <param name="pitch">The pitch commands.</param>
    /// <param name="rest">The rest commands.</param>
    /// <param name="rhythm">The rhythm commands.</param>
    /// <param name="lyrics">The lyric commands.</param>
    /// <param name="pitchLanguage">The current document's pitch-name language.</param>
    /// <param name="changePitchLanguage">What picking a language does.</param>
    /// <returns>The menu.</returns>
    private static MenuBarItem ToolsMenu(
        PanelManager panels,
        DocumentActions documentActions,
        CompletionActions completion,
        PitchActions pitch = null,
        RestActions rest = null,
        RhythmActions rhythm = null,
        LyricsActions lyrics = null,
        Func<string> pitchLanguage = null,
        Action<string> changePitchLanguage = null,
        MainActions main = null,
        FontsActions fonts = null)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&Tools")),
        };

        if (completion != null)
        {
            menu.Items.Add(ItemFor(completion.AutoComplete));
            menu.Items.Add(ItemFor(completion.PopupCompletions));
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        if (documentActions != null)
        {
            menu.Items.Add(Submenu(
                I18n.Get("submenu title", "Code &Formatting"),
                documentActions.ToolsIndentAuto,
                documentActions.ToolsIndentIndent,
                documentActions.ToolsReformat,
                documentActions.ToolsRemoveTrailingWhitespace));
            menu.Items.Add(TransformationsMenu(
                documentActions, pitch, rest, rhythm, lyrics,
                pitchLanguage, changePitchLanguage));

            //Upstream's own position: the fonts plugin inserts its action into
            //the Tools menu between Musical Transformations and Update with
            //convert-ly.
            if (fonts != null) { menu.Items.Add(ItemFor(fonts.DocumentFonts)); }

            menu.Items.Add(ItemFor(documentActions.ToolsConvertLy));
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        if (main != null)
        {
            //Upstream's Tools > Directories, minus its third entry: there is no
            //LilyPond data directory to open. The engine's own data is
            //vendored inside the assemblies (rulings FR2 and FR5.1), so
            //`engrave_open_lilypond_datadir' has nothing to point at and is
            //not ported.
            menu.Items.Add(Submenu(
                I18n.Get("submenu title", "&Directories"),
                main.FileOpenCurrentDirectory,
                main.FileOpenCommandPrompt));
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        //The panels come LAST, which is where upstream's panel manager adds
        //them; W5 had them first because there was nothing above them yet.
        foreach (var group in PanelManager.GroupNames)
        {
            IReadOnlyList<Panel> inGroup = panels.PanelsInGroup(group);
            if (inGroup.Count == 0) { continue; }

            MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
            {
                Text = Display(GroupTitle(group)),
            };
            foreach (var panel in inGroup)
            {
                submenu.Items.Add(ItemFor(panel.ToggleAction));
            }

            menu.Items.Add(submenu);
        }

        foreach (var panel in panels.UngroupedPanels())
        {
            menu.Items.Add(ItemFor(panel.ToggleAction));
        }

        return menu;
    }

    /// <summary>
    /// Tools &gt; Musical Transformations: everything that changes the music
    /// rather than the text around it.
    /// </summary>
    /// <param name="documentActions">The direction and Quick Remove commands.</param>
    /// <param name="pitch">The pitch commands.</param>
    /// <param name="rest">The rest commands.</param>
    /// <param name="rhythm">The rhythm commands.</param>
    /// <param name="lyrics">The lyric commands.</param>
    /// <param name="pitchLanguage">The current document's pitch-name language.</param>
    /// <param name="changePitchLanguage">What picking a language does.</param>
    /// <returns>The submenu.</returns>
    private static MenuFlyoutSubItem TransformationsMenu(
        DocumentActions documentActions,
        PitchActions pitch,
        RestActions rest,
        RhythmActions rhythm,
        LyricsActions lyrics,
        Func<string> pitchLanguage,
        Action<string> changePitchLanguage)
    {
        MenuFlyoutSubItem menu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("submenu title", "Musical &Transformations")),
        };

        if (pitch != null)
        {
            menu.Items.Add(PitchMenu(pitch, pitchLanguage, changePitchLanguage));
        }

        if (rest != null)
        {
            MenuFlyoutSubItem restMenu = new MenuFlyoutSubItem
            {
                Text = Display(I18n.Get("submenu title", "Rest")),
            };
            restMenu.Items.Add(ItemFor(rest.RestFmRestToSpacer));
            restMenu.Items.Add(ItemFor(rest.RestSpacerToFmRest));
            restMenu.Items.Add(new MenuFlyoutSeparator());
            restMenu.Items.Add(ItemFor(rest.RestCommToRest));
            menu.Items.Add(restMenu);
        }

        if (rhythm != null)
        {
            menu.Items.Add(RhythmMenu(rhythm));
        }

        if (lyrics != null)
        {
            MenuFlyoutSubItem lyricsMenu = new MenuFlyoutSubItem
            {
                Text = Display(I18n.Get("submenu title", "&Lyrics")),
            };
            lyricsMenu.Items.Add(ItemFor(lyrics.LyricsHyphenate));
            lyricsMenu.Items.Add(ItemFor(lyrics.LyricsDehyphenate));
            lyricsMenu.Items.Add(new MenuFlyoutSeparator());
            lyricsMenu.Items.Add(ItemFor(lyrics.LyricsCopyDehyphenated));
            menu.Items.Add(lyricsMenu);
        }

        menu.Items.Add(Submenu(
            I18n.Get("submenu title", "&Directions"),
            documentActions.ForceDirections["up"],
            documentActions.ForceDirections["neutral"],
            documentActions.ForceDirections["down"]));

        MenuFlyoutSubItem remove = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("submenu title", "&Quick Remove")),
        };
        foreach (var kind in new[]
        {
            "comments", "articulations", "ornaments", "instrument_scripts",
            "slurs", "beams", "ligatures", "dynamics", "fingerings", "markup",
        })
        {
            remove.Items.Add(ItemFor(documentActions.QuickRemove[kind]));
        }

        menu.Items.Add(remove);
        return menu;
    }

    /// <summary>The Pitch submenu, language list and all.</summary>
    /// <param name="pitch">The pitch commands.</param>
    /// <param name="currentLanguage">The current document's pitch-name
    /// language, which decides which entry is ticked.</param>
    /// <param name="change">What picking a language does.</param>
    /// <returns>The submenu.</returns>
    private static MenuFlyoutSubItem PitchMenu(
        PitchActions pitch, Func<string> currentLanguage, Action<string> change)
    {
        MenuFlyoutSubItem menu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("submenu title", "&Pitch")),
        };

        menu.Items.Add(LanguageMenu(pitch, currentLanguage, change));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(pitch.PitchRelativeAssumeFirstPitchAbsolute));
        menu.Items.Add(ItemFor(pitch.PitchRelativeWriteStartPitch));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(pitch.PitchRel2Abs));
        menu.Items.Add(ItemFor(pitch.PitchAbs2Rel));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(pitch.PitchTranspose));
        menu.Items.Add(ItemFor(pitch.PitchModalTranspose));
        menu.Items.Add(ItemFor(pitch.PitchModeShift));
        menu.Items.Add(ItemFor(pitch.PitchSimplify));
        return menu;
    }

    /// <summary>
    /// The pitch-name language list, which ticks the language the current
    /// document is written in.
    /// </summary>
    /// <param name="pitch">The pitch commands, for the submenu's own text.</param>
    /// <param name="currentLanguage">The current document's language.</param>
    /// <param name="change">What picking a language does.</param>
    /// <returns>The submenu.</returns>
    /// <remarks>Upstream ticks the right entry from the menu's
    /// <c>aboutToShow</c>. There is no such moment here (board trap 39), so
    /// the pointer arriving at the entry — the moment before it opens — is
    /// where the ticks are set.</remarks>
    private static MenuFlyoutSubItem LanguageMenu(
        PitchActions pitch, Func<string> currentLanguage, Action<string> change)
    {
        MenuFlyoutSubItem menu = new MenuFlyoutSubItem
        {
            Text = Display(pitch.PitchLanguage.Text),
        };

        List<ToggleMenuFlyoutItem> items = new List<ToggleMenuFlyoutItem>();
        foreach (var language in PitchActions.Languages)
        {
            string name = language;
            ToggleMenuFlyoutItem item = new ToggleMenuFlyoutItem
            {
                Text = Title(name),
            };
            item.Click += (_, _) =>
            {
                Tick(items, name);
                change?.Invoke(name);
            };
            items.Add(item);
            menu.Items.Add(item);
        }

        void Refresh() => Tick(
            items,
            currentLanguage?.Invoke() ?? Tools.PitchTools.DefaultLanguage);

        menu.PointerEntered += (_, _) => Refresh();
        menu.Loaded += (_, _) => Refresh();
        Refresh();
        return menu;
    }

    /// <summary>Ticks exactly one entry of the language list.</summary>
    /// <param name="items">The entries.</param>
    /// <param name="language">The language to tick.</param>
    private static void Tick(
        IReadOnlyList<ToggleMenuFlyoutItem> items, string language)
    {
        for (int index = 0; index < items.Count; index++)
        {
            items[index].IsChecked = string.Equals(
                PitchActions.Languages[index], language, StringComparison.Ordinal);
        }
    }

    /// <summary>Capitalises a language name for the menu.</summary>
    /// <param name="text">The name.</param>
    /// <returns>The name with its first letter upper case.</returns>
    /// <remarks>Upstream's <c>name.title()</c>. Invariant casing, because a
    /// Turkish locale would otherwise turn "italiano" into "İtaliano".</remarks>
    private static string Title(string text)
        => string.IsNullOrEmpty(text)
            ? text
            : char.ToUpperInvariant(text[0]) + text.Substring(1);

    /// <summary>The Rhythm submenu.</summary>
    /// <param name="rhythm">The rhythm commands.</param>
    /// <returns>The submenu.</returns>
    private static MenuFlyoutSubItem RhythmMenu(RhythmActions rhythm)
    {
        MenuFlyoutSubItem menu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("submenu title", "&Rhythm")),
        };

        //Upstream's grouping: scale, dots, removals, implicit/explicit, and
        //the three that write a rhythm the user supplies.
        string[][] groups =
        {
            new[] { "double", "halve" },
            new[] { "dot", "undot" },
            new[] { "remove_scaling", "remove_fraction_scaling", "remove" },
            new[] { "implicit", "implicit_per_line", "explicit" },
            new[] { "apply", "copy", "paste" },
        };

        for (int group = 0; group < groups.Length; group++)
        {
            if (group > 0) { menu.Items.Add(new MenuFlyoutSeparator()); }

            foreach (var operation in groups[group])
            {
                menu.Items.Add(ItemFor(rhythm.Operations[operation]));
            }
        }

        return menu;
    }

    /// <summary>
    /// The Snippets menu: the snippets whose <c>menu</c> variable puts them
    /// there, grouped by that variable's value, together with FD10's native
    /// editor commands in the groups upstream's own <c>menu</c> variable gave
    /// them.
    /// </summary>
    /// <param name="snippets">The snippet library.</param>
    /// <param name="actions">The Snippets panel's commands.</param>
    /// <param name="apply">What picking a snippet does.</param>
    /// <param name="editorCommands">FD10's native commands, or null.</param>
    /// <param name="hasSelection">Reads whether the editor has a selection.</param>
    /// <returns>The menu.</returns>
    /// <remarks>
    /// Fourteen of the twenty-two commands have no <c>menu</c> variable at all:
    /// upstream reaches them from the Snippets PANEL, which lists every
    /// snippet. FD10 takes them out of the library, so that route is gone and
    /// they would have no way in but a keyboard shortcut — and eleven of them
    /// have no default shortcut either. They therefore go in a block of their
    /// own after upstream's groups. A group is only an ordering and a
    /// separator here, exactly as it is upstream — no group has a title — so
    /// upstream's own four groups still read exactly as they do there.
    /// </remarks>
    private static MenuBarItem SnippetMenu(
        SnippetLibrary snippets,
        SnippetToolActions actions,
        Action<string> apply,
        EditorCommandActions editorCommands,
        Func<bool> hasSelection = null)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "Sn&ippets")),
        };

        void Fill()
        {
            while (menu.Items.Count > 0)
            {
                menu.Items.RemoveAt(menu.Items.Count - 1);
            }

            foreach (var group in SnippetMenuGroups(snippets, editorCommands))
            {
                foreach (var (name, action) in group)
                {
                    menu.Items.Add(action == null
                        ? SnippetItem(snippets, name, apply, hasSelection)
                        : ItemFor(action));
                }

                menu.Items.Add(new MenuFlyoutSeparator());
            }

            menu.Items.Add(ItemFor(actions.Activate));
        }

        //Upstream refills the menu each time it is OPENED (`aboutToShow' ->
        //`repopulate'). A menu bar item has no such moment here, and refilling
        //it while it is on screen leaves the old entries visible beside the new
        //ones — so the pointer arriving at it, which is the moment before it
        //opens, refills it (board trap 39).
        //was previously: the refill happened only when the library had changed.
        //That is not enough now the entries carry ENABLEMENT: a snippet
        //declaring `selection: yes' is greyed without a selection, and the
        //selection changes constantly without the library changing at all.
        menu.PointerEntered += (_, _) => Fill();
        menu.Loaded += (_, _) => Fill();
        Fill();
        return menu;
    }

    /// <summary>
    /// File &gt; New from Template: the snippets whose <c>template</c>
    /// variable puts them there.
    /// </summary>
    /// <param name="snippets">The snippet library.</param>
    /// <param name="actions">The Snippets panel's commands.</param>
    /// <param name="apply">What picking a template does.</param>
    /// <returns>The submenu.</returns>
    private static MenuFlyoutSubItem TemplateMenu(
        SnippetLibrary snippets,
        SnippetToolActions actions,
        Action<string> apply,
        ScoreWizardActions scoreWizard)
    {
        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("New")),
        };

        void Fill()
            => FillTemplates(submenu.Items, snippets, actions, apply, scoreWizard);

        bool stale = false;
        snippets.Changed += (_, _) => stale = true;
        submenu.PointerEntered += (_, _) =>
        {
            if (!stale) { return; }

            stale = false;
            Fill();
        };

        submenu.Loaded += (_, _) => Fill();
        Fill();
        return submenu;
    }

    /// <summary>
    /// The Snippets menu's contents, group by group: the snippets that declare
    /// a <c>menu</c> variable and the native editor commands, merged into
    /// upstream's own groups, each group sorted by name.
    /// </summary>
    /// <param name="snippets">The snippet library.</param>
    /// <param name="editorCommands">The native commands, or null.</param>
    /// <returns>The groups, in the order the menu shows them.</returns>
    private static IReadOnlyList<IReadOnlyList<(string Name, AppAction Action)>>
        SnippetMenuGroups(SnippetLibrary snippets, EditorCommandActions editorCommands)
    {
        Dictionary<string, List<(string Name, AppAction Action)>> groups
            = new Dictionary<string, List<(string, AppAction)>>(StringComparer.Ordinal);

        List<(string Name, AppAction Action)> Group(string key)
        {
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<(string, AppAction)>();
                groups[key] = list;
            }

            return list;
        }

        //A variable declared with no value sorts first — upstream's
        //`'' if g is True else g`; the commands with no group sort last, under
        //a key no snippet variable can hold.
        const string Ungrouped = "￿";
        foreach (var (key, names) in SnippetFilter.Grouped(snippets, "menu"))
        {
            List<(string, AppAction)> group = Group(
                string.Equals(key, "yes", StringComparison.Ordinal) ? string.Empty : key);
            foreach (var name in names) { group.Add((name, null)); }
        }

        foreach (EditorCommandInfo info in editorCommands == null
            ? Array.Empty<EditorCommandInfo>()
            : (IEnumerable<EditorCommandInfo>)EditorCommands.All)
        {
            AppAction action = editorCommands.Action(info.Name);
            if (action == null) { continue; }

            Group(info.MenuGroup ?? Ungrouped).Add((info.Name, action));
        }

        return groups
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (IReadOnlyList<(string, AppAction)>)g.Value
                .OrderBy(e => e.Item1, StringComparer.Ordinal)
                .ToList())
            .ToList();
    }

    private static MenuFlyoutItem SnippetItem(
        SnippetLibrary snippets,
        string name,
        Action<string> apply,
        Func<bool> hasSelection = null)
    {
        MenuFlyoutItem item = new MenuFlyoutItem
        {
            //A snippet's title is the user's own text, not a msgid — an
            //ampersand in it is a character, so it is not stripped.
            Text = snippets.Title(name),
        };

        //Upstream's `visitAction': a snippet that declares `selection: yes'
        //NEEDS one, so without a selection the entry is disabled rather than
        //left to decline when it is pressed (SnippetInserter does decline —
        //this is the affordance, not the behaviour).
        if (hasSelection != null
            && snippets.Get(name).VariableHas("selection", "yes"))
        {
            item.IsEnabled = hasSelection();
        }

        item.Click += (_, _) => apply?.Invoke(name);
        return item;
    }

    /// <summary>The Session menu: the named sessions, grouped by their prefix.</summary>
    /// <param name="store">The stored sessions.</param>
    /// <param name="actions">The session commands.</param>
    /// <param name="start">What picking a session does.</param>
    /// <returns>The menu.</returns>
    private static MenuBarItem SessionMenu(
        SessionStore store, SessionActions actions, Action<string> start)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&Session")),
        };

        void Fill()
        {
            //Removed one at a time from the END rather than cleared: a menu
            //bar item that has been opened once does not honour a Clear, and
            //the old entries stay on screen beside the new ones.
            while (menu.Items.Count > 0)
            {
                menu.Items.RemoveAt(menu.Items.Count - 1);
            }

            menu.Items.Add(ItemFor(actions.SessionNew));
            menu.Items.Add(ItemFor(actions.SessionSave));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(actions.SessionManage));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(actions.SessionNone));
            menu.Items.Add(new MenuFlyoutSeparator());

            //A name with a slash in it groups: "Bach/Cantatas" becomes a
            //Bach submenu holding Cantatas.
            //was previously: a group was added the first time a name mentioned
            //it, and its title was the bare group name. Upstream sorts the group
            //KEYS (sessions/menu.py `for k in sorted(groups.keys())') and marks
            //the group the CURRENT session is inside with a "* " prefix, so the
            //user can see which closed submenu they are in.
            Dictionary<string, List<string>> groups
                = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string> topLevel = new List<string>();

            foreach (var name in store.SessionNames())
            {
                int slash = name.IndexOf('/');
                if (slash <= 0) { topLevel.Add(name); continue; }

                string group = name.Substring(0, slash);
                if (!groups.TryGetValue(group, out var names))
                {
                    names = new List<string>();
                    groups[group] = names;
                }

                names.Add(name);
            }

            string current = store.CurrentSession;
            foreach (var group in groups.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                bool holdsCurrent = current != null
                    && current.StartsWith(group + "/", StringComparison.Ordinal);
                MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
                {
                    Text = holdsCurrent ? "* " + group : group,
                };
                foreach (var name in groups[group])
                {
                    submenu.Items.Add(SessionItem(
                        store, name, name.Substring(group.Length + 1), start));
                }

                menu.Items.Add(submenu);
            }

            if (groups.Count > 0) { menu.Items.Add(new MenuFlyoutSeparator()); }

            foreach (var name in topLevel)
            {
                menu.Items.Add(SessionItem(store, name, name, start));
            }
        }

        bool stale = false;
        store.SessionsChanged += (_, _) => stale = true;
        menu.PointerEntered += (_, _) =>
        {
            if (!stale) { return; }

            stale = false;
            Fill();
        };

        menu.Loaded += (_, _) => Fill();
        Fill();
        return menu;
    }

    private static ToggleMenuFlyoutItem SessionItem(
        SessionStore store, string fullName, string text, Action<string> start)
    {
        ToggleMenuFlyoutItem item = new ToggleMenuFlyoutItem
        {
            Text = text,
            IsChecked = string.Equals(
                fullName, store.CurrentSession, StringComparison.Ordinal),
        };
        item.Click += (_, _) => start?.Invoke(fullName);
        return item;
    }

    /// <summary>The Documents menu: one entry per open document.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="stickyDocument">Reads the document the engraver is pinned
    /// to, or null when nothing can say.</param>
    /// <returns>The menu.</returns>
    /// <remarks>
    /// //was previously: the title msgid was "&amp;Document", SINGULAR, which
    /// is wrong in English and matches no catalog entry in any of the thirteen
    /// languages; upstream's msgid is "&amp;Documents" (documentmenu.py). The
    /// sticky mark and the path tooltip were missing too.
    /// </remarks>
    private static MenuBarItem DocumentMenu(
        DocumentManager documents, Func<EditorDocument> stickyDocument = null)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&Documents")),
        };

        void Fill()
        {
            menu.Items.Clear();
            foreach (var document in documents.Documents)
            {
                menu.Items.Add(DocumentItem(documents, document, stickyDocument));
            }
        }

        //The document list changes constantly; rebuilding on open is upstream's
        //approach too (DocumentMenu.populate on aboutToShow). The sticky mark
        //has no event of its own to hang on here — upstream connects
        //`stickyChanged', `jobStarted' and `jobFinished' — so the pointer
        //arriving at the menu, which is the moment before it opens, refills it
        //(board trap 39).
        menu.PointerEntered += (_, _) => Fill();
        menu.Loaded += (_, _) => Fill();
        documents.DocumentCreated += (_, _) => Fill();
        documents.DocumentClosed += (_, _) => Fill();
        documents.DocumentUrlChanged += (_, _) => Fill();
        documents.CurrentDocumentChanged += (_, _) => Fill();
        Fill();
        return menu;
    }

    /// <summary>One Documents-menu entry.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="document">The document this entry raises.</param>
    /// <param name="stickyDocument">Reads the document the engraver is pinned
    /// to, or null.</param>
    /// <returns>The entry.</returns>
    /// <remarks>Upstream appends the "[always engraved]" mark to the sticky
    /// document's text and puts the document's FOLDER — homified, the same
    /// string the recent-files entries carry — on the entry's tooltip
    /// (documentmenu.py <c>setDocumentStatus</c>, which tooltips
    /// <c>util.path(doc.url())</c>). The per-document <c>&amp;</c> accelerator
    /// it also assigns has nowhere to go here — see
    /// <see cref="Display"/>.</remarks>
    private static ToggleMenuFlyoutItem DocumentItem(
        DocumentManager documents,
        EditorDocument document,
        Func<EditorDocument> stickyDocument)
    {
        bool sticky = stickyDocument != null && stickyDocument() == document;
        ToggleMenuFlyoutItem entry = new ToggleMenuFlyoutItem
        {
            //A document's name is the user's own file name, not a msgid — an
            //ampersand in it is a character, so nothing is stripped.
            //L10N: 'always engraved': the document is marked as 'Always Engrave'
            //in the engine's menu.
            Text = sticky
                ? document.DocumentName() + " " + I18n.Get("[always engraved]")
                : document.DocumentName(),
            IsChecked = document == documents.CurrentDocument,
        };
        if (!string.IsNullOrEmpty(document.Path))
        {
            ToolTipService.SetToolTip(
                entry,
                PathUtil.Homify(System.IO.Path.GetDirectoryName(document.Path)));
        }

        entry.Click += (_, _) => documents.CurrentDocument = document;
        return entry;
    }

    /// <summary>
    /// The Documents menu's entries as a SUBMENU, which the document context
    /// menu nests at its head.
    /// </summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="stickyDocument">Reads the document the engraver is pinned
    /// to, or null.</param>
    /// <returns>The submenu.</returns>
    /// <remarks>Upstream's <c>documentcontextmenu.py</c> adds the whole
    /// Documents menu as its first entry.</remarks>
    public static MenuFlyoutSubItem DocumentSubmenu(
        DocumentManager documents, Func<EditorDocument> stickyDocument = null)
    {
        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("menu title", "&Documents")),
        };

        foreach (var document in documents.Documents)
        {
            submenu.Items.Add(DocumentItem(documents, document, stickyDocument));
        }

        return submenu;
    }

    private static MenuBarItem WindowMenu(MainActions main, ViewActions views)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&Window")),
        };

        menu.Items.Add(ItemFor(main.WindowNew));
        if (views != null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(views.WindowSplitHorizontal));
            menu.Items.Add(ItemFor(views.WindowSplitVertical));
            menu.Items.Add(ItemFor(views.WindowCloseView));
            menu.Items.Add(ItemFor(views.WindowCloseOthers));
            menu.Items.Add(ItemFor(views.WindowNextView));
            menu.Items.Add(ItemFor(views.WindowPreviousView));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.WindowFullscreen));
        return menu;
    }

    private static MenuBarItem LilyPortMenu(
        EngraveActions engrave,
        Func<EditorDocument> engravedDocument,
        Action<string> openGeneratedFile)
    {
        MenuBarItem menu = new MenuBarItem
        {
            //was previously: "&LilyPond". No UI element of Fresco.Brix names LilyPond:
            //the engine the user drives is LilyPort. The lineage is acknowledged in
            //informational text and in About, never in the chrome.
            Title = Display(I18n.Get("menu title", "&LilyPort")),
        };

        menu.Items.Add(ItemFor(engrave.EngraveSticky));
        menu.Items.Add(ItemFor(engrave.EngraveAutoCompile));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(engrave.EngravePreview));
        menu.Items.Add(ItemFor(engrave.EngravePublish));
        menu.Items.Add(ItemFor(engrave.EngraveDebug));
        menu.Items.Add(ItemFor(engrave.EngraveCustom));
        menu.Items.Add(ItemFor(engrave.EngraveAbort));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(GeneratedFilesMenu(engravedDocument, openGeneratedFile));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(engrave.EngraveEngineInfo));
        return menu;
    }

    /// <summary>
    /// The Generated Files submenu: what the last run left on disk, gathered
    /// by kind.
    /// </summary>
    /// <remarks>Rebuilt every time it opens, because a run that finishes while
    /// the menu is shut must be reflected the next time it is opened.</remarks>
    private static MenuFlyoutSubItem GeneratedFilesMenu(
        Func<EditorDocument> engravedDocument, Action<string> openGeneratedFile)
    {
        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
        {
            Text = Display(I18n.Get("Generated &Files")),
        };

        void Fill()
        {
            submenu.Items.Clear();
            EditorDocument document = engravedDocument?.Invoke();
            if (document != null)
            {
                IReadOnlyList<string> files = ResultFiles.For(document).Files();
                bool first = true;
                foreach (var group in PathUtil.GroupFiles(
                    files, new[] { "pdf", "mid midi", "svg svgz", "png", "!ly ily lyi" }))
                {
                    if (group.Count == 0) { continue; }

                    if (!first) { submenu.Items.Add(new MenuFlyoutSeparator()); }

                    first = false;
                    foreach (var file in group)
                    {
                        MenuFlyoutItem entry = new MenuFlyoutItem
                        {
                            Text = Path.GetFileName(file),
                        };
                        ToolTipService.SetToolTip(entry, file);
                        entry.Click += (_, _) => openGeneratedFile?.Invoke(file);
                        submenu.Items.Add(entry);
                    }
                }
            }

            if (submenu.Items.Count == 0)
            {
                submenu.Items.Add(new MenuFlyoutItem
                {
                    Text = Display(I18n.Get("No files available")),
                    IsEnabled = false,
                });
            }
        }

        submenu.Loaded += (_, _) => Fill();
        Fill();
        return submenu;
    }

    private static MenuBarItem HelpMenu(MainActions main, DocumentationActions documentation)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&Help")),
        };

        menu.Items.Add(ItemFor(main.HelpManual));

        //was previously: upstream has `help_whatsthis' here — Qt's
        //"What's This?" cursor mode, which the platform has no equivalent of
        //and which is not ported. `help_bugreport' sits between the
        //documentation entries and About upstream; it is a post-v1 candidate
        //(board section 10) and is not here either.
        if (documentation != null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(documentation.HelpDocumentation));
            menu.Items.Add(ItemFor(documentation.HelpContext));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.HelpAbout));
        return menu;
    }

    private static MenuFlyoutSubItem Submenu(string title, params AppAction[] actions)
    {
        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem { Text = Display(title) };
        foreach (var action in actions)
        {
            submenu.Items.Add(ItemFor(action));
        }

        return submenu;
    }

    private static string GroupTitle(string group)
        => group switch
        {
            "viewers" => I18n.Get("&Viewers"),
            "coding" => I18n.Get("&Coding"),
            "structure" => I18n.Get("&Structure"),
            "midi" => I18n.Get("&MIDI"),
            _ => group,
        };

    /// <summary>
    /// Keeps a menu entry in step with its action.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="item">The entry.</param>
    /// <param name="update">What to copy across.</param>
    /// <remarks>
    /// <para>
    /// The handler is unhooked when the entry leaves the tree, so a rebuilt
    /// menu does not leave the old entries listening — and hooked again, with
    /// a refresh, when it comes back.
    /// </para>
    /// <para>
    /// ⚠ The refresh is the whole point, and W6 found out why. A flyout's
    /// items UNLOAD every time the menu closes, not only when the menu is
    /// thrown away: unhooking without hooking up again froze every entry at
    /// whatever state it had the first time it was shown. The rhythm commands
    /// were the ones that showed it — greyed out for want of a selection,
    /// then still greyed with one — but Quick Remove and Cut and Assign had
    /// the same fault and nobody had opened them twice.
    /// </para>
    /// </remarks>
    private static void Follow(AppAction action, MenuFlyoutItemBase item, Action update)
    {
        //Shortcuts are NOT attached here: a flyout's items are not in the
        //visual tree until the menu is first opened, so an accelerator on one
        //would never fire. ShortcutRegistrar puts them on the window instead.
        PropertyChangedEventHandler handler = (_, _) => update();
        bool following = false;

        void Start()
        {
            if (following) { return; }

            action.PropertyChanged += handler;
            following = true;

            //Catch up on everything that changed while nobody was listening.
            update();
        }

        void Stop()
        {
            if (!following) { return; }

            action.PropertyChanged -= handler;
            following = false;
        }

        Start();
        item.Loaded += (_, _) => Start();
        item.Unloaded += (_, _) => Stop();
    }
}
