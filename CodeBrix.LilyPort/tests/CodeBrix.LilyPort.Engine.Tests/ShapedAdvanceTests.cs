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
/// D44 — the integer pipeline a shaped text advance travels, stage by stage.
/// <para>
/// Every expected value here is READ OFF AN AUTHORITY, never off the port (rule 33):
/// the two scale stages off Pango's own source (<c>lround (size * PANGO_SCALE)</c> in
/// <c>lily/font-select.cc:215</c>, then <c>pango_units_from_double</c> of the pixel size
/// in <c>pangofc-font.c</c>), and the multiplier stage off the LIBRARY, which rule 35a
/// says is an oracle: the values below were measured by driving the real libharfbuzz
/// 10.2.0 through <c>~/ClaudeHome/lilyport-probe-parity17/hb_advance.py</c> over the
/// oracle's own faces.
/// </para>
/// <para>
/// ⚠ EVERY CASE IS PAIRED WITH THE RULE IT REPLACES. The refuted reading —
/// <c>mult = (x_scale &lt;&lt; 16) / upem</c> in exact integer arithmetic — agrees with
/// HarfBuzz on all but a handful of samples, so a fence built on an ordinary advance
/// passes under both and fences nothing. The cases chosen are the ones where the two
/// readings DISAGREE, and the assertions say so explicitly.
/// </para>
/// </summary>
public class ShapedAdvanceTests
{
    // The refuted reading, kept here as the control: the 16.16 multiplier taken in exact
    // integer arithmetic instead of through float.
    private static long ExactDivisionMultiplier(int xScale, int unitsPerEm)
        => ((long)xScale << 16) / unitsPerEm;

    [Fact]
    public void the_multiplier_lands_above_the_exact_value_where_float_rounds_up()
    {
        //Arrange
        // MEASURED: hb 10.2.0 answers 16117 for a 500-unit glyph on a 1000-unit em at
        // scale 32233. The exact product is 16116.5 — a dead tie — so the answer is
        // decided entirely by which side of the exact value the multiplier sits on.
        const int XScale = 32233;
        const int UnitsPerEm = 1000;
        const long Units = 500;

        //Act
        long viaFloat = TextFontMetric.EmMult(
            Units, TextFontMetric.Multiplier(XScale, UnitsPerEm));
        long viaExactDivision = TextFontMetric.EmMult(
            Units, ExactDivisionMultiplier(XScale, UnitsPerEm));

        //Assert
        viaFloat.Should().Be(16117);
        viaExactDivision.Should().Be(16116, "the refuted reading must come out DIFFERENTLY here");
    }

    [Fact]
    public void the_tie_breaks_the_other_way_where_the_exact_rational_rule_rounds_up()
    {
        //Arrange
        // MEASURED: hb 10.2.0 answers 8533 at scale 17067, where the exact product is
        // 8533.5. The tie breaks the OTHER WAY from the case above — 32233 rounded a tie
        // UP and this one rounds it DOWN — which is what no closed form over the exact
        // value can reproduce, and it is the pair of cases together that refutes the
        // exact-rational reading rather than either one alone.
        const int XScale = 17067;
        const int UnitsPerEm = 1000;
        const long Units = 500;

        //Act
        long viaFloat = TextFontMetric.EmMult(
            Units, TextFontMetric.Multiplier(XScale, UnitsPerEm));
        long roundHalfAwayFromZero = (Units * XScale + (UnitsPerEm / 2)) / UnitsPerEm;

        //Assert
        viaFloat.Should().Be(8533);
        roundHalfAwayFromZero.Should().Be(
            8534, "the exact-rational reading must come out DIFFERENTLY here");
    }

    [Fact]
    public void an_ordinary_advance_is_where_every_candidate_rule_agrees()
    {
        //Arrange
        // THE CONTROL FOR BOTH CASES ABOVE. C059-Roman's `H' is 722 units; at the same
        // scale the product is nowhere near a tie, and all three readings agree. A fence
        // written on a glyph like this one would pass with the defect in place.
        const int XScale = 32233;
        const int UnitsPerEm = 1000;
        const long Units = 722;

        //Act
        long viaFloat = TextFontMetric.EmMult(
            Units, TextFontMetric.Multiplier(XScale, UnitsPerEm));
        long viaExactDivision = TextFontMetric.EmMult(
            Units, ExactDivisionMultiplier(XScale, UnitsPerEm));

        //Assert
        viaFloat.Should().Be(viaExactDivision);
    }

    [Fact]
    public void the_scale_is_a_whole_number_of_pango_units_and_not_the_exact_size()
    {
        //Arrange
        // pango_units_from_double is floor (x + 0.5), so the scale is an INTEGER and is
        // up to half a unit from the exact value. A size whose pango_size is not a
        // multiple of three cannot land exactly, because the device factor is 1200/72.
        // pango_size 1993 -> exact 1993 * 50 / 3 = 33216.666..., so the scale is 33217.
        TextFontMetric font = new TextFontMetric(
            "serif", false, false, false, 1993.0 / 1024.0, 1.0);

        //Act
        int pangoSize = font.PangoSize;
        int xScale = font.XScale;

        //Assert
        pangoSize.Should().Be(1993);
        xScale.Should().Be(33217);
    }

    [Fact]
    public void a_size_on_the_three_unit_lattice_needs_no_rounding_at_all()
    {
        //Arrange
        // THE CONTROL for the case above: pango_size 1992 IS divisible by three, so
        // 1992 * 50 / 3 = 33200 exactly and the rounding term changes nothing. The two
        // cases together are what show the rounding is real rather than incidental.
        TextFontMetric font = new TextFontMetric(
            "serif", false, false, false, 1992.0 / 1024.0, 1.0);

        //Act
        int xScale = font.XScale;

        //Assert
        xScale.Should().Be(33200);
    }

    [Fact]
    public void a_kern_scales_separately_from_the_advance_it_adjusts()
    {
        //Arrange
        // GPOS's apply_value calls em_scale_x on the pair value and ADDS it to an
        // x_advance em_scale_x already produced, so the run owes TWO em_mults. Scaling
        // the design-unit SUM once is a different number whenever the two roundings do
        // not happen to agree; MEASURED at this scale against a 722-unit advance, -97 is
        // the first pair value where they part company (20145 separately, 20146
        // together), and a smaller kern such as -35 agrees under both — which is why the
        // fence has to name a case rather than any case.
        const int XScale = 32233;
        const int UnitsPerEm = 1000;
        const long Advance = 722;
        const long Kern = -97;
        long multiplier = TextFontMetric.Multiplier(XScale, UnitsPerEm);

        //Act
        long separately = TextFontMetric.EmMult(Advance, multiplier)
            + TextFontMetric.EmMult(Kern, multiplier);
        long together = TextFontMetric.EmMult(Advance + Kern, multiplier);

        //Assert
        separately.Should().NotBe(together);
    }

    [Fact]
    public void the_dot_rounding_snaps_up_at_the_half_dot_and_not_before()
    {
        //Arrange
        // PANGO_UNITS_ROUND is ((d + PANGO_SCALE/2) & ~(PANGO_SCALE - 1)), so 512 is the
        // first value that reaches a whole dot and 511 is the last that does not.
        const long JustUnder = 511;
        const long AtTheBoundary = 512;

        //Act
        long under = TextFontMetric.PangoUnitsRound(JustUnder);
        long at = TextFontMetric.PangoUnitsRound(AtTheBoundary);

        //Assert
        under.Should().Be(0);
        at.Should().Be(1024);
    }
}
