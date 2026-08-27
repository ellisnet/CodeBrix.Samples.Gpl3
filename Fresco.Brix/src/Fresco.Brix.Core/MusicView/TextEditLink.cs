// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Text.RegularExpressions;

namespace Fresco.Brix.MusicView; //was previously: frescobaldi/textedit.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A place in a source file that a <c>textedit:</c> URL points at.
/// </summary>
/// <param name="FileName">The file, percent-decoded.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 0-based character index within the line.</param>
public readonly record struct TextEditPlace(string FileName, int Line, int Column);

/// <summary>Reads the <c>textedit:</c> URLs the engine writes into a page.</summary>
/// <remarks>
/// <para>
/// The engine writes <c>textedit://&lt;file&gt;:&lt;line&gt;:&lt;char&gt;:&lt;column&gt;</c>
/// — four fields, of which the THIRD is the one that matters: it is the 0-based
/// index of the character within the line, and the fourth is a display column
/// that counts a tab as several. Upstream's own regular expression captures the
/// third and discards the fourth, and so does this.
/// </para>
/// </remarks>
public static class TextEditLink
{
    private static readonly Regex Pattern = new Regex(
        @"^textedit://(.*?):(\d+):(\d+)(?::\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Reads a <c>textedit:</c> URL.</summary>
    /// <param name="url">The URL.</param>
    /// <param name="place">The place it points at, when it is one.</param>
    /// <returns>Whether the URL was a valid <c>textedit:</c> URL.</returns>
    public static bool TryParse(string url, out TextEditPlace place)
    {
        place = default;
        if (string.IsNullOrEmpty(url)) { return false; }

        Match match = Pattern.Match(url);
        if (!match.Success) { return false; }

        if (!int.TryParse(match.Groups[2].Value, out int line)
            || !int.TryParse(match.Groups[3].Value, out int column))
        {
            return false;
        }

        place = new TextEditPlace(Uri.UnescapeDataString(match.Groups[1].Value), line, column);
        return true;
    }

    /// <summary>Returns whether a URL is a <c>textedit:</c> URL at all.</summary>
    /// <param name="url">The URL.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsTextEdit(string url)
        => url != null && url.StartsWith("textedit:", StringComparison.Ordinal);
}
