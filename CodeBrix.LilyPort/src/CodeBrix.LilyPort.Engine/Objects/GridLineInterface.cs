/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/grid-line-interface.cc, lily/include/grid-line-interface.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// A line that is spanned between grid points.
/// </summary>
public static class GridLineInterface
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");

    /// <summary>
    /// The <c>stencil</c> callback: a vertical line uniting the extents of the grid
    /// points, or suicide when they have none.
    /// </summary>
    /// <param name="grob">The grid line.</param>
    /// <returns>The stencil, or the empty list after suicide.</returns>
    public static object Print(Grob grob)
    {
        IReadOnlyList<Grob> elements
            = PointerGroupInterface.ExtractGrobSet(grob, ElementsSymbol);

        /* compute common refpoint of elements */
        Grob refp = AxisGroupInterface.CommonRefpointOfArray(elements, grob, Axis.Y);
        Interval iv = Interval.Empty;

        foreach (Grob point in elements)
        {
            iv.Unite(point.Extent(refp, Axis.Y));
        }

        if (iv.IsEmpty)
        {
            grob.Suicide();
            return Nil.Instance;
        }

        double staffline = grob.Layout != null
            ? grob.Layout.GetDimension(LineThicknessSymbol)
            : 0.0;
        double thick = Epg8Support.ToDouble(grob.GetProperty(ThicknessSymbol), 1.0)
            * staffline;

        iv.Translate(-grob.RelativeCoordinate(refp, Axis.Y));
        Stencil st = Lookup.FilledBox(new Box(new Interval(0, thick), iv));

        return st;
    }

    /// <summary>The <c>X-extent</c> callback: the line's thickness.</summary>
    /// <param name="grob">The grid line.</param>
    /// <returns>The horizontal extent.</returns>
    public static Interval Width(Grob grob)
    {
        double staffline = grob.Layout != null
            ? grob.Layout.GetDimension(LineThicknessSymbol)
            : 0.0;
        double thick = Epg8Support.ToDouble(grob.GetProperty(ThicknessSymbol), 1.0)
            * staffline;

        return new Interval(0, thick);
    }

    /// <summary>Attaches a grid point to a grid line.</summary>
    /// <param name="grob">The grid line.</param>
    /// <param name="point">The grid point to add.</param>
    public static void AddGridPoint(Grob grob, Grob point)
        => PointerGroupInterface.AddGrob(grob, ElementsSymbol, point);
}
