/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2001--2026  Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/note-spacing.cc, lily/include/note-spacing.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.
// Modified by Jeremy Ellis on 2026-08-11 as part of the CodeBrix port:
//   - stem_dir_correction's stem loop is ported in full, knee_correction included.
//     It had been hollow behind a stale EPG5/EPG6 absence note, and became reachable
//     when Paper_column_engraver started acknowledging spacing wishes onto the
//     columns. See PORT-COVERAGE, STAFF-LINES.

/*
  Adjust the ideal and minimum distance between note columns,
  based on the notehead size, skylines, and optical illusions.
*/

/// <summary>
/// The spacing wish one voice states between two musical columns.
/// <para>
/// It starts from the duration-based spring the spacing spanner computed and adjusts
/// it for what is actually being drawn: the width of the note head (a quarter rest
/// wants noticeably less room than a note), the true skyline distance, and the optical
/// illusions that up-stem/down-stem pairs create.
/// </para>
/// </summary>
public static class NoteSpacing
{
    private static readonly Symbol RestSymbol = Symbol.Intern("rest");
    private static readonly Symbol LeftItems = Symbol.Intern("left-items");
    private static readonly Symbol RightItems = Symbol.Intern("right-items");
    private static readonly Symbol SkylineVerticalPadding
        = Symbol.Intern("skyline-vertical-padding");

    private static readonly Symbol SpaceToBarline = Symbol.Intern("space-to-barline");
    private static readonly Symbol BreakAlignment = Symbol.Intern("break-alignment");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol StemSpacingCorrection
        = Symbol.Intern("stem-spacing-correction");

    private static readonly Symbol SameDirectionCorrection
        = Symbol.Intern("same-direction-correction");

    private static readonly Symbol KneeSpacingCorrection
        = Symbol.Intern("knee-spacing-correction");

    private static bool _headAbsenceReported;
    private static bool _staffBarGroupAbsenceReported;

    /// <summary>
    /// Adjusts a duration-based spring for what the two columns actually contain.
    /// </summary>
    /// <param name="me">The note spacing wish.</param>
    /// <param name="rightCol">The column on the right.</param>
    /// <param name="baseSpring">The spring from the duration alone.</param>
    /// <param name="increment">One note head's width.</param>
    /// <returns>The adjusted spring.</returns>
    public static Spring GetSpacing(Grob me, Item rightCol, Spring baseSpring, double increment)
    {
        List<Item> noteColumns = SpacingInterface.LeftNoteColumns(me);
        double leftHeadEnd = 0;

        for (int i = 0; i < noteColumns.Count; i++)
        {
            Grob g = noteColumns[i].GetObject(RestSymbol) as Grob;
            Grob col = noteColumns[i].GetColumn();

            if (g == null)
            {
                g = FirstHead(noteColumns[i]);
            }

            /*
              Ugh. If Stem is switched off, we don't know what the
              first note head will be.
            */
            if (g != null)
            {
                if (!g.HasInAncestry(col, Axis.X))
                {
                    Warn.ProgrammingError(
                        "Note_spacing::get_spacing (): Common refpoint incorrect");
                }
                else
                {
                    leftHeadEnd = g.Extent(col, Axis.X).Right;
                }
            }
        }

        /*
          The main factor that determines the amount of space is the width of the
          note head (or the rest). For example, a quarter rest gets almost 0.5 ss
          less horizontal space than a note.
        */
        double ideal = baseSpring.IdealDistance - increment + leftHeadEnd;
        DrulArray<Skyline> skys = SpacingInterface.Skylines(me, rightCol);
        double distance = skys[Direction.Negative].Distance(
            skys[Direction.Positive],
            RobustDouble(rightCol.GetProperty(SkylineVerticalPadding), 0.0));
        double minDist = Math.Max(0.0, distance);
        baseSpring.SetMinDistance(minDist);

        /* If we have a NonMusical column on the right, we measure the ideal distance
           to the bar-line (if present), not the start of the column. */
        if (!PaperColumn.IsMusical(rightCol)
            && !skys[Direction.Positive].IsEmpty
            && SchemeUtilities.ToBool(me.GetProperty(SpaceToBarline)))
        {
            Grob staffBarGroup = null;
            if (rightCol.GetObject(BreakAlignment) is Item breakAlignment)
            {
                staffBarGroup = FindStaffBarGroup(breakAlignment);
            }

            if (staffBarGroup != null)
            {
                ideal -= staffBarGroup.Extent(rightCol, Axis.X).Left;
            }
            else
            {
                /* Measure ideal distance to the right side of the NonMusical column
                   but keep at least half the gap we would have had to a note */
                double minDesiredSpace = (ideal + minDist) / 2.0;
                ideal -= rightCol.Extent(rightCol, Axis.X).Right;
                ideal = Math.Max(ideal, minDesiredSpace);
            }
        }

        StemDirCorrection(me, rightCol, increment, ref ideal);

        baseSpring.SetIdealDistance(Math.Max(0.0, ideal));
        return baseSpring;
    }

    /*
      Correct for optical illusions. See [Wanske] p. 138. The combination
      up-stem + down-stem should get extra space, the combination
      down-stem + up-stem less.

      TODO: have to check whether the stems are in the same staff.
    */

    /// <summary>
    /// Adds the optical correction that up-stem/down-stem pairs need: the combination
    /// up-then-down wants extra room, down-then-up less.
    /// <para>
    /// The EPG5/EPG6-era named absence here retired with the STAFF-LINES session
    /// (2026-08-11): the stem loop was hollow — its callees had all long since landed —
    /// and it became reachable the moment <c>Paper_column_engraver</c> started
    /// acknowledging spacing wishes onto the columns.
    /// </para>
    /// </summary>
    /// <param name="me">The note spacing wish.</param>
    /// <param name="rcolumn">The column on the right.</param>
    /// <param name="increment">One note head's width.</param>
    /// <param name="space">The space to correct.</param>
    public static void StemDirCorrection(Grob me, Item rcolumn, double increment, ref double space)
    {
        DrulArray<Direction> stemDirs = new DrulArray<Direction>(Direction.Zero, Direction.Zero);
        DrulArray<Interval> stemPosns = new DrulArray<Interval>(Interval.Empty, Interval.Empty);
        DrulArray<Interval> headPosns = new DrulArray<Interval>(Interval.Empty, Interval.Empty);
        DrulArray<IReadOnlyList<Grob>> props = new DrulArray<IReadOnlyList<Grob>>(
            PointerGroupInterface.ExtractGrobSet(me, LeftItems),
            PointerGroupInterface.ExtractGrobSet(me, RightItems));

        DrulArray<Spanner> beamsDrul = new DrulArray<Spanner>(null, null);
        DrulArray<Grob> stemsDrul = new DrulArray<Grob>(null, null);

        bool accRight = false;

        Interval barXextent = default;
        Interval barYextent = default;
        barYextent.SetEmpty();

        Grob bar = SpacingInterface.ExtremalBreakAlignedGrob(
            me, Direction.Positive, rcolumn.BreakStatusDirection(), ref barXextent);
        if (bar != null && ReferenceEquals((bar as Item)?.GetColumn(), rcolumn))
        {
            barYextent = StaffSpacing.BarYPositions(bar);
        }

        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            IReadOnlyList<Grob> items = props[d];

            for (int i = 0; i < items.Count; i++)
            {
                Item it = items[i] as Item;
                if (it == null || !it.HasInterface(NoteColumnInterface))
                {
                    continue;
                }

                if (d == Direction.Positive && !ReferenceEquals(it.GetColumn(), rcolumn))
                {
                    continue;
                }

                /*
                  Find accidentals which are sticking out of the right side.
                */
                if (d == Direction.Positive)
                {
                    accRight = accRight || NoteColumn.Accidentals(it) != null;
                }

                Grob stem = NoteColumn.GetStem(it);

                if (stem == null || !stem.IsLive || Stem.IsInvisible(stem))
                {
                    return;
                }

                stemsDrul[d] = stem;
                beamsDrul[d] = Stem.GetBeam(stem);

                Direction stemDir = DirectionalElementInterface.GetGrobDirection(stem);
                if (stemDirs[d].IsNonZero && stemDirs[d] != stemDir)
                {
                    return;
                }

                stemDirs[d] = stemDir;

                /*
                  Correction doesn't seem appropriate  when there is a large flag
                  hanging from the note.
                */
                if (d == Direction.Negative && Stem.DurationLog(stem) > 2
                    && Stem.GetBeam(stem) == null)
                {
                    return;
                }

                Interval hp = Stem.HeadPositions(stem);
                if (!hp.IsEmpty)
                {
                    double ss = StaffSymbolReferencer.StaffSpace(stem);
                    // The read is PURE (upstream: `stem->pure_y_extent (stem, 0,
                    // INT_MAX)`) — this runs during horizontal spacing, before line
                    // breaking, where an ordinary extent would ask for a stencil.
                    stemPosns[d] = stem.PureYExtent(stem, 0, int.MaxValue) * (2 / ss);

                    Interval united = headPosns[d];
                    united.Unite(hp);
                    headPosns[d] = united;
                }
            }
        }

        double correction = 0.0;

        if (!barYextent.IsEmpty)
        {
            stemDirs[Direction.Positive] = -stemDirs[Direction.Negative];
            stemPosns[Direction.Positive] = barYextent;
            stemPosns[Direction.Positive] *= 2;
        }

        if (Direction.DirectedOpposite(stemDirs[Direction.Negative], stemDirs[Direction.Positive]))
        {
            if (beamsDrul[Direction.Negative] != null
                && ReferenceEquals(beamsDrul[Direction.Negative], beamsDrul[Direction.Positive]))
            {
                correction = KneeCorrection(me, stemsDrul[Direction.Positive], increment);
            }
            else
            {
                correction = DifferentDirectionsCorrection(
                    me, stemPosns, stemDirs[Direction.Negative]);

                if (!barYextent.IsEmpty)
                {
                    correction *= 0.5;
                }
            }
        }

        /*
          Only apply same direction correction if there are no
          accidentals sticking out of the right hand side.
        */
        else if (Direction.DirectedSame(stemDirs[Direction.Negative], stemDirs[Direction.Positive])
            && !accRight)
        {
            correction = SameDirectionCorrectionOf(me, headPosns);
        }

        space += correction;

        /* there used to be a correction for bar_xextent () here, but
           it's unclear what that was good for ?
        */
    }

    private static double DifferentDirectionsCorrection(
        Grob noteSpacing,
        DrulArray<Interval> stemPosns,
        Direction leftStemDir)
    {
        double ret = 0.0;
        Interval intersect = stemPosns[Direction.Negative];
        intersect.Intersect(stemPosns[Direction.Positive]);

        if (!intersect.IsEmpty)
        {
            ret = Math.Abs(intersect.Length);

            /*
              Ugh. 7 is hardcoded.
            */
            ret = Math.Min(ret / 7, 1.0) * leftStemDir
                * RobustDouble(noteSpacing.GetProperty(StemSpacingCorrection), 0);
        }

        return ret;
    }

    private static double KneeCorrection(Grob noteSpacing, Grob rightStem, double increment)
    {
        double noteHeadWidth = increment;
        Item head = rightStem != null ? Stem.SupportHead(rightStem) as Item : null;

        if (head != null)
        {
            Interval headExtent = head.Extent(head.GetColumn(), Axis.X);

            if (!headExtent.IsEmpty)
            {
                noteHeadWidth = headExtent[Direction.Positive];
            }

            noteHeadWidth -= Stem.Thickness(rightStem);
        }

        return -noteHeadWidth * DirectionalElementInterface.GetGrobDirection(rightStem)
            * RobustDouble(noteSpacing.GetProperty(KneeSpacingCorrection), 0);
    }

    /*
      Correct for the following situation:

      X      X
      |      |
      |      |
      |   X  |
      |  |   |
      ========

      ^ move the center one to the left.


      this effect seems to be much more subtle than the
      stem-direction stuff (why?), and also does not scale with the
      difference in stem length.
    */
    private static double SameDirectionCorrectionOf(Grob noteSpacing, DrulArray<Interval> headPosns)
    {
        Interval hp = headPosns[Direction.Negative];
        hp.Intersect(headPosns[Direction.Positive]);
        if (!hp.IsEmpty)
        {
            return 0;
        }

        Direction lowest
            = headPosns[Direction.Negative][Direction.Negative]
                > headPosns[Direction.Positive][Direction.Positive]
                ? Direction.Positive
                : Direction.Negative;

        double delta = headPosns[-lowest][Direction.Negative] - headPosns[lowest][Direction.Positive];
        double corr = RobustDouble(noteSpacing.GetProperty(SameDirectionCorrection), 0);

        return delta > 1 ? -lowest * corr : 0;
    }

    /// <summary>
    /// The seam for <c>Note_column::first_head</c>, which is EPG5's. Nothing carries
    /// <c>note-column-interface</c> yet, so this is only ever reached with a grob that
    /// cannot be one.
    /// </summary>
    private static Grob FirstHead(Item noteColumn)
    {
        if (!_headAbsenceReported)
        {
            _headAbsenceReported = true;
            Warn.ProgrammingError(
                "Note_column::first_head is not ported (EPG5); the left head end stays 0,"
                + " which is upstream's answer when the first head cannot be determined");
        }

        return null;
    }

    /// <summary>
    /// The seam for <c>Break_alignment_interface::find_nonempty_break_align_group</c>,
    /// which is EPG8's. Until it lands the spacing measures to the right side of the
    /// non-musical column instead — upstream's own fallback when no staff-bar group is
    /// found.
    /// </summary>
    private static Grob FindStaffBarGroup(Item breakAlignment)
    {
        if (!_staffBarGroupAbsenceReported)
        {
            _staffBarGroupAbsenceReported = true;
            Warn.ProgrammingError(
                "Break_alignment_interface::find_nonempty_break_align_group is not ported"
                + " (EPG8); measuring to the right side of the non-musical column, which is"
                + " upstream's own no-staff-bar-group fallback");
        }

        return null;
    }

    private static double RobustDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "note spacing")
            : fallback;
}
