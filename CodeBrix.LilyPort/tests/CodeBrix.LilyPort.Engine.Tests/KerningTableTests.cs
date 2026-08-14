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
/// The kerning fences: a text run's width is the SHAPED advance sum.
/// <para>
/// Upstream measures text by asking Pango for a shaped run's logical rectangle, which
/// includes the font's GPOS kerning; summing raw <c>hmtx</c> advances is never larger
/// by accident, it is larger by exactly the kerning (trap 6f). The §3.9 session
/// (2026-08-13) closed that gap.
/// </para>
/// <para>
/// Every expected value here is HAND-COMPUTED from the font's own tables, read
/// independently of the port with fontTools 4.57.0 on 2026-08-13 (the session's
/// kern-probe script): C059-Roman.otf has units-per-em 1000, advances
/// i=315 A=722 V=722 L=667 o=500, and its GPOS <c>kern</c> feature (one type-2
/// lookup, PairPos Format 1, XAdvance-on-first-glyph only) carries
/// kern(A,V)=&#8722;96, kern(V,A)=&#8722;96, kern(L,i)=+4, kern(o,space)=0. The
/// oracle-side width in the last fence was measured FIRST, off the pinned oracle
/// (rule 18b): STATUS_lilyport_refpin_2026-08-13.txt §4 records the engraving
/// tagline "LilyPond v2.27.2" at 7.7299 em against a raw advance sum of 7.7810.
/// </para>
/// </summary>
public class KerningTableTests
{
    [Fact]
    public void a_kern_pair_matches_the_value_read_from_the_font_gpos_table()
    {
        //Arrange
        TextFace face = TextFontChain.Face("C059-Roman.otf");
        int a = face.GlyphIndex('A');
        int v = face.GlyphIndex('V');
        int o = face.GlyphIndex('o');
        int space = face.GlyphIndex(' ');

        //Act
        double kernAv = face.Kerning(a, v);
        double kernOSpace = face.Kerning(o, space);

        //Assert
        // GPOS says -96 design units; the control pair carries no record and must
        // read 0, so a reader that answered a constant could not pass both.
        kernAv.Should().BeApproximately(-96.0, 1e-12);
        kernOSpace.Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void a_positive_kern_pair_keeps_its_sign()
    {
        //Arrange
        TextFace face = TextFontChain.Face("C059-Roman.otf");
        int l = face.GlyphIndex('L');
        int i = face.GlyphIndex('i');
        int a = face.GlyphIndex('A');
        int v = face.GlyphIndex('V');

        //Act
        double kernLi = face.Kerning(l, i);
        double kernAv = face.Kerning(a, v);

        //Assert
        // Kerning is not always negative: L,i WIDENS by 4 design units in this face.
        // The control is the opposite-signed pair from the same lookup, so a reader
        // that dropped or flipped signs fails one of the two.
        kernLi.Should().BeApproximately(4.0, 1e-12);
        kernAv.Should().BeLessThan(0.0);
    }

    [Fact]
    public void a_monospace_face_carries_no_kerning()
    {
        //Arrange
        // NimbusMonoPS has no GPOS table at all (measured 2026-08-13 across all 24
        // vendored faces); its pairs must read 0 while the serif face's do not.
        TextFace mono = TextFontChain.Face("NimbusMonoPS-Regular.otf");
        TextFace serif = TextFontChain.Face("C059-Roman.otf");

        //Act
        double monoKern = mono.Kerning(mono.GlyphIndex('A'), mono.GlyphIndex('V'));
        double serifKern = serif.Kerning(serif.GlyphIndex('A'), serif.GlyphIndex('V'));

        //Assert
        monoKern.Should().BeApproximately(0.0, 1e-12);
        serifKern.Should().NotBeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void a_kerned_string_is_narrower_by_exactly_the_pair_values()
    {
        //Arrange
        // Size 1 at output-scale 1 makes the design-units-to-output factor exactly
        // 1/1000, so every expected width is the hand summed table value over 1000.
        TextFontMetric metric = new TextFontMetric("serif", false, false, false, 1.0, 1.0);

        //Act
        Stencil kerned = metric.TextStencil("AVAVAV");
        Stencil single = metric.TextStencil("A");

        //Assert
        // 3*722 + 3*722 + 5*(-96) = 3852. The single-glyph width pins the advance
        // itself, so the string fence fails through the KERN term, not the advances.
        single.XExtent.Right.Should().BeApproximately(0.722, 1e-12);
        kerned.XExtent.Right.Should().BeApproximately(3.852, 1e-12);
    }

    [Fact]
    public void a_string_without_kern_pairs_equals_its_raw_advance_sum()
    {
        //Arrange
        TextFontMetric metric = new TextFontMetric("serif", false, false, false, 1.0, 1.0);

        //Act
        Stencil run = metric.TextStencil("iiiiii");
        Stencil single = metric.TextStencil("i");

        //Assert
        // The CONTROL: i,i carries no kern record, so shaping must change NOTHING —
        // 6*315 = 1890 exactly. A kerning pass that adjusted every pair would fail
        // here while the kerned fence above still passed.
        single.XExtent.Right.Should().BeApproximately(0.315, 1e-12);
        run.XExtent.Right.Should().BeApproximately(1.890, 1e-12);
    }

    [Fact]
    public void the_engraving_tagline_moves_to_the_oracle_side_of_its_raw_sum()
    {
        //Arrange
        // Rule 18b: the oracle was read FIRST. REF-PIN measured the tagline at
        // 7.7299 em against the port's then-raw 7.7810 (STATUS §4); the hand-shaped
        // sum is 7781 + (Li +4) + (ly -28) + (Po -29) = 7728 design units.
        TextFontMetric metric = new TextFontMetric("serif", false, false, false, 1.0, 1.0);
        const double OracleEm = 7.7299;
        const double RawSumEm = 7.7810;

        //Act
        Stencil tagline = metric.TextStencil("LilyPond v2.27.2");
        double width = tagline.XExtent.Right;

        //Assert
        // The width is the hand-computed shaped sum exactly, and it sits inside the
        // oracle's size-quantum band (the no-kern control string differs from the
        // oracle by 0.0015 em, so ±0.0025 is the honest tolerance) where the raw sum
        // missed by 0.0511 — the relationship that makes this a kerning fence rather
        // than a recorded literal.
        width.Should().BeApproximately(7.728, 1e-12);
        System.Math.Abs(width - OracleEm).Should().BeLessThan(0.0025);
        System.Math.Abs(RawSumEm - OracleEm).Should().BeGreaterThan(0.05);
    }
}
