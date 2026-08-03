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
/// The ported musical value types: scale, pitch, moment and duration.
/// <para>
/// Upstream has no unit tests for <c>lily/</c>, so these are written against the
/// documented behaviour of each entry point and against the arithmetic the C++ performs.
/// </para>
/// </summary>
public class MusicValueTests
{
    [Fact]
    public void the_default_scale_is_the_seven_step_major_scale_in_tones()
    {
        //Arrange
        // scm/lily.scm: (ly:make-scale #(0 1 2 5/2 7/2 9/2 11/2))
        Scale scale = Scale.DefaultGlobal;

        //Act
        int steps = scale.StepCount;

        //Assert
        steps.Should().Be(7);
        scale.StepTones[3].Should().Be(new Rational(5, 2));
    }

    [Fact]
    public void a_scale_step_size_wraps_around_at_the_octave()
    {
        //Arrange
        Scale scale = Scale.DefaultGlobal;

        //Act
        // From B to the C above: 6 tones in an octave, minus B's 11/2.
        Rational last = scale.StepSize(6);

        //Assert
        last.Should().Be(new Rational(1, 2));
    }

    [Fact]
    public void tones_at_step_adds_six_tones_for_each_octave()
    {
        //Arrange
        Scale scale = Scale.DefaultGlobal;

        //Act
        Rational middle = scale.TonesAtStep(0, 0);
        Rational above = scale.TonesAtStep(0, 1);

        //Assert
        middle.Should().Be(Rational.Zero);
        above.Should().Be(new Rational(6));
    }

    [Fact]
    public void a_pitch_normalizes_its_octave_on_construction()
    {
        //Arrange
        // Note name 7 is one octave above note name 0 on a seven-step scale.
        //Act
        Pitch pitch = new Pitch(0, 7, Rational.Zero);

        //Assert
        pitch.Octave.Should().Be(1);
        pitch.NoteName.Should().Be(0);
    }

    [Fact]
    public void a_negative_note_name_normalizes_downward()
    {
        //Arrange & Act
        Pitch pitch = new Pitch(0, -1, Rational.Zero);

        //Assert
        pitch.Octave.Should().Be(-1);
        pitch.NoteName.Should().Be(6);
    }

    [Fact]
    public void pitches_order_by_octave_then_note_name_then_alteration()
    {
        //Arrange
        Pitch c = new Pitch(0, 0, Rational.Zero);
        Pitch cSharp = new Pitch(0, 0, new Rational(1, 2));
        Pitch d = new Pitch(0, 1, Rational.Zero);
        Pitch cAbove = new Pitch(1, 0, Rational.Zero);

        //Act & Assert
        Pitch.Compare(c, cSharp).Should().BeLessThan(0);
        Pitch.Compare(cSharp, d).Should().BeLessThan(0);
        Pitch.Compare(d, cAbove).Should().BeLessThan(0);
        Pitch.Compare(c, c).Should().Be(0);
    }

    [Fact]
    public void semitone_pitch_counts_twelve_to_the_octave()
    {
        //Arrange
        Pitch c = new Pitch(0, 0, Rational.Zero);
        Pitch cAbove = new Pitch(1, 0, Rational.Zero);

        //Act
        int low = c.RoundedSemitonePitch();
        int high = cAbove.RoundedSemitonePitch();

        //Assert
        (high - low).Should().Be(12);
    }

    [Fact]
    public void transposing_by_a_pitch_adds_both_step_and_sound()
    {
        //Arrange
        Pitch c = new Pitch(0, 0, Rational.Zero);
        Pitch wholeTone = new Pitch(0, 1, Rational.Zero);

        //Act
        Pitch transposed = c.Transposed(wholeTone);

        //Assert
        transposed.NoteName.Should().Be(1);
        transposed.Octave.Should().Be(0);
    }

    [Fact]
    public void a_pitch_prints_in_lilypond_note_name_form()
    {
        //Arrange
        // Note name 0 is C, and octave 0 is the one containing middle C, which LilyPond
        // writes with a single apostrophe.
        Pitch c = new Pitch(0, 0, Rational.Zero);
        Pitch cSharp = new Pitch(0, 0, new Rational(1, 2));

        //Act
        string plain = c.ToString();
        string sharp = cSharp.ToString();

        //Assert
        plain.Should().Be("c'");
        sharp.Should().Be("cis'");
    }

    [Fact]
    public void a_moment_adds_main_and_grace_parts_independently()
    {
        //Arrange
        Moment a = new Moment(new Rational(1, 4), new Rational(1, 8));
        Moment b = new Moment(new Rational(1, 4), new Rational(1, 8));

        //Act
        Moment sum = a + b;

        //Assert
        sum.MainPart.Should().Be(new Rational(1, 2));
        sum.GracePart.Should().Be(new Rational(1, 4));
    }

    [Fact]
    public void moments_compare_on_the_main_part_before_the_grace_part()
    {
        //Arrange
        Moment plain = new Moment(new Rational(1, 4), Rational.Zero);
        Moment graced = new Moment(new Rational(1, 4), new Rational(1, 8));
        Moment later = new Moment(new Rational(1, 2), Rational.Zero);

        //Act & Assert
        (plain < graced).Should().BeTrue();
        (graced < later).Should().BeTrue();
        plain.Equals(new Moment(new Rational(1, 4))).Should().BeTrue();
    }

    [Fact]
    public void a_quarter_note_duration_is_a_quarter_of_a_whole_note()
    {
        //Arrange
        // The duration log is the negative base-2 logarithm: 2 is a quarter note.
        Duration quarter = new Duration(2, 0);

        //Act
        Rational length = quarter.ToWholeNotes();

        //Assert
        length.Should().Be(new Rational(1, 4));
    }

    [Fact]
    public void each_dot_adds_half_of_the_previous_increment()
    {
        //Arrange
        Duration dotted = new Duration(2, 1);
        Duration doubleDotted = new Duration(2, 2);

        //Act & Assert
        dotted.ToWholeNotes().Should().Be(new Rational(3, 8));
        doubleDotted.ToWholeNotes().Should().Be(new Rational(7, 16));
    }

    [Fact]
    public void a_duration_round_trips_through_its_length_in_whole_notes()
    {
        //Arrange
        Duration dotted = new Duration(2, 1);

        //Act
        Duration rebuilt = Duration.FromWholeNotes(dotted.ToWholeNotes(), false);

        //Assert
        rebuilt.DurationLog.Should().Be(2);
        rebuilt.DotCount.Should().Be(1);
    }

    [Fact]
    public void compressing_a_duration_scales_its_length_but_not_its_spelling()
    {
        //Arrange
        Duration quarter = new Duration(2, 0);

        //Act
        Duration triplet = quarter.Compressed(new Rational(2, 3));

        //Assert
        triplet.DurationLog.Should().Be(2);
        triplet.DotCount.Should().Be(0);
        triplet.ToWholeNotes().Should().Be(new Rational(1, 6));
    }

    [Fact]
    public void durations_shorter_than_a_sixty_fourth_note_collapse_to_a_scale_factor()
    {
        //Arrange
        // Upstream only writes durations down to 64th notes; anything shorter becomes a
        // 64th note carrying the remainder as a factor.
        Rational veryShort = new Rational(1, 256);

        //Act
        Duration duration = Duration.FromWholeNotes(veryShort, false);

        //Assert
        duration.DurationLog.Should().Be(6);
        duration.DotCount.Should().Be(0);
        duration.ToWholeNotes().Should().Be(veryShort);
    }
}
