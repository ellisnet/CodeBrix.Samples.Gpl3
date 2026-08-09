/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2006--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/figured-bass-continuation.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - upstream DECLARES Figured_bass_continuation::print and never DEFINES it anywhere in
//     the tree, so it is NOT an entry point and no binding is made for it. The stencil
//     comes from Scheme: define-grobs.scm names `figured-bass-continuation::print`, with
//     no `ly:` prefix, which output-lib.scm defines. Same shape as the
//     `Slur::vertical_skylines` declaration EPG12 recorded.

/// <summary>Simple extender line between bounds.</summary>
public static class FiguredBassContinuation
{
    private static readonly Symbol FiguresSymbol = Symbol.Intern("figures");

    /// <summary>
    /// The <c>Y-offset</c> callback: centres the extender on the figures it spans.
    /// </summary>
    /// <param name="me">The continuation line.</param>
    /// <returns>The vertical offset.</returns>
    public static object CenterOnFigures(Grob me)
    {
        IReadOnlyList<Grob> figures = PointerGroupInterface.ExtractGrobSet(me, FiguresSymbol);
        if (figures.Count == 0)
        {
            return 0.0;
        }

        Grob common = AxisGroupInterface.CommonRefpointOfArray(figures, me, Axis.Y);

        Interval ext = AxisGroupInterface.RelativeGroupExtentOf(figures, common, Axis.Y);
        if (ext.IsEmpty)
        {
            return 0.0;
        }

        return ext.Center - me.RelativeCoordinate(common, Axis.Y);
    }
}
