// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Search; //was previously: frescobaldi/search/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One place a search term was found.</summary>
public readonly struct SearchMatch
{
    /// <summary>Creates a match.</summary>
    /// <param name="start">Where it starts.</param>
    /// <param name="length">How long it is.</param>
    public SearchMatch(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>Gets where the match starts.</summary>
    public int Start { get; }

    /// <summary>Gets how long the match is.</summary>
    public int Length { get; }

    /// <summary>Gets where the match ends.</summary>
    public int End => Start + Length;
}

/// <summary>
/// Finding a term in a document and working out what a replacement produces —
/// everything the search bar does that is not a widget.
/// </summary>
public static class SearchLogic
{
    /// <summary>
    /// Finds every occurrence of a term.
    /// </summary>
    /// <param name="text">The whole document text.</param>
    /// <param name="term">What to look for.</param>
    /// <param name="caseSensitive">Whether case matters.</param>
    /// <param name="regex">Whether the term is a regular expression.</param>
    /// <param name="rangeStart">Where to start looking.</param>
    /// <param name="rangeEnd">Where to stop, or -1 for the end of the text.</param>
    /// <returns>The matches, in document order and in DOCUMENT offsets.</returns>
    /// <remarks>
    /// Upstream searches with <c>MULTILINE | DOTALL</c>, so <c>^</c> and
    /// <c>$</c> mean line boundaries and <c>.</c> matches a newline; .NET
    /// spells those <c>Multiline</c> and <c>Singleline</c>, and both are set
    /// here for the same reason.
    /// </remarks>
    public static IReadOnlyList<SearchMatch> Find(
        string text,
        string term,
        bool caseSensitive = true,
        bool regex = false,
        int rangeStart = 0,
        int rangeEnd = -1)
    {
        List<SearchMatch> found = new List<SearchMatch>();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
        {
            return found;
        }

        if (rangeStart < 0) { rangeStart = 0; }

        if (rangeEnd < 0 || rangeEnd > text.Length) { rangeEnd = text.Length; }

        if (rangeStart >= rangeEnd) { return found; }

        Regex expression = Compile(term, caseSensitive, regex);
        if (expression == null) { return found; }

        //Matched over the SUBSTRING, as upstream does, so that ^ means the
        //start of a line inside the range rather than of the whole document.
        string slice = text.Substring(rangeStart, rangeEnd - rangeStart);
        foreach (Match match in expression.Matches(slice))
        {
            found.Add(new SearchMatch(rangeStart + match.Index, match.Length));
        }

        return found;
    }

    /// <summary>
    /// Compiles a search term, or answers null when it is not a usable
    /// expression.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <param name="caseSensitive">Whether case matters.</param>
    /// <param name="regex">Whether the term is a regular expression.</param>
    /// <returns>The expression, or null.</returns>
    public static Regex Compile(string term, bool caseSensitive, bool regex)
    {
        if (string.IsNullOrEmpty(term)) { return null; }

        RegexOptions options = RegexOptions.Multiline | RegexOptions.Singleline;
        if (!caseSensitive) { options |= RegexOptions.IgnoreCase; }

        try
        {
            return new Regex(regex ? term : Regex.Escape(term), options);
        }
        catch (ArgumentException)
        {
            //An expression the user is still typing is not an error; upstream
            //simply finds nothing until it compiles.
            return null;
        }
    }

    /// <summary>
    /// Works out what one match should be replaced BY, or null when it should
    /// not be replaced at all.
    /// </summary>
    /// <param name="matchedText">The text the match currently covers.</param>
    /// <param name="term">The search term.</param>
    /// <param name="replacement">The replacement text.</param>
    /// <param name="caseSensitive">Whether case matters.</param>
    /// <param name="regex">Whether the term is a regular expression.</param>
    /// <returns>The replacement, or null.</returns>
    /// <remarks>
    /// The check that the text STILL matches is upstream's, and it is what
    /// makes Replace safe: the positions were found before the user started
    /// replacing, and a replacement that changed the text under a later one
    /// must not go through.
    /// </remarks>
    public static string ReplacementFor(
        string matchedText,
        string term,
        string replacement,
        bool caseSensitive = true,
        bool regex = false)
    {
        if (matchedText == null) { return null; }

        if (!regex)
        {
            return string.Equals(matchedText, term, StringComparison.Ordinal)
                ? replacement ?? string.Empty
                : null;
        }

        Regex expression = Compile(term, caseSensitive, regex: true);
        Match match = expression?.Match(matchedText);
        if (match is not { Success: true }) { return null; }

        try
        {
            //Upstream expands the template against the match, so \1 and $1
            //style back-references work; .NET's own Result does exactly that
            //with its $1 spelling.
            return match.Result(replacement ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the index of the first match at or after an offset.
    /// </summary>
    /// <param name="matches">The matches, in order.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The index; the count when there is none.</returns>
    public static int BisectLeft(IReadOnlyList<SearchMatch> matches, int offset)
    {
        int low = 0;
        int high = matches.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (matches[middle].Start < offset) { low = middle + 1; } else { high = middle; }
        }

        return low;
    }

    /// <summary>
    /// Finds the index of the first match strictly after an offset.
    /// </summary>
    /// <param name="matches">The matches, in order.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The index; the count when there is none.</returns>
    public static int BisectRight(IReadOnlyList<SearchMatch> matches, int offset)
    {
        int low = 0;
        int high = matches.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (matches[middle].Start <= offset) { low = middle + 1; } else { high = middle; }
        }

        return low;
    }

    /// <summary>
    /// Gets the word to put in the search box when the command is given with
    /// a selection, or the empty string when the selection is not a word.
    /// </summary>
    /// <param name="selectedText">The selected text.</param>
    /// <param name="regex">Whether the search box is in regex mode.</param>
    /// <returns>The term.</returns>
    public static string TermForSelection(string selectedText, bool regex)
    {
        if (string.IsNullOrEmpty(selectedText)) { return string.Empty; }

        //Upstream requires at least one word character, so selecting a run of
        //punctuation does not replace what is already in the box.
        if (!Regex.IsMatch(selectedText, @"\w")) { return string.Empty; }

        return regex ? Regex.Escape(selectedText) : selectedText;
    }
}
