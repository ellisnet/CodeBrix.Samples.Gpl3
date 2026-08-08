// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG10: the beam group's pure pieces, checked against HAND-COMPUTED arithmetic
/// rather than against the port's own output.
/// <para>
/// <c>beam-quanting.cc</c> is a scorer, so nothing here is allowed to be a
/// characterization test that would happily lock in a wrong answer: the least-squares
/// fit, the minefield search and the beaming pattern all have answers that can be
/// worked out on paper from upstream's own formulas, and those are the answers asserted.
/// </para>
/// </summary>
public class Epg10Tests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    [Fact]
    public void least_squares_fits_the_line_through_collinear_points()
    {
        //Arrange
        List<Offset> points = new List<Offset>
        {
            new Offset(0, 0), new Offset(1, 1), new Offset(2, 2),
        };

        //Act
        LeastSquares.MinimiseLeastSquares(out double coef, out double offset, points);

        //Assert
        coef.Should().BeApproximately(1.0, 1e-12);
        offset.Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void least_squares_matches_the_hand_computed_fit()
    {
        //Arrange
        // sx = 6, sy = 4, sqx = 14, sxy = 12, n = 4.
        // den   = n*sqx - sx^2      = 56 - 36 = 20
        // coef  = (n*sxy - sx*sy)/den = (48 - 24)/20 = 1.2
        // offset= (sy - coef*sx)/n    = (4 - 7.2)/4  = -0.8
        List<Offset> points = new List<Offset>
        {
            new Offset(0, 0), new Offset(1, 0), new Offset(2, 0), new Offset(3, 4),
        };

        //Act
        LeastSquares.MinimiseLeastSquares(out double coef, out double offset, points);

        //Assert
        coef.Should().BeApproximately(1.2, 1e-12);
        offset.Should().BeApproximately(-0.8, 1e-12);
    }

    [Fact]
    public void least_squares_answers_the_mean_when_there_is_nothing_to_minimise()
    {
        //Arrange
        // Every point shares an x, so den is zero. Upstream does NOT leave the answer
        // undefined: it reports a programming error and answers a FLAT line through the
        // mean y, which beam.cc then uses. Losing that fallback gives beams a NaN slope.
        List<Offset> points = new List<Offset> { new Offset(1, 2), new Offset(1, 4) };

        //Act
        LeastSquares.MinimiseLeastSquares(out double coef, out double offset, points);

        //Assert
        coef.Should().Be(0.0);
        offset.Should().BeApproximately(3.0, 1e-12);
    }

    [Fact]
    public void the_minefield_pushes_a_placement_clear_of_a_forbidden_interval()
    {
        //Arrange
        // A beam wanting to start at 0 with a forbidden band of (-1, 1) around it. Each
        // end is pushed epsilon past its own side of the band, and the epsilon is
        // upstream's 1e-10.
        IntervalMinefield minefield = new IntervalMinefield(new Interval(0.0, 0.0), 0.0);
        minefield.AddForbiddenInterval(new Interval(-1.0, 1.0));

        //Act
        minefield.Solve();
        Interval feasible = minefield.FeasiblePlacements();

        //Assert
        feasible.Left.Should().BeApproximately(-1.0 - 1e-10, 1e-15);
        feasible.Right.Should().BeApproximately(1.0 + 1e-10, 1e-15);
    }

    [Fact]
    public void the_minefield_widens_by_the_bulk_it_is_given()
    {
        //Arrange
        // bulk is the beam's own thickness: the placement has to clear the band by half
        // of it on each side, on top of the epsilon.
        IntervalMinefield minefield = new IntervalMinefield(new Interval(0.0, 0.0), 2.0);
        minefield.AddForbiddenInterval(new Interval(-1.0, 1.0));

        //Act
        minefield.Solve();
        Interval feasible = minefield.FeasiblePlacements();

        //Assert
        feasible.Left.Should().BeApproximately(-2.0 - 1e-10, 1e-9);
        feasible.Right.Should().BeApproximately(2.0 + 1e-10, 1e-9);
    }

    [Fact]
    public void the_minefield_leaves_an_uncontested_placement_alone()
    {
        //Arrange
        IntervalMinefield minefield = new IntervalMinefield(new Interval(5.0, 5.0), 0.0);
        minefield.AddForbiddenInterval(new Interval(-1.0, 1.0));

        //Act
        minefield.Solve();
        Interval feasible = minefield.FeasiblePlacements();

        //Assert
        feasible.Left.Should().Be(5.0);
        feasible.Right.Should().Be(5.0);
    }

    [Fact]
    public void a_beaming_pattern_gives_every_plain_eighth_one_beamlet_a_side()
    {
        //Arrange
        BeamingPattern pattern = FourEighths();

        //Act
        pattern.Beamify(DefaultOptions());

        //Assert
        for (int i = 0; i < 4; i++)
        {
            pattern.BeamletCount(i, Direction.Negative).Should().Be(1u);
            pattern.BeamletCount(i, Direction.Positive).Should().Be(1u);
        }
    }

    [Fact]
    public void a_beaming_pattern_gives_a_sixteenth_two_beamlets()
    {
        //Arrange
        // beam_count is max (duration_log - 2, 0): an eighth has duration-log 3 and one
        // beam, a sixteenth has 4 and two.
        BeamingPattern pattern = new BeamingPattern(Rational.Zero);
        Rational at = Rational.Zero;
        for (int i = 0; i < 4; i++)
        {
            pattern.AddStem(at, false, new Duration(4, 0), null);
            at += new Rational(1, 16);
        }

        //Act
        pattern.Beamify(DefaultOptions());

        //Assert
        pattern.BeamletCount(0, Direction.Positive).Should().Be(2u);
        pattern.BeamletCount(3, Direction.Negative).Should().Be(2u);
    }

    [Fact]
    public void an_invisible_stem_takes_its_least_beamed_neighbours_count()
    {
        //Arrange
        // A rest under a beam is an INVISIBLE stem, and upstream gives it the smaller of
        // its neighbours' beam counts rather than its own, so a sixteenth rest between
        // two eighths does not sprout a second beam.
        BeamingPattern pattern = new BeamingPattern(Rational.Zero);
        pattern.AddStem(Rational.Zero, false, new Duration(3, 0), null);
        pattern.AddStem(new Rational(1, 8), true, new Duration(4, 0), null);
        pattern.AddStem(new Rational(3, 16), false, new Duration(3, 0), null);

        //Act
        pattern.Beamify(DefaultOptions());

        //Assert
        pattern.BeamletCount(1, Direction.Negative).Should().Be(1u);
        pattern.BeamletCount(1, Direction.Positive).Should().Be(1u);
    }

    [Fact]
    public void splitting_a_pattern_moves_the_tail_into_the_new_one()
    {
        //Arrange
        // Auto-beaming splits a run when a shorter note turns up and creates a new break;
        // the first half stays put and the second becomes a fresh pattern whose measure
        // offset is where it actually starts.
        BeamingPattern pattern = FourEighths();

        //Act
        BeamingPattern tail = pattern.SplitPattern(1, Rational.One);

        //Assert
        pattern.Count.Should().Be(2);
        tail.Count.Should().Be(2);
        tail.MeasureOffset.Should().Be(new Rational(1, 4));
        tail.StartMoment(0).Should().Be(new Rational(1, 4));
        tail.StartMoment(1).Should().Be(new Rational(3, 8));
    }

    [Fact]
    public void every_beam_callback_is_a_live_procedure()
    {
        //Arrange
        string[] callbacks =
        {
            "ly:beam::calc-normal-stems", "ly:beam::calc-direction",
            "ly:beam::calc-beaming", "ly:beam::calc-stem-shorten",
            "ly:beam::calc-cross-staff", "ly:beam::set-stem-lengths",
            "ly:beam::quanting", "ly:beam::tremolo-springs-and-rods",
            "ly:beam::calc-beam-segments", "ly:beam::calc-x-positions",
            "ly:beam::print", "ly:beam::rest-collision-callback",
            "ly:beam::pure-rest-collision-callback",
        };

        //Act & Assert
        Epg8TestHarness.Loaded();
        try
        {
            foreach (string name in callbacks)
            {
                // A stub answers #t to procedure? as well, so the test asks the closure
                // whether the name is IMPLEMENTED, not merely bound.
                Epg8TestHarness.Eval("(procedure? " + name + ")").Should().Be(true);
            }
        }
        finally
        {
            Epg8TestHarness.Cleanup();
        }
    }

    [Fact]
    public void every_beam_translator_is_registered()
    {
        //Arrange
        string[] translators =
        {
            "Beam_engraver", "Grace_beam_engraver", "Auto_beam_engraver",
            "Grace_auto_beam_engraver", "Beam_collision_engraver",
            "Chord_tremolo_engraver",
        };

        //Act & Assert
        Epg8TestHarness.Loaded();
        try
        {
            foreach (string name in translators)
            {
                LilyPondScheme.Registries.Translators.ContainsKey(Sym(name))
                    .Should().BeTrue();
            }
        }
        finally
        {
            Epg8TestHarness.Cleanup();
        }
    }

    private static BeamingPattern FourEighths()
    {
        BeamingPattern pattern = new BeamingPattern(Rational.Zero);
        Rational at = Rational.Zero;
        for (int i = 0; i < 4; i++)
        {
            pattern.AddStem(at, false, new Duration(3, 0), null);
            at += new Rational(1, 8);
        }

        return pattern;
    }

    private static BeamingOptions DefaultOptions()
        => new BeamingOptions
        {
            BeatBase = new Rational(1, 4),
            BeatStructure = Pair.List(1L, 1L, 1L, 1L),
            Period = Rational.One,
        };
}
