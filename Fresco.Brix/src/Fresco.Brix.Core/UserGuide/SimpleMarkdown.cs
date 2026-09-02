// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fresco.Brix.UserGuide; //was previously: frescobaldi/simplemarkdown.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// SimpleMarkdown — the basic markdown-like parser the user guide's pages are
/// written in, and the free functions that go with it.
/// </summary>
/// <remarks>
/// <para>
/// The module is pure text processing and is ported whole, quirks included.
/// It supports headings (<c>=</c> to <c>===</c>), paragraphs, unordered,
/// ordered and definition lists with nesting by indent, fenced code blocks
/// with an optional language specifier, and the inline forms
/// <c>*emphasis*</c>, <c>`code`</c> and <c>[link text]</c>. Block quotes are
/// not supported, upstream says so itself, and they are not supported here.
/// </para>
/// <para>
/// Everything here is verified against Frescobaldi's own module rather than
/// against this port: <c>tools/userguideprobe/gen-userguide-fixtures.py</c>
/// runs <c>simplemarkdown.py</c> over all 80 shipped pages and 43 hand-written
/// corner cases and records the parse tree and the HTML each one produces.
/// The module imports nothing but <c>contextlib</c>, so board trap 49 applies:
/// an oracle that imports clean needs no shim at all.
/// </para>
/// </remarks>
public static class SimpleMarkdown
{
    /// <summary>
    /// Returns what <c>string.TrimStart(chars)</c> would chop off the front.
    /// </summary>
    /// <param name="text">The string.</param>
    /// <param name="characters">The characters to strip, or null for
    /// whitespace.</param>
    /// <returns>The chopped-off prefix.</returns>
    /// <remarks>
    /// ⚠ Upstream is <c>string[:-len(string.lstrip(chars))]</c>, and Python's
    /// <c>-0</c> is <c>0</c>: when the strip leaves NOTHING, the slice is
    /// <c>string[:0]</c> and the answer is the EMPTY string rather than the
    /// whole of it. A heading line of nothing but <c>=</c> and spaces takes
    /// that path. The quirk is reproduced deliberately — the parse of such a
    /// line differs without it.
    /// </remarks>
    public static string ChopLeft(string text, string characters = null)
    {
        if (string.IsNullOrEmpty(text)) { return string.Empty; }

        string stripped = LeftStrip(text, characters);
        return stripped.Length == 0
            ? string.Empty
            : text.Substring(0, text.Length - stripped.Length);
    }

    /// <summary>Python's <c>str.lstrip</c>.</summary>
    /// <param name="text">The string.</param>
    /// <param name="characters">The characters to strip, or null for
    /// whitespace.</param>
    /// <returns>The stripped string.</returns>
    public static string LeftStrip(string text, string characters = null)
    {
        if (string.IsNullOrEmpty(text)) { return text ?? string.Empty; }

        int index = 0;
        while (index < text.Length && IsStripped(text[index], characters)) { index++; }

        return text.Substring(index);
    }

    /// <summary>Python's <c>str.rstrip</c>.</summary>
    /// <param name="text">The string.</param>
    /// <param name="characters">The characters to strip, or null for
    /// whitespace.</param>
    /// <returns>The stripped string.</returns>
    public static string RightStrip(string text, string characters = null)
    {
        if (string.IsNullOrEmpty(text)) { return text ?? string.Empty; }

        int end = text.Length;
        while (end > 0 && IsStripped(text[end - 1], characters)) { end--; }

        return text.Substring(0, end);
    }

    /// <summary>Python's <c>str.strip</c>.</summary>
    /// <param name="text">The string.</param>
    /// <param name="characters">The characters to strip, or null for
    /// whitespace.</param>
    /// <returns>The stripped string.</returns>
    public static string Strip(string text, string characters = null)
        => RightStrip(LeftStrip(text, characters), characters);

    /// <summary>
    /// Finds the first occurrence in the text of any of the given characters.
    /// </summary>
    /// <param name="text">The string to search.</param>
    /// <param name="characters">The characters to look for.</param>
    /// <param name="start">Where to start looking.</param>
    /// <param name="end">Where to stop looking, or null for the end.</param>
    /// <returns>The character found (or null) and the position; the position
    /// is meaningless when no character was found.</returns>
    /// <remarks>Upstream's <c>find_first</c>, whose returned position is the
    /// running search limit and is therefore <c>end</c> when nothing is
    /// found — the caller only looks at it when a character came back.</remarks>
    public static (char? Character, int? Position) FindFirst(
        string text, string characters, int start = 0, int? end = null)
    {
        int? position = end;
        char? found = null;

        foreach (char candidate in characters)
        {
            int index = IndexOf(text, candidate, start, position);
            if (index == start) { return (candidate, start); }

            if (index != -1)
            {
                found = candidate;
                position = index;
            }
        }

        return (found, position);
    }

    /// <summary>
    /// Yields pairs of the text before and after each separator.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="separator">The separator.</param>
    /// <returns>The pairs.</returns>
    /// <remarks>Upstream's <c>iter_split</c>: it splits into at most three
    /// parts each time round, so an ODD trailing separator ends the walk with
    /// the remainder handed back whole.</remarks>
    public static IEnumerable<(string Before, string After)> IterSplit(
        string text, string separator)
    {
        while (true)
        {
            List<string> parts = SplitAtMost(text, separator, 2);
            if (parts.Count < 3)
            {
                if (text.Length > 0) { yield return (text, string.Empty); }

                yield break;
            }

            yield return (parts[0], parts[1]);
            text = parts[2];
        }
    }

    /// <summary>
    /// Yields pairs of the text outside and inside a pair of separators.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="separator">The opening separator.</param>
    /// <param name="separator2">The closing separator.</param>
    /// <returns>The pairs.</returns>
    /// <remarks>Upstream's <c>iter_split2</c>, used to parse
    /// <c>text with [bracketed words] in it</c> and, in the user guide, the
    /// <c>_(</c> … <c>)_</c> islands inside an untranslated paragraph.</remarks>
    public static IEnumerable<(string Outside, string Inside)> IterSplit2(
        string text, string separator, string separator2)
    {
        while (true)
        {
            List<string> parts = SplitAtMost(text, separator, 1);
            if (parts.Count > 1)
            {
                List<string> rest = SplitAtMost(parts[1], separator2, 1);
                if (rest.Count > 1)
                {
                    yield return (parts[0], rest[0]);
                    text = rest[1];
                    continue;
                }
            }

            if (text.Length > 0) { yield return (text, string.Empty); }

            yield break;
        }
    }

    /// <summary>Converts markdown text to HTML.</summary>
    /// <param name="text">The markdown text.</param>
    /// <returns>The HTML.</returns>
    public static string Html(string text)
    {
        MarkdownHtmlOutput output = new MarkdownHtmlOutput();
        new MarkdownParser().Parse(text, output);
        return output.Html();
    }

    /// <summary>Converts INLINE markdown text to HTML.</summary>
    /// <param name="text">The markdown text.</param>
    /// <returns>The HTML: links, emphasis and code, and nothing block-level.</returns>
    public static string HtmlInline(string text)
    {
        MarkdownParser parser = new MarkdownParser();
        MarkdownHtmlOutput output = new MarkdownHtmlOutput();
        parser.Output = output;
        parser.ParseInlineText(text);
        return output.Html();
    }

    /// <summary>Escapes <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    /// <remarks>Upstream escapes exactly these three and no more — quotes are
    /// escaped at the one place they matter, in <c>tag()</c>'s attributes.</remarks>
    public static string HtmlEscape(string text)
        => (text ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

    /// <summary>Parses markdown text into a tree.</summary>
    /// <param name="text">The markdown text.</param>
    /// <returns>The tree.</returns>
    public static MarkdownTree Tree(string text)
    {
        MarkdownTree tree = new MarkdownTree();
        new MarkdownParser().Parse(text, tree);
        return tree;
    }

    /// <summary>
    /// Formats a value the way Python's <c>repr()</c> would.
    /// </summary>
    /// <param name="value">A string, an integer or null.</param>
    /// <returns>The representation.</returns>
    /// <remarks>Used only by <see cref="MarkdownTree.Dump"/>, whose output is
    /// upstream's own debugging format; keeping the format identical is what
    /// lets the tree recorded from Frescobaldi be compared as text.</remarks>
    internal static string PythonRepr(object value)
    {
        if (value == null) { return "None"; }

        if (value is string text)
        {
            //Python picks the quote: single, unless the string holds a single
            //quote and no double quote.
            char quote = text.IndexOf('\'') >= 0 && text.IndexOf('"') < 0 ? '"' : '\'';
            StringBuilder builder = new StringBuilder();
            builder.Append(quote);
            foreach (char character in text)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character == quote)
                        {
                            builder.Append('\\').Append(character);
                        }
                        else if (character < 0x20 || character == 0x7f)
                        {
                            builder.Append(
                                "\\x" + ((int)character).ToString(
                                    "x2", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            //Python 3 keeps printable non-ASCII as itself.
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append(quote);
            return builder.ToString();
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a node's arguments as a Python tuple would print.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The representation, e.g. <c>()</c>, <c>('x',)</c>.</returns>
    internal static string PythonTuple(IReadOnlyList<object> arguments)
    {
        if (arguments == null || arguments.Count == 0) { return "()"; }

        if (arguments.Count == 1) { return "(" + PythonRepr(arguments[0]) + ",)"; }

        StringBuilder builder = new StringBuilder("(");
        for (int index = 0; index < arguments.Count; index++)
        {
            if (index > 0) { builder.Append(", "); }

            builder.Append(PythonRepr(arguments[index]));
        }

        return builder.Append(')').ToString();
    }

    /// <summary>Splits text the way Python's <c>str.split(sep, maxsplit)</c> does.</summary>
    /// <param name="text">The text.</param>
    /// <param name="separator">The separator.</param>
    /// <param name="maximum">The maximum number of splits.</param>
    /// <returns>The parts.</returns>
    internal static List<string> SplitAtMost(string text, string separator, int maximum)
    {
        List<string> parts = new List<string>();
        int start = 0;
        while (parts.Count < maximum)
        {
            int index = text.IndexOf(separator, start, StringComparison.Ordinal);
            if (index < 0) { break; }

            parts.Add(text.Substring(start, index - start));
            start = index + separator.Length;
        }

        parts.Add(text.Substring(start));
        return parts;
    }

    /// <summary>
    /// Splits on runs of whitespace the way Python's <c>str.split(None, n)</c>
    /// does: leading whitespace is skipped and never produces an empty part.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="maximum">The maximum number of splits.</param>
    /// <returns>The parts.</returns>
    internal static List<string> SplitWhitespace(string text, int maximum = int.MaxValue)
    {
        List<string> parts = new List<string>();
        int index = 0;
        while (true)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) { index++; }

            if (index >= text.Length) { break; }

            if (parts.Count == maximum)
            {
                parts.Add(text.Substring(index));
                break;
            }

            int start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index])) { index++; }

            parts.Add(text.Substring(start, index - start));
        }

        return parts;
    }

    /// <summary>Splits into lines the way Python's <c>str.splitlines</c> does.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The lines, without their terminators.</returns>
    /// <remarks>Python splits on more boundaries than this (vertical tab, form
    /// feed, U+2028…); the guide's pages are plain LF text and the extra
    /// boundaries have never appeared in one.</remarks>
    internal static List<string> SplitLines(string text)
    {
        List<string> lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { return lines; }

        int start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                int end = index > start && text[index - 1] == '\r' ? index - 1 : index;
                lines.Add(text.Substring(start, end - start));
                start = index + 1;
            }
        }

        if (start < text.Length) { lines.Add(text.Substring(start)); }

        return lines;
    }

    private static bool IsStripped(char character, string characters)
        => characters == null
            ? char.IsWhiteSpace(character)
            : characters.IndexOf(character) >= 0;

    private static int IndexOf(string text, char character, int start, int? end)
    {
        int limit = end ?? text.Length;
        if (limit > text.Length) { limit = text.Length; }

        if (start >= limit) { return -1; }

        int index = text.IndexOf(character, start, limit - start);
        return index;
    }
}
