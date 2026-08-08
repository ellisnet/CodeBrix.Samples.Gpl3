// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG12's arithmetic and its configuration queue, asserted against HAND-COMPUTED values
/// and against algebraic properties — never against the port's own output.
/// </summary>
public class Epg12Tests
{
    [Fact]
    public void fit_factor_is_the_worst_ratio_of_avoided_point_to_curve_height()
    {
        //Arrange
        // A symmetric cubic with control points (0,0) (1,1) (2,1) (3,0). Its x-coordinates
        // are uniformly spaced, so x(t) = 3t exactly and x = 1.5 is t = 0.5. At t = 0.5,
        // y = 3(0.25)(0.5)(1) + 3(0.5)(0.25)(1) = 0.375 + 0.375 = 0.75.
        //
        // fit_factor projects each avoided point onto the slur's own frame and reports the
        // largest point-height / curve-height ratio. A point at (1.5, 0.5) therefore gives
        // 0.5 / 0.75 = 2/3: the curve is ALREADY higher than it needs to be, so the factor
        // is below one and generate_curve keeps the original height.
        Bezier curve = new Bezier();
        curve[0] = new Offset(0, 0);
        curve[1] = new Offset(1, 1);
        curve[2] = new Offset(2, 1);
        curve[3] = new Offset(3, 0);

        List<Offset> avoid = new List<Offset> { new Offset(1.5, 0.5) };

        //Act
        double factor = SlurConfiguration.FitFactor(
            new Offset(1, 0), new Offset(0, 1), 0.1, curve, Direction.Positive, avoid);

        //Assert
        factor.Should().BeApproximately(2.0 / 3.0, 1e-9);
    }

    [Fact]
    public void fit_factor_exceeds_one_when_a_point_pokes_through_the_curve()
    {
        //Arrange
        // Same curve; the avoided point is now at (1.5, 1.5), ABOVE the curve's 0.75.
        // 1.5 / 0.75 = 2, so the height has to double for the slur to clear it.
        Bezier curve = new Bezier();
        curve[0] = new Offset(0, 0);
        curve[1] = new Offset(1, 1);
        curve[2] = new Offset(2, 1);
        curve[3] = new Offset(3, 0);

        List<Offset> avoid = new List<Offset> { new Offset(1.5, 1.5) };

        //Act
        double factor = SlurConfiguration.FitFactor(
            new Offset(1, 0), new Offset(0, 1), 0.1, curve, Direction.Positive, avoid);

        //Assert
        factor.Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void fit_factor_ignores_points_near_the_slurs_ends()
    {
        //Arrange
        // close_to_edge_length exists so that an object sitting under a slur's TIP does not
        // force the whole curve upward — the tip is where the slur is lowest by
        // construction, and hoisting the curve to clear it would ruin the shape. The point
        // below is at x = 0.05, well inside a close-to-edge length of 0.5, so it is skipped
        // and the factor stays at its initial zero.
        Bezier curve = new Bezier();
        curve[0] = new Offset(0, 0);
        curve[1] = new Offset(1, 1);
        curve[2] = new Offset(2, 1);
        curve[3] = new Offset(3, 0);

        List<Offset> avoid = new List<Offset> { new Offset(0.05, 5.0) };

        //Act
        double factor = SlurConfiguration.FitFactor(
            new Offset(1, 0), new Offset(0, 1), 0.5, curve, Direction.Positive, avoid);

        //Assert
        factor.Should().Be(0.0);
    }

    [Fact]
    public void fit_factor_does_not_deform_the_curve_it_measures()
    {
        //Arrange
        // Upstream takes the Bezier BY VALUE and then translates, rotates and scales it.
        // The port's Bezier is a class, so without an explicit copy those three mutations
        // would land on the CALLER's curve — the very curve generate_curve is about to
        // keep and draw. This asserts the copy is really there.
        Bezier curve = new Bezier();
        curve[0] = new Offset(0, 0);
        curve[1] = new Offset(1, 1);
        curve[2] = new Offset(2, 1);
        curve[3] = new Offset(3, 0);

        List<Offset> avoid = new List<Offset> { new Offset(1.5, 0.5) };

        //Act
        SlurConfiguration.FitFactor(
            new Offset(1, 0), new Offset(0, 1), 0.1, curve, Direction.Negative, avoid);

        //Assert
        curve[0].Should().Be(new Offset(0, 0));
        curve[1].Should().Be(new Offset(1, 1));
        curve[2].Should().Be(new Offset(2, 1));
        curve[3].Should().Be(new Offset(3, 0));
    }

    [Fact]
    public void a_slur_configuration_refuses_negative_demerits()
    {
        //Arrange
        // Upstream reports a programming error and treats the demerit as zero rather than
        // letting a negative score make a bad candidate look good.
        SlurConfiguration config = SlurConfiguration.NewConfig(
            new DrulArray<Offset>(new Offset(0, 0), new Offset(4, 0)), 0);

        //Act
        config.AddScore(-5.0, "slope");
        config.AddScore(2.0, "encompass");

        //Assert
        config.Score().Should().BeApproximately(2.0, 1e-12);
        config.Card().Should().Be("encompass=2.00");
    }

    [Fact]
    public void a_new_slur_configuration_starts_at_the_second_scorer_and_finishes_after_the_last()
    {
        //Arrange
        // new_config sets next_scorer_todo_ to INITIAL_SCORE + 1, because INITIAL_SCORE is
        // charged at construction and is not a scorer that runs. Four scorers then remain,
        // and done() must not answer true until all four have.
        SlurConfiguration config = SlurConfiguration.NewConfig(
            new DrulArray<Offset>(new Offset(0, 0), new Offset(4, 0)), 7);

        //Act / Assert
        config.Index.Should().Be(7);
        config.NextScorerTodo.Should().Be((int)SlurConfiguration.SlurScorers.InitialScore + 1);
        config.Done().Should().BeFalse();

        config.NextScorerTodo = (int)SlurConfiguration.SlurScorers.NumScorers - 1;
        config.Done().Should().BeFalse();

        config.NextScorerTodo = (int)SlurConfiguration.SlurScorers.NumScorers;
        config.Done().Should().BeTrue();
    }

    [Fact]
    public void the_configuration_heap_returns_the_lowest_score()
    {
        //Arrange
        // The comparator upstream passes is INVERTED — l.score > r.score — so the heap's
        // top is the candidate with the SMALLEST demerits. Getting the inversion backwards
        // would return the worst slur on the page and nothing would fail loudly.
        ConfigurationHeap<double> heap = new ConfigurationHeap<double>((l, r) => l > r);

        //Act
        heap.Push(9.0);
        heap.Push(2.0);
        heap.Push(5.0);

        //Assert
        heap.Top().Should().Be(2.0);
        heap.Pop();
        heap.Top().Should().Be(5.0);
        heap.Pop();
        heap.Top().Should().Be(9.0);
    }

    [Fact]
    public void the_configuration_heap_keeps_the_first_of_several_equal_scores_on_top()
    {
        //Arrange
        // THIS is why the heap is a replica of libstdc++'s rather than a PriorityQueue.
        // push_heap only promotes a new element when Less (parent, value) is STRICTLY true,
        // and Less here is `>`, so an equal score never displaces the one already at the
        // root. Symmetric music produces exactly-equal candidates as the normal case, so
        // which one survives a tie is not a curiosity — it is where the slur ends up.
        //
        // Boxed so reference identity distinguishes equal values.
        object first = 4.0;
        object second = 4.0;
        object third = 4.0;
        ConfigurationHeap<object> heap
            = new ConfigurationHeap<object>((l, r) => (double)l > (double)r);

        //Act
        heap.Push(first);
        heap.Push(second);
        heap.Push(third);

        //Assert
        heap.Top().Should().BeSameAs(first);

        // ...and a strictly better candidate still takes the top, ties notwithstanding.
        object better = 1.0;
        heap.Push(better);
        heap.Top().Should().BeSameAs(better);
    }

    [Fact]
    public void the_configuration_heap_counts_what_it_holds()
    {
        //Arrange
        ConfigurationHeap<double> heap = new ConfigurationHeap<double>((l, r) => l > r);

        //Act / Assert
        heap.Count.Should().Be(0);
        heap.Push(1.0);
        heap.Push(2.0);
        heap.Count.Should().Be(2);
        heap.Pop();
        heap.Count.Should().Be(1);
    }

    [Fact]
    public void the_slur_shape_is_the_closed_form_before_any_scoring()
    {
        //Arrange
        // get_slur_indent_height: height is the atan curve, and the indent is
        // 2 h_inf - max_fraction q^2 / (w + q) with q = 2 h_inf / max_fraction and
        // max_fraction = 1/3.1. For h_inf = 0.75 and w = 4:
        //   q = 1.5 * 3.1 = 4.65
        //   indent = 1.5 - (4.65^2 / 3.1) / (4 + 4.65) = 1.5 - 6.975 / 8.65
        const double HeightLimit = 0.75;
        const double Ratio = 0.333;
        const double Width = 4.0;
        double expectedIndent = 1.5 - ((4.65 * 4.65 / 3.1) / (Width + 4.65));

        //Act
        BezierBow.GetSlurIndentHeight(
            out double indent, out double height, Width, HeightLimit, Ratio);
        Bezier shape = BezierBow.SlurShape(Width, HeightLimit, Ratio);

        //Assert
        indent.Should().BeApproximately(expectedIndent, 1e-12);
        height.Should().BeApproximately(0.5856618534986101, 1e-12);

        // The curve runs left to right, points upward, and returns to the baseline.
        shape[0].Should().Be(new Offset(0, 0));
        shape[3].Should().Be(new Offset(Width, 0));
        shape[1].X.Should().BeApproximately(indent, 1e-12);
        shape[2].X.Should().BeApproximately(Width - indent, 1e-12);
        shape[1].Y.Should().BeApproximately(height, 1e-12);
        shape[2].Y.Should().BeApproximately(height, 1e-12);
    }

    [Fact]
    public void the_alteration_constants_are_the_ones_pitch_cc_defines()
    {
        //Arrange
        // Carried by EPG12 because get_extra_encompass_infos shifts an accidental's
        // collision box by a DIFFERENT amount for a flat, a sharp and a natural, and it
        // tells them apart by comparing to these. Alterations are in 200-cent tones, so a
        // semitone is a half, not a whole.

        //Act / Assert
        Music.Pitch.NaturalAlteration.Should().Be(new Rational(0));
        Music.Pitch.FlatAlteration.Should().Be(new Rational(-1, 2));
        Music.Pitch.DoubleFlatAlteration.Should().Be(new Rational(-1));
        Music.Pitch.SharpAlteration.Should().Be(new Rational(1, 2));
        Music.Pitch.DoubleSharpAlteration.Should().Be(new Rational(1));
    }
}
