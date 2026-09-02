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

namespace Fresco.Brix.Shell; //was previously: frescobaldi/documentcontextmenu.py
//(contextmenu.py — the EDITOR's own right-click menu — is Shell/EditorContextMenu.)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The menu that appears on right-clicking a document — on its tab, or on its
/// row in the Documents panel: save it, save it under another name, close it,
/// or close everything else.
/// </summary>
/// <remarks>
/// //was previously: a remark saying the sticky pin "belongs to the engrave
/// service and arrives with it in W3". W3 shipped the engraver and the pin;
/// the entry was never added. It is here now, checkable and ticked for the
/// document the engraver is pinned to, exactly as
/// <c>documentcontextmenu.py</c> has it — and so is upstream's first entry,
/// the whole Documents menu nested as a submenu.
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

    /// <summary>Gets or sets the open documents, for the nested Documents
    /// submenu, or null to leave it out.</summary>
    public DocumentManager Documents { get; set; }

    /// <summary>Gets or sets how to read the document the engraver is pinned
    /// to, or null when nothing can say.</summary>
    public Func<EditorDocument> StickyDocument { get; set; }

    /// <summary>Gets or sets how to pin a document, or unpin it when it is
    /// already the pinned one.</summary>
    public Action<EditorDocument> ToggleSticky { get; set; }

    /// <summary>Shows the menu for a document, at the pointer.</summary>
    /// <param name="target">The element that was right-clicked.</param>
    /// <param name="document">The document it stands for.</param>
    /// <param name="position">Where to put the menu, relative to the target.</param>
    public void Show(FrameworkElement target, EditorDocument document, Point position)
    {
        if (target == null || document == null) { return; }

        MenuFlyout flyout = new MenuFlyout();
        if (Documents != null)
        {
            flyout.Items.Add(MenuBuilder.DocumentSubmenu(Documents, StickyDocument));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        flyout.Items.Add(Item(I18n.Get("&Save"), () => _save?.Invoke(document)));
        flyout.Items.Add(Item(I18n.Get("Save &As..."), () => _saveAs?.Invoke(document)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(Item(I18n.Get("&Close"), () => _close?.Invoke(document)));
        flyout.Items.Add(Item(
            I18n.Get("Close Other Documents"), () => _closeOthers?.Invoke(document)));

        if (ToggleSticky != null)
        {
            //Upstream ticks this in `updateActions', which runs just before the
            //menu shows; the menu is built per right-click here, so the state is
            //read where it is built.
            flyout.Items.Add(new MenuFlyoutSeparator());
            ToggleMenuFlyoutItem sticky = new ToggleMenuFlyoutItem
            {
                Text = ActionCollectionManager.RemoveAccelerator(
                    I18n.Get("Always &Engrave This Document")),
                IsChecked = StickyDocument?.Invoke() == document,
            };
            sticky.Click += (_, _) => ToggleSticky(document);
            flyout.Items.Add(sticky);
        }

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
    /// <param name="untitledGroup">Whether the row clicked is the group that
    /// gathers documents with no folder yet — upstream captions that one
    /// differently again.</param>
    public void ShowForMany(
        FrameworkElement target,
        IReadOnlyList<EditorDocument> documents,
        Point position,
        string groupName = null,
        bool untitledGroup = false)
    {
        if (target == null || documents == null || documents.Count == 0) { return; }

        if (documents.Count == 1 && groupName == null)
        {
            Show(target, documents[0], position);
            return;
        }

        //was previously: two captions, for a multiple selection and for a
        //folder. Upstream has THREE — the "Untitled" group is neither.
        MenuFlyout flyout = new MenuFlyout();
        flyout.Items.Add(Item(
            untitledGroup
                ? I18n.Get("Save all untitled documents")
                : groupName == null
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
            untitledGroup
                ? I18n.Get("Close all untitled documents")
                : groupName == null
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
