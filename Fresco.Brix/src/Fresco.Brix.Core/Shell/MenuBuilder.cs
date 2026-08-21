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
        DocumentationActions documentation = null)
    {
        if (menuBar == null) { throw new ArgumentNullException(nameof(menuBar)); }

        if (main == null) { throw new ArgumentNullException(nameof(main)); }

        menuBar.Items.Clear();
        menuBar.Items.Add(FileMenu(
            main, recentFiles, openRecent, snippets, snippetActions, applySnippet,
            scoreWizard));
        menuBar.Items.Add(EditMenu(main, documentActions));
        menuBar.Items.Add(ViewMenu(main, sideBar, bookmarks, browser, documentActions));
        if (musicView != null)
        {
            menuBar.Items.Add(MusicMenu(musicView));
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
                main));
        }

        if (snippets != null && snippetActions != null)
        {
            menuBar.Items.Add(SnippetMenu(snippets, snippetActions, applySnippet));
        }

        if (sessionStore != null && sessionActions != null)
        {
            menuBar.Items.Add(SessionMenu(sessionStore, sessionActions, startSession));
        }

        if (documents != null)
        {
            menuBar.Items.Add(DocumentMenu(documents));
        }

        menuBar.Items.Add(WindowMenu(main, views));
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

    private static MenuBarItem FileMenu(
        MainActions main,
        RecentFiles recentFiles,
        Action<string> openRecent,
        SnippetLibrary snippets,
        SnippetToolActions snippetActions,
        Action<string> applySnippet,
        ScoreWizardActions scoreWizard)
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
        menu.Items.Add(Submenu(I18n.Get("submenu title", "Close"),
            main.FileCloseOther, main.FileCloseAll, main.FileCloseAllAndSession));
        menu.Items.Add(ItemFor(main.FileSave));
        menu.Items.Add(Submenu(I18n.Get("submenu title", "Save"),
            main.FileSaveAs, main.FileSaveCopyAs, main.FileRename, main.FileSaveAll));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.FileReload));
        menu.Items.Add(ItemFor(main.FileReloadAll));
        menu.Items.Add(ItemFor(main.FileExternalChanges));
        if (snippetActions != null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(snippetActions.SaveAsTemplate));
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemFor(main.FileQuit));
        return menu;
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
            submenu.Items.Clear();
            foreach (var path in recentFiles?.Paths() ?? Array.Empty<string>())
            {
                MenuFlyoutItem entry = new MenuFlyoutItem
                {
                    Text = System.IO.Path.GetFileName(path),
                };
                ToolTipService.SetToolTip(entry, path);
                entry.Click += (_, _) => openRecent?.Invoke(path);
                submenu.Items.Add(entry);
            }

            submenu.IsEnabled = submenu.Items.Count > 0;
        }

        //The list is rebuilt each time the menu opens, so it is right however
        //the recent files changed since the last look.
        submenu.Loaded += (_, _) => Fill();
        Fill();
        return submenu;
    }

    private static MenuBarItem EditMenu(
        MainActions main, DocumentActions documentActions)
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
        if (documentActions != null)
        {
            menu.Items.Add(ItemFor(documentActions.EditCutAssign));
            menu.Items.Add(ItemFor(documentActions.EditMoveToIncludeFile));
        }

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

    private static MenuBarItem ViewMenu(
        MainActions main,
        SideBarActions sideBar,
        BookmarkActions bookmarks,
        BrowserActions browser,
        DocumentActions documentActions)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&View")),
        };

        menu.Items.Add(ItemFor(main.ViewNextDocument));
        menu.Items.Add(ItemFor(main.ViewPreviousDocument));
        if (browser != null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(browser.GoBack));
            menu.Items.Add(ItemFor(browser.GoForward));
        }

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
        if (bookmarks != null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemFor(bookmarks.ViewBookmark));
            menu.Items.Add(ItemFor(bookmarks.ViewNextMark));
            menu.Items.Add(ItemFor(bookmarks.ViewPreviousMark));
            menu.Items.Add(ItemFor(bookmarks.ViewClearErrorMarks));
            menu.Items.Add(ItemFor(bookmarks.ViewClearAllMarks));
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
        menu.Items.Add(ItemFor(music.MusicJumpToCursor));
        menu.Items.Add(ItemFor(music.MusicSyncCursor));
        menu.Items.Add(new MenuFlyoutSeparator());
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
        MainActions main = null)
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
    /// there, grouped by that variable's value.
    /// </summary>
    /// <param name="snippets">The snippet library.</param>
    /// <param name="actions">The Snippets panel's commands.</param>
    /// <param name="apply">What picking a snippet does.</param>
    /// <returns>The menu.</returns>
    private static MenuBarItem SnippetMenu(
        SnippetLibrary snippets, SnippetToolActions actions, Action<string> apply)
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

            foreach (var (_, names) in SnippetFilter.Grouped(snippets, "menu"))
            {
                foreach (var name in names)
                {
                    menu.Items.Add(SnippetItem(snippets, name, apply));
                }

                menu.Items.Add(new MenuFlyoutSeparator());
            }

            menu.Items.Add(ItemFor(actions.Activate));
        }

        //Upstream refills the menu each time it is OPENED. A menu bar item
        //has no such moment here, and refilling it while it is on screen
        //leaves the old entries visible beside the new ones — so a change to
        //the library marks the menu stale and the pointer arriving at it,
        //which is the moment before it opens, refills it.
        bool stale = false;
        snippets.Changed += (_, _) => stale = true;
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
        {
            while (submenu.Items.Count > 0)
            {
                submenu.Items.RemoveAt(submenu.Items.Count - 1);
            }

            //The wizard opens this menu, exactly as it does upstream: it is
            //the other way of starting a document from nothing.
            if (scoreWizard != null)
            {
                submenu.Items.Add(ItemFor(scoreWizard.ScoreWizard));
                submenu.Items.Add(ItemFor(scoreWizard.ScoreWizardFromCurrent));
                submenu.Items.Add(new MenuFlyoutSeparator());
            }

            foreach (var (_, names) in SnippetFilter.Grouped(snippets, "template"))
            {
                foreach (var name in names)
                {
                    submenu.Items.Add(SnippetItem(snippets, name, apply));
                }

                submenu.Items.Add(new MenuFlyoutSeparator());
            }

            submenu.Items.Add(ItemFor(actions.ManageTemplates));
        }

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

    private static MenuFlyoutItem SnippetItem(
        SnippetLibrary snippets, string name, Action<string> apply)
    {
        MenuFlyoutItem item = new MenuFlyoutItem
        {
            //A snippet's title is the user's own text, not a msgid — an
            //ampersand in it is a character, so it is not stripped.
            Text = snippets.Title(name),
        };
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
            Dictionary<string, MenuFlyoutSubItem> groups
                = new Dictionary<string, MenuFlyoutSubItem>(StringComparer.Ordinal);
            List<string> topLevel = new List<string>();

            foreach (var name in store.SessionNames())
            {
                int slash = name.IndexOf('/');
                if (slash <= 0) { topLevel.Add(name); continue; }

                string group = name.Substring(0, slash);
                if (!groups.TryGetValue(group, out var submenu))
                {
                    submenu = new MenuFlyoutSubItem { Text = group };
                    groups[group] = submenu;
                    menu.Items.Add(submenu);
                }

                submenu.Items.Add(SessionItem(store, name, name.Substring(slash + 1), start));
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

    private static MenuBarItem DocumentMenu(DocumentManager documents)
    {
        MenuBarItem menu = new MenuBarItem
        {
            Title = Display(I18n.Get("menu title", "&Document")),
        };

        void Fill()
        {
            menu.Items.Clear();
            foreach (var document in documents.Documents)
            {
                ToggleMenuFlyoutItem entry = new ToggleMenuFlyoutItem
                {
                    Text = document.DocumentName(),
                    IsChecked = document == documents.CurrentDocument,
                };
                entry.Click += (_, _) => documents.CurrentDocument = document;
                menu.Items.Add(entry);
            }
        }

        //The document list changes constantly; rebuilding on open is upstream's
        //approach too (DocumentMenu.populate on aboutToShow).
        menu.Loaded += (_, _) => Fill();
        documents.DocumentCreated += (_, _) => Fill();
        documents.DocumentClosed += (_, _) => Fill();
        documents.DocumentUrlChanged += (_, _) => Fill();
        documents.CurrentDocumentChanged += (_, _) => Fill();
        Fill();
        return menu;
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
