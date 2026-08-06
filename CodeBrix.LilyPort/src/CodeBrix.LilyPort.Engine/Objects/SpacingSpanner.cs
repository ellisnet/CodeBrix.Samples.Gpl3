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
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/spacing-spanner.cc, lily/include/spacing-spanner.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// The grob that decides every horizontal distance in a score.
/// <para>
/// It does not place anything. It walks the paper columns once and records, on each
/// column, a SPRING to its neighbour (the distance the music would like) and any RODS
/// (distances collisions make compulsory). The line breaker then solves that system;
/// this class only states it.
/// </para>
/// <para>
/// The space a note gets is a function of its DURATION: doubling a duration adds one
/// <c>spacing-increment</c> — a note head's width — rather than doubling the space.
/// The most common shortest note in the piece is the reference, which is why
/// <see cref="CalcCommonShortestDuration"/> counts durations per measure before any
/// spacing is generated.
/// </para>
/// </summary>
public static partial class SpacingSpanner
{
    private static readonly Symbol SpacingWishes = Symbol.Intern("spacing-wishes");
    private static readonly Symbol RightItems = Symbol.Intern("right-items");
    private static readonly Symbol BetweenCols = Symbol.Intern("between-cols");
    private static readonly Symbol MaybeLoose = Symbol.Intern("maybe-loose");
    private static readonly Symbol GraceSpacing = Symbol.Intern("grace-spacing");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol WhenSymbol = Symbol.Intern("when");
    private static readonly Symbol ShortestStarterDuration
        = Symbol.Intern("shortest-starter-duration");

    private static readonly Symbol BaseShortestDuration = Symbol.Intern("base-shortest-duration");
    private static readonly Symbol MeasureLength = Symbol.Intern("measure-length");
    private static readonly Symbol FullMeasureExtraSpace
        = Symbol.Intern("full-measure-extra-space");

    private static readonly Symbol HorizontalSkylines = Symbol.Intern("horizontal-skylines");
    private static readonly Symbol NoteSpacingInterface = Symbol.Intern("note-spacing-interface");
    private static readonly Symbol StaffSpacingInterface = Symbol.Intern("staff-spacing-interface");

    /// <summary>
    /// Returns the columns this spanner is responsible for: every used column between
    /// its two bounds.
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <returns>The columns, in rank order.</returns>
    public static List<PaperColumn> GetColumns(Spanner me)
    {
        PaperColumn lBound = me.GetBound(Direction.Negative) as PaperColumn;
        if (lBound == null)
        {
            Warn.ProgrammingError("spanner's left bound is not a paper column");
            return new List<PaperColumn>();
        }

        PaperColumn rBound = me.GetBound(Direction.Positive) as PaperColumn;
        if (rBound == null)
        {
            Warn.ProgrammingError("spanner's right bound is not a paper column");
            return new List<PaperColumn>();
        }

        SystemGrob root = SystemGrob.GetRootSystem(me);
        return root == null
            ? new List<PaperColumn>()
            : root.UsedColumnsInRange(lBound.Rank, rBound.Rank + 1);
    }

    /// <summary>
    /// States the whole spacing problem: which columns are loose, which are neighbours,
    /// and what spring goes between each remaining pair.
    /// <para>
    /// Registered as <c>ly:spacing-spanner::set-springs</c>, and reached because
    /// <c>System::pre_processing</c> READS every grob's <c>springs-and-rods</c>
    /// property. Nothing calls this directly; the property read is the call.
    /// </para>
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    public static void SetSprings(Spanner me)
    {
        if (me == null)
        {
            throw new ArgumentNullException(nameof(me));
        }

        /*
          can't use get_system () ? --hwn.
        */
        SpacingOptions options = new SpacingOptions();
        options.InitFromGrob(me);
        List<PaperColumn> cols = GetColumns(me);
        if (cols.Count == 0)
        {
            return;
        }

        SetExplicitNeighborColumns(cols);

        PruneLooseColumns(me, cols, options);
        SetImplicitNeighborColumns(cols);
        GenerateSprings(me, cols, options);
    }

    /*
      We want the shortest note that is also "common" in the piece, so we
      find the shortest in each measure, and take the most frequently
      found duration.

      This probably gives weird effects with modern music, where every
      note has a different duration, but hey, don't write that kind of
      stuff, then.
    */

    /// <summary>
    /// Returns the duration the whole score's spacing is measured against: the shortest
    /// note of each measure, of which the most frequently occurring one wins.
    /// <para>
    /// Registered as <c>ly:spacing-spanner::calc-common-shortest-duration</c>.
    /// </para>
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <returns>The duration, as a moment.</returns>
    public static Moment CalcCommonShortestDuration(Spanner me)
    {
        if (me == null)
        {
            throw new ArgumentNullException(nameof(me));
        }

        List<PaperColumn> cols = GetColumns(me);

        /*
          ascending in duration
        */
        List<Rational> durations = new List<Rational>();
        List<int> counts = new List<int>();

        Rational shortestInMeasure = Rational.Infinity;

        for (int i = 0; i < cols.Count; i++)
        {
            if (PaperColumn.IsMusical(cols[i]))
            {
                /*
                  ignore grace notes for shortest notes.
                */
                if (cols[i].GetProperty(WhenSymbol) is Moment when && when.GracePart.IsNonZero)
                {
                    continue;
                }

                Rational thisShortest = RobustRational(
                    cols[i].GetProperty(ShortestStarterDuration), Rational.Infinity);
                shortestInMeasure = Min(shortestInMeasure, thisShortest);
            }
            else if (!shortestInMeasure.IsInfinite && PaperColumn.IsBreakable(cols[i]))
            {
                int j = 0;
                for (; j < durations.Count; j++)
                {
                    if (durations[j] > shortestInMeasure)
                    {
                        counts.Insert(j, 1);
                        durations.Insert(j, shortestInMeasure);
                        break;
                    }

                    if (durations[j] == shortestInMeasure)
                    {
                        counts[j]++;
                        break;
                    }
                }

                if (durations.Count == j)
                {
                    durations.Add(shortestInMeasure);
                    counts.Add(1);
                }

                shortestInMeasure = Rational.Infinity;
            }
        }

        int maxIdx = -1;
        int maxCount = 0;
        for (int i = durations.Count; i-- > 0;)
        {
            if (counts[i] >= maxCount)
            {
                maxIdx = i;
                maxCount = counts[i];
            }
        }

        Rational d = new Rational(1, 8);
        if (me.GetProperty(BaseShortestDuration) is Moment m)
        {
            d = m.MainPart;
        }

        if (maxIdx >= 0)
        {
            d = Min(d, durations[maxIdx]);
        }

        return new Moment(d);
    }

    /// <summary>
    /// Generates the spacing between one pair of columns, taking care of the prebroken
    /// pieces on either side.
    /// <para>
    /// A non-musical column that is allowed to FLOAT is handled by spacing straight past
    /// it, from the musical column on its left to the musical one on its right, and
    /// recording on it which two columns it now lives between. That is what
    /// <c>strict-note-spacing</c> does: bar lines and clefs stop taking part in the
    /// rhythm of the note spacing.
    /// </para>
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <param name="leftCol">The left column.</param>
    /// <param name="rightCol">The right column.</param>
    /// <param name="afterRightCol">The column after the right one, or null.</param>
    /// <param name="options">The spacing options.</param>
    public static void GeneratePairSpacing(
        Grob me,
        PaperColumn leftCol,
        PaperColumn rightCol,
        PaperColumn afterRightCol,
        SpacingOptions options)
    {
        if (PaperColumn.IsMusical(leftCol))
        {
            if (!PaperColumn.IsMusical(rightCol)
                && (options.FloatNonmusicalColumns
                    || SchemeUtilities.ToBool(rightCol.GetProperty(MaybeLoose)))
                && afterRightCol != null && PaperColumn.IsMusical(afterRightCol))
            {
                /*
                  TODO: should generate rods to prevent collisions.
                */
                MusicalColumnSpacing(me, leftCol, afterRightCol, options);
                rightCol.SetObject(BetweenCols, new Pair(leftCol, afterRightCol));
            }
            else
            {
                MusicalColumnSpacing(me, leftCol, rightCol, options);
            }

            PaperColumn rb = rightCol.FindPrebrokenPiece(Direction.Negative);
            if (rb != null)
            {
                MusicalColumnSpacing(me, leftCol, rb, options);
            }
        }
        else
        {
            /*
              The case that the right part is broken as well is rather
              rare, but it is possible, eg. with a single empty measure,
              or if one staff finishes a tad earlier than the rest.
            */
            Item lb = leftCol.FindPrebrokenPiece(Direction.Positive);
            Item rb = rightCol.FindPrebrokenPiece(Direction.Negative);

            if (leftCol != null && rightCol != null)
            {
                BreakableColumnSpacing(me, leftCol, rightCol, options);
            }

            if (lb != null && rightCol != null)
            {
                BreakableColumnSpacing(me, lb, rightCol, options);
            }

            if (leftCol != null && rb != null)
            {
                BreakableColumnSpacing(me, leftCol, rb, options);
            }

            if (lb != null && rb != null)
            {
                BreakableColumnSpacing(me, lb, rb, options);
            }
        }
    }

    /// <summary>
    /// Walks the columns once, generating the spring between each adjacent pair, then
    /// lays the collision rods over the whole run.
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <param name="cols">The columns.</param>
    /// <param name="options">The spacing options.</param>
    public static void GenerateSprings(
        Grob me,
        IReadOnlyList<PaperColumn> cols,
        SpacingOptions options)
    {
        PaperColumn prev = cols[0];
        for (int i = 1; i < cols.Count; i++)
        {
            PaperColumn col = cols[i];
            PaperColumn next = i + 1 < cols.Count ? cols[i + 1] : null;

            GeneratePairSpacing(me, prev, col, next, options);

            prev = col;
        }

        double padding = RobustDouble(prev.GetProperty(PaddingSymbol), 0.1);
        SetColumnRods(cols, padding);
    }

    /*
      Generate the space between two musical columns LEFT_COL and RIGHT_COL.
    */

    /// <summary>
    /// Generates the spring between two musical columns: the duration-based spring,
    /// adjusted by whatever note-spacing wishes the left column carries.
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <param name="leftCol">The left column.</param>
    /// <param name="rightCol">The right column.</param>
    /// <param name="options">The spacing options.</param>
    public static void MusicalColumnSpacing(
        Grob me,
        PaperColumn leftCol,
        Item rightCol,
        SpacingOptions options)
    {
        Spring spring = NoteSpacingSpring(me, leftCol, rightCol, options);

        if (options.StretchUniformly)
        {
            spring.SetMinDistance(0.0);
            spring.SetDefaultStrength();
        }
        else
        {
            List<Spring> springs = new List<Spring>();
            IReadOnlyList<Grob> wishes
                = PointerGroupInterface.ExtractGrobSet(leftCol, SpacingWishes);

            for (int i = 0; i < wishes.Count; i++)
            {
                Grob wish = wishes[i];
                if (!ReferenceEquals(SpacingInterface.LeftColumn(wish), leftCol))
                {
                    /* This shouldn't really happen, but the ancient music
                       stuff really messes up the spacing code, grrr
                    */
                    continue;
                }

                IReadOnlyList<Grob> rightItems
                    = PointerGroupInterface.ExtractGrobSet(wish, RightItems);
                bool foundMatchingColumn = false;
                for (int j = 0; j < rightItems.Count; j++)
                {
                    Item it = rightItems[j] as Item;
                    if (it != null
                        && (ReferenceEquals(rightCol, it.GetColumn())
                            || ReferenceEquals(rightCol.Original, it.GetColumn())))
                    {
                        foundMatchingColumn = true;
                    }
                }

                /*
                  This is probably a waste of time in the case of polyphonic
                  music.
                */
                if (foundMatchingColumn && wish.HasInterface(NoteSpacingInterface))
                {
                    double inc = options.Increment;
                    Grob gsp = leftCol.GetObject(GraceSpacing) as Grob;
                    if (gsp != null && PaperColumn.WhenMoment(leftCol).GracePart.IsNonZero)
                    {
                        SpacingOptions graceOpts = new SpacingOptions();
                        graceOpts.InitFromGrob(gsp);
                        inc = graceOpts.Increment;
                    }

                    springs.Add(NoteSpacing.GetSpacing(wish, rightCol, spring, inc));
                }
            }

            if (springs.Count == 0)
            {
                if (PaperColumn.IsMusical(rightCol))
                {
                    /*
                      Min distance should be 0.0. If there are no spacing
                      wishes, we're probably dealing with polyphonic spacing
                      of hemiolas.
                    */
                    spring.SetMinDistance(0.0);
                }
            }
            else
            {
                spring = Spring.Merge(springs);
            }
        }

        if (PaperColumn.WhenMoment(rightCol).GracePart.IsNonZero
            && !PaperColumn.WhenMoment(leftCol).GracePart.IsNonZero)
        {
            /*
              Ugh. 0.8 is arbitrary.
            */
            spring *= 0.8;
        }

        /*
          TODO: make sure that the space doesn't exceed the right margin.
        */
        if (options.Packed)
        {
            /*
              In packed mode, pack notes as tight as possible.  This makes
              sense mostly in combination with ragged-right mode: the notes
              are then printed at minimum distance.  This is mostly useful
              for ancient notation, but may also be useful for some flavours
              of contemporary music.  If not in ragged-right mode, lily will
              pack as many bars of music as possible into a line, but the
              line will then be stretched to fill the whole linewidth.

              Note that we don't actually pack things as tightly as possible:
              we don't allow the next column to begin before this one ends.
            */
            /* FIXME: the else clause below is the "right" thing to do,
               but we can't do it because of all the empty columns that the
               ligature-engravers leave lying around. In that case, the extent of
               the column is incorrect because it includes note-heads that aren't
               there. We get around this by only including the column extent if
               the left-hand column is "genuine". This is a dirty hack and it
               should be fixed in the ligature-engravers. --jneem
            */
            if (PaperColumn.IsExtraneousColumnFromLigature(leftCol))
            {
                spring.SetIdealDistance(spring.MinDistance);
            }
            else
            {
                spring.SetIdealDistance(Math.Max(
                    leftCol.Extent(leftCol, Axis.X).Right, spring.MinDistance));
            }

            spring.SetInverseStretchStrength(1.0);
        }

        SpaceableGrob.AddSpring(leftCol, rightCol, spring);
    }

    /*
      Check if COL fills the whole measure.
    */

    /// <summary>Determines whether a column takes up the rest of its measure.</summary>
    /// <param name="me">The spacing spanner.</param>
    /// <param name="left">The column on the left.</param>
    /// <param name="col">The column to test.</param>
    /// <returns><see langword="true"/> when the column fills the measure.</returns>
    public static bool FillsMeasure(Grob me, Item left, Item col)
    {
        SystemGrob sys = SystemGrob.GetRootSystem(me);
        PaperColumn ownColumn = col.GetColumn();
        Item next = sys == null || ownColumn == null ? null : sys.Column(ownColumn.Rank + 1);
        if (next == null)
        {
            return false;
        }

        if (PaperColumn.IsMusical(next) || PaperColumn.IsMusical(left)
            || !PaperColumn.IsMusical(col) || !PaperColumn.IsUsed(next))
        {
            return false;
        }

        Moment dt = PaperColumn.WhenMoment(next) - PaperColumn.WhenMoment(col);

        if (!(left.GetProperty(MeasureLength) is Moment len))
        {
            return false;
        }

        /*
          Don't check for exact measure length, since ending measures are
          often shortened due to pickups.
        */
        if (dt.MainPart > len.MainPart / new Rational(2)
            && (next.IsBroken || next.BreakStatusDirection().IsNonZero))
        {
            return true;
        }

        return false;
    }

    /*
      Read hints from L and generate springs.
    */

    /// <summary>
    /// Generates the spring across a non-musical column, from whatever staff-spacing
    /// wishes it carries, or from the standard rule when it carries none.
    /// </summary>
    /// <param name="me">The spacing spanner.</param>
    /// <param name="l">The left column.</param>
    /// <param name="r">The right column.</param>
    /// <param name="options">The spacing options.</param>
    public static void BreakableColumnSpacing(Grob me, Item l, Item r, SpacingOptions options)
    {
        List<Spring> springs = new List<Spring>();
        Spring spring;

        double fullMeasureSpace = 0.0;
        if (PaperColumn.IsMusical(r) && !l.BreakStatusDirection().IsNonZero
            && FillsMeasure(me, l, r))
        {
            fullMeasureSpace = RobustDouble(l.GetProperty(FullMeasureExtraSpace), 1.0);
        }

        Moment dt = PaperColumn.WhenMoment(r) - PaperColumn.WhenMoment(l);

        if (dt == Moment.Zero)
        {
            IReadOnlyList<Grob> wishes = PointerGroupInterface.ExtractGrobSet(l, SpacingWishes);

            for (int i = 0; i < wishes.Count; i++)
            {
                Item spacingGrob = wishes[i] as Item;

                if (spacingGrob == null || !spacingGrob.HasInterface(StaffSpacingInterface))
                {
                    continue;
                }

                /*
                  column for the left one settings should be ok due automatic
                  pointer munging.
                */
                if (!ReferenceEquals(spacingGrob.GetColumn(), l))
                {
                    Warn.ProgrammingError(
                        "staff spacing wish is not in the column it was collected from");
                    continue;
                }

                springs.Add(StaffSpacing.GetSpacing(spacingGrob, r, fullMeasureSpace));
            }
        }

        if (springs.Count == 0)
        {
            spring = StandardBreakableColumnSpacing(me, l, r, options);
        }
        else
        {
            spring = Spring.Merge(springs);
        }

        if (PaperColumn.WhenMoment(r).GracePart.IsNonZero)
        {
            /*
              Correct for grace notes.

              Ugh. The 0.8 is arbitrary.
            */
            spring *= 0.8;
        }

        if (options.StretchUniformly && l.BreakStatusDirection() != Direction.Positive)
        {
            spring.SetMinDistance(0.0);
            spring.SetDefaultStrength();
        }

        SpaceableGrob.AddSpring(l, r, spring);
    }

    /// <summary>
    /// Lays the collision rods: for every column, the minimum distances to each column
    /// to its left that can still reach it.
    /// <para>
    /// The inner loop looks quadratic and is not. It stops as soon as the columns to the
    /// left can no longer overhang far enough to reach this one, which for real music is
    /// after a constant number of steps — the whole reason the overhang running total is
    /// carried at all.
    /// </para>
    /// </summary>
    /// <param name="cols">The columns.</param>
    /// <param name="padding">The padding to insist on beyond touching.</param>
    private static void SetColumnRods(IReadOnlyList<PaperColumn> cols, double padding)
    {
        /* distances[i] will be the distance betwen cols[i-1] and cols[i], and
           overhangs[j] the amount by which cols[0 thru j] extend beyond cols[j]
           when each column is placed as far to the left as possible. */
        double[] distances = new double[cols.Count];
        double[] overhangs = new double[cols.Count];

        for (int i = 0; i < cols.Count; i++)
        {
            PaperColumn r = cols[i];
            Item rb = r.FindPrebrokenPiece(Direction.Negative);

            if (SeparationItem.IsEmpty(r) && (rb == null || SeparationItem.IsEmpty(rb)))
            {
                continue;
            }

            SkylinePair skyp = SkylinePair.FromScheme(r.GetProperty(HorizontalSkylines));
            bool skypOk = skyp != null;
            overhangs[i] = skypOk ? skyp[Direction.Positive].MaxHeight() : 0.0;

            if (i == 0)
            {
                continue;
            }

            /* min rather than max because stickout will be negative if the right-hand column
               sticks out a lot to the left */
            double stickout = Math.Min(
                skypOk ? skyp[Direction.Negative].MaxHeight() : 0.0,
                SeparationItem.ConditionalSkyline(r, cols[i - 1]).MaxHeight());

            double prevDistances = 0.0;

            /* This is an inner loop and hence it is potentially quadratic. However, we only continue
               as long as there is a rod to insert. Therefore, this loop will usually only execute
               a constant number of times per iteration of the outer loop. */
            for (int j = i; j-- > 0;)
            {
                if (overhangs[j] + padding <= prevDistances + distances[i] + stickout)
                {
                    break; // cols[0 thru j] cannot reach cols[i]
                }

                PaperColumn l = cols[j];
                Item lb = l.FindPrebrokenPiece(Direction.Positive);

                double dist = SeparationItem.SetDistance(l, r, padding);
                distances[i] = Math.Max(distances[i], dist - prevDistances);

                if (lb != null)
                {
                    dist = SeparationItem.SetDistance(lb, r, padding);

                    // The left-broken version might reach more columns to the
                    // right than the unbroken version, by extending farther and/or
                    // nesting more closely;
                    if (j == i - 1)
                    {
                        // check this, the first time we see each lb.
                        overhangs[j] = Math.Max(
                            overhangs[j],
                            lb.Extent(lb, Axis.X).Right + distances[i] - dist);
                    }
                }

                if (rb != null)
                {
                    SeparationItem.SetDistance(l, rb, padding);
                }

                if (lb != null && rb != null)
                {
                    SeparationItem.SetDistance(lb, rb, padding);
                }

                prevDistances += distances[j];
            }

            overhangs[i] = Math.Max(overhangs[i], overhangs[i - 1] - distances[i]);
        }
    }

    private static Rational Min(Rational a, Rational b) => a < b ? a : b;

    private static Rational RobustRational(object value, Rational fallback)
    {
        switch (value)
        {
            case Rational rational:
                return rational;
            case Moment moment:
                return moment.MainPart;
            default:
                return SchemeConvert.IsNumber(value)
                    ? SchemeConvert.ToRational(value, "duration")
                    : fallback;
        }
    }

    private static double RobustDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "spacing spanner")
            : fallback;
}
