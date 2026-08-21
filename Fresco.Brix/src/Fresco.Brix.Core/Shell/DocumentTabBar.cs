// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/tabbar.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The row of tabs above the editor, one per open document, showing each
/// document's name, its file path as a tool tip, and an icon saying whether it
/// has unsaved changes or how its last engrave run went.
/// </summary>
/// <remarks>
/// The tabs follow the <see cref="DocumentManager"/>'s list rather than
/// keeping one of their own, so dragging a tab reorders the documents
/// themselves and every other list of documents agrees with the tabs.
/// </remarks>
public sealed class DocumentTabBar : TabView
{
    private readonly DocumentManager _documents;
    private bool _suppressSelection;

    /// <summary>Creates the tab bar over a document list.</summary>
    /// <param name="documents">The open documents.</param>
    public DocumentTabBar(DocumentManager documents)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));

        IsAddTabButtonVisible = false;
        TabWidthMode = TabViewWidthMode.SizeToContent;
        CanReorderTabs = true;
        CanDragTabs = false;

        foreach (var document in _documents.Documents)
        {
            AddDocument(document);
        }

        SetCurrentDocument(_documents.CurrentDocument);

        _documents.DocumentCreated += (_, e) => AddDocument(e.Document);
        _documents.DocumentClosed += (_, e) => RemoveDocument(e.Document);
        _documents.DocumentUrlChanged += (_, e) => UpdateDocumentStatus(e.Document);
        _documents.DocumentModificationChanged
            += (_, e) => UpdateDocumentStatus(e.Document);
        _documents.CurrentDocumentChanged += (_, e) => SetCurrentDocument(e.Document);

        SelectionChanged += OnSelectionChanged;
        TabCloseRequested += (_, e) =>
        {
            if (e.Tab?.Tag is EditorDocument document)
            {
                CloseRequested?.Invoke(this, new DocumentEventArgs(document));
            }
        };
        TabItemsChanged += (_, _) => SyncOrder();
        RightTapped += (_, e) =>
        {
            if (ContextMenu == null) { return; }

            //Find the tab under the pointer: a right-click anywhere else on
            //the bar is not about any particular document.
            foreach (var tab in Tabs())
            {
                var bounds = e.GetPosition(tab);
                if (bounds.X < 0 || bounds.Y < 0
                    || bounds.X > tab.ActualWidth || bounds.Y > tab.ActualHeight)
                {
                    continue;
                }

                if (tab.Tag is EditorDocument document)
                {
                    ContextMenu.Show(tab, document, bounds);
                    e.Handled = true;
                }

                return;
            }
        };
    }

    /// <summary>Raised when the user clicks a tab's close button.</summary>
    public event EventHandler<DocumentEventArgs> CloseRequested;

    /// <summary>
    /// Gets or sets the menu shown when a tab is right-clicked.
    /// </summary>
    public DocumentContextMenu ContextMenu { get; set; }

    /// <summary>Gets or sets whether tabs show a close button.</summary>
    public bool TabsClosable
    {
        get;
        set
        {
            field = value;
            foreach (var tab in Tabs())
            {
                tab.IsClosable = value;
            }
        }
    } = true;

    /// <summary>Moves to the next tab, wrapping round.</summary>
    public void NextDocument() => Step(1);

    /// <summary>Moves to the previous tab, wrapping round.</summary>
    public void PreviousDocument() => Step(-1);

    /// <summary>Refreshes a tab's name, tool tip and icon.</summary>
    /// <param name="document">The document.</param>
    /// <param name="isSticky">Whether engraving is pinned to it.</param>
    /// <param name="engraveState">What its last engrave run did.</param>
    public void UpdateDocumentStatus(
        EditorDocument document,
        bool isSticky = false,
        EngraveState engraveState = EngraveState.None)
    {
        TabViewItem tab = TabFor(document);
        if (tab == null) { return; }

        tab.Header = document.DocumentName();
        ToolTipService.SetToolTip(tab, document.Path ?? document.DocumentName());

        //The icon NAME is settled here; the per-head icon assets are a W13
        //item, so until then the modified state also shows as a leading star.
        string icon = DocumentIcon.NameFor(document, isSticky, engraveState);
        tab.Tag = document;
        if (icon == DocumentIcon.Modified)
        {
            tab.Header = "* " + tab.Header;
        }
    }

    private void AddDocument(EditorDocument document)
    {
        if (document == null || TabFor(document) != null) { return; }

        TabViewItem tab = new TabViewItem
        {
            Tag = document,
            IsClosable = TabsClosable,
            //The content lives in the view manager, not the tab: a document
            //may be open in several panes at once, and a tab is only the
            //handle for choosing which document is current.
            Content = null,
        };

        _suppressSelection = true;
        TabItems.Add(tab);
        _suppressSelection = false;
        UpdateDocumentStatus(document);
    }

    private void RemoveDocument(EditorDocument document)
    {
        TabViewItem tab = TabFor(document);
        if (tab == null) { return; }

        _suppressSelection = true;
        TabItems.Remove(tab);
        _suppressSelection = false;
    }

    private void SetCurrentDocument(EditorDocument document)
    {
        TabViewItem tab = TabFor(document);
        if (tab == null || ReferenceEquals(SelectedItem, tab)) { return; }

        _suppressSelection = true;
        SelectedItem = tab;
        _suppressSelection = false;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) { return; }

        if (SelectedItem is TabViewItem tab && tab.Tag is EditorDocument document)
        {
            _documents.CurrentDocument = document;
        }
    }

    private void Step(int direction)
    {
        if (TabItems.Count == 0) { return; }

        int index = SelectedIndex + direction;
        SelectedIndex = ((index % TabItems.Count) + TabItems.Count) % TabItems.Count;
    }

    private void SyncOrder()
    {
        //A tab drag reorders the documents themselves, so every other view of
        //the list stays in step with the tabs.
        List<EditorDocument> order = Tabs()
            .Select(t => t.Tag as EditorDocument)
            .Where(d => d != null)
            .ToList();

        for (int wanted = 0; wanted < order.Count; wanted++)
        {
            int current = _documents.Documents.ToList().IndexOf(order[wanted]);
            if (current >= 0 && current != wanted)
            {
                _documents.MoveDocument(current, wanted);
            }
        }
    }

    private IEnumerable<TabViewItem> Tabs() => TabItems.OfType<TabViewItem>();

    private TabViewItem TabFor(EditorDocument document)
        => document == null
            ? null
            : Tabs().FirstOrDefault(t => ReferenceEquals(t.Tag, document));
}
