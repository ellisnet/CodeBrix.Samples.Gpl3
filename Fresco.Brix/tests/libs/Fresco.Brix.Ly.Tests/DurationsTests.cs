// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SilverAssertions;
using Xunit;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// <see cref="Fraction"/>, <see cref="Durations"/> and <see cref="LyUtil"/>
/// against hand-computed values from python-ly's own code and docstrings
/// (ly/duration.py, ly/util.py) — a whole note is 8 (an eighth of a whole per
/// unit... no: the BASE unit is Fraction(8, 1 &lt;&lt; index), so '1' at index
/// 3 gives 8/8 = 1); the util examples are the docstrings' own.
/// </summary>
public class DurationsTests
{
    [Fact]
    public void a_dotted_quarter_with_scaling_reads_base_and_scaling_apart()
    {
        //Arrange
        //'4.' scaled *2/3: base = 8/(1<<5) = 1/4, dot adds 1/8 -> 3/8;
        //scaling 2/3 — hand-computed from base_scaling_string.
        (Fraction baseValue, Fraction scaling) = Durations.BaseScalingString("4.*2/3");

        //Assert
        baseValue.Should().Be(new Fraction(3, 8));
        scaling.Should().Be(new Fraction(2, 3));
    }

    [Fact]
    public void the_duration_names_map_logarithmic_values()
    {
        //Act + Assert
        Durations.ToString(-2).Should().Be("\\longa");
        Durations.ToString(0).Should().Be("1");
        Durations.ToString(2, dots: 2).Should().Be("4..");
        Durations.ToString(3, dots: 0, new Fraction(1, 3)).Should().Be("8*1/3");
    }

    [Fact]
    public void fraction_string_multiplies_base_and_scaling()
    {
        //Act + Assert
        //'2*3/4': base 1/2, scaling 3/4 -> 3/8.
        Durations.FractionString("2*3/4").Should().Be(new Fraction(3, 8));
        Durations.FormatFraction(new Fraction(5, 1)).Should().Be("5/1");
        Durations.FormatFraction(Fraction.Zero).Should().Be("0");
    }

    [Fact]
    public void fractions_normalize_and_compare_exactly()
    {
        //Act + Assert
        new Fraction(2, 4).Should().Be(new Fraction(1, 2));
        new Fraction(1, -2).Should().Be(new Fraction(-1, 2));
        (new Fraction(1, 3) + new Fraction(1, 6)).Should().Be(new Fraction(1, 2));
        (new Fraction(3, 4) * new Fraction(2, 3)).Should().Be(new Fraction(1, 2));
        (new Fraction(1, 2) / new Fraction(1, 4)).Should().Be(new Fraction(2, 1));
        (new Fraction(1, 4) < new Fraction(1, 3)).Should().BeTrue();
        Fraction.Parse("6/8").Should().Be(new Fraction(3, 4));
    }

    [Fact]
    public void util_number_names_follow_the_docstring_examples()
    {
        //Act + Assert
        LyUtil.Int2Text(1).Should().Be("One");
        LyUtil.Int2Text(0).Should().Be("Zero");
        LyUtil.Int2Text(21).Should().Be("TwentyOne");
        LyUtil.Int2Text(215).Should().Be("TwoHundredFifteen");
        LyUtil.Int2Roman(1).Should().Be("I");
        LyUtil.Int2Roman(12).Should().Be("XII");
        LyUtil.Int2Roman(2015).Should().Be("MMXV");
        LyUtil.Int2Letter(1).Should().Be("A");
        LyUtil.Int2Letter(26).Should().Be("Z");
        LyUtil.Int2Letter(27).Should().Be("AA");
        LyUtil.MkId("Violin").Should().Be("violin");
        LyUtil.MkId("soprano", "verse").Should().Be("sopranoVerse");
        LyUtil.MkId("scoreOne", "choirII").Should().Be("scoreOneChoirII");
    }
}
