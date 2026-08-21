// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/insert.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What inserting a snippet did.</summary>
public sealed class SnippetInsertion
{
    /// <summary>Creates a result.</summary>
    /// <param name="inserted">Whether anything was inserted.</param>
    /// <param name="selectionStart">Where the caret's anchor should be.</param>
    /// <param name="selectionEnd">Where the caret should be.</param>
    public SnippetInsertion(bool inserted, int selectionStart, int selectionEnd)
    {
        Inserted = inserted;
        SelectionStart = selectionStart;
        SelectionEnd = selectionEnd;
    }

    /// <summary>Gets whether anything was inserted.</summary>
    public bool Inserted { get; }

    /// <summary>Gets where the caret's anchor should be.</summary>
    public int SelectionStart { get; }

    /// <summary>Gets where the caret should be.</summary>
    public int SelectionEnd { get; }
}

/// <summary>
/// Puts a snippet into a document: the text with its variables expanded, the
/// selection dropped in where <c>$SELECTION</c> asked for it, the caret left
/// where <c>$CURSOR</c> asked for it, and the whole thing re-indented.
/// </summary>
/// <remarks>
/// Upstream's <c>insert_python</c> and <c>insert_macro</c> paths are not here.
/// FR5.3 excludes snippet Python code and the extension system with it; the
/// macro snippet is a list of command names, which the same ruling excludes
/// because a user could name any action from it. What remains — and it is the
/// large majority of upstream's shipped library — is the TEMPLATE path.
/// </remarks>
public static class SnippetInserter
{
    /// <summary>Inserts a snippet.</summary>
    /// <param name="library">The snippet library.</param>
    /// <param name="name">The snippet name.</param>
    /// <param name="document">The document.</param>
    /// <param name="selectionStart">Where the selection starts.</param>
    /// <param name="selectionEnd">Where it ends.</param>
    /// <returns>What happened.</returns>
    public static SnippetInsertion Insert(
        SnippetLibrary library,
        string name,
        EditorDocument document,
        int selectionStart,
        int selectionEnd)
    {
        if (library == null || document == null)
        {
            return new SnippetInsertion(false, selectionStart, selectionEnd);
        }

        SnippetText snippet = library.Get(name);
        DocumentEditorState state = DocumentEditorState.For(document);
        TextDocument store = document.Document;
        bool hasSelection = selectionEnd > selectionStart;

        //"selection: yes;" means the snippet needs one.
        if (snippet.VariableHas("selection", "yes") && !hasSelection)
        {
            return new SnippetInsertion(false, selectionStart, selectionEnd);
        }

        if (snippet.VariableHas("selection", "strip") && hasSelection)
        {
            Cursor stripped = new Cursor(
                state.LyDocument, selectionStart, selectionEnd);
            stripped.Strip();
            selectionStart = stripped.Start;
            selectionEnd = stripped.End ?? selectionEnd;
            hasSelection = selectionEnd > selectionStart;
        }

        string selectedText = hasSelection
            ? store.GetText(selectionStart, selectionEnd - selectionStart)
            : string.Empty;

        List<object> events = BuildEvents(
            snippet, document, hasSelection, selectedText);

        int start = selectionStart;
        int caret = -1;
        int anchor = -1;
        int position = start;

        store.BeginUpdate();
        try
        {
            //The selection is replaced by whatever the snippet produces; where
            //$SELECTION appears the ORIGINAL text goes back in.
            if (hasSelection)
            {
                store.Remove(selectionStart, selectionEnd - selectionStart);
            }

            foreach (var item in events)
            {
                switch (item)
                {
                    case SnippetMarker.Anchor:
                        anchor = position;
                        break;

                    case SnippetMarker.Cursor:
                        caret = position;
                        break;

                    case SnippetMarker.Selection:
                        store.Insert(position, selectedText);
                        position += selectedText.Length;
                        break;

                    case string text:
                        store.Insert(position, text);
                        position += text.Length;
                        break;
                }
            }

            //Re-indent the inserted region unless the snippet says not to.
            if (!snippet.VariableHas("indent", "no"))
            {
                DocumentLine first = store.GetLineByOffset(start);
                DocumentLine last = store.GetLineByOffset(
                    Math.Min(position, store.TextLength));
                if (last.LineNumber != first.LineNumber)
                {
                    Indenting.ReIndent(
                        state.LyDocument,
                        Indenting.CreateIndenter(state.Settings, document.Text),
                        first.Offset,
                        Math.Min(position, store.TextLength),
                        indentBlankLines: true);
                }
            }
        }
        finally
        {
            store.EndUpdate();
        }

        if (anchor >= 0 || caret >= 0)
        {
            int from = anchor >= 0 ? anchor : caret;
            int to = caret >= 0 ? caret : anchor;
            return new SnippetInsertion(true, from, to);
        }

        //"selection: keep;" leaves the inserted text selected.
        if (snippet.VariableHas("selection", "keep"))
        {
            return new SnippetInsertion(true, start, position);
        }

        return new SnippetInsertion(true, position, position);
    }

    /// <summary>
    /// Builds the list a snippet expands into: strings and markers, in order.
    /// </summary>
    /// <param name="snippet">The snippet.</param>
    /// <param name="document">The document.</param>
    /// <param name="hasSelection">Whether anything is selected.</param>
    /// <param name="selectedText">The selected text.</param>
    /// <returns>The pieces.</returns>
    public static List<object> BuildEvents(
        SnippetText snippet,
        EditorDocument document,
        bool hasSelection,
        string selectedText)
    {
        SnippetExpander expander = new SnippetExpander(document, hasSelection);
        List<object> events = new List<object>();

        foreach (var part in SnippetParser.Expand(snippet.Text))
        {
            if (part.Text.Length > 0) { events.Add(part.Text); }

            if (part.Expansion.Length == 0) { continue; }

            if (part.Expansion == "$")
            {
                events.Add("$");
                continue;
            }

            string expanded = expander.Expand(part.Expansion, out SnippetMarker marker);
            if (marker != SnippetMarker.None)
            {
                events.Add(marker);
            }
            else if (expanded != null)
            {
                events.Add(expanded);
            }

            //A ${braced} expansion that is not a known variable is upstream's
            //way of writing a comment in a snippet: it produces nothing.
        }

        //"selection: strip;" pads around the selection so that what surrounds
        //it keeps its shape: a newline when the selection spans lines, a space
        //otherwise.
        int index = events.FindIndex(e => e is SnippetMarker.Selection);
        if (index < 0 || !snippet.VariableHas("selection", "strip")) { return events; }

        string space = selectedText.Contains('\n') ? "\n" : " ";
        for (int i = index - 1; i >= 0; i--)
        {
            if (events[i] is not string before) { continue; }

            events[i] = before.TrimEnd() + space;
            break;
        }

        for (int i = index + 1; i < events.Count; i++)
        {
            if (events[i] is not string after) { continue; }

            events[i] = space + after.TrimStart();
            break;
        }

        return events;
    }
}
