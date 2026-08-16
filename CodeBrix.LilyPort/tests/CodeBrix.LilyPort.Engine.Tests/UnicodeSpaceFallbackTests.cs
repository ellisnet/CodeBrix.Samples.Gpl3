// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The Unicode space fences: a space character no vendored face covers gets HarfBuzz's
/// synthesised advance, not <c>.notdef</c>'s.
/// <para>
/// This is trap 15 — upstream hands text to Pango, Pango hands it to HarfBuzz, and the
/// port owes the LIBRARY'S behaviour. HarfBuzz substitutes the ordinary space glyph for
/// a space it cannot map and then rewrites the advance from the character's space type.
/// None of the vendored faces covers <c>U+200A</c>, so before the fallback existed a
/// hair space measured as C059's <c>.notdef</c> — 278 units, byte-for-byte as wide as an
/// ordinary space — and <c>format-sign-with-number</c>, which puts TWO hair spaces
/// between the two signs of a <c>\segnoMark</c>, set them 2 × 0.4780 too far apart.
/// </para>
/// <para>
/// The fixture is <c>KerningTableTests</c>': size 4 at output-scale 1, where one device
/// dot is exactly 15 design units and 0.06 output units, and one em is 4.0 output units
/// = 66.667 dots. Every expected value below is HAND-COMPUTED from that and from the
/// vendored C059's own advances (space 278, period 278, digit 556 per 1000-unit em) —
/// never recorded from the port's own output — and each fact is paired with a control
/// that must come out differently.
/// </para>
/// <para>
/// ⚠ Every space is written as an ESCAPE, never as a literal character. A file full of
/// invisible code points is a fence nobody can review, and one that silently becomes an
/// ordinary space under an editor or a merge still passes.
/// </para>
/// </summary>
public class UnicodeSpaceFallbackTests
{
    private const double Dot = 0.06;

    private const string Space = "\u0020";
    private const string NoBreak = "\u00a0";
    private const string EnQuad = "\u2000";
    private const string EnSpace = "\u2002";
    private const string EmSpace = "\u2003";
    private const string ThreePerEm = "\u2004";
    private const string FourPerEm = "\u2005";
    private const string SixPerEm = "\u2006";
    private const string Figure = "\u2007";
    private const string Punctuation = "\u2008";
    private const string Thin = "\u2009";
    private const string Hair = "\u200a";
    private const string NarrowNoBreak = "\u202f";
    private const string MediumMath = "\u205f";

    private static double Width(string text) =>
        new TextFontMetric("serif", false, false, false, 4.0, 1.0).TextStencil(text).XExtent.Right;

    [Fact]
    public void a_hair_space_is_a_sixteenth_of_an_em()
    {
        //Arrange
        // em/16 = 4.0/16 = 0.25 output units = 4.1667 dots, which the shaping pass
        // rounds onto the dot grid as 4 dots.

        //Act
        double hair = Width(Hair);
        double space = Width(Space);

        //Assert
        hair.Should().BeApproximately(4 * Dot, 1e-12);

        // THE CONTROL, and it is the whole point of the fence: an ordinary space is a
        // real glyph in C059 at 278/1000 em = 1.112 output units = 18.533 dots -> 19.
        // The defect was that the hair space measured the SAME as this, because
        // .notdef is 278 units wide too — so a fallback that answered the space
        // advance would satisfy nothing here.
        space.Should().BeApproximately(19 * Dot, 1e-12);
        hair.Should().NotBeApproximately(space, 1e-9);
    }

    [Fact]
    public void two_hair_spaces_cost_two_sixteenths_where_two_spaces_cost_two_spaces()
    {
        //Arrange
        // The shape format-sign-with-number builds for a jump mark numbered above 2:
        // sign, hair space, number, hair space, sign. Digits stand in for the signs so
        // the fact under test is the SPACING and not the music font.
        double bare = Width("88");

        //Act
        double hairs = Width("8" + Hair + "8");
        double spaces = Width("8" + Space + "8");

        //Assert
        // The RELATIONSHIP rather than the literal: inserting a hair space costs one
        // hair space, and it is 15 dots narrower than inserting an ordinary space.
        (hairs - bare).Should().BeApproximately(4 * Dot, 1e-12);
        (spaces - bare).Should().BeApproximately(19 * Dot, 1e-12);
        (spaces - hairs).Should().BeApproximately(15 * Dot, 1e-12);
    }

    [Fact]
    public void an_en_quad_draws_the_en_space_glyph_rather_than_half_an_em()
    {
        //Arrange
        // U+2000 EN QUAD is canonically equivalent to U+2002 EN SPACE, and HarfBuzz
        // normalizes before it maps. C059 HAS a glyph for U+2002 — 556 units, which is
        // WIDER than the half em the fallback rule alone would have given.

        //Act
        double enQuad = Width(EnQuad);
        double enSpace = Width(EnSpace);

        //Assert
        // 556/1000 * 4.0 = 2.224 output units = 37.067 dots -> 37.
        enSpace.Should().BeApproximately(37 * Dot, 1e-12);
        enQuad.Should().BeApproximately(enSpace, 1e-12);

        // THE CONTROL: half an em is 2.0 output units = 33.333 dots -> 33, four dots
        // narrower. Without the canonical mapping the en quad would land there, so
        // this is what makes Canonicalize load-bearing rather than cosmetic.
        enQuad.Should().NotBeApproximately(33 * Dot, 1e-9);
    }

    [Fact]
    public void the_em_fraction_spaces_divide_the_em_and_not_the_space()
    {
        //Arrange
        // em = 4.0 output units = 66.667 dots. Hand computed per character:
        //   U+2003 em   -> 66.667 -> 67     U+2004 em/3  -> 22.222 -> 22
        //   U+2005 em/4 -> 16.667 -> 17     U+2006 em/6  -> 11.111 -> 11
        //   U+2009 em/5 -> 13.333 -> 13     U+205F 4/18e -> 14.815 -> 15

        //Act & Assert
        Width(EmSpace).Should().BeApproximately(67 * Dot, 1e-12);
        Width(ThreePerEm).Should().BeApproximately(22 * Dot, 1e-12);
        Width(FourPerEm).Should().BeApproximately(17 * Dot, 1e-12);
        Width(SixPerEm).Should().BeApproximately(11 * Dot, 1e-12);
        Width(Thin).Should().BeApproximately(13 * Dot, 1e-12);
        Width(MediumMath).Should().BeApproximately(15 * Dot, 1e-12);

        // THE CONTROL: they must be ORDERED as their names say, so a table that
        // returned one constant for all of them — which is exactly the defect — cannot
        // pass. Every step is monotone from a whole em down to a sixteenth.
        Width(EmSpace).Should().BeGreaterThan(Width(ThreePerEm));
        Width(ThreePerEm).Should().BeGreaterThan(Width(FourPerEm));
        Width(FourPerEm).Should().BeGreaterThan(Width(Thin));
        Width(Thin).Should().BeGreaterThan(Width(SixPerEm));
        Width(SixPerEm).Should().BeGreaterThan(Width(Hair));
    }

    [Fact]
    public void the_measured_spaces_take_their_width_from_the_glyph_they_name()
    {
        //Arrange
        // Three of HarfBuzz's fallbacks measure a real glyph instead of dividing the
        // em: FIGURE SPACE is as wide as a digit, PUNCTUATION SPACE as wide as a
        // period, and NARROW NO-BREAK SPACE is half an ordinary space.

        //Act
        double figure = Width(Figure);
        double punctuation = Width(Punctuation);
        double narrow = Width(NarrowNoBreak);

        //Assert
        // digit 556 -> 2.224 = 37.067 dots -> 37; period 278 -> 18.533 -> 19;
        // half a space is 139 units -> 0.556 = 9.267 dots -> 9.
        figure.Should().BeApproximately(37 * Dot, 1e-12);
        punctuation.Should().BeApproximately(19 * Dot, 1e-12);
        narrow.Should().BeApproximately(9 * Dot, 1e-12);

        // THE CONTROLS: the figure space must equal a DIGIT. In C059 the period and
        // the space are both 278 units, so the punctuation assertion alone proves
        // nothing — pairing it with the digit, which is 556, is what discriminates.
        figure.Should().BeApproximately(Width("0"), 1e-12);
        punctuation.Should().BeApproximately(Width("."), 1e-12);
        figure.Should().NotBeApproximately(Width(Space), 1e-9);
        narrow.Should().BeLessThan(Width(Space));
    }

    [Fact]
    public void the_ordinary_spaces_are_left_to_the_font()
    {
        //Arrange
        // U+0020, U+00A0 and U+2002 are real glyphs in C059, so the fallback must not
        // touch any of them. This is the fence against the fix reaching wider than it
        // was measured to reach.

        //Act & Assert
        Width(Space).Should().BeApproximately(19 * Dot, 1e-12);
        Width(NoBreak).Should().BeApproximately(19 * Dot, 1e-12);

        // THE CONTROL: a character the fallback DOES own, so "nothing changed" cannot
        // pass this test by the fallback never running at all.
        Width(Hair).Should().BeApproximately(4 * Dot, 1e-12);
    }

    [Fact]
    public void canonicalize_maps_only_the_two_quad_characters()
    {
        //Act & Assert
        UnicodeSpaceFallback.Canonicalize(0x2000).Should().Be(0x2002);
        UnicodeSpaceFallback.Canonicalize(0x2001).Should().Be(0x2003);

        // The controls: every other space is looked up as itself, so a normalizer that
        // reached further would fail here.
        UnicodeSpaceFallback.Canonicalize(0x200a).Should().Be(0x200a);
        UnicodeSpaceFallback.Canonicalize(0x2002).Should().Be(0x2002);
        UnicodeSpaceFallback.Canonicalize(0x0020).Should().Be(0x0020);
    }

    [Fact]
    public void a_character_that_is_not_a_space_has_no_fallback()
    {
        //Act & Assert
        UnicodeSpaceFallback.KindOf('A').Should().Be(SpaceFallbackKind.None);

        // U+200B ZERO WIDTH SPACE is named a space and is NOT one of HarfBuzz's:
        // it is a format character, not Zs, so it gets no synthesised width.
        UnicodeSpaceFallback.KindOf(0x200b).Should().Be(SpaceFallbackKind.None);

        // The control: the neighbouring code point IS one, so "None for everything"
        // cannot pass.
        UnicodeSpaceFallback.KindOf(0x200a).Should().Be(SpaceFallbackKind.EmSixteenth);
    }
}
