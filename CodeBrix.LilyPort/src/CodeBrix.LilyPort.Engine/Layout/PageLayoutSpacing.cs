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

using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/page-layout-problem.cc (is_spaceable, read_spacing_spec, get_spacing_spec, get_fixed_spacing and add_stretchability only);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The staff-to-staff spacing-specification readers of
/// <c>lily/page-layout-problem.cc</c>, pulled forward ahead of the page-layout group
/// because <c>Align_interface::internal_get_minimum_translations</c> reads
/// every pair of adjacent staves through them. The <c>Page_layout_problem</c> solver
/// itself, which consumes the same specs to spread a page, stayed with its owning
/// file; the ledger row records the split.
/// </summary>
public static class PageLayoutSpacing
{
    /// <summary>
    /// The stretchability planted on a spec between unrelated staves, so that it gives
    /// way to every real constraint.
    /// </summary>
    public const double LargeStretch = 10e5;

    /// <summary>The stretchability of the no-spec-at-all spring.</summary>
    public const double HugeStretch = 10e7;

    private static readonly Symbol StretchabilitySymbol = Symbol.Intern("stretchability");
    private static readonly Symbol StaffAffinitySymbol = Symbol.Intern("staff-affinity");
    private static readonly Symbol StaffStaffSpacingSymbol = Symbol.Intern("staff-staff-spacing");
    private static readonly Symbol NonstaffRelatedstaffSymbol
        = Symbol.Intern("nonstaff-relatedstaff-spacing");

    private static readonly Symbol NonstaffUnrelatedstaffSymbol
        = Symbol.Intern("nonstaff-unrelatedstaff-spacing");

    private static readonly Symbol NonstaffNonstaffSymbol
        = Symbol.Intern("nonstaff-nonstaff-spacing");

    private static readonly Symbol LineBreakSystemDetailsSymbol
        = Symbol.Intern("line-break-system-details");

    private static readonly Symbol AlignmentDistancesSymbol
        = Symbol.Intern("alignment-distances");

    private static bool _affinityWarned;

    /// <summary>
    /// Determines whether a grob is a SPACEABLE staff: one with no
    /// <c>staff-affinity</c>, so it holds its own place rather than clinging to a
    /// neighbour the way lyrics do.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns><see langword="true"/> when spaceable.</returns>
    public static bool IsSpaceable(Grob grob)
        => !SchemeConvert.IsNumber(grob.GetProperty(StaffAffinitySymbol));

    /// <summary>Reads one number out of a spacing-spec alist.</summary>
    /// <param name="spec">The spec alist.</param>
    /// <param name="symbol">The entry to read.</param>
    /// <param name="destination">Receives the value when present.</param>
    /// <returns><see langword="true"/> when the entry was present and numeric.</returns>
    public static bool ReadSpacingSpec(object spec, Symbol symbol, ref double destination)
    {
        Pair pair = SchemeUtilities.Assq(symbol, spec);
        if (pair != null && SchemeConvert.IsNumber(pair.Cdr))
        {
            destination = SchemeConvert.ToDouble(pair.Cdr, "spacing-spec");
            return true;
        }

        return false;
    }

    private static object AddStretchability(object alist, double stretch)
    {
        if (SchemeUtilities.Assq(StretchabilitySymbol, alist) == null)
        {
            return new Pair(new Pair(StretchabilitySymbol, stretch), alist ?? Nil.Instance);
        }

        return alist;
    }

    /// <summary>
    /// Returns the spacing spec that rules the gap between two vertically adjacent
    /// grobs, chosen by which of the two is a spaceable staff and, for loose lines, by
    /// their <c>staff-affinity</c>.
    /// <para>
    /// PURE lookups take the unpure answer: the pure-property machinery is
    /// <c>unpure-pure-container.cc</c>'s, and every spec the vendored defaults
    /// state is a plain alist for which the two answers coincide. Recorded in
    /// PORT-COVERAGE.
    /// </para>
    /// </summary>
    /// <param name="before">The upper grob, or <see langword="null"/>.</param>
    /// <param name="after">The lower grob, or <see langword="null"/>.</param>
    /// <param name="pure">Whether this is a pure (pre-line-breaking) lookup.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <returns>The spec alist.</returns>
    public static object GetSpacingSpec(Grob before, Grob after, bool pure, int start, int end)
    {
        // If there are no spacing wishes, return a very flexible spring.
        // This will occur, for example, if there are lyrics at the bottom of
        // the page, in which case we don't want the spring from the lyrics to
        // the bottom of the page to have much effect.
        if (before == null || after == null)
        {
            return AddStretchability(Nil.Instance, HugeStretch);
        }

        if (IsSpaceable(before))
        {
            if (IsSpaceable(after))
            {
                return before.GetProperty(StaffStaffSpacingSymbol);
            }

            Direction affinity = DirectionalElementInterface.FromScheme(
                after.GetProperty(StaffAffinitySymbol), Direction.Center);
            return affinity == Direction.Negative
                ? AddStretchability(
                    after.GetProperty(NonstaffUnrelatedstaffSymbol), LargeStretch)
                : after.GetProperty(NonstaffRelatedstaffSymbol);
        }

        if (IsSpaceable(after))
        {
            Direction affinity = DirectionalElementInterface.FromScheme(
                before.GetProperty(StaffAffinitySymbol), Direction.Center);
            return affinity == Direction.Positive
                ? AddStretchability(
                    before.GetProperty(NonstaffUnrelatedstaffSymbol), LargeStretch)
                : before.GetProperty(NonstaffRelatedstaffSymbol);
        }

        Direction beforeAffinity = DirectionalElementInterface.FromScheme(
            before.GetProperty(StaffAffinitySymbol), Direction.Center);
        Direction afterAffinity = DirectionalElementInterface.FromScheme(
            after.GetProperty(StaffAffinitySymbol), Direction.Center);
        if ((int)afterAffinity > (int)beforeAffinity && !_affinityWarned && !pure)
        {
            Warn.Warning("staff-affinities should only decrease");
            _affinityWarned = true;
        }

        if (beforeAffinity != Direction.Positive)
        {
            return before.GetProperty(NonstaffNonstaffSymbol);
        }
        else if (afterAffinity != Direction.Negative)
        {
            return before.GetProperty(NonstaffNonstaffSymbol);
        }

        return AddStretchability(
            before.GetProperty(NonstaffUnrelatedstaffSymbol), LargeStretch);
    }

    /// <summary>
    /// Returns the FIXED distance <c>line-break-system-details</c>'s
    /// <c>alignment-distances</c> forces between two spaceable staves, or negative
    /// infinity when none is forced.
    /// <para>
    /// Upstream caches the pure answer on the AFTER spanner
    /// (<c>get_cached_pure_property</c>); the cache is pure-property machinery and is skipped
    /// here — the answer is recomputed, never wrong.
    /// </para>
    /// </summary>
    /// <param name="before">The upper grob.</param>
    /// <param name="after">The lower grob.</param>
    /// <param name="spaceableIndex">How many spaceable staves precede <paramref name="after"/>.</param>
    /// <param name="pure">Whether this is a pure lookup.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <returns>The forced distance, or negative infinity.</returns>
    public static double GetFixedSpacing(
        Grob before,
        Grob after,
        int spaceableIndex,
        bool pure,
        int start,
        int end)
    {
        double ret = double.NegativeInfinity;

        // If we're pure, then paper-columns have not had their systems set,
        // and so elts[i]->get_system () is unreliable.
        SystemGrob sys = pure ? Grob.SystemOf(before) : before.GetSystem();
        Grob leftBound = sys?.GetBound(Direction.Negative);

        if (IsSpaceable(before) && IsSpaceable(after) && leftBound != null)
        {
            object details = leftBound.GetProperty(LineBreakSystemDetailsSymbol);
            Pair entry = SchemeUtilities.Assq(AlignmentDistancesSymbol, details);
            object manualDists = entry == null ? Nil.Instance : entry.Cdr;
            if (manualDists is Pair)
            {
                object forced = RobustListRef(spaceableIndex - 1, manualDists);
                if (SchemeConvert.IsNumber(forced))
                {
                    ret = System.Math.Max(
                        ret, SchemeConvert.ToDouble(forced, "alignment-distances"));
                }
            }
        }

        return ret;
    }

    /// <summary>
    /// Returns list element <paramref name="index"/>, or the LAST element when the list
    /// is shorter — upstream's <c>robust_list_ref</c>, whose clamping is what lets one
    /// <c>alignment-distances</c> entry rule every following staff.
    /// </summary>
    /// <param name="index">The index wanted.</param>
    /// <param name="list">The list.</param>
    /// <returns>The element, or the empty list for an empty list.</returns>
    public static object RobustListRef(int index, object list)
    {
        object cursor = list;
        while (index-- > 0 && cursor is Pair pair && pair.Cdr is Pair)
        {
            cursor = pair.Cdr;
        }

        return cursor is Pair head ? head.Car : Nil.Instance;
    }
}
