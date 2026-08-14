/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2009--2026 Joe Neeman <joeneeman@gmail.com>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/staff-grouper-interface.cc, lily/include/staff-grouper-interface.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Queries over a <c>StaffGrouper</c> — the spanner a nested
/// <c>Vertical_align_engraver</c> makes to collect the staves of a
/// <c>StaffGroup</c>-like context.
/// <para>
/// The hara-kiri calls both methods make upstream — <c>consider_suicide</c> before
/// measuring a staff, <c>request_suicide</c> in the pure branch — are the deliberately
/// unported staff-removal machinery (the output-pipeline note): no staff ever vanishes here, so
/// the first is skipped and the second answers "no". Recorded in PORT-COVERAGE.
/// </para>
/// </summary>
public static class StaffGrouperInterface
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol StaffGrouperSymbol = Symbol.Intern("staff-grouper");

    // Find the furthest staff in the given direction whose x-extent overlaps with
    // the given interval.

    /// <summary>
    /// Returns the outermost staff of a group, in one direction, whose horizontal
    /// extent overlaps an interval.
    /// </summary>
    /// <param name="me">The staff grouper — or a <c>VerticalAlignment</c>; see below.</param>
    /// <param name="refpoint">The grob to measure X extents against.</param>
    /// <param name="dir">UP for the first staff, DOWN for the last.</param>
    /// <param name="iv">The horizontal interval to overlap.</param>
    /// <returns>The staff, or <see langword="null"/>.</returns>
    public static Grob GetExtremalStaff(Grob me, Grob refpoint, Direction dir, Interval iv)
    {
        // N.B. This is intended to work for a VerticalAlignment grob even though
        // VerticalAlignment does not have the staff-grouper interface.  StaffGrouper
        // and VerticalAlignment grobs are both created by the
        // Vertical_align_engraver and contain elements meeting a common set of
        // criteria, yet they are not described as having a common interface.  Should
        // we treat staff grouping as a subset of vertical alignment?  Should we
        // factor out the shared subset of features into a new interface?

        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        int start = dir == Direction.Positive ? 0 : elts.Count - 1;
        int end = dir == Direction.Positive ? elts.Count : -1;
        for (int i = start; i != end; i += dir)
        {
            // Upstream calls Hara_kiri_group_spanner::consider_suicide here, so a
            // staff that turned out empty is skipped as dead. Suicide is unported;
            // every staff stays live, which is visible rather than silent.
            Interval intersection = elts[i].Extent(refpoint, Axis.X);
            intersection.Intersect(iv);
            if (elts[i].IsLive && !intersection.IsEmpty)
            {
                return elts[i];
            }
        }

        return null;
    }

    /* Checks whether the child grob is in the "interior" of this staff-grouper.
       This is the case if the next spaceable, living child after the given one
       belongs to the group.
    */

    /// <summary>
    /// Determines whether a child sits in the INTERIOR of a group: some spaceable,
    /// living child after it still belongs to the same grouper — which is what decides
    /// whether <c>staff-staff-spacing</c> or <c>staffgroup-staff-spacing</c> rules the
    /// gap below it.
    /// </summary>
    /// <param name="me">The staff grouper.</param>
    /// <param name="child">The child to test.</param>
    /// <param name="pure">Whether this is a pure lookup.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <returns><see langword="true"/> when within the group.</returns>
    public static bool MaybePureWithinGroup(Grob me, Grob child, bool pure, int start, int end)
    {
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);

        int i = -1;
        for (int j = 0; j < elts.Count; j++)
        {
            if (ReferenceEquals(elts[j], child))
            {
                i = j;
                break;
            }
        }

        if (i < 0)
        {
            return false;
        }

        for (++i; i < elts.Count; i++)
        {
            // The pure branch asks Hara_kiri_group_spanner::request_suicide whether
            // the staff vanishes over [start, end); suicide is unported, so the
            // answer is always "no" and both branches reduce to is-live.
            if (PageLayoutSpacing.IsSpaceable(elts[i]) && elts[i].IsLive)
            {
                return ReferenceEquals(me, elts[i].GetObject(StaffGrouperSymbol));
            }
        }

        // If there was no spaceable, living child after me, I don't
        // count as within the group.
        return false;
    }
}
