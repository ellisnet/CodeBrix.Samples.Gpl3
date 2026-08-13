// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Globalization;
using CodeBrix.LilyPort.Engine.Fonts;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The lattice text-font sizes live on.
/// <para>
/// Upstream never hands Pango the size it computed. <c>lily/font-select.cc:215-216</c>
/// reads
/// <c>int pango_size = static_cast&lt;int&gt; (std::lround (requested_size * PANGO_SCALE));</c>
/// followed by <c>pango_font_description_set_size (description, pango_size)</c>, and
/// <c>PANGO_SCALE</c> is 1024 — so every text size in play is a whole number of
/// 1/1024ths, and the description string that carries it to the backend is written to
/// three decimals.
/// </para>
/// <para>
/// The expected values here are HAND-COMPUTED from that expression, never recorded from
/// the port's own output, and each fact is paired with a control that must come out
/// differently.
/// </para>
/// </summary>
public class PangoFontSizeTests
{
    [Fact]
    public void a_quantized_size_is_a_whole_number_of_pango_units()
    {
        //Arrange
        // The default text size: text-font-size 11 multiplied by point_constant
        // (1 PT in mm = 25.4/72.27), which is what select_font hands Pango at font-size 0.
        double requested = 11.0 * (25.4 / 72.27);

        //Act
        double quantized = FontInterface.QuantizeToPangoUnits(requested);

        //Assert
        // lround (3.866057... * 1024) == 3959, so the size is 3959/1024 exactly.
        (quantized * 1024.0).Should().BeApproximately(3959.0, 1e-9);
        quantized.Should().BeApproximately(3959.0 / 1024.0, 1e-12);

        // The control: the raw value is NOT on the lattice, so quantizing is a real
        // change rather than a no-op that would pass whatever the code did.
        (requested * 1024.0).Should().NotBeApproximately(3959.0, 1e-3);
    }

    [Fact]
    public void sizes_closer_together_than_one_pango_unit_collapse_onto_the_same_lattice_point()
    {
        //Arrange
        // Half a Pango unit apart, either side of a lattice point.
        double point = 3959.0 / 1024.0;
        double justBelow = point - 0.2 / 1024.0;
        double justAbove = point + 0.2 / 1024.0;

        //Act
        double a = FontInterface.QuantizeToPangoUnits(justBelow);
        double b = FontInterface.QuantizeToPangoUnits(justAbove);

        //Assert
        a.Should().BeApproximately(b, 1e-12);

        // The control: more than a whole unit apart and they must stay apart, or the
        // assertion above would be satisfied by a function that returned a constant.
        double farApart = FontInterface.QuantizeToPangoUnits(point + 1.6 / 1024.0);
        farApart.Should().NotBeApproximately(a, 1e-9);
    }

    [Fact]
    public void a_half_unit_rounds_away_from_zero_as_lround_does()
    {
        //Arrange
        // std::lround rounds halves AWAY from zero. C#'s default Math.Round is banker's
        // rounding and would answer 3958 here, which is the bug this pins.
        double exactlyHalf = 3958.5 / 1024.0;

        //Act
        double quantized = FontInterface.QuantizeToPangoUnits(exactlyHalf);

        //Assert
        (quantized * 1024.0).Should().BeApproximately(3959.0, 1e-9);
    }

    [Fact]
    public void the_description_string_carries_the_size_to_three_decimals()
    {
        //Arrange
        // The description is the ONLY route the size takes to the backend, which parses
        // it back out with upstream's own regular expression. Upstream's string comes
        // from pango_font_description_to_string, which writes three decimals.
        double size = FontInterface.QuantizeToPangoUnits(11.0 * (25.4 / 72.27));
        TextFontMetric font = new TextFontMetric("serif", false, false, false, size, 1.7573);

        //Act
        string description = font.DescriptionString;

        //Assert
        // 3959/1024 == 3.8662109375, so three decimals is "3.866".
        description.Should().Be("serif 3.866");

        // The control: a size a whole Pango unit away must print differently, so the
        // assertion above is not satisfied by a formatter that has lost the value.
        TextFontMetric other = new TextFontMetric(
            "serif", false, false, false, 3970.0 / 1024.0, 1.7573);
        other.DescriptionString.Should().NotBe(description);
    }

    [Fact]
    public void the_description_string_is_written_the_way_the_backend_reads_it()
    {
        //Arrange
        // The style words and their order are what output-svg.scm's
        // ( Bold)?( Italic)?( Small-Caps)?[ -]([0-9.]+)$ expects; anything else silently
        // lands in the family name instead.
        double size = FontInterface.QuantizeToPangoUnits(4.0);
        TextFontMetric font = new TextFontMetric("serif", true, true, true, size, 1.7573);

        //Act
        string description = font.DescriptionString;

        //Assert
        description.Should().StartWith("serif Bold Italic Small-Caps ");

        // The tail must parse as a number in the invariant culture, whatever the host's
        // decimal separator is.
        string tail = description.Substring(description.LastIndexOf(' ') + 1);
        double parsed = double.Parse(tail, CultureInfo.InvariantCulture);
        (parsed * 1024.0).Should().BeApproximately(Math.Round(4.0 * 1024.0), 0.6);
    }
}
