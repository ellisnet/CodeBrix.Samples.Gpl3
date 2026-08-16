// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Globalization;
using CodeBrix.LilyScheme.Unicode;

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// Upstream's "no glyph for character" warning —
/// <c>Pango_font::get_glyph_desc</c>, <c>lily/pango-font.cc:253</c>.
/// <para>
/// New-in-family. The port draws D23's tofu where upstream cannot draw a
/// character either, and this is the sentence that goes with the picture.
/// </para>
/// <para>
/// THE NAME COMES FROM CodeBrix.LilyScheme, which is where Guile puts it:
/// <c>(ice-9 unicode)</c>'s <c>char-&gt;formal-name</c>, over a shipped table
/// because Guile implements it in C over GNU libunistring. Upstream reaches the
/// same procedure through <c>lily-imports.cc</c>'s Scheme import; the port calls
/// the same library's managed accessor instead, which is the identical table by
/// a shorter road and does not require an interpreter to be up at the moment a
/// text metric is taken.
/// </para>
/// </summary>
public static class MissingGlyphWarning
{
    // Unicode's Default_Ignorable_Code_Point, merged to seventeen ranges out of
    // DerivedCoreProperties.txt. Small enough to spell out, and spelled out rather
    // than generated because it moves about once a decade — unlike the name table,
    // which is one entry per assigned character and lives in CodeBrix.LilyScheme.
    private static readonly int[] ZeroWidthRanges =
    {
        0x00AD, 0x00AD, 0x034F, 0x034F, 0x061C, 0x061C, 0x115F, 0x1160,
        0x17B4, 0x17B5, 0x180B, 0x180F, 0x200B, 0x200F, 0x202A, 0x202E,
        0x2060, 0x206F, 0x3164, 0x3164, 0xFE00, 0xFE0F, 0xFEFF, 0xFEFF,
        0xFFA0, 0xFFA0, 0xFFF0, 0xFFF8, 0x1BCA0, 0x1BCA3, 0x1D173, 0x1D17A,
        0xE0000, 0xE0FFF,
    };

    /// <summary>
    /// Determines whether a code point is one upstream never warns about because it
    /// draws nothing by definition — upstream's own early return, with its own comment:
    /// "Zero-width input characters are valid Unicode but don't have associated glyphs."
    /// <para>
    /// Pango answers <c>PANGO_GLYPH_EMPTY</c> for these and
    /// <c>Pango_font::get_glyph_desc</c> returns before the warning. The port has no
    /// Pango, so the condition is spelled as what Pango's is: Unicode's
    /// <c>Default_Ignorable_Code_Point</c>.
    /// </para>
    /// <para>
    /// MEASURED against the corpus in BOTH directions, which is what makes it a rule
    /// rather than a patch for seven characters: all seven the port over-warned about
    /// (the bidi embeddings, overrides and marks) are default-ignorable, and NONE of
    /// the 79 code points the oracle DOES warn about is.
    /// </para>
    /// </summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns><see langword="true"/> when the character is default-ignorable.</returns>
    public static bool IsZeroWidth(int codePoint)
    {
        for (int i = 0; i < ZeroWidthRanges.Length; i += 2)
        {
            if (codePoint >= ZeroWidthRanges[i] && codePoint <= ZeroWidthRanges[i + 1])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a code point's formal Unicode name, or <see langword="null"/> when it
    /// has none — <c>char-&gt;formal-name</c>'s <c>#f</c>.
    /// </summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public static string FormalName(int codePoint) => UnicodeCharacterNames.Of(codePoint);

    /// <summary>
    /// Warns exactly as <c>lily/pango-font.cc:253</c> does:
    /// <c>no glyph for character '%s' (U+%04X%s) in font `%s'</c>, where the third
    /// field is a SPACE followed by the formal name, or nothing at all when the
    /// character has none.
    /// </summary>
    /// <param name="codePoint">The code point no face covered.</param>
    /// <param name="fontFileName">
    /// The file the glyph was looked for in. Upstream names the font's full path and
    /// the diagnostics comparator reduces it to its base name; the port has no path,
    /// because its fonts ship inside the assembly.
    /// </param>
    public static void Warn(int codePoint, string fontFileName)
    {
        if (IsZeroWidth(codePoint))
        {
            return;
        }

        string name = FormalName(codePoint);
        Flower.Warn.Warning(string.Format(
            CultureInfo.InvariantCulture,
            "no glyph for character '{0}' (U+{1:X4}{2}) in font `{3}'",
            char.ConvertFromUtf32(codePoint),
            codePoint,
            name == null ? string.Empty : " " + name,
            fontFileName));
    }
}
