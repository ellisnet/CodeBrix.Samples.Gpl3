/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/spacing-determine-loose-columns.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the do-not-float-a-bar-line guard's stand-in is RETIRED and the call routes to
//     BreakAlignmentInterface.FindNonemptyBreakAlignGroup, which had landed long
//     before and which nothing re-checked against this site. See PORT-COVERAGE.

/// <summary>
/// The half of the spacing spanner that decides which columns are LOOSE — attached to
/// their neighbours rather than solved for.
/// <para>
/// A clef change in the middle of a measure has no rhythmic position of its own; making
/// the solver find one distorts the notes around it. Such a column is pruned out of the
/// spacing problem entirely, told which two columns it now lives between, and draped
/// back into place afterwards.
/// </para>
/// </summary>
public static partial class SpacingSpanner
{
    private static readonly Symbol AllowLooseSpacing = Symbol.Intern("allow-loose-spacing");
    private static readonly Symbol RightNeighbor = Symbol.Intern("right-neighbor");
    private static readonly Symbol LeftNeighbor = Symbol.Intern("left-neighbor");
    private static readonly Symbol LabelsSymbol = Symbol.Intern("labels");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol BreakAlignmentSymbol = Symbol.Intern("break-alignment");
    private static readonly Symbol StaffBarSymbol = Symbol.Intern("staff-bar");

    /*
      Return whether COL is fixed to its neighbors by some kind of spacing
      constraint.


      If in doubt, then we're not loose; the spacing engine should space
      for it, risking suboptimal spacing.

      (Otherwise, we might risk core dumps, and other weird stuff.)
    */
    private static bool IsLooseColumn(
        PaperColumn l,
        PaperColumn col,
        PaperColumn r,
        SpacingOptions options)
    {
        if (!SchemeUtilities.ToBool(col.GetProperty(AllowLooseSpacing)))
        {
            return false;
        }

        if ((options.FloatNonmusicalColumns || options.FloatGraceColumns)
            && PaperColumn.WhenMoment(col).GracePart.IsNonZero)
        {
            return true;
        }

        if (PaperColumn.IsMusical(col))
        {
            return false;
        }

        /*
          If this column doesn't have a proper neighbor, we should really
          make it loose, but spacing it correctly is more than we can
          currently can handle.

          (this happens in the following situation:

          |
          |    clef G
          *

          |               |      ||
          |               |      ||
          O               O       ||


          the column containing the clef is really loose, and should be
          attached right to the first column, but that is a lot of work for
          such a borderline case.)
        */

        PaperColumn rNeighbor = col.GetObject(RightNeighbor) as PaperColumn;
        if (rNeighbor == null)
        {
            return false;
        }

        PaperColumn lNeighbor = col.GetObject(LeftNeighbor) as PaperColumn;
        if (lNeighbor == null)
        {
            return false;
        }

        /* If a non-empty column (ie. not \bar "") is placed nicely in series with
           its neighbor (ie. no funny polyphonic stuff), don't make it loose.
        */
        if (ReferenceEquals(l, lNeighbor) && ReferenceEquals(r, rNeighbor)
            && col.Extent(col, Axis.X).Length > 0)
        {
            return false;
        }

        /*
          Only declare loose if the bounds make a little sense.  This means
          some cases (two isolated, consecutive clef changes) won't be
          nicely folded, but hey, then don't do that.
        */
        if (!IsSensibleNeighbor(lNeighbor) || !IsSensibleNeighbor(rNeighbor))
        {
            return false;
        }

        /*
          in any case, we don't want to move bar lines.
        */
        if (col.GetObject(BreakAlignmentSymbol) is Item breakAlignment)
        {
            Grob staffBarGroup = BreakAlignmentInterface.FindNonemptyBreakAlignGroup(
                breakAlignment, StaffBarSymbol);
            if (staffBarGroup != null && staffBarGroup.Extent(staffBarGroup, Axis.X).Length > 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSensibleNeighbor(PaperColumn neighbor)
        => PaperColumn.IsMusical(neighbor) || PaperColumn.IsBreakable(neighbor);

    /// <summary>
    /// Records how far a loose column's two neighbours must stay apart to leave room for
    /// it, as a single rod spanning both sides of it.
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <param name="c">The loose column.</param>
    /// <param name="nextDoor">The columns on either side of it.</param>
    /// <param name="options">The spacing options.</param>
    public static void SetDistancesForLooseCol(
        Grob me,
        Grob c,
        DrulArray<Item> nextDoor,
        SpacingOptions options)
    {
        DrulArray<double> dists = new DrulArray<double>(0.0, 0.0);

        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            PaperColumn lc = (d == Direction.Negative ? nextDoor[Direction.Negative] : c) as PaperColumn;
            PaperColumn rc = (d == Direction.Negative ? c : nextDoor[Direction.Positive]) as PaperColumn;
            if (lc == null || rc == null)
            {
                continue;
            }

            IReadOnlyList<Grob> wishes = PointerGroupInterface.ExtractGrobSet(lc, SpacingWishes);
            for (int k = wishes.Count; k-- > 0;)
            {
                Grob sp = wishes[k];
                if (!ReferenceEquals(SpacingInterface.LeftColumn(sp), lc)
                    || !ReferenceEquals(SpacingInterface.RightColumn(sp), rc))
                {
                    continue;
                }

                if (sp.HasInterface(NoteSpacingInterface))
                {
                    /*
                      The note spacing should be taken from the musical
                      columns.
                    */
                    Spring baseSpring = NoteSpacingSpring(me, lc, rc, options);
                    Spring spring = NoteSpacing.GetSpacing(sp, rc, baseSpring, options.Increment);

                    dists[d] = Math.Max(dists[d], spring.MinDistance);
                }
                else if (sp.HasInterface(StaffSpacingInterface))
                {
                    Spring spring = StaffSpacing.GetSpacing(sp, rc, 0.0);

                    dists[d] = Math.Max(dists[d], spring.MinDistance);
                }
                else
                {
                    Warn.ProgrammingError("Subversive spacing wish");
                }
            }
        }

        Rod r = new Rod(nextDoor[Direction.Negative], nextDoor[Direction.Positive])
        {
            Distance = dists[Direction.Negative] + dists[Direction.Positive],
        };

        r.AddToColumns();
    }

    /*
      Remove columns that are not tightly fitting from COLS. In the
      removed columns, set 'between-cols to the columns where it is in
      between.
    */

    /// <summary>
    /// Removes the loose columns from the spacing problem, recording on each which two
    /// columns it now lives between.
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <param name="cols">The columns; loose ones are removed in place.</param>
    /// <param name="options">The spacing options.</param>
    public static void PruneLooseColumns(Grob me, List<PaperColumn> cols, SpacingOptions options)
    {
        // rp is a post-increment read index running over the cols list, wp is a
        // post-increment write index. They start in sync but become different once a
        // loose column gets pruned. The last column is kept in its own variable rather
        // than read back through rp, which may already have been overwritten via wp.
        int wp = 0;
        PaperColumn lastcol = null;
        for (int rp = 0; rp < cols.Count;)
        {
            PaperColumn c = cols[rp++];

            bool loose = lastcol != null && rp < cols.Count
                && IsLooseColumn(lastcol, c, cols[rp], options);

            /* Breakable columns never get pruned; even if they are loose,
              their broken pieces are not.  However, we mark them so that
              the spacing can take their mid-line looseness into account. */
            if (loose && PaperColumn.IsBreakable(c))
            {
                loose = false;
                c.SetProperty(MaybeLoose, true);
            }

            /*
              Unbreakable columns which only contain page-labels also
              never get pruned, otherwise the labels are lost before they can
              be collected by the System: so we mark these columns too.
            */
            if (!loose && !PaperColumn.IsBreakable(c) && c.GetProperty(LabelsSymbol) is Pair)
            {
                if (PointerGroupInterface.ExtractGrobSet(c, ElementsSymbol).Count == 0)
                {
                    c.SetProperty(MaybeLoose, true);
                }
            }

            if (loose)
            {
                Grob rightNeighbor = c.GetObject(RightNeighbor) as Grob;
                Grob leftNeighbor = c.GetObject(LeftNeighbor) as Grob;

                /*
                  Either object can be non existent, if the score ends
                  prematurely.
                */
                if (rightNeighbor == null || leftNeighbor == null)
                {
                    Warn.ProgrammingError("Cannot determine neighbors for floating column.");
                    c.SetObject(BetweenCols, new Pair(lastcol, cols[rp]));
                }
                else
                {
                    c.SetObject(BetweenCols, new Pair(leftNeighbor, rightNeighbor));

                    /*
                      Set distance constraints for loose columns
                    */
                    DrulArray<Item> nextDoor = new DrulArray<Item>(
                        leftNeighbor as Item, rightNeighbor as Item);

                    SetDistancesForLooseCol(me, c, nextDoor, options);
                }
            }
            else
            {
                cols[wp++] = c;
            }

            lastcol = c;
        }

        cols.RemoveRange(wp, cols.Count - wp);
    }

    /*
      Set neighboring columns determined by the spacing-wishes grob property.
    */

    /// <summary>
    /// Records, for every column, the nearest column its own spacing wishes reach — the
    /// explicit neighbours.
    /// </summary>
    /// <param name="cols">The columns.</param>
    public static void SetExplicitNeighborColumns(IReadOnlyList<PaperColumn> cols)
    {
        for (int i = 0; i < cols.Count; i++)
        {
            IReadOnlyList<Grob> wishes = PointerGroupInterface.ExtractGrobSet(cols[i], SpacingWishes);
            for (int j = wishes.Count; j-- > 0;)
            {
                Item wish = wishes[j] as Item;
                PaperColumn leftCol = wish?.GetColumn();
                if (leftCol == null)
                {
                    continue;
                }

                int leftRank = leftCol.Rank;
                int minRightRank = int.MaxValue;

                IReadOnlyList<Grob> rightItems
                    = PointerGroupInterface.ExtractGrobSet(wish, RightItems);
                for (int k = rightItems.Count; k-- > 0;)
                {
                    PaperColumn rightCol = (rightItems[k] as Item)?.GetColumn();
                    if (rightCol == null)
                    {
                        continue;
                    }

                    int rightRank = rightCol.Rank;

                    if (rightRank < minRightRank)
                    {
                        leftCol.SetObject(RightNeighbor, rightCol);
                        minRightRank = rightRank;
                    }

                    PaperColumn oldLeftNeighbor = rightCol.GetObject(LeftNeighbor) as PaperColumn;
                    if (oldLeftNeighbor == null || leftRank > oldLeftNeighbor.Rank)
                    {
                        rightCol.SetObject(LeftNeighbor, leftCol);
                    }
                }
            }
        }
    }

    /*
      Set neighboring columns that have no left/right-neighbor set
      yet. Only do breakable non-musical columns, and musical columns.
      Why only these? --jneem
    */

    /// <summary>
    /// Fills in the neighbours no spacing wish named, from simple adjacency.
    /// </summary>
    /// <param name="cols">The columns.</param>
    public static void SetImplicitNeighborColumns(IReadOnlyList<PaperColumn> cols)
    {
        for (int i = 0; i < cols.Count; i++)
        {
            PaperColumn it = cols[i];
            if (!PaperColumn.IsBreakable(it) && !PaperColumn.IsMusical(it))
            {
                continue;
            }

            if (i > 0 && !(cols[i].GetObject(LeftNeighbor) is Grob))
            {
                cols[i].SetObject(LeftNeighbor, cols[i - 1]);
            }

            if (i + 1 < cols.Count && !(cols[i].GetObject(RightNeighbor) is Grob))
            {
                cols[i].SetObject(RightNeighbor, cols[i + 1]);
            }
        }
    }

}
