// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Fresco.Brix.MusicView; //was previously: frescobaldi/musicview/contextmenu.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The menu that appears on right-clicking the music: what can be done with the
/// object under the pointer, or — when there is none — how to look at the page.
/// </summary>
/// <remarks>
/// Upstream's shape exactly: an object under the pointer offers what to do with
/// it, and only an EMPTY menu falls back to the view commands. Two of its
/// entries are not here yet and neither is an oversight — Edit in Place is the
/// editor-tools wave's, and copying a selection as an image or as text arrives
/// with the export wave that can make one.
/// </remarks>
public sealed class MusicViewContextMenu
{
    private readonly MusicViewActions _actions;

    /// <summary>Creates the menu.</summary>
    /// <param name="actions">The Music View's commands.</param>
    public MusicViewContextMenu(MusicViewActions actions)
        => _actions = actions ?? throw new ArgumentNullException(nameof(actions));

    /// <summary>Gets or sets what to do with a link that leaves the score.</summary>
    public Action<string> OpenExternalUrl { get; set; }

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

        var flyout = new MenuFlyout();

        if (link != null && source == null)
        {
            if (link.IsExternal && !TextEditLink.IsTextEdit(link.Url))
            {
                flyout.Items.Add(Item(
                    I18n.Get("Open Link in &New Window"), () => OpenExternalUrl?.Invoke(link.Url)));
            }

            flyout.Items.Add(Item(I18n.Get("Copy &Link"), () => CopyToClipboard(link.Url)));
        }

        if (flyout.Items.Count == 0)
        {
            flyout.Items.Add(Item(_actions.MusicFitWidth));
            flyout.Items.Add(Item(_actions.MusicFitHeight));
            flyout.Items.Add(Item(_actions.MusicZoomOriginal));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(Item(_actions.MusicSyncCursor));
        }

        flyout.ShowAt(target, position);
    }

    private static void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) { return; }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private static MenuFlyoutItem Item(string text, Action handler)
    {
        var item = new MenuFlyoutItem { Text = Shell.MenuBuilder.Display(text) };
        item.Click += (_, _) => handler?.Invoke();
        return item;
    }

    private static MenuFlyoutItem Item(AppAction action)
    {
        var item = new MenuFlyoutItem { Text = Shell.MenuBuilder.Display(action.Text) };
        item.Click += (_, _) => action.Trigger();
        return item;
    }
}
