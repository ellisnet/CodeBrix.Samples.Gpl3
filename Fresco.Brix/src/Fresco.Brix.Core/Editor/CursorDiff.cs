// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/cursordiff.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Replaces a stretch of text by changing only the parts that actually
/// differ, so anything anchored inside it — another cursor, a bookmark, a
/// highlight, a fold — survives.
/// <para>
/// The music tools rewrite whole passages (transpose a phrase, re-bar a
/// section). Replacing the passage outright would move every anchor in it to
/// its end; replacing only what changed leaves them where they belong.
/// </para>
/// </summary>
public static class CursorDiff
{
    /// <summary>
    /// Replaces a range with new text, editing only the parts that differ.
    /// </summary>
    /// <param name="document">The text store.</param>
    /// <param name="offset">Where the replaced range starts.</param>
    /// <param name="length">How long the replaced range is.</param>
    /// <param name="text">The replacement.</param>
    /// <returns>The offset just past the new text.</returns>
    public static int Replace(
        TextDocument document, int offset, int length, string text)
    {
        if (document == null) { throw new ArgumentNullException(nameof(document)); }

        text ??= string.Empty;

        //Nothing to be clever about when there is no selection, or when the
        //replacement is empty: upstream takes the same shortcut.
        if (length <= 0 || text.Length == 0)
        {
            document.Replace(offset, length, text);
            return offset + text.Length;
        }

        string old = document.GetText(offset, length);
        if (string.Equals(old, text, StringComparison.Ordinal))
        {
            return offset + text.Length;
        }

        //The edits are applied back to front, so an earlier edit's offsets are
        //still valid when it is reached.
        List<(int Start, int End, string Text)> edits = Differences(old, text)
            .Select(d => (offset + d.Start, offset + d.End, d.Text))
            .OrderByDescending(d => d.Item1)
            .ToList();

        //One undo step for the whole rewrite, as upstream's compress_undo does.
        using (document.RunUpdate())
        {
            foreach (var (start, end, replacement) in edits)
            {
                document.Replace(start, end - start, replacement);
            }
        }

        return offset + text.Length;
    }

    /// <summary>
    /// Finds the stretches of <paramref name="old"/> that differ from
    /// <paramref name="text"/>, as (start, end, replacement) in
    /// <paramref name="old"/>'s coordinates.
    /// </summary>
    /// <param name="old">The existing text.</param>
    /// <param name="text">The replacement text.</param>
    /// <returns>The differing stretches, in order.</returns>
    /// <remarks>
    /// Upstream uses python's <c>difflib.SequenceMatcher</c>. A full
    /// longest-common-subsequence diff would be quadratic in the passage
    /// length for no benefit here: the caller is rewriting a passage it just
    /// derived from this very text, so the parts that match are the unchanged
    /// head and tail. Trimming those and replacing the middle keeps every
    /// anchor outside the changed span — which is the whole point — at linear
    /// cost.
    /// </remarks>
    public static IEnumerable<(int Start, int End, string Text)> Differences(
        string old, string text)
    {
        int prefix = 0;
        int limit = Math.Min(old.Length, text.Length);
        while (prefix < limit && old[prefix] == text[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < limit - prefix
            && old[old.Length - 1 - suffix] == text[text.Length - 1 - suffix])
        {
            suffix++;
        }

        if (prefix == old.Length && prefix == text.Length)
        {
            yield break;
        }

        yield return (
            prefix,
            old.Length - suffix,
            text.Substring(prefix, text.Length - suffix - prefix));
    }
}
