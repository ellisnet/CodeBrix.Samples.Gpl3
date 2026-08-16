// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Backends.Tests;

/// <summary>
/// <c>partial-ellipse</c> — the elliptical-arc drawing command, which the SVG backend
/// did not handle at all until PARITY 13.
/// <para>
/// Every expected string below is HAND-COMPUTED from <c>scm/output-svg.scm</c>'s own
/// procedure, not recorded from this backend. The two branches are fenced against each
/// other: an arc whose endpoints coincide must come out as an <c>&lt;ellipse&gt;</c>,
/// because an SVG elliptical-arc command with coincident endpoints draws nothing, and
/// an arc whose endpoints do not coincide must come out as a <c>&lt;path&gt;</c>.
/// </para>
/// </summary>
public class SvgPartialEllipseTests
{
    private static Stencil Arc(
        double xRadius,
        double yRadius,
        double startAngle,
        double endAngle,
        double thickness,
        bool connect,
        bool fill)
    {
        object expression = Pair.List(
            Symbol.Intern("partial-ellipse"),
            xRadius, yRadius, startAngle, endAngle, thickness, connect, fill);

        // The extents do not participate in what is written; the backend reads the
        // expression. A unit box keeps the stencil legal.
        return new Stencil(new Box(new Interval(-1, 1), new Interval(-1, 1)), expression);
    }

    [Fact]
    public void an_arc_is_a_path_carrying_the_elliptical_arc_command()
    {
        //Arrange
        // 215 degrees to 325 degrees on the unit circle. Hand-computed:
        //   cos 215 = -0.81915, sin 215 = -0.57358  -> M-0.8192 0.5736   (Y is negated)
        //   cos 325 =  0.81915, sin 325 = -0.57358  ->  0.8192 0.5736
        // start - end = 3.7525 - 5.6723 < 0, so the large-arc flag is 0, and it prints
        // as a bare "0" because ly:format writes an EXACT integer without decimals.
        Stencil stencil = Arc(1, 1, 215, 325, 0.1, true, false);
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("<path");
        svg.Should().Contain(
            "d=\"M-0.8192 0.5736A1.0000 1.0000 0 0 0 0.8192 0.5736L-0.8192,0.5736\"");
        svg.Should().Contain("fill=\"none\"");
        svg.Should().Contain("stroke-width=\"0.1000\"");
    }

    [Fact]
    public void the_large_arc_flag_is_one_when_the_start_angle_is_past_the_end_angle()
    {
        //Arrange
        // 180 degrees to 0: start - end = 3.1416 - 0, which is NOT negative, so the flag
        // is 1. This is the CONTROL for the case above — the same shape of command with
        // the one digit that decides which way round the arc is drawn.
        Stencil stencil = Arc(1, 1, 180, 0, 0.1, true, false);
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("A1.0000 1.0000 0 1 0 ");
    }

    [Fact]
    public void an_arc_that_closes_on_itself_is_written_as_an_ellipse_instead()
    {
        //Arrange
        // 0 to 360: angle-0-360 maps 360 back to 0, so both endpoints are the same point
        // and upstream's 1.5e-3 epsilon takes the ellipse branch.
        Stencil stencil = Arc(1, 1, 0, 360, 0.1, true, false);
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("<ellipse");
        svg.Should().Contain("cx=\"0\" cy=\"0\" rx=\"1.0000\" ry=\"1.0000\"");
        svg.Should().NotContain("<path");
    }

    [Fact]
    public void connect_decides_whether_the_arc_closes_back_to_its_start()
    {
        //Arrange
        Stencil connected = Arc(1, 1, 215, 325, 0.1, true, false);
        Stencil open = Arc(1, 1, 215, 325, 0.1, false, false);
        SvgBackend backend = new SvgBackend();

        //Act
        string withLine = backend.RenderFragment(connected);
        string withoutLine = backend.RenderFragment(open);

        //Assert
        withLine.Should().Contain("L-0.8192,0.5736");
        withoutLine.Should().NotContain("L-0.8192,0.5736");
    }

    [Fact]
    public void an_arc_of_an_ellipse_meets_the_ellipse_rather_than_a_circle()
    {
        //Arrange
        // The radius at an angle is x*y / sqrt(y^2 cos^2 + x^2 sin^2), NOT the circle
        // radius: at 90 degrees on a 2-by-1 ellipse it is 2*1/sqrt(4) = 1, so the point
        // is (0, 1) and the command reads "A2.0000 1.0000".
        Stencil stencil = Arc(2, 1, 90, 200, 0.1, false, false);
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("M0.0000 -1.0000A2.0000 1.0000 0 ");
    }
}
