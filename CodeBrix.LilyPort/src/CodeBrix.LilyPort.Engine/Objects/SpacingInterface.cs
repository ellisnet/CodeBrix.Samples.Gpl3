/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2007--2026 Joe Neeman <joeneeman@gmail.com>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/spacing-interface.cc, lily/include/spacing-interface.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - skylines shifts by the PURE relative Y coordinate, as upstream; the
//     horizontal-spacing group's recorded re-check came true at scale. See PORT-COVERAGE.

/// <summary>
/// The shared half of the two spacing wishes: how far apart the columns a wish spans
/// may come, and what sits on either side of it.
/// <para>
/// A wish is an ITEM that names a set of grobs on its left and a set on its right. The
/// distance it can state is the distance between the facing horizontal skylines of
/// those two sets, which is why almost everything here is a walk from the wish out to
/// the separation items that actually carry the profiles.
/// </para>
/// </summary>
public static class SpacingInterface
{
    private static readonly Symbol LeftItems = Symbol.Intern("left-items");
    private static readonly Symbol RightItems = Symbol.Intern("right-items");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol HorizontalSkylines = Symbol.Intern("horizontal-skylines");
    private static readonly Symbol SeparationItemInterface
        = Symbol.Intern("separation-item-interface");

    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol SpaceAlist = Symbol.Intern("space-alist");
    private static readonly Symbol LeftBreakAligned = Symbol.Intern("left-break-aligned");
    private static readonly Symbol RightBreakAligned = Symbol.Intern("right-break-aligned");

    /* return the right-pointing skyline of the left-items and the left-pointing
       skyline of the right-items (with the skyline of the left-items in
       ret[LEFT]) */

    /// <summary>
    /// Returns the two facing skylines a wish spans: the right-pointing profile of
    /// everything on its left, and the left-pointing profile of everything on its right.
    /// <para>
    /// The item lists are read off the ORIGINAL wish, because a wish does not copy them
    /// when it is prebroken; each item found is then swapped back to the prebroken piece
    /// that belongs on this side of the break. Skip either half of that dance and a
    /// broken wish silently measures the wrong pieces.
    /// </para>
    /// <para>
    /// DIVERGENCE, recorded in PORT-COVERAGE: upstream shifts by a PURE relative Y
    /// coordinate. The pure/unpure machinery is the line-breaking group's; the port uses the real
    /// coordinate, which agrees for every grob that has no separate pure callback — all
    /// of them, currently — but does compute and cache a Y offset earlier than upstream
    /// would.
    /// </para>
    /// </summary>
    /// <param name="me">The spacing wish.</param>
    /// <param name="rightCol">The column on the right.</param>
    /// <returns>The two skylines, left-facing-right first.</returns>
    public static DrulArray<Skyline> Skylines(Grob me, Grob rightCol)
    {
        /* the logic here is a little convoluted.
           A {Staff,Note}_spacing doesn't copy left-items when it clones,
           so in order to find the separation items, we need to use the original
           spacing grob. But once we find the separation items, we need to get back
           the broken piece.
        */

        Grob orig = me.Original ?? me;
        DrulArray<Direction> breakDirs = new DrulArray<Direction>(
            (me as Item)?.BreakStatusDirection() ?? Direction.Center,
            (rightCol as Item)?.BreakStatusDirection() ?? Direction.Center);

        DrulArray<Skyline> skylines = new DrulArray<Skyline>(
            new Skyline(Direction.Positive), new Skyline(Direction.Negative));

        DrulArray<IReadOnlyList<Grob>> items = new DrulArray<IReadOnlyList<Grob>>(
            PointerGroupInterface.ExtractGrobSet(orig, LeftItems),
            PointerGroupInterface.ExtractGrobSet(orig, RightItems));

        Grob system = me.GetSystem();
        Grob leftCol = (me as Item)?.GetColumn();

        DrulArray<Grob> columns = new DrulArray<Grob>(leftCol, rightCol);

        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            IReadOnlyList<Grob> side = items[d];
            for (int i = 0; i < side.Count; i++)
            {
                Item g = side[i] as Item;
                if (g != null)
                {
                    Item piece = g.FindPrebrokenPiece(breakDirs[d]);
                    if (piece != null)
                    {
                        g = piece;
                    }
                }

                if (g != null
                    && g.HasInterface(SeparationItemInterface)
                    && ReferenceEquals(g.GetColumn(), columns[d]))
                {
                    SkylinePair skyp = SkylinePair.FromScheme(g.GetProperty(HorizontalSkylines));
                    if (skyp == null)
                    {
                        Warn.ProgrammingError("separation item has no skyline");
                        continue;
                    }

                    IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(g, ElementsSymbol);
                    Grob ycommon = AxisGroupInterface.CommonRefpointOfArray(elts, g, Axis.Y);

                    // The shift is a PURE coordinate upstream
                    // (`ycommon->pure_relative_y_coordinate (system, 0, INT_MAX)`):
                    // this runs during horizontal spacing, and the early
                    // ordinary stand-in retired with the stale-stand-in
                    // class sweep along with the rest of its class.
                    double shift = ycommon.PureRelativeYCoordinate(system, 0, int.MaxValue);

                    skylines[d].Shift(-shift);

                    skylines[d].Merge(skyp[-d]);

                    if (d == Direction.Positive && items[Direction.Negative].Count > 0)
                    {
                        skylines[d].Merge(SeparationItem.ConditionalSkyline(
                            side[i], items[Direction.Negative][0]));
                    }

                    skylines[d].Shift(shift);
                }
            }
        }

        return skylines;
    }

    /// <summary>
    /// Returns how close a wish will let its two columns come: where the facing
    /// skylines touch, never less than zero.
    /// </summary>
    /// <param name="me">The spacing wish.</param>
    /// <param name="right">The column on the right.</param>
    /// <returns>The minimum distance.</returns>
    public static double MinimumDistance(Grob me, Grob right)
    {
        DrulArray<Skyline> skylines = Skylines(me, right);

        return Math.Max(0.0, skylines[Direction.Negative].Distance(skylines[Direction.Positive]));
    }

    /*
      Compute the left-most column of the right-items.
    */

    /// <summary>Returns the leftmost column any of a wish's right-items sits in.</summary>
    /// <param name="me">The spacing wish.</param>
    /// <returns>The column, or <see langword="null"/>.</returns>
    public static PaperColumn RightColumn(Grob me)
    {
        if (me == null || !me.IsLive)
        {
            return null;
        }

        PaperColumn mincol = null;
        if (me.GetObject(RightItems) is GrobArray a)
        {
            int minRank = int.MaxValue;
            foreach (Grob rg in a)
            {
                if (rg is Item ri)
                {
                    PaperColumn col = ri.GetColumn();
                    if (col != null)
                    {
                        int rank = col.Rank;
                        if (rank < minRank)
                        {
                            minRank = rank;
                            mincol = col;
                        }
                    }
                }
            }
        }

        return mincol;
    }

    /// <summary>Returns the column a wish itself sits in.</summary>
    /// <param name="meAsGrob">The spacing wish.</param>
    /// <returns>The column, or <see langword="null"/>.</returns>
    public static PaperColumn LeftColumn(Grob meAsGrob)
    {
        Item me = meAsGrob as Item;
        if (me == null || !me.IsLive)
        {
            return null;
        }

        return me.GetColumn();
    }

    /// <summary>Returns the note columns among a wish's right-items.</summary>
    /// <param name="me">The spacing wish.</param>
    /// <returns>The note columns.</returns>
    public static List<Item> RightNoteColumns(Grob me)
        => GetNoteColumns(PointerGroupInterface.ExtractGrobSet(me, RightItems));

    /// <summary>Returns the note columns among a wish's left-items.</summary>
    /// <param name="me">The spacing wish.</param>
    /// <returns>The note columns.</returns>
    public static List<Item> LeftNoteColumns(Grob me)
        => GetNoteColumns(PointerGroupInterface.ExtractGrobSet(me, LeftItems));

    /*
      Try to find the break-aligned symbol that belongs on the D-side
      of ME, sticking out in direction -D. The x size is put in LAST_EXT
    */

    /// <summary>
    /// Finds the break-aligned grob on one side of a wish that sticks out furthest
    /// towards it — the clef, key signature or bar line the spacing is measured from.
    /// </summary>
    /// <param name="me">The spacing wish.</param>
    /// <param name="d">Which side of the wish to look at.</param>
    /// <param name="breakDir">Which prebroken piece to take.</param>
    /// <param name="lastExtent">Receives the winner's horizontal extent.</param>
    /// <returns>The grob, or <see langword="null"/> when there is none.</returns>
    public static Grob ExtremalBreakAlignedGrob(
        Grob me,
        Direction d,
        Direction breakDir,
        ref Interval lastExtent)
    {
        Grob col = null;
        lastExtent.SetEmpty();
        Grob lastGrob = null;

        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(
            me, d == Direction.Negative ? LeftBreakAligned : RightBreakAligned);

        for (int i = elts.Count; i-- > 0;)
        {
            Item breakItem = elts[i] as Item;

            if (breakItem != null && breakItem.BreakStatusDirection() != breakDir)
            {
                breakItem = breakItem.FindPrebrokenPiece(breakDir);
            }

            if (breakItem == null || !(breakItem.GetProperty(SpaceAlist) is Pair))
            {
                continue;
            }

            if (col == null)
            {
                col = (elts[0] as Item)?.GetColumn()?.FindPrebrokenPiece(breakDir);
                if (col == null)
                {
                    continue;
                }
            }

            Interval ext = breakItem.Extent(col, Axis.X);

            if (ext.IsEmpty)
            {
                continue;
            }

            if (lastGrob == null || (d * (ext[-d] - lastExtent[-d])) < 0)
            {
                lastExtent = ext;
                lastGrob = breakItem;
            }
        }

        return lastGrob;
    }

    private static List<Item> GetNoteColumns(IReadOnlyList<Grob> elts)
    {
        List<Item> ret = new List<Item>();

        for (int i = 0; i < elts.Count; i++)
        {
            if (elts[i].HasInterface(NoteColumnInterface))
            {
                ret.Add(elts[i] as Item);
            }
            else if (elts[i].HasInterface(SeparationItemInterface))
            {
                IReadOnlyList<Grob> moreElts
                    = PointerGroupInterface.ExtractGrobSet(elts[i], ElementsSymbol);
                ret.AddRange(GetNoteColumns(moreElts));
            }
        }

        return ret;
    }
}
