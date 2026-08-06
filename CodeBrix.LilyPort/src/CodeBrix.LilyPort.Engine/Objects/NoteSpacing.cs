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

    private static bool _stemAbsenceReported;
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
    /// NAMED ABSENCE, recorded in PORT-COVERAGE: every measurement here comes off a
    /// note column's STEM, and stems are EPG6 (note columns EPG5). The structure is
    /// ported in full — including the bar-line substitution, which needs no stem — and
    /// the moment a real note column turns up the method takes upstream's own
    /// give-up path, which leaves the space unchanged.
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

                // Everything from here on reads the note column's stem, which is EPG6.
                // Upstream RETURNS from this method the moment it cannot determine a
                // stem, leaving *space untouched; that is the path taken here.
                if (!_stemAbsenceReported)
                {
                    _stemAbsenceReported = true;
                    Warn.ProgrammingError(
                        "Note_spacing::stem_dir_correction needs Note_column (EPG5) and"
                        + " Stem (EPG6); taking upstream's own undeterminable-stem exit,"
                        + " which leaves the space unchanged");
                }

                return;
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
            // The knee case needs the right-hand STEM (EPG6); with no note columns
            // reachable this branch cannot be entered, because both stem directions are
            // still zero and directed_opposite is false for them.
            correction = DifferentDirectionsCorrection(me, stemPosns, stemDirs[Direction.Negative]);

            if (!barYextent.IsEmpty)
            {
                correction *= 0.5;
            }
        }

        /*
          Only apply same direction correction if there are no
          accidentals sticking out of the right hand side.
        */
        else if (Direction.DirectedSame(stemDirs[Direction.Negative], stemDirs[Direction.Positive]))
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
