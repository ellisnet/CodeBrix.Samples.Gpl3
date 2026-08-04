/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  Jan Nieuwenhuizen <janneke@gnu.org>

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

using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/lookup.cc, lily/include/lookup.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// The stencil shape constructors: the primitive drawing vocabulary the engine builds
/// every graphical object from.
/// <para>
/// Each of these returns a <see cref="Stencil"/> whose expression names one of the
/// backend procedures — <c>polygon</c>, <c>draw-line</c>, <c>round-filled-box</c>,
/// <c>circle</c>, <c>path</c>. The extents are computed here, in C#, because the
/// engine needs them long before anything is rendered.
/// </para>
/// </summary>
public static class Lookup
{
    private static readonly Symbol PolygonHead = Symbol.Intern("polygon");
    private static readonly Symbol CircleHead = Symbol.Intern("circle");
    private static readonly Symbol RoundFilledBoxHead = Symbol.Intern("round-filled-box");
    private static readonly Symbol PathHead = Symbol.Intern("path");
    private static readonly Symbol MoveTo = Symbol.Intern("moveto");
    private static readonly Symbol LineTo = Symbol.Intern("lineto");
    private static readonly Symbol RLineTo = Symbol.Intern("rlineto");
    private static readonly Symbol CurveTo = Symbol.Intern("curveto");
    private static readonly Symbol ClosePath = Symbol.Intern("closepath");
    private static readonly Symbol Round = Symbol.Intern("round");

    /// <summary>Returns a stencil for one beam: a sloped, blotted quadrilateral.</summary>
    /// <param name="slope">The beam's slope.</param>
    /// <param name="width">The beam's horizontal length.</param>
    /// <param name="thickness">The beam's vertical thickness.</param>
    /// <param name="blot">The corner rounding diameter.</param>
    /// <returns>The beam stencil.</returns>
    public static Stencil Beam(double slope, double width, double thickness, double blot)
    {
        Box b = default;

        // Upstream conses each point on the front of the list, so the list ends up in
        // reverse of the order the corners are visited. The order matters to the
        // backend, so it is preserved exactly.
        List<double> flat = new List<double>();

        Offset p = new Offset(0, thickness / 2);
        b.AddPoint(p);
        p += new Offset(1, -1) * (blot / 2);
        PushFront(flat, p);

        p = new Offset(0, -thickness / 2);
        b.AddPoint(p);
        p += new Offset(1, 1) * (blot / 2);
        PushFront(flat, p);

        p = new Offset(width, (width * slope) - (thickness / 2));
        b.AddPoint(p);
        p += new Offset(-1, 1) * (blot / 2);
        PushFront(flat, p);

        p = new Offset(width, (width * slope) + (thickness / 2));
        b.AddPoint(p);
        p += new Offset(-1, -1) * (blot / 2);
        PushFront(flat, p);

        object points = FlatList(flat);
        object expression = Pair.List(PolygonHead, points, blot, true);

        return new Stencil(b, expression);
    }

    /// <summary>Returns a stencil for a rectangle rotated to a given slope.</summary>
    /// <param name="slope">The slope to rotate to.</param>
    /// <param name="width">The rectangle's length before rotation.</param>
    /// <param name="thickness">The rectangle's thickness.</param>
    /// <param name="blot">The corner rounding diameter.</param>
    /// <returns>The rotated box stencil.</returns>
    public static Stencil RotatedBox(double slope, double width, double thickness, double blot)
    {
        Offset rotation = new Offset(1, slope).Direction();

        List<Offset> points = new List<Offset>
        {
            Offset.ComplexMultiply(new Offset(0, -thickness / 2), rotation),
            Offset.ComplexMultiply(new Offset(width, -thickness / 2), rotation),
            Offset.ComplexMultiply(new Offset(width, thickness / 2), rotation),
            Offset.ComplexMultiply(new Offset(0, thickness / 2), rotation),
        };

        return RoundPolygon(points, blot, -1.0, true);
    }

    /// <summary>Returns a stencil for a horizontal line.</summary>
    /// <param name="extent">The line's horizontal extent.</param>
    /// <param name="thickness">The line's thickness.</param>
    /// <returns>The line stencil.</returns>
    public static Stencil HorizontalLine(Interval extent, double thickness)
    {
        object at = Pair.List(
            Symbol.Intern("draw-line"),
            thickness,
            extent.Left,
            0.0,
            extent.Right,
            0.0);

        Box box = default;
        box[Axis.X] = extent;
        box[Axis.Y] = new Interval(-thickness / 2, thickness / 2);

        return new Stencil(box, at);
    }

    /// <summary>
    /// Returns a stencil that occupies space but draws nothing. The expression is an
    /// empty string, which every backend renders as nothing at all.
    /// </summary>
    /// <param name="box">The space to occupy.</param>
    /// <returns>The blank stencil.</returns>
    public static Stencil Blank(Box box) => new Stencil(box, string.Empty);

    /// <summary>Returns a stencil for a circle.</summary>
    /// <param name="radius">The circle's radius.</param>
    /// <param name="thickness">The outline thickness.</param>
    /// <param name="filled">Whether the circle is filled.</param>
    /// <returns>The circle stencil.</returns>
    public static Stencil Circle(double radius, double thickness, bool filled)
    {
        Box b = new Box(new Interval(-radius, radius), new Interval(-radius, radius));
        return new Stencil(b, Pair.List(CircleHead, radius, thickness, filled));
    }

    /// <summary>Returns a stencil for a filled rectangle with square corners.</summary>
    /// <param name="box">The rectangle to fill.</param>
    /// <returns>The filled box stencil.</returns>
    public static Stencil FilledBox(Box box) => RoundFilledBox(box, 0.0);

    /*
     * round filled box:
     *
     *   __________________________________
     *  /     \  ^           /     \      ^
     * |         |blot              |     |
     * |       | |dia       |       |     |
     * |         |meter             |     |
     * |\ _ _ /  v           \ _ _ /|     |
     * |                            |     |
     * |                            |     | Box
     * |                    <------>|     | extent
     * |                      blot  |     | (Y_AXIS)
     * |                    diameter|     |
     * |                            |     |
     * |  _ _                  _ _  |     |
     * |/     \              /     \|     |
     * |                            |     |
     * |       |            |       |     |
     * |                            |     |
     * x\_____/______________\_____/|_____v
     * |(0, 0)                       |
     * |                            |
     * |                            |
     * |<-------------------------->|
     *       Box extent (X_AXIS)
     */

    /// <summary>Returns a stencil for a filled rectangle with rounded corners.</summary>
    /// <param name="box">The rectangle to fill.</param>
    /// <param name="blotDiameter">
    /// The corner rounding diameter, clamped to the box's own width and height.
    /// </param>
    /// <returns>The rounded box stencil, or an empty one for a negative dimension.</returns>
    public static Stencil RoundFilledBox(Box box, double blotDiameter)
    {
        double width = box.X.Length;
        blotDiameter = Math.Min(blotDiameter, width);
        double height = box.Y.Length;
        blotDiameter = Math.Min(blotDiameter, height);

        if (blotDiameter < 0.0)
        {
            if (!double.IsInfinity(blotDiameter))
            {
                Warn.Warning(string.Format(
                    CultureInfo.InvariantCulture,
                    "Not drawing a box with negative dimension, {0:F2} by {1:F2}.",
                    width,
                    height));
            }

            return new Stencil(box, Nil.Instance);
        }

        object at = Pair.List(
            RoundFilledBoxHead,
            -box[Axis.X].Left,
            box[Axis.X].Right,
            -box[Axis.Y].Left,
            box[Axis.Y].Right,
            blotDiameter);

        return new Stencil(box, at);
    }

    /*
     * Create Stencil that represents a polygon with round edges.
     *
     * LIMITATIONS:
     *
     * (a) Only outer (convex) edges are rounded.
     *
     * (b) This algorithm works as expected only for polygons whose edges
     * do not intersect.
     *
     * (c) Do not draw rounded polygons that have a leg smaller or thinner
     * than blotdiameter (or set blotdiameter to a sufficiently small value
     * -- maybe even 0.0)!
     *
     * NOTE: Limitations (b) and (c) arise from the fact that round edges
     * are made by moulding sharp edges to round ones rather than adding
     * to a core polygon.
     *
     * An extra parameter "extroversion" has been added since staying just
     * inside of a polygon will reduce its visual size when tracing a
     * rounded path.  If extroversion is zero, the polygon is just traced
     * as-is.  If it is -1 (the default) the drawing will stay just within
     * the given polygon.  If it is 1, the traced line will stay just
     * outside of the given polygon.
     */

    /// <summary>Returns a stencil for a polygon with rounded corners.</summary>
    /// <param name="points">The polygon's vertices, in order.</param>
    /// <param name="blotDiameter">The corner rounding diameter.</param>
    /// <param name="extroversion">
    /// Where the traced line sits relative to the polygon: -1 just inside, 0 on it,
    /// 1 just outside.
    /// </param>
    /// <param name="filled">Whether the polygon is filled.</param>
    /// <returns>The polygon stencil.</returns>
    public static Stencil RoundPolygon(
        IReadOnlyList<Offset> points,
        double blotDiameter,
        double extroversion = 0.0,
        bool filled = true)
    {
        if (points == null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        /* special cases: degenerated polygons */
        if (points.Count == 0)
        {
            return Stencil.Empty;
        }

        if (points.Count == 1)
        {
            Stencil circle = Circle(0.5 * (1.0 + extroversion) * blotDiameter, 0, true);
            circle.Translate(points[0]);
            return circle;
        }

        if (points.Count == 2)
        {
            return LineInterface.MakeLine((1.0 + extroversion) * blotDiameter, points[0], points[1]);
        }

        List<Offset> shrunk = ShrinkPolygon(points, blotDiameter, extroversion);

        /* build scm expression and bounding box */
        List<double> flat = new List<double>();
        Box box = default;
        Box shrunkBox = default;
        for (int i = 0; i < shrunk.Count; i++)
        {
            PushFront(flat, shrunk[i]);
            box.AddPoint(points[i]);
            shrunkBox.AddPoint(shrunk[i]);
        }

        shrunkBox.Widen(0.5 * blotDiameter, 0.5 * blotDiameter);
        box.Unite(shrunkBox);

        object polygonExpression = Pair.List(PolygonHead, FlatList(flat), blotDiameter, filled);

        return new Stencil(box, polygonExpression);
    }

    /// <summary>Returns a stencil for a rectangular frame.</summary>
    /// <param name="box">The frame's outer extents.</param>
    /// <param name="thickness">The frame's line thickness.</param>
    /// <param name="blot">The corner rounding diameter.</param>
    /// <returns>The frame stencil.</returns>
    public static Stencil Frame(Box box, double thickness, double blot)
    {
        Stencil result = Stencil.Empty;

        foreach (Axis a in new[] { Axis.X, Axis.Y })
        {
            Axis o = Axes.Other(a);
            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                Box edges = default;
                edges[a] = new Interval(-1, 1) * (0.5 * thickness) + box[a][d];

                Interval other = new Interval(
                    box[o].Left - (thickness / 2),
                    box[o].Right + (thickness / 2));
                edges[o] = other;

                result.AddStencil(RoundFilledBox(edges, blot));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a stencil for a slur or tie: two Bézier curves forming a sandwich, with
    /// optional dashing.
    /// </summary>
    /// <param name="curve">The slur's centre line.</param>
    /// <param name="curveThickness">The thickness at the slur's middle.</param>
    /// <param name="lineThickness">The thickness of the traced outline.</param>
    /// <param name="dashDetails">
    /// A Scheme list of dash patterns, or any non-pair for a solid slur.
    /// </param>
    /// <returns>The slur stencil.</returns>
    public static Stencil Slur(Bezier curve, double curveThickness, double lineThickness, object dashDetails)
    {
        if (curve == null)
        {
            throw new ArgumentNullException(nameof(curve));
        }

        Stencil result = Stencil.Empty;

        /*
            calculate the offset for the two beziers that make the sandwich
            for the slur
        */
        IReadOnlyList<Offset> control = curve.ControlPoints;
        Offset dir = (control[3] - control[0]).Direction();

        Offset perpendicular = 0.5 * curveThickness * new Offset(-dir.Y, dir.X);

        Offset[] backPoints = new Offset[Bezier.ControlCount];
        Offset[] frontPoints = new Offset[Bezier.ControlCount];
        for (int i = 0; i < Bezier.ControlCount; i++)
        {
            backPoints[i] = control[i];
            frontPoints[i] = control[i];
        }

        backPoints[1] += perpendicular;
        backPoints[2] += perpendicular;
        frontPoints[1] -= perpendicular;
        frontPoints[2] -= perpendicular;

        Bezier back = new Bezier(backPoints);
        Bezier front = new Bezier(frontPoints);

        if (!(dashDetails is Pair))
        {
            /* solid slur  */
            return BezierSandwich(back, front, lineThickness);
        }

        /* dashed or combination slur */
        List<object> segments = Pair.ToList(dashDetails);
        for (int i = 0; i < segments.Count; i++)
        {
            List<object> pattern = Pair.ToList(segments[i]);
            double tMin = PatternValue(pattern, 0, 0.0);
            double tMax = PatternValue(pattern, 1, 1.0);
            double dashFraction = PatternValue(pattern, 2, 1.0);
            double dashPeriod = PatternValue(pattern, 3, 0.75);

            Bezier backSegment = back.Extract(tMin, tMax);
            Bezier curveSegment = front.Extract(tMin, tMax);

            if (dashFraction == 1.0)
            {
                result.AddStencil(BezierSandwich(backSegment, curveSegment, lineThickness));
            }
            else
            {
                double segmentLength =
                    (backSegment.ControlPoints[3] - backSegment.ControlPoints[0]).Length;
                int patternCount = (int)(segmentLength / dashPeriod);
                double patternLength = 1.0 / (patternCount + dashFraction);

                for (int p = 0; p <= patternCount; p++)
                {
                    double startT = p * patternLength;
                    double endT = (p + dashFraction) * patternLength;
                    Bezier backDash = backSegment.Extract(startT, endT);
                    Bezier curveDash = curveSegment.Extract(startT, endT);
                    result.AddStencil(BezierSandwich(backDash, curveDash, lineThickness));
                }
            }
        }

        return result;
    }

    /*
     * Bezier Sandwich:
     *
     *                               .|
     *                        .       |
     *              top .             |
     *              . curve           |
     *          .                     |
     *       .                        |
     *     .                          |
     *    |                           |
     *    |                          .|
     *    |                     .
     *    |         bottom .
     *    |            . curve
     *    |         .
     *    |      .
     *    |   .
     *    | .
     *    |.
     *    |
     *
     */

    /// <summary>Returns a stencil for the region between two Bézier curves.</summary>
    /// <param name="topCurve">The upper curve.</param>
    /// <param name="bottomCurve">The lower curve.</param>
    /// <param name="thickness">The outline thickness.</param>
    /// <returns>The sandwich stencil.</returns>
    public static Stencil BezierSandwich(Bezier topCurve, Bezier bottomCurve, double thickness)
    {
        if (topCurve == null)
        {
            throw new ArgumentNullException(nameof(topCurve));
        }

        if (bottomCurve == null)
        {
            throw new ArgumentNullException(nameof(bottomCurve));
        }

        IReadOnlyList<Offset> top = topCurve.ControlPoints;
        IReadOnlyList<Offset> bottom = bottomCurve.ControlPoints;

        object commands = Pair.List(
            MoveTo, top[0].X, top[0].Y,
            CurveTo, top[1].X, top[1].Y, top[2].X, top[2].Y, top[3].X, top[3].Y,
            LineTo, bottom[3].X, bottom[3].Y,
            CurveTo, bottom[2].X, bottom[2].Y, bottom[1].X, bottom[1].Y, bottom[0].X, bottom[0].Y,
            ClosePath);

        object horizontalBend = Pair.List(PathHead, thickness, commands, Round, Round, true);

        Interval xExtent = topCurve.Extent(Axis.X);
        xExtent.Unite(bottomCurve.Extent(Axis.X));
        Interval yExtent = topCurve.Extent(Axis.Y);
        yExtent.Unite(bottomCurve.Extent(Axis.Y));
        Box b = new Box(xExtent, yExtent);

        b.Widen(0.5 * thickness, 0.5 * thickness);
        return new Stencil(b, horizontalBend);
    }

    /// <summary>Returns a stencil for the slash used by percent repeats.</summary>
    /// <param name="width">The slash's horizontal run.</param>
    /// <param name="slope">The slash's slope.</param>
    /// <param name="thickness">The slash's thickness.</param>
    /// <returns>The slash stencil.</returns>
    public static Stencil RepeatSlash(double width, double slope, double thickness)
    {
        double xWidth = Hypot(thickness, thickness / slope);
        double height = width * slope;

        object controls = Pair.List(
            MoveTo, 0.0, 0.0,
            RLineTo, xWidth, 0.0,
            RLineTo, width, height,
            RLineTo, -xWidth, 0.0,
            ClosePath);

        object slashNoDot = Pair.List(PathHead, 0.0, controls, Round, Round, true);

        Box b = new Box(new Interval(0, width + xWidth), new Interval(0, height));

        return new Stencil(b, slashNoDot); //  http://slashnodot.org
    }

    /// <summary>Returns a stencil for a bracket with turned-in ends.</summary>
    /// <param name="axis">The axis the bracket runs along.</param>
    /// <param name="extent">The bracket's extent along that axis.</param>
    /// <param name="thickness">The bracket's line thickness.</param>
    /// <param name="protrude">How far the ends turn in, and which way.</param>
    /// <param name="blot">The corner rounding diameter.</param>
    /// <returns>The bracket stencil.</returns>
    public static Stencil Bracket(Axis axis, Interval extent, double thickness, double protrude, double blot)
    {
        Box b = default;
        Axis other = Axes.Other(axis);
        b[axis] = extent;
        b[other] = new Interval(-1, 1) * (thickness * 0.5);

        Stencil m = RoundFilledBox(b, blot);

        b[axis] = new Interval(extent.Right - thickness, extent.Right);
        Interval oi = new Interval(-thickness / 2, (thickness / 2) + Math.Abs(protrude));

        // Not Math.Sign: it throws on NaN, where upstream's flower `sign` returns 0.
        oi *= Sign(protrude);
        b[other] = oi;
        m.AddStencil(RoundFilledBox(b, blot));

        b[axis] = new Interval(extent.Left, extent.Left + thickness);
        m.AddStencil(RoundFilledBox(b, blot));

        return m;
    }

    /// <summary>Returns a stencil for a triangle drawn as three lines.</summary>
    /// <param name="extent">The triangle's base, as a horizontal extent.</param>
    /// <param name="thickness">The line thickness.</param>
    /// <param name="protrude">The apex height, which may be negative.</param>
    /// <returns>The triangle stencil.</returns>
    public static Stencil Triangle(Interval extent, double thickness, double protrude)
    {
        List<Offset> points = new List<Offset>
        {
            new Offset(extent.Left, 0),
            new Offset(extent.Right, 0),
            new Offset(extent.Center, protrude),
            new Offset(extent.Left, 0), // close triangle
        };

        return PointsToLineStencil(thickness, points);
    }

    /// <summary>Returns a stencil joining a sequence of points with straight lines.</summary>
    /// <param name="thickness">The line thickness.</param>
    /// <param name="points">The points to join, in order.</param>
    /// <returns>The combined stencil. Non-finite points are skipped.</returns>
    public static Stencil PointsToLineStencil(double thickness, IReadOnlyList<Offset> points)
    {
        if (points == null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        Stencil result = Stencil.Empty;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i - 1].IsSane && points[i].IsSane)
            {
                result.AddStencil(LineInterface.MakeLine(thickness, points[i - 1], points[i]));
            }
        }

        return result;
    }

    private static List<Offset> ShrinkPolygon(
        IReadOnlyList<Offset> points,
        double blotDiameter,
        double extroversion)
    {
        if (extroversion == 0.0)
        {
            return new List<Offset>(points);
        }

        /* shrink polygon in size by 0.5 * blotdiameter */

        // first we need to determine the orientation of the polygon in
        // order to decide whether shrinking means moving the polygon to the
        // left or to the right of the outline.  We do that by calculating
        // (double) the oriented area of the polygon.  We first determine the
        // center and do the area calculations relative to it.
        // Mathematically, the result is not affected by this shift, but
        // numerically a lot of cancellation is going on and this keeps its
        // effects in check.
        Offset center = Offset.Zero;
        for (int i = 0; i < points.Count; i++)
        {
            center += points[i];
        }

        center /= points.Count;

        double area = 0.0;
        Offset last = points[points.Count - 1] - center;

        for (int i = 0; i < points.Count; i++)
        {
            Offset here = points[i] - center;
            area += Offset.CrossProduct(last, here);
            last = here;
        }

        // true if whole shape is counterclockwise oriented
        bool counterclockwise = area >= 0.0;

        Offset[] shrunk = new Offset[points.Count];

        for (int i = 0; i < points.Count; i++)
        {
            int i0 = i;
            int i1 = (i + 1) % points.Count;
            int i2 = (i + 2) % points.Count;
            Offset p0 = points[i0];
            Offset p1 = points[i1];
            Offset p2 = points[i2];
            Offset p01 = p1 - p0;
            Offset p12 = p2 - p1;
            Offset inward0 = new Offset(-p01.Y, p01.X).Direction();
            Offset inward2 = new Offset(-p12.Y, p12.X).Direction();

            if (!counterclockwise)
            {
                inward0 = -inward0;
                inward2 = -inward2;
            }

            Offset middle = 0.5 * (inward0 + inward2);

            // "middle" now is a vector in the right direction for the
            // shrinkage.  Its size needs to be large enough that the
            // projection on either of the inward vectors has a size of 1.
            double projection = Offset.DotProduct(middle, inward0);

            // Basically we want to keep the shape from inverting from pulling
            // too far inward. 3 diameters is pretty much a handwaving guess.
            if (Math.Abs(projection) < 0.03)
            {
                projection = projection < 0 ? -0.03 : 0.03;
            }

            shrunk[i1] = p1 - (((0.5 * blotDiameter / projection) * middle) * extroversion);
        }

        return new List<Offset>(shrunk);
    }

    /// <summary>
    /// Prepends a point's two coordinates to a flat coordinate buffer, matching
    /// upstream's cons-onto-the-front construction so the emitted list order is
    /// identical.
    /// </summary>
    private static void PushFront(List<double> flat, Offset point)
    {
        flat.Insert(0, point.Y);
        flat.Insert(0, point.X);
    }

    private static object FlatList(List<double> flat)
    {
        object result = Nil.Instance;
        for (int i = flat.Count - 1; i >= 0; i--)
        {
            result = new Pair(flat[i], result);
        }

        return result;
    }

    private static double PatternValue(List<object> pattern, int index, double fallback)
    {
        if (index >= pattern.Count)
        {
            return fallback;
        }

        return Bootstrap.SchemeConvert.ToDouble(pattern[index], "ly:slur-dash-pattern");
    }

    private static double Hypot(double a, double b) => Math.Sqrt((a * a) + (b * b));

    private static double Sign(double value) => value > 0 ? 1.0 : (value < 0 ? -1.0 : 0.0);
}
