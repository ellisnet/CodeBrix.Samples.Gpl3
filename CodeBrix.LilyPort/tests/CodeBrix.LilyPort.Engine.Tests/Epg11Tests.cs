// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG11's arithmetic, asserted against HAND-COMPUTED values.
/// </summary>
/// <remarks>
/// <para>
/// Not against the port's own output. A scorer is a heuristic with no independent notion
/// of "right", so a characterization test — record what the code does today, assert it
/// keeps doing that — will happily lock in a wrong answer and then defend it. Every
/// expected number below was computed from upstream's formula by hand, so the test can
/// disagree with the code.
/// </para>
/// <para>
/// EPG10 established this rule for the beam scorer; ties and slurs follow it.
/// </para>
/// </remarks>
public class Epg11Tests
{
    [Fact]
    public void peak_around_is_one_at_zero_and_zero_at_the_threshold()
    {
        //Arrange
        // peak_around (eps, threshold, x) = max (-eps (x - threshold) / ((x + eps) threshold), 0)
        // At x = 0:  -0.1 * (-1) / (0.1 * 1)   = 1
        // At x = 1:  -0.1 * ( 0) / (1.1 * 1)   = 0
        // At x = 0.4: -0.1 * (-0.6) / (0.5 * 1) = 0.12
        const double Epsilon = 0.1;
        const double Threshold = 1.0;

        //Act / Assert
        Misc.PeakAround(Epsilon, Threshold, 0.0).Should().BeApproximately(1.0, 1e-12);
        Misc.PeakAround(Epsilon, Threshold, Threshold).Should().BeApproximately(0.0, 1e-12);
        Misc.PeakAround(Epsilon, Threshold, 0.4).Should().BeApproximately(0.12, 1e-12);

        // Beyond the threshold the max() clamps it, so a distant object is free.
        Misc.PeakAround(Epsilon, Threshold, 5.0).Should().Be(0.0);
    }

    [Fact]
    public void peak_around_answers_one_for_a_negative_distance()
    {
        //Arrange
        // The x < 0 branch is a SEPARATE early return upstream, not a consequence of the
        // formula: the formula would give 1.833... at x = -0.5, not 1. A negative distance
        // means the objects overlap, and overlap is charged the full penalty, not more.

        //Act / Assert
        Misc.PeakAround(0.1, 1.0, -0.5).Should().Be(1.0);
        Misc.PeakAround(0.1, 1.0, -1000.0).Should().Be(1.0);
    }

    [Fact]
    public void convex_amplifier_is_zero_at_zero_and_one_at_the_standard_distance()
    {
        //Arrange
        // convex_amplifier (standard_x, factor, x) = (e^(factor x / standard_x) - 1) / (e^factor - 1)
        // At x = 0        the numerator is e^0 - 1 = 0.
        // At x = standard the numerator IS the denominator, so it is exactly 1.
        // At standard_x = 2, factor = 0.9, x = 1: (e^0.45 - 1) / (e^0.9 - 1), computed
        // independently as 0.389360766050778.

        //Act / Assert
        Misc.ConvexAmplifier(1.0, 0.9, 0.0).Should().BeApproximately(0.0, 1e-12);
        Misc.ConvexAmplifier(1.0, 0.9, 1.0).Should().BeApproximately(1.0, 1e-12);
        Misc.ConvexAmplifier(2.0, 0.9, 1.0).Should().BeApproximately(0.389360766050778, 1e-12);

        // Convex, not linear: half the distance costs LESS than half the penalty.
        Misc.ConvexAmplifier(2.0, 0.9, 1.0).Should().BeLessThan(0.5);
    }

    [Fact]
    public void linear_interpolate_and_normalize_match_the_header_formulas()
    {
        //Arrange
        // linear_interpolate (x, x1, x2, y1, y2) = (x2-x)/(x2-x1) y1 + (x-x1)/(x2-x1) y2
        // At x = 1 over [0,2] onto [10,20]: 0.5*10 + 0.5*20 = 15.
        // normalize (x, x1, x2) = (x - x1) / (x2 - x1); 1 over [0,4] is 0.25.

        //Act / Assert
        Misc.LinearInterpolate(1.0, 0.0, 2.0, 10.0, 20.0).Should().BeApproximately(15.0, 1e-12);
        Misc.LinearInterpolate(0.0, 0.0, 2.0, 10.0, 20.0).Should().BeApproximately(10.0, 1e-12);
        Misc.LinearInterpolate(2.0, 0.0, 2.0, 10.0, 20.0).Should().BeApproximately(20.0, 1e-12);
        Misc.Normalize(1.0, 0.0, 4.0).Should().BeApproximately(0.25, 1e-12);
    }

    [Fact]
    public void round_halfway_up_breaks_ties_upward_where_dotnet_rounding_does_not()
    {
        //Arrange
        // floor (x - 0.5) + 1.0, which is upstream's own documented example: -7.5 rounds
        // to -7, NOT to -8. Math.Round and C's round both answer -8, so this is exactly
        // the case where a "tidier" implementation would silently change every staff
        // position that lands on a half.

        //Act / Assert
        LibcExtension.RoundHalfwayUp(-7.5).Should().Be(-7.0);
        LibcExtension.RoundHalfwayUp(-0.5).Should().Be(0.0);
        LibcExtension.RoundHalfwayUp(2.5).Should().Be(3.0);
        LibcExtension.RoundHalfwayUp(3.5).Should().Be(4.0);

        // ...and the ordinary cases still round the ordinary way.
        LibcExtension.RoundHalfwayUp(2.4).Should().Be(2.0);
        LibcExtension.RoundHalfwayUp(2.6).Should().Be(3.0);
    }

    [Fact]
    public void tie_configuration_distance_is_signed_and_favours_opposing_directions()
    {
        //Arrange
        // distance (a, b) = 3 (a.pos - b.pos), then + (2 + (a.dir - b.dir)) when that is
        // non-negative and + (2 + (b.dir - a.dir)) when it is negative — so the direction
        // term always ENLARGES the gap, whichever way round the pair is given.
        //
        // a at position 5 pointing up, b at position 3 pointing down:
        //   3 * (5 - 3) = 6, which is >= 0, so + (2 + (1 - (-1))) = + 4  =>  10.
        TieConfiguration high = new TieConfiguration { Position = 5, Dir = Direction.Positive };
        TieConfiguration low = new TieConfiguration { Position = 3, Dir = Direction.Negative };

        //Act / Assert
        TieConfiguration.Distance(high, low).Should().BeApproximately(10.0, 1e-12);

        // Reversed: 3 * (3 - 5) = -6, which is < 0, so + (2 + (high.dir - low.dir)) = + 4.
        TieConfiguration.Distance(low, high).Should().BeApproximately(-2.0, 1e-12);
    }

    [Fact]
    public void a_tie_configuration_copy_does_not_share_score_with_its_original()
    {
        //Arrange
        // Upstream's Tie_configuration is a VALUE inside a std::vector, so every variant
        // the 1-opt search builds gets its own copy and scores it independently. The port
        // makes it a class, which shares by default — so if Copy() ever stops being called
        // (or stops copying a field) one variant's demerits leak into another's and the
        // search silently picks the wrong tie. This is that fence.
        TieConfiguration original = new TieConfiguration { Position = 4, Dir = Direction.Positive };
        original.AddScore(7.5, "minlength");

        //Act
        TieConfiguration copy = original.Copy();
        copy.AddScore(2.5, "tipline");

        //Assert
        copy.Score().Should().BeApproximately(10.0, 1e-12);
        original.Score().Should().BeApproximately(7.5, 1e-12);
        copy.Position.Should().Be(4);
        copy.Dir.Should().Be(Direction.Positive);
    }

    [Fact]
    public void a_ties_configuration_copy_is_deep()
    {
        //Arrange
        // The same hazard one level up: find_best_variation copy-constructs the whole
        // Ties_configuration and then OVERWRITES one element. A shallow copy would write
        // through to the base configuration every variant is measured against, so the
        // search would compare each candidate to a moving target.
        TiesConfiguration original = new TiesConfiguration();
        original.PushBack(new TieConfiguration { Position = 1, Dir = Direction.Negative });
        original.PushBack(new TieConfiguration { Position = 6, Dir = Direction.Positive });
        original.AddScore(3.0, "length symm");

        //Act
        TiesConfiguration variant = original.Copy();
        variant[0].Position = 99;
        variant.ResetScore();
        variant.AddScore(11.0, "monotone edge");

        //Assert
        original[0].Position.Should().Be(1);
        original.Score().Should().BeApproximately(3.0, 1e-12);
        variant[0].Position.Should().Be(99);
        variant.Score().Should().BeApproximately(11.0, 1e-12);
    }

    [Fact]
    public void a_tie_configuration_spans_the_columns_it_is_given()
    {
        //Arrange
        // column_span_length is what tells generate_configuration whether to dodge the
        // stems: a SEMI-tie spans zero columns and must not, an ordinary tie does.
        TieConfiguration tie = new TieConfiguration
        {
            ColumnRanks = new DrulArray<int>(12, 15),
        };
        TieConfiguration semiTie = new TieConfiguration
        {
            ColumnRanks = new DrulArray<int>(12, 12),
        };

        //Act / Assert
        tie.ColumnSpanLength().Should().Be(3);
        semiTie.ColumnSpanLength().Should().Be(0);
    }

    [Fact]
    public void tie_details_start_from_upstreams_constructed_defaults()
    {
        //Arrange
        // NOT the values scm/define-grobs.scm installs — these are the ones upstream's
        // Tie_details constructor sets, which apply before any grob has been read.

        //Act
        TieDetails details = new TieDetails();

        //Assert
        details.StaffSpace.Should().Be(1.0);
        details.HeightLimit.Should().Be(1.0);
        details.Ratio.Should().Be(.333);
    }

    [Fact]
    public void the_tie_curve_rises_towards_its_height_limit_and_never_past_it()
    {
        //Arrange
        // A tie takes the same closed-form shape a slur does: height =
        // h_inf * 2/pi * atan (pi * (width * r_0 / h_inf) / 2). It is bounded by h_inf for
        // every width, which is the whole point of the atan — that is what stops a long
        // tie from becoming a semicircle.
        const double HeightLimit = 0.75;
        const double Ratio = 0.333;

        //Act
        double narrow = BezierBow.SlurHeight(1.0, HeightLimit, Ratio);
        double wide = BezierBow.SlurHeight(4.0, HeightLimit, Ratio);
        double enormous = BezierBow.SlurHeight(1000.0, HeightLimit, Ratio);

        //Assert
        // Computed independently from the formula for width 4: 0.5856618534986101.
        wide.Should().BeApproximately(0.5856618534986101, 1e-12);
        narrow.Should().BeLessThan(wide);
        wide.Should().BeLessThan(enormous);
        enormous.Should().BeLessThan(HeightLimit);
    }
}
