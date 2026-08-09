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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/break-substitution.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.
// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - EPG15 completed the file. EPG22 had carried the Direction criterion alone; the
//     SYSTEM criterion, which is what line breaking actually runs, is now here beside it.
//   - The two criteria are written as readonly structs behind one interface so the walk
//     itself exists ONCE, which is what upstream's template does. See ICriterion.
//   - Spanner::fast_substitute_grob_array and Spanner::substitute_one_mutable_property
//     are DEFINED in this upstream file but are Spanner methods; they live in Spanner.cs,
//     where the rest of Spanner is, and this note is here so a reader comparing the two
//     files does not take their absence for an omission.

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
/// TWO PASSES, and the difference between them is the whole file. Breaking breakable
/// ITEMS happens BEFORE line breaking, because the broken pieces are what pure
/// calculations and the line breaker itself measure; there, the criterion for finding
/// the right piece is having the same break DIRECTION. Breaking SPANNERS happens after
/// a configuration has been chosen; there, the criterion is living on the same SYSTEM.
/// </para>
/// <para>
/// EPG22 pulled the Direction half forward on 2026-08-07 because the prebreak pass is
/// not optional and never was: <c>ly:span-bar::before-line-breaking</c> reads a SpanBar's
/// <c>elements</c> with no default, and a clone with an empty object alist answers
/// <c>'()</c> and throws — 87 files in that day's sweep. EPG15 added the System half
/// (2026-08-08) with the rest of line breaking.
/// </para>
/// </summary>
public static class BreakSubstitution
{
    /// <summary>
    /// What a substitution pass matches a grob against.
    /// <para>
    /// Upstream writes <c>substitute_grob</c> and <c>do_break_substitution</c> as
    /// TEMPLATES with two explicit specializations, <c>Direction</c> and
    /// <c>System *</c>. This interface plus the two <see langword="readonly"/>
    /// <see langword="struct"/> implementations below is the same shape: the walk is
    /// written ONCE and instantiated twice, and a struct type argument lets the JIT
    /// specialize each instantiation the way the C++ compiler does. Writing the walk
    /// twice would be the easy translation and the wrong one — it is the top function in
    /// upstream's own profile, and two copies drift.
    /// </para>
    /// </summary>
    private interface ICriterion
    {
        /// <summary>Returns the piece of a grob this criterion selects.</summary>
        /// <param name="sc">The grob to substitute.</param>
        /// <returns>The selected piece, or <see langword="null"/> when there is none.</returns>
        Grob Substitute(Grob sc);
    }

    /// <summary>
    /// The PREBREAK criterion: match a grob's break direction.
    /// <para>Upstream: <c>substitute_grob (Direction, Grob *)</c>.</para>
    /// </summary>
    private readonly struct DirectionCriterion : ICriterion
    {
        private readonly Direction _d;

        /// <summary>Initializes the criterion for one side of a break.</summary>
        /// <param name="d">The side of the break.</param>
        public DirectionCriterion(Direction d) => _d = d;

        /// <summary>Returns the grob's prebroken piece for this side.</summary>
        /// <param name="sc">The grob to substitute.</param>
        /// <returns>The piece, the grob itself, or <see langword="null"/>.</returns>
        public Grob Substitute(Grob sc)
        {
            if (sc is Item item && item.BreakStatusDirection() != _d)
            {
                return item.FindPrebrokenPiece(_d);
            }

            return sc;
        }
    }

    /// <summary>
    /// The LINE-BREAKING criterion: match the system a grob ended up on.
    /// <para>Upstream: <c>substitute_grob (System *, Grob *)</c>.</para>
    /// </summary>
    private readonly struct SystemCriterion : ICriterion
    {
        private readonly SystemGrob _line;

        /// <summary>Initializes the criterion for one system.</summary>
        /// <param name="line">The system being assembled.</param>
        public SystemCriterion(SystemGrob line) => _line = line;

        /// <summary>Returns the grob's piece living on this system.</summary>
        /// <param name="sc">The grob to substitute.</param>
        /// <returns>The piece, or <see langword="null"/> when it does not belong here.</returns>
        public Grob Substitute(Grob sc)
        {
            // Note and FIXME carried from upstream: sc.GetSystem() may be null.
            if (!ReferenceEquals(sc.GetSystem(), _line))
            {
                sc = sc.FindBrokenPiece(_line);
            }

            // This grob has no broken piece for this system.
            if (sc == null)
            {
                return null;
            }

            return sc.HasInAncestry(_line, Axis.X) && sc.HasInAncestry(_line, Axis.Y)
                ? sc
                : null;
        }
    }

    /// <summary>
    /// Returns the piece of a grob that belongs on one side of a break.
    /// <para>Upstream: <c>substitute_grob (Direction, Grob *)</c>.</para>
    /// </summary>
    /// <param name="d">The side of the break.</param>
    /// <param name="sc">The grob to substitute.</param>
    /// <returns>The grob's prebroken piece, the grob itself, or null when it has none.</returns>
    public static Grob SubstituteGrob(Direction d, Grob sc)
        => new DirectionCriterion(d).Substitute(sc);

    /// <summary>
    /// Returns the piece of a grob that lives on a given system.
    /// <para>Upstream: <c>substitute_grob (System *, Grob *)</c>.</para>
    /// </summary>
    /// <param name="line">The system being assembled.</param>
    /// <param name="sc">The grob to substitute.</param>
    /// <returns>The piece, or <see langword="null"/> when it does not belong there.</returns>
    public static Grob SubstituteGrob(SystemGrob line, Grob sc)
        => new SystemCriterion(line).Substitute(sc);

    /// <summary>
    /// Substitutes through an arbitrary value: a grob, a grob array, a vector, a pair,
    /// or anything else (which passes through).
    /// <para>Upstream: <c>do_break_substitution (Direction, SCM)</c>. A grob with no
    /// piece on this side answers <see cref="Unspecified"/>, which is the marker
    /// <see cref="SubstituteObjectAlist(Direction, object)"/> uses to DROP the entry
    /// rather than store a null.</para>
    /// </summary>
    /// <param name="d">The side of the break.</param>
    /// <param name="src">The value to substitute through.</param>
    /// <returns>The substituted value.</returns>
    public static object DoBreakSubstitution(Direction d, object src)
        => DoBreakSubstitution(new DirectionCriterion(d), src);

    /// <summary>
    /// Substitutes through an arbitrary value for one SYSTEM, rather than for one side
    /// of a break.
    /// <para>Upstream: <c>do_break_substitution (System *, SCM)</c>.</para>
    /// </summary>
    /// <param name="line">The system being assembled.</param>
    /// <param name="src">The value to substitute through.</param>
    /// <returns>The substituted value.</returns>
    public static object DoBreakSubstitution(SystemGrob line, object src)
        => DoBreakSubstitution(new SystemCriterion(line), src);

    private static object DoBreakSubstitution<TCrit>(TCrit d, object src)
        where TCrit : struct, ICriterion
    {
        if (src is Grob og)
        {
            Grob g = d.Substitute(og);
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
                Grob g = d.Substitute(member);
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
        => SubstituteObjectAlist(new DirectionCriterion(d), alist);

    /// <summary>
    /// Builds a broken piece's object alist from its original's, for one SYSTEM.
    /// <para>Upstream: <c>substitute_object_alist (System *, SCM alist, SCM *dest)</c>.</para>
    /// </summary>
    /// <param name="line">The system being assembled.</param>
    /// <param name="alist">The original's object alist.</param>
    /// <returns>The substituted alist.</returns>
    public static object SubstituteObjectAlist(SystemGrob line, object alist)
        => SubstituteObjectAlist(new SystemCriterion(line), alist);

    private static object SubstituteObjectAlist<TCrit>(TCrit d, object alist)
        where TCrit : struct, ICriterion
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
