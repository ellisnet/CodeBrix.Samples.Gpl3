// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/outline/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Outline panel: what is in the document, nested the way the document
/// nests it, with each row leading back to the line it came from.
/// </summary>
/// <remarks>
/// The nesting comes from the tokenizer's own parser DEPTH at each item's
/// line — not from indentation and not from a second parse — so a
/// <c>\score</c> at depth 1 is a root and everything the braces below it open
/// hangs off it.
/// </remarks>
public sealed class OutlinePanel : Panel
{
    private readonly DocumentManager _documents;
    private readonly SettingsStore _settings;
    private readonly Dictionary<TreeViewNode, int> _positions
        = new Dictionary<TreeViewNode, int>();

    private TreeView _tree;
    private EditorDocument _connected;
    private DispatcherTimer _timer;

    /// <summary>Creates the panel.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="settings">The settings store, or null.</param>
    public OutlinePanel(DocumentManager documents, SettingsStore settings = null)
        : base("outline", DockArea.Left)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _settings = settings;
        ToggleAction.WithShortcut("Meta+Alt+O");
        _documents.CurrentDocumentChanged += (_, e) => Connect(e.Document);
    }

    /// <summary>Gets or sets what to do when the user picks a row.</summary>
    public Action<EditorDocument, int> GoTo { get; set; }

    /// <summary>Gets or sets how to read the caret, so the panel can scroll to
    /// the row the user is in.</summary>
    public Func<int> CaretPosition { get; set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Outline");

    /// <inheritdoc/>
    public override void TranslateUI() => ToggleAction.Text = I18n.Get("&Outline");

    /// <summary>Rebuilds the tree from the current document's structure.</summary>
    public void UpdateView()
    {
        if (_tree == null) { return; }

        _tree.RootNodes.Clear();
        _positions.Clear();

        EditorDocument document = _documents.CurrentDocument;
        if (document == null) { return; }

        DocumentEditorState state = DocumentEditorState.For(document, _settings);
        TextDocument store = document.Document;
        int caret = CaretPosition?.Invoke() ?? 0;

        TreeViewNode lastItem = null;
        TreeViewNode currentItem = null;
        DocumentLine lastLine = null;
        Dictionary<TreeViewNode, int> depths = new Dictionary<TreeViewNode, int>();

        foreach (var item in DocumentStructure.For(document).Outline())
        {
            DocumentLine line = store.GetLineByOffset(item.Position);
            int depth = TokenIter.StateAt(state.Highlighter, line.LineNumber)?.Depth() ?? 1;
            IList<TreeViewNode> parent = ParentFor(
                _tree, state, store, depths, ref lastItem, lastLine, line, depth);

            TreeViewNode node = new TreeViewNode
            {
                Content = new OutlineRow(item),
                IsExpanded = true,
            };
            parent.Add(node);
            depths[node] = depth;
            _positions[node] = item.Position;
            lastItem = node;
            lastLine = line;

            //The row the caret is at or after: the tree scrolls to it, so
            //opening the panel shows where the user actually is.
            if (item.Position <= caret) { currentItem = node; }
        }

        if (currentItem != null && _tree.SelectedNode != currentItem)
        {
            _tree.SelectedNode = currentItem;
        }
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        _tree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            CanDragItems = false,
            CanReorderItems = false,
            ItemTemplate = RowTemplate(),
        };

        _tree.ItemInvoked += (_, e) =>
        {
            if (e.InvokedItem is not TreeViewNode node
                || !_positions.TryGetValue(node, out int position))
            {
                return;
            }

            EditorDocument document = _documents.CurrentDocument;
            if (document == null) { return; }

            //Upstream moves to the START of the item's line, not to the match.
            DocumentLine line = document.Document.GetLineByOffset(position);
            GoTo?.Invoke(document, line.Offset);
        };

        //A big change redraws quickly, a small one lazily: upstream's 100 ms
        //and 2 s, so that typing does not rebuild the tree on every keystroke.
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            UpdateView();
        };

        Connect(_documents.CurrentDocument);
        UpdateView();
        return _tree;
    }

    private void Connect(EditorDocument document)
    {
        if (ReferenceEquals(_connected, document)) { return; }

        if (_connected != null)
        {
            _connected.ContentsChanged -= ContentsChanged;
        }

        _connected = document;
        if (_connected != null)
        {
            _connected.ContentsChanged += ContentsChanged;
        }

        Schedule(TimeSpan.FromMilliseconds(100));
    }

    private void ContentsChanged(object sender, EventArgs e)
        => Schedule(TimeSpan.FromSeconds(2));

    private void Schedule(TimeSpan delay)
    {
        if (_timer == null) { return; }

        _timer.Stop();
        _timer.Interval = delay;
        _timer.Start();
    }

    /// <summary>
    /// Decides which node a new item hangs off, from the parser depth at its
    /// line.
    /// </summary>
    private static IList<TreeViewNode> ParentFor(
        TreeView tree,
        DocumentEditorState state,
        TextDocument store,
        IReadOnlyDictionary<TreeViewNode, int> depths,
        ref TreeViewNode lastItem,
        DocumentLine lastLine,
        DocumentLine line,
        int depth)
    {
        if (lastItem != null && lastLine != null
            && lastLine.LineNumber == line.LineNumber)
        {
            //Two items on one line: the second belongs under the first.
            return lastItem.Children;
        }

        if (lastLine == null || depth == 1) { return tree.RootNodes; }

        while (lastItem != null && depth <= depths[lastItem])
        {
            lastItem = lastItem.Parent as TreeViewNode;
        }

        if (lastItem == null) { return tree.RootNodes; }

        //The item could belong under lastItem — but only if nothing BETWEEN
        //them went back to the top level.
        for (int number = lastLine.LineNumber + 1; number < line.LineNumber; number++)
        {
            int between = TokenIter.StateAt(state.Highlighter, number)?.Depth() ?? 1;
            if (between == 1) { return tree.RootNodes; }

            while (lastItem != null && between <= depths[lastItem])
            {
                lastItem = lastItem.Parent as TreeViewNode;
            }

            if (lastItem == null) { return tree.RootNodes; }
        }

        return lastItem.Children;
    }

    /// <summary>
    /// The tree's row template: the item's text, in the weight, slant and
    /// colour its kind calls for.
    /// </summary>
    /// <returns>The template.</returns>
    /// <remarks>The tree shows a node's Content as TEXT, so a UIElement put
    /// there would come out as its type name. A template over a small row
    /// object is the way to keep a heading bold and an alert red.</remarks>
    private static DataTemplate RowTemplate()
    {
        string xaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<TextBlock Text=\"{Binding Content.Text}\" "
            + "FontWeight=\"{Binding Content.Weight}\" "
            + "FontStyle=\"{Binding Content.Slant}\" "
            + "Foreground=\"{Binding Content.Brush}\" /></DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }
}

/// <summary>One row of the outline tree.</summary>
public sealed class OutlineRow
{
    /// <summary>Creates a row.</summary>
    /// <param name="item">The outline item it stands for.</param>
    public OutlineRow(OutlineItem item)
    {
        Text = item.Text;
        Weight = item.IsTitle ? FontWeights.Bold : FontWeights.Normal;
        Slant = item.IsAlert
            ? Windows.UI.Text.FontStyle.Italic
            : Windows.UI.Text.FontStyle.Normal;

        //An alert is drawn in red; everything else takes the theme's own
        //colour, which a null brush leaves the template to inherit.
        Brush = item.IsAlert
            ? new SolidColorBrush(Color.FromArgb(0xff, 0xdd, 0x55, 0x55))
            : null;
    }

    /// <summary>Gets the text.</summary>
    public string Text { get; }

    /// <summary>Gets the weight the text is drawn in.</summary>
    public Windows.UI.Text.FontWeight Weight { get; }

    /// <summary>Gets the slant the text is drawn in.</summary>
    public Windows.UI.Text.FontStyle Slant { get; }

    /// <summary>Gets the colour the text is drawn in, or null for the
    /// inherited one.</summary>
    public Brush Brush { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
