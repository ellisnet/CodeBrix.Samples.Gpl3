/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/break-substitution.cc (the Direction criterion only);

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// Re-points a PREBROKEN item's links at the pieces that belong with it.
/// <para>
/// A breakable item is cloned twice — one copy for the end of a line, one for the start
/// of the next — and each clone starts with an EMPTY object alist, exactly as upstream's
/// copy constructor leaves it. Filling it is a separate pass: every link in the
/// original is copied across, with each linked item replaced by ITS clone for the same
/// side of the break.
/// </para>
/// <para>
/// SCOPE, and why this file exists in EPG22. Upstream's <c>break-substitution.cc</c>
/// has two criteria: a <c>Direction</c> (the PREBREAK pass, run before line breaking)
/// and a <c>System</c> (the LINE-BREAKING pass). Only the Direction half is here.
/// EPG22 pulled it forward because the prebreak pass is not optional and never was:
/// <c>ly:span-bar::before-line-breaking</c> reads a SpanBar's <c>elements</c> with no
/// default, and a clone with an empty object alist answers <c>'()</c> and throws — 87
/// files in the 2026-08-07 sweep. The System half stays EPG15's, with the rest of line
/// breaking.
/// </para>
/// </summary>
public static class BreakSubstitution
{
    /// <summary>
    /// Returns the piece of a grob that belongs on one side of a break.
    /// <para>Upstream: <c>substitute_grob (Direction, Grob *)</c>.</para>
    /// </summary>
    /// <param name="d">The side of the break.</param>
    /// <param name="sc">The grob to substitute.</param>
    /// <returns>The grob's prebroken piece, the grob itself, or null when it has none.</returns>
    public static Grob SubstituteGrob(Direction d, Grob sc)
    {
        if (sc is Item item && item.BreakStatusDirection() != d)
        {
            return item.FindPrebrokenPiece(d);
        }

        return sc;
    }

    /// <summary>
    /// Substitutes through an arbitrary value: a grob, a grob array, a vector, a pair,
    /// or anything else (which passes through).
    /// <para>Upstream: <c>do_break_substitution (Direction, SCM)</c>. A grob with no
    /// piece on this side answers <see cref="Unspecified"/>, which is the marker
    /// <see cref="SubstituteObjectAlist"/> uses to DROP the entry rather than store a
    /// null.</para>
    /// </summary>
    /// <param name="d">The side of the break.</param>
    /// <param name="src">The value to substitute through.</param>
    /// <returns>The substituted value.</returns>
    public static object DoBreakSubstitution(Direction d, object src)
    {
        if (src is Grob og)
        {
            Grob g = SubstituteGrob(d, og);
            return g ?? (object)Unspecified.Instance;
        }

        if (src is GrobArray ga)
        {
            // The new array is ordered iff the original is. That way, when doing the
            // second break substitution (with systems), we'll also use the optimization
            // available for unordered arrays for arrays created by the first
            // substitution (with directions).
            GrobArray newArray = new GrobArray { IsOrdered = ga.IsOrdered };
            foreach (Grob member in ga)
            {
                Grob g = SubstituteGrob(d, member);
                if (g != null)
                {
                    newArray.Add(g);
                }
            }

            return newArray;
        }

        if (src is object[] vector)
        {
            object[] nv = new object[vector.Length];
            for (int i = 0; i < vector.Length; i++)
            {
                nv[i] = DoBreakSubstitution(d, vector[i]);
            }

            return nv;
        }

        if (src is Pair)
        {
            /* If it's a pair, src could be just any kind of nested data structure.
               However, typical Scheme patterns (lists) have potentially large data in
               the cdr and not the car.  Thus we recurse in the car and keep stack
               depth constant for the cdr (think of it as tail recursion). */
            Pair head = null;
            Pair tail = null;
            object cursor = src;
            do
            {
                Pair pair = (Pair)cursor;
                Pair cell = new Pair(DoBreakSubstitution(d, pair.Car), Nil.Instance);
                if (tail == null)
                {
                    head = cell;
                }
                else
                {
                    tail.Cdr = cell;
                }

                tail = cell;
                cursor = pair.Cdr;
            }
            while (cursor is Pair);

            tail.Cdr = DoBreakSubstitution(d, cursor);
            return head;
        }

        return src;
    }

    /// <summary>
    /// Builds a prebroken piece's object alist from its original's.
    /// <para>Upstream: <c>substitute_object_alist (Crit, SCM alist, SCM *dest)</c>.</para>
    /// </summary>
    /// <param name="d">The side of the break.</param>
    /// <param name="alist">The original's object alist.</param>
    /// <returns>The substituted alist.</returns>
    public static object SubstituteObjectAlist(Direction d, object alist)
    {
        Pair head = null;
        Pair tail = null;

        for (object s = alist; s is Pair pair; s = pair.Cdr)
        {
            if (!(pair.Car is Pair entry))
            {
                continue;
            }

            object val = DoBreakSubstitution(d, entry.Cdr);

            // Don't even set the property if there is no equivalent of the grob
            // satisfying the criterion. This is legacy, but for now the choice is to
            // not risk breakage.
            if (val is Unspecified)
            {
                continue;
            }

            Pair cell = new Pair(new Pair(entry.Car, val), Nil.Instance);
            if (tail == null)
            {
                head = cell;
            }
            else
            {
                tail.Cdr = cell;
            }

            tail = cell;
        }

        return (object)head ?? Nil.Instance;
    }
}
