// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The axis-aligned rectangle. Hand-written, as upstream has no tests for
/// <c>lily/box.cc</c>.
/// </summary>
public class BoxTests
{
    [Fact]
    public void a_default_box_is_empty_on_both_axes()
    {
        //Arrange
        Box box = default;

        //Act
        bool empty = box.IsEmpty;

        //Assert
        empty.Should().BeTrue();
        box.IsEmptyOn(Axis.X).Should().BeTrue();
        box.IsEmptyOn(Axis.Y).Should().BeTrue();
    }

    [Fact]
    public void a_box_empty_on_only_one_axis_is_not_empty()
    {
        //Arrange
        // Upstream's is_empty () wants BOTH axes empty, and the skyline builder
        // depends on the distinction.
        Box box = new Box(new Interval(0.0, 1.0), Interval.Empty);

        //Act
        bool empty = box.IsEmpty;

        //Assert
        empty.Should().BeFalse();
        box.IsEmptyOn(Axis.Y).Should().BeTrue();
    }

    [Fact]
    public void an_inverted_but_finite_extent_does_not_read_as_the_empty_sentinel()
    {
        //Arrange
        // Interval.IsEmpty is "left > right"; Box.IsEmptyOn is "holds the sentinels".
        // They disagree here, and upstream relies on the second meaning.
        Box box = new Box(new Interval(5.0, 1.0), new Interval(0.0, 1.0));

        //Act
        bool emptyOnX = box.IsEmptyOn(Axis.X);

        //Assert
        emptyOnX.Should().BeFalse();
        box.X.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void the_area_is_the_product_of_the_two_lengths()
    {
        //Arrange
        Box box = new Box(new Interval(0.0, 2.0), new Interval(1.0, 4.0));

        //Act
        double area = box.Area;

        //Assert
        area.Should().Be(6.0);
        box.Center.Should().Be(new Offset(1.0, 2.5));
    }

    [Fact]
    public void translating_a_box_moves_both_extents()
    {
        //Arrange
        Box box = new Box(new Interval(0.0, 2.0), new Interval(1.0, 4.0));

        //Act
        box.Translate(new Offset(1.0, -1.0));

        //Assert
        box.X.Should().Be(new Interval(1.0, 3.0));
        box.Y.Should().Be(new Interval(0.0, 3.0));
    }

    [Fact]
    public void translating_leaves_an_empty_axis_empty()
    {
        //Arrange
        Box box = new Box(new Interval(0.0, 2.0), Interval.Empty);

        //Act
        box.Translate(new Offset(1.0, 5.0));

        //Assert
        box.IsEmptyOn(Axis.Y).Should().BeTrue();
        box.X.Should().Be(new Interval(1.0, 3.0));
    }

    [Fact]
    public void uniting_grows_the_box_to_cover_both()
    {
        //Arrange
        Box box = new Box(new Interval(0.0, 1.0), new Interval(0.0, 1.0));
        Box other = new Box(new Interval(2.0, 3.0), new Interval(-1.0, 0.5));

        //Act
        box.Unite(other);

        //Assert
        box.X.Should().Be(new Interval(0.0, 3.0));
        box.Y.Should().Be(new Interval(-1.0, 1.0));
    }

    [Fact]
    public void intersecting_shrinks_the_box_to_the_overlap()
    {
        //Arrange
        Box box = new Box(new Interval(0.0, 3.0), new Interval(0.0, 3.0));
        Box other = new Box(new Interval(1.0, 5.0), new Interval(-1.0, 2.0));

        //Act
        box.Intersect(other);

        //Assert
        box.X.Should().Be(new Interval(1.0, 3.0));
        box.Y.Should().Be(new Interval(0.0, 2.0));
    }

    [Fact]
    public void adding_points_to_an_empty_box_accumulates_a_bounding_box()
    {
        //Arrange
        Box box = default;

        //Act
        box.AddPoint(new Offset(1.0, 2.0));
        box.AddPoint(new Offset(-3.0, 5.0));

        //Assert
        box.X.Should().Be(new Interval(-3.0, 1.0));
        box.Y.Should().Be(new Interval(2.0, 5.0));
    }

    [Fact]
    public void widening_grows_the_box_on_every_side()
    {
        //Arrange
        Box box = new Box(new Interval(0.0, 1.0), new Interval(0.0, 1.0));

        //Act
        box.Widen(0.5, 2.0);

        //Assert
        box.X.Should().Be(new Interval(-0.5, 1.5));
        box.Y.Should().Be(new Interval(-2.0, 3.0));
    }

    [Fact]
    public void scaling_by_a_negative_factor_keeps_the_extents_ordered()
    {
        //Arrange
        Box box = new Box(new Interval(1.0, 2.0), new Interval(1.0, 2.0));

        //Act
        box.Scale(-2.0);

        //Assert
        box.X.Should().Be(new Interval(-4.0, -2.0));
        box.Y.Should().Be(new Interval(-4.0, -2.0));
    }

    [Fact]
    public void the_axis_indexer_addresses_both_extents()
    {
        //Arrange
        Box box = new Box(new Interval(0.0, 1.0), new Interval(2.0, 3.0));

        //Act
        box[Axis.X] = new Interval(5.0, 6.0);

        //Assert
        box.X.Should().Be(new Interval(5.0, 6.0));
        box[Axis.Y].Should().Be(new Interval(2.0, 3.0));
    }
}
