// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Manuscripts;
using Fresco.Brix.MusicView;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/viewers/contextmenu.py + viewers/manuscript/contextmenu.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The menu that appears on right-clicking a manuscript: what can be done with
/// the thing under the pointer, which manuscripts are open, and how to look at
/// them.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>AbstractViewerContextMenu.show</c>, in its own order, with its
/// own submenus. Its manuscript subclass adds NOTHING — the module says so:
/// "our base class's context menu will form the base for all future viewers'
/// menus. So we actually do not need this subclass at all."
/// </para>
/// <para>
/// ⚠ Two of upstream's steps have no counterpart. <c>addExtensionMenu</c> is
/// dead under ruling FR5.3 (there is no extensions system, so there are no
/// extension actions to list). Printing was never in this menu, and ruling
/// FR5.5 keeps it out of the panel entirely.
/// </para>
/// <para>
/// The menu is BUILT EACH TIME it is shown, as upstream's is
/// (<c>self._menu.clear()</c> at the top of <c>show</c>): which entries appear
/// depends on the selection, the link under the pointer and how many
/// manuscripts are open.
/// </para>
/// </remarks>
public sealed class ManuscriptViewerContextMenu
{
    private readonly ManuscriptViewerActions _actions;

    /// <summary>Creates the menu.</summary>
    /// <param name="actions">The panel's commands.</param>
    public ManuscriptViewerContextMenu(ManuscriptViewerActions actions)
        => _actions = actions ?? throw new ArgumentNullException(nameof(actions));

    /// <summary>Gets or sets the panel whose manuscripts the menu lists.</summary>
    public ManuscriptViewerPanel Panel { get; set; }

    /// <summary>Gets or sets what to do with a link that leaves the manuscript.</summary>
    public Action<string> OpenExternalUrl { get; set; }

    /// <summary>Gets or sets what "Edit in Place" does with a source position.</summary>
    public Action<EditorDocument, int> EditInPlace { get; set; }

    /// <summary>Gets or sets what the Help entry opens.</summary>
    public Action ShowHelp { get; set; }

    /// <summary>Gets or sets how to ask whether the rubber band selected anything.</summary>
    public Func<bool> HasSelection { get; set; }

    /// <summary>Shows the menu at a point of an element.</summary>
    /// <param name="target">The element that was right-clicked.</param>
    /// <param name="position">Where to put the menu, relative to the target.</param>
    /// <param name="link">The link under the pointer, or null.</param>
    /// <param name="source">Where that link points in the source, or null.</param>
    public void Show(
        FrameworkElement target,
        Point position,
        Link link,
        (EditorDocument Document, int Offset)? source)
    {
        if (target == null) { return; }

        MenuFlyout flyout = new MenuFlyout();

        //1. addCopyImageAction — only when the rubber band has a selection.
        if (HasSelection?.Invoke() ?? false)
        {
            flyout.Items.Add(MenuBuilder.ItemFor(_actions.ViewerCopyImage));
        }

        //2. addCursorLinkActions — Edit in Place for a source position, the two
        //   link entries for any other URL. Upstream's branch is exclusive.
        if (source != null && EditInPlace != null)
        {
            (EditorDocument document, int offset) = source.Value;
            flyout.Items.Add(Item(
                I18n.Get("Edit in Place"), () => EditInPlace(document, offset)));
        }
        else if (link != null && !string.IsNullOrEmpty(link.Url))
        {
            flyout.Items.Add(Item(
                I18n.Get("Open Link in &New Window"), () => OpenExternalUrl?.Invoke(link.Url)));
            flyout.Items.Add(Item(I18n.Get("Copy &Link"), () => CopyToClipboard(link.Url)));
        }

        //3. addShowActions — a submenu of the open manuscripts, the current one
        //   ticked, disabled when there is only one.
        AddShowActions(flyout);

        //4. addOpenCloseActions — Open, then a Close submenu when anything is open.
        AddOpenCloseActions(flyout);

        //5. addReloadAction — only when something is open.
        if (Panel?.Current() != null) { flyout.Items.Add(MenuBuilder.ItemFor(_actions.ViewerReload)); }

        //6. addZoomActions — a separator, then the Zoom submenu.
        AddZoomActions(flyout);

        //7. addSynchronizeAction.
        flyout.Items.Add(MenuBuilder.ItemFor(_actions.ViewerSyncCursor));

        //8. addShowToolbarAction.
        flyout.Items.Add(MenuBuilder.ItemFor(_actions.ViewerShowToolbar));

        //   addExtensionMenu is dead under ruling FR5.3.

        //9. addHelpAction — "The help action is added always".
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(Item(I18n.Get("Show Help"), () => ShowHelp?.Invoke()));

        flyout.ShowAt(target, position);
    }

    private void AddShowActions(MenuFlyout flyout)
    {
        ManuscriptList list = Panel?.Manuscripts;
        if (list == null) { return; }

        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
        {
            Text = MenuBuilder.Display(I18n.Get("Show...")),
            IsEnabled = list.Count > 1,
        };

        ManuscriptEntry current = list.Current;
        for (int i = 0; i < list.Count; i++)
        {
            ManuscriptEntry entry = list.Entries[i];
            int index = i;
            ToggleMenuFlyoutItem item = new ToggleMenuFlyoutItem
            {
                Text = entry.Name,
                IsChecked = entry == current,
            };
            ToolTipService.SetToolTip(item, entry.Path);
            item.Click += (_, _) => list.SetCurrentIndex(index);
            submenu.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(submenu);
    }

    private void AddOpenCloseActions(MenuFlyout flyout)
    {
        flyout.Items.Add(MenuBuilder.ItemFor(_actions.ViewerOpen));

        int count = Panel?.Manuscripts?.Count ?? 0;
        if (count == 0) { return; }

        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
        {
            Text = MenuBuilder.Display(I18n.Get("Close...")),
        };

        //Upstream sets these two enabled only when more than one is open, on
        //the ACTIONS, which is why the toolbar follows suit.
        _actions.ViewerCloseOther.IsEnabled = count > 1;
        _actions.ViewerCloseAll.IsEnabled = count > 1;

        submenu.Items.Add(MenuBuilder.ItemFor(_actions.ViewerClose));
        submenu.Items.Add(MenuBuilder.ItemFor(_actions.ViewerCloseOther));
        submenu.Items.Add(MenuBuilder.ItemFor(_actions.ViewerCloseAll));
        flyout.Items.Add(submenu);
    }

    private void AddZoomActions(MenuFlyout flyout)
    {
        flyout.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
        {
            Text = MenuBuilder.Display(I18n.Get("Zoom")),
        };
        submenu.Items.Add(MenuBuilder.ItemFor(_actions.ViewerFitWidth));
        submenu.Items.Add(MenuBuilder.ItemFor(_actions.ViewerFitHeight));
        submenu.Items.Add(MenuBuilder.ItemFor(_actions.ViewerFitBoth));
        submenu.Items.Add(new MenuFlyoutSeparator());
        submenu.Items.Add(MenuBuilder.ItemFor(_actions.ViewerZoomIn));
        submenu.Items.Add(MenuBuilder.ItemFor(_actions.ViewerZoomOut));
        submenu.Items.Add(MenuBuilder.ItemFor(_actions.ViewerZoomOriginal));
        flyout.Items.Add(submenu);
    }

    private static void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) { return; }

        DataPackage package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private static MenuFlyoutItem Item(string text, Action handler)
    {
        MenuFlyoutItem item = new MenuFlyoutItem { Text = MenuBuilder.Display(text) };
        item.Click += (_, _) => handler?.Invoke();
        return item;
    }
}
