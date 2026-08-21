// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
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
    public void one_device_dot_is_pangos_resolution_over_the_output_scale()
    {
        //Arrange
        // The fixture every width fence below uses. Size 4 at output-scale 1 puts one
        // device dot at exactly 15 design units, which is both a round number to hand
        // compute with and close to the 15.5 the engine actually runs at (PARITY 5
        // measured the default text size at 15.519 units per dot).
        TextFontMetric fixture = new TextFontMetric("serif", false, false, false, 4.0, 1.0);
        TextFontMetric halfScale = new TextFontMetric("serif", false, false, false, 4.0, 2.0);

        //Act
        double dot = fixture.DevicePixel;
        double halved = halfScale.DevicePixel;

        //Assert
        // INCH_TO_BP / (PANGO_RESOLUTION * output_scale) = 72 / 1200 = 0.06 output
        // units, and 15 design units at this size. The CONTROL doubles output-scale,
        // which must HALVE the dot — a constant would pass the first and fail this.
        dot.Should().BeApproximately(0.06, 1e-12);
        (dot / (4.0 / 1000.0)).Should().BeApproximately(15.0, 1e-12);
        halved.Should().BeApproximately(0.03, 1e-12);
    }

    [Fact]
    public void a_kerned_string_is_narrower_by_the_pair_values_rounded_onto_the_dot_grid()
    {
        //Arrange
        TextFontMetric metric = new TextFontMetric("serif", false, false, false, 4.0, 1.0);

        //Act
        Stencil kerned = metric.TextStencil("AVAVAV");
        Stencil single = metric.TextStencil("A");

        //Assert
        // Pango rounds each SHAPED advance to a whole device dot, so a width is a
        // count of dots and the kern is inside the rounding, not outside it. Hand
        // computed at 15 units per dot: A alone is round(722/15) = 48 dots; each of
        // the five kerned glyphs is round((722-96)/15) = round(41.73) = 42, and the
        // last V has no pair to its right, so "AVAVAV" is 5*42 + 48 = 258 dots.
        // 0.06 output units per dot gives 2.88 and 15.48.
        single.XExtent.Right.Should().BeApproximately(48 * 0.06, 1e-12);
        kerned.XExtent.Right.Should().BeApproximately(258 * 0.06, 1e-12);

        // The RELATIONSHIP the literals exist to protect: kerning still narrows the
        // run, and by more than one dot, so a shaping pass that quietly stopped
        // kerning would reach 6*48 = 288 dots and fail here even if the grid arithmetic
        // above were changed to match it.
        kerned.XExtent.Right.Should().BeLessThan(6 * single.XExtent.Right - metric.DevicePixel);
    }

    [Fact]
    public void a_string_without_kern_pairs_sums_its_advances_dot_by_dot()
    {
        //Arrange
        TextFontMetric metric = new TextFontMetric("serif", false, false, false, 4.0, 1.0);

        //Act
        Stencil run = metric.TextStencil("iiiiii");
        Stencil single = metric.TextStencil("i");

        //Assert
        // The CONTROL: i,i carries no kern record, so shaping must change NOTHING, and
        // 315 design units is exactly 21 dots at this size — a glyph already ON the
        // grid, which the rounding must leave alone. 6 * 21 = 126 dots.
        single.XExtent.Right.Should().BeApproximately(21 * 0.06, 1e-12);
        run.XExtent.Right.Should().BeApproximately(126 * 0.06, 1e-12);

        // ...and because this glyph sits on the grid, its run is ALSO the raw advance
        // sum. That equality is what separates "the grid is being applied" from "the
        // grid is eating advances": a kerning pass that adjusted every pair, or a
        // rounding that biased in one direction, breaks it while the fence above holds.
        run.XExtent.Right.Should().BeApproximately(6 * 315 * (4.0 / 1000.0), 1e-12);
    }

    [Fact]
    public void the_engraving_tagline_moves_to_the_oracle_side_of_its_raw_sum()
    {
        //Arrange
        // Rule 18b: the oracle was read FIRST. REF-PIN measured the tagline at
        // 7.7299 em against the port's then-raw 7.7810 (STATUS §4); the hand-shaped
        // sum is 7781 + (Li +4) + (ly -28) + (Po -29) = 7728 design units, and that
        // sum then lands on the dot grid glyph by glyph.
        TextFontMetric metric = new TextFontMetric("serif", false, false, false, 4.0, 1.0);
        const double OracleEm = 7.7299;
        const double RawSumEm = 7.7810;

        //Act
        // The 7.7299 em below is an ORACLE MEASUREMENT of this exact string, so the
        // string is built from LilyVersion.CompatibleWithVersion rather than pinned to a
        // literal: advancing the port onto a newer LilyPond changes the tagline's glyphs
        // and therefore its width, and this assertion should FAIL loudly and be
        // re-measured against the new oracle instead of quietly measuring a string the
        // engine no longer emits.
        Stencil tagline = metric.TextStencil("LilyPond v" + LilyVersion.CompatibleWithVersion);
        double width = tagline.XExtent.Right;
        double widthEm = width / (4.0 / 1000.0) / 1000.0;

        //Assert
        // Hand computed at 15 units per dot over L i l y P o n d _ v 2 . 2 7 . 2, with
        // the three kern pairs folded into their first glyph's step before rounding:
        // 517 dots.
        width.Should().BeApproximately(517 * 0.06, 1e-12);

        // The relationship, which is what this fence is for: the shaped width sits on
        // the ORACLE's side of the raw sum, twice as close to it. It is deliberately
        // NOT a tight band any more — PARITY 5 measured the dot grid, and a width in
        // EM is therefore size dependent, so comparing this fixture's em figure with an
        // em figure the oracle produced at its own size is indicative, not exact.
        System.Math.Abs(widthEm - OracleEm)
            .Should().BeLessThan(System.Math.Abs(RawSumEm - OracleEm) / 2.0);
    }

    [Fact]
    public void a_run_asking_for_minus_kern_is_not_kerned()
    {
        //Arrange
        // font-features reaches GPOS as well as GSUB. HarfBuzz turns `kern' on for a
        // horizontal run without being asked, so the tag only ever appears in a run that
        // wants it OFF — which is what emmentaler-fractions and emmentaler-number-kerning
        // each devote their fourth page to.
        TextFontMetric metric = new TextFontMetric("serif", false, false, false, 4.0, 1.0);

        //Act
        double kerned = metric.TextStencil("AVAVAV", string.Empty).XExtent.Right;
        double unkerned = metric.TextStencil("AVAVAV", "-kern").XExtent.Right;

        //Assert
        // The CONTROL is the default run: a fence that only measured the -kern run would
        // pass with kerning switched off everywhere.
        unkerned.Should().BeGreaterThan(kerned);
    }

    [Fact]
    public void the_kern_tag_is_read_the_way_the_substitution_tags_are_read()
    {
        //Arrange
        // Same -tag/+tag spelling SubstitutionTable reads, and the LAST entry naming the
        // tag wins, because HarfBuzz appends a run's features in order.

        //Act, Assert
        KerningTable.Enabled(null).Should().BeTrue();
        KerningTable.Enabled(string.Empty).Should().BeTrue();
        KerningTable.Enabled("tnum,cv47").Should().BeTrue();
        KerningTable.Enabled("-kern").Should().BeFalse();
        KerningTable.Enabled("tnum,cv47,-kern").Should().BeFalse();
        KerningTable.Enabled("-kern,+kern").Should().BeTrue();
        KerningTable.Enabled("+kern,-kern").Should().BeFalse();

        // A tag that merely CONTAINS the letters is not the tag.
        KerningTable.Enabled("-kerning").Should().BeTrue();
    }
}
