// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/app.py (the document list and its signals)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The open documents, in tab order, and the announcements everything else
/// listens to: a document was created, loaded, saved, renamed, modified or
/// closed, and the current one changed.
/// </summary>
/// <remarks>
/// Upstream keeps this in module-level state on <c>app</c> (a list plus a
/// dozen module-level signals). Here it is a service so the tests can make one
/// per test instead of unpicking global state between them.
/// </remarks>
public sealed class DocumentManager
{
    private readonly List<EditorDocument> _documents = new List<EditorDocument>();
    private EditorDocument _current;

    /// <summary>Raised after a document joins the list.</summary>
    public event EventHandler<DocumentEventArgs> DocumentCreated;

    /// <summary>Raised after a document's text is (re)loaded from its file.</summary>
    public event EventHandler<DocumentEventArgs> DocumentLoaded;

    /// <summary>Raised after a document is written to its file.</summary>
    public event EventHandler<DocumentEventArgs> DocumentSaved;

    /// <summary>Raised when a document's modified flag changes.</summary>
    public event EventHandler<DocumentEventArgs> DocumentModificationChanged;

    /// <summary>Raised when a document's file changes.</summary>
    public event EventHandler<DocumentEventArgs> DocumentUrlChanged;

    /// <summary>Raised after a document leaves the list.</summary>
    public event EventHandler<DocumentEventArgs> DocumentClosed;

    /// <summary>Raised when a different document becomes the current one.</summary>
    public event EventHandler<DocumentEventArgs> CurrentDocumentChanged;

    /// <summary>Gets the open documents, in tab order.</summary>
    public IReadOnlyList<EditorDocument> Documents => _documents;

    /// <summary>Gets or sets the document the user is working in.</summary>
    public EditorDocument CurrentDocument
    {
        get => _current;
        set
        {
            if (value == _current) { return; }

            _current = value;
            CurrentDocumentChanged?.Invoke(this, new DocumentEventArgs(value));
        }
    }

    /// <summary>Creates an empty document and adds it.</summary>
    /// <returns>The document.</returns>
    public EditorDocument CreateDocument()
    {
        EditorDocument document = new EditorDocument();
        Add(document);
        return document;
    }

    /// <summary>
    /// Opens a file — or answers the document already showing it, so a file
    /// never ends up in two tabs.
    /// </summary>
    /// <param name="path">The file to open.</param>
    /// <param name="encoding">The encoding, or null to detect it.</param>
    /// <returns>The document.</returns>
    public EditorDocument OpenDocument(string path, Encoding encoding = null)
    {
        EditorDocument existing = FindDocument(path);
        if (existing != null) { return existing; }

        EditorDocument document = EditorDocument.NewFromPath(path, encoding);
        Add(document);
        DocumentLoaded?.Invoke(this, new DocumentEventArgs(document));
        return document;
    }

    /// <summary>Finds the open document for a file, or null.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The document, or null.</returns>
    public EditorDocument FindDocument(string path)
    {
        if (string.IsNullOrEmpty(path)) { return null; }

        string full = System.IO.Path.GetFullPath(path);
        return _documents.FirstOrDefault(
            d => string.Equals(d.Path, full, StringComparison.Ordinal));
    }

    /// <summary>
    /// Closes a document: it leaves the list, and if it was the current one
    /// the next document along takes over.
    /// </summary>
    /// <param name="document">The document to close.</param>
    public void CloseDocument(EditorDocument document)
    {
        int index = _documents.IndexOf(document);
        if (index < 0) { return; }

        bool wasCurrent = document == _current;
        _documents.RemoveAt(index);
        Unhook(document);
        document.RaiseClosed();
        DocumentClosed?.Invoke(this, new DocumentEventArgs(document));

        if (!wasCurrent) { return; }

        //Upstream raises the tab that took the closed one's place, falling
        //back to the last one when the closed tab was at the end.
        _current = null;
        CurrentDocument = _documents.Count == 0
            ? null
            : _documents[Math.Min(index, _documents.Count - 1)];
    }

    /// <summary>Adds an already-made document to the list.</summary>
    /// <param name="document">The document.</param>
    public void Add(EditorDocument document)
    {
        if (document == null) { throw new ArgumentNullException(nameof(document)); }

        if (_documents.Contains(document)) { return; }

        AssignNumber(document);
        _documents.Add(document);
        Hook(document);
        DocumentCreated?.Invoke(this, new DocumentEventArgs(document));

        //The first document becomes the current one, and that is ANNOUNCED:
        //assigning the field directly would leave the window with a document
        //nothing had been told to show.
        if (_current == null)
        {
            CurrentDocument = document;
        }
    }

    /// <summary>Moves a document to a new position, as a tab drag does.</summary>
    /// <param name="from">The current index.</param>
    /// <param name="to">The wanted index.</param>
    public void MoveDocument(int from, int to)
    {
        if (from < 0 || from >= _documents.Count) { return; }
        if (to < 0 || to >= _documents.Count) { return; }

        EditorDocument document = _documents[from];
        _documents.RemoveAt(from);
        _documents.Insert(to, document);
    }

    /// <summary>
    /// Assigns the number a nameless document displays: one more than the
    /// highest already in use, so a name is never reused while it is on screen.
    /// </summary>
    /// <param name="document">The document.</param>
    private void AssignNumber(EditorDocument document)
        => document.SetNumber(document.Path != null
            ? 0
            : _documents.Where(d => d != document)
                .Select(d => d.Number)
                .Append(0)
                .Max() + 1);

    private void Hook(EditorDocument document)
    {
        document.Loaded += OnLoaded;
        document.Saved += OnSaved;
        document.ModificationChanged += OnModificationChanged;
        document.UrlChanged += OnUrlChanged;
    }

    private void Unhook(EditorDocument document)
    {
        document.Loaded -= OnLoaded;
        document.Saved -= OnSaved;
        document.ModificationChanged -= OnModificationChanged;
        document.UrlChanged -= OnUrlChanged;
    }

    private void OnLoaded(object sender, EventArgs e)
        => DocumentLoaded?.Invoke(this, new DocumentEventArgs((EditorDocument)sender));

    private void OnSaved(object sender, EventArgs e)
        => DocumentSaved?.Invoke(this, new DocumentEventArgs((EditorDocument)sender));

    private void OnModificationChanged(object sender, EventArgs e)
        => DocumentModificationChanged?.Invoke(
            this, new DocumentEventArgs((EditorDocument)sender));

    private void OnUrlChanged(object sender, UrlChangedEventArgs e)
    {
        EditorDocument document = (EditorDocument)sender;

        //Saving a nameless document under a name retires its number.
        AssignNumber(document);
        DocumentUrlChanged?.Invoke(this, new DocumentEventArgs(document));
    }
}

/// <summary>The document an announcement is about.</summary>
public sealed class DocumentEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="document">The document, or null.</param>
    public DocumentEventArgs(EditorDocument document) => Document = document;

    /// <summary>Gets the document, or null.</summary>
    public EditorDocument Document { get; }
}
