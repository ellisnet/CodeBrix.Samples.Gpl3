// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Flower.Tests;

/// <summary>
/// The cubic Bézier curve, and the directed-offset helper it rotates with.
/// </summary>
public class BezierTests
{
    private const double Tolerance = 1e-12;

    private static Bezier UnitCurve() => new Bezier(new[]
    {
        new Offset(0, 0),
        new Offset(1, 1),
        new Offset(2, 1),
        new Offset(3, 0),
    });

    [Fact]
    public void the_curve_starts_and_ends_at_its_outer_control_points()
    {
        //Arrange
        Bezier curve = UnitCurve();

        //Act
        Offset start = curve.CurvePoint(0);
        Offset end = curve.CurvePoint(1);

        //Assert
        start.X.Should().BeApproximately(0, Tolerance);
        start.Y.Should().BeApproximately(0, Tolerance);
        end.X.Should().BeApproximately(3, Tolerance);
        end.Y.Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void subdividing_gives_two_curves_that_meet_at_the_split_point()
    {
        //Arrange
        Bezier curve = UnitCurve();
        Offset expected = curve.CurvePoint(0.25);

        //Act
        curve.Subdivide(0.25, out Bezier left, out Bezier right);

        //Assert
        left[Bezier.ControlCount - 1].X.Should().BeApproximately(expected.X, Tolerance);
        left[Bezier.ControlCount - 1].Y.Should().BeApproximately(expected.Y, Tolerance);
        right[0].X.Should().BeApproximately(expected.X, Tolerance);
        right[0].Y.Should().BeApproximately(expected.Y, Tolerance);
    }

    [Fact]
    public void extracting_the_whole_range_reproduces_the_original_curve()
    {
        //Arrange
        Bezier curve = UnitCurve();

        //Act
        Bezier extracted = curve.Extract(0.0, 1.0);

        //Assert
        for (int i = 0; i < Bezier.ControlCount; i++)
        {
            extracted[i].X.Should().BeApproximately(curve[i].X, Tolerance);
            extracted[i].Y.Should().BeApproximately(curve[i].Y, Tolerance);
        }
    }

    [Fact]
    public void an_extracted_sub_curve_traces_the_same_path_as_the_original()
    {
        //Arrange
        // A sub-curve of a Bezier curve is in turn a Bezier curve, so the point halfway
        // along the extract must lie on the original at the corresponding parameter.
        Bezier curve = UnitCurve();

        //Act
        Bezier part = curve.Extract(0.25, 0.75);
        Offset middleOfPart = part.CurvePoint(0.5);
        Offset middleOfWhole = curve.CurvePoint(0.5);

        //Assert
        middleOfPart.X.Should().BeApproximately(middleOfWhole.X, 1e-9);
        middleOfPart.Y.Should().BeApproximately(middleOfWhole.Y, 1e-9);
    }

    [Fact]
    public void reversing_swaps_the_outer_control_points()
    {
        //Arrange
        Bezier curve = UnitCurve();

        //Act
        curve.Reverse();

        //Assert
        curve[0].X.Should().BeApproximately(3, Tolerance);
        curve[Bezier.ControlCount - 1].X.Should().BeApproximately(0, Tolerance);
    }

    [Fact]
    public void translating_moves_every_control_point()
    {
        //Arrange
        Bezier curve = UnitCurve();

        //Act
        curve.Translate(new Offset(10, -5));

        //Assert
        curve[0].X.Should().BeApproximately(10, Tolerance);
        curve[0].Y.Should().BeApproximately(-5, Tolerance);
    }

    [Fact]
    public void the_control_point_extent_spans_the_outer_points()
    {
        //Arrange
        Bezier curve = UnitCurve();

        //Act
        Interval x = curve.ControlPointExtent(Axis.X);

        //Assert
        x.Left.Should().BeApproximately(0, Tolerance);
        x.Right.Should().BeApproximately(3, Tolerance);
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(90.0, 0.0, 1.0)]
    [InlineData(180.0, -1.0, 0.0)]
    [InlineData(-90.0, 0.0, -1.0)]
    public void a_directed_offset_points_at_the_requested_angle(double degrees, double x, double y)
    {
        //Arrange & Act
        Offset offset = Offset.Directed(degrees);

        //Assert
        offset.X.Should().BeApproximately(x, 1e-15);
        offset.Y.Should().BeApproximately(y, 1e-15);
    }

    [Fact]
    public void a_directed_offset_has_equal_magnitudes_at_odd_multiples_of_45_degrees()
    {
        //Arrange
        // This is exactly what upstream's sine arrangement is there to guarantee, and it
        // is the reason the implementation must not be "simplified" to cos/sin.
        //Act
        Offset offset = Offset.Directed(45);

        //Assert
        Math.Abs(offset.X).Should().Be(Math.Abs(offset.Y));
    }

    [Fact]
    public void a_directed_offset_folds_angles_beyond_a_full_turn()
    {
        //Arrange & Act
        Offset once = Offset.Directed(45);
        Offset again = Offset.Directed(405);

        //Assert
        again.X.Should().BeApproximately(once.X, 1e-15);
        again.Y.Should().BeApproximately(once.Y, 1e-15);
    }

    [Fact]
    public void a_directed_offset_is_a_unit_vector()
    {
        //Arrange & Act
        Offset offset = Offset.Directed(37.5);

        //Assert
        offset.Length.Should().BeApproximately(1.0, 1e-12);
    }
}
