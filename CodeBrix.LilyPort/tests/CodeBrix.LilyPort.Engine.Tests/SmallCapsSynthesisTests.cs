// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Pango's SYNTHETIC small capitals — what <c>font-variant: small-caps</c> does to a
/// run's measurement when the face has no small-caps variant to switch to.
/// <para>
/// The synthesis is not an OpenType feature: C059 carries no <c>smcp</c> lookup at all
/// (which is why <c>font-features.ly</c>'s explicit <c>smcp</c> line measures exactly
/// like its plain neighbour on both engines), and yet the pinned oracle's
/// <c>\fontCaps</c> measures differently from plain text. Pango splits the item at each
/// run of lowercase, uppercases it, and sets that piece at a reduced size.
/// </para>
/// <para>
/// EXPECTED VALUES COME FROM AUTHORITIES, never from the port (rules 33/35a): the scale
/// is read out of the FACE'S OWN <c>OS/2</c> table, and the behavioural cases were
/// measured on the pinned oracle under the corpus's font pinning with
/// <c>~/ClaudeHome/lilyport-probe-parity23/smallcaps-rule.ly</c>, which runs unchanged
/// on both engines. Its readings, at the regression suite's default TextScript size:
/// <c>\fontCaps normal</c> 6.6579448818897635, <c>\fontCaps Normal</c>
/// 7.340811023622047, <c>\fontCaps NORMAL</c> and <c>\fontCaps 0123456789</c> exactly
/// their plain selves.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SmallCapsSynthesisTests
{
    // The suite's own text face, and the one every reading above was taken through.
    private const string Serif = "C059-Roman.otf";

    // A face from a DIFFERENT family, kept as the control for the scale being read
    // per-face rather than being a constant someone wrote down.
    private const string Sans = "NimbusSans-Regular.otf";

    private static TextFontMetric Metric(bool smallCaps, double size)
        => new TextFontMetric("serif", false, false, smallCaps, size, 1.0);

    [Fact]
    public void the_synthesis_scale_is_the_face_s_own_x_height_over_its_cap_height()
    {
        //Arrange
        // READ OFF THE FONT (rule 35a): C059-Roman's OS/2 says sxHeight 466 and
        // sCapHeight 722 in a 1000-unit em. Small capitals ARE capitals at the height of
        // lowercase, so that quotient is the scale.
        TextFace serif = TextFace.Load(Serif);
        TextFace sans = TextFace.Load(Sans);
        serif.Should().NotBeNull();
        sans.Should().NotBeNull();

        //Act
        (int xHeight, int capHeight) = serif.Reader.ReadXAndCapHeight();

        //Assert
        xHeight.Should().Be(466);
        capHeight.Should().Be(722);
        serif.SmallCapsScale.Should().BeApproximately(466.0 / 722.0, 1e-12);

        // THE CONTROL: another face answers its OWN ratio. A fence checking one face
        // passes with the scale hard-coded, which is the mistake it exists to catch.
        sans.SmallCapsScale.Should().NotBeApproximately(serif.SmallCapsScale, 1e-4);
    }

    [Fact]
    public void a_lowercase_run_measures_as_capitals_set_at_the_reduced_size()
    {
        //Arrange
        // The mechanism stated as a relationship (rule 33): the small-caps measurement
        // of "normal" is the ORDINARY measurement of "NORMAL" at the scaled size —
        // shaped there, not multiplied afterwards, because every advance is rounded to a
        // whole device dot at the size it is shaped at.
        const double Size = 12.0;
        double scale = TextFace.Load(Serif).SmallCapsScale;

        //Act
        double synthesised = Metric(true, Size).TextStencil("normal").XExtent.Right;
        double capitalsAtReducedSize
            = Metric(false, Size * scale).TextStencil("NORMAL").XExtent.Right;
        double plain = Metric(false, Size).TextStencil("normal").XExtent.Right;

        //Assert
        synthesised.Should().BeApproximately(capitalsAtReducedSize, 1e-9);

        // THE CONTROL, which must come out DIFFERENTLY: without the synthesis the run is
        // just lowercase at full size, and that is a different number.
        synthesised.Should().NotBeApproximately(plain, 1e-3);
    }

    [Fact]
    public void characters_that_are_not_lowercase_are_left_entirely_alone()
    {
        //Arrange
        // MEASURED ON THE ORACLE: `\fontCaps NORMAL' and `\fontCaps 0123456789' come out
        // byte-identical to their plain selves, so the synthesis is per-character and
        // does not rescale the whole run.
        const double Size = 12.0;

        //Act
        TextFontMetric small = Metric(true, Size);
        TextFontMetric plain = Metric(false, Size);

        //Assert
        small.TextStencil("NORMAL").XExtent.Right.Should()
            .BeApproximately(plain.TextStencil("NORMAL").XExtent.Right, 1e-12);
        small.TextStencil("0123456789").XExtent.Right.Should()
            .BeApproximately(plain.TextStencil("0123456789").XExtent.Right, 1e-12);

        // And the mixed case is genuinely mixed rather than all-or-nothing. The oracle
        // reads 1.5884640132874015 for `\fontCaps Normal' and 1.0460388964074803 for
        // `\fontCaps normal', while plain "NORMAL" reaches 1.6214403128075787 — three
        // different heights, and each one says something:
        //
        //   * the leading "N" keeps its FULL cap height, so the mixed run is as tall as
        //     a lone capital;
        //   * plain "Normal" is TALLER still, because its `l' is an ascender — and the
        //     synthesis is what removes that ascender, by setting an `L' instead;
        //   * an all-lowercase run has no full-height letter left at all.
        double mixedTop = small.TextStencil("Normal").YExtent.Right;
        double allLowerTop = small.TextStencil("normal").YExtent.Right;
        double loneCapitalTop = plain.TextStencil("N").YExtent.Right;
        double plainAscenderTop = plain.TextStencil("Normal").YExtent.Right;

        mixedTop.Should().BeApproximately(loneCapitalTop, 1e-9);
        mixedTop.Should().BeLessThan(plainAscenderTop);
        allLowerTop.Should().BeLessThan(mixedTop);
    }

    [Fact]
    public void a_metric_that_was_not_asked_for_small_caps_synthesises_nothing()
    {
        //Arrange
        // The whole mechanism is gated on the font description, and this is the fence
        // that says so — every other test here would still pass if the synthesis were
        // applied unconditionally to lowercase text.
        //
        // ⚠ The relationship is NOT "small capitals are narrower". Whether they are
        // depends entirely on the letters: "normal" shrinks (7.31 -> 6.66 on the oracle)
        // because its `l' is narrow and its `L' is not, while "hello" GROWS. The honest
        // statement is that the two measurements differ, and that the asked-for one is
        // the capitals at the reduced size.
        const double Size = 12.0;
        double scale = TextFace.Load(Serif).SmallCapsScale;

        //Act
        double asked = Metric(true, Size).TextStencil("hello").XExtent.Right;
        double notAsked = Metric(false, Size).TextStencil("hello").XExtent.Right;
        double capitalsAtReducedSize
            = Metric(false, Size * scale).TextStencil("HELLO").XExtent.Right;

        //Assert
        asked.Should().BeApproximately(capitalsAtReducedSize, 1e-9);
        notAsked.Should().NotBeApproximately(asked, 1e-3);
    }
}
