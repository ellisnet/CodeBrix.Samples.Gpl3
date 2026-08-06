/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2013--2026 Mike Solomon <mike@mikesolomon.org>

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
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/stencil-integral.cc;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// Where a grob's SKYLINES come from — the outline other grobs are kept clear of.
/// <para>
/// A grob's extents are a rectangle, and a rectangle is a poor description of a slur or
/// a beam. Skylines are the finer answer: for each of the two directions along an axis,
/// the profile of how far the grob actually reaches at every position across it. That
/// is what lets a slur tuck under a beam instead of being pushed clear of its bounding
/// box.
/// </para>
/// <para>
/// Three families of callback build them, and this file has all three:
/// </para>
/// <list type="bullet">
/// <item>FROM EXTENTS — the grob's own bounding box, one rectangle. Exact.</item>
/// <item>FROM ELEMENT STENCILS — the union of the children's skylines, each shifted
/// into this grob's frame. Exact, given the children's.</item>
/// <item>FROM STENCIL — the grob's own drawing, WALKED: every line, curve, ellipse,
/// box and glyph outline in the stencil expression is turned into segments, so the
/// skyline follows the ink.</item>
/// </list>
/// <para>
/// The walk has one deliberate fallback, upstream's own: an expression it cannot
/// decompose (<c>embedded-ps</c> is upstream's example) contributes nothing, and a
/// stencil that contributed nothing at all falls back to its extent box.
/// </para>
/// </summary>
public static class StencilIntegral
{
    /// <summary>
    /// How finely curves are flattened, in output units. Upstream's
    /// <c>QUANTIZATION_UNIT</c>, and a global there rather than a constant — it is a
    /// speed/accuracy dial, not a fact about the notation.
    /// </summary>
    private const double QuantizationUnit = 0.2;

    private static readonly Symbol RotationSymbol = Symbol.Intern("rotation");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol VerticalSkylinesSymbol = Symbol.Intern("vertical-skylines");
    private static readonly Symbol HorizontalSkylinesSymbol
        = Symbol.Intern("horizontal-skylines");
    private static readonly Symbol SkylineHorizontalPaddingSymbol
        = Symbol.Intern("skyline-horizontal-padding");
    private static readonly Symbol SkylineVerticalPaddingSymbol
        = Symbol.Intern("skyline-vertical-padding");

    private static readonly Symbol CombineStencilSymbol = Symbol.Intern("combine-stencil");
    private static readonly Symbol TranslateStencilSymbol = Symbol.Intern("translate-stencil");
    private static readonly Symbol ScaleStencilSymbol = Symbol.Intern("scale-stencil");
    private static readonly Symbol RotateStencilSymbol = Symbol.Intern("rotate-stencil");
    private static readonly Symbol GrobCauseSymbol = Symbol.Intern("grob-cause");
    private static readonly Symbol ColorSymbol = Symbol.Intern("color");
    private static readonly Symbol OutputAttributesSymbol = Symbol.Intern("output-attributes");
    private static readonly Symbol Utf8StringSymbol = Symbol.Intern("utf-8-string");
    private static readonly Symbol WithOutlineSymbol = Symbol.Intern("with-outline");
    private static readonly Symbol DrawLineSymbol = Symbol.Intern("draw-line");
    private static readonly Symbol DashedLineSymbol = Symbol.Intern("dashed-line");
    private static readonly Symbol CircleSymbol = Symbol.Intern("circle");
    private static readonly Symbol EllipseSymbol = Symbol.Intern("ellipse");
    private static readonly Symbol PartialEllipseSymbol = Symbol.Intern("partial-ellipse");
    private static readonly Symbol RoundFilledBoxSymbol = Symbol.Intern("round-filled-box");
    private static readonly Symbol NamedGlyphSymbol = Symbol.Intern("named-glyph");
    private static readonly Symbol PolygonSymbol = Symbol.Intern("polygon");
    private static readonly Symbol PathSymbol = Symbol.Intern("path");

    private static readonly Symbol MoveToSymbol = Symbol.Intern("moveto");
    private static readonly Symbol RMoveToSymbol = Symbol.Intern("rmoveto");
    private static readonly Symbol LineToSymbol = Symbol.Intern("lineto");
    private static readonly Symbol RLineToSymbol = Symbol.Intern("rlineto");
    private static readonly Symbol CurveToSymbol = Symbol.Intern("curveto");
    private static readonly Symbol RCurveToSymbol = Symbol.Intern("rcurveto");
    private static readonly Symbol ClosePathSymbol = Symbol.Intern("closepath");

    /// <summary>
    /// The one-rectangle skyline pair, from the grob's own extents.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <param name="axis">The horizon axis.</param>
    /// <param name="ignoreX">
    /// Whether to treat the horizontal extent as infinite. Set before line breaking,
    /// when how far a spanner stretches is not yet known.
    /// </param>
    /// <param name="ignoreY">
    /// Whether to treat the vertical extent as infinite. Set for a cross-staff grob,
    /// whose height is not known until axis groups are spaced.
    /// </param>
    /// <returns>The skyline pair.</returns>
    public static SkylinePair SimpleSkylinesFromExtents(
        Grob grob, Axis axis, bool ignoreX, bool ignoreY)
    {
        if (grob == null)
        {
            throw new ArgumentNullException(nameof(grob));
        }

        Interval x = ignoreX
            ? new Interval(double.NegativeInfinity, double.PositiveInfinity)
            : grob.Extent(grob, Axis.X);

        Interval y = ignoreY
            ? new Interval(double.NegativeInfinity, double.PositiveInfinity)
            : grob.Extent(grob, Axis.Y);

        if (x.IsEmpty || y.IsEmpty)
        {
            return new SkylinePair();
        }

        return new SkylinePair(new List<Box> { new Box(x, y) }, axis);
    }

    /// <summary>
    /// <c>ly:grob::simple-vertical-skylines-from-extents</c>.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The skyline pair.</returns>
    public static SkylinePair SimpleVerticalFromExtents(Grob grob)
        => SimpleSkylinesFromExtents(grob, Axis.X, false, false);

    /// <summary>
    /// <c>ly:grob::pure-simple-vertical-skylines-from-extents</c>. The horizontal
    /// extent is taken as infinite: before line breaking there is no way to measure how
    /// far a spanner reaches.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The skyline pair.</returns>
    public static SkylinePair PureSimpleVerticalFromExtents(Grob grob)
        => SimpleSkylinesFromExtents(grob, Axis.X, true, false);

    /// <summary>
    /// <c>ly:grob::simple-horizontal-skylines-from-extents</c>.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The skyline pair.</returns>
    public static SkylinePair SimpleHorizontalFromExtents(Grob grob)
        => SimpleSkylinesFromExtents(grob, Axis.Y, false, IsCrossStaff(grob));

    /// <summary>
    /// <c>ly:grob::pure-simple-horizontal-skylines-from-extents</c>.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The skyline pair.</returns>
    public static SkylinePair PureSimpleHorizontalFromExtents(Grob grob)
        => SimpleSkylinesFromExtents(grob, Axis.Y, true, IsCrossStaff(grob));

    /// <summary>
    /// The skyline pair of a grob's own drawing.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <param name="axis">The horizon axis.</param>
    /// <returns>The skyline pair.</returns>
    public static SkylinePair SkylinesFromStencil(Grob grob, Axis axis)
    {
        if (grob == null)
        {
            throw new ArgumentNullException(nameof(grob));
        }

        SkylinePair pair = SkylinesFromStencil(
            grob.GetStencil(), grob.GetProperty(RotationSymbol), axis);

        object padding = grob.GetProperty(
            axis == Axis.X ? SkylineHorizontalPaddingSymbol : SkylineVerticalPaddingSymbol);
        if (Bootstrap.SchemeConvert.IsNumber(padding))
        {
            pair.Pad(Bootstrap.SchemeConvert.ToDouble(padding, "skyline padding"));
        }

        return pair;
    }

    /// <summary>
    /// Walks a stencil and returns the skylines its ink traces out.
    /// </summary>
    /// <param name="stencil">The stencil, or <see langword="null"/> for none.</param>
    /// <param name="rotation">
    /// The grob's <c>rotation</c> property, as <c>(ANGLE X Y)</c> with the centre given
    /// relative to the STENCIL's extents rather than to the grob.
    /// </param>
    /// <param name="axis">The horizon axis.</param>
    /// <returns>The skyline pair.</returns>
    public static SkylinePair SkylinesFromStencil(Stencil? stencil, object rotation, Axis axis)
    {
        LazySkylinePair lazy = new LazySkylinePair(axis);
        if (!stencil.HasValue)
        {
            return lazy.ToPair();
        }

        Stencil drawing = stencil.Value;

        if (rotation is Pair parts)
        {
            List<object> values = Pair.ToList(parts);
            if (values.Count >= 3)
            {
                drawing.RotateDegrees(
                    Real(values[0]),
                    new Offset(Real(values[1]), Real(values[2])));
            }
        }

        // A stencil that mixes text with drawing is the one case the walk cannot grade,
        // and it takes the whole stencil's box rather than the drawn part alone. See
        // TEXT STENCILS in Engine/PORT-COVERAGE.txt: the port's text expression carries
        // no inner drawing to walk, so walking would silently omit the text — and a
        // skyline that omits ink is the one error direction that causes collisions.
        // EPG14 is when this becomes measurable and worth closing properly.
        if (!ContainsText(drawing.Expression))
        {
            InterpretForSkyline(lazy, Transform.Identity, drawing.Expression);
        }

        if (lazy.IsEmpty && !drawing.IsEmptyOn(Axis.X) && !drawing.IsEmptyOn(Axis.Y))
        {
            // Upstream's own fallback, for an expression it cannot decompose.
            lazy.AddBox(Transform.Identity, drawing.ExtentBox);
        }

        return lazy.ToPair();
    }

    /// <summary>
    /// The union of a grob's children's skylines, each moved into this grob's frame.
    /// <para>
    /// The two moves are NOT interchangeable, and upstream's comment records what
    /// getting them the wrong way round cost: a skyline is <c>Shift</c>ed along the
    /// horizon axis and <c>Raise</c>d along the other one. Swapping them transposes
    /// every child's contribution, which still produces a plausible skyline.
    /// </para>
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <param name="axis">The horizon axis.</param>
    /// <returns>The merged skyline pair.</returns>
    public static SkylinePair SkylinesFromElementStencils(Grob grob, Axis axis)
    {
        if (grob == null)
        {
            throw new ArgumentNullException(nameof(grob));
        }

        SkylinePair result = new SkylinePair();
        if (!(grob.GetProperty(ElementsSymbol) is GrobArray array) || array.IsEmpty)
        {
            return result;
        }

        IReadOnlyList<Grob> elements = array.Array;

        Grob xCommon = CommonRefpointOf(elements, grob, Axis.X);
        Grob yCommon = CommonRefpointOf(elements, grob, Axis.Y);

        double myX = grob.RelativeCoordinate(xCommon, Axis.X);
        double myY = grob.RelativeCoordinate(yCommon, Axis.Y);

        Symbol property = axis == Axis.X ? VerticalSkylinesSymbol : HorizontalSkylinesSymbol;

        foreach (Grob element in elements)
        {
            SkylinePair child = SkylinePair.FromScheme(element.GetProperty(property));
            if (child == null)
            {
                continue;
            }

            Offset offset = new Offset(
                element.RelativeCoordinate(xCommon, Axis.X) - myX,
                element.RelativeCoordinate(yCommon, Axis.Y) - myY);

            child.Shift(offset[axis]);
            child.Raise(offset[axis == Axis.X ? Axis.Y : Axis.X]);
            result.Merge(child);
        }

        return result;
    }

    /// <summary>
    /// Walks a stencil expression, dispatching on its head and adding what each
    /// drawing command traces out.
    /// <para>
    /// A head this does not know contributes nothing and raises NO warning, which is
    /// upstream's decision and its reason: the registry in
    /// <c>stencil-expression.cc</c> is what checks that a head is legal, so a complaint
    /// here would only be a second, worse copy of that check.
    /// </para>
    /// </summary>
    /// <param name="skyline">The collector to add to.</param>
    /// <param name="transform">The transform in force at this point of the tree.</param>
    /// <param name="expression">The expression.</param>
    public static void InterpretForSkyline(
        LazySkylinePair skyline, Transform transform, object expression)
    {
        if (skyline == null)
        {
            throw new ArgumentNullException(nameof(skyline));
        }

        if (!(expression is Pair pair))
        {
            return;
        }

        object head = pair.Car;
        object rest = pair.Cdr;

        if (ReferenceEquals(head, CombineStencilSymbol))
        {
            for (object s = rest; s is Pair item; s = item.Cdr)
            {
                InterpretForSkyline(skyline, transform, item.Car);
            }
        }
        else if (ReferenceEquals(head, TranslateStencilSymbol))
        {
            Transform local = transform;
            local.Translate(ToOffset(Second(expression)));
            InterpretForSkyline(skyline, local, Third(expression));
        }
        else if (ReferenceEquals(head, ScaleStencilSymbol))
        {
            object factors = Second(expression);
            Transform local = transform;
            local.Scale(Real(Car(factors)), Real(Second(factors)));
            InterpretForSkyline(skyline, local, Third(expression));
        }
        else if (ReferenceEquals(head, RotateStencilSymbol))
        {
            object arguments = Second(expression);
            Transform local = transform;
            local.Rotate(Real(Car(arguments)), ToOffset(Second(arguments)));
            InterpretForSkyline(skyline, local, Third(expression));
        }
        else if (ReferenceEquals(head, GrobCauseSymbol)
                 || ReferenceEquals(head, ColorSymbol)
                 || ReferenceEquals(head, OutputAttributesSymbol))
        {
            InterpretForSkyline(skyline, transform, Third(expression));
        }
        else if (ReferenceEquals(head, Utf8StringSymbol))
        {
            // The drawing the encapsulation replaces, which upstream fills with the
            // glyph run Pango shaped. See the note in SkylinesFromStencil.
            InterpretForSkyline(skyline, transform, Fourth(expression));
        }
        else if (ReferenceEquals(head, WithOutlineSymbol))
        {
            // The SECOND element: an explicit outline stencil overrides the drawing.
            InterpretForSkyline(skyline, transform, Second(expression));
        }
        else if (ReferenceEquals(head, DrawLineSymbol))
        {
            AddDrawLineSegments(skyline, transform, rest);
        }
        else if (ReferenceEquals(head, DashedLineSymbol))
        {
            // (dashed-line THICK ON OFF DX DY PHASE) — the dashes are ignored and the
            // whole run from the origin to (DX, DY) is taken as drawn, which is what
            // the grob below it must be kept clear of anyway.
            double thickness = Real(Nth(expression, 1));
            double dx = Real(Nth(expression, 4));
            double dy = Real(Nth(expression, 5));
            skyline.AddSegment(transform, Offset.Zero, new Offset(dx, dy), thickness);
        }
        else if (ReferenceEquals(head, CircleSymbol))
        {
            double radius = Real(Nth(expression, 1));
            double thickness = Real(Nth(expression, 2));
            AddPartialEllipseSegments(
                skyline, transform, new Offset(radius, radius), 0.0, 360.0, thickness,
                false, true);
        }
        else if (ReferenceEquals(head, EllipseSymbol))
        {
            double xRadius = Real(Nth(expression, 1));
            double yRadius = Real(Nth(expression, 2));
            double thickness = Real(Nth(expression, 3));
            AddPartialEllipseSegments(
                skyline, transform, new Offset(xRadius, yRadius), 0.0, 360.0, thickness,
                false, true);
        }
        else if (ReferenceEquals(head, PartialEllipseSymbol))
        {
            AddPartialEllipseSegments(skyline, transform, rest);
        }
        else if (ReferenceEquals(head, RoundFilledBoxSymbol))
        {
            AddRoundFilledBoxSegments(skyline, transform, rest);
        }
        else if (ReferenceEquals(head, NamedGlyphSymbol))
        {
            AddNamedGlyphSegments(skyline, transform, rest);
        }
        else if (ReferenceEquals(head, PolygonSymbol))
        {
            AddPolygonSegments(skyline, transform, rest);
        }
        else if (ReferenceEquals(head, PathSymbol))
        {
            AddPathSegments(skyline, transform, rest);
        }
    }

    /// <summary>
    /// <c>(draw-line THICK X0 Y0 X1 Y1)</c>, with the head already stripped.
    /// </summary>
    /// <param name="skyline">The collector.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="arguments">The argument list.</param>
    private static void AddDrawLineSegments(
        LazySkylinePair skyline, Transform transform, object arguments)
    {
        double thickness = Real(Nth(arguments, 0));
        Offset left = new Offset(Real(Nth(arguments, 1)), Real(Nth(arguments, 2)));
        Offset right = new Offset(Real(Nth(arguments, 3)), Real(Nth(arguments, 4)));

        skyline.AddSegment(transform, left, right, thickness);
    }

    /// <summary>
    /// <c>(partial-ellipse X-RAD Y-RAD START END THICK CONNECT FILL)</c>, with the head
    /// already stripped.
    /// </summary>
    /// <param name="skyline">The collector.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="arguments">The argument list.</param>
    private static void AddPartialEllipseSegments(
        LazySkylinePair skyline, Transform transform, object arguments)
    {
        Offset radii = new Offset(Real(Nth(arguments, 0)), Real(Nth(arguments, 1)));
        double start = Real(Nth(arguments, 2));
        double end = Real(Nth(arguments, 3));
        double thickness = Real(Nth(arguments, 4));
        bool connect = SchemeUtilities.ToBool(Nth(arguments, 5));
        bool fill = SchemeUtilities.ToBool(Nth(arguments, 6));

        AddPartialEllipseSegments(
            skyline, transform, radii, start, end, thickness, connect, fill);
    }

    /// <summary>
    /// Flattens an elliptical arc into segments.
    /// </summary>
    /// <param name="skyline">The collector.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="radii">The two radii.</param>
    /// <param name="start">The starting angle, in degrees.</param>
    /// <param name="end">The ending angle, in degrees.</param>
    /// <param name="thickness">The pen's diameter.</param>
    /// <param name="connect">Whether the two ends are joined by a chord.</param>
    /// <param name="fill">Whether the arc is filled, which also joins the ends.</param>
    private static void AddPartialEllipseSegments(
        LazySkylinePair skyline, Transform transform, Offset radii,
        double start, double end, double thickness, bool connect, bool fill)
    {
        if (end == start)
        {
            end += 360;
        }

        // How much the transform stretches each axis, so an ellipse drawn inside a
        // scaled stencil is still flattened finely enough.
        double xScale = Math.Sqrt(Square(transform.XX) + Square(transform.YX));
        double yScale = Math.Sqrt(Square(transform.XY) + Square(transform.YY));

        int quantization = (int)Math.Max(
            1.0,
            ((radii[Axis.X] * xScale) + (radii[Axis.Y] * yScale)) * Math.PI / QuantizationUnit);

        Offset last = Offset.Zero;
        Offset first = Offset.Zero;

        for (int i = 0; i <= quantization; i++)
        {
            double angle = LinearInterpolate(i, 0, quantization, start, end);
            Offset point = Offset.Directed(angle);
            point = new Offset(point.X * radii[Axis.X], point.Y * radii[Axis.Y]);

            if (i > 0)
            {
                skyline.AddSegment(transform, last, point, thickness);
            }
            else
            {
                first = point;
            }

            last = point;
        }

        if (connect || fill)
        {
            skyline.AddSegment(transform, first, last, thickness);
        }
    }

    /// <summary>
    /// <c>(round-filled-box LEFT RIGHT BOTTOM TOP DIAMETER)</c>, with the head already
    /// stripped.
    /// <para>
    /// The four edge values are DISTANCES from the origin, so the box runs from
    /// <c>-LEFT</c> to <c>RIGHT</c> and from <c>-BOTTOM</c> to <c>TOP</c>.
    /// </para>
    /// </summary>
    /// <param name="skyline">The collector.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="arguments">The argument list.</param>
    private static void AddRoundFilledBoxSegments(
        LazySkylinePair skyline, Transform transform, object arguments)
    {
        double left = Real(Nth(arguments, 0));
        double right = Real(Nth(arguments, 1));
        double bottom = Real(Nth(arguments, 2));
        double top = Real(Nth(arguments, 3));
        double diameter = Real(Nth(arguments, 4));

        Interval x = new Interval(-left, right);
        Interval y = new Interval(-bottom, top);
        if (x.IsEmpty || y.IsEmpty)
        {
            return;
        }

        double xScale = Math.Sqrt(Square(transform.XX) + Square(transform.YX));
        double yScale = Math.Sqrt(Square(transform.XY) + Square(transform.YY));

        // A blot smaller than half an output unit is not worth rounding, and a box that
        // is neither rounded nor rotated is exactly four sides — which the collector
        // already knows how to take whole.
        bool rounded = diameter * Math.Max(xScale, yScale) > 0.5;
        bool rotated = transform.YX != 0.0 || transform.XY != 0.0;

        if (!rotated && !rounded)
        {
            skyline.AddBox(transform, new Box(new Interval(-left, right), new Interval(-bottom, top)));
            return;
        }

        int quantization = (int)Math.Max(
            0.0,
            (rounded ? 1 : 0) * diameter * (xScale + yScale) * Math.PI / QuantizationUnit / 8);

        // With no quantization there is nothing to draw the corners with, so the
        // effective corner radius is zero and the sides meet square.
        double radius = quantization != 0 ? diameter / 2 : 0.0;

        Offset[] points =
        {
            new Offset(-left, -bottom + radius), new Offset(-left, top - radius),
            new Offset(-left + radius, top),     new Offset(right - radius, top),
            new Offset(right, top - radius),     new Offset(right, -bottom + radius),
            new Offset(right - radius, -bottom), new Offset(-left + radius, -bottom),
        };

        for (int i = 0; i < points.Length; i += 2)
        {
            skyline.AddContourSegment(
                transform, Orientation.Clockwise, points[i], points[i + 1]);
        }

        if (radius == 0.0)
        {
            return;
        }

        DrulArray<double> cx = new DrulArray<double>(-left + radius, right - radius);
        DrulArray<double> cy = new DrulArray<double>(-bottom + radius, top - radius);

        foreach (Direction v in new[] { Direction.Negative, Direction.Positive })
        {
            foreach (Direction h in new[] { Direction.Negative, Direction.Positive })
            {
                Offset last = Offset.Zero;
                for (int i = 0; i <= quantization; i++)
                {
                    double angle = LinearInterpolate(i, 0, quantization, 0.0, 90.0);
                    Offset point = Offset.Directed(angle) * radius;
                    Offset corner = new Offset(
                        cx[h] + (h.Value * point.X),
                        cy[v] + (v.Value * point.Y));

                    if (i > 0)
                    {
                        skyline.AddSegment(transform, last, corner);
                    }

                    last = corner;
                }
            }
        }
    }

    /// <summary>
    /// Flattens a cubic Bézier into segments drawn with a pen.
    /// </summary>
    /// <param name="skyline">The collector.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="thickness">The pen's diameter.</param>
    /// <param name="control">The four control points.</param>
    private static void AddDrawBezierSegments(
        LazySkylinePair skyline, Transform transform, double thickness, Offset[] control)
    {
        Bezier curve = new Bezier(control);

        // The step count comes from the length of the TRANSFORMED control polygon,
        // which over-estimates the curve's own length — deliberately, since a curve is
        // never longer than its hull.
        double length = 0.0;
        Offset last = Offset.Zero;
        for (int i = 0; i < Bezier.ControlCount; i++)
        {
            Offset transformed = transform.Apply(control[i]);
            if (i > 0)
            {
                length += (transformed - last).Length;
            }

            last = transformed;
        }

        int quantization = (int)Math.Max(0.0, length / QuantizationUnit);

        Offset previous = curve[0];
        for (int i = 1; i < quantization; i++)
        {
            Offset point = curve.CurvePoint(i / (double)quantization);
            skyline.AddSegment(transform, previous, point, thickness);
            previous = point;
        }

        skyline.AddSegment(transform, previous, curve[3], thickness);
    }

    /// <summary>
    /// Turns a path's drawing commands into absolute coordinate groups — four numbers
    /// for a line, eight for a curve — the way upstream's
    /// <c>all_commands_to_absolute_and_group</c> does.
    /// <para>
    /// A malformed path warns and keeps what it had, rather than throwing: upstream
    /// takes the same view, that a bad path should not take the score down.
    /// </para>
    /// </summary>
    /// <param name="expression">The command list.</param>
    /// <returns>The groups, in drawing order.</returns>
    private static List<double[]> AllCommandsToAbsoluteAndGroup(object expression)
    {
        List<double[]> output = new List<double[]>();
        Offset start = Offset.Zero;
        Offset current = Offset.Zero;
        bool first = true;

        object expr = expression;

        while (expr is Pair pair)
        {
            object command = pair.Car;

            if (ReferenceEquals(command, MoveToSymbol)
                || (ReferenceEquals(command, RMoveToSymbol) && first))
            {
                start = new Offset(Real(Nth(expr, 1)), Real(Nth(expr, 2)));
                current = start;
                expr = Drop(expr, 3);
            }
            else if (ReferenceEquals(command, RMoveToSymbol))
            {
                start = new Offset(Real(Nth(expr, 1)), Real(Nth(expr, 2))) + current;
                current = start;
                expr = Drop(expr, 3);
            }
            else if (ReferenceEquals(command, LineToSymbol)
                     || ReferenceEquals(command, RLineToSymbol))
            {
                Offset to = new Offset(Real(Nth(expr, 1)), Real(Nth(expr, 2)));
                if (ReferenceEquals(command, RLineToSymbol))
                {
                    to += current;
                }

                output.Add(new[] { current.X, current.Y, to.X, to.Y });
                current = to;
                expr = Drop(expr, 3);
            }
            else if (ReferenceEquals(command, CurveToSymbol)
                     || ReferenceEquals(command, RCurveToSymbol))
            {
                Offset origin = ReferenceEquals(command, RCurveToSymbol) ? current : Offset.Zero;

                Offset c1 = new Offset(Real(Nth(expr, 1)), Real(Nth(expr, 2))) + origin;
                Offset c2 = new Offset(Real(Nth(expr, 3)), Real(Nth(expr, 4))) + origin;
                Offset to = new Offset(Real(Nth(expr, 5)), Real(Nth(expr, 6))) + origin;

                output.Add(new[]
                {
                    current.X, current.Y, c1.X, c1.Y, c2.X, c2.Y, to.X, to.Y,
                });
                current = to;
                expr = Drop(expr, 7);
            }
            else if (ReferenceEquals(command, ClosePathSymbol))
            {
                if (current.X != start.X || current.Y != start.Y)
                {
                    output.Add(new[] { current.X, current.Y, start.X, start.Y });
                    current = start;
                }

                expr = pair.Cdr;
            }
            else
            {
                Warn.Warning("Malformed path for path stencil.");
                return output;
            }

            first = false;
        }

        return output;
    }

    /// <summary>
    /// Draws a grouped path, one segment or curve at a time.
    /// </summary>
    /// <param name="skyline">The collector.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="thickness">The pen's diameter.</param>
    /// <param name="commands">The command list to group and draw.</param>
    private static void InternalAddPathSegments(
        LazySkylinePair skyline, Transform transform, double thickness, object commands)
    {
        foreach (double[] group in AllCommandsToAbsoluteAndGroup(commands))
        {
            if (group.Length == 4)
            {
                skyline.AddSegment(
                    transform,
                    new Offset(group[0], group[1]),
                    new Offset(group[2], group[3]),
                    thickness);
            }
            else
            {
                AddDrawBezierSegments(skyline, transform, thickness, new[]
                {
                    new Offset(group[0], group[1]),
                    new Offset(group[2], group[3]),
                    new Offset(group[4], group[5]),
                    new Offset(group[6], group[7]),
                });
            }
        }
    }

    /// <summary>
    /// <c>(path THICK COMMANDS …)</c>, with the head already stripped. Anything after
    /// the command list — cap style, join style, fill — does not change the ink's
    /// outline enough to matter here, and upstream ignores it too.
    /// </summary>
    /// <param name="skyline">The collector.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="arguments">The argument list.</param>
    private static void AddPathSegments(
        LazySkylinePair skyline, Transform transform, object arguments)
        => InternalAddPathSegments(
            skyline, transform, Real(Car(arguments)), GetPathList(Cdr(arguments)));

    /// <summary>
    /// <c>(polygon POINTS DIAMETER FILL)</c>, with the head already stripped. The
    /// points are turned into a closed path and drawn as one.
    /// </summary>
    /// <param name="skyline">The collector.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="arguments">The argument list.</param>
    private static void AddPolygonSegments(
        LazySkylinePair skyline, Transform transform, object arguments)
    {
        object coordinates = GetNumberList(Car(arguments));
        double diameter = Real(Nth(arguments, 1));

        List<object> commands = new List<object>();
        bool first = true;

        for (object s = coordinates; s is Pair pair && pair.Cdr is Pair second; s = second.Cdr)
        {
            commands.Add(first ? MoveToSymbol : LineToSymbol);
            commands.Add(pair.Car);
            commands.Add(second.Car);
            first = false;
        }

        commands.Add(ClosePathSymbol);

        InternalAddPathSegments(
            skyline, transform, diameter, Pair.List(commands.ToArray()));
    }

    /// <summary>
    /// <c>(named-glyph FONT GLYPH-NAME)</c>, with the head already stripped: traces the
    /// glyph's real outline.
    /// <para>
    /// DIVERGENCE in mechanism, recorded in PORT-COVERAGE. Upstream divides two boxes to
    /// recover the design-units-to-output-units factor, because its two sources for them
    /// — FreeType's glyph metrics and FreeType's outline bounding box — round
    /// differently. The port has ONE source for both, the charstring interpreter, so the
    /// ratio is the scale factor itself: the font's drawing size over its units per em,
    /// which is the same number the SVG backend scales the same outline by.
    /// </para>
    /// </summary>
    /// <param name="skyline">The collector.</param>
    /// <param name="transform">The transform.</param>
    /// <param name="arguments">The argument list.</param>
    private static void AddNamedGlyphSegments(
        LazySkylinePair skyline, Transform transform, object arguments)
    {
        if (!(Car(arguments) is FontMetric metric))
        {
            return;
        }

        FontMetric original = metric is ModifiedFontMetric modified
            ? modified.OriginalFont
            : metric;

        if (!(original is OpenTypeFontMetric openType) || openType.Font.Cff == null)
        {
            return;
        }

        string glyphName = AsString(Nth(arguments, 1));
        int index = openType.NameToIndex(glyphName);
        if (index == FontMetric.GlyphIndexInvalid)
        {
            return;
        }

        int unitsPerEm = openType.Font.UnitsPerEm > 0 ? openType.Font.UnitsPerEm : 1000;
        double scale = metric.FontScaling / unitsPerEm;

        Transform local = transform;
        local.Scale(scale, scale);

        openType.Font.Cff.AddOutlineToSkyline(skyline, local, index);
    }

    /// <summary>
    /// Determines whether an expression sets any text.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns><see langword="true"/> when a text node appears anywhere in it.</returns>
    private static bool ContainsText(object expression)
    {
        if (!(expression is Pair pair))
        {
            return false;
        }

        if (ReferenceEquals(pair.Car, Utf8StringSymbol))
        {
            return true;
        }

        for (object s = expression; s is Pair item; s = item.Cdr)
        {
            if (ContainsText(item.Car))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the first list of numbers found anywhere inside a nested list, which is
    /// how a polygon's coordinates are dug out of whatever wraps them.
    /// </summary>
    /// <param name="list">The list to search.</param>
    /// <returns>The list of numbers, or <see cref="Nil.Instance"/> when there is none.</returns>
    private static object GetNumberList(object list)
    {
        if (!(list is Pair pair))
        {
            return Nil.Instance;
        }

        if (Bootstrap.SchemeConvert.IsNumber(pair.Car))
        {
            return list;
        }

        object head = GetNumberList(pair.Car);
        return head is Pair ? head : GetNumberList(pair.Cdr);
    }

    /// <summary>
    /// Returns the first sub-list that starts with a path command, searching nested
    /// lists head-first.
    /// </summary>
    /// <param name="list">The list to search.</param>
    /// <returns>The command list, or <see cref="Nil.Instance"/> when there is none.</returns>
    private static object GetPathList(object list)
    {
        for (object s = list; s is Pair pair; s = pair.Cdr)
        {
            object head = pair.Car;
            if (ReferenceEquals(head, MoveToSymbol)
                || ReferenceEquals(head, RMoveToSymbol)
                || ReferenceEquals(head, LineToSymbol)
                || ReferenceEquals(head, RLineToSymbol)
                || ReferenceEquals(head, CurveToSymbol)
                || ReferenceEquals(head, RCurveToSymbol)
                || ReferenceEquals(head, ClosePathSymbol))
            {
                return s;
            }

            object found = GetPathList(head);
            if (found is Pair)
            {
                return found;
            }
        }

        return Nil.Instance;
    }

    private static Grob CommonRefpointOf(IReadOnlyList<Grob> elements, Grob grob, Axis axis)
    {
        Grob common = grob;
        foreach (Grob element in elements)
        {
            common = common.CommonRefpoint(element, axis);
        }

        return common;
    }

    private static bool IsCrossStaff(Grob grob)
        => SchemeUtilities.ToBool(grob.GetProperty(CrossStaffSymbol));

    private static double Square(double value) => value * value;

    /// <summary>
    /// Maps a value from one range onto another, as upstream's
    /// <c>linear_interpolate</c>.
    /// </summary>
    /// <param name="x">The value.</param>
    /// <param name="x1">The start of the source range.</param>
    /// <param name="x2">The end of the source range.</param>
    /// <param name="y1">The start of the target range.</param>
    /// <param name="y2">The end of the target range.</param>
    /// <returns>The mapped value.</returns>
    private static double LinearInterpolate(
        double x, double x1, double x2, double y1, double y2)
        => ((x2 - x) * y1 + (x - x1) * y2) / (x2 - x1);

    /// <summary>
    /// Reads a number, answering zero for anything that is not one — which is what
    /// upstream's <c>from_scm&lt;double&gt; (…, 0.0)</c> does.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The number, or zero.</returns>
    private static double Real(object value)
        => Bootstrap.SchemeConvert.IsNumber(value)
            ? Bootstrap.SchemeConvert.ToDouble(value, "stencil-integral")
            : 0.0;

    private static Offset ToOffset(object value)
        => value is Pair pair ? new Offset(Real(pair.Car), Real(pair.Cdr)) : Offset.Zero;

    private static string AsString(object value)
        => value is MutableString text ? text.ToString() : value as string;

    private static object Car(object list) => list is Pair pair ? pair.Car : Nil.Instance;

    private static object Cdr(object list) => list is Pair pair ? pair.Cdr : Nil.Instance;

    private static object Second(object list) => Nth(list, 1);

    private static object Third(object list) => Nth(list, 2);

    private static object Fourth(object list) => Nth(list, 3);

    private static object Nth(object list, int index)
    {
        object s = list;
        for (int i = 0; i < index; i++)
        {
            if (!(s is Pair pair))
            {
                return Nil.Instance;
            }

            s = pair.Cdr;
        }

        return Car(s);
    }

    private static object Drop(object list, int count)
    {
        object s = list;
        for (int i = 0; i < count; i++)
        {
            if (!(s is Pair pair))
            {
                return Nil.Instance;
            }

            s = pair.Cdr;
        }

        return s;
    }
}
