// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/template.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Turning the document a user is in into a template they can start a new one
/// from.
/// </summary>
public static class SnippetTemplate
{
    /// <summary>The header line a template carries.</summary>
    public const string HeaderLine = "-*- template; indent: no;";

    /// <summary>The extra marker that engraves a new document at once.</summary>
    public const string RunMarker = " template-run;";

    /// <summary>
    /// Makes a template's text from a document, marking where the caret and
    /// the selection's anchor are.
    /// </summary>
    /// <param name="text">The document's text.</param>
    /// <param name="selectionStart">Where the selection starts.</param>
    /// <param name="selectionEnd">Where it ends, or the same as the start.</param>
    /// <param name="engraveOnUse">Whether a new document should be engraved
    /// as soon as it is made.</param>
    /// <returns>The template text.</returns>
    /// <remarks>Every <c>$</c> already in the document is doubled, so that a
    /// document holding one does not turn into an expansion.</remarks>
    public static string FromDocument(
        string text,
        int selectionStart,
        int selectionEnd,
        bool engraveOnUse = false)
    {
        text ??= string.Empty;
        List<(int Position, string Marker)> markers
            = new List<(int, string)> { (selectionEnd, "${CURSOR}") };
        if (selectionEnd > selectionStart)
        {
            markers.Add((selectionStart, "${ANCHOR}"));
            markers.Sort((a, b) => a.Position.CompareTo(b.Position));
        }

        StringBuilder builder = new StringBuilder();
        builder.Append(HeaderLine);
        if (engraveOnUse) { builder.Append(RunMarker); }

        builder.Append('\n');

        int previous = 0;
        foreach (var (position, marker) in markers)
        {
            int at = Math.Clamp(position, 0, text.Length);
            builder.Append(text.Substring(previous, at - previous).Replace("$", "$$"));
            builder.Append(marker);
            previous = at;
        }

        builder.Append(text.Substring(previous).Replace("$", "$$"));
        return builder.ToString();
    }
}
