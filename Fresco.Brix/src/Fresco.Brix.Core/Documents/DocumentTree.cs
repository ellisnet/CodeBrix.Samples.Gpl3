// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/documenttree.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One document, or one file it names, in the include tree.</summary>
public sealed class DocumentNode
{
    private readonly List<DocumentNode> _children = new List<DocumentNode>();

    /// <summary>Creates a node.</summary>
    /// <param name="document">The open document, or null.</param>
    /// <param name="path">The file, when it is not open.</param>
    public DocumentNode(EditorDocument document, string path = null)
    {
        Document = document;
        Path = document?.Path ?? path;
    }

    /// <summary>Gets the open document, or null.</summary>
    public EditorDocument Document { get; }

    /// <summary>Gets the file, or null.</summary>
    public string Path { get; }

    /// <summary>Gets the documents this one includes.</summary>
    public IReadOnlyList<DocumentNode> Children => _children;

    /// <summary>Adds a child.</summary>
    /// <param name="child">The child.</param>
    public void Add(DocumentNode child)
    {
        if (!_children.Contains(child)) { _children.Add(child); }
    }
}

/// <summary>
/// The open documents arranged by what includes what, so that a master
/// document and the files it pulls in read as one piece of work.
/// </summary>
/// <remarks>
/// A document that several others include shows up under ONE of them, which is
/// upstream's rule: this is a tree, not a graph, and the point of it is to see
/// the shape of a project rather than every edge.
/// </remarks>
public static class DocumentTree
{
    /// <summary>Builds the tree.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="includeUnopened">Whether to list files that are named but
    /// not open.</param>
    /// <returns>The roots — the documents nothing else includes.</returns>
    public static IReadOnlyList<DocumentNode> Build(
        DocumentManager documents, bool includeUnopened = false)
    {
        if (documents == null) { return Array.Empty<DocumentNode>(); }

        Dictionary<EditorDocument, DocumentNode> nodes
            = new Dictionary<EditorDocument, DocumentNode>();
        Dictionary<string, DocumentNode> unopened
            = new Dictionary<string, DocumentNode>(StringComparer.Ordinal);
        HashSet<DocumentNode> children = new HashSet<DocumentNode>();

        foreach (var document in documents.Documents)
        {
            nodes[document] = new DocumentNode(document);
        }

        foreach (var document in documents.Documents)
        {
            DocumentNode node = nodes[document];
            foreach (var path in ChildPaths(document))
            {
                EditorDocument child = documents.FindDocument(path);
                if (child != null && nodes.TryGetValue(child, out var childNode))
                {
                    if (ReferenceEquals(childNode, node)) { continue; }

                    node.Add(childNode);
                    children.Add(childNode);
                }
                else if (includeUnopened && child == null)
                {
                    if (!unopened.TryGetValue(path, out var fileNode))
                    {
                        fileNode = new DocumentNode(null, path);
                        unopened[path] = fileNode;
                    }

                    node.Add(fileNode);
                    children.Add(fileNode);
                }
            }
        }

        return nodes.Values.Where(n => !children.Contains(n)).ToList();
    }

    private static IEnumerable<string> ChildPaths(EditorDocument document)
    {
        try
        {
            return DocumentInfo.For(document).IncludeFiles();
        }
        catch (System.IO.IOException)
        {
            //A document naming a file that is not there is not an error worth
            //stopping the tree over.
            return Array.Empty<string>();
        }
    }
}
