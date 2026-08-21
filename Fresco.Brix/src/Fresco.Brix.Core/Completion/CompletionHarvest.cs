// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Lex;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Scheme = Fresco.Brix.Ly.Lex.SchemeMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Completion; //was previously: frescobaldi/autocomplete/harvest.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What the completions know about THIS document: the variables it defines,
/// the markup commands it defines, the words it uses in its lyrics, markup,
/// strings and comments, and the same three from the files it includes.
/// </summary>
public static class CompletionHarvest
{
    /// <summary>
    /// Words worth offering: five characters or more, or two-plus with a
    /// hyphen or colon in them.
    /// </summary>
    private static readonly Regex WordExpression
        = new Regex(@"\w{5,}|\w{2,}(?:[:-]\w+)+", RegexOptions.Compiled);

    /// <summary>Gets the document's own information up to a position.</summary>
    /// <param name="document">The document.</param>
    /// <param name="position">The offset to stop at.</param>
    /// <returns>The information.</returns>
    public static DocInfo DocInfoUntil(EditorDocument document, int position)
        => DocumentInfo.For(document).DocInfo().Range(0, position);

    /// <summary>Gets the variables the document defines before a position.</summary>
    /// <param name="document">The document.</param>
    /// <param name="position">The offset.</param>
    /// <returns>The names.</returns>
    public static IEnumerable<string> Names(EditorDocument document, int position)
        => DocInfoUntil(document, position).Definitions().Select(t => t.Text);

    /// <summary>Gets the markup commands the document defines before a position.</summary>
    /// <param name="document">The document.</param>
    /// <param name="position">The offset.</param>
    /// <returns>The names.</returns>
    public static IEnumerable<string> MarkupCommands(
        EditorDocument document, int position)
        => DocInfoUntil(document, position).MarkupDefinitions().Select(t => t.Text);

    /// <summary>Gets the scheme words used anywhere in the document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The words.</returns>
    public static IEnumerable<string> SchemeWords(EditorDocument document)
        => AllTokens(document)
            .Where(t => t.GetType() == typeof(Scheme.Word))
            .Select(t => t.Text);

    /// <summary>
    /// Gets the words used in the document's strings, lyrics, markup and
    /// comments.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The words.</returns>
    public static IEnumerable<string> Words(EditorDocument document)
    {
        foreach (var token in AllTokens(document))
        {
            if (token is not (StringBase or Comment or Unparsed
                or Lily.MarkupWord or Lily.LyricText))
            {
                continue;
            }

            foreach (Match match in WordExpression.Matches(token.Text))
            {
                yield return match.Value;
            }
        }
    }

    /// <summary>Gets the variables the included files define.</summary>
    /// <param name="document">The document.</param>
    /// <param name="position">The offset.</param>
    /// <returns>The names.</returns>
    public static IEnumerable<string> IncludeIdentifiers(
        EditorDocument document, int position)
        => IncludedFiles(document, position)
            .SelectMany(f => LyFileInfo.DocInfo(f).Definitions())
            .Select(t => t.Text);

    /// <summary>Gets the markup commands the included files define.</summary>
    /// <param name="document">The document.</param>
    /// <param name="position">The offset.</param>
    /// <returns>The names.</returns>
    public static IEnumerable<string> IncludeMarkupCommands(
        EditorDocument document, int position)
        => IncludedFiles(document, position)
            .SelectMany(f => LyFileInfo.DocInfo(f).MarkupDefinitions())
            .Select(t => t.Text);

    /// <summary>Gets the files the document includes, transitively.</summary>
    /// <param name="document">The document.</param>
    /// <param name="position">The offset to read includes up to.</param>
    /// <returns>The file names.</returns>
    public static IReadOnlyCollection<string> IncludedFiles(
        EditorDocument document, int position)
    {
        try
        {
            return LyFileInfo.IncludeFiles(
                DocInfoUntil(document, position),
                DocumentInfo.For(document).IncludePath());
        }
        catch (System.IO.IOException)
        {
            //A half-typed \include names a file that is not there yet; that is
            //not something to interrupt the user's typing over.
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<Token> AllTokens(EditorDocument document)
    {
        DocumentEditorState state = DocumentEditorState.For(document);
        return state == null
            ? Enumerable.Empty<Token>()
            : TokenIter.AllTokens(state.Highlighter);
    }
}
