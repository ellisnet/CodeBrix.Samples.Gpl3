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
/// <para>
/// Upstream keeps one of these per window and has them listen to each other:
/// a window with NOTHING open follows whatever another window makes current.
/// //was previously: that half was left out, because there is one window and
/// FD5 was unbuilt. FD5 is built (see <see cref="RemoteInstance"/>), so it is
/// ported here in full — the static <see cref="CurrentDocumentSet"/> is
/// upstream's module-level <c>_setCurrentDocument</c> signal, and
/// <see cref="Listen"/> is its <c>_listen</c> slot, re-entrancy guard and all.
/// </para>
/// <para>
/// With one window there is one listener and nothing to follow; the mechanism
/// is exercised by the tests, which make two managers over one document list,
/// and comes alive the day a second window does.
/// </para>
/// </remarks>
public sealed class HistoryManager
{
    //Upstream's `with _setCurrentDocument.blocked()': while one manager is
    //making a document current in answer to the signal, the announcement that
    //causes must not come back round to the managers again.
    private static bool _blocked;

    private readonly List<EditorDocument> _documents = new List<EditorDocument>();
    private readonly DocumentManager _manager;
    private bool _hasCurrent;

    /// <summary>Creates the history over a document manager.</summary>
    /// <param name="manager">The open documents.</param>
    /// <param name="other">The history of the window this one was opened from,
    /// whose order the new one starts with; null to start from the document
    /// list as it stands.</param>
    public HistoryManager(DocumentManager manager, HistoryManager other = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _documents.AddRange(other != null ? other._documents : manager.Documents);
        _hasCurrent = _documents.Count > 0;

        //Priority matters upstream (it connects with 1 so it runs before the
        //window's own handlers); here the window asks this object what to
        //raise, so ordering falls out of who calls whom.
        manager.DocumentCreated += (_, e) => AddDocument(e.Document);
        manager.DocumentClosed += (_, e) => RemoveDocument(e.Document);
        manager.CurrentDocumentChanged += (_, e) => SetCurrentDocument(e.Document);
        CurrentDocumentSet += OnCurrentDocumentSet;
    }

    /// <summary>
    /// Raised whenever ANY window makes a document current, so a window with
    /// nothing open can follow it.
    /// </summary>
    /// <remarks>Upstream's module-level <c>_setCurrentDocument</c> signal.</remarks>
    public static event EventHandler<DocumentEventArgs> CurrentDocumentSet;

    /// <summary>
    /// Gets or sets how this history's own window is told to show a document.
    /// </summary>
    /// <remarks>Upstream calls <c>self.mainwindow().setCurrentDocument(doc)</c>;
    /// the window is a weak reference there and a delegate here. The default
    /// sets it on the document manager the history was made over.</remarks>
    public Action<EditorDocument> SetCurrentDocumentInWindow { get; set; }

    /// <summary>Gets whether this window has a current document at all.</summary>
    /// <remarks>Upstream's <c>_has_current</c>: false only after the LAST
    /// document has gone, which is the state that makes a window follow.</remarks>
    public bool HasCurrent => _hasCurrent;

    /// <summary>Stops following other windows.</summary>
    /// <remarks>Upstream's manager dies with its window and Python's weak
    /// references take care of it; a static C# event has to be let go of by
    /// hand or it keeps the manager, and the window, alive.</remarks>
    public void Detach() => CurrentDocumentSet -= OnCurrentDocumentSet;

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
    {
        int index = _documents.IndexOf(document);
        if (index < 0) { return; }

        //Upstream: closing the ACTIVE document with nothing behind it leaves
        //the window with no current document, and that is the state in which
        //it starts following other windows.
        if (index == _documents.Count - 1 && _documents.Count == 1)
        {
            _hasCurrent = false;
        }

        _documents.RemoveAt(index);
    }

    private void SetCurrentDocument(EditorDocument document)
    {
        if (document == null) { return; }

        _documents.Remove(document);
        _documents.Add(document);
        _hasCurrent = true;

        //Notify possible interested parties — the other windows.
        if (_blocked) { return; }

        CurrentDocumentSet?.Invoke(this, new DocumentEventArgs(document));
    }

    private void OnCurrentDocumentSet(object sender, DocumentEventArgs e)
    {
        if (ReferenceEquals(sender, this) || _hasCurrent || e.Document == null) { return; }

        //Prevent nested emits of this signal from reacting windows.
        _blocked = true;
        try
        {
            Action<EditorDocument> show = SetCurrentDocumentInWindow
                ?? (document => _manager.CurrentDocument = document);
            show(e.Document);
        }
        finally
        {
            _blocked = false;
        }
    }
}
