// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/doclist/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Documents panel: every open document in one list, optionally gathered
/// by the folder it lives in. Picking one makes it current; the current one is
/// always the selected one.
/// </summary>
/// <remarks>
/// <para>
/// Upstream puts this panel in W5 with the rest of the editor tools. It landed
/// with the dock shell instead because the shell needed a real occupant to be
/// verified against — and this is the one panel that needs nothing beyond the
/// document list W2 already built.
/// </para>
/// <para>
/// ✅ W5 added the rest: multiple selection, and a right-click menu that acts
/// on it — or on a whole folder when a folder row is the one clicked.
/// </para>
/// </remarks>
public sealed class DocumentListPanel : Panel
{
    /// <summary>The setting deciding whether documents are grouped.</summary>
    public const string GroupSettingKey = "document_list/group_by_folder";

    private readonly DocumentManager _documents;
    private readonly SettingsStore _settings;
    private readonly Dictionary<TreeViewNode, EditorDocument> _nodes
        = new Dictionary<TreeViewNode, EditorDocument>();

    private readonly Dictionary<TreeViewNode, string> _folders
        = new Dictionary<TreeViewNode, string>();

    private TreeView _tree;
    private bool _suppressSelection;

    /// <summary>Creates the panel.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="settings">The settings store, or null.</param>
    public DocumentListPanel(DocumentManager documents, SettingsStore settings = null)
        : base("doclist", DockArea.Left)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _settings = settings;

        ToggleAction.WithShortcut("Meta+Alt+F");

        _documents.DocumentCreated += (_, _) => Populate();
        _documents.DocumentClosed += (_, _) => Populate();
        _documents.DocumentLoaded += (_, _) => Populate();
        _documents.DocumentUrlChanged += (_, _) => Populate();
        _documents.DocumentModificationChanged += (_, _) => Populate();
        _documents.CurrentDocumentChanged += (_, e) => Select(e.Document);
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Documents");

    /// <inheritdoc/>
    public override void TranslateUI()
        => ToggleAction.Text = I18n.Get("Docum&ents");

    /// <summary>Gets whether documents are gathered by folder.</summary>
    public bool GroupByFolder => _settings?.GetBool(GroupSettingKey) ?? false;

    /// <summary>Gets or sets the right-click menu, or null for none.</summary>
    public DocumentContextMenu ContextMenu { get; set; }

    /// <summary>Gets the documents the user has selected.</summary>
    public IReadOnlyList<EditorDocument> SelectedDocuments()
    {
        if (_tree == null) { return Array.Empty<EditorDocument>(); }

        List<EditorDocument> selected = new List<EditorDocument>();
        foreach (var node in _tree.SelectedNodes)
        {
            if (_nodes.TryGetValue(node, out var document))
            {
                selected.Add(document);
            }
            else if (_folders.ContainsKey(node))
            {
                //Selecting a folder means selecting what is in it.
                selected.AddRange(node.Children
                    .Where(c => _nodes.ContainsKey(c))
                    .Select(c => _nodes[c]));
            }
        }

        return selected.Distinct().ToList();
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        _tree = new TreeView
        {
            //Multiple selection is what the context menu acts on: upstream's
            //list saves or closes everything the user has picked out.
            SelectionMode = TreeViewSelectionMode.Multiple,
            CanDragItems = false,
            CanReorderItems = false,
        };

        _tree.RightTapped += (_, e) =>
        {
            if (ContextMenu == null) { return; }

            IReadOnlyList<EditorDocument> selected = SelectedDocuments();
            string folder = null;

            //Right-clicking a row that is NOT part of the selection acts on
            //that row instead, which is what a user expects of a list.
            if (e.OriginalSource is FrameworkElement source
                && source.DataContext is TreeViewNode clicked)
            {
                if (_folders.TryGetValue(clicked, out string name))
                {
                    folder = name;
                    selected = clicked.Children
                        .Where(c => _nodes.ContainsKey(c))
                        .Select(c => _nodes[c])
                        .ToList();
                }
                else if (_nodes.TryGetValue(clicked, out var document)
                    && !selected.Contains(document))
                {
                    selected = new[] { document };
                }
            }

            if (selected.Count == 0) { return; }

            ContextMenu.ShowForMany(
                _tree, selected, e.GetPosition(_tree), folder);
            e.Handled = true;
        };

        _tree.ItemInvoked += (_, e) =>
        {
            if (_suppressSelection) { return; }

            if (e.InvokedItem is TreeViewNode node
                && _nodes.TryGetValue(node, out var document))
            {
                _documents.CurrentDocument = document;
            }
        };

        Populate();
        return _tree;
    }

    /// <summary>Rebuilds the list.</summary>
    private void Populate()
    {
        if (_tree == null) { return; }

        _suppressSelection = true;
        _tree.RootNodes.Clear();
        _nodes.Clear();
        _folders.Clear();

        bool group = GroupByFolder;
        Dictionary<string, TreeViewNode> folders
            = new Dictionary<string, TreeViewNode>(StringComparer.Ordinal);

        foreach (var document in _documents.Documents
            .OrderBy(d => d.DocumentName(), StringComparer.OrdinalIgnoreCase))
        {
            TreeViewNode node = new TreeViewNode { Content = Label(document) };
            _nodes[node] = document;

            if (!group)
            {
                _tree.RootNodes.Add(node);
                continue;
            }

            //Grouped: one parent per folder, "Untitled" gathering the
            //documents that have no folder yet.
            string folder = document.Path == null
                ? I18n.Get("Untitled")
                : System.IO.Path.GetDirectoryName(document.Path);
            if (!folders.TryGetValue(folder, out var parent))
            {
                parent = new TreeViewNode { Content = folder, IsExpanded = true };
                folders[folder] = parent;
                _folders[parent] = folder;
                _tree.RootNodes.Add(parent);
            }

            parent.Children.Add(node);
        }

        _suppressSelection = false;
        Select(_documents.CurrentDocument);
    }

    private void Select(EditorDocument document)
    {
        if (_tree == null || document == null) { return; }

        TreeViewNode node = _nodes
            .FirstOrDefault(pair => pair.Value == document).Key;
        if (node == null) { return; }

        _suppressSelection = true;
        _tree.SelectedNodes.Clear();
        _tree.SelectedNodes.Add(node);
        _suppressSelection = false;
    }

    private static string Label(EditorDocument document)
        => document.IsModified
            ? "* " + document.DocumentName()
            : document.DocumentName();
}
