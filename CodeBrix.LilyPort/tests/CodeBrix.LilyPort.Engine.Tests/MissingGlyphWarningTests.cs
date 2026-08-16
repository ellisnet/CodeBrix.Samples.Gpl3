// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Fonts;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The missing-glyph warning: which characters it says nothing about, and that the
/// formal name reaches it.
/// <para>
/// The NAME TABLE itself is CodeBrix.LilyScheme's — <c>(ice-9 unicode)</c> is where
/// Guile puts <c>char-&gt;formal-name</c> — and is fenced there, against Guile's
/// documented behaviour. What belongs here is everything that is Pango's rather than
/// Guile's, plus one case proving the two libraries are actually joined up.
/// </para>
/// <para>
/// Every expectation is read off the PINNED ORACLE'S OWN OUTPUT — the "no glyph for
/// character" lines in the reference diagnostics logs — and not off a plausible rule
/// (rule 35a).
/// </para>
/// </summary>
public class MissingGlyphWarningTests
{
    [Fact]
    public void the_formal_name_reaches_the_warning_from_the_interpreter_library()
    {
        //Arrange
        // The INTEGRATION case. The table moved to CodeBrix.LilyScheme, so what this
        // repository has to prove is that the name still arrives — a warning that
        // silently lost its third field would still be a well-formed sentence.
        // reference/diagnostics/markup-bidi-explicit-embedding.log, ten times:
        //   no glyph for character 'ה' (U+05D4 HEBREW LETTER HE) in font `...'
        const int hebrewLetterHe = 0x05D4;

        //Act
        string name = MissingGlyphWarning.FormalName(hebrewLetterHe);

        //Assert
        name.Should().Be("HEBREW LETTER HE");
    }

    [Fact]
    public void a_character_the_oracle_leaves_unnamed_has_no_name_here_either()
    {
        //Arrange
        // reference/diagnostics/pdf-copy-paste.log:
        //   no glyph for character '見' (U+898B) in font `...'
        // -- no name field at all. Paired with a character from the SAME file that the
        // oracle DOES name, so "answers nothing to everything" cannot pass.
        const int cjkUnifiedIdeograph = 0x898B;
        const int kangxiRadicalSee = 0x2F92;

        //Act
        string ideograph = MissingGlyphWarning.FormalName(cjkUnifiedIdeograph);
        string radical = MissingGlyphWarning.FormalName(kangxiRadicalSee);

        //Assert
        ideograph.Should().BeNull();
        radical.Should().Be("KANGXI RADICAL SEE");
    }

    [Fact]
    public void a_zero_width_formatting_character_is_never_warned_about()
    {
        //Arrange
        // Upstream returns from get_glyph_desc BEFORE the warning for anything Pango
        // answers PANGO_GLYPH_EMPTY to — "valid Unicode but don't have associated
        // glyphs", in its own words. markup-bidi-explicit-embedding SETS these and the
        // oracle says nothing about them; the port said it four times.
        const int leftToRightEmbedding = 0x202A;
        const int popDirectionalFormatting = 0x202C;
        const int rightToLeftMark = 0x200F;

        //Act & Assert
        MissingGlyphWarning.IsZeroWidth(leftToRightEmbedding).Should().BeTrue();
        MissingGlyphWarning.IsZeroWidth(popDirectionalFormatting).Should().BeTrue();
        MissingGlyphWarning.IsZeroWidth(rightToLeftMark).Should().BeTrue();
    }

    [Fact]
    public void the_characters_the_oracle_does_warn_about_are_not_zero_width()
    {
        //Arrange
        // The control, and the half that makes the rule a rule: a predicate answering
        // TRUE to everything would suppress all 316 warnings and pass the case above.
        // Measured over the whole reference corpus — none of the 79 code points the
        // oracle warns about is default-ignorable. Three of them, from three scripts.
        const int hebrewLetterHe = 0x05D4;
        const int hiraganaLetterRo = 0x308D;
        const int latinSmallLetterI = 0x0069;

        //Act & Assert
        MissingGlyphWarning.IsZeroWidth(hebrewLetterHe).Should().BeFalse();
        MissingGlyphWarning.IsZeroWidth(hiraganaLetterRo).Should().BeFalse();
        MissingGlyphWarning.IsZeroWidth(latinSmallLetterI).Should().BeFalse();
    }

    [Fact]
    public void a_hair_space_is_not_treated_as_zero_width()
    {
        //Arrange
        // The boundary that matters, because the two rules are neighbours in the code
        // chart and D38's space fallback owns the one below: U+200A is a HAIR SPACE
        // with a real width, U+200B the ZERO WIDTH SPACE next to it.
        const int hairSpace = 0x200A;
        const int zeroWidthSpace = 0x200B;

        //Act & Assert
        MissingGlyphWarning.IsZeroWidth(hairSpace).Should().BeFalse();
        MissingGlyphWarning.IsZeroWidth(zeroWidthSpace).Should().BeTrue();
    }

    [Fact]
    public void the_music_font_carries_the_signs_a_text_run_must_not_warn_about()
    {
        //Arrange
        // The PREMISE of the text path's music-font suppression, fenced where it could
        // silently change: a font rebuild. Upstream's fontconfig chain for a text run
        // continues past the two text faces into Emmentaler (measured, fc-match -s
        // serif), so a MUSIC FLAT SIGN in a custom note name is a character Pango finds
        // and never warns about.
        SfntReader music = new SfntReader(FontAssets.MusicFont("emmentaler-20"));
        Dictionary<int, int> cmap = music.ReadCmap();

        const int musicFlatSign = 0x266D;
        const int musicSharpSign = 0x266F;

        // The control, and the half that keeps the suppression narrow: of the 79 code
        // points the oracle warns about corpus-wide, no Emmentaler face covers any of
        // the 78 that reach the TEXT path. Three of them, from three scripts.
        const int hebrewLetterHe = 0x05D4;
        const int hiraganaLetterRo = 0x308D;
        const int kangxiRadicalSee = 0x2F92;

        //Act & Assert
        cmap.ContainsKey(musicFlatSign).Should().BeTrue();
        cmap.ContainsKey(musicSharpSign).Should().BeTrue();
        cmap.ContainsKey(hebrewLetterHe).Should().BeFalse();
        cmap.ContainsKey(hiraganaLetterRo).Should().BeFalse();
        cmap.ContainsKey(kangxiRadicalSee).Should().BeFalse();
    }
}
