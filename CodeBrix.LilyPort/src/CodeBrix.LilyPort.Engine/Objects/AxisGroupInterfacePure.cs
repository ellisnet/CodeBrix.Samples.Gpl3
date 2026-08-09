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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/axis-group-interface.cc (the pure-height half);

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - NEW FILE, added by EPG15. axis-group-interface.cc's ledger row has said `ported'
//     since EPG0, but its entire PURE-HEIGHT half had never come across: pure_group_height,
//     relative_pure_height, sum_partial_pure_heights, part_of_line_pure_height,
//     begin_of_line_pure_height, rest_of_line_pure_height, combine_pure_heights,
//     adjacent_pure_heights and the two calc_pure_relevant_grobs. Nothing asked for them
//     until line breaking did, which is the same shape as EPG11's grob.cc constructor
//     defaults and EPG18's Context_handle: registered, plausible, and hollow.
//   - It lives in its own file rather than in AxisGroupInterface.cs for the reason
//     AxisGroupInterfaceVertical.cs already exists: the upstream file is 1,100 lines and
//     the port splits it by subsystem. The `was previously' line names the half.

/// <summary>
/// The PURE half of the axis-group interface: what a group of grobs would be tall, for a
/// line that has not been chosen yet.
/// <para>
/// The line breaker has to know how tall each candidate line would be BEFORE it decides
/// where the lines go, and it asks about thousands of candidates. A pure height is that
/// estimate: it is computed from a column range rather than from real positions, it may
/// not trigger any of the layout that would fix those positions, and it is cached per
/// range because the same question is asked over and over.
/// </para>
/// <para>
/// The estimate is deliberately additive where it can be. Upstream's own comment says it
/// saves "a _lot_ of time" to assume a VerticalAxisGroup's height over a range is the
/// union of its parts, notes that this is not always true when a VerticalAlignment is
/// among the descendants, and settles on the rule reproduced here: assume additivity when
/// our Y parent is an alignment, since in practice the only alignment comes from Score.
/// </para>
/// </summary>
public static class AxisGroupInterfacePure
{
    private static readonly Symbol PureYCommonSymbol = Symbol.Intern("pure-Y-common");
    private static readonly Symbol PureRelevantGrobsSymbol = Symbol.Intern("pure-relevant-grobs");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol OutsideStaffPrioritySymbol
        = Symbol.Intern("outside-staff-priority");
    private static readonly Symbol OutsideStaffPaddingSymbol
        = Symbol.Intern("outside-staff-padding");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol AdjacentPureHeightsSymbol
        = Symbol.Intern("adjacent-pure-heights");
    private static readonly Symbol BeginOfLinePureHeightSymbol
        = Symbol.Intern("begin-of-line-pure-height");
    private static readonly Symbol RestOfLinePureHeightSymbol
        = Symbol.Intern("rest-of-line-pure-height");
    private static readonly Symbol StemInterfaceSymbol = Symbol.Intern("stem-interface");
    private static readonly Symbol AlignInterfaceSymbol = Symbol.Intern("align-interface");

    /// <summary>
    /// The pure vertical extent of a group over a column range, measured from the group
    /// itself — <c>ly:axis-group-interface::pure-height</c>.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The pure extent.</returns>
    public static Interval PureGroupHeight(Grob me, int start, int end)
    {
        Grob common = me.GetObject(PureYCommonSymbol) as Grob;

        if (common == null)
        {
            Warn.ProgrammingError("no pure Y common refpoint");
            return Interval.Empty;
        }

        double myCoord = me.PureRelativeYCoordinate(common, start, end);
        Interval r = RelativePureHeight(me, start, end);

        r.Left -= myCoord;
        r.Right -= myCoord;
        return r;
    }

    /// <summary>
    /// The pure vertical extent of a group relative to its pure common refpoint.
    /// <para>
    /// Takes the ADDITIVE shortcut when this group's vertical parent is an alignment —
    /// see the type's remarks for why that is safe in practice and what it buys — and
    /// otherwise measures every pure-relevant grob directly.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The pure extent.</returns>
    public static Interval RelativePureHeight(Grob me, int start, int end)
    {
        /* It saves a _lot_ of time if we assume a VerticalAxisGroup is additive
           (ie. height (i, k) = max (height (i, j) height (j, k)) for all i <= j <= k).
           Unfortunately, it isn't always true, particularly if there is a
           VerticalAlignment somewhere in the descendants.

           Usually, the only VerticalAlignment comes from Score. This makes it
           reasonably safe to assume that if our parent is a VerticalAlignment,
           we can assume additivity and cache things nicely. */
        Grob p = me.GetParent(Axis.Y);
        if (p != null && p.HasInterface(AlignInterfaceSymbol))
        {
            return SumPartialPureHeights(me, start, end);
        }

        Grob common = me.GetObject(PureYCommonSymbol) as Grob;
        IReadOnlyList<Grob> elts
            = PointerGroupInterface.ExtractGrobSet(me, PureRelevantGrobsSymbol);

        Interval r = Interval.Empty;
        foreach (Grob element in elts)
        {
            Grob g = element.PureFindVisiblePrebrokenPiece(start, end);
            if (g == null)
            {
                continue;
            }

            Slice rankSpan = g.SpannedColumnRankInterval();
            if (rankSpan.Left <= end
                && rankSpan.Right >= start
                && !(SchemeUtilities.ToBool(g.GetProperty(CrossStaffSymbol))
                     && g.HasInterface(StemInterfaceSymbol)))
            {
                Interval dims = g.PureYExtent(common, start, end);
                if (!dims.IsEmpty)
                {
                    r.Unite(dims);
                }
            }
        }

        return r;
    }

    /// <summary>
    /// The union of the beginning-of-line and rest-of-line pure heights — the additive
    /// estimate <see cref="RelativePureHeight"/> takes when it can.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The combined extent.</returns>
    public static Interval SumPartialPureHeights(Grob me, int start, int end)
    {
        Interval iv = BeginOfLinePureHeight(me, start);
        iv.Unite(RestOfLinePureHeight(me, start, end));

        return iv;
    }

    /// <summary>
    /// The pure height of one PART of a line — its beginning, or its remainder.
    /// <para>
    /// Reads the per-measure vectors <c>adjacent-pure-heights</c> holds and combines only
    /// the measures the requested range covers, and caches the answer on the spanner
    /// keyed by that range. A grob that is not a spanner has no cache and no measures,
    /// and answers a zero interval rather than an empty one, which is upstream's choice
    /// and matters: an empty interval would propagate as "no height known" rather than as
    /// "no height".
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="begin">Whether the beginning of the line is wanted.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The extent.</returns>
    public static Interval PartOfLinePureHeight(Grob me, bool begin, int start, int end)
    {
        if (!(me is Spanner sp))
        {
            return new Interval(0, 0);
        }

        Symbol cacheSymbol = begin ? BeginOfLinePureHeightSymbol : RestOfLinePureHeightSymbol;
        object cached = sp.GetCachedPureProperty(cacheSymbol, start, end);
        if (cached is Pair cachedPair
            && SchemeConvert.IsNumber(cachedPair.Car)
            && SchemeConvert.IsNumber(cachedPair.Cdr))
        {
            return new Interval(
                SchemeConvert.ToDouble(cachedPair.Car, "pure height"),
                SchemeConvert.ToDouble(cachedPair.Cdr, "pure height"));
        }

        object adjacentPureHeights = me.GetProperty(AdjacentPureHeightsSymbol);
        Interval ret;

        if (!(adjacentPureHeights is Pair adjacent))
        {
            ret = new Interval(0, 0);
        }
        else
        {
            object thesePureHeights = begin ? adjacent.Car : adjacent.Cdr;

            ret = thesePureHeights is object[] vector
                ? CombinePureHeights(me, vector, start, end)
                : new Interval(0, 0);
        }

        sp.CachePureProperty(cacheSymbol, start, end, new Pair(ret.Left, ret.Right));
        return ret;
    }

    /// <summary>The pure height of the START of a line.</summary>
    /// <param name="me">The group.</param>
    /// <param name="start">The starting column rank.</param>
    /// <returns>The extent.</returns>
    public static Interval BeginOfLinePureHeight(Grob me, int start)
        => PartOfLinePureHeight(me, true, start, start + 1);

    /// <summary>The pure height of the REST of a line.</summary>
    /// <param name="me">The group.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The extent.</returns>
    public static Interval RestOfLinePureHeight(Grob me, int start, int end)
        => PartOfLinePureHeight(me, false, start, end);

    /// <summary>
    /// Unites the per-measure extents that fall inside a column range.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="measureExtents">One extent per measure.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The united extent.</returns>
    public static Interval CombinePureHeights(
        Grob me, object[] measureExtents, int start, int end)
    {
        SystemGrob root = SystemGrob.GetRootSystem(me);
        Layout.PaperScore ps = root?.PaperScore;
        if (ps == null)
        {
            return Interval.Empty;
        }

        IReadOnlyList<int> breakRanks = ps.GetBreakRanks();
        int breakIdx = LowerBound(breakRanks, start);
        IReadOnlyList<int> breaks = ps.GetBreakIndices();
        IReadOnlyList<PaperColumn> cols = ps.GetColumns();

        Interval ext = Interval.Empty;
        for (int i = breakIdx; i + 1 < breaks.Count; i++)
        {
            int r = cols[breaks[i]].Rank;
            if (r >= end)
            {
                break;
            }

            if (i < measureExtents.Length
                && measureExtents[i] is Pair pair
                && SchemeConvert.IsNumber(pair.Car)
                && SchemeConvert.IsNumber(pair.Cdr))
            {
                ext.Unite(new Interval(
                    SchemeConvert.ToDouble(pair.Car, "measure extent"),
                    SchemeConvert.ToDouble(pair.Cdr, "measure extent")));
            }
        }

        return ext;
    }

    /// <summary>
    /// Computes, for every measure in the score, how tall this group is when the measure
    /// STARTS a line and how tall it is when the measure sits inside one —
    /// <c>ly:axis-group-interface::adjacent-pure-heights</c>.
    /// <para>
    /// The two differ because a clef, key signature or instrument name appears only at
    /// the start of a line, so the same measure is taller there. Storing both per measure
    /// is what makes <see cref="PartOfLinePureHeight"/> a lookup instead of a
    /// recomputation, which is what keeps line breaking out of quadratic time.
    /// </para>
    /// <para>
    /// Outside-staff grobs are handled with an approximation upstream states plainly: a
    /// snapshot of the staff heights is taken when the first outside-staff grob is met,
    /// and later outside-staff grobs are stacked above that snapshot rather than above
    /// each other. It accounts for outside-staff grobs needing to clear the staff, and
    /// deliberately not for them colliding with one another.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns>A pair of per-measure extent vectors: beginning-of-line and mid-line.</returns>
    public static object AdjacentPureHeights(Grob me)
    {
        Grob common = me.GetObject(PureYCommonSymbol) as Grob;
        IReadOnlyList<Grob> elts
            = PointerGroupInterface.ExtractGrobSet(me, PureRelevantGrobsSymbol);

        SystemGrob root = SystemGrob.GetRootSystem(me);
        Layout.PaperScore ps = root?.PaperScore;
        if (ps == null)
        {
            return new Pair(new object[0], new object[0]);
        }

        IReadOnlyList<int> ranks = ps.GetBreakRanks();
        if (ranks.Count == 0)
        {
            return new Pair(new object[0], new object[0]);
        }

        int measures = ranks.Count - 1;
        Interval[] beginLineHeights = NewEmptyIntervals(measures);
        Interval[] midLineHeights = NewEmptyIntervals(measures);
        Interval[] beginLineStaffHeights = null;
        Interval[] midLineStaffHeights = null;

        for (int i = 0; i < elts.Count; ++i)
        {
            Grob g = elts[i];

            if (SchemeUtilities.ToBool(g.GetProperty(CrossStaffSymbol)))
            {
                continue;
            }

            if (!g.IsLive)
            {
                if (!(g is Item it))
                {
                    continue;
                }

                if (it.GetColumn() == null)
                {
                    continue;
                }
            }

            bool outsideStaff = SchemeConvert.IsNumber(g.GetProperty(OutsideStaffPrioritySymbol));
            object paddingValue = g.GetProperty(OutsideStaffPaddingSymbol);
            double padding = SchemeConvert.IsNumber(paddingValue)
                ? SchemeConvert.ToDouble(paddingValue, "outside-staff-padding")
                : AxisGroupInterfaceVertical.GetDefaultOutsideStaffPadding();

            // When we encounter the first outside-staff grob, make a copy
            // of the current heights to use as an estimate for the staff heights.
            if (outsideStaff && beginLineStaffHeights == null)
            {
                beginLineStaffHeights = (Interval[])beginLineHeights.Clone();
                midLineStaffHeights = (Interval[])midLineHeights.Clone();
            }

            Direction d = DirectionalElementInterface.FromScheme(
                g.GetPropertyData(DirectionSymbol), Direction.Center);
            d = d == Direction.Center ? Direction.Positive : d;

            Slice rankSpan = g.SpannedColumnRankInterval();
            int firstBreak = LowerBound(ranks, rankSpan.Left);
            if (firstBreak != 0)
            {
                firstBreak--;
            }

            for (int j = firstBreak; j + 1 < ranks.Count && ranks[j] <= rankSpan.Right; ++j)
            {
                int start = ranks[j];
                int end = ranks[j + 1];

                // Take grobs that are visible with respect to a slightly longer line.
                // Otherwise, we will never include grobs at breakpoints which aren't
                // end-of-line-visible.
                int visibilityEnd = j + 2 < ranks.Count ? ranks[j + 2] : end;

                Grob maybeSubst = g.PureFindVisiblePrebrokenPiece(start, visibilityEnd);
                if (maybeSubst != null)
                {
                    Interval dims = maybeSubst.PureYExtent(common, start, end);
                    if (!dims.IsEmpty)
                    {
                        if (rankSpan.Left <= start)
                        {
                            if (outsideStaff)
                            {
                                beginLineHeights[j].Unite(
                                    beginLineStaffHeights[j].UnionDisjoint(dims, padding, d));
                            }
                            else
                            {
                                beginLineHeights[j].Unite(dims);
                            }
                        }

                        if (rankSpan.Right > start)
                        {
                            if (outsideStaff)
                            {
                                midLineHeights[j].Unite(
                                    midLineStaffHeights[j].UnionDisjoint(dims, padding, d));
                            }
                            else
                            {
                                midLineHeights[j].Unite(dims);
                            }
                        }
                    }
                }
            }
        }

        object[] beginScm = new object[measures];
        object[] midScm = new object[measures];
        for (int i = 0; i < measures; ++i)
        {
            beginScm[i] = new Pair(beginLineHeights[i].Left, beginLineHeights[i].Right);
            midScm[i] = new Pair(midLineHeights[i].Left, midLineHeights[i].Right);
        }

        return new Pair(beginScm, midScm);
    }

    /// <summary>
    /// The grobs whose pure heights are worth measuring —
    /// <c>ly:axis-group-interface::calc-pure-relevant-grobs</c>.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns>The relevant grobs, ordered by outside-staff priority.</returns>
    public static object CalcPureRelevantGrobs(Grob me)
        => InternalCalcPureRelevantGrobs(me, ElementsSymbol);

    /// <summary>
    /// The shared body of the pure-relevant-grob calculations, over any named grob set.
    /// <para>
    /// PREBROKEN CLONES ARE DROPPED and their originals kept — an item with an original
    /// is skipped — because the caller is expected to ask for the right clone through
    /// <see cref="Grob.PureFindVisiblePrebrokenPiece"/> when it needs one. Upstream's own
    /// comment says so, and notes the list may therefore include grobs that will be
    /// suicided.
    /// </para>
    /// <para>
    /// The sort is by outside-staff priority, and it is a STABLE sort here for the reason
    /// it matters elsewhere in this port: <c>std::sort</c> is unspecified for equal keys,
    /// but grobs with no priority all share the same sentinel, so an unstable sort would
    /// reorder the bulk of the list run to run.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="grobSetName">Which grob set to read.</param>
    /// <returns>The relevant grobs.</returns>
    public static object InternalCalcPureRelevantGrobs(Grob me, Symbol grobSetName)
    {
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, grobSetName);

        // It is cheaper to cache the outside-staff-priority than saving the one copy
        // to assemble the final Grob_array.
        List<(Grob Grob, double Priority, int Index)> relevantGrobs
            = new List<(Grob, double, int)>();

        foreach (Grob g in elts)
        {
            if (g is Item it && it.Original != null)
            {
                continue;
            }

            object priorityValue = g.GetProperty(OutsideStaffPrioritySymbol);
            double priority = SchemeConvert.IsNumber(priorityValue)
                ? SchemeConvert.ToDouble(priorityValue, "outside-staff-priority")
                : double.NegativeInfinity;

            /* This might include potentially suicided items. Callers should
               look at the relevant prebroken clone where necessary */
            relevantGrobs.Add((g, priority, relevantGrobs.Count));
        }

        relevantGrobs.Sort((a, b) => a.Priority != b.Priority
            ? a.Priority.CompareTo(b.Priority)
            : a.Index.CompareTo(b.Index));

        GrobArray grobs = new GrobArray();
        foreach ((Grob Grob, double Priority, int Index) entry in relevantGrobs)
        {
            grobs.Add(entry.Grob);
        }

        return grobs;
    }

    /// <summary>
    /// The reference point every pure height in this group is measured against —
    /// <c>ly:axis-group-interface::calc-pure-y-common</c>.
    /// <para>
    /// It is the common vertical refpoint of the pure-relevant grobs. A
    /// VerticalAlignment is REFUSED as the answer and replaced by the group itself,
    /// because an alignment may hold several staves and measuring one staff's contents
    /// against it would silently mix in the others. Upstream's own TODO says the real fix
    /// is to filter such elements out of <c>calc_pure_relevant_grobs</c>, and that until
    /// then "we need to trap this case in calc_pure_y_common" — this is that trap.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns>The common refpoint, or the empty list when there is none.</returns>
    public static object CalcPureYCommon(Grob me)
    {
        IReadOnlyList<Grob> elts
            = PointerGroupInterface.ExtractGrobSet(me, PureRelevantGrobsSymbol);
        Grob common = AxisGroupInterface.CommonRefpointOfArray(elts, me, Axis.Y);
        if (!ReferenceEquals(common, me)
            && common != null
            && common.HasInterface(AlignInterfaceSymbol))
        {
            Warn.ProgrammingError(
                "My pure_y_common is a VerticalAlignment, which might contain several staves.");
            common = me;
        }

        if (common == null)
        {
            Warn.ProgrammingError("No common parent found in calc_pure_y_common.");
            return Nil.Instance;
        }

        return common;
    }

    private static Interval[] NewEmptyIntervals(int count)
    {
        Interval[] result = new Interval[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = Interval.Empty;
        }

        return result;
    }

    private static int LowerBound(IReadOnlyList<int> values, int target)
    {
        int lo = 0;
        int hi = values.Count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (values[mid] < target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
