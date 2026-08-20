// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.Texinfo2Pdf;

namespace Lily.Docs.Rendering;

/// <summary>
/// Puts the package chain's own TEXT families into the PDF stage's per-glyph fallback
/// chain, which they do not join by themselves.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ THE SVG TEXT PATH AND THE HTML TEXT PATH DO NOT SHARE A FALLBACK CHAIN BY DEFAULT,
/// AND THE DIFFERENCE IS INVISIBLE FROM EITHER SIDE. MEASURED at wave LD4 against
/// Texinfo2Pdf 1.0.232.110: a Greek Ψ set in <c>font-family="serif"</c> resolves to
/// Merriweather, which carries no Greek at all, and in HTML it renders — the chain reaches
/// a covering face — while in an engraved SVG the identical run DROPS. Only
/// <c>Noto Music</c> joins the chain on its own, which is why the ♭ in the very same
/// picture came out correctly and the Greek beside it did not.
/// </para>
/// <para>
/// So this is the one call the board predicted LD4 might owe — and it turned out to be four
/// calls, not one, because the families are chosen to COVER what the corpus actually asks
/// for rather than to look tidy:
/// </para>
/// <list type="bullet">
/// <item><description><c>Noto Serif</c> — Ψ α ω in <c>notation-appendices</c>'s Unicode
/// example. Merriweather has none of the three; Noto Serif has all three and ships inside
/// the very same font package.</description></item>
/// <item><description><c>Merriweather</c> — U+2192 RIGHTWARDS ARROW, twice, in a
/// <c>monospace</c> run. ⚠ THIS ONE WAS IN NOBODY'S PREDICTION: the board's drop table
/// listed Hebrew, Cyrillic and Greek and stopped there, and the arrow only appeared when
/// the render was measured. Roboto Mono does not carry it; Merriweather does.</description></item>
/// <item><description><c>Roboto</c> and <c>Roboto Mono</c> — the remaining two defaults,
/// added so the chain is "every text family the package ships" rather than "the two faces
/// that happened to be missing on the day". A fallback family is consulted ONLY for
/// characters the resolved face already lacks, so a wider chain cannot change a glyph that
/// was already rendering.</description></item>
/// </list>
/// <para>
/// MEASURED effect on the Notation Reference, on the same three pictures that carry every
/// one of its drops: 26 distinct dropped code points / 29 occurrences before, 22 / 24
/// after. What remains is HEBREW, and it is irreducible — U+05D0–U+05EA is carried by NONE
/// of the twelve font families reachable through this chain, checked cmap by cmap. Decision
/// D47 covers it and Phase 5 does not chase it.
/// </para>
/// <para>
/// ⚠ Registration is PROCESS-GLOBAL — it is static state on the package's own font
/// registry, not a per-render option — so it is done once and the fact is recorded here
/// rather than discovered by a later reader wondering why a second render behaves like the
/// first.
/// </para>
/// </remarks>
public static class PdfTextFallback
{
    private static readonly object Gate = new object();
    private static bool _registered;

    /// <summary>
    /// The families added to the fallback chain, in the order they are added.
    /// </summary>
    /// <remarks>
    /// Public so a gate can assert the list rather than trusting that the call was made:
    /// with the chain being global and additive, a missing registration shows up only as
    /// characters quietly reverting to the missing-glyph path.
    /// </remarks>
    public static IReadOnlyList<string> Families { get; } = new[]
    {
        "Noto Serif", "Merriweather", "Roboto", "Roboto Mono",
    };

    /// <summary>Registers the fallback families, once per process.</summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            foreach (string family in Families)
            {
                TexinfoPdfFonts.AddFallbackFamily(family);
            }

            _registered = true;
        }
    }
}
