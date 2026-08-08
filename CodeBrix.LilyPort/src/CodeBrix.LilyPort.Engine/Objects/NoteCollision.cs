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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/note-collision.cc, lily/include/note-collision.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The object that resolves clashes between note columns with OPPOSITE stem directions:
/// merging identical heads where the style rules allow it, and shifting whole columns
/// sideways where they do not.
/// </summary>
public static class NoteCollisionInterface
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol DotSymbol = Symbol.Intern("dot");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol TransparentSymbol = Symbol.Intern("transparent");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol HorizontalShiftSymbol = Symbol.Intern("horizontal-shift");
    private static readonly Symbol ForceHshiftSymbol = Symbol.Intern("force-hshift");
    private static readonly Symbol XOffsetSymbol = Symbol.Intern("X-offset");
    private static readonly Symbol DotCountSymbol = Symbol.Intern("dot-count");
    private static readonly Symbol StemAttachmentSymbol = Symbol.Intern("stem-attachment");
    private static readonly Symbol NoteCollisionThreshold
        = Symbol.Intern("note-collision-threshold");
    private static readonly Symbol MergeDifferentlyDotted
        = Symbol.Intern("merge-differently-dotted");
    private static readonly Symbol MergeDifferentlyHeaded
        = Symbol.Intern("merge-differently-headed");
    private static readonly Symbol PreferDottedRight = Symbol.Intern("prefer-dotted-right");
    private static readonly Symbol FaStylesSymbol = Symbol.Intern("fa-styles");
    private static readonly Symbol FaMergeDirectionSymbol = Symbol.Intern("fa-merge-direction");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol DotColumnInterface = Symbol.Intern("dot-column-interface");
    private static readonly Symbol XParentPositioningSymbol
        = Symbol.Intern("ly:grob::x-parent-positioning");

    /// <summary>
    /// Decides how far the two INNERMOST clashing chords must move — the up-stem and
    /// down-stem columns closest to each other — and merges or wipes heads and dots
    /// where the merge rules say the two chords can share ink.
    /// </summary>
    /// <param name="me">The collision object.</param>
    /// <param name="clashUp">The innermost up-stem column.</param>
    /// <param name="clashDown">The innermost down-stem column.</param>
    /// <returns>The shift amount, as a fraction of the down-stem head width.</returns>
    private static double CheckMeshingChords(Grob me, Grob clashUp, Grob clashDown)
    {
        /* Every note column should have a stem, but avoid a crash. */
        if (NoteColumn.GetStem(clashUp) == null || NoteColumn.GetStem(clashDown) == null)
        {
            return 0.0;
        }

        DrulArray<Grob> stems = new DrulArray<Grob>(
            NoteColumn.GetStem(clashDown),
            NoteColumn.GetStem(clashUp));

        Grob fhUp = NoteColumn.FirstHead(clashUp);
        Grob fhDown = NoteColumn.FirstHead(clashDown);
        Grob shUp = NoteColumn.SupportHead(clashUp);
        Grob shDown = NoteColumn.SupportHead(clashDown);

        Interval extentUp = shUp.Extent(shUp, Axis.X);
        Interval extentDown = shDown.Extent(shDown, Axis.X);

        /* Staff-positions of all noteheads on each stem */
        List<int> ups = Stem.NoteHeadPositions(stems[Direction.Positive]);
        List<int> dps = Stem.NoteHeadPositions(stems[Direction.Negative]);

        object thresholdScm = me.GetProperty(NoteCollisionThreshold);
        int threshold = SchemeConvert.IsNumber(thresholdScm)
            ? SchemeConvert.ToInt(thresholdScm, "note-collision-threshold")
            : 1;

        /* Too far apart to collide. */
        if (ups[0] > dps[dps.Count - 1] + threshold)
        {
            return 0.0;
        }

        /* If the chords just 'touch' their extreme noteheads,
           then we can align their stems.
        */
        bool touch = false;
        if (ups[0] >= dps[dps.Count - 1]
            && (dps.Count < 2 || ups[0] >= dps[dps.Count - 2] + threshold + 1)
            && (ups.Count < 2 || ups[1] >= dps[dps.Count - 1] + threshold + 1))
        {
            touch = true;
        }

        /* Filter out the 'o's in this configuration, since they're no
         * part in the collision.
         *
         *  |
         * x|o
         * x|o
         * x
         *
         */
        ups = Stem.NoteHeadPositions(stems[Direction.Positive], true);
        dps = Stem.NoteHeadPositions(stems[Direction.Negative], true);

        /* Merge heads if the notes lie the same line, or if the "stem-up-note" is
           above the "stem-down-note". */
        bool mergePossible = ups.Count > 0 && dps.Count > 0 && ups[0] >= dps[0]
            && ups[ups.Count - 1] >= dps[dps.Count - 1];

        /* Do not merge notes typeset in different style. */
        if (!SchemeUtilities.IsEqual(
                fhUp.GetProperty(StyleSymbol), fhDown.GetProperty(StyleSymbol)))
        {
            mergePossible = false;
        }

        int upBallType = RhythmicHead.DurationLog(fhUp);
        int downBallType = RhythmicHead.DurationLog(fhDown);

        /* Do not merge whole notes (or longer, like breve, longa, maxima). */
        if (mergePossible && (upBallType <= 0 || downBallType <= 0))
        {
            mergePossible = false;
        }

        if (mergePossible
            && RhythmicHead.DotCount(fhUp) != RhythmicHead.DotCount(fhDown)
            && !SchemeUtilities.ToBool(me.GetProperty(MergeDifferentlyDotted)))
        {
            mergePossible = false;
        }

        /* Can only merge different heads if merge-differently-headed is set. */
        if (mergePossible && upBallType != downBallType
            && !SchemeUtilities.ToBool(me.GetProperty(MergeDifferentlyHeaded)))
        {
            mergePossible = false;
        }

        // Should never merge quarter and half notes, as this would make
        // them indistinguishable.
        //
        // TODO: The stem duration doesn't tell the full story if the heads themselves
        // have been tweaked.
        if (mergePossible
            && ((Stem.DurationLog(stems[Direction.Positive]) == 1
                 && Stem.DurationLog(stems[Direction.Negative]) == 2)
                || (Stem.DurationLog(stems[Direction.Positive]) == 2
                    && Stem.DurationLog(stems[Direction.Negative]) == 1)))
        {
            mergePossible = false;
        }

        /*
         * this case (distant half collide),
         *
         *    |
         *  x |
         * | x
         * |
         *
         * the noteheads may be closer than this case (close half collide)
         *
         *    |
         *    |
         *   x
         *  x
         * |
         * |
         *
         */

        bool closeHalfCollide = false;
        bool distantHalfCollide = false;
        bool fullCollide = false;

        for (int i = 0, j = 0; i < ups.Count && j < dps.Count;)
        {
            if (ups[i] == dps[j])
            {
                fullCollide = true;
            }
            else if (Math.Abs(ups[i] - dps[j]) <= threshold)
            {
                mergePossible = false;
                if (ups[i] > dps[j])
                {
                    closeHalfCollide = true;
                }
                else
                {
                    distantHalfCollide = true;
                }
            }
            else if (ups[i] > dps[0] && ups[i] < dps[dps.Count - 1])
            {
                mergePossible = false;
            }
            else if (dps[j] > ups[0] && dps[j] < ups[ups.Count - 1])
            {
                mergePossible = false;
            }

            if (ups[i] < dps[j])
            {
                i++;
            }
            else if (ups[i] > dps[j])
            {
                j++;
            }
            else
            {
                i++;
                j++;
            }
        }

        fullCollide = fullCollide || (closeHalfCollide && distantHalfCollide)
            || (distantHalfCollide // like full_ for wholes and longer
                && (upBallType <= 0 || downBallType <= 0));

        /* Determine which chord goes on the left, and which goes right.
           Up-stem usually goes on the right, but if chords just 'touch' we can put
           both stems on a common vertical line.  In the presense of collisions,
           right hand heads may obscure dots, so dotted heads to go the right.
        */
        double shiftAmount = 1;
        bool stemToStem = false;
        if ((fullCollide
             || ((closeHalfCollide || distantHalfCollide)
                 && SchemeUtilities.ToBool(me.GetProperty(PreferDottedRight))))
            && RhythmicHead.DotCount(fhUp) < RhythmicHead.DotCount(fhDown))
        {
            shiftAmount = -1;
            if (!touch)
            {
                // remember to leave clearance between stems
                stemToStem = true;
            }
        }
        else if (touch)
        {
            // Up-stem note on a line has a raised dot, so no risk of collision
            bool IsOnStaffLine()
            {
                Grob staff = StaffSymbolReferencer.GetStaffSymbol(me);
                return staff != null && StaffSymbol.OnLine(staff, ups[0]);
            }

            if ((fullCollide
                 || (!IsOnStaffLine()
                     && SchemeUtilities.ToBool(me.GetProperty(PreferDottedRight))))
                && RhythmicHead.DotCount(fhUp) > RhythmicHead.DotCount(fhDown))
            {
                touch = false;
            }
            else
            {
                shiftAmount = -1;
            }
        }

        /* The 'fa' shape note heads have a triangular shape, which is
           inverted depending on the stem direction.  In case of a
           collision, one of them should be removed so that the resulting
           note does not look like a rectangular block.
        */
        object faStyles = me.GetProperty(FaStylesSymbol);
        object upStyle = fhUp.GetProperty(StyleSymbol);
        object downStyle = fhDown.GetProperty(StyleSymbol);
        if (mergePossible && SchemeUtilities.Memq(upStyle, faStyles)
            && SchemeUtilities.Memq(downStyle, faStyles))
        {
            // Compute which shape should be displayed.
            Direction d = DirectionalElementInterface.FromScheme(
                me.GetProperty(FaMergeDirectionSymbol), Direction.Negative);

            // Hide unwanted glyph.
            (d == Direction.Positive ? fhDown : fhUp).SetProperty(TransparentSymbol, true);

            // Adjust starting point of the stem to get a smooth connection
            // between stem and glyph.
            Offset upAtt = new Offset(0.0, d == Direction.Positive ? 0.5 : -1.0);
            Offset downAtt = new Offset(0.0, d == Direction.Negative ? -0.5 : 1.0);
            fhUp.SetProperty(StemAttachmentSymbol, new Pair(upAtt.X, upAtt.Y));
            fhDown.SetProperty(StemAttachmentSymbol, new Pair(downAtt.X, downAtt.Y));
        }

        if (mergePossible)
        {
            shiftAmount = 0;

            /* If possible, don't wipe any heads.  Else, wipe shortest head,
               or head with smallest amount of dots.  Note: when merging
               different heads, dots on the smaller one disappear; and when
               merging identical heads, dots on the down-stem head disappear */
            Grob wipeBall = null;
            Grob dotWipeHead = fhUp;

            // The user might have hidden one of the notes.  It was correct to ignore
            // that to this point, since transparent grobs are expected to affect
            // layout; but now that we have decided to eliminate one, it makes sense
            // to be satisfied when one is already hidden.
            int VisibleDotCount(Grob head)
            {
                Item dots = RhythmicHead.GetDots(head);
                if (dots == null || dots.IsTransparent)
                {
                    return 0;
                }

                object count = dots.GetProperty(DotCountSymbol);
                return SchemeConvert.IsNumber(count)
                    ? SchemeConvert.ToInt(count, "dot-count")
                    : 0;
            }

            if (upBallType == downBallType)
            {
                if (VisibleDotCount(fhDown) < VisibleDotCount(fhUp))
                {
                    wipeBall = fhDown;
                    dotWipeHead = fhDown;
                }
                else if (VisibleDotCount(fhDown) > VisibleDotCount(fhUp))
                {
                    dotWipeHead = fhUp;
                    wipeBall = fhUp;
                }
                else
                {
                    dotWipeHead = fhDown;
                }
            }
            else if (downBallType > upBallType)
            {
                wipeBall = fhDown;
                dotWipeHead = fhDown;
            }
            else if (downBallType < upBallType)
            {
                wipeBall = fhUp;
                dotWipeHead = fhUp;

                /*
                  If upper head is eighth note or shorter, and lower head is half note,
                  shift by the difference between the open and filled note head widths,
                  otherwise upper stem will be misaligned slightly.
                */
                if (Stem.DurationLog(stems[Direction.Negative]) == 1
                    && Stem.DurationLog(stems[Direction.Positive]) >= 3)
                {
                    shiftAmount = (1 - extentUp[Direction.Positive]
                                       / extentDown[Direction.Positive]) * 0.5;
                }
            }

            if (dotWipeHead != null)
            {
                if (dotWipeHead.GetObject(DotSymbol) is Grob d)
                {
                    d.Suicide();
                }
            }

            if (wipeBall != null && wipeBall.IsLive)
            {
                wipeBall.SetProperty(TransparentSymbol, true);
            }
        }

        /* TODO: these numbers are magic; should devise a set of grob props
           to tune this behavior. */
        else if (stemToStem)
        {
            shiftAmount *= 0.65;
        }
        else if (touch)
        {
            shiftAmount *= 0.5;
        }
        else if (closeHalfCollide)
        {
            shiftAmount *= 0.52;
        }
        else if (fullCollide)
        {
            shiftAmount *= 0.5;
        }
        else if (distantHalfCollide)
        {
            shiftAmount *= 0.4;
        }

        /* we're meshing. */
        else if (RhythmicHead.DotCount(fhUp) != 0 || RhythmicHead.DotCount(fhDown) != 0)
        {
            shiftAmount *= 0.1;
        }
        else
        {
            shiftAmount *= 0.17;
        }

        /* The offsets computed in this routine are multiplied,
           in calc_positioning_done(), by the width of the downstem note.
           The shift required to clear collisions, however, depends on the extents
           of the note heads on the sides that interfere. */
        if (shiftAmount < 0.0) // Down-stem shifts right.
        {
            shiftAmount *= (extentUp[Direction.Positive] - extentDown[Direction.Negative])
                / extentDown.Length;
        }
        else // Up-stem shifts right.
        {
            shiftAmount *= (extentDown[Direction.Positive] - extentUp[Direction.Negative])
                / extentDown.Length;
        }

        /* If any dotted notes ended up on the left,
           tell the Dot_Columnn to avoid the note heads on the right.
         */
        if (shiftAmount < -1e-6 && RhythmicHead.DotCount(fhUp) != 0)
        {
            Grob d = fhUp.GetObject(DotSymbol) as Grob;
            Grob parent = d?.XParent;
            if (parent != null && parent.HasInterface(DotColumnInterface))
            {
                SidePositionInterface.AddSupport(parent, fhDown);
            }
        }
        else if (RhythmicHead.DotCount(fhDown) != 0)
        {
            Grob d = fhDown.GetObject(DotSymbol) as Grob;
            Grob parent = d?.XParent;
            if (parent != null && parent.HasInterface(DotColumnInterface))
            {
                Grob stem = fhUp.GetObject(StemSymbol) as Grob;

                // Loop over all heads on an up-pointing-stem to see if dots
                // need to clear any heads suspended on its right side.
                if (stem != null)
                {
                    IReadOnlyList<Grob> heads
                        = PointerGroupInterface.ExtractGrobSet(stem, NoteHeadsSymbol);
                    foreach (Grob head in heads)
                    {
                        SidePositionInterface.AddSupport(parent, head);
                    }
                }
            }
        }

        // In meshed chords with dots on the left, adjust dot direction
        if (shiftAmount > 1e-6 && RhythmicHead.DotCount(fhDown) != 0)
        {
            Grob dotDown = fhDown.GetObject(DotSymbol) as Grob;
            Grob colDown = dotDown?.XParent;
            Direction dir = Direction.Positive;
            if (RhythmicHead.DotCount(fhUp) != 0)
            {
                Grob dotUp = fhUp.GetObject(DotSymbol) as Grob;
                Grob colUp = dotUp?.XParent;
                if (ReferenceEquals(colUp, colDown))
                {
                    // let the common DotColumn arrange dots
                    dir = Direction.Center;
                }
                else
                {
                    // conform to the dot direction on the up-stem chord
                    dir = DirectionalElementInterface.FromScheme(
                        dotUp?.GetProperty(DirectionSymbol), Direction.Positive);
                }
            }

            if (dir != Direction.Center)
            {
                Grob stem = fhDown.GetObject(StemSymbol) as Grob;
                if (stem != null)
                {
                    IReadOnlyList<Grob> heads
                        = PointerGroupInterface.ExtractGrobSet(stem, NoteHeadsSymbol);
                    foreach (Grob head in heads)
                    {
                        if (head.GetObject(DotSymbol) is Grob dot)
                        {
                            dot.SetProperty(DirectionSymbol, (long)dir.Value);
                        }
                    }
                }
            }
        }

        return shiftAmount;
    }

    /// <summary>
    /// The <c>positioning-done</c> callback: measures every clashing column, works out
    /// the automatic and forced shifts, and translates the columns so the leftmost sits
    /// at the collision's own origin.
    /// </summary>
    /// <param name="me">The collision object.</param>
    /// <returns><see langword="true"/>.</returns>
    public static bool CalcPositioningDone(Grob me)
    {
        me.SetProperty(PositioningDoneSymbol, true);

        DrulArray<List<Grob>> clashGroups = GetClashGroups(me);

        foreach (Direction d in new[] { Direction.Positive, Direction.Negative })
        {
            for (int i = clashGroups[d].Count; i-- > 0;)
            {
                /*
                  Trigger positioning
                */
                clashGroups[d][i].Extent(me, Axis.X);
            }
        }

        object autos = AutomaticShift(me, clashGroups);
        object hand = ForcedShift(me);

        double wid = 0.0;
        foreach (Direction d in new[] { Direction.Positive, Direction.Negative })
        {
            if (clashGroups[d].Count > 0)
            {
                Grob h = clashGroups[d][0];
                Grob sh = NoteColumn.SupportHead(h);
                if (sh != null)
                {
                    wid = sh.Extent(h, Axis.X).Length;
                }
            }
        }

        List<Grob> done = new List<Grob>();
        double leftMost = 0.0;

        List<double> amounts = new List<double>();
        for (object s = hand; s is Pair pair; s = pair.Cdr)
        {
            Pair entry = (Pair)pair.Car;
            Grob grob = (Grob)entry.Car;
            double amount = SchemeConvert.ToDouble(entry.Cdr, "force-hshift") * wid;

            done.Add(grob);
            amounts.Add(amount);
        }

        for (object s = autos; s is Pair pair; s = pair.Cdr)
        {
            Pair entry = (Pair)pair.Car;
            Grob grob = (Grob)entry.Car;
            double amount = SchemeConvert.ToDouble(entry.Cdr, "shift") * wid;

            if (!done.Contains(grob))
            {
                done.Add(grob);
                amounts.Add(amount);
                if (amount < leftMost)
                {
                    leftMost = amount;
                }
            }
        }

        for (int i = 0; i < amounts.Count; i++)
        {
            done[i].TranslateAxis(amounts[i] - leftMost, Axis.X);
        }

        return true;
    }

    /// <summary>
    /// Splits the collision's elements into its up-stem and down-stem note columns,
    /// each ordered by <c>horizontal-shift</c>.
    /// </summary>
    /// <param name="me">The collision object.</param>
    /// <returns>The two groups.</returns>
    public static DrulArray<List<Grob>> GetClashGroups(Grob me)
    {
        DrulArray<List<Grob>> clashGroups
            = new DrulArray<List<Grob>>(new List<Grob>(), new List<Grob>());

        IReadOnlyList<Grob> elements = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        foreach (Grob se in elements)
        {
            if (se.HasInterface(NoteColumnInterface))
            {
                if (!NoteColumn.Dir(se).IsNonZero)
                {
                    Warn.ProgrammingError("note-column has no direction");
                }
                else
                {
                    clashGroups[NoteColumn.Dir(se)].Add(se);
                }
            }
        }

        foreach (Direction d in new[] { Direction.Positive, Direction.Negative })
        {
            // A stable insertion sort stands in for std::sort over shift_less; the
            // comparison is a strict weak ordering here, and stability makes the
            // outcome reproducible where upstream's tie order is unspecified.
            List<Grob> clashes = clashGroups[d];
            for (int i = 1; i < clashes.Count; i++)
            {
                Grob current = clashes[i];
                int j = i - 1;
                while (j >= 0 && NoteColumn.ShiftLess(current, clashes[j]))
                {
                    clashes[j + 1] = clashes[j];
                    j--;
                }

                clashes[j + 1] = current;
            }
        }

        return clashGroups;
    }

    /*
      This complicated routine moves note columns around horizontally to
      ensure that notes don't clash.
    */

    /// <summary>
    /// Works out one shift per column, in fractions of a head width: the two innermost
    /// columns get the meshing answer, every further same-direction column steps
    /// outward from there.
    /// </summary>
    /// <param name="me">The collision object.</param>
    /// <param name="clashGroups">The clash groups from <see cref="GetClashGroups"/>.</param>
    /// <returns>An alist of (column . amount) pairs.</returns>
    public static object AutomaticShift(Grob me, DrulArray<List<Grob>> clashGroups)
    {
        object tups = Nil.Instance;

        DrulArray<List<Slice>> extents
            = new DrulArray<List<Slice>>(new List<Slice>(), new List<Slice>());
        DrulArray<Slice> extentUnion = new DrulArray<Slice>(Slice.Empty, Slice.Empty);
        DrulArray<List<Grob>> stems
            = new DrulArray<List<Grob>>(new List<Grob>(), new List<Grob>());

        foreach (Direction d in new[] { Direction.Positive, Direction.Negative })
        {
            for (int i = 0; i < clashGroups[d].Count; i++)
            {
                Slice s = NoteColumn.HeadPositionsInterval(clashGroups[d][i]);
                s = new Slice(s.Left - 1, s.Right + 1);
                extents[d].Add(s);
                Slice union = extentUnion[d];
                union.Unite(s);
                extentUnion[d] = union;
                stems[d].Add(NoteColumn.GetStem(clashGroups[d][i]));
            }
        }

        double innerOffset
            = clashGroups[Direction.Positive].Count > 0
              && clashGroups[Direction.Negative].Count > 0
                ? CheckMeshingChords(
                    me,
                    clashGroups[Direction.Positive][0],
                    clashGroups[Direction.Negative][0])
                : 0.0;

        /*
         * do horizontal shifts of each direction
         *
         *  |
         * x||
         *  x||
         *   x|
        */
        DrulArray<List<double>> offsets
            = new DrulArray<List<double>>(new List<double>(), new List<double>());
        foreach (Direction d in new[] { Direction.Positive, Direction.Negative })
        {
            double offset = innerOffset;
            List<int> shifts = new List<int>();
            for (int i = 0; i < clashGroups[d].Count; i++)
            {
                Grob col = clashGroups[d][i];
                object sh = col.GetProperty(HorizontalShiftSymbol);
                shifts.Add(SchemeConvert.IsNumber(sh)
                    ? SchemeConvert.ToInt(sh, "horizontal-shift")
                    : 0);

                if (i == 0)
                {
                    offset = innerOffset;
                }
                else
                {
                    bool explicitShift = SchemeConvert.IsNumber(sh);
                    if (!explicitShift)
                    {
                        Warn.Warning("this Voice needs a \\voiceXx or \\shiftXx setting");
                    }

                    if (explicitShift && shifts[i] == shifts[i - 1])
                    {
                        // Match the previous notecolumn offset
                    }
                    else if (extents[d][i][Direction.Positive]
                                 > extents[d][i - 1][Direction.Negative]
                             && extents[d][i][Direction.Negative]
                                 < extents[d][i - 1][Direction.Positive])
                    {
                        offset += 1.0; // fully clear the previous-notecolumn heads
                    }
                    else if (d.Value * extents[d][i][-d] >= d.Value * extents[d][i - 1][d])
                    {
                        offset += Stem.IsValidStem(stems[d][i - 1])
                            ? 1.0
                            : 0.5; // we cross the previous notecolumn
                    }
                    else if (Stem.IsValidStem(stems[d][i]))
                    {
                        offset += 0.5;
                    }

                    // check if we cross the opposite-stemmed voices
                    if (d.Value * extents[d][i][-d] < d.Value * extentUnion[-d][d])
                    {
                        offset = Math.Max(offset, 0.5);
                    }

                    if (extents[-d].Count > 0
                        && extents[d][i][Direction.Positive]
                            > extents[-d][0][Direction.Negative]
                        && extents[d][i][Direction.Negative]
                            < extents[-d][0][Direction.Positive])
                    {
                        offset = Math.Max(offset, 1.0);
                    }
                }

                offsets[d].Add(d.Value * offset);
            }
        }

        /*
          see input/regression/dot-up-voice-collision.ly
        */
        for (int i = 0; i < clashGroups[Direction.Positive].Count; i++)
        {
            Grob g = clashGroups[Direction.Positive][i];
            Grob dc = NoteColumn.GetDotColumn(g);

            if (dc != null)
            {
                for (int j = i + 1; j < clashGroups[Direction.Positive].Count; j++)
                {
                    SidePositionInterface.AddSupport(dc, stems[Direction.Positive][j]);
                }
            }
        }

        foreach (Direction d in new[] { Direction.Positive, Direction.Negative })
        {
            for (int i = 0; i < clashGroups[d].Count; i++)
            {
                tups = new Pair(
                    new Pair(clashGroups[d][i], offsets[d][i]),
                    tups);
            }
        }

        return tups;
    }

    /// <summary>Collects the columns whose <c>force-hshift</c> was set by hand.</summary>
    /// <param name="me">The collision object.</param>
    /// <returns>An alist of (column . amount) pairs.</returns>
    public static object ForcedShift(Grob me)
    {
        object tups = Nil.Instance;

        IReadOnlyList<Grob> elements = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        foreach (Grob se in elements)
        {
            object force = se.GetProperty(ForceHshiftSymbol);
            if (SchemeConvert.IsNumber(force))
            {
                tups = new Pair(new Pair(se, force), tups);
            }
        }

        return tups;
    }

    /// <summary>
    /// Takes a note column into the collision: parented, and positioned by the parent's
    /// own positioning.
    /// </summary>
    /// <param name="me">The collision object.</param>
    /// <param name="ncol">The note column.</param>
    public static void AddColumn(Grob me, Grob ncol)
    {
        object proc = Bootstrap.LilyPondScheme.LookupProcedure(XParentPositioningSymbol);
        if (proc != null)
        {
            ncol.SetProperty(XOffsetSymbol, proc);
        }
        else
        {
            Warn.ProgrammingError("ly:grob::x-parent-positioning is not defined");
        }

        AxisGroupInterface.AddElement(me, ncol);
    }

    /// <summary>Returns every head position on every stem in the collision, sorted.</summary>
    /// <param name="me">The collision object.</param>
    /// <returns>The positions.</returns>
    public static List<int> NoteHeadPositions(Grob me)
    {
        List<int> result = new List<int>();
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        foreach (Grob element in elts)
        {
            if (element.GetObject(StemSymbol) is Grob stem)
            {
                result.AddRange(Stem.NoteHeadPositions(stem));
            }
        }

        result.Sort();
        return result;
    }
}
