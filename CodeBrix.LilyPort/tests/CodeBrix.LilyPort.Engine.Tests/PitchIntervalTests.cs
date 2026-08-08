// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Pins <see cref="PitchInterval"/> and <see cref="PitchLexicographicInterval"/>: the
/// inverted default state, the expansion reporting <c>add_point</c> answers, and the
/// one place the two types disagree — a comparison by sounding height against one by
/// spelling.
/// </summary>
public class PitchIntervalTests
{
    [Fact]
    public void a_fresh_interval_is_empty_until_a_point_lands()
    {
        //Arrange
        PitchInterval interval = new PitchInterval();

        //Act
        bool before = interval.IsEmpty();
        DrulArray<bool> expands = interval.AddPoint(new Pitch(0, 0, Rational.Zero));
        bool after = interval.IsEmpty();

        //Assert
        // The default bounds are octave 100 against octave -100, so the FIRST point
        // must expand both sides at once.
        before.Should().BeTrue();
        expands.Negative.Should().BeTrue();
        expands.Positive.Should().BeTrue();
        after.Should().BeFalse();
    }

    [Fact]
    public void add_point_reports_which_side_moved()
    {
        //Arrange
        PitchInterval interval = new PitchInterval();
        interval.AddPoint(new Pitch(0, 3, Rational.Zero));

        //Act
        DrulArray<bool> lower = interval.AddPoint(new Pitch(0, 0, Rational.Zero));
        DrulArray<bool> higher = interval.AddPoint(new Pitch(1, 1, Rational.Zero));
        DrulArray<bool> inside = interval.AddPoint(new Pitch(0, 4, Rational.Zero));

        //Assert
        lower.Negative.Should().BeTrue();
        lower.Positive.Should().BeFalse();
        higher.Positive.Should().BeTrue();
        higher.Negative.Should().BeFalse();
        inside.Negative.Should().BeFalse();
        inside.Positive.Should().BeFalse();
        interval[Direction.Negative].Steps().Should().Be(0);
        interval[Direction.Positive].Steps().Should().Be(8);
    }

    [Fact]
    public void the_tone_interval_compares_by_sounding_height_not_spelling()
    {
        //Arrange
        // bisis (b double sharp, 6.5 tones) SOUNDS above the next octave's c (6.0
        // tones), while lexicographically it sits below it (octave 0 against 1). The
        // two interval types must disagree about which one is the top.
        Pitch bSharp = new Pitch(0, 6, Rational.One);
        Pitch cNatural = new Pitch(1, 0, Rational.Zero);

        PitchInterval byTone = new PitchInterval();
        PitchLexicographicInterval bySpelling = new PitchLexicographicInterval();

        //Act
        byTone.AddPoint(cNatural);
        DrulArray<bool> toneExpands = byTone.AddPoint(bSharp);

        bySpelling.AddPoint(cNatural);
        DrulArray<bool> spellingExpands = bySpelling.AddPoint(bSharp);

        //Assert
        toneExpands.Positive.Should().BeTrue();
        spellingExpands.Positive.Should().BeFalse();
        spellingExpands.Negative.Should().BeTrue();
    }
}
