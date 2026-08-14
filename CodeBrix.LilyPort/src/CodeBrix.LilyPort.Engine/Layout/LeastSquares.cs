/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/least-squares.cc, lily/include/least-squares.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the two out-parameters replace upstream's Real* out-pointers
//   - the degenerate branch keeps upstream's programming_error and its wording,
//     because beam.cc relies on the zero-slope/mean-offset answer it leaves behind

/// <summary>
/// Least squares minimisation in 2 variables — the straight line through a set of
/// points that beams fit to their stems.
/// </summary>
public static class LeastSquares
{
    /// <summary>
    /// Fits <c>y = coef * x + offset</c> to the given points by least squares.
    /// </summary>
    /// <param name="coef">Receives the slope.</param>
    /// <param name="offset">Receives the intercept.</param>
    /// <param name="input">The points to fit.</param>
    public static void MinimiseLeastSquares(
        out double coef,
        out double offset,
        IReadOnlyList<Offset> input)
    {
        double sx = 0.0;
        double sy = 0.0;
        double sqx = 0.0;
        double sxy = 0.0;

        foreach (Offset point in input)
        {
            double x = point[Axis.X];
            double y = point[Axis.Y];
            sx += x;
            sy += y;
            sqx += x * x;
            sxy += x * y;
        }

        double count = input.Count;

        coef = 0.0;
        offset = 0.0;

        double den = (count * sqx) - (sx * sx);
        if (count == 0.0 || den == 0.0)
        {
            Warn.ProgrammingError("minimise_least_squares ():  Nothing to minimise\n"
                                  + "This means that vertical spacing is triggered\n"
                                  + "before line breaking");
            coef = 0.0;
            offset = count != 0.0 ? sy / count : 0.0;
        }
        else
        {
            coef = ((count * sxy) - (sx * sy)) / den;
            offset = (sy - (coef * sx)) / count;
        }
    }
}
