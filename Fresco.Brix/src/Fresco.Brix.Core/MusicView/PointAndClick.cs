// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Lex;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilyPondTokens = Fresco.Brix.Ly.Lex.LilyPondMode;

namespace Fresco.Brix.MusicView; //was previously: frescobaldi/pointandclick.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The point-and-click links of one engraved score, grouped by the source file
/// they point into and, once that file is open, bound to positions in it.
/// </summary>
/// <remarks>
/// <para>
/// A link's usefulness outlives the text it was engraved from: the user edits,
/// and the note that was at line 8 column 24 is now somewhere else. Upstream
/// solves this by turning every link into a QTextCursor as soon as the document
/// is open, so the editor's own change tracking moves them. The editor here
/// gives the same thing through <see cref="ITextAnchor"/>, and it is used the
/// same way.
/// </para>
/// </remarks>
public class PointAndClickLinks
{
    private readonly Dictionary<string, Dictionary<(int Line, int Column), List<object>>> _links
        = new Dictionary<string, Dictionary<(int, int), List<object>>>(StringComparer.Ordinal);
    private readonly Dictionary<string, BoundLinks> _bound
        = new Dictionary<string, BoundLinks>(StringComparer.Ordinal);

    /// <summary>Records a link.</summary>
    /// <param name="fileName">The source file it points into.</param>
    /// <param name="line">The 1-based line.</param>
    /// <param name="column">The 0-based character index within the line.</param>
    /// <param name="destination">Whatever describes where the link points FROM.</param>
    public void AddLink(string fileName, int line, int column, object destination)
    {
        if (!_links.TryGetValue(fileName, out var byPosition))
        {
            byPosition = new Dictionary<(int, int), List<object>>();
            _links[fileName] = byPosition;
        }

        if (!byPosition.TryGetValue((line, column), out List<object> destinations))
        {
            destinations = new List<object>();
            byPosition[(line, column)] = destinations;
        }

        destinations.Add(destination);
    }

    /// <summary>
    /// Binds every file that is already open, and follows documents opening and
    /// closing from then on.
    /// </summary>
    /// <param name="documents">The open documents.</param>
    public void Finish(DocumentManager documents)
    {
        if (documents == null) { return; }

        foreach (string fileName in _links.Keys.ToList())
        {
            EditorDocument document = ScratchDir.FindDocument(documents, fileName)
                ?? documents.FindDocument(fileName);
            if (document != null) { Bind(fileName, document); }
        }

        documents.DocumentLoaded += OnDocumentLoaded;
        documents.DocumentClosed += OnDocumentClosed;
        Documents = documents;
    }

    /// <summary>Gets the document manager this set follows, once bound.</summary>
    public DocumentManager Documents { get; private set; }

    /// <summary>Stops following the document manager.</summary>
    public void Detach()
    {
        if (Documents == null) { return; }

        Documents.DocumentLoaded -= OnDocumentLoaded;
        Documents.DocumentClosed -= OnDocumentClosed;
        Documents = null;
    }

    /// <summary>Binds a file's links to an open document.</summary>
    /// <param name="fileName">The file.</param>
    /// <param name="document">The document.</param>
    public void Bind(string fileName, EditorDocument document)
    {
        if (_bound.ContainsKey(fileName) || !_links.TryGetValue(fileName, out var positions)) { return; }

        _bound[fileName] = new BoundLinks(document, positions);
    }

    /// <summary>Returns the bound links of a document, or null.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The bound links.</returns>
    public BoundLinks BoundLinksFor(EditorDocument document)
        => _bound.Values.FirstOrDefault(b => b.Document == document);

    /// <summary>Returns where in a document a link points, or null.</summary>
    /// <param name="fileName">The source file.</param>
    /// <param name="line">The 1-based line.</param>
    /// <param name="column">The 0-based character index within the line.</param>
    /// <param name="load">Whether to open the file when it is not open.</param>
    /// <returns>The document and offset, or null.</returns>
    public (EditorDocument Document, int Offset)? Cursor(
        string fileName, int line, int column, bool load = false)
    {
        if (_bound.TryGetValue(fileName, out BoundLinks bound)) { return bound.Cursor(line, column); }

        if (!load || Documents == null || !File.Exists(fileName)) { return null; }

        EditorDocument document = Documents.OpenDocument(fileName);
        if (document == null) { return null; }

        Bind(fileName, document);
        return _bound.TryGetValue(fileName, out bound) ? bound.Cursor(line, column) : null;
    }

    private void OnDocumentLoaded(object sender, DocumentEventArgs e)
    {
        string path = e.Document?.Path;
        if (!string.IsNullOrEmpty(path) && _links.ContainsKey(PathUtil.NormPath(path)))
        {
            Bind(PathUtil.NormPath(path), e.Document);
        }
    }

    private void OnDocumentClosed(object sender, DocumentEventArgs e)
    {
        string key = _bound.FirstOrDefault(kv => kv.Value.Document == e.Document).Key;
        if (key != null) { _bound.Remove(key); }
    }
}

/// <summary>
/// One source document's links, held as anchors so the editor moves them when
/// the user types.
/// </summary>
public sealed class BoundLinks
{
    private readonly Dictionary<(int Line, int Column), ITextAnchor> _byPosition
        = new Dictionary<(int, int), ITextAnchor>();
    private readonly List<ITextAnchor> _anchors = new List<ITextAnchor>();
    private readonly List<List<object>> _destinations = new List<List<object>>();

    /// <summary>Creates the anchors for one document's links.</summary>
    /// <param name="document">The document.</param>
    /// <param name="links">The links, keyed by line and column.</param>
    public BoundLinks(
        EditorDocument document, IReadOnlyDictionary<(int Line, int Column), List<object>> links)
    {
        Document = document;
        TextDocument store = document.Document;
        foreach (var entry in links.OrderBy(kv => kv.Key.Line).ThenBy(kv => kv.Key.Column))
        {
            (int line, int column) = entry.Key;
            if (line < 1 || line > store.LineCount) { continue; }

            DocumentLine documentLine = store.GetLineByNumber(line);
            int offset = Math.Min(documentLine.Offset + column, documentLine.EndOffset);
            ITextAnchor anchor = store.CreateAnchor(offset);
            anchor.SurviveDeletion = true;
            _byPosition[entry.Key] = anchor;
            _anchors.Add(anchor);
            _destinations.Add(entry.Value);
        }
    }

    /// <summary>Gets the document these links point into.</summary>
    public EditorDocument Document { get; }

    /// <summary>Gets the anchors, in document order.</summary>
    public IReadOnlyList<ITextAnchor> Anchors => _anchors;

    /// <summary>Gets the destinations, matched to <see cref="Anchors"/> by index.</summary>
    public IReadOnlyList<List<object>> Destinations => _destinations;

    /// <summary>Returns where a link points, or null when it was never bound.</summary>
    /// <param name="line">The 1-based line.</param>
    /// <param name="column">The 0-based character index within the line.</param>
    /// <returns>The document and offset.</returns>
    public (EditorDocument Document, int Offset)? Cursor(int line, int column)
        => _byPosition.TryGetValue((line, column), out ITextAnchor anchor)
            ? (Document, anchor.Offset)
            : null;

    /// <summary>
    /// Returns which destinations a caret position or selection points at.
    /// </summary>
    /// <param name="start">The selection's start, or the caret offset.</param>
    /// <param name="end">The selection's end, or the caret offset.</param>
    /// <param name="lyDocument">The tokenized view of the same document.</param>
    /// <returns>
    /// The range of destinations, or null when the caret is nowhere near one,
    /// or an empty range when earlier highlighting should be cleared.
    /// </returns>
    /// <remarks>
    /// The trickery is upstream's, and it is what makes clicking just after a
    /// slur highlight the slur: when the nearest link is BEHIND the caret, the
    /// tokens between them are read backwards looking for the closing half of a
    /// slur, phrasing slur or beam, and if one is found its opening half is the
    /// link that gets highlighted.
    /// </remarks>
    public (int Start, int Length)? Indices(int start, int end, DocumentBase lyDocument)
    {
        if (_anchors.Count == 0) { return null; }

        if (end > start)
        {
            int last = FindLink(end - 1);
            if (last >= 0)
            {
                int first = FindLink(start);
                if (first < 0 || _anchors[first].Offset < start) { first++; }

                if (first <= last) { return (first, last - first + 1); }
            }

            return (0, 0);
        }

        int index = FindLink(start);
        if (index < 0) { return null; }

        ITextAnchor anchor = _anchors[index];
        if (anchor.Offset < start)
        {
            //The slur search needs the tokens; the line comparison does not,
            //and upstream makes it either way.
            int? opening = lyDocument == null
                ? null
                : FindMatchStartBefore(lyDocument, anchor.Offset, start);
            if (opening.HasValue)
            {
                int candidate = FindLink(opening.Value);
                if (candidate < 0 || _anchors[candidate].Offset != opening.Value) { return null; }

                index = candidate;
            }
            else if (LineOf(anchor.Offset) != LineOf(start))
            {
                return (0, 0);
            }
        }

        return (index, 1);
    }

    private int LineOf(int offset)
    {
        TextDocument store = Document.Document;
        offset = Math.Clamp(offset, 0, store.TextLength);
        return store.GetLineByOffset(offset).LineNumber;
    }

    private int FindLink(int position)
    {
        int lo = 0;
        int hi = _anchors.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (position < _anchors[mid].Offset) { hi = mid; } else { lo = mid + 1; }
        }

        return lo - 1;
    }

    private int? FindMatchStartBefore(DocumentBase lyDocument, int previous, int position)
    {
        var runner = new Runner(lyDocument);
        runner.SetPosition(position);
        string name = null;
        foreach (Token token in runner.BackwardLine())
        {
            int tokenPosition = runner.Position();
            if (tokenPosition <= previous) { break; }

            if (tokenPosition > position) { continue; }

            if (token is IMatchEnd matchEnd
                && (matchEnd.MatchName == "slur" || matchEnd.MatchName == "phrasingslur"
                    || matchEnd.MatchName == "beam"))
            {
                name = matchEnd.MatchName;
            }

            break;
        }

        if (name == null) { return null; }

        int nest = 1;
        foreach (Token token in runner.Backward())
        {
            if (token is IMatchStart start && start.MatchName == name)
            {
                nest--;
                if (nest == 0) { return runner.Position(); }
            }
            else if (token is IMatchEnd end && end.MatchName == name)
            {
                nest++;
            }
        }

        return null;
    }
}
