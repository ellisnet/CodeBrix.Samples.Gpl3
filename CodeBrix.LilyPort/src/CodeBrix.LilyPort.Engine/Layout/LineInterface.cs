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

using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/line-interface.cc, lily/include/line-interface.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// Line drawing, as stencils.
/// <para>
/// Only the grob-independent constructors are ported so far; the property-driven
/// entry points (<c>line</c>, <c>arrows</c>, <c>make_arrow</c>) read grob properties
/// and arrive with the grob layer. See PORT-COVERAGE.
/// </para>
/// </summary>
public static class LineInterface
{
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
}
