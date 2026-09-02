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
/// <para>
/// Upstream's shape exactly: an object under the pointer offers what to do with
/// it, and only an EMPTY menu falls back to the view commands. Note the
/// exclusive branch — a click that resolves to a SOURCE CURSOR offers Edit in
/// Place and nothing else; only a click that resolves to a plain link offers
/// the two link entries.
/// </para>
/// <para>
/// //was previously: a remark saying Edit in Place was "the editor-tools
/// wave's". That wave shipped <c>Tools/EditInPlace</c> whole, and the entry
/// that opens it was simply never added — so a complete, tested feature had no
/// caller anywhere in the application. It is here now.
/// </para>
/// <para>
/// ⚠ ONE DIVERGENCE, in upstream's first branch. Upstream shows Copy Selected
/// Text and then Copy to Image whenever the rubber band has selected
/// something, and Copy Selected Text only when that selection yields TEXT
/// (<c>rubberband().selectedText()</c>). The rubber band itself is here and
/// this branch is upstream's; <c>music_copy_text</c> is not, because the Music
/// View draws a RETAINED SVG SCENE GRAPH whose text is glyph runs positioned by
/// the engine, not a text layer with a reading order — asking a rectangle over
/// it for "the text" would answer glyphs in draw order, which is not what a
/// user pasting a bar of lyrics means. Extracting it properly is a feature, not
/// a wiring job, and is recorded on board §9 as post-v1 together with the
/// <c>music_copy_text</c> command it would make possible.
/// </para>
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

    /// <summary>
    /// Gets or sets what "Edit in Place" does with the source position under
    /// the pointer, or null when the window cannot open the dialog.
    /// </summary>
    public Action<EditorDocument, int> EditInPlace { get; set; }

    /// <summary>
    /// Gets or sets what the Help entry opens — the user guide's
    /// <c>musicview</c> page.
    /// </summary>
    public Action ShowHelp { get; set; }

    /// <summary>
    /// Gets or sets how to ask whether the rubber band has selected anything.
    /// </summary>
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

        var flyout = new MenuFlyout();

        //Upstream's own first branch: what to do with a rubber-band selection.
        if (HasSelection?.Invoke() ?? false)
        {
            //Upstream puts music_copy_text before this one, when the selection
            //yields text — see the class remarks for why it is not here.
            flyout.Items.Add(Item(_actions.MusicCopyImage));
        }

        if (source != null && EditInPlace != null)
        {
            (EditorDocument document, int offset) = source.Value;
            flyout.Items.Add(Item(
                I18n.Get("Edit in Place"), () => EditInPlace(document, offset)));
        }
        else if (link != null && source == null)
        {
            if (link.IsExternal && !TextEditLink.IsTextEdit(link.Url))
            {
                flyout.Items.Add(Item(
                    I18n.Get("Open Link in &New Window"), () => OpenExternalUrl?.Invoke(link.Url)));
            }

            flyout.Items.Add(Item(I18n.Get("Copy &Link"), () => CopyToClipboard(link.Url)));
        }

        //was previously: this block also carried a separator and Copy to Image,
        //unconditionally — the only route the command had from the music, given
        //that the selection branch above did not exist. It does now, and this
        //block is upstream's four entries again.
        if (flyout.Items.Count == 0)
        {
            flyout.Items.Add(Item(_actions.MusicFitWidth));
            flyout.Items.Add(Item(_actions.MusicFitHeight));
            flyout.Items.Add(Item(_actions.MusicZoomOriginal));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(Item(_actions.MusicSyncCursor));
        }

        //Upstream ends every one of its shapes with a separator and Help, which
        //opens the user guide's own `musicview' page.
        if (ShowHelp != null)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(Item(I18n.Get("Help"), () => ShowHelp()));
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
