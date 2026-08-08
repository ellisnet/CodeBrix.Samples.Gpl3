/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/line-interface.cc, lily/include/line-interface.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.
// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port:
//   - the property-driven half landed (line, arrows, make_arrow, make_zigzag_line,
//     make_trill_line), pulled in by EPG17's demand loop: Bracket::make_bracket draws
//     every bracket edge through Line_interface::line, so a volta or tuplet bracket
//     cannot exist without it. This CLOSES the divergence PORT-COVERAGE recorded under
//     "lily/line-interface.cc arrows / make_arrow / the property-driven `line'".

/// <summary>
/// Line drawing, as stencils. Anything that draws a line — brackets, spanners,
/// glissandi, ledger lines — comes through here, and the <c>style</c> property decides
/// which shape it gets.
/// </summary>
public static class LineInterface
{
    private static readonly Symbol ArrowLengthSymbol = Symbol.Intern("arrow-length");
    private static readonly Symbol ArrowWidthSymbol = Symbol.Intern("arrow-width");
    private static readonly Symbol DashFractionSymbol = Symbol.Intern("dash-fraction");
    private static readonly Symbol DashPeriodSymbol = Symbol.Intern("dash-period");
    private static readonly Symbol DottedLineStyle = Symbol.Intern("dotted-line");
    private static readonly Symbol DashedLineStyle = Symbol.Intern("dashed-line");
    private static readonly Symbol NoneStyle = Symbol.Intern("none");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol TrillStyle = Symbol.Intern("trill");
    private static readonly Symbol ZigzagStyle = Symbol.Intern("zigzag");
    private static readonly Symbol ZigzagLengthSymbol = Symbol.Intern("zigzag-length");
    private static readonly Symbol ZigzagWidthSymbol = Symbol.Intern("zigzag-width");

    private static readonly Symbol DrawLine = Symbol.Intern("draw-line");
    private static readonly Symbol DashedLine = Symbol.Intern("dashed-line");

    /// <summary>Returns a stencil drawing a straight line between two points.</summary>
    /// <param name="thickness">The line thickness.</param>
    /// <param name="from">The start point.</param>
    /// <param name="to">The end point.</param>
    /// <returns>The line stencil.</returns>
    public static Stencil MakeLine(double thickness, Offset from, Offset to)
    {
        object at = Pair.List(DrawLine, thickness, from.X, from.Y, to.X, to.Y);

        Box box = default;
        box.AddPoint(from);
        box.AddPoint(to);

        Interval x = box.X;
        x.Widen(thickness / 2);
        box.X = x;

        Interval y = box.Y;
        y.Widen(thickness / 2);
        box.Y = y;

        return new Stencil(box, at);
    }

    /// <summary>Returns a stencil drawing a dashed straight line between two points.</summary>
    /// <param name="thickness">The line thickness.</param>
    /// <param name="from">The start point.</param>
    /// <param name="to">The end point.</param>
    /// <param name="dashPeriod">The length of one dash-plus-gap.</param>
    /// <param name="dashFraction">The fraction of a period that is drawn.</param>
    /// <returns>The dashed line stencil.</returns>
    public static Stencil MakeDashedLine(
        double thickness,
        Offset from,
        Offset to,
        double dashPeriod,
        double dashFraction)
    {
        dashFraction = System.Math.Min(System.Math.Max(dashFraction, 0.0), 1.0);
        double on = dashFraction * dashPeriod;
        double off = System.Math.Max(0.0, dashPeriod - on);

        Offset delta = to - from;
        object at = Pair.List(DashedLine, thickness, on, off, delta.X, delta.Y, 0.0);

        Box box = default;
        box.AddPoint(Offset.Zero);
        box.AddPoint(delta);

        Interval x = box.X;
        x.Widen(thickness / 2);
        box.X = x;

        Interval y = box.Y;
        y.Widen(thickness / 2);
        box.Y = y;

        Stencil stencil = new Stencil(box, at);
        stencil.Translate(from);
        return stencil;
    }

    /// <summary>Returns a stencil drawing an arrowhead at the end of a segment.</summary>
    /// <param name="begin">The start of the segment, which fixes the direction.</param>
    /// <param name="end">The point of the arrow.</param>
    /// <param name="thickness">The blot diameter of the polygon.</param>
    /// <param name="length">How far back from the point the arrow reaches.</param>
    /// <param name="width">Half the arrow's width at its base.</param>
    /// <returns>The arrowhead stencil.</returns>
    public static Stencil MakeArrow(
        Offset begin, Offset end, double thickness, double length, double width)
    {
        Offset direction = (end - begin).Direction();
        List<Offset> points = new List<Offset>
        {
            new Offset(0, 0),
            new Offset(-length, width),
            new Offset(-length, -width),
        };

        for (int i = 0; i < points.Count; i++)
        {
            points[i] = Offset.ComplexMultiply(points[i], direction) + end;
        }

        return Lookup.RoundPolygon(points, thickness, -1.0);
    }

    /// <summary>Returns a stencil holding whichever arrowheads a grob asks for.</summary>
    /// <param name="me">The grob, read for thickness and arrow dimensions.</param>
    /// <param name="from">The start of the line.</param>
    /// <param name="to">The end of the line.</param>
    /// <param name="fromArrow">Whether to draw an arrowhead at the start.</param>
    /// <param name="toArrow">Whether to draw an arrowhead at the end.</param>
    /// <returns>The arrowheads, or an empty stencil when neither is wanted.</returns>
    public static Stencil Arrows(Grob me, Offset from, Offset to, bool fromArrow, bool toArrow)
    {
        Stencil result = new Stencil();
        if (fromArrow || toArrow)
        {
            double thickness = StaffSymbolReferencer.LineThickness(me)
                * ReadDouble(me.GetProperty(ThicknessSymbol), 1);
            double staffSpace = StaffSymbolReferencer.StaffSpace(me);

            double length = ReadDouble(me.GetProperty(ArrowLengthSymbol), 1.3 * staffSpace);
            double width = ReadDouble(me.GetProperty(ArrowWidthSymbol), 0.5 * staffSpace);

            if (toArrow)
            {
                result.AddStencil(MakeArrow(from, to, thickness, length, width));
            }

            if (fromArrow)
            {
                result.AddStencil(MakeArrow(to, from, thickness, length, width));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the line a grob's <c>style</c> property asks for: plain, dashed, dotted,
    /// zigzag, trill, or nothing at all.
    /// </summary>
    /// <param name="me">The grob, read for style and thickness.</param>
    /// <param name="from">The start of the line.</param>
    /// <param name="to">The end of the line.</param>
    /// <returns>The line stencil.</returns>
    public static Stencil Line(Grob me, Offset from, Offset to)
    {
        double thickness = StaffSymbolReferencer.LineThickness(me)
            * ReadDouble(me.GetProperty(ThicknessSymbol), 1);

        object type = me.GetProperty(StyleSymbol);
        if (ReferenceEquals(type, ZigzagStyle))
        {
            return MakeZigzagLine(me, from, to);
        }

        if (ReferenceEquals(type, TrillStyle))
        {
            return MakeTrillLine(me, from, to);
        }

        if (ReferenceEquals(type, NoneStyle))
        {
            return new Stencil();
        }

        if (ReferenceEquals(type, DashedLineStyle) || ReferenceEquals(type, DottedLineStyle))
        {
            double fraction = ReferenceEquals(type, DottedLineStyle)
                ? 0.0
                : ReadDouble(me.GetProperty(DashFractionSymbol), 0.4);

            fraction = Math.Min(Math.Max(fraction, 0.0), 1.0);
            double period = StaffSymbolReferencer.StaffSpace(me)
                * ReadDouble(me.GetProperty(DashPeriodSymbol), 1.0);

            if (period <= 0)
            {
                return new Stencil();
            }

            double length = (to - from).Length;

            // Dashed lines should begin and end with a dash. Therefore, there will be one
            // more dash than complete dash + whitespace units (full periods).
            int fullPeriodCount = (int)Math.Round(
                (length - (period * fraction)) / period, MidpointRounding.ToEven);

            fullPeriodCount = Math.Max(0, fullPeriodCount);
            if (fullPeriodCount > 0)
            {
                period = length / (fraction + fullPeriodCount);
            }

            return MakeDashedLine(thickness, from, to, period, fraction);
        }

        return MakeLine(thickness, from, to);
    }

    /// <summary>Returns a zigzag line between two points.</summary>
    /// <param name="me">The grob, read for thickness and zigzag dimensions.</param>
    /// <param name="from">The start of the line.</param>
    /// <param name="to">The end of the line.</param>
    /// <returns>The zigzag stencil.</returns>
    public static Stencil MakeZigzagLine(Grob me, Offset from, Offset to)
    {
        Offset dz = to - from;

        double thickness = StaffSymbolReferencer.LineThickness(me);
        thickness *= ReadDouble(me.GetProperty(ThicknessSymbol), 1.0);

        double staffSpace = StaffSymbolReferencer.StaffSpace(me);

        double w = ReadDouble(me.GetProperty(ZigzagWidthSymbol), 1) * staffSpace;
        int count = (int)Math.Ceiling(dz.Length / w);
        if (count <= 0)
        {
            return new Stencil();
        }

        w = dz.Length / count;

        double l = ReadDouble(me.GetProperty(ZigzagLengthSymbol), 1) * w;
        double h = l > w / 2 ? Math.Sqrt((l * l) - (w * w / 4)) : 0;

        Offset rotationFactor = dz.Direction();

        Offset[] points =
        {
            new Offset(0, -h / 2),
            new Offset(w / 2, h / 2),
            new Offset(w, -h / 2),
        };

        for (int i = 0; i < 3; i++)
        {
            points[i] = Offset.ComplexMultiply(points[i], rotationFactor);
        }

        Stencil squiggle = MakeLine(thickness, points[0], points[1]);
        squiggle.AddStencil(MakeLine(thickness, points[1], points[2]));

        Stencil total = new Stencil();
        for (int i = 0; i < count; i++)
        {
            Stencil moved = squiggle;
            moved.Translate(from + Offset.ComplexMultiply(new Offset(i * w, 0), rotationFactor));
            total.AddStencil(moved);
        }

        return total;
    }

    /// <summary>Returns a line of trill elements between two points.</summary>
    /// <param name="me">The grob, whose default font supplies the element glyph.</param>
    /// <param name="from">The start of the line.</param>
    /// <param name="to">The end of the line.</param>
    /// <returns>The trill-line stencil.</returns>
    public static Stencil MakeTrillLine(Grob me, Offset from, Offset to)
    {
        Offset dz = to - from;
        double dzx = dz.X;
        double dzy = dz.Y;
        foreach (Axis axis in new[] { Axis.X, Axis.Y })
        {
            double value = axis == Axis.X ? dzx : dzy;
            if (double.IsInfinity(value) || double.IsNaN(value) || Math.Abs(value) > 1e6)
            {
                Warn.ProgrammingError(
                    "Improbable offset for stencil: " + value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    + " staff space\nSetting to zero.");

                if (axis == Axis.X)
                {
                    dzx = 0.0;
                }
                else
                {
                    dzy = 0.0;
                }
            }
        }

        dz = new Offset(dzx, dzy);

        FontMetric font = FontInterface.GetDefaultFont(me);
        Stencil element = font.FindByName("scripts.trill_element");
        element.AlignTo(Axis.Y, 0.0);
        double elementLength = element.Extent(Axis.X).Length;

        // Get the real length of the trill element, so as not to exceed the allotted
        // length for the line. The element sticks out of its bounding box so that two
        // elements blend when concatenated.
        SkylinePair pair = StencilIntegral.SkylinesFromStencil(element, Nil.Instance, Axis.Y);
        Interval trueExtent = new Interval(
            pair[Direction.Negative].MaxHeight(), pair[Direction.Positive].MaxHeight());

        double trueLength = trueExtent.Length;
        if (trueLength <= 0)
        {
            Warn.ProgrammingError("can't find scripts.trill_element");
            return element;
        }

        // Always have at least one trill element, even if the space allotted technically
        // doesn't allow it.
        Stencil line = element;
        line.TranslateAxis(-trueExtent.Left, Axis.X);
        double totalLength = trueLength;
        double delta = dz.Length - totalLength;
        if (delta > 0 && elementLength > 0)
        {
            // First trill element takes trueLength, each further element only adds
            // elementLength because of the overlap.
            int extra = (int)(delta / elementLength);
            for (int i = 0; i < extra; i++)
            {
                line.AddAtEdge(Axis.X, Direction.Positive, element, 0);
            }

            totalLength += extra * elementLength;
        }

        Box box = line.ExtentBox;
        Box newBox = new Box(new Interval(0, totalLength), box[Axis.Y]);
        Stencil newLine = new Stencil(newBox, line.Expression);

        newLine.Rotate(dz.AngleDegrees(), new Offset(-1, 0));
        newLine.Translate(from);

        return newLine;
    }

    private static double ReadDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "line-interface")
            : fallback;
}
