// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Ly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/open_file_at_cursor.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The file named where the caret is — the target of File &gt; Open File at
/// Cursor and of the <c>\include</c> tooltip.
/// </summary>
public static class OpenFileAtCursor
{
    /// <summary>
    /// Finds <c>\include "…"</c> arguments in a line of text.
    /// </summary>
    /// <remarks>Upstream uses this cheap expression for the CTRL-hover target
    /// rather than the tokenizer, because it has to answer for one line while
    /// the user is moving the mouse.</remarks>
    private static readonly Regex IncludeExpression
        = new Regex("(\\\\include\\s*\")([^\"]*)(\")", RegexOptions.Compiled);

    /// <summary>
    /// Gets the absolute paths of the include file named under a position in a
    /// line, or an empty list when the position is not inside one.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="lineText">The text of the line.</param>
    /// <param name="columnInLine">The 0-based offset within the line.</param>
    /// <returns>The paths that exist.</returns>
    public static IReadOnlyList<string> IncludeTargets(
        EditorDocument document, string lineText, int columnInLine)
    {
        if (document == null || lineText == null) { return Array.Empty<string>(); }

        List<string> names = new List<string>();
        Match match = IncludeExpression.Match(lineText);
        while (match.Success)
        {
            int start = match.Index + match.Groups[1].Length;
            if (start <= columnInLine && columnInLine <= match.Index + match.Length - 1)
            {
                names.Add(match.Groups[2].Value);
                break;
            }

            match = IncludeExpression.Match(lineText, match.Index + match.Length);
        }

        return names.Count == 0
            ? Array.Empty<string>()
            : Resolve(document, names, existingOnly: true);
    }

    /// <summary>
    /// Gets the file names mentioned at (or selected by) the caret.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="selectionStart">The offset the selection starts at, or the
    /// caret offset when there is none.</param>
    /// <param name="selectionEnd">The offset it ends at, or the same as the
    /// start when there is none.</param>
    /// <param name="existingOnly">Whether to leave out names with no file.</param>
    /// <returns>The absolute paths.</returns>
    /// <remarks>Upstream asks the tokenizer for <c>\include</c> and
    /// <c>(load …)</c> arguments over the range, and falls back to the
    /// selected text itself when it is one line and neither matched.</remarks>
    public static IReadOnlyList<string> FilenamesAtCursor(
        EditorDocument document,
        int selectionStart,
        int selectionEnd,
        bool existingOnly = true)
    {
        if (document == null) { return Array.Empty<string>(); }

        DocumentEditorState state = DocumentEditorState.For(document);
        AteLyDocument bridge = state?.LyDocument;
        if (bridge == null) { return Array.Empty<string>(); }

        bool hasSelection = selectionEnd > selectionStart;

        //The range starts at the beginning of the selection's first LINE, as
        //upstream's does: an \include is only found whole.
        int start = bridge.GetBlock(selectionStart) is { } block
            ? bridge.Position(block)
            : selectionStart;
        int end = hasSelection
            ? selectionEnd
            : start + (bridge.Text(bridge.GetBlock(selectionStart))?.Length ?? 0) + 1;

        DocInfo info = DocumentInfo.For(document).DocInfo().Range(start, end);
        IReadOnlyList<string> names = info.IncludeArgs();
        if (names.Count == 0) { names = info.SchemeLoadArgs(); }

        if (names.Count == 0 && hasSelection)
        {
            string text = document.Text.Substring(
                selectionStart, selectionEnd - selectionStart);
            if (!text.Trim().Contains('\n'))
            {
                names = new[] { text };
            }
        }

        return names.Count == 0
            ? Array.Empty<string>()
            : Resolve(document, names, existingOnly);
    }

    /// <summary>Gets the directories a document's includes are searched in.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The directories, the document's own first.</returns>
    public static IReadOnlyList<string> SearchPath(EditorDocument document)
    {
        List<string> path = new List<string>();
        string directory = document?.Path == null
            ? null
            : Path.GetDirectoryName(document.Path);
        if (!string.IsNullOrEmpty(directory)) { path.Add(directory); }

        if (document != null)
        {
            path.AddRange(DocumentInfo.For(document).IncludePath());
        }

        return path;
    }

    private static IReadOnlyList<string> Resolve(
        EditorDocument document, IEnumerable<string> names, bool existingOnly)
    {
        IReadOnlyList<string> path = SearchPath(document);
        string directory = path.Count > 0 ? path[0] : null;
        List<string> found = new List<string>();

        foreach (var name in names)
        {
            string resolved = path
                .Select(p => Path.GetFullPath(Path.Combine(p, name)))
                .FirstOrDefault(File.Exists);
            if (resolved != null)
            {
                found.Add(resolved);
            }
            else if (!existingOnly)
            {
                //A name with no file still gets a place to be created in, so
                //that opening it makes a new document in the right directory.
                found.Add(directory == null
                    ? name
                    : Path.GetFullPath(Path.Combine(directory, name)));
            }
        }

        return found;
    }
}
