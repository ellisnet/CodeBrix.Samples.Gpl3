// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/contextmenu.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The menu a right-click in the editor opens: what the text under the caret
/// leads to, and what can be done with a selection.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: nothing at all. Every command here was built, wired and
/// enablement-tracked, and a right-click in the editor offered none of them.
/// </para>
/// <para>
/// Upstream builds this on top of Qt's own
/// <c>createStandardContextMenu()</c> — it INSERTS its entries above the
/// standard Undo/Cut/Copy/Paste block and puts a separator between. The editor
/// control here has no context menu of its own, so the standard block is built
/// too, from the window's own commands, and it goes where upstream's is: under
/// the separator.
/// </para>
/// </remarks>
public sealed class EditorContextMenu
{
    private readonly MainActions _main;
    private readonly DocumentActions _document;
    private readonly SnippetToolActions _snippets;

    /// <summary>Creates the menu.</summary>
    /// <param name="main">The window's own commands.</param>
    /// <param name="document">The transforming commands.</param>
    /// <param name="snippets">The Snippets panel's commands, or null.</param>
    public EditorContextMenu(
        MainActions main, DocumentActions document, SnippetToolActions snippets)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));
        _document = document;
        _snippets = snippets;
    }

    /// <summary>Gets or sets how to open a file the cursor names.</summary>
    public Action<string> OpenFile { get; set; }

    /// <summary>Gets or sets how to go to a definition.</summary>
    public Action<DefinitionTarget> GoToDefinition { get; set; }

    /// <summary>Gets or sets how to name the document a target is in.</summary>
    public Func<DefinitionTarget, EditorDocument> DocumentOf { get; set; }

    /// <summary>Shows the menu over an editor.</summary>
    /// <param name="view">The view that was right-clicked.</param>
    /// <param name="target">The element to place the menu against.</param>
    /// <param name="position">Where to put it, relative to that element.</param>
    public void Show(EditorView view, FrameworkElement target, Point position)
    {
        if (view == null || target == null) { return; }

        MenuFlyout flyout = new MenuFlyout();
        int own = 0;

        //Upstream's `open_files': every file name the cursor's LINE names, in
        //the order the tokenizer found them.
        foreach (var path in OpenFileAtCursor.FilenamesAtCursor(
            view.Document, view.SelectionStart, view.SelectionEnd))
        {
            string file = path;
            flyout.Items.Add(Item(
                I18n.Format(
                    I18n.Get("Open \"{url}\""), ("url", PathUtil.Homify(file))),
                () => OpenFile?.Invoke(file)));
            own++;
        }

        //Upstream's `jump_to_definition': one entry, captioned by WHERE the
        //definition is — and disabled when the name has no definition at all.
        MenuFlyoutItem definition = DefinitionItem(view);
        if (definition != null)
        {
            flyout.Items.Add(definition);
            own++;
        }

        if (view.HasSelection)
        {
            flyout.Items.Add(MenuBuilder.ItemFor(_main.EditCopyColoredHtml));
            own++;
            if (_snippets != null)
            {
                flyout.Items.Add(MenuBuilder.ItemFor(_snippets.CopyToSnippet));
                own++;
            }

            if (_document != null)
            {
                flyout.Items.Add(MenuBuilder.ItemFor(_document.EditCutAssign));
                flyout.Items.Add(MenuBuilder.ItemFor(_document.EditMoveToIncludeFile));
                own += 2;
            }
        }

        if (own > 0) { flyout.Items.Add(new MenuFlyoutSeparator()); }

        //Qt's own standard context menu, which upstream inherits and this
        //editor does not have: the ordinary edit commands, following their own
        //enablement.
        flyout.Items.Add(MenuBuilder.ItemFor(_main.EditUndo));
        flyout.Items.Add(MenuBuilder.ItemFor(_main.EditRedo));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(MenuBuilder.ItemFor(_main.EditCut));
        flyout.Items.Add(MenuBuilder.ItemFor(_main.EditCopy));
        flyout.Items.Add(MenuBuilder.ItemFor(_main.EditPaste));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(MenuBuilder.ItemFor(_main.EditSelectAll));

        flyout.ShowAt(target, position);
    }

    /// <summary>Builds the "jump to definition" entry, or null.</summary>
    /// <param name="view">The view.</param>
    /// <returns>The entry, or null when the caret is not on a reference.</returns>
    /// <remarks>Upstream builds the entry as soon as it knows there IS a
    /// reference node, and fills its text in on the next event loop turn —
    /// resolving the target can read other files. Here the resolve is done
    /// before the menu opens, which the same read cost allows because
    /// <c>DocumentInfo</c> has already cached what it needs.</remarks>
    private MenuFlyoutItem DefinitionItem(EditorView view)
    {
        if (GotoDefinition.ReferenceNode(
            view.Document, view.Editor.CaretOffset, view.SelectionEnd) == null)
        {
            return null;
        }

        DefinitionTarget target = GotoDefinition.Find(
            view.Document, view.Editor.CaretOffset, view.SelectionEnd);
        if (target == null)
        {
            MenuFlyoutItem unknown = new MenuFlyoutItem
            {
                Text = MenuBuilder.Display(
                    I18n.Get("&Jump to definition (unknown)")),
                IsEnabled = false,
            };
            return unknown;
        }

        EditorDocument document = DocumentOf?.Invoke(target);
        string caption = document != null && document == view.Document
            ? I18n.Format(
                I18n.Get("&Jump to definition (line {num})"),
                ("num", LineOf(view, target.Position).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)))
            : I18n.Format(
                I18n.Get("&Jump to definition (in {filename})"),
                ("filename", PathUtil.Homify(
                    target.Filename ?? string.Empty)));

        return Item(caption, () => GoToDefinition?.Invoke(target));
    }

    /// <summary>The 1-based line an offset falls on.</summary>
    /// <param name="view">The view.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The line number.</returns>
    private static int LineOf(EditorView view, int offset)
        => view.Editor.Document.GetLineByOffset(
            Math.Clamp(offset, 0, view.Editor.Document.TextLength)).LineNumber;

    private static MenuFlyoutItem Item(string text, Action handler)
    {
        MenuFlyoutItem item = new MenuFlyoutItem
        {
            Text = MenuBuilder.Display(text),
        };
        item.Click += (_, _) => handler?.Invoke();
        return item;
    }
}
