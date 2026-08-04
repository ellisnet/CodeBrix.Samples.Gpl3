// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The skyline geometry: buildings, skylines and skyline pairs.
/// <para>
/// Upstream has no unit tests for <c>lily/</c>, so every expectation here was derived
/// by hand from the C++ in <c>lily/skyline.cc</c> — the merge walk, the
/// stored-upside-down convention for DOWN skylines, and the sign convention in
/// <c>internal_distance</c>, where a NEGATIVE distance means the two outlines do not
/// reach each other.
/// </para>
/// </summary>
public class SkylineTests
{
    private static Box MakeBox(double left, double right, double down, double up)
        => new Box(new Interval(left, right), new Interval(down, up));

    [Fact]
    public void a_fresh_skyline_is_empty_and_infinitely_low()
    {
        //Arrange
        Skyline sky = new Skyline(Direction.Positive);

        //Act
        bool empty = sky.IsEmpty;

        //Assert
        empty.Should().BeTrue();
        sky.MaxHeight().Should().Be(double.NegativeInfinity);
        sky.Left().Should().Be(double.PositiveInfinity);
        sky.Right().Should().Be(double.NegativeInfinity);
    }

    [Fact]
    public void an_upward_skyline_over_one_box_is_flat_at_the_box_top()
    {
        //Arrange
        Box box = MakeBox(0.0, 1.0, 0.0, 2.0);

        //Act
        Skyline sky = new Skyline(box, Axis.X, Direction.Positive);

        //Assert
        sky.Height(0.5).Should().Be(2.0);
        sky.Height(1.0).Should().Be(2.0);
        sky.Height(5.0).Should().Be(double.NegativeInfinity);
        sky.IsEmpty.Should().BeFalse();

        // At a shared edge the LEFT building wins, because the lookup finds the first
        // building whose right edge reaches x. Upstream's lower_bound does the same.
        sky.Height(0.0).Should().Be(double.NegativeInfinity);
    }

    [Fact]
    public void a_downward_skyline_over_one_box_reports_the_box_bottom()
    {
        //Arrange
        // A DOWN skyline is stored upside-down, so this is the case that would break
        // first if the direction sign were dropped anywhere.
        Box box = MakeBox(0.0, 1.0, -3.0, 2.0);

        //Act
        Skyline sky = new Skyline(box, Axis.X, Direction.Negative);

        //Assert
        sky.Height(0.5).Should().Be(-3.0);
    }

    [Fact]
    public void a_box_empty_on_either_axis_contributes_nothing()
    {
        //Arrange
        Box empty = default;
        List<Box> boxes = new List<Box> { empty };

        //Act
        Skyline sky = new Skyline(boxes, Axis.X, Direction.Positive);

        //Assert
        sky.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void a_skyline_over_two_boxes_takes_the_higher_roof_over_each_span()
    {
        //Arrange
        List<Box> boxes = new List<Box>
        {
            MakeBox(0.0, 2.0, 0.0, 2.0),
            MakeBox(1.0, 3.0, 0.0, 5.0),
        };

        //Act
        Skyline sky = new Skyline(boxes, Axis.X, Direction.Positive);

        //Assert
        sky.Height(0.5).Should().Be(2.0);
        sky.Height(1.5).Should().Be(5.0);
        sky.Height(2.5).Should().Be(5.0);
        sky.MaxHeight().Should().Be(5.0);
    }

    [Fact]
    public void merging_a_taller_skyline_raises_the_roof()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);
        Skyline taller = new Skyline(MakeBox(0.0, 1.0, 0.0, 5.0), Axis.X, Direction.Positive);

        //Act
        sky.Merge(taller);

        //Assert
        sky.Height(0.5).Should().Be(5.0);
    }

    [Fact]
    public void merging_a_shorter_skyline_leaves_the_roof_alone()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 5.0), Axis.X, Direction.Positive);
        Skyline shorter = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);

        //Act
        sky.Merge(shorter);

        //Assert
        sky.Height(0.5).Should().Be(5.0);
    }

    [Fact]
    public void inserting_a_box_merges_it_into_the_skyline()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);

        //Act
        sky.Insert(MakeBox(1.0, 2.0, 0.0, 4.0), Axis.X);

        //Assert
        sky.Height(0.5).Should().Be(2.0);
        sky.Height(1.5).Should().Be(4.0);
    }

    [Fact]
    public void distance_between_opposing_skylines_is_negative_when_they_do_not_touch()
    {
        //Arrange
        // The upward outline tops out at 3; the downward outline bottoms out at 5.
        Skyline up = new Skyline(MakeBox(0.0, 2.0, 0.0, 3.0), Axis.X, Direction.Positive);
        Skyline down = new Skyline(MakeBox(1.0, 3.0, 5.0, 7.0), Axis.X, Direction.Negative);

        //Act
        double distance = up.Distance(down);
        double touch = up.TouchingPoint(down);

        //Assert
        distance.Should().Be(-2.0);
        touch.Should().Be(2.0);
    }

    [Fact]
    public void raising_a_skyline_closes_the_distance_by_exactly_that_much()
    {
        //Arrange
        Skyline up = new Skyline(MakeBox(0.0, 2.0, 0.0, 3.0), Axis.X, Direction.Positive);
        Skyline down = new Skyline(MakeBox(1.0, 3.0, 5.0, 7.0), Axis.X, Direction.Negative);

        //Act
        up.Raise(2.0);

        //Assert
        up.Height(1.0).Should().Be(5.0);
        up.Distance(down).Should().Be(0.0);
    }

    [Fact]
    public void raising_a_downward_skyline_still_moves_it_up_the_page()
    {
        //Arrange
        // The stored intercept is signed by the direction AND read back signed by it,
        // so the two signs cancel: raise moves the reported outline by +r either way.
        Skyline down = new Skyline(MakeBox(0.0, 1.0, 2.0, 4.0), Axis.X, Direction.Negative);

        //Act
        down.Raise(1.0);

        //Assert
        down.Height(0.5).Should().Be(3.0);
    }

    [Fact]
    public void shifting_a_skyline_moves_it_along_the_horizon()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);

        //Act
        sky.Shift(10.0);

        //Assert
        sky.Height(10.5).Should().Be(2.0);
        sky.Height(0.5).Should().Be(double.NegativeInfinity);
        sky.Left().Should().Be(10.0);
        sky.Right().Should().Be(11.0);
    }

    [Fact]
    public void shifting_preserves_the_height_of_a_sloped_building()
    {
        //Arrange
        // The slope-intercept storage means a shift has to correct the intercept, not
        // just the span. This is the case that catches it.
        List<DrulArray<Offset>> segments = new List<DrulArray<Offset>>
        {
            new DrulArray<Offset>(new Offset(0.0, 0.0), new Offset(2.0, 4.0)),
        };

        Skyline sky = new Skyline(segments, Axis.X, Direction.Positive);

        //Act
        sky.Shift(5.0);

        //Assert
        sky.Height(6.0).Should().Be(2.0);
        sky.Height(7.0).Should().Be(4.0);
    }

    [Fact]
    public void a_segment_skyline_interpolates_between_its_ends()
    {
        //Arrange
        List<DrulArray<Offset>> segments = new List<DrulArray<Offset>>
        {
            new DrulArray<Offset>(new Offset(0.0, 0.0), new Offset(2.0, 4.0)),
        };

        //Act
        Skyline sky = new Skyline(segments, Axis.X, Direction.Positive);

        //Assert
        sky.Height(0.5).Should().Be(1.0);
        sky.Height(1.0).Should().Be(2.0);
        sky.Height(2.0).Should().Be(4.0);
    }

    [Fact]
    public void a_segment_given_right_to_left_is_stored_left_to_right()
    {
        //Arrange
        List<DrulArray<Offset>> segments = new List<DrulArray<Offset>>
        {
            new DrulArray<Offset>(new Offset(2.0, 4.0), new Offset(0.0, 0.0)),
        };

        //Act
        Skyline sky = new Skyline(segments, Axis.X, Direction.Positive);

        //Assert
        sky.Height(1.0).Should().Be(2.0);
        sky.Left().Should().Be(0.0);
        sky.Right().Should().Be(2.0);
    }

    [Fact]
    public void max_height_position_names_where_the_skyline_peaks()
    {
        //Arrange
        List<Box> boxes = new List<Box>
        {
            MakeBox(0.0, 1.0, 0.0, 2.0),
            MakeBox(2.0, 3.0, 0.0, 5.0),
        };

        Skyline sky = new Skyline(boxes, Axis.X, Direction.Positive);

        //Act
        double position = sky.MaxHeightPosition();

        //Assert
        sky.MaxHeight().Should().Be(5.0);
        position.Should().Be(3.0);
    }

    [Fact]
    public void set_minimum_height_raises_a_skyline_up_to_a_floor()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);

        //Act
        sky.SetMinimumHeight(1.0);

        //Assert
        sky.Height(0.5).Should().Be(2.0);
        sky.Height(50.0).Should().Be(1.0);
    }

    [Fact]
    public void padding_widens_the_skyline_by_twice_the_padding_on_each_side()
    {
        //Arrange
        // Each side gets a flat apron one padding wide, then a ramp another padding
        // wide that falls by the padding amount.
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);

        //Act
        Skyline padded = sky.Padded(0.5);

        //Assert
        padded.Height(0.5).Should().Be(2.0);
        padded.Height(-0.5).Should().Be(2.0);
        padded.Height(1.5).Should().Be(2.0);
        padded.Left().Should().Be(-1.0);
        padded.Right().Should().Be(2.0);

        // Partway down the outer ramp, which falls by the padding over its width.
        padded.Height(-0.75).Should().BeApproximately(1.75, 1e-9);
    }

    [Fact]
    public void padding_by_zero_returns_the_same_skyline()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);

        //Act
        Skyline padded = sky.Padded(0.0);

        //Assert
        padded.Should().BeSameAs(sky);
    }

    [Fact]
    public void horizon_padding_pushes_opposing_skylines_apart_horizontally()
    {
        //Arrange
        // The two boxes do not overlap horizontally at all, so without padding they
        // never see each other.
        Skyline up = new Skyline(MakeBox(0.0, 1.0, 0.0, 3.0), Axis.X, Direction.Positive);
        Skyline down = new Skyline(MakeBox(2.0, 3.0, 1.0, 5.0), Axis.X, Direction.Negative);

        //Act
        double unpadded = up.Distance(down);
        double padded = up.Distance(down, 2.0);

        //Assert
        unpadded.Should().Be(double.NegativeInfinity);
        padded.Should().Be(2.0);
    }

    [Fact]
    public void to_points_returns_two_points_per_building()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);

        //Act
        List<Offset> points = sky.ToPoints(Axis.X);

        //Assert
        // Three buildings: the infinite one on each side, and the box between them.
        points.Count.Should().Be(6);
        points[2].Should().Be(new Offset(0.0, 2.0));
        points[3].Should().Be(new Offset(1.0, 2.0));
    }

    [Fact]
    public void to_points_on_the_vertical_horizon_swaps_the_coordinates()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);

        //Act
        List<Offset> points = sky.ToPoints(Axis.Y);

        //Assert
        points[2].Should().Be(new Offset(2.0, 0.0));
        points[3].Should().Be(new Offset(2.0, 1.0));
    }

    [Fact]
    public void clearing_a_skyline_empties_it_again()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);

        //Act
        sky.Clear();

        //Assert
        sky.IsEmpty.Should().BeTrue();
        sky.Height(0.5).Should().Be(double.NegativeInfinity);
    }

    [Fact]
    public void a_copy_is_independent_of_the_original()
    {
        //Arrange
        Skyline sky = new Skyline(MakeBox(0.0, 1.0, 0.0, 2.0), Axis.X, Direction.Positive);
        Skyline copy = sky.Copy();

        //Act
        copy.Raise(3.0);

        //Assert
        sky.Height(0.5).Should().Be(2.0);
        copy.Height(0.5).Should().Be(5.0);
    }

    [Fact]
    public void a_building_is_stored_in_slope_intercept_form()
    {
        //Arrange
        Building building = new Building(0.0, 1.0, 3.0, 2.0);

        //Act
        double slope = building.Slope;

        //Assert
        slope.Should().Be(1.0);
        building.YIntercept.Should().Be(1.0);
        building.Height(1.0).Should().Be(2.0);
    }

    [Fact]
    public void a_building_too_steep_to_store_is_flattened_to_its_higher_end()
    {
        //Arrange
        // Upstream clamps at a slope of 1e6, because round-off in the intercept would
        // otherwise dominate the height.
        Building building = new Building(0.0, 0.0, 1e7, 1.0);

        //Act
        double slope = building.Slope;

        //Assert
        slope.Should().Be(0.0);
        building.YIntercept.Should().Be(1e7);
    }

    [Fact]
    public void two_nearly_parallel_buildings_intersect_at_the_later_left_edge()
    {
        //Arrange
        // The near-parallel guard is what stops a division by a near-zero slope
        // difference from throwing the crossing point off to infinity.
        Building a = new Building(0.0, 0.0, 1.0, 10.0);
        Building b = new Building(2.0, 5.0, 6.0, 12.0);

        //Act
        double crossing = a.IntersectionX(b);

        //Assert
        crossing.Should().Be(2.0);
    }

    [Fact]
    public void a_skyline_pair_reports_both_outlines_of_a_box()
    {
        //Arrange
        Box box = MakeBox(0.0, 1.0, -2.0, 3.0);

        //Act
        SkylinePair pair = new SkylinePair(box, Axis.X);

        //Assert
        pair.Up.Height(0.5).Should().Be(3.0);
        pair.Down.Height(0.5).Should().Be(-2.0);
        pair.Left().Should().Be(0.0);
        pair.Right().Should().Be(1.0);
        pair.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void an_empty_skyline_pair_is_empty_on_both_sides()
    {
        //Arrange
        SkylinePair pair = new SkylinePair();

        //Act
        bool empty = pair.IsEmpty;

        //Assert
        empty.Should().BeTrue();
    }

    [Fact]
    public void raising_a_skyline_pair_moves_both_outlines_the_same_way()
    {
        //Arrange
        // Both outlines move up the page by the same amount, so the pair keeps its
        // shape — this is how a whole grob is moved vertically.
        SkylinePair pair = new SkylinePair(MakeBox(0.0, 1.0, -2.0, 3.0), Axis.X);

        //Act
        pair.Raise(1.0);

        //Assert
        pair.Up.Height(0.5).Should().Be(4.0);
        pair.Down.Height(0.5).Should().Be(-1.0);
    }

    [Fact]
    public void merging_skyline_pairs_takes_the_outer_outline_on_each_side()
    {
        //Arrange
        SkylinePair pair = new SkylinePair(MakeBox(0.0, 1.0, -2.0, 3.0), Axis.X);
        SkylinePair other = new SkylinePair(MakeBox(0.0, 1.0, -5.0, 1.0), Axis.X);

        //Act
        pair.Merge(other);

        //Assert
        pair.Up.Height(0.5).Should().Be(3.0);
        pair.Down.Height(0.5).Should().Be(-5.0);
    }

    [Fact]
    public void a_pair_built_from_several_pairs_covers_all_of_them()
    {
        //Arrange
        List<SkylinePair> pairs = new List<SkylinePair>
        {
            new SkylinePair(MakeBox(0.0, 1.0, -2.0, 3.0), Axis.X),
            new SkylinePair(MakeBox(2.0, 3.0, -6.0, 8.0), Axis.X),
        };

        //Act
        SkylinePair merged = new SkylinePair(pairs);

        //Assert
        merged.Up.Height(0.5).Should().Be(3.0);
        merged.Up.Height(2.5).Should().Be(8.0);
        merged.Down.Height(2.5).Should().Be(-6.0);
        merged.Left().Should().Be(0.0);
        merged.Right().Should().Be(3.0);
    }

    [Fact]
    public void padding_a_pair_widens_both_outlines()
    {
        //Arrange
        SkylinePair pair = new SkylinePair(MakeBox(0.0, 1.0, -2.0, 3.0), Axis.X);

        //Act
        pair.Pad(0.5);

        //Assert
        pair.Up.Height(1.25).Should().Be(3.0);
        pair.Down.Height(1.25).Should().Be(-2.0);
    }
}
