// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text;

namespace Fresco.Brix.UserGuide; //was previously: frescobaldi/simplemarkdown.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Where a parsed markdown document goes: a handler that is told to push a
/// node, and later to pop it.
/// </summary>
/// <remarks>
/// Upstream's <c>Output</c>. Its <c>__call__</c> context manager is
/// <see cref="Scope"/> here, so a subclass or a caller writes
/// <c>using (output.Scope("paragraph")) { … }</c> where upstream writes
/// <c>with self.output('paragraph'):</c>.
/// </remarks>
public abstract class MarkdownOutput
{
    /// <summary>Pushes a node and pops it again straight away.</summary>
    /// <param name="name">The node name.</param>
    /// <param name="arguments">The node's arguments.</param>
    public void Append(string name, params object[] arguments)
    {
        Push(name, arguments);
        Pop();
    }

    /// <summary>Appends a node and makes it the current one.</summary>
    /// <param name="name">The node name.</param>
    /// <param name="arguments">The node's arguments.</param>
    public abstract void Push(string name, params object[] arguments);

    /// <summary>Makes the current node's parent the current node.</summary>
    public abstract void Pop();

    /// <summary>Pushes a node for the lifetime of the returned scope.</summary>
    /// <param name="name">The node name.</param>
    /// <param name="arguments">The node's arguments.</param>
    /// <returns>A scope that pops the node when disposed.</returns>
    public IDisposable Scope(string name, params object[] arguments)
    {
        Push(name, arguments);
        return new PopScope(this);
    }

    private sealed class PopScope : IDisposable
    {
        private MarkdownOutput _output;

        internal PopScope(MarkdownOutput output) => _output = output;

        public void Dispose()
        {
            MarkdownOutput output = _output;
            _output = null;
            output?.Pop();
        }
    }
}

/// <summary>
/// An <see cref="MarkdownOutput"/> that keeps the parsed document as a tree.
/// </summary>
/// <remarks>Upstream's <c>Tree</c>, whose <c>Node</c> is a Python list with a
/// name and an argument tuple bolted on.</remarks>
public sealed class MarkdownTree : MarkdownOutput
{
    private readonly Node _root = new Node("root");
    private readonly List<Node> _cursor = new List<Node>();

    /// <summary>Creates an empty tree.</summary>
    public MarkdownTree() => _cursor.Add(_root);

    /// <summary>Gets the root node, whose children are the document.</summary>
    public Node Root => _root;

    /// <inheritdoc/>
    public override void Push(string name, params object[] arguments)
    {
        Node node = new Node(name, arguments);
        _cursor[_cursor.Count - 1].Children.Add(node);
        _cursor.Add(node);
    }

    /// <inheritdoc/>
    /// <remarks>⚠ Upstream guards the root: a pop with nothing pushed does
    /// nothing rather than failing, which is why an unclosed list at the end
    /// of a document leaves the tree exactly as this port leaves it.</remarks>
    public override void Pop()
    {
        if (_cursor.Count > 1) { _cursor.RemoveAt(_cursor.Count - 1); }
    }

    /// <summary>Pretty-prints the node, or the whole tree.</summary>
    /// <param name="node">The node, or null for the whole tree.</param>
    /// <param name="indentStart">The indent level to start at.</param>
    /// <param name="indentString">One level of indent.</param>
    /// <returns>The dump.</returns>
    /// <remarks>The format is upstream's own, Python argument-tuple
    /// representation included (<c>heading (2,)</c>,
    /// <c>inline_text ('one',)</c>), so the dump recorded from Frescobaldi's
    /// own parser compares to this one as text.</remarks>
    public string Dump(Node node = null, int indentStart = 0, string indentString = "  ")
    {
        List<string> lines = new List<string>();
        IReadOnlyList<Node> nodes = node != null
            ? new[] { node }
            : (IReadOnlyList<Node>)_root.Children;

        foreach (Node top in nodes) { DumpNode(top, indentStart, indentString, lines); }

        return string.Join("\n", lines);
    }

    /// <summary>Copies the tree, or one node, into another output.</summary>
    /// <param name="output">The output to copy into.</param>
    /// <param name="node">The node, or null for the whole tree.</param>
    public void Copy(MarkdownOutput output, Node node = null)
    {
        if (output == null) { throw new ArgumentNullException(nameof(output)); }

        if (node == null || ReferenceEquals(node, _root))
        {
            foreach (Node child in _root.Children) { Copy(output, child); }

            return;
        }

        using (output.Scope(node.Name, node.ArgumentArray))
        {
            foreach (Node child in node.Children) { Copy(output, child); }
        }
    }

    /// <summary>Enumerates every node with the given name.</summary>
    /// <param name="path">The node name.</param>
    /// <param name="node">The node to search under, or null for the tree.</param>
    /// <returns>The matching nodes, in document order.</returns>
    public IEnumerable<Node> Find(string path, Node node = null)
    {
        node ??= _root;
        foreach (Node child in node.Children)
        {
            if (string.Equals(child.Name, path, StringComparison.Ordinal))
            {
                yield return child;
            }

            foreach (Node deeper in Find(path, child)) { yield return deeper; }
        }
    }

    /// <summary>Concatenates the plain text under a node.</summary>
    /// <param name="node">The node, or null for the whole tree.</param>
    /// <returns>The text.</returns>
    public string Text(Node node = null)
    {
        StringBuilder builder = new StringBuilder();
        foreach (Node found in Find("inline_text", node))
        {
            if (found.Arguments.Count > 0)
            {
                builder.Append(found.Arguments[0] as string);
            }
        }

        return builder.ToString();
    }

    /// <summary>Renders the tree, or one node, as HTML.</summary>
    /// <param name="node">The node, or null for the whole tree.</param>
    /// <returns>The HTML.</returns>
    public string Html(Node node = null)
    {
        MarkdownHtmlOutput output = new MarkdownHtmlOutput();
        Copy(output, node);
        return output.Html();
    }

    private static void DumpNode(
        Node node, int indent, string indentString, List<string> lines)
    {
        lines.Add(
            string.Concat(Repeat(indentString, indent), node.Name, " ",
                SimpleMarkdown.PythonTuple(node.Arguments)));

        foreach (Node child in node.Children)
        {
            DumpNode(child, indent + 1, indentString, lines);
        }
    }

    private static string Repeat(string text, int count)
    {
        if (count <= 0) { return string.Empty; }

        StringBuilder builder = new StringBuilder(text.Length * count);
        for (int index = 0; index < count; index++) { builder.Append(text); }

        return builder.ToString();
    }

    /// <summary>One node of a parsed markdown document.</summary>
    public sealed class Node
    {
        /// <summary>Creates a node.</summary>
        /// <param name="name">The node name.</param>
        /// <param name="arguments">The node's arguments.</param>
        public Node(string name, params object[] arguments)
        {
            Name = name;
            ArgumentArray = arguments ?? Array.Empty<object>();
        }

        /// <summary>Gets the node's name, e.g. <c>paragraph</c>.</summary>
        public string Name { get; }

        /// <summary>Gets the node's arguments.</summary>
        public IReadOnlyList<object> Arguments => ArgumentArray;

        /// <summary>The arguments as the array a push wants them back as.</summary>
        internal object[] ArgumentArray { get; }

        /// <summary>Gets the node's children.</summary>
        public List<Node> Children { get; } = new List<Node>();

        /// <inheritdoc/>
        public override string ToString()
            => Name + " " + SimpleMarkdown.PythonTuple(Arguments);
    }
}
