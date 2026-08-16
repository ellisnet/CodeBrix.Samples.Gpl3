// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// What a Unicode space character's advance falls back to when no face in the chain
/// has a glyph for it.
/// <para>
/// New-in-family, and it exists because of trap 15: upstream hands text to Pango, Pango
/// hands it to HarfBuzz, and the port owes the LIBRARY'S BEHAVIOUR rather than
/// equivalent-looking arithmetic. HarfBuzz does not draw <c>.notdef</c> for a space
/// character it cannot map. It substitutes the ordinary SPACE glyph and then REWRITES
/// the advance from the character's space type, so a hair space is a sixteenth of an em
/// even in a font that has never heard of one.
/// </para>
/// <para>
/// None of the vendored text faces covers any of these except <c>U+2002</c>, so before
/// this existed every one of them measured as <c>.notdef</c> — which in C059 is 278
/// units, the same as an ordinary space. That is what set the two signs of a
/// <c>\segnoMark</c> too far apart: <c>format-sign-with-number</c> puts TWO hair spaces
/// between them, and each was 0.4780 output units too wide.
/// </para>
/// </summary>
public enum SpaceFallbackKind
{
    /// <summary>Not a space character, or one that needs no fallback.</summary>
    None = 0,

    /// <summary>A whole em: <c>U+2003</c>, <c>U+3000</c>.</summary>
    Em,

    /// <summary>Half an em: <c>U+2002</c>.</summary>
    EmHalf,

    /// <summary>A third of an em: <c>U+2004</c>.</summary>
    EmThird,

    /// <summary>A quarter of an em: <c>U+2005</c>.</summary>
    EmQuarter,

    /// <summary>A fifth of an em: <c>U+2009</c>.</summary>
    EmFifth,

    /// <summary>A sixth of an em: <c>U+2006</c>.</summary>
    EmSixth,

    /// <summary>A sixteenth of an em: <c>U+200A</c>, the hair space.</summary>
    EmSixteenth,

    /// <summary>Four eighteenths of an em: <c>U+205F</c>.</summary>
    FourEighteenthsEm,

    /// <summary>As wide as a digit: <c>U+2007</c>.</summary>
    Figure,

    /// <summary>As wide as a period: <c>U+2008</c>.</summary>
    Punctuation,

    /// <summary>Half an ordinary space: <c>U+202F</c>.</summary>
    Narrow,
}

/// <summary>
/// HarfBuzz's space fallback: which characters get a synthesised advance, and what
/// that advance is.
/// <para>
/// ⚠ THE TABLE BELOW WAS READ OFF THE PINNED ORACLE, NOT OFF HARFBUZZ'S SOURCE (rule
/// 35a — a library rule is an oracle, and the way to get it right is to measure it).
/// Every entry was measured by setting the character between two music glyphs and
/// reading the two glyph origins out of the SVG, under the corpus's own font pinning
/// (trap 8b). At the probe's size one em was exactly 64 of Pango's device dots, and the
/// oracle answered: <c>U+2003</c>/<c>U+3000</c> 64, <c>U+2004</c> 21, <c>U+2005</c> 16,
/// <c>U+2006</c> 11, <c>U+2009</c> 13, <c>U+200A</c> 4, <c>U+205F</c> 14,
/// <c>U+2007</c> 36 (a digit), <c>U+2008</c> 18 (a period), <c>U+202F</c> 9 (half a
/// space). Each is <c>em/n</c> quantised to a whole dot by the rounding
/// <see cref="TextFontMetric"/> already applies.
/// </para>
/// </summary>
public static class UnicodeSpaceFallback
{
    /// <summary>
    /// Applies the canonical decompositions HarfBuzz's normalizer applies before it
    /// looks a character up.
    /// <para>
    /// Only two matter here, and they are why <c>U+2000</c> does NOT come out as half an
    /// em: <c>U+2000</c> EN QUAD and <c>U+2001</c> EM QUAD are canonically equivalent to
    /// <c>U+2002</c> EN SPACE and <c>U+2003</c> EM SPACE, so the lookup that follows sees
    /// the decomposed character. C059 HAS a glyph for <c>U+2002</c> — 556 units, wider
    /// than half an em — and the oracle draws it for <c>U+2000</c>, which is 36 device
    /// dots where the fallback rule alone would have said 32.
    /// </para>
    /// <para>
    /// This is deliberately NOT general Unicode normalization. HarfBuzz normalizes
    /// everything; the port normalizes the two characters it has MEASURED, because the
    /// rest of the corpus agrees without it and a wider change would be reasoned rather
    /// than evidenced. Recorded in PORT-COVERAGE.
    /// </para>
    /// </summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The code point to look up.</returns>
    public static int Canonicalize(int codePoint)
    {
        switch (codePoint)
        {
            case 0x2000: return 0x2002;
            case 0x2001: return 0x2003;
            default: return codePoint;
        }
    }

    /// <summary>
    /// Returns how a character's advance should be synthesised when no face covers it.
    /// </summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>
    /// The fallback kind, or <see cref="SpaceFallbackKind.None"/> when the character is
    /// not a space HarfBuzz synthesises for.
    /// </returns>
    public static SpaceFallbackKind KindOf(int codePoint)
    {
        switch (codePoint)
        {
            case 0x2002: return SpaceFallbackKind.EmHalf;
            case 0x2003: return SpaceFallbackKind.Em;
            case 0x2004: return SpaceFallbackKind.EmThird;
            case 0x2005: return SpaceFallbackKind.EmQuarter;
            case 0x2006: return SpaceFallbackKind.EmSixth;
            case 0x2007: return SpaceFallbackKind.Figure;
            case 0x2008: return SpaceFallbackKind.Punctuation;
            case 0x2009: return SpaceFallbackKind.EmFifth;
            case 0x200A: return SpaceFallbackKind.EmSixteenth;
            case 0x202F: return SpaceFallbackKind.Narrow;
            case 0x205F: return SpaceFallbackKind.FourEighteenthsEm;
            case 0x3000: return SpaceFallbackKind.Em;
            default: return SpaceFallbackKind.None;
        }
    }

    /// <summary>
    /// Returns the synthesised advance for a space, in the same units as
    /// <paramref name="em"/>.
    /// </summary>
    /// <param name="kind">The fallback kind.</param>
    /// <param name="em">One em.</param>
    /// <param name="spaceAdvance">The ordinary SPACE glyph's advance.</param>
    /// <param name="digitAdvance">A digit's advance, for <c>U+2007</c>.</param>
    /// <param name="periodAdvance">A period's advance, for <c>U+2008</c>.</param>
    /// <returns>The advance.</returns>
    public static double Advance(
        SpaceFallbackKind kind,
        double em,
        double spaceAdvance,
        double digitAdvance,
        double periodAdvance)
    {
        switch (kind)
        {
            case SpaceFallbackKind.Em: return em;
            case SpaceFallbackKind.EmHalf: return em / 2.0;
            case SpaceFallbackKind.EmThird: return em / 3.0;
            case SpaceFallbackKind.EmQuarter: return em / 4.0;
            case SpaceFallbackKind.EmFifth: return em / 5.0;
            case SpaceFallbackKind.EmSixth: return em / 6.0;
            case SpaceFallbackKind.EmSixteenth: return em / 16.0;
            case SpaceFallbackKind.FourEighteenthsEm: return em * 4.0 / 18.0;
            case SpaceFallbackKind.Figure: return digitAdvance;
            case SpaceFallbackKind.Punctuation: return periodAdvance;
            case SpaceFallbackKind.Narrow: return spaceAdvance / 2.0;
            default: return spaceAdvance;
        }
    }
}
