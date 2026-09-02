// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/musicview/__init__.py (class Actions) + qpageview/viewactions.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Music View's commands: what to show, how large, and what to do with the
/// place the text cursor points at.
/// </summary>
/// <remarks>
/// Upstream splits these between its own collection and qpageview's
/// ViewActions; here they are one collection, because the split existed so that
/// a general-purpose page viewer could be reused outside Frescobaldi, and this
/// view is not that.
/// <para>
/// <c>music_print</c> is absent for good: FR5.5 rules printing out permanently.
/// </para>
/// </remarks>
public sealed class MusicViewActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "musicview";

    /// <summary>The setting remembering whether the view follows the cursor.</summary>
    public const string SyncCursorSettingKey = "musicview/sync_cursor";

    /// <summary>Creates the Music View commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public MusicViewActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <inheritdoc/>
    public override string Title => I18n.Get("Music View");

    /// <summary>Zoom in one step.</summary>
    public AppAction MusicZoomIn { get; private set; }

    /// <summary>Zoom out one step.</summary>
    public AppAction MusicZoomOut { get; private set; }

    /// <summary>Zoom to 100%.</summary>
    public AppAction MusicZoomOriginal { get; private set; }

    /// <summary>Fit the page's width to the view.</summary>
    public AppAction MusicFitWidth { get; private set; }

    /// <summary>Fit the page's height to the view.</summary>
    public AppAction MusicFitHeight { get; private set; }

    /// <summary>Fit the whole page in the view.</summary>
    public AppAction MusicFitBoth { get; private set; }

    /// <summary>One page at a time, in a column.</summary>
    public AppAction MusicSinglePages { get; private set; }

    /// <summary>Two pages side by side, the first on the right.</summary>
    public AppAction MusicTwoPagesFirstRight { get; private set; }

    /// <summary>Two pages side by side, the first on the left.</summary>
    public AppAction MusicTwoPagesFirstLeft { get; private set; }

    /// <summary>As many pages as fit, in a grid.</summary>
    public AppAction MusicRaster { get; private set; }

    /// <summary>Arrange the pages left to right.</summary>
    public AppAction MusicHorizontal { get; private set; }

    /// <summary>Arrange the pages top to bottom.</summary>
    public AppAction MusicVertical { get; private set; }

    /// <summary>Show every page, rather than one set at a time.</summary>
    public AppAction MusicContinuous { get; private set; }

    /// <summary>Turn the pages a quarter turn anti-clockwise.</summary>
    public AppAction MusicRotateLeft { get; private set; }

    /// <summary>Turn the pages a quarter turn clockwise.</summary>
    public AppAction MusicRotateRight { get; private set; }

    /// <summary>Go to the next page.</summary>
    public AppAction MusicNextPage { get; private set; }

    /// <summary>Go to the previous page.</summary>
    public AppAction MusicPreviousPage { get; private set; }

    /// <summary>Show what the text cursor points at.</summary>
    public AppAction MusicJumpToCursor { get; private set; }

    /// <summary>Keep the view on whatever the text cursor points at.</summary>
    public AppAction MusicSyncCursor { get; private set; }

    /// <summary>Read the engraved files again.</summary>
    public AppAction MusicReload { get; private set; }

    /// <summary>Empty the view and forget the engraved files.</summary>
    public AppAction MusicClear { get; private set; }

    /// <summary>Remember the current view settings as the defaults.</summary>
    public AppAction MusicSaveSettings { get; private set; }

    /// <summary>Music View &gt; Copy to Image.</summary>
    public AppAction MusicCopyImage { get; private set; }

    /// <summary>Music View &gt; Magnifier.</summary>
    public AppAction MusicMagnifier { get; private set; }

    /// <summary>Music View &gt; Export PDF.</summary>
    /// <remarks>
    /// ⚠ NOT UPSTREAM'S. Frescobaldi has no PDF export because LilyPond writes
    /// its PDF for it; the engine here writes SVG, so the application makes the
    /// PDF (board decision FD13). It sits where upstream's Print action sat,
    /// which ruling FR5.5 removed permanently.
    /// </remarks>
    public AppAction MusicExportPdf { get; private set; }

    /// <summary>Music View &gt; Export PNG of the current page.</summary>
    public AppAction MusicExportPng { get; private set; }

    /// <summary>Music View &gt; Export SVG of the current page.</summary>
    public AppAction MusicExportSvg { get; private set; }

    /// <summary>Music View toolbar &gt; the score chooser.</summary>
    /// <remarks>Upstream's <c>music_document_select</c> is a
    /// <c>ComboBoxAction</c> — the action IS the combo, and it carries the
    /// caption, the tooltip and Ctrl+Shift+O. Here the combo is an ordinary
    /// control on the panel's toolbar and this action gives it those three
    /// things.</remarks>
    public AppAction MusicDocumentSelect { get; private set; }

    /// <summary>Music &gt; Maximize.</summary>
    /// <remarks>Upstream's <c>music_maximize</c>: the Music View takes the
    /// whole screen area. It carries no default shortcut.</remarks>
    public AppAction MusicMaximize { get; private set; }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        MusicMaximize = Add("music_maximize").WithIcon("view-fullscreen");
        MusicDocumentSelect = Add("music_document_select")
            .WithShortcut("Ctrl+Shift+O");
        //Upstream inherits Qt's ZoomIn/ZoomOut standard keys from qpageview's
        //own ViewActions.setActionShortcuts; on X11 they are Ctrl++ and Ctrl+-.
        //was previously: neither had a keyboard route at all.
        MusicZoomIn = Add("music_zoom_in").WithIcon("zoom-in")
            .WithShortcuts(StandardKeys.ZoomIn);
        MusicZoomOut = Add("music_zoom_out").WithIcon("zoom-out")
            .WithShortcuts(StandardKeys.ZoomOut);
        MusicZoomOriginal = Add("music_zoom_original").WithIcon("zoom-original");
        MusicFitWidth = Add("music_fit_width").WithIcon("zoom-fit-width").AsToggle();
        MusicFitHeight = Add("music_fit_height").WithIcon("zoom-fit-height").AsToggle();
        MusicFitBoth = Add("music_fit_both").WithIcon("zoom-fit-best").AsToggle();
        MusicSinglePages = Add("music_single_pages").AsToggle();
        MusicTwoPagesFirstRight = Add("music_two_pages_first_right").AsToggle();
        MusicTwoPagesFirstLeft = Add("music_two_pages_first_left").AsToggle();
        MusicRaster = Add("music_raster").AsToggle();
        MusicHorizontal = Add("music_horizontal").AsToggle();
        MusicVertical = Add("music_vertical").AsToggle();
        MusicContinuous = Add("music_continuous").AsToggle();
        MusicRotateLeft = Add("music_rotate_left").WithIcon("rotate-left");
        MusicRotateRight = Add("music_rotate_right").WithIcon("rotate-right");
        MusicNextPage = Add("music_next_page").WithIcon("go-next");
        MusicPreviousPage = Add("music_prev_page").WithIcon("go-previous");
        MusicJumpToCursor = Add("music_jump_to_cursor").WithIcon("go-jump")
            .WithShortcut("Ctrl+J");
        MusicSyncCursor = Add("music_sync_cursor").AsToggle();
        MusicReload = Add("music_reload").WithIcon("view-refresh").WithShortcut("F5");
        MusicClear = Add("music_clear").WithIcon("edit-clear");
        MusicSaveSettings = Add("music_save_settings");
        MusicCopyImage = Add("music_copy_image").WithIcon("edit-copy")
            .WithShortcut("Ctrl+Shift+C");
        //A TOGGLE, as upstream's is: the glass is either available or not,
        //and while it is available Ctrl and the left button call it up.
        MusicMagnifier = Add("music_magnifier").WithIcon("zoom-in").AsToggle();
        MusicExportPdf = Add("music_export_pdf").WithIcon("document-export");
        MusicExportPng = Add("music_export_png").WithIcon("document-export");
        MusicExportSvg = Add("music_export_svg").WithIcon("document-export");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        MusicMaximize.Text = I18n.Get("&Maximize");
        MusicDocumentSelect.Text = I18n.Get("Select Music View Document");
        //was previously: nothing — the chooser was a bare ComboBox with no
        //action, so it had no caption, no Shortcuts-page row and no key.
        //⚠ Upstream's tooltip is "Choose the PDF document to display."; this
        //view shows the engraved SCORE, and a PDF is something this application
        //exports rather than displays (FR7/FR8), so the tooltip is a
        //Fresco.Brix-original msgid and is in the renamed-string table.
        MusicDocumentSelect.ToolTip = I18n.Get("Choose the score to display.");
        MusicZoomIn.Text = I18n.Get("Zoom &In");
        MusicZoomOut.Text = I18n.Get("Zoom &Out");
        MusicZoomOriginal.Text = I18n.Get("Original &Size");
        MusicFitWidth.Text = I18n.Get("Fit &Width");
        MusicFitHeight.Text = I18n.Get("Fit &Height");
        MusicFitBoth.Text = I18n.Get("Fit &Page");
        MusicSinglePages.Text = I18n.Get("Single Pages");
        MusicTwoPagesFirstRight.Text = I18n.Get("Two Pages (first page right)");
        MusicTwoPagesFirstLeft.Text = I18n.Get("Two Pages (first page left)");
        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). qpageview 1.0.5 —
        //the version Frescobaldi pins — says _("Grid Layout") here, but
        //Frescobaldi's own catalogs never caught up: every one of them still
        //carries the STALE msgid "Raster" (with the translator comment "a
        //layout type (like grid)") and none has a "Grid Layout" entry at all.
        //So Frescobaldi 4.0.7 shows an UNTRANSLATED "Grid Layout" in all
        //thirteen of its languages while a perfectly good translation of
        //"Raster" sits unreachable in each catalog. Keeping "Raster" is the
        //msgid the translations can actually render; matching upstream's English
        //would mean an untranslated entry in thirteen languages, to match a bug.
        //Written up as a bug report in W13's STATUS file.
        MusicRaster.Text = I18n.Get("Raster");
        MusicHorizontal.Text = I18n.Get("Horizontal");
        MusicVertical.Text = I18n.Get("Vertical");
        MusicContinuous.Text = I18n.Get("&Continuous");
        MusicRotateLeft.Text = I18n.Get("Rotate &Left");
        MusicRotateRight.Text = I18n.Get("Rotate &Right");
        MusicNextPage.Text = I18n.Get("Next Page");
        MusicNextPage.ToolTip = I18n.Get("Show the next page.");
        MusicPreviousPage.Text = I18n.Get("Previous Page");
        MusicPreviousPage.ToolTip = I18n.Get("Show the previous page.");
        MusicJumpToCursor.Text = I18n.Get("&Jump to Cursor Position");
        MusicSyncCursor.Text = I18n.Get("S&ynchronize with Cursor Position");
        MusicReload.Text = I18n.Get("&Reload");
        MusicClear.Text = I18n.Get("Clear");
        MusicSaveSettings.Text = I18n.Get("Save current View settings as default");
        MusicCopyImage.Text = I18n.Get("Copy to &Image...");
        MusicMagnifier.Text = I18n.Get("Magnifier");
        MusicMagnifier.ToolTip = I18n.Get(
            "Shows a magnifying glass; hold Ctrl and drag with the left button.");

        //Fresco.Brix-ORIGINAL strings: there is no upstream msgid to key them
        //by, so W-I18N's harvest cannot map them and they fall back to English
        //until translated (the same consequence ruling FR13's renames have).
        MusicExportPdf.Text = I18n.Get("Export &PDF...");
        MusicExportPdf.ToolTip = I18n.Get("Writes the whole score to a PDF file.");
        MusicExportPng.Text = I18n.Get("Export PN&G...");
        MusicExportPng.ToolTip = I18n.Get("Writes the current page to a picture file.");
        MusicExportSvg.Text = I18n.Get("Export S&VG...");
        MusicExportSvg.ToolTip = I18n.Get("Writes the current page to an SVG file.");
    }
}
