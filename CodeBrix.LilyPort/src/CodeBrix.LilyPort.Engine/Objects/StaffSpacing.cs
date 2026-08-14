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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/staff-spacing.cc, lily/include/staff-spacing.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - optical_correction is ported in full (it answered 0 behind a stale early
//     absence note); its stem read is the PURE extent, as upstream. See
//     PORT-COVERAGE.

/// <summary>
/// The spacing wish that runs from a prefatory symbol — a clef, a key signature, a bar
/// line — to whatever follows it.
/// <para>
/// It is read out of the <c>space-alist</c> of the break-aligned grob it starts at, so
/// the distance a clef asks for after itself is declared in Scheme rather than
/// computed here. What IS computed here is the optical correction that keeps a
/// down-stem from looking glued to the bar line before it.
/// </para>
/// </summary>
public static class StaffSpacing
{
    private static readonly Symbol BarLineInterface = Symbol.Intern("bar-line-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol GlyphName = Symbol.Intern("glyph-name");
    private static readonly Symbol SpaceAlist = Symbol.Intern("space-alist");
    private static readonly Symbol FirstNote = Symbol.Intern("first-note");
    private static readonly Symbol NextNote = Symbol.Intern("next-note");
    private static readonly Symbol FixedSpace = Symbol.Intern("fixed-space");
    private static readonly Symbol ExtraSpace = Symbol.Intern("extra-space");
    private static readonly Symbol SemiFixedSpace = Symbol.Intern("semi-fixed-space");
    private static readonly Symbol MinimumSpace = Symbol.Intern("minimum-space");
    private static readonly Symbol MinimumFixedSpace = Symbol.Intern("minimum-fixed-space");
    private static readonly Symbol ShrinkSpace = Symbol.Intern("shrink-space");
    private static readonly Symbol SemiShrinkSpace = Symbol.Intern("semi-shrink-space");

    private static readonly Symbol StemSpacingCorrectionSymbol
        = Symbol.Intern("stem-spacing-correction");

    /* A stem following a bar-line creates an optical illusion similar to the
       one mentioned in note-spacing.cc. We correct for it here.

       TODO: should we still correct if there are accidentals/arpeggios before
       the stem?
    */

    /// <summary>
    /// Returns how much extra space a down-stem needs after a bar line, so that the two
    /// verticals do not read as crowded.
    /// <para>
    /// The early named absence here retired with the stale-stand-in class
    /// sweep: its callees had all long since landed, and it became reachable
    /// the moment <c>Paper_column_engraver</c> started acknowledging spacing wishes
    /// onto the columns. The stem read is PURE (upstream:
    /// <c>stem-&gt;pure_y_extent (stem, 0, INT_MAX)</c>) because this runs during
    /// horizontal spacing, before line breaking.
    /// </para>
    /// </summary>
    /// <param name="me">The staff spacing wish.</param>
    /// <param name="g">The note column following the bar line.</param>
    /// <param name="barHeight">The vertical span the bar line covers.</param>
    /// <returns>The correction.</returns>
    public static double OpticalCorrection(Grob me, Grob g, Interval barHeight)
    {
        if (g == null || !g.HasInterface(NoteColumnInterface))
        {
            return 0;
        }

        Grob stem = NoteColumn.GetStem(g);
        double ret = 0.0;

        if (!barHeight.IsEmpty && stem != null)
        {
            Direction d = DirectionalElementInterface.GetGrobDirection(stem);
            if (Stem.IsNormalStem(stem) && d == Direction.Negative)
            {
                Interval stemPosns = stem.PureYExtent(stem, 0, int.MaxValue);

                stemPosns.Intersect(barHeight);

                ret = Math.Min(Math.Abs(stemPosns.Length / 7.0), 1.0);
                ret *= Bootstrap.SchemeConvert.IsNumber(
                        me.GetProperty(StemSpacingCorrectionSymbol))
                    ? Bootstrap.SchemeConvert.ToDouble(
                        me.GetProperty(StemSpacingCorrectionSymbol), "stem-spacing-correction")
                    : 1.0;
            }
        }

        return ret;
    }

    /*
      Y-positions that are covered by BAR_GROB, in the case that it is a
      barline.
    */

    /// <summary>
    /// Returns the staff positions a bar line covers, in staff spaces, or an empty
    /// interval when the grob is not a plain bar line.
    /// </summary>
    /// <param name="barGrob">The candidate bar line.</param>
    /// <returns>The covered positions.</returns>
    public static Interval BarYPositions(Grob barGrob)
    {
        Interval barSize = default;
        barSize.SetEmpty();

        if (barGrob != null && barGrob.HasInterface(BarLineInterface))
        {
            object glyph = barGrob.GetProperty(GlyphName);
            Grob staffSym = StaffSymbolReferencer.GetStaffSymbol(barGrob);

            string glyphString = glyph is MutableString text
                ? text.ToString()
                : glyph as string ?? string.Empty;
            if (glyphString.StartsWith("|", StringComparison.Ordinal)
                || glyphString.StartsWith(".", StringComparison.Ordinal))
            {
                Grob common = barGrob.CommonRefpoint(staffSym, Axis.Y);
                barSize = barGrob.Extent(common, Axis.Y);
                barSize *= 1.0 / StaffSymbolReferencer.StaffSpace(barGrob);
            }
        }

        return barSize;
    }

    /// <summary>
    /// Returns the largest optical correction any of the note columns to the right of a
    /// wish asks for.
    /// </summary>
    /// <param name="me">The staff spacing wish.</param>
    /// <param name="lastGrob">The break-aligned grob the spacing starts at.</param>
    /// <returns>The correction.</returns>
    public static double NextNotesCorrection(Grob me, Grob lastGrob)
    {
        Interval barSize = BarYPositions(lastGrob);
        Grob orig = me.Original ?? me;
        List<Item> noteColumns = SpacingInterface.RightNoteColumns(orig);

        double maxOptical = 0.0;

        for (int i = 0; i < noteColumns.Count; i++)
        {
            maxOptical = Math.Max(maxOptical, OpticalCorrection(me, noteColumns[i], barSize));
        }

        return maxOptical;
    }

    /* We calculate three things here: the ideal distance, the minimum distance
       (which is the distance at which collisions will occur) and the "fixed"
       distance, which is the distance at which things start to look really bad.
       We arrange things so that the fixed distance will be attained when the
       line is compressed with a force of 1.0 */

    /// <summary>
    /// Returns the spring from a prefatory symbol to what follows it.
    /// <para>
    /// Three distances come out of this, not two: the ideal, the minimum at which things
    /// collide, and a FIXED distance at which the result starts to look bad. The spring
    /// is then given the compress strength that makes the fixed distance the one reached
    /// at a compression force of exactly 1.
    /// </para>
    /// </summary>
    /// <param name="me">The staff spacing wish.</param>
    /// <param name="rightCol">The column on the right.</param>
    /// <param name="situationalSpace">Extra space the caller asks for, such as
    /// <c>full-measure-extra-space</c>.</param>
    /// <returns>The spring.</returns>
    public static Spring GetSpacing(Grob me, Grob rightCol, double situationalSpace)
    {
        Item meItem = me as Item;
        if (meItem == null)
        {
            return Spring.Default;
        }

        Grob leftCol = meItem.GetColumn();

        Interval lastExt = default;
        Direction breakDir = meItem.BreakStatusDirection();
        Grob lastGrob = SpacingInterface.ExtremalBreakAlignedGrob(
            me, Direction.Negative, breakDir, ref lastExt);
        if (lastGrob == null)
        {
            /*
              TODO:

              Should insert an adjustable space here? For exercises, you might want to
              use a staff without a clef in the beginning.
            */

            /*
              we used to have a warning here, but it generates a lot of
              spurious error messages.
            */
            return Spring.Default;
        }

        object alist = lastGrob.GetProperty(SpaceAlist);
        if (!(alist is Pair) && !(alist is Nil))
        {
            return Spring.Default;
        }

        Pair spaceDef = SchemeUtilities.Assq(FirstNote, alist);
        if (!meItem.BreakStatusDirection().IsNonZero)
        {
            Pair nndef = SchemeUtilities.Assq(NextNote, alist);
            if (nndef != null)
            {
                spaceDef = nndef;
            }
        }

        if (spaceDef == null || !(spaceDef.Cdr is Pair definition))
        {
            Warn.ProgrammingError("unknown prefatory spacing");
            return Spring.Default;
        }

        double distance = SchemeConvert.IsNumber(definition.Cdr)
            ? SchemeConvert.ToDouble(definition.Cdr, "space-alist distance")
            : 0.0;
        object type = definition.Car;
        bool isStretchable = true;

        double fixedDistance = lastExt.Right;
        double ideal = fixedDistance + 1.0;

        if (ReferenceEquals(type, FixedSpace))
        {
            fixedDistance += distance;
            ideal = fixedDistance;
        }
        else if (ReferenceEquals(type, ExtraSpace))
        {
            ideal = fixedDistance + distance;
        }
        else if (ReferenceEquals(type, SemiFixedSpace))
        {
            fixedDistance += distance / 2;
            ideal = fixedDistance + (distance / 2);
        }
        else if (ReferenceEquals(type, MinimumSpace))
        {
            ideal = lastExt.Left + Math.Max(lastExt.Length, distance);
        }
        else if (ReferenceEquals(type, MinimumFixedSpace))
        {
            fixedDistance = lastExt.Left + Math.Max(lastExt.Length, distance);
            ideal = fixedDistance;
        }
        else if (ReferenceEquals(type, ShrinkSpace))
        {
            ideal = fixedDistance + distance;
            isStretchable = false;
        }
        else if (ReferenceEquals(type, SemiShrinkSpace))
        {
            fixedDistance += distance / 2;
            ideal = fixedDistance + (distance / 2);
            isStretchable = false;
        }

        double stretchability = isStretchable ? ideal - fixedDistance : 0;

        /* 'situational_space' passed by the caller
            could include full-measure-extra-space */
        ideal += situationalSpace;

        double opticalCorrection = NextNotesCorrection(me, lastGrob);
        fixedDistance += opticalCorrection;
        ideal += opticalCorrection;

        double minDist = PaperColumn.MinimumDistance(leftCol, rightCol);

        /* ensure that the "fixed" distance will leave a gap of at least 0.3 ss. */
        double minDistCorrection = Math.Max(0.0, 0.3 + minDist - fixedDistance);
        fixedDistance += minDistCorrection;
        ideal = Math.Max(ideal, fixedDistance);

        Spring ret = new Spring(ideal, minDist);
        ret.SetInverseStretchStrength(Math.Max(0.0, stretchability));
        ret.SetInverseCompressStrength(Math.Max(0.0, ideal - fixedDistance));
        return ret;
    }
}
