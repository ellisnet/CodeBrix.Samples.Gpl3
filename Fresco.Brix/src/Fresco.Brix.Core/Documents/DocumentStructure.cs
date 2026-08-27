// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Lex;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/documentstructure.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One thing the outline lists.</summary>
public sealed class OutlineItem
{
    /// <summary>Creates an item.</summary>
    /// <param name="position">Where it starts in the document.</param>
    /// <param name="text">The matched text.</param>
    /// <param name="isTitle">Whether it is shown as a heading.</param>
    /// <param name="isAlert">Whether it is shown as an alert.</param>
    public OutlineItem(int position, string text, bool isTitle, bool isAlert)
    {
        Position = position;
        Text = text ?? string.Empty;
        IsTitle = isTitle;
        IsAlert = isAlert;
    }

    /// <summary>Gets where the item starts.</summary>
    public int Position { get; }

    /// <summary>Gets the matched text.</summary>
    public string Text { get; }

    /// <summary>Gets whether the item is a heading (a <c>title</c> group
    /// matched).</summary>
    public bool IsTitle { get; }

    /// <summary>Gets whether the item is an alert — a FIXME, HACK or
    /// XXX — and is drawn in red.</summary>
    public bool IsAlert { get; }
}

/// <summary>
/// An overview of what is in a document: its scores, books, paper and layout
/// blocks, variable assignments and the notes the author left in comments.
/// </summary>
/// <remarks>
/// The patterns are settings, and the user can change them (the preferences
/// page is W12); the defaults are upstream's. Two sets are kept, because one
/// half is deliberately matched in comments too and the other deliberately is
/// not.
/// </remarks>
public sealed class DocumentStructure : Plugin<EditorDocument, DocumentStructure>
{
    /// <summary>The settings key the code patterns live under.</summary>
    public const string PatternsKey = "documentstructure/outline_patterns";

    /// <summary>The settings key the comment patterns live under.</summary>
    public const string CommentPatternsKey
        = "documentstructure/outline_patterns_comments";

    /// <summary>The default patterns, which are ignored inside comments.</summary>
    public static readonly IReadOnlyList<string> DefaultPatterns = new[]
    {
        @"(?<title>\\(score|book|bookpart))\b",
        @"^\\(paper|layout|header)\b",
        @"\\(new|context)\s+[A-Z]\w+",
        @"^[a-zA-Z]+\s*=",
        @"^<<",
        @"^\{",
        @"^\\relative([ \t]+\w+[',]*)?",
    };

    /// <summary>The default patterns, which are matched inside comments too.</summary>
    public static readonly IReadOnlyList<string> DefaultCommentPatterns = new[]
    {
        @"(?<title>BEGIN[^\n]*)[ \t]*$",
        @"\b(?<alert>(FIXME|HACK|XXX+)\b\W*\w+)",
    };

    private static Regex _codeExpression;
    private static Regex _commentExpression;

    private IReadOnlyList<OutlineItem> _outline;

    private DocumentStructure(EditorDocument document)
        : base(document)
        => document.ContentsChanged += (_, _) => Invalidate();

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the structure of a document, creating it on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The structure.</returns>
    public static DocumentStructure For(EditorDocument document)
        => Instance(document, owner => new DocumentStructure(owner));

    /// <summary>Forgets the compiled patterns, after a settings change.</summary>
    public static void ResetPatterns()
    {
        _codeExpression = null;
        _commentExpression = null;
    }

    /// <summary>Gets the expression the outline is found with.</summary>
    /// <param name="comments">Whether it is the set that matches in comments.</param>
    /// <param name="settings">The settings the patterns come from, or null for
    /// the defaults.</param>
    /// <returns>The expression.</returns>
    /// <remarks>
    /// Upstream renumbers duplicate named groups before joining the patterns,
    /// because Python rejects a pattern that names one group twice. .NET
    /// allows it and treats the repeats as ONE group whose captures combine —
    /// which is the behaviour the renaming was working around — so the
    /// patterns are joined as they are, and asking whether <c>title</c>
    /// matched still answers for whichever alternative fired.
    /// </remarks>
    public static Regex OutlineExpression(bool comments, SettingsStore settings = null)
    {
        ref Regex cached = ref comments ? ref _commentExpression : ref _codeExpression;
        return cached ??= Compile(Patterns(comments, settings));
    }

    /// <summary>Gets the patterns in force.</summary>
    /// <param name="comments">Whether it is the comment set.</param>
    /// <param name="settings">The settings, or null for the defaults.</param>
    /// <returns>The patterns.</returns>
    public static IReadOnlyList<string> Patterns(
        bool comments, SettingsStore settings = null)
    {
        IReadOnlyList<string> defaults
            = comments ? DefaultCommentPatterns : DefaultPatterns;
        string key = comments ? CommentPatternsKey : PatternsKey;
        string stored = settings?.GetString(key);
        return string.IsNullOrEmpty(stored)
            ? defaults
            : stored.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Gets the document's outline, recomputing it when stale.</summary>
    /// <returns>The items, in document order.</returns>
    public IReadOnlyList<OutlineItem> Outline()
        => _outline ??= Compute();

    /// <summary>Forgets the computed outline.</summary>
    public void Invalidate() => _outline = null;

    /// <summary>
    /// Answers the document's text with every comment blanked out — same
    /// length, same positions, no comments.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The text.</returns>
    public static string RemoveComments(EditorDocument document)
    {
        string text = document?.Text ?? string.Empty;
        var plain = new Ly.Document(text);
        var cursor = new Cursor(plain);
        var source = new Source(
            cursor, null, stateFromDocument: true, tokensWithPosition: true);
        var builder = new StringBuilder(text);
        int blockStart = 0;

        foreach (var token in source)
        {
            if (token is BlockCommentStart)
            {
                blockStart = token.Pos;
            }
            else if (token is BlockCommentEnd)
            {
                if (blockStart > 0)
                {
                    Blank(builder, blockStart, token.End);
                    blockStart = 0;
                }
            }
            else if (token is Comment)
            {
                Blank(builder, token.Pos, token.End);
            }
        }

        return builder.ToString();
    }

    private static void Blank(StringBuilder builder, int start, int end)
    {
        int stop = Math.Min(end, builder.Length);
        for (int i = Math.Max(0, start); i < stop; i++)
        {
            //Newlines stay: the patterns are multiline, and blanking one would
            //join two lines into a match that is not there.
            if (builder[i] != '\n') { builder[i] = ' '; }
        }
    }

    private static Regex Compile(IEnumerable<string> patterns)
    {
        List<string> usable = new List<string>();
        foreach (var pattern in patterns)
        {
            try
            {
                _ = new Regex(pattern);
                usable.Add(pattern);
            }
            catch (ArgumentException)
            {
                //A pattern the user mistyped is skipped, exactly as upstream
                //skips one that will not compile.
            }
        }

        return new Regex(
            usable.Count == 0 ? "(?!)" : string.Join("|", usable),
            RegexOptions.Multiline | RegexOptions.Compiled);
    }

    private IReadOnlyList<OutlineItem> Compute()
    {
        EditorDocument document = Document;
        if (document == null) { return Array.Empty<OutlineItem>(); }

        SettingsStore settings = DocumentEditorState.For(document)?.Settings;
        string text = document.Text;
        List<OutlineItem> items = new List<OutlineItem>();

        items.AddRange(ItemsFrom(
            OutlineExpression(false, settings), RemoveComments(document)));
        items.AddRange(ItemsFrom(OutlineExpression(true, settings), text));
        items.Sort((a, b) => a.Position.CompareTo(b.Position));
        return items;
    }

    private static IEnumerable<OutlineItem> ItemsFrom(Regex expression, string text)
    {
        foreach (Match match in expression.Matches(text))
        {
            //A pattern with no groups at all still matches something; an empty
            //match would give the outline a row with no text.
            if (match.Length == 0) { continue; }

            yield return new OutlineItem(
                match.Index,
                match.Value,
                match.Groups["title"].Success,
                match.Groups["alert"].Success);
        }
    }
}
