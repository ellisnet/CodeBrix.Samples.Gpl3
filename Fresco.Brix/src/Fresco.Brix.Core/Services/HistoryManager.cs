// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Services; //was previously: frescobaldi/historymanager.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Remembers which documents were active, most recent last, so that closing
/// the one in front falls back to the one the user was in before it rather
/// than to whatever happens to be next in the tab bar.
/// </summary>
/// <remarks>
/// Upstream keeps one of these per window and has them listen to each other,
/// so a second window with nothing open follows the first. There is one window
/// here (FD5 puts a second one post-v1), so the cross-window half is not
/// ported; what remains is the ordering, which the document list and
/// Window &gt; Next/Previous both read.
/// </remarks>
public sealed class HistoryManager
{
    private readonly List<EditorDocument> _documents = new List<EditorDocument>();
    private readonly DocumentManager _manager;

    /// <summary>Creates the history over a document manager.</summary>
    /// <param name="manager">The open documents.</param>
    public HistoryManager(DocumentManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _documents.AddRange(manager.Documents);

        //Priority matters upstream (it connects with 1 so it runs before the
        //window's own handlers); here the window asks this object what to
        //raise, so ordering falls out of who calls whom.
        manager.DocumentCreated += (_, e) => AddDocument(e.Document);
        manager.DocumentClosed += (_, e) => RemoveDocument(e.Document);
        manager.CurrentDocumentChanged += (_, e) => SetCurrentDocument(e.Document);
    }

    /// <summary>
    /// Gets the documents in order of most recently active first.
    /// </summary>
    /// <returns>The documents.</returns>
    public IReadOnlyList<EditorDocument> Documents()
        => Enumerable.Reverse(_documents).ToList();

    /// <summary>
    /// Gets the document that should become active when one is closed, or null
    /// when the list would be left empty.
    /// </summary>
    /// <param name="closing">The document about to close.</param>
    /// <returns>The document to raise, or null.</returns>
    /// <remarks>Upstream does the raising itself from inside its
    /// <c>removeDocument</c>; here the window owns that decision, because it
    /// also has to save metainfo and put a new empty document up when the last
    /// one goes.</remarks>
    public EditorDocument SuccessorOf(EditorDocument closing)
    {
        int index = _documents.IndexOf(closing);
        if (index < 0 || index != _documents.Count - 1) { return null; }

        //Only a document that is CURRENTLY in front needs a successor; closing
        //one in the background leaves the front one where it is.
        return _documents.Count > 1 ? _documents[_documents.Count - 2] : null;
    }

    private void AddDocument(EditorDocument document)
    {
        //Upstream inserts at -1: a newly created document goes BEHIND the
        //current one, because creating it is not the same as switching to it.
        int index = Math.Max(_documents.Count - 1, 0);
        _documents.Insert(index, document);
    }

    private void RemoveDocument(EditorDocument document)
        => _documents.Remove(document);

    private void SetCurrentDocument(EditorDocument document)
    {
        if (document == null) { return; }

        _documents.Remove(document);
        _documents.Add(document);
    }
}
