// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using Fresco.Brix.Tools;
using System.Collections.Generic;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/mainwindow.py (createToolBars, settingsChanged)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What one place on a toolbar holds.</summary>
public enum ToolbarEntryKind
{
    /// <summary>A button that fires a command.</summary>
    Action,

    /// <summary>A gap between groups.</summary>
    Separator,

    /// <summary>A control that is not a button.</summary>
    Widget,
}

/// <summary>Which control an entry is, when it is not a button.</summary>
public enum ToolbarWidget
{
    /// <summary>Not a control.</summary>
    None,

    /// <summary>The chooser naming which engraved score is shown.</summary>
    DocumentChooser,

    /// <summary>The zoom chooser: three fit modes and the percentages.</summary>
    ZoomChooser,

    /// <summary>The page box, "{num} of {total}".</summary>
    Pager,
}

/// <summary>Which menu hangs off a button's arrow, when one does.</summary>
public enum ToolbarMenu
{
    /// <summary>None; the button is only a button.</summary>
    None,

    /// <summary>The recently opened files.</summary>
    RecentFiles,

    /// <summary>The engrave modes: publish and custom.</summary>
    EngraveModes,

    /// <summary>The File menu's New group: the templates and the wizard.</summary>
    Templates,

    /// <summary>The File menu's Save sub-menu.</summary>
    Save,

    /// <summary>The File menu's Close sub-menu.</summary>
    Close,
}

/// <summary>One place on a toolbar.</summary>
public sealed class ToolbarEntry
{
    private ToolbarEntry()
    {
    }

    /// <summary>Gets what this place holds.</summary>
    public ToolbarEntryKind Kind { get; private init; }

    /// <summary>Gets the command a button fires, or null.</summary>
    public AppAction Action { get; private init; }

    /// <summary>Gets which control this is, when it is not a button.</summary>
    public ToolbarWidget Widget { get; private init; }

    /// <summary>Gets which menu hangs off the button's arrow.</summary>
    public ToolbarMenu Menu { get; private init; }

    /// <summary>Makes a button.</summary>
    /// <param name="action">The command it fires.</param>
    /// <param name="menu">The menu on its arrow, if any.</param>
    /// <returns>The entry.</returns>
    public static ToolbarEntry For(AppAction action, ToolbarMenu menu = ToolbarMenu.None)
        => new ToolbarEntry
        {
            Kind = ToolbarEntryKind.Action,
            Action = action,
            Menu = menu,
        };

    /// <summary>Makes a gap.</summary>
    /// <returns>The entry.</returns>
    public static ToolbarEntry Separator()
        => new ToolbarEntry { Kind = ToolbarEntryKind.Separator };

    /// <summary>Makes a control.</summary>
    /// <param name="widget">Which control.</param>
    /// <param name="action">The command that carries its caption and key.</param>
    /// <returns>The entry.</returns>
    public static ToolbarEntry Control(ToolbarWidget widget, AppAction action = null)
        => new ToolbarEntry
        {
            Kind = ToolbarEntryKind.Widget,
            Widget = widget,
            Action = action,
        };
}

/// <summary>
/// What the window's two toolbars hold, in upstream's order, with upstream's
/// separators.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>mainwindow.createToolBars</c> and the <c>verbose_toolbuttons</c>
/// branch of <c>mainwindow.settingsChanged</c>, as data rather than as widget
/// calls, so the ORDER can be asserted without a window
/// (<c>MainToolbarTests</c>).
/// </para>
/// <para>
/// Upstream adds two <c>QToolBar</c>s to the same top area, which Qt lays out
/// side by side on one row until they no longer fit. There is no toolbar area
/// in a CodeBrix.Platform window, so <see cref="MainToolbar"/> draws the two
/// bars as two runs of controls on one row under the menu bar — the same
/// arrangement, said out loud.
/// </para>
/// <para>
/// <c>music_print</c> is missing from the Music View bar and is not coming
/// back: ruling FR5.5 rules printing out permanently, which is why upstream's
/// second entry has no counterpart here.
/// </para>
/// </remarks>
public static class ToolbarLayout
{
    /// <summary>The Main Toolbar's title.</summary>
    /// <remarks>Upstream sets it as the bar's window title
    /// (<c>mainwindow.translateUI</c>); a bar here is not a window, so it names
    /// the run of controls for anything that has to say which bar it means.
    /// </remarks>
    public static string MainTitle() => I18n.Get("Main Toolbar");

    /// <summary>The Music View Toolbar's title.</summary>
    public static string MusicTitle() => I18n.Get("Music View Toolbar");

    /// <summary>Builds the Main Toolbar's entries.</summary>
    /// <param name="main">The window's own commands.</param>
    /// <param name="browser">The back/forward commands.</param>
    /// <param name="scoreWizard">The Score Wizard's commands.</param>
    /// <param name="engrave">The engraving commands.</param>
    /// <param name="verboseToolButtons">
    /// Whether New, Save and Close carry their pull-down menus — the General
    /// page's <c>verbose_toolbuttons</c>.
    /// </param>
    /// <returns>The entries, in order.</returns>
    public static IReadOnlyList<ToolbarEntry> Main(
        MainActions main,
        BrowserActions browser,
        ScoreWizardActions scoreWizard,
        EngraveActions engrave,
        bool verboseToolButtons)
    {
        List<ToolbarEntry> entries = new List<ToolbarEntry>();
        if (main == null) { return entries; }

        entries.Add(ToolbarEntry.For(
            main.FileNew,
            verboseToolButtons ? ToolbarMenu.Templates : ToolbarMenu.None));

        //Upstream hangs the recent-files menu on the Open button
        //unconditionally — it is not part of the verbose branch.
        entries.Add(ToolbarEntry.For(main.FileOpen, ToolbarMenu.RecentFiles));
        entries.Add(ToolbarEntry.For(
            main.FileSave,
            verboseToolButtons ? ToolbarMenu.Save : ToolbarMenu.None));
        entries.Add(ToolbarEntry.For(
            main.FileClose,
            verboseToolButtons ? ToolbarMenu.Close : ToolbarMenu.None));

        if (browser != null)
        {
            entries.Add(ToolbarEntry.Separator());
            entries.Add(ToolbarEntry.For(browser.GoBack));
            entries.Add(ToolbarEntry.For(browser.GoForward));
        }

        entries.Add(ToolbarEntry.Separator());
        entries.Add(ToolbarEntry.For(main.EditUndo));
        entries.Add(ToolbarEntry.For(main.EditRedo));

        if (scoreWizard != null || engrave != null)
        {
            entries.Add(ToolbarEntry.Separator());
        }

        if (scoreWizard != null)
        {
            entries.Add(ToolbarEntry.For(scoreWizard.ScoreWizard));
        }

        if (engrave != null)
        {
            //Upstream hangs engrave_publish and engrave_custom off the runner's
            //own button widget (`w.addAction(...)' on the tool button), which
            //Qt shows as the button's menu.
            entries.Add(ToolbarEntry.For(
                engrave.EngraveRunner, ToolbarMenu.EngraveModes));
        }

        return entries;
    }

    /// <summary>Builds the Music View Toolbar's entries.</summary>
    /// <param name="music">The Music View's commands.</param>
    /// <returns>The entries, in order.</returns>
    public static IReadOnlyList<ToolbarEntry> Music(MusicViewActions music)
    {
        List<ToolbarEntry> entries = new List<ToolbarEntry>();
        if (music == null) { return entries; }

        entries.Add(ToolbarEntry.Control(
            ToolbarWidget.DocumentChooser, music.MusicDocumentSelect));

        //was previously (upstream): music_print, then a separator. Printing is
        //ruled out for good (FR5.5), so the entry is gone and the separator it
        //stood before is the one below.
        entries.Add(ToolbarEntry.Separator());
        entries.Add(ToolbarEntry.For(music.MusicZoomIn));
        entries.Add(ToolbarEntry.Control(ToolbarWidget.ZoomChooser));
        entries.Add(ToolbarEntry.For(music.MusicZoomOut));
        entries.Add(ToolbarEntry.For(music.MusicMagnifier));
        entries.Add(ToolbarEntry.Separator());
        entries.Add(ToolbarEntry.For(music.MusicPreviousPage));
        entries.Add(ToolbarEntry.Control(ToolbarWidget.Pager));
        entries.Add(ToolbarEntry.For(music.MusicNextPage));
        entries.Add(ToolbarEntry.Separator());
        entries.Add(ToolbarEntry.For(music.MusicClear));
        return entries;
    }
}
