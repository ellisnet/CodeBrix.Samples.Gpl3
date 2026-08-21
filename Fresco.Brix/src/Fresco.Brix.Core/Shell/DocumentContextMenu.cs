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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/documentcontextmenu.py and contextmenu.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The menu that appears on right-clicking a document — on its tab, or on its
/// row in the Documents panel: save it, save it under another name, close it,
/// or close everything else.
/// </summary>
/// <remarks>
/// Upstream's menu also carries "Always Engrave This Document", the sticky
/// pin. That command belongs to the engrave service and arrives with it in W3;
/// the menu is built to take it then.
/// </remarks>
public sealed class DocumentContextMenu
{
    private readonly Func<EditorDocument, Task<bool>> _save;
    private readonly Func<EditorDocument, Task<bool>> _saveAs;
    private readonly Func<EditorDocument, Task<bool>> _close;
    private readonly Func<EditorDocument, Task> _closeOthers;

    /// <summary>Creates the menu.</summary>
    /// <param name="save">Saves a document.</param>
    /// <param name="saveAs">Saves a document under a new name.</param>
    /// <param name="close">Closes a document.</param>
    /// <param name="closeOthers">Closes everything but a document.</param>
    public DocumentContextMenu(
        Func<EditorDocument, Task<bool>> save,
        Func<EditorDocument, Task<bool>> saveAs,
        Func<EditorDocument, Task<bool>> close,
        Func<EditorDocument, Task> closeOthers)
    {
        _save = save;
        _saveAs = saveAs;
        _close = close;
        _closeOthers = closeOthers;
    }

    /// <summary>Shows the menu for a document, at the pointer.</summary>
    /// <param name="target">The element that was right-clicked.</param>
    /// <param name="document">The document it stands for.</param>
    /// <param name="position">Where to put the menu, relative to the target.</param>
    public void Show(FrameworkElement target, EditorDocument document, Point position)
    {
        if (target == null || document == null) { return; }

        MenuFlyout flyout = new MenuFlyout();
        flyout.Items.Add(Item(I18n.Get("&Save"), () => _save?.Invoke(document)));
        flyout.Items.Add(Item(I18n.Get("Save &As..."), () => _saveAs?.Invoke(document)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(Item(I18n.Get("&Close"), () => _close?.Invoke(document)));
        flyout.Items.Add(Item(
            I18n.Get("Close Other Documents"), () => _closeOthers?.Invoke(document)));
        flyout.ShowAt(target, position);
    }

    /// <summary>
    /// Shows the menu for a SET of documents — what the Documents panel needs
    /// when the user has selected more than one, or a whole folder.
    /// </summary>
    /// <param name="target">The element that was right-clicked.</param>
    /// <param name="documents">The documents the menu acts on.</param>
    /// <param name="position">Where to put the menu, relative to the target.</param>
    /// <param name="groupName">The folder name when a folder was clicked, or
    /// null when it is a plain multiple selection.</param>
    public void ShowForMany(
        FrameworkElement target,
        IReadOnlyList<EditorDocument> documents,
        Point position,
        string groupName = null)
    {
        if (target == null || documents == null || documents.Count == 0) { return; }

        if (documents.Count == 1 && groupName == null)
        {
            Show(target, documents[0], position);
            return;
        }

        MenuFlyout flyout = new MenuFlyout();
        flyout.Items.Add(Item(
            groupName == null
                ? I18n.Get("Save selected documents")
                : I18n.Format(
                    I18n.Get("Save documents in this folder ({folder})"),
                    ("folder", groupName)),
            async () =>
            {
                foreach (var document in documents)
                {
                    if (_save != null) { await _save(document); }
                }
            }));
        flyout.Items.Add(Item(
            groupName == null
                ? I18n.Get("Close selected documents")
                : I18n.Format(
                    I18n.Get("Close documents in this folder ({folder})"),
                    ("folder", groupName)),
            async () =>
            {
                //A copy, because closing a document takes it out of the list
                //the caller handed in.
                foreach (var document in documents.ToList())
                {
                    if (_close != null) { await _close(document); }
                }
            }));
        flyout.ShowAt(target, position);
    }

    private static MenuFlyoutItem Item(string text, Func<Task> work)
    {
        MenuFlyoutItem item = new MenuFlyoutItem
        {
            Text = ActionCollectionManager.RemoveAccelerator(text),
        };
        item.Click += (_, _) => _ = work();
        return item;
    }
}
