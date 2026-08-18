// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// D31 AS AMENDED (Jeremy, 2026-08-17): in the MUSIC font, a code point the font cannot
/// map advances by TWO spaces and draws nothing.
/// <para>
/// The tofu rule stays where it was made — for TEXT fonts, where a missing Hebrew or CJK
/// glyph SHOULD be visible so a user says "you don't support this". Emmentaler is the
/// opposite case: upstream advances and draws nothing, so the port owes the advance and
/// nothing else. The port used to DROP the glyph, losing the advance and moving every
/// glyph after it — `\compound-meter #'((+inf.0))' came out a whole character narrow,
/// because Emmentaler has no `i'.
/// </para>
/// <para>
/// WHY TWO, and the number is measured rather than chosen: Pango's unknown-glyph box is a
/// per-font, per-size constant the port cannot compute (its width is
/// `approximate_char_width', which Pango derives from a sample string in a fallback font
/// the port does not have). Measured against the pinned oracle at seven sizes from
/// magstep 1/4 to 16, the box is between 2.00 and 2.27 space advances — so TWO is the
/// nearest whole number at every size, and exact at magstep 1/4.
/// </para>
/// </summary>
// Standing rule 8: this class builds an interpreter, so it serializes with every other
// class that touches process-global engine state.
[Collection(EngineGlobalStateCollection.Name)]
public class UnmappableCodePointTests
{
    private const string FontName = "emmentaler-20";
    private const int Unmappable = 'i';
    private const int Space = ' ';
    private const int Mappable = '0';
    private const int MusicEm = 1000;

    private static OpenTypeFontMetric LoadMusicFont()
    {
        byte[] bytes = FontAssets.MusicFont(FontName);
        bytes.Should().NotBeNull();

        Interpreter interpreter = new Interpreter();
        SchemeBootstrap.LoadCore(interpreter);
        return new OpenTypeFontMetric(new OpenTypeFont(bytes, FontName, interpreter), FontName);
    }

    /// <summary>
    /// A bare layout on purpose: its device dot is zero, so advances are not put on the
    /// output grid and the fence measures the relationship rather than the rounding.
    /// </summary>
    private static double Width(OpenTypeFontMetric font, string text)
        => TextInterface.MusicFontTextStencil(new OutputDef(), font, text, string.Empty)
            .Extent(Axis.X).Length;

    /// <summary>The run's Y extent, which is the union of its glyphs' INK boxes.</summary>
    private static Interval YExtent(OpenTypeFontMetric font, string text)
        => TextInterface.MusicFontTextStencil(new OutputDef(), font, text, string.Empty)
            .Extent(Axis.Y);

    [Fact]
    public void the_premise_emmentaler_cannot_map_i_and_can_map_space_and_zero()
    {
        //Arrange
        OpenTypeFontMetric font = LoadMusicFont();

        //Act & Assert -- read off the FONT, which is the authority here
        font.CharToGlyphIndex(Unmappable).Should().Be(FontMetric.GlyphIndexInvalid);
        font.CharToGlyphIndex(Space).Should().NotBe(FontMetric.GlyphIndexInvalid);
        font.CharToGlyphIndex(Mappable).Should().NotBe(FontMetric.GlyphIndexInvalid);
    }

    [Fact]
    public void an_unmappable_code_point_advances_by_exactly_two_spaces()
    {
        //Arrange
        OpenTypeFontMetric font = LoadMusicFont();

        //Act
        double unmappable = Width(font, "i");
        double twoSpaces = Width(font, "  ");

        //Assert -- the RELATIONSHIP, not a literal: whatever the space measures, the
        // stand-in is two of them
        unmappable.Should().Be(twoSpaces);
    }

    [Fact]
    public void the_control_one_space_and_three_spaces_are_both_wrong()
    {
        //Arrange -- so "two" is a measured count and not a coincidence of any count
        OpenTypeFontMetric font = LoadMusicFont();

        //Act
        double unmappable = Width(font, "i");

        //Assert
        unmappable.Should().NotBe(Width(font, " "));
        unmappable.Should().NotBe(Width(font, "   "));
    }

    [Fact]
    public void in_a_run_it_is_not_dropped_and_substitutes_for_two_spaces()
    {
        //Arrange -- the defect this closes: the glyph used to contribute NO advance, so
        // every glyph after it moved left by a whole character.
        // /!\ The two runs are compared against each other and NOT against "00": Emmentaler
        // KERNS its digits, so removing the middle character changes the kern pairs and the
        // difference stops being the stand-in's own width. The fence's first form asserted
        // that and was wrong by exactly one 0-0 kern (rule 35a -- a red fence is as likely
        // to be a bad expectation as a bad port).
        OpenTypeFontMetric font = LoadMusicFont();

        //Act
        double withUnmappable = Width(font, "0i0");
        double withTwoSpaces = Width(font, "0  0");
        double withoutIt = Width(font, "00");

        //Assert
        withUnmappable.Should().Be(withTwoSpaces);
        withUnmappable.Should().BeGreaterThan(withoutIt);
    }

    [Fact]
    public void the_control_a_mappable_code_point_is_untouched()
    {
        //Arrange -- the stand-in must not swallow characters the font DOES have
        OpenTypeFontMetric font = LoadMusicFont();

        //Act
        double zero = Width(font, "0");

        //Assert
        zero.Should().NotBe(Width(font, "  "));
        zero.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void the_stand_in_draws_nothing_because_emmentalers_space_has_no_outline()
    {
        //Arrange -- upstream draws nothing for an unknown glyph, so the stand-in must not
        OpenTypeFontMetric font = LoadMusicFont();
        int space = font.CharToGlyphIndex(Space);
        int zero = font.CharToGlyphIndex(Mappable);

        //Act -- trace both glyphs' outlines and count the segments each contributes
        LazySkylinePair spaceInk = new LazySkylinePair(Axis.X);
        GlyphOutlineSkyline.AddOutline(font.Font.Cff, spaceInk, Transform.Identity, space);

        LazySkylinePair zeroInk = new LazySkylinePair(Axis.X);
        GlyphOutlineSkyline.AddOutline(font.Font.Cff, zeroInk, Transform.Identity, zero);

        //Assert -- paired with a control that must come out differently
        spaceInk.IsEmpty.Should().BeTrue();
        zeroInk.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void the_typographic_metrics_come_from_os2_and_span_exactly_one_em()
    {
        //Arrange -- read off the FONT's own OS/2 table, which is the authority (rule 35a)
        OpenTypeFontMetric font = LoadMusicFont();

        //Act
        (int ascender, int descender) = font.TypoAscenderDescender;

        //Assert -- Emmentaler's typographic pair spans the baseline and totals one em
        ascender.Should().Be(800);
        descender.Should().Be(-200);
        (ascender - descender).Should().Be(MusicEm);
    }

    [Fact]
    public void an_unmappable_code_point_reserves_the_typographic_pair_as_its_height()
    {
        //Arrange -- two spaces give the ADVANCE; a space has no ink, so the HEIGHT has to
        // come from somewhere else, and it comes from the font's own OS/2 metrics.
        OpenTypeFontMetric font = LoadMusicFont();
        (int ascender, int descender) = font.TypoAscenderDescender;

        // Stencil units per DESIGN unit -- a metric answers raw * FontScaling / MusicEm,
        // so ONE EM in stencil units is FontScaling itself. The two are a factor of a
        // thousand apart and naming them the same thing is how this fence first went red.
        double perDesignUnit = font.FontScaling / MusicEm;

        //Act
        Interval reserved = YExtent(font, "i");

        //Assert -- hand-computed from the font, not recorded from the port
        reserved.Left.Should().BeApproximately(descender * perDesignUnit, 1e-9);
        reserved.Right.Should().BeApproximately(ascender * perDesignUnit, 1e-9);
    }

    [Fact]
    public void the_control_two_real_spaces_reserve_no_height_at_all()
    {
        //Arrange -- the same two glyphs, asked for by the document rather than substituted.
        // This is what makes the case above a STAND-IN rule and not a space rule.
        OpenTypeFontMetric font = LoadMusicFont();

        //Act
        Interval spaces = YExtent(font, "  ");
        Interval standIn = YExtent(font, "i");

        //Assert
        spaces.Length.Should().Be(0.0);
        standIn.Length.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void the_control_a_mappable_glyph_keeps_its_own_ink_height()
    {
        //Arrange -- the reservation must not overwrite a real glyph's measured ink
        OpenTypeFontMetric font = LoadMusicFont();
        double oneEm = font.FontScaling;

        //Act
        Interval zero = YExtent(font, "0");

        //Assert -- a digit is shorter than a whole em and does not descend below the
        // baseline, so it cannot be the reserved pair
        zero.Length.Should().BeLessThan(oneEm);
        zero.Left.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void the_control_the_height_is_not_the_fonts_glyph_RANGE()
    {
        //Arrange -- hhea's ascender/descender and usWinAscent/Descent are 2127/-2314 for
        // Emmentaler, four and a half em, because music glyphs reach far past the staff.
        // Taking those instead would be a plausible-looking mistake.
        OpenTypeFontMetric font = LoadMusicFont();
        double oneEm = font.FontScaling;

        //Act
        double reserved = YExtent(font, "i").Length;

        //Assert -- exactly one em, and nowhere near hhea's four and a half
        reserved.Should().BeApproximately(oneEm, 1e-9);
        reserved.Should().BeLessThan(2.0 * oneEm);
    }
}
