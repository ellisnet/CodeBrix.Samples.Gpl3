// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly;
using Fresco.Brix.Services;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/reformat.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Tidies a document's whitespace without changing a note of its music.
/// </summary>
/// <remarks>
/// <para>
/// What it does, in upstream's own words: removes trailing whitespace; puts a
/// newline after every <c>{</c> or <c>&lt;&lt;</c> that is not closed on the
/// same line, and before the matching <c>}</c> or <c>&gt;&gt;</c>; takes the
/// indent off comment lines written with more than two comment characters.
/// It never removes a newline the user put there, and it leaves scheme, html
/// and strings alone.
/// </para>
/// <para>
/// Both commands work over the selection, or the whole document when there is
/// none.
/// </para>
/// </remarks>
public static class Reformatting
{
    /// <summary>Reformats the selection or the document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="settings">The store the indent settings live in.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="end">Where it ends; equal to <paramref name="start"/> for
    /// no selection.</param>
    public static void Reformat(
        EditorDocument document, SettingsStore settings, int start, int end)
    {
        if (document == null) { return; }

        Reformatter.Reformat(
            CursorFor(document, start, end),
            Indenting.CreateIndenter(settings, document.Text));
    }

    /// <summary>Takes the trailing whitespace off every line.</summary>
    /// <param name="document">The document.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="end">Where it ends.</param>
    public static void RemoveTrailingWhitespace(
        EditorDocument document, int start, int end)
    {
        if (document == null) { return; }

        Reformatter.RemoveTrailingWhitespace(CursorFor(document, start, end));
    }

    private static Cursor CursorFor(EditorDocument document, int start, int end)
    {
        AteLyDocument text = DocumentEditorState.For(document).LyDocument;
        return end > start
            ? new Cursor(text, start, end)
            : new Cursor(text, 0, document.Text.Length);
    }
}
