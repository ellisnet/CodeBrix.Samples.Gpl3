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
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/ledger-line-spanner.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - upstream's std::map<vsize, Drul_array<Ledger_request>> and
//     std::map<Real, std::vector<Interval>> are both ORDERED maps, and both are iterated
//     in key order to decide where ledgers shorten. The port keeps the keys in a sorted
//     list beside a Dictionary rather than using a SortedDictionary, because the double
//     keys are ledger positions read out of Staff_symbol and comparing them the way
//     std::map does — plain operator< — is what keeps the iteration order identical.

/*
  TODO: ledger share a lot of info. Lots of room to optimize away
  common use of objects/variables.
*/

/// <summary>
/// This spanner draws the ledger lines of a staff. This is a separate grob because it
/// has to process all potential collisions between all note heads.
/// </summary>
public static class LedgerLineSpanner
{
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol MinimumLengthFractionSymbol
        = Symbol.Intern("minimum-length-fraction");
    private static readonly Symbol LengthFractionSymbol = Symbol.Intern("length-fraction");
    private static readonly Symbol GapSymbol = Symbol.Intern("gap");
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol ParenthesizedSymbol = Symbol.Intern("parenthesized");
    private static readonly Symbol GlyphNameSymbol = Symbol.Intern("glyph-name");
    private static readonly Symbol AmbitusInterface = Symbol.Intern("ambitus-interface");

    private static readonly Direction[] DownUp = { Direction.Negative, Direction.Positive };
    private static readonly Direction[] LeftRight = { Direction.Negative, Direction.Positive };

    // upstream's Head_data.
    private sealed class HeadData
    {
        internal int Position;
        internal List<double> LedgerPositions = new List<double>();
        internal Interval HeadExtent = Interval.Empty;
        internal Interval LedgerExtent = Interval.Empty;
        internal Interval AccidentalExtent = Interval.Empty;
        internal Interval LedgerShorteningRange = Interval.Empty;
    }

    // upstream's Ledger_request.
    private sealed class LedgerRequest
    {
        internal Interval MaxLedgerExtent = Interval.Empty;
        internal Interval MaxHeadExtent = Interval.Empty;
        internal int MaxPosition;
        internal readonly List<HeadData> Heads = new List<HeadData>();

        // The map's keys are vertical ledger line positions. The values are
        // vectors of the x-extents of ledger lines.
        internal readonly List<double> LedgerExtentKeys = new List<double>();
        internal readonly Dictionary<double, List<Interval>> LedgerExtents
            = new Dictionary<double, List<Interval>>();

        internal void AddLedgerExtent(double lpos, Interval xExtent)
        {
            if (!LedgerExtents.TryGetValue(lpos, out List<Interval> list))
            {
                list = new List<Interval>();
                LedgerExtents[lpos] = list;
                LedgerExtentKeys.Add(lpos);
                LedgerExtentKeys.Sort();
            }

            list.Add(xExtent);
        }
    }

    private static void SetRods(
        DrulArray<Interval> currentExtents,
        DrulArray<Interval> previousExtents,
        Item currentColumn,
        Item previousColumn,
        double minLength)
    {
        foreach (Direction d in new[] { Direction.Positive, Direction.Negative })
        {
            if (!currentExtents[d].IsEmpty && !previousExtents[d].IsEmpty)
            {
                Rod rod = new Rod(currentColumn, previousColumn);
                rod.Distance = (2 * minLength)

                    /*
                      we go from right to left.
                    */
                    - previousExtents[d][Direction.Negative]
                    + currentExtents[d][Direction.Positive];
                rod.AddToColumns();
            }
        }
    }

    /// <summary>
    /// The <c>set-spacing-rods</c> callback: keep enough room between columns for the
    /// ledger lines that will be drawn between them.
    /// </summary>
    /// <param name="me">The ledger line spanner.</param>
    /// <returns>Unspecified, as upstream.</returns>
    public static object SetSpacingRods(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return Unspecified.Instance;
        }

        // find size of note heads.
        Grob staff = StaffSymbolReferencer.GetStaffSymbol(spanner);
        if (staff == null)
        {
            spanner.Suicide();
            return Nil.Instance;
        }

        double minLengthFraction
            = ToDouble(spanner.GetProperty(MinimumLengthFractionSymbol), 0.15);

        DrulArray<Interval> currentExtents
            = new DrulArray<Interval>(Interval.Empty, Interval.Empty);
        DrulArray<Interval> previousExtents
            = new DrulArray<Interval>(Interval.Empty, Interval.Empty);
        double currentHeadWidth = 0.0;
        Item previousColumn = null;
        Item currentColumn = null;

        double halfspace = StaffSymbol.StaffSpace(staff) / 2;

        /*
          Run through heads using a loop. Since Ledger_line_spanner can
          contain a lot of noteheads, superlinear performance is too slow.
        */
        IReadOnlyList<Grob> heads
            = PointerGroupInterface.ExtractGrobSet(spanner, NoteHeadsSymbol);
        for (int i = heads.Count; i-- > 0;)
        {
            if (!(heads[i] is Item h))
            {
                continue;
            }

            int pos = StaffSymbolReferencer.GetRoundedPosition(h);
            if (StaffSymbol.LedgerPositions(staff, pos, null).Count == 0)
            {
                continue;
            }

            /* Ambitus heads can appear out-of-order in heads[],
             * but as part of prefatory matter, they need no rods */
            if (h.HasInterface(AmbitusInterface))
            {
                continue;
            }

            Item column = h.GetColumn();
            if (!ReferenceEquals(currentColumn, column))
            {
                SetRods(
                    currentExtents,
                    previousExtents,
                    currentColumn,
                    previousColumn,
                    currentHeadWidth * minLengthFraction);
                previousColumn = currentColumn;
                currentColumn = column;
                previousExtents = currentExtents;
                currentExtents = new DrulArray<Interval>(Interval.Empty, Interval.Empty);
                currentHeadWidth = 0.0;
            }

            Interval headExtent = h.Extent(column, Axis.X);
            Direction vdir = new Direction((long)Math.Sign(pos));
            if (vdir == Direction.Center)
            {
                continue;
            }

            Interval united = currentExtents[vdir];
            united.Unite(headExtent);
            currentExtents[vdir] = united;
            currentHeadWidth = Math.Max(currentHeadWidth, headExtent.Length);
        }

        if (previousColumn != null && currentColumn != null)
        {
            SetRods(
                currentExtents,
                previousExtents,
                currentColumn,
                previousColumn,
                currentHeadWidth * minLengthFraction);
        }

        return Unspecified.Instance;
    }

    /// <summary>Draws every ledger line the staff needs.</summary>
    /// <param name="me">The ledger line spanner.</param>
    /// <returns>The stencil, or the empty list when there is nothing to draw.</returns>
    public static object Print(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return Nil.Instance;
        }

        // Generate ledger requests from note head properties, etc.
        IReadOnlyList<Grob> heads
            = PointerGroupInterface.ExtractGrobSet(spanner, NoteHeadsSymbol);
        if (heads.Count == 0)
        {
            return Nil.Instance;
        }

        Grob staff = StaffSymbolReferencer.GetStaffSymbol(spanner);
        if (staff == null)
        {
            return Nil.Instance;
        }

        double halfspace = StaffSymbol.StaffSpace(staff) / 2;
        Interval staffExtent = staff.Extent(staff, Axis.Y);
        staffExtent *= 1 / halfspace;

        double lengthFraction = ToDouble(spanner.GetProperty(LengthFractionSymbol), 0.25);

        Grob commonX = AxisGroupInterface.CommonRefpointOfArray(heads, spanner, Axis.X);
        for (int i = heads.Count; i-- > 0;)
        {
            if (heads[i].GetObject(AccidentalGrobSymbol) is Grob g)
            {
                commonX = commonX.CommonRefpoint(g, Axis.X);
            }
        }

        // upstream's Ledger_requests: an ORDERED map from column rank to a drul pair.
        List<int> ranks = new List<int>();
        Dictionary<int, DrulArray<LedgerRequest>> reqs
            = new Dictionary<int, DrulArray<LedgerRequest>>();

        for (int i = heads.Count; i-- > 0;)
        {
            if (!(heads[i] is Item h))
            {
                continue;
            }

            int pos = StaffSymbolReferencer.GetRoundedPosition(h);
            List<double> ledgerPositions = StaffSymbol.LedgerPositions(staff, pos, h);

            // We work with all notes that produce ledgers and any notes that
            // fall outside the staff that do not produce ledgers, such as
            // notes in the first space just beyond the staff.
            if (ledgerPositions.Count != 0 || !staffExtent.Contains(pos))
            {
                Interval headExtent = h.Extent(commonX, Axis.X);
                Interval ledgerExtent = headExtent;
                ledgerExtent.Widen(lengthFraction * headExtent.Length);
                Direction vdir = new Direction((long)Math.Sign(pos != 0 ? pos : 1));
                int rank = h.GetColumn().Rank;

                if (!reqs.TryGetValue(rank, out DrulArray<LedgerRequest> pair))
                {
                    pair = new DrulArray<LedgerRequest>(
                        new LedgerRequest(), new LedgerRequest());
                    reqs[rank] = pair;
                    ranks.Add(rank);
                    ranks.Sort();
                }

                LedgerRequest lr = pair[vdir];
                lr.MaxLedgerExtent.Unite(ledgerExtent);
                lr.MaxHeadExtent.Unite(headExtent);
                lr.MaxPosition = (int)vdir
                    * Math.Max((int)vdir * lr.MaxPosition, (int)vdir * pos);

                HeadData hd = new HeadData();
                hd.Position = pos;
                hd.LedgerPositions = ledgerPositions;
                hd.LedgerExtent = ledgerExtent;
                hd.HeadExtent = headExtent;

                if (h.GetObject(AccidentalGrobSymbol) is Grob g)
                {
                    hd.AccidentalExtent = g.Extent(commonX, Axis.X);
                    string glyph = null;
                    if (SchemeUtilities.ToBool(g.GetProperty(ParenthesizedSymbol)))
                    {
                        glyph = "accidentals.rightparen";
                    }
                    else if (g.GetProperty(GlyphNameSymbol) is string glyphName)
                    {
                        glyph = glyphName;
                    }

                    if (!string.IsNullOrEmpty(glyph))
                    {
                        FontMetric fm = FontInterface.GetDefaultFont(g);
                        hd.LedgerShorteningRange
                            = fm.LedgerShorteningRange(glyph) * (1 / halfspace);

                        // Compensate for rounding errors.
                        hd.LedgerShorteningRange.Widen(1e-3);
                    }
                }

                lr.Heads.Add(hd);
            }
        }

        if (reqs.Count == 0)
        {
            return Nil.Instance;
        }

        // Iterate through ledger requests and when ledger lines will be
        // too close together horizontally, shorten max_ledger_extent to
        // produce more space between them.
        double gap = ToDouble(spanner.GetProperty(GapSymbol), 0.1);

        // upstream walks with `last = i++` and skips the first pass, which is the same
        // pairing as starting at the second rank and reading the one before it.
        for (int ri = 1; ri < ranks.Count; ri++)
        {
            foreach (Direction d in DownUp)
            {
                LedgerRequest lastReq = reqs[ranks[ri - 1]][d];
                LedgerRequest thisReq = reqs[ranks[ri]][d];

                // Some rank--> vdir--> reqs will be 'empty' because notes
                // will not be above AND below the staff for a given rank.
                if (!staffExtent.Contains(lastReq.MaxPosition)
                    && !staffExtent.Contains(thisReq.MaxPosition))
                {
                    // Midpoint between the furthest bounds of the two heads.
                    double center = (lastReq.MaxHeadExtent[Direction.Positive]
                                     + thisReq.MaxHeadExtent[Direction.Negative]) / 2;

                    // Do both reqs have notes further than the first space
                    // beyond the staff?
                    // (due tilt of quarter note-heads)
                    /* FIXME */
                    bool both
                        = !staffExtent.Contains(
                              lastReq.MaxPosition - Math.Sign(lastReq.MaxPosition))
                          && !staffExtent.Contains(
                              thisReq.MaxPosition - Math.Sign(thisReq.MaxPosition));

                    foreach (Direction which in LeftRight)
                    {
                        LedgerRequest lr = which == Direction.Negative ? lastReq : thisReq;
                        double limit = center + (both ? (int)which * gap / 2 : 0);
                        lr.MaxLedgerExtent[-which]
                            = (int)which
                              * Math.Max(
                                  (int)which * lr.MaxLedgerExtent[-which],
                                  (int)which * limit);
                    }
                }
            }
        }

        // Iterate through ledger requests and the data they have about each
        // note head to generate the final extents for all ledger lines.
        // Note heads of different widths produce different ledger extents.
        for (int ri = 0; ri < ranks.Count; ri++)
        {
            DrulArray<LedgerRequest> dirReqs = reqs[ranks[ri]];
            foreach (Direction d in DownUp)
            {
                LedgerRequest lr = dirReqs[d];
                for (int h = 0; h < lr.Heads.Count; h++)
                {
                    List<double> ledgerPosns = lr.Heads[h].LedgerPositions;
                    Interval ledgerSize = lr.Heads[h].LedgerExtent;
                    Interval headSize = lr.Heads[h].HeadExtent;
                    Interval accExtent = lr.Heads[h].AccidentalExtent;
                    int pos = lr.Heads[h].Position;

                    // TODO: shall this be made user-configurable?
                    Interval ledgerShorteningRange = lr.Heads[h].LedgerShorteningRange;

                    // Limit ledger extents to a maximum to preserve space
                    // between ledgers when note heads get close.
                    if (!lr.MaxLedgerExtent.IsEmpty)
                    {
                        ledgerSize.Intersect(lr.MaxLedgerExtent);
                    }

                    // Iterate through the ledgers for a given note head.
                    for (int l = 0; l < ledgerPosns.Count; l++)
                    {
                        double lpos = ledgerPosns[l];
                        Interval xExtent = ledgerSize;

                        // Notes with accidental signs get shorter ledgers.
                        if (ledgerShorteningRange.Contains(lpos - pos) && !accExtent.IsEmpty)
                        {
                            double dist = (accExtent.Right + headSize.Left) / 2;
                            double leftShorten
                                = Math.Max(-ledgerSize[Direction.Negative] + dist, 0.0);
                            xExtent[Direction.Negative] += leftShorten;
                        }

                        if (xExtent.IsEmpty)
                        {
                            continue;
                        }

                        lr.AddLedgerExtent(lpos, xExtent);
                    }
                }
            }
        }

        // Create the stencil for the ledger line spanner by iterating
        // through the ledger requests and their data on ledger extents.
        Stencil ledgers = Stencil.Empty;
        double thickness = StaffSymbol.GetLedgerLineThickness(staff);
        double halfThickness = thickness * 0.5;
        Interval yExtent = new Interval(-halfThickness, halfThickness);

        for (int ri = 0; ri < ranks.Count; ri++)
        {
            DrulArray<LedgerRequest> dirReqs = reqs[ranks[ri]];
            foreach (Direction d in DownUp)
            {
                LedgerRequest lr = dirReqs[d];
                for (int k = 0; k < lr.LedgerExtentKeys.Count; k++)
                {
                    double lpos = lr.LedgerExtentKeys[k];
                    List<Interval> extents = lr.LedgerExtents[lpos];

                    // When the extents of two ledgers at the same
                    // vertical position overlap horizontally, we merge
                    // them together to produce a single stencil.  In rare
                    // cases they do not overlap and we do not merge them.
                    IntervalSet merged = IntervalSet.IntervalUnion(extents);
                    foreach (Interval xExtent in merged.Intervals)
                    {
                        // thickness (ledger line thickness) is the blot diameter
                        Stencil line = Lookup.RoundFilledBox(
                            new Box(xExtent, yExtent), thickness);
                        line.TranslateAxis(lpos * halfspace, Axis.Y);
                        ledgers.AddStencil(line);
                    }
                }
            }
        }

        ledgers.TranslateAxis(-spanner.RelativeCoordinate(commonX, Axis.X), Axis.X);
        return ledgers;
    }

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "ledger-line-spanner")
            : fallback;
}
