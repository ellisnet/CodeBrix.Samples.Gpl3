// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// The glyph outlines of a music font, read out of the SVG font that ships beside its
/// OTF.
/// <para>
/// New-in-family, and a translation in spirit of <c>output-svg.scm</c>'s
/// <c>svg-defs</c> / <c>glyph-element-regexp</c> / <c>extract-glyph</c> trio. Upstream
/// pattern-matches the <c>&lt;glyph&gt;</c> element out of the font file with a regular
/// expression and hands the <c>d</c> attribute to the backend untouched, with a
/// standing <c>TODO</c> wishing for an XML library and a hash table. This is that hash
/// table: the whole file is scanned ONCE per font and the answers cached, instead of a
/// fresh regexp search per glyph drawn.
/// </para>
/// <para>
/// The outline text is deliberately NOT reformatted. It is copied byte for byte,
/// including any newline inside the attribute value, because the SVG the port emits is
/// compared against the SVG upstream emits and upstream emits exactly these bytes.
/// </para>
/// </summary>
public sealed class SvgFontOutlines
{
    // Mirrors output-svg.scm's glyph-path-regexp character class, which admits the
    // newlines FontForge writes inside a long outline.
    private static readonly Regex GlyphElement = new Regex(
        "<glyph(?<before>(\\s+[-a-z]+=\"[^\"]*\")*)\\s+glyph-name=\"(?<name>[^\"]*)\""
        + "(?<after>(\\s+[-a-z]+=\"[^\"]*\")*)\\s*/>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PathAttribute = new Regex(
        "\\bd=\"(?<d>[-+MmZzLlHhVvCcSsQqTtAa0-9,.Ee\\n ]*)\"",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AdvanceAttribute = new Regex(
        "\\bhoriz-adv-x=\"(?<x>[-0-9.]+)\"",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly Dictionary<string, string> _outlines
        = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly Dictionary<string, double> _advances
        = new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>Initializes the table by scanning an SVG font document.</summary>
    /// <param name="document">The whole SVG font file text.</param>
    public SvgFontOutlines(string document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        // Upstream searches only within <defs>...</defs>; anything outside it is
        // metadata rather than glyphs.
        int start = document.IndexOf("<defs>", StringComparison.Ordinal);
        int end = document.IndexOf("</defs>", StringComparison.Ordinal);
        string glyphs = start >= 0 && end > start
            ? document.Substring(start + 6, end - start - 6)
            : document;

        foreach (Match match in GlyphElement.Matches(glyphs))
        {
            string name = match.Groups["name"].Value;
            if (name.Length == 0 || _outlines.ContainsKey(name))
            {
                // An alist with duplicate keys keeps the first, and so does this.
                continue;
            }

            string attributes = match.Groups["before"].Value + match.Groups["after"].Value;

            Match path = PathAttribute.Match(attributes);

            // A glyph with no path data is a space. Upstream returns "" for it, which
            // is not the same as an unknown glyph, so it is recorded rather than
            // skipped.
            _outlines[name] = path.Success ? path.Groups["d"].Value : string.Empty;

            Match advance = AdvanceAttribute.Match(attributes);
            if (advance.Success
                && double.TryParse(
                    advance.Groups["x"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double x))
            {
                _advances[name] = x;
            }
        }
    }

    /// <summary>Gets the number of glyphs the font describes.</summary>
    public int Count => _outlines.Count;

    /// <summary>
    /// Returns a glyph's outline path data, in FONT units.
    /// </summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>
    /// The <c>d</c> attribute text; the empty string for a glyph that has no outline,
    /// such as a space; <see langword="null"/> when the font has no such glyph.
    /// </returns>
    public string Outline(string glyphName)
        => glyphName != null && _outlines.TryGetValue(glyphName, out string d) ? d : null;

    /// <summary>Returns a glyph's horizontal advance, in font units.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns>The advance, or 0 when the glyph does not record one.</returns>
    public double Advance(string glyphName)
        => glyphName != null && _advances.TryGetValue(glyphName, out double x) ? x : 0.0;
}
