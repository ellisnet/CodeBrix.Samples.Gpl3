/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2020--2026 Daniel Eble <nine.fierce.ballads@gmail.com>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

// was previously: flower/test-interval.cc, flower/test-interval-set.cc
// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port:
//   - translated from C++/yaffut to C#/xUnit v3 with SilverAssertions
//   - the NaN and infinity cases are upstream's, and they are the reason this file
//     exists: an interval implementation that gets them wrong fails silently

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Flower.Tests;

public class IntervalTests
{
    [Fact]
    public void a_default_interval_is_empty()
    {
        //Arrange / Act -- this is the case C# would get wrong without the assigned
        //flag: zeroed fields would read as a zero-length interval at the origin
        Interval iv = default;

        //Assert
        iv.IsEmpty.Should().BeTrue();
        iv.Left.Should().Be(double.PositiveInfinity);
        iv.Right.Should().Be(double.NegativeInfinity);
    }

    [Fact]
    public void an_interval_in_a_fresh_array_is_empty()
    {
        //Arrange / Act -- array allocation also bypasses constructors
        Interval[] intervals = new Interval[3];

        //Assert
        intervals[0].IsEmpty.Should().BeTrue();
        intervals[2].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void accumulating_points_from_empty_works()
    {
        //Arrange -- the whole reason empty is stored inverted
        Interval iv = default;

        //Act
        iv.AddPoint(5.0);
        iv.AddPoint(-2.0);

        //Assert
        iv.Left.Should().Be(-2.0);
        iv.Right.Should().Be(5.0);
    }

    [Fact]
    public void set_empty_and_set_full_use_the_sentinels()
    {
        //Arrange
        Interval iv = new Interval(-33, 33);

        //Act
        iv.SetEmpty();

        //Assert
        iv.IsEmpty.Should().BeTrue();

        //Act
        iv.SetFull();

        //Assert
        iv.Left.Should().Be(double.NegativeInfinity);
        iv.Right.Should().Be(double.PositiveInfinity);
        iv.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void is_empty_is_false_for_every_nan_combination()
    {
        //Arrange / Act / Assert -- NaN comparisons are always false, so an interval
        //with a NaN bound is never "empty". Upstream tests exactly this.
        new Interval(-double.NaN, -double.NaN).IsEmpty.Should().BeFalse();
        new Interval(-double.NaN, 0).IsEmpty.Should().BeFalse();
        new Interval(-double.NaN, double.NaN).IsEmpty.Should().BeFalse();
        new Interval(0, double.NaN).IsEmpty.Should().BeFalse();
        new Interval(double.NaN, 0).IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void length_with_infinite_bounds_matches_upstream()
    {
        //Arrange / Act / Assert
        double.IsNaN(new Interval(double.NegativeInfinity, double.NegativeInfinity).Length)
            .Should().BeTrue();
        new Interval(double.NegativeInfinity, 0).Length.Should().Be(double.PositiveInfinity);
        new Interval(double.NegativeInfinity, double.PositiveInfinity).Length
            .Should().Be(double.PositiveInfinity);

        //An inverted pair is empty, so its length is zero rather than negative
        new Interval(0, double.NegativeInfinity).Length.Should().Be(0.0);
    }

    [Fact]
    public void length_with_nan_bounds_is_nan()
    {
        //Arrange / Act / Assert
        double.IsNaN(new Interval(0, double.NaN).Length).Should().BeTrue();
        double.IsNaN(new Interval(double.NaN, 0).Length).Should().BeTrue();
        double.IsNaN(new Interval(-double.NaN, double.NaN).Length).Should().BeTrue();
    }

    [Fact]
    public void linear_combination_interpolates_across_the_interval()
    {
        //Arrange
        Interval iv = new Interval(10.0, 20.0);

        //Act / Assert -- -1 is the left end, +1 the right, 0 the centre
        iv.LinearCombination(-1.0).Should().Be(10.0);
        iv.LinearCombination(0.0).Should().Be(15.0);
        iv.LinearCombination(1.0).Should().Be(20.0);
    }

    [Fact]
    public void inverse_linear_combination_undoes_linear_combination()
    {
        //Arrange
        Interval iv = new Interval(10.0, 20.0);

        //Act / Assert
        iv.InverseLinearCombination(10.0).Should().Be(-1.0);
        iv.InverseLinearCombination(15.0).Should().Be(0.0);
        iv.InverseLinearCombination(20.0).Should().Be(1.0);
    }

    [Fact]
    public void a_point_interval_has_zero_length_and_is_its_own_centre()
    {
        //Arrange / Act
        Interval iv = new Interval(42.0);

        //Assert
        iv.Length.Should().Be(0.0);
        iv.Center.Should().Be(42.0);
        iv.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void unite_and_intersect_behave_as_expected()
    {
        //Arrange
        Interval a = new Interval(0, 10);

        //Act
        a.Unite(new Interval(5, 20));

        //Assert
        a.Should().Be(new Interval(0, 20));

        //Act
        a.Intersect(new Interval(15, 30));

        //Assert
        a.Should().Be(new Interval(15, 20));
    }

    [Fact]
    public void intersecting_disjoint_intervals_gives_an_empty_one()
    {
        //Arrange / Act
        Interval result = Interval.Intersection(new Interval(0, 1), new Interval(5, 6));

        //Assert
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void contains_is_inclusive_at_both_ends()
    {
        //Arrange
        Interval iv = new Interval(0, 10);

        //Act / Assert
        iv.Contains(0).Should().BeTrue();
        iv.Contains(10).Should().BeTrue();
        iv.Contains(5).Should().BeTrue();
        iv.Contains(-0.001).Should().BeFalse();
    }

    [Fact]
    public void clamp_leaves_a_value_alone_when_the_interval_is_empty()
    {
        //Arrange / Act / Assert
        new Interval(0, 10).Clamp(20).Should().Be(10.0);
        new Interval(0, 10).Clamp(-5).Should().Be(0.0);
        Interval.Empty.Clamp(99).Should().Be(99.0);
    }

    [Fact]
    public void distance_is_zero_inside_and_the_gap_outside()
    {
        //Arrange
        Interval iv = new Interval(0, 10);

        //Act / Assert
        iv.Distance(5).Should().Be(0.0);
        iv.Distance(12).Should().Be(2.0);
        iv.Distance(-3).Should().Be(3.0);
    }

    [Fact]
    public void widen_translate_negate_and_swap()
    {
        //Arrange
        Interval iv = new Interval(2, 4);

        //Act
        iv.Widen(1);

        //Assert
        iv.Should().Be(new Interval(1, 5));

        //Act
        iv.Translate(10);

        //Assert
        iv.Should().Be(new Interval(11, 15));

        //Act
        iv.Negate();

        //Assert
        iv.Should().Be(new Interval(-15, -11));
    }

    [Fact]
    public void scaling_by_a_negative_factor_keeps_the_ends_ordered()
    {
        //Arrange / Act
        Interval result = new Interval(2, 4) * -1.0;

        //Assert -- without the swap this would be the inverted, and so "empty", (-2,-4)
        result.Should().Be(new Interval(-4, -2));
        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void indexing_by_direction_addresses_the_two_ends()
    {
        //Arrange
        Interval iv = new Interval(1, 9);

        //Act / Assert
        iv[Direction.Negative].Should().Be(1.0);
        iv[Direction.Positive].Should().Be(9.0);
    }

    [Fact]
    public void compare_reports_containment()
    {
        //Arrange / Act / Assert
        Interval.Compare(new Interval(0, 10), new Interval(0, 10)).Should().Be(0);
        Interval.Compare(new Interval(0, 10), new Interval(2, 8)).Should().Be(1);
        Interval.Compare(new Interval(2, 8), new Interval(0, 10)).Should().Be(-1);
    }

    [Fact]
    public void compare_throws_when_neither_interval_contains_the_other()
    {
        //Arrange / Act / Assert -- upstream asserts here; the relation is partial
        Assert.Throws<InvalidOperationException>(
            () => Interval.Compare(new Interval(0, 5), new Interval(3, 8)));
    }

    [Fact]
    public void to_string_matches_upstream_formatting()
    {
        //Arrange / Act / Assert
        Interval.Empty.ToString().Should().Be("[empty]");
        new Interval(1, 2).ToString().Should().Be("[1,2]");
    }

    [Fact]
    public void union_disjoint_pushes_the_other_interval_clear()
    {
        //Arrange -- the two overlap, so uniting disjointly must translate the second
        Interval a = new Interval(0, 10);

        //Act
        Interval result = a.UnionDisjoint(new Interval(5, 15), 1.0, Direction.Positive);

        //Assert -- the result starts where a does and extends past a's right edge
        result.Left.Should().Be(0.0);
        (result.Right > 10.0).Should().BeTrue();
    }
}

public class SliceTests
{
    [Fact]
    public void slice_uses_negated_max_as_its_lower_sentinel()
    {
        //Arrange / Act / Assert -- NOT int.MinValue, which would break negation
        Slice.MinSentinel.Should().Be(-int.MaxValue);
        Slice.MaxSentinel.Should().Be(int.MaxValue);
        Slice.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void unite_and_contains_work_over_integers()
    {
        //Arrange
        Slice s = new Slice(0, 5);

        //Act
        s.Unite(new Slice(3, 9));

        //Assert
        s.Should().Be(new Slice(0, 9));
        s.Contains(9).Should().BeTrue();
        s.Length.Should().Be(9);
    }
}

public class IntervalSetTests
{
    [Fact]
    public void union_merges_overlapping_intervals()
    {
        //Arrange
        List<Interval> input = new List<Interval>
        {
            new Interval(0, 3),
            new Interval(2, 5),
            new Interval(10, 12),
        };

        //Act
        IntervalSet set = IntervalSet.IntervalUnion(input);

        //Assert
        set.Intervals.Count.Should().Be(2);
        set.Intervals[0].Should().Be(new Interval(0, 5));
        set.Intervals[1].Should().Be(new Interval(10, 12));
    }

    [Fact]
    public void union_merges_intervals_that_merely_touch()
    {
        //Arrange -- upstream uses >=, so abutting intervals merge
        List<Interval> input = new List<Interval>
        {
            new Interval(0, 5),
            new Interval(5, 10),
        };

        //Act
        IntervalSet set = IntervalSet.IntervalUnion(input);

        //Assert
        set.Intervals.Count.Should().Be(1);
        set.Intervals[0].Should().Be(new Interval(0, 10));
    }

    [Fact]
    public void union_of_nothing_is_empty()
        => IntervalSet.IntervalUnion(new List<Interval>()).Intervals.Count.Should().Be(0);

    [Fact]
    public void nearest_point_returns_the_input_when_it_is_already_inside()
    {
        //Arrange
        IntervalSet set = IntervalSet.IntervalUnion(new[] { new Interval(0, 10) });

        //Act / Assert
        set.NearestPoint(5).Should().Be(5.0);
    }

    [Fact]
    public void nearest_point_finds_the_closer_edge()
    {
        //Arrange
        IntervalSet set = IntervalSet.IntervalUnion(
            new[] { new Interval(0, 10), new Interval(20, 30) });

        //Act / Assert -- 12 is nearer to 10 than to 20
        set.NearestPoint(12).Should().Be(10.0);
        set.NearestPoint(18).Should().Be(20.0);
    }

    [Fact]
    public void nearest_point_can_be_restricted_to_one_side()
    {
        //Arrange
        IntervalSet set = IntervalSet.IntervalUnion(
            new[] { new Interval(0, 10), new Interval(20, 30) });

        //Act / Assert
        set.NearestPoint(12, Direction.Positive).Should().Be(20.0);
        set.NearestPoint(12, Direction.Negative).Should().Be(10.0);
    }

    [Fact]
    public void complement_of_an_empty_set_is_the_whole_line()
    {
        //Arrange / Act
        IntervalSet complement = new IntervalSet().Complement();

        //Assert
        complement.Intervals.Count.Should().Be(1);
        complement.Intervals[0].Left.Should().Be(double.NegativeInfinity);
        complement.Intervals[0].Right.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void complement_returns_the_gaps()
    {
        //Arrange
        IntervalSet set = IntervalSet.IntervalUnion(
            new[] { new Interval(0, 10), new Interval(20, 30) });

        //Act
        IntervalSet complement = set.Complement();

        //Assert -- (-inf,0), (10,20), (30,+inf)
        complement.Intervals.Count.Should().Be(3);
        complement.Intervals[1].Should().Be(new Interval(10, 20));
    }
}

public class MatrixTests
{
    [Fact]
    public void stores_and_reads_cells()
    {
        //Arrange
        Matrix<int> matrix = new Matrix<int>(2, 3, 0);

        //Act
        matrix[1, 2] = 42;

        //Assert
        matrix[1, 2].Should().Be(42);
        matrix[0, 0].Should().Be(0);
        matrix.Rows.Should().Be(2);
        matrix.Columns.Should().Be(3);
    }

    [Fact]
    public void resize_preserves_the_overlapping_region()
    {
        //Arrange
        Matrix<int> matrix = new Matrix<int>(2, 2, 0);
        matrix[0, 0] = 1;
        matrix[1, 1] = 4;

        //Act
        matrix.Resize(3, 3, -1);

        //Assert
        matrix[0, 0].Should().Be(1);
        matrix[1, 1].Should().Be(4);
        matrix[2, 2].Should().Be(-1);
        matrix.Rows.Should().Be(3);
    }
}
