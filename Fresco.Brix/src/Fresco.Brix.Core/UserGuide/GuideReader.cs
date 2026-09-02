// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Fresco.Brix.UserGuide; //was previously: frescobaldi/userguide/read.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Reading a user-guide page file: the split into the document text and its
/// <c>#</c>-named blocks, and the parser that understands the page format's
/// two extra inline rules.
/// </summary>
/// <remarks>
/// A page is a markdown-like file whose body is followed by blocks introduced
/// by a line of the form <c>#NAME</c> — <c>#SUBDOCS</c>, <c>#SEEALSO</c>,
/// <c>#VARS</c> and, in two pages, <c>#SUBDOCS_TODO</c> and
/// <c>#SUBDOCS_TO_ADD</c>, which nothing reads and which are therefore an
/// author's note rather than a broken link.
/// </remarks>
public static class GuideReader
{
    /// <summary>
    /// The <c>{variable}</c> pattern: lowercase words joined by underscores.
    /// </summary>
    /// <remarks>Upstream's <c>_variable_re</c>. It deliberately does not match
    /// digits or capitals, so a <c>{Grob}</c> in a page's prose is left
    /// alone.</remarks>
    public static readonly Regex VariablePattern
        = new Regex(@"\{([a-z]+(_[a-z]+)*)\}", RegexOptions.Compiled);

    private static readonly Regex BlockPattern
        = new Regex(@"^#([A-Z]\w+)[ \t\f\v]*\r?$",
            RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Splits a page's text into the document and its <c>#</c>-named blocks.
    /// </summary>
    /// <param name="text">The whole file.</param>
    /// <returns>The document text and the blocks, each a list of stripped
    /// lines.</returns>
    public static (string Document, Dictionary<string, List<string>> Blocks)
        SplitDocument(string text)
    {
        text ??= string.Empty;
        Dictionary<string, List<string>> blocks
            = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        MatchCollection matches = BlockPattern.Matches(text);
        if (matches.Count == 0) { return (text, blocks); }

        string document = text.Substring(0, matches[0].Index);
        for (int index = 0; index < matches.Count; index++)
        {
            Match match = matches[index];
            int start = match.Index + match.Length;
            int end = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            blocks[match.Groups[1].Value] = SplitLines(text.Substring(start, end - start));
        }

        return (document, blocks);
    }

    /// <summary>Splits into lines and strips each one; drops nothing else.</summary>
    /// <param name="text">The block's text.</param>
    /// <returns>The lines.</returns>
    /// <remarks>Upstream strips the WHOLE block first, so a block with nothing
    /// in it is an empty list rather than a list holding one empty
    /// string.</remarks>
    public static List<string> SplitLines(string text)
    {
        List<string> lines = new List<string>();
        string stripped = SimpleMarkdown.Strip(text ?? string.Empty);
        if (stripped.Length == 0) { return lines; }

        foreach (string line in SimpleMarkdown.SplitLines(stripped))
        {
            lines.Add(SimpleMarkdown.Strip(line));
        }

        return lines;
    }

    /// <summary>Reads a page file and splits it.</summary>
    /// <param name="path">The full path to the <c>.md</c> file.</param>
    /// <returns>The document text and the blocks.</returns>
    public static (string Document, Dictionary<string, List<string>> Blocks)
        Document(string path)
    {
        if (!path.EndsWith(".md", StringComparison.Ordinal)) { path += ".md"; }

        return SplitDocument(File.ReadAllText(path, System.Text.Encoding.UTF8));
    }
}

/// <summary>
/// The markdown parser with the two rules a user-guide page adds.
/// </summary>
/// <remarks>
/// <para>
/// A paragraph is translated unless it starts with <c>!</c>, in which case
/// only the islands wrapped in <c>_(</c> … <c>)_</c> are; and a paragraph that
/// is nothing but <c>{variables}</c> is not translated at all, because there
/// would be nothing in it to translate.
/// </para>
/// <para>
/// ⚠ RULING FR5.6: the user guide is ENGLISH ONLY. The translation seam is
/// ported all the same — it decides where the <c>!</c> and <c>_(</c> markers
/// are REMOVED, which changes the text on screen whether or not anything is
/// translated — and <see cref="Translate"/> is the identity here.
/// </para>
/// </remarks>
public class GuideParser : MarkdownParser
{
    /// <inheritdoc/>
    public override void ParseInlineText(string text)
    {
        text = (text ?? string.Empty).Replace("\n", " ");
        if (!text.StartsWith("!", StringComparison.Ordinal))
        {
            string result = ProbablyTranslate(text);
            if (!string.IsNullOrEmpty(result)) { base.ParseInlineText(result); }

            return;
        }

        List<string> parts = new List<string>();
        bool missing = false;
        foreach ((string outside, string inside) in
            SimpleMarkdown.IterSplit2(text.Substring(1), "_(", ")_"))
        {
            if (outside.Length > 0) { parts.Add(outside); }

            if (inside.Length > 0)
            {
                string translated = ProbablyTranslate(inside);
                if (translated == null) { missing = true; }

                parts.Add(translated);
            }
        }

        if (!missing) { base.ParseInlineText(string.Concat(parts)); }
    }

    /// <summary>
    /// Translates the string when it is a sensible translatable message.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The translation, or the text.</returns>
    /// <remarks>A string with no letters outside its <c>{variables}</c> is
    /// handed back untouched — there is nothing in it a translator could
    /// act on.</remarks>
    protected string ProbablyTranslate(string text)
    {
        int position = 0;
        foreach (Match match in GuideReader.VariablePattern.Matches(text))
        {
            if (match.Index > position && HasLetter(text, position, match.Index))
            {
                return Translate(text);
            }

            position = match.Index + match.Length;
        }

        return position < text.Length && HasLetter(text, position, text.Length)
            ? Translate(text)
            : text;
    }

    /// <summary>Translates a page's message.</summary>
    /// <param name="text">The message.</param>
    /// <returns>The translation.</returns>
    /// <remarks>⚠ RULING FR5.6 — the guide's page TEXT is English-only and is
    /// NOT looked up in the message catalog, which carries the CHROME's
    /// strings. The method is the seam upstream translates through and is the
    /// identity here.</remarks>
    protected virtual string Translate(string text) => text;

    private static bool HasLetter(string text, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            if (char.IsLetter(text[index])) { return true; }
        }

        return false;
    }
}
