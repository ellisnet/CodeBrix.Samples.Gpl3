// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/viewers/__init__.py (class ViewerActions) + viewers/manuscript/__init__.py (class ManuscriptViewerActions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Manuscript Viewer's commands: which manuscripts are open, which one is
/// shown, how large, and what a click in it does.
/// </summary>
/// <remarks>
/// <para>
/// Upstream splits these three ways — qpageview's <c>ViewActions</c>,
/// <c>viewers.ViewerActions</c> and the manuscript viewer's own subclass, which
/// exists only to re-word six of them. They are one collection here for the
/// reason <see cref="MusicViewActions"/> is: the split existed so that a
/// general-purpose page viewer could be reused outside Frescobaldi, and this
/// view is not that. The re-worded six carry the manuscript viewer's own
/// msgids, verbatim.
/// </para>
/// <para>
/// The collection's NAME is upstream's own, <c>manuscript</c>
/// (<c>ManuscriptViewerActions.name</c>), so a user's rebound key lands under
/// the same heading on the Shortcuts page as it would there.
/// </para>
/// <para>
/// ⚠ <c>viewer_print</c> IS ABSENT, permanently: ruling FR5.5 rules printing
/// out, and Jeremy said so again when he ruled the panel into v1 on
/// 2026-09-02 ("no printing functionality is needed or should be supported").
/// Upstream's toolbar has a Print button between the chooser and the zoom
/// controls, and its <c>updateActions</c> exists only to enable it; neither has
/// a counterpart here.
/// </para>
/// <para>
/// ⚠ Four more of upstream's are absent because nothing in this panel's toolbar
/// or context menu shows them: <c>viewer_maximize</c> (this application's
/// Maximize is the Music View's, board wave W13's integration pass), and the
/// three page-layout toggles <c>viewer_single_pages</c>,
/// <c>viewer_two_pages_first_right</c> and <c>viewer_two_pages_first_left</c>,
/// which upstream builds into the collection and then never puts on a menu.
/// </para>
/// </remarks>
public sealed class ManuscriptViewerActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    /// <remarks>Upstream's <c>ManuscriptViewerActions.name</c>.</remarks>
    public const string CollectionName = "manuscript";

    /// <summary>The setting remembering whether the view follows the cursor.</summary>
    /// <remarks>Upstream's <c>&lt;viewerName&gt;/sync-cursor</c>.</remarks>
    public const string SyncCursorSettingKey = "manuscriptview/sync-cursor";

    /// <summary>The setting remembering whether the panel's toolbar is shown.</summary>
    /// <remarks>Upstream's <c>&lt;viewerName&gt;/show-toolbar</c>, which
    /// defaults to TRUE.</remarks>
    public const string ShowToolbarSettingKey = "manuscriptview/show-toolbar";

    /// <summary>Creates the Manuscript Viewer's commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public ManuscriptViewerActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <inheritdoc/>
    /// <remarks>Upstream's <c>ManuscriptViewerActions.title()</c>.</remarks>
    public override string Title => I18n.Get("Manuscript");

    /// <summary>Open one or more manuscripts.</summary>
    public AppAction ViewerOpen { get; private set; }

    /// <summary>Close the manuscript being shown.</summary>
    public AppAction ViewerClose { get; private set; }

    /// <summary>Close every manuscript but the one being shown.</summary>
    public AppAction ViewerCloseOther { get; private set; }

    /// <summary>Close every manuscript.</summary>
    public AppAction ViewerCloseAll { get; private set; }

    /// <summary>The chooser naming which manuscript is shown.</summary>
    public AppAction ViewerDocumentSelect { get; private set; }

    /// <summary>Read the manuscript's file again.</summary>
    public AppAction ViewerReload { get; private set; }

    /// <summary>Zoom in one step.</summary>
    public AppAction ViewerZoomIn { get; private set; }

    /// <summary>Zoom out one step.</summary>
    public AppAction ViewerZoomOut { get; private set; }

    /// <summary>Zoom to 100%.</summary>
    public AppAction ViewerZoomOriginal { get; private set; }

    /// <summary>The zoom chooser: the three fit modes and the percentages.</summary>
    public AppAction ViewerZoomCombo { get; private set; }

    /// <summary>Fit the page's width to the view.</summary>
    public AppAction ViewerFitWidth { get; private set; }

    /// <summary>Fit the page's height to the view.</summary>
    public AppAction ViewerFitHeight { get; private set; }

    /// <summary>Fit the whole page in the view.</summary>
    public AppAction ViewerFitBoth { get; private set; }

    /// <summary>Turn the pages a quarter turn anti-clockwise.</summary>
    public AppAction ViewerRotateLeft { get; private set; }

    /// <summary>Turn the pages a quarter turn clockwise.</summary>
    public AppAction ViewerRotateRight { get; private set; }

    /// <summary>Go to the next page.</summary>
    public AppAction ViewerNextPage { get; private set; }

    /// <summary>Go to the previous page.</summary>
    public AppAction ViewerPreviousPage { get; private set; }

    /// <summary>The magnifying glass.</summary>
    public AppAction ViewerMagnifier { get; private set; }

    /// <summary>Copy the selected part of the manuscript to a picture.</summary>
    public AppAction ViewerCopyImage { get; private set; }

    /// <summary>Show what the text cursor points at.</summary>
    public AppAction ViewerJumpToCursor { get; private set; }

    /// <summary>Keep the view on whatever the text cursor points at.</summary>
    public AppAction ViewerSyncCursor { get; private set; }

    /// <summary>Show or hide the panel's own toolbar.</summary>
    public AppAction ViewerShowToolbar { get; private set; }

    /// <summary>Open the user guide's page for this panel.</summary>
    public AppAction ViewerHelp { get; private set; }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        ViewerHelp = Add("viewer_help").WithIcon("help-contents");
        ViewerDocumentSelect = Add("viewer_document_select");
        ViewerOpen = Add("viewer_open").WithIcon("document-open");
        ViewerClose = Add("viewer_close").WithIcon("document-close");
        ViewerCloseOther = Add("viewer_close_other");
        ViewerCloseAll = Add("viewer_close_all");
        ViewerReload = Add("viewer_reload").WithIcon("reload");
        ViewerZoomCombo = Add("viewer_zoom_combo");

        //qpageview's ViewActions gives the two zoom steps Qt's standard keys,
        //which the Music View's own pair also carry.
        ViewerZoomIn = Add("viewer_zoom_in").WithIcon("zoom-in")
            .WithShortcuts(StandardKeys.ZoomIn);
        ViewerZoomOut = Add("viewer_zoom_out").WithIcon("zoom-out")
            .WithShortcuts(StandardKeys.ZoomOut);
        ViewerZoomOriginal = Add("viewer_zoom_original").WithIcon("zoom-original");
        ViewerFitWidth = Add("viewer_fit_width").WithIcon("zoom-fit-width").AsToggle();
        ViewerFitHeight = Add("viewer_fit_height").WithIcon("zoom-fit-height").AsToggle();
        ViewerFitBoth = Add("viewer_fit_both").WithIcon("zoom-fit-best").AsToggle();
        ViewerRotateLeft = Add("viewer_rotate_left").WithIcon("rotate-left");
        ViewerRotateRight = Add("viewer_rotate_right").WithIcon("rotate-right");
        ViewerPreviousPage = Add("viewer_prev_page").WithIcon("go-previous");
        ViewerNextPage = Add("viewer_next_page").WithIcon("go-next");
        ViewerMagnifier = Add("viewer_magnifier").WithIcon("zoom-magnifier").AsToggle();
        ViewerCopyImage = Add("viewer_copy_image").WithIcon("edit-copy");
        ViewerJumpToCursor = Add("viewer_jump_to_cursor").WithIcon("go-jump");
        ViewerSyncCursor = Add("viewer_sync_cursor").AsToggle();
        ViewerShowToolbar = Add("viewer_show_toolbar").AsToggle(true);
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        //The six the manuscript viewer re-words, verbatim
        //(viewers/manuscript/__init__.py translateUI).
        ViewerDocumentSelect.Text = I18n.Get("Select Manuscript Document");
        ViewerOpen.Text = I18n.Get("Open manuscript(s)");
        ViewerOpen.IconText = I18n.Get("Open");
        ViewerClose.Text = I18n.Get("Close manuscript");
        ViewerClose.IconText = I18n.Get("Close");
        ViewerCloseOther.Text = I18n.Get("Close other manuscripts");
        ViewerCloseAll.Text = I18n.Get("Close all manuscripts");

        //Upstream's ViewdocChooser tooltip, unchanged: a manuscript IS a PDF
        //document, so ruling FR13 has nothing to say about this one.
        ViewerDocumentSelect.ToolTip = I18n.Get("Choose the PDF document to display.");

        //The rest are viewers.ViewerActions' own, and qpageview's under it.
        ViewerHelp.Text = I18n.Get("Show Help");
        ViewerReload.Text = I18n.Get("&Reload");
        ViewerZoomCombo.Text = I18n.Get("Zoom Music");
        ViewerZoomIn.Text = I18n.Get("Zoom &In");
        ViewerZoomOut.Text = I18n.Get("Zoom &Out");
        ViewerZoomOriginal.Text = I18n.Get("Original &Size");
        ViewerFitWidth.Text = I18n.Get("Fit &Width");
        ViewerFitHeight.Text = I18n.Get("Fit &Height");
        ViewerFitBoth.Text = I18n.Get("Fit &Page");
        ViewerRotateLeft.Text = I18n.Get("Rotate &Left");
        ViewerRotateRight.Text = I18n.Get("Rotate &Right");
        ViewerNextPage.Text = I18n.Get("Next Page");
        ViewerNextPage.ToolTip = I18n.Get("Show the next page.");
        ViewerPreviousPage.Text = I18n.Get("Previous Page");
        ViewerPreviousPage.ToolTip = I18n.Get("Show the previous page.");
        ViewerMagnifier.Text = I18n.Get("Magnifier");
        ViewerMagnifier.ToolTip = I18n.Get(
            "Shows a magnifying glass; hold Ctrl and drag with the left button.");
        ViewerCopyImage.Text = I18n.Get("Copy to &Image...");
        ViewerJumpToCursor.Text = I18n.Get("&Jump to Cursor Position");
        ViewerSyncCursor.Text = I18n.Get("S&ynchronize with Cursor Position");
        ViewerShowToolbar.Text = I18n.Get("Show toolbar");
    }
}
