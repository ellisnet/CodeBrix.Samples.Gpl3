/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Flower; //was previously: flower/libc-extension.cc, flower/include/libc-extension.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - EPG10 wrote round_halfway_up as a private static inside Objects/BeamQuanting.cs,
//     because beams were its only caller. EPG11 and EPG12 add three more callers
//     (Tie_formatting_problem, Slur_score_state and avoid_staff_line), so it moves to
//     the file upstream actually declares it in rather than being re-derived per group.
//     BeamQuanting now forwards here; the arithmetic is unchanged.

/// <summary>
/// The one function the port needs out of upstream's C-library shims.
/// </summary>
public static class LibcExtension
{
    /// <summary>
    /// Rounds to the nearest integer, breaking a tie UPWARD rather than away from zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <c>floor (x - 0.5) + 1.0</c>, not <c>floor (x + 0.5)</c> and not
    /// <see cref="Math.Round(double)"/>. The three disagree on exact .5 boundaries, which
    /// is precisely where staff positions land: <c>RoundHalfwayUp(-7.5)</c> is
    /// <c>-7.0</c>, where <see cref="Math.Round(double)"/> answers <c>-8</c> and C's
    /// <c>round</c> answers <c>-8</c> as well.
    /// </para>
    /// <para>
    /// Upstream's own header says "DO NOT USE in new code" — it is kept because the
    /// scorers' tuning depends on the tie-breaking direction.
    /// </para>
    /// </remarks>
    /// <param name="x">The value to round.</param>
    /// <returns>The rounded value.</returns>
    public static double RoundHalfwayUp(double x) => Math.Floor(x - 0.5) + 1.0;
}
