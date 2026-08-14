/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/spaceable-grob.cc, lily/include/spaceable-grob.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A layout object that takes part in the spacing problem.
/// <para>
/// Springs and rods are stored ON the left-hand column of each pair, as alists keyed
/// by the column they reach to. The spacing solver then walks the column list once
/// and reads them off, which is why both are recorded from the left column's side.
/// </para>
/// </summary>
public static class SpaceableGrob
{
    private static readonly Symbol MinimumDistances = Symbol.Intern("minimum-distances");
    private static readonly Symbol IdealDistances = Symbol.Intern("ideal-distances");

    /// <summary>Returns the rods recorded on a column, as an alist keyed by column.</summary>
    /// <param name="grob">The column.</param>
    /// <returns>The alist.</returns>
    public static object GetMinimumDistances(Grob grob) => grob.GetObject(MinimumDistances);

    /// <summary>Returns the springs recorded on a column, as an alist of (spring . column).</summary>
    /// <param name="grob">The column.</param>
    /// <returns>The alist.</returns>
    public static object GetIdealDistances(Grob grob) => grob.GetObject(IdealDistances);

    /// <summary>
    /// Records a hard minimum distance from one column to another.
    /// <para>
    /// An existing rod to the same column is RAISED rather than replaced, because two
    /// independent reasons for a minimum distance both have to be satisfied.
    /// </para>
    /// </summary>
    /// <param name="me">The left column.</param>
    /// <param name="other">The right column.</param>
    /// <param name="distance">The minimum distance. Negative distances are ignored.</param>
    public static void AddRod(PaperColumn me, PaperColumn other, double distance)
    {
        if (distance < 0)
        {
            return;
        }

        if (double.IsInfinity(distance))
        {
            Warn.ProgrammingError("infinite rod");
        }

        object minimums = GetMinimumDistances(me);
        object cursor = minimums;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && ReferenceEquals(entry.Car, other))
            {
                double existing = entry.Cdr is double value ? value : 0.0;
                entry.Cdr = Math.Max(existing, distance);
                return;
            }

            cursor = pair.Cdr;
        }

        if (other.Rank < me.Rank)
        {
            Warn.ProgrammingError("Adding reverse rod");
        }

        me.SetObject(MinimumDistances, new Pair(new Pair(other, distance), minimums ?? Nil.Instance));
    }

    /// <summary>Records the spring between one column and the next.</summary>
    /// <param name="me">The left column.</param>
    /// <param name="other">The right column.</param>
    /// <param name="spring">The spring.</param>
    public static void AddSpring(Grob me, Grob other, Spring spring)
    {
        object ideal = GetIdealDistances(me);
        me.SetObject(IdealDistances, new Pair(new Pair(spring, other), ideal ?? Nil.Instance));
    }

    /// <summary>
    /// Returns the spring between a column and a given next column.
    /// </summary>
    /// <param name="column">The left column.</param>
    /// <param name="nextColumn">The right column.</param>
    /// <returns>The spring, or a default spring when none was recorded.</returns>
    public static Spring GetSpring(PaperColumn column, Grob nextColumn)
    {
        object cursor = GetIdealDistances(column);
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && ReferenceEquals(entry.Cdr, nextColumn) && entry.Car is Spring spring)
            {
                return spring;
            }

            cursor = pair.Cdr;
        }

        Warn.ProgrammingError(
            "No spring between column " + column.Rank + " and next one");
        return Spring.Default;
    }
}
