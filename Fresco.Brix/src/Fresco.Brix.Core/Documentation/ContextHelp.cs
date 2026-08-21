// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using Words = Fresco.Brix.Ly.Words;

namespace Fresco.Brix.Documentation;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Where contextual help decided to send the reader.</summary>
public sealed class ContextHelpTarget
{
    /// <summary>Creates a target.</summary>
    /// <param name="term">The word that was looked up.</param>
    /// <param name="manual">The manual to open.</param>
    /// <param name="page">The 1-based page, or 1 when nothing matched.</param>
    /// <param name="entry">The heading that matched, or null.</param>
    internal ContextHelpTarget(
        string term, ManualDefinition manual, int page, ManualOutlineEntry entry)
    {
        Term = term;
        Manual = manual;
        Page = page;
        Entry = entry;
    }

    /// <summary>Gets the word that was looked up.</summary>
    public string Term { get; }

    /// <summary>Gets the manual to open.</summary>
    public ManualDefinition Manual { get; }

    /// <summary>Gets the 1-based page to show.</summary>
    public int Page { get; }

    /// <summary>Gets the heading that matched, or null when none did.</summary>
    public ManualOutlineEntry Entry { get; }

    /// <summary>Gets whether a heading actually named the word.</summary>
    public bool IsExact => Entry != null;
}

/// <summary>
/// Contextual help: the word the caret is on, and the page of the manual that
/// documents it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Frescobaldi's
/// <c>help_lilypond_context</c> action exists, carries the title
/// "&amp;Contextual LilyPond Help", is given the shortcut Shift+F9 and is added
/// to the Help menu — and is connected to NOTHING.
/// <c>docbrowser/__init__.py</c> creates it at line 76 and the only other
/// mentions of it in the whole tree are its icon, its shortcut, its text and
/// <c>menu.py:498</c> putting it on the menu. Pressing it does nothing at all.
/// A menu entry with a title and a shortcut that is wired to no slot does not
/// do what its own interface says it does, so this port implements it. The
/// board asks for it in as many words: W10's scope is "context-help entry
/// points resolving into the right manual (deep-linking to page = stretch)".
/// </para>
/// <para>
/// It resolves BOTH halves. Which manual is decided from the port's own data —
/// a grob, engraver, context or property goes to the Internals Reference,
/// markup goes to the Notation Reference, a Scheme word goes to Extending —
/// and which PAGE is decided by searching that manual's own table of contents,
/// which the PDF carries as its bookmark tree (591 headings in the Notation
/// Reference, 810 in the Internals Reference, every one of them with a page).
/// </para>
/// </remarks>
public sealed class ContextHelp
{
    private readonly ManualLibrary _library;

    /// <summary>Creates the resolver over a library of manuals.</summary>
    /// <param name="library">The manuals.</param>
    public ContextHelp(ManualLibrary library)
        => _library = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>
    /// Trims a token down to the word to look up: no backslash, no hash, no
    /// trailing punctuation.
    /// </summary>
    /// <param name="token">The token text, or any word.</param>
    /// <returns>The term, or null when nothing is left.</returns>
    public static string TermOf(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) { return null; }

        string term = token.Trim();
        term = term.TrimStart('\\', '#', '\'', '$');
        term = term.Trim('"', '(', ')', '{', '}', '.', ',', ';', ':', '!', '?');

        //A property path — Staff.NoteHead.color — asks about its last part;
        //a grob-property override is the commonest thing a reader stops on.
        int dot = term.LastIndexOf('.');
        if (dot >= 0 && dot < term.Length - 1) { term = term.Substring(dot + 1); }

        return string.IsNullOrWhiteSpace(term) ? null : term;
    }

    /// <summary>
    /// Works out which manuals to search for a term, best first.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns>The manual names, in search order.</returns>
    /// <remarks>
    /// The decision is made from the port's own regenerated LilyPond data
    /// rather than from the shape of the word, so it is right about the words
    /// the engine actually knows. A word in none of the lists still gets a
    /// sensible order: the Notation Reference is where a reader of a
    /// <c>.ly</c> file most often needs to be.
    /// </remarks>
    public static IReadOnlyList<string> SearchOrder(string term)
    {
        //Order is by where a term is DOCUMENTED, and the fall-through is the
        //rest of the catalogue in reading order, so a search never stops
        //early just because the first guess was wrong.
        string[] head = IsInternalsTerm(term)
            ? new[] { "internals", "notation", "learning" }
            : IsSchemeTerm(term)
                ? new[] { "extending", "notation", "internals" }
                : new[] { "notation", "learning", "usage", "extending", "internals" };

        List<string> order = new List<string>(head);
        foreach (ManualDefinition manual in ManualCatalog.All)
        {
            if (!order.Contains(manual.Name, StringComparer.Ordinal)) { order.Add(manual.Name); }
        }

        return order;
    }

    /// <summary>Finds the manual page that documents a word.</summary>
    /// <param name="token">The token or word the caret is on.</param>
    /// <returns>The target, or null when there is no word to look up and no
    /// manual installed.</returns>
    /// <remarks>
    /// A term that matches no heading anywhere still returns a target — the
    /// first installed manual of the search order, at page one — because
    /// "here is where to start looking" is more use than nothing happening,
    /// which is exactly what upstream's unconnected action does.
    /// </remarks>
    public ContextHelpTarget Resolve(string token)
    {
        string term = TermOf(token);
        ManualDefinition fallback = null;
        ContextHelpTarget partial = null;

        foreach (string name in SearchOrder(term))
        {
            ManualDefinition manual = ManualCatalog.Find(name);
            if (manual == null || !_library.IsInstalled(manual)) { continue; }

            fallback ??= manual;
            if (term == null) { break; }

            IReadOnlyList<ManualOutlineEntry> outline = _library.OutlineOf(manual);
            ManualOutlineEntry exact = Match(outline, term, whole: true);
            if (exact != null)
            {
                return new ContextHelpTarget(term, manual, exact.Page, exact);
            }

            if (partial == null)
            {
                ManualOutlineEntry loose = Match(outline, term, whole: false);
                if (loose != null)
                {
                    partial = new ContextHelpTarget(term, manual, loose.Page, loose);
                }
            }
        }

        if (partial != null) { return partial; }

        return fallback == null ? null : new ContextHelpTarget(term, fallback, 1, null);
    }

    private static ManualOutlineEntry Match(
        IReadOnlyList<ManualOutlineEntry> outline, string term, bool whole)
    {
        foreach (string candidate in Candidates(term))
        {
            foreach (ManualOutlineEntry entry in outline)
            {
                if (entry.Page < 1) { continue; }

                if (whole)
                {
                    if (string.Equals(entry.Heading, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
                else if (ContainsWord(entry.Heading, candidate))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The forms of a term worth looking for, best first.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns>The candidates.</returns>
    /// <remarks>
    /// The manuals head their sections with the NOUN, usually plural —
    /// <c>\tuplet</c> is documented under "Tuplets" and <c>\clef</c> under
    /// "Clef" — so a lookup that only tried the word itself would miss most of
    /// what a reader stops on.
    /// </remarks>
    private static IEnumerable<string> Candidates(string term)
    {
        yield return term;

        if (term.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            yield return term.Substring(0, term.Length - 1);
        }
        else
        {
            yield return term + "s";
            if (term.EndsWith("h", StringComparison.OrdinalIgnoreCase)
                || term.EndsWith("x", StringComparison.OrdinalIgnoreCase))
            {
                yield return term + "es";
            }
        }
    }

    private static bool ContainsWord(string heading, string term)
    {
        int index = heading.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool before = index == 0 || !char.IsLetterOrDigit(heading[index - 1]);
            int after = index + term.Length;
            bool ends = after >= heading.Length || !char.IsLetterOrDigit(heading[after]);
            if (before && ends) { return true; }

            index = heading.IndexOf(term, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsInternalsTerm(string term)
    {
        if (term == null) { return false; }

        return Words.Contains(LyData.Grobs(), term)
            || Words.Contains(LyData.Engravers(), term)
            || Words.Contains(Words.Contexts, term)
            || Words.Contains(LyData.ContextProperties(), term)
            || Words.Contains(LyData.AllGrobProperties(), term);
    }

    private static bool IsSchemeTerm(string term)
    {
        if (term == null) { return false; }

        return Words.Contains(LyData.SchemeKeywords(), term)
            || Words.Contains(LyData.SchemeFunctions(), term)
            || Words.Contains(LyData.SchemeVariables(), term)
            || Words.Contains(LyData.SchemeConstants(), term);
    }
}
