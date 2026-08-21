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

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        MusicZoomIn = Add("music_zoom_in").WithIcon("zoom-in");
        MusicZoomOut = Add("music_zoom_out").WithIcon("zoom-out");
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
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        MusicZoomIn.Text = I18n.Get("Zoom &In");
        MusicZoomOut.Text = I18n.Get("Zoom &Out");
        MusicZoomOriginal.Text = I18n.Get("&Original Size");
        MusicFitWidth.Text = I18n.Get("Fit &Width");
        MusicFitHeight.Text = I18n.Get("Fit &Height");
        MusicFitBoth.Text = I18n.Get("Fit &Page");
        MusicSinglePages.Text = I18n.Get("Single Pages");
        MusicTwoPagesFirstRight.Text = I18n.Get("Two Pages (first page right)");
        MusicTwoPagesFirstLeft.Text = I18n.Get("Two Pages (first page left)");
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
    }
}
