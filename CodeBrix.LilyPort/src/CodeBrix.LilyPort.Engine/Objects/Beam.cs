/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Jan Nieuwenhuizen <janneke@gnu.org>

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

/*
  TODO:

  - Determine auto knees based on positions if it's set by the user.

  - the code is littered with * and / staff_space calls for
  #'positions. Consider moving to real-world coordinates?

  Problematic issue is user tweaks (user tweaks are in staff-coordinates.)

  Notes:

  - Stems run to the Y-center of the beam.

  - beam_translation is the offset between Y centers of the beam.
*/

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/beam.cc, lily/include/beam.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the three Beam_* helper structs become nested types on Beam, since C# has no
//     free-floating file-scope structs and they are meaningless apart from it
//   - Beam::print's `extreme` index is CLAMPED into range; upstream stores a signed
//     beam rank in a vsize and indexes with it, which is undefined behaviour for a
//     negative rank. See PORT-COVERAGE, BEAM PRINT EXTREME INDEX
//   - ly_memv over the beaming lists is a local helper: SchemeUtilities.Memq compares
//     by reference, which never matches a boxed number

/// <summary>
/// A beam: the horizontal bar joining a run of stems, together with the callbacks that
/// decide its direction, slope, segments and the lengths it imposes on its stems.
/// </summary>
public static class Beam
{
    private static readonly Symbol StemsSymbol = Symbol.Intern("stems");
    private static readonly Symbol NormalStemsSymbol = Symbol.Intern("normal-stems");
    private static readonly Symbol BeamSymbol = Symbol.Intern("beam");
    private static readonly Symbol BeamThicknessSymbol = Symbol.Intern("beam-thickness");
    private static readonly Symbol LengthFractionSymbol = Symbol.Intern("length-fraction");
    private static readonly Symbol BeamingSymbol = Symbol.Intern("beaming");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol DefaultDirectionSymbol = Symbol.Intern("default-direction");
    private static readonly Symbol NeutralDirectionSymbol = Symbol.Intern("neutral-direction");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol KievanSymbol = Symbol.Intern("kievan");
    private static readonly Symbol GapSymbol = Symbol.Intern("gap");
    private static readonly Symbol GapCountSymbol = Symbol.Intern("gap-count");
    private static readonly Symbol AccidentalPaddingSymbol = Symbol.Intern("accidental-padding");
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol MaxBeamConnectSymbol = Symbol.Intern("max-beam-connect");
    private static readonly Symbol BreakOvershootSymbol = Symbol.Intern("break-overshoot");
    private static readonly Symbol BeamletDefaultLengthSymbol
        = Symbol.Intern("beamlet-default-length");
    private static readonly Symbol BeamletMaxLengthProportionSymbol
        = Symbol.Intern("beamlet-max-length-proportion");
    private static readonly Symbol VerticalCountSymbol = Symbol.Intern("vertical-count");
    private static readonly Symbol HorizontalSymbol = Symbol.Intern("horizontal");
    private static readonly Symbol BeamSegmentsSymbol = Symbol.Intern("beam-segments");
    private static readonly Symbol XPositionsSymbol = Symbol.Intern("X-positions");
    private static readonly Symbol PositionsSymbol = Symbol.Intern("positions");
    private static readonly Symbol QuantizedPositionsSymbol = Symbol.Intern("quantized-positions");
    private static readonly Symbol BlotDiameterSymbol = Symbol.Intern("blot-diameter");
    private static readonly Symbol GrowDirectionSymbol = Symbol.Intern("grow-direction");
    private static readonly Symbol NormalizedEndpointsSymbol
        = Symbol.Intern("normalized-endpoints");
    private static readonly Symbol AnnotationSymbol = Symbol.Intern("annotation");
    private static readonly Symbol AutoKneeGapSymbol = Symbol.Intern("auto-knee-gap");
    private static readonly Symbol KneeSymbol = Symbol.Intern("knee");
    private static readonly Symbol BeamedStemShortenSymbol = Symbol.Intern("beamed-stem-shorten");
    private static readonly Symbol FrenchBeamingSymbol = Symbol.Intern("french-beaming");
    private static readonly Symbol ClipEdgesSymbol = Symbol.Intern("clip-edges");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol StemletLengthSymbol = Symbol.Intern("stemlet-length");
    private static readonly Symbol MinimumDistanceSymbol = Symbol.Intern("minimum-distance");
    private static readonly Symbol BeamInterfaceSymbol = Symbol.Intern("beam-interface");

    private static readonly Direction[] BothDirections
        = { Direction.Negative, Direction.Positive };

    private static readonly Direction[] DownUp
        = { Direction.Negative, Direction.Positive };

    /// <summary>Adds a stem to a beam, claiming it.</summary>
    /// <param name="me">The beam.</param>
    /// <param name="s">The stem.</param>
    public static void AddStem(Grob me, Grob s)
    {
        if (Stem.GetBeam(s) != null)
        {
            Warn.ProgrammingError("Stem already has beam");
            return;
        }

        PointerGroupInterface.AddGrob(me, StemsSymbol, s);
        s.SetObject(BeamSymbol, me);
        Spanner.AddBoundItem(me as Spanner, s as Item);
    }

    /// <summary>The beam's thickness, in output units.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The thickness.</returns>
    public static double GetBeamThickness(Grob me)
        => Stem.ToDouble(me.GetProperty(BeamThicknessSymbol), 0)
           * StaffSymbolReferencer.StaffSpace(me);

    /* Return the translation between 2 adjoining beams. */

    /// <summary>The distance between two adjoining beams.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The translation.</returns>
    public static double GetBeamTranslation(Grob me)
    {
        int beamCount = GetBeamCount(me);
        double staffSpace = StaffSymbolReferencer.StaffSpace(me);
        double line = StaffSymbolReferencer.LineThickness(me);
        double beamThickness = GetBeamThickness(me);
        double fract = Stem.ToDouble(me.GetProperty(LengthFractionSymbol), 1.0);

        /*
          if fract != 1.0, as is the case for grace notes, we want the gap
          to decrease too. To achieve this, we divide the thickness by
          fract */
        return beamCount < 4
            ? ((2 * staffSpace * fract) + (line * fract) - beamThickness) / 2.0
            : ((3 * staffSpace * fract) + (line * fract) - beamThickness) / 3.0;
    }

    /* Maximum beam_count. */

    /// <summary>The maximum beam count over the beam's stems.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The count.</returns>
    public static int GetBeamCount(Grob me)
    {
        int m = 0;

        foreach (Grob stem in PointerGroupInterface.ExtractGrobSet(me, StemsSymbol))
        {
            m = Math.Max(m, Stem.BeamMultiplicity(stem).Length + 1);
        }

        return m;
    }

    /// <summary>Collects the stems that are not invisible — <c>ly:beam::calc-normal-stems</c>.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The grob array of normal stems.</returns>
    public static object CalcNormalStems(Grob me)
    {
        GrobArray ga = new GrobArray();
        foreach (Grob stem in PointerGroupInterface.ExtractGrobSet(me, StemsSymbol))
        {
            if (Stem.IsNormalStem(stem))
            {
                ga.Add(stem);
            }
        }

        return ga;
    }

    /// <summary>Decides the beam's direction — <c>ly:beam::calc-direction</c>.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The direction, or unspecified when the beam removed itself.</returns>
    public static object CalcDirection(Grob me)
    {
        /* Beams with less than 2 two stems don't make much sense, but could happen
           when you do

           r8[ c8 r8]

        */

        Direction dir = Direction.Center;

        int count = NormalStemCount(me);
        if (count < 2)
        {
            IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
            if (stems.Count == 0)
            {
                me.Warning("removing beam with no stems");
                me.Suicide();

                return Unspecified.Instance;
            }
            else
            {
                Grob stem = FirstNormalStem(me);

                /*
                  This happens for chord tremolos.
                */
                if (stem == null)
                {
                    stem = stems[0];
                }

                object dirData = stem.GetPropertyData(DirectionSymbol);
                dir = SchemeConvert.IsNumber(dirData)
                    ? Stem.FromScmDirection(dirData)
                    : Stem.FromScmDirection(stem.GetProperty(DefaultDirectionSymbol));

                IReadOnlyList<Grob> heads
                    = PointerGroupInterface.ExtractGrobSet(stem, NoteHeadsSymbol);

                /* default position of Kievan heads with beams is down
                   placing this here avoids warnings downstream */
                if (heads.Count != 0)
                {
                    if (ReferenceEquals(heads[0].GetProperty(StyleSymbol), KievanSymbol))
                    {
                        if (dir == Direction.Center)
                        {
                            dir = Direction.Negative;
                        }
                    }
                }
            }
        }

        if (!dir.IsNonZero)
        {
            dir = GetDefaultDir(me);
        }

        if (count >= 1)
        {
            ConsiderAutoKnees(me);
        }

        SetStemDirections(me, dir);

        return (long)dir.Value;
    }

    /* We want a maximal number of shared beams, but if there is choice, we
     * take the one that is closest to the end of the stem. This is for
     * situations like
     *
     *        x
     *       |
     *       |
     *   |===|
     *   |=
     *   |
     *  x
     */

    /// <summary>
    /// Finds the beam position that shares the most beams with the previous stem,
    /// preferring the one closest to the end of the stem when there is a choice.
    /// </summary>
    /// <param name="leftBeaming">The previous stem's beaming pair.</param>
    /// <param name="rightBeaming">This stem's beaming pair.</param>
    /// <param name="leftDir">The previous stem's direction.</param>
    /// <param name="rightDir">This stem's direction.</param>
    /// <param name="specialShift">Whether a beam corner is allowed here.</param>
    /// <returns>The chosen start position.</returns>
    public static int PositionWithMaximalCommonBeams(
        object leftBeaming,
        object rightBeaming,
        Direction leftDir,
        Direction rightDir,
        bool specialShift)
    {
        Slice lslice = Stem.IntListToSlice((leftBeaming as Pair)?.Cdr);

        int bestCount = 0;
        int bestStart = 0;
        for (int i = lslice[-leftDir]; (i - lslice[leftDir]) * leftDir.Value <= 0;
             i += leftDir.Value)
        {
            int count = 0;
            object cursor = (rightBeaming as Pair)?.Car;
            while (cursor is Pair pair)
            {
                int beamNo = (int)SchemeConvert.ToLong(pair.Car, "beam position");
                int k = (-rightDir.Value * beamNo) + i;
                if (Memv(k, (leftBeaming as Pair)?.Cdr))
                {
                    count++;
                }

                cursor = pair.Cdr;
            }

            /* TODO: consider flipping left_dir based on value of special_shift
             * instead of the below conjunction and calculation of `new_beam_pos`
             * in Beam::calc_beaming
             */
            if (count > bestCount || (count == bestCount && !specialShift))
            {
                bestCount = count;
                bestStart = i;
            }
        }

        return bestStart;
    }

    /// <summary>
    /// Rewrites every stem's beaming positions so neighbouring stems share beams —
    /// <c>ly:beam::calc-beaming</c>.
    /// </summary>
    /// <param name="me">The beam.</param>
    /// <returns>The empty list; the work is done by side effect on the stems.</returns>
    public static object CalcBeaming(Grob me)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);

        /* When a kneed beam alternates stem directions, the previous Slice
         * of the same direction of the current direction (index 0) should be remembered
         * should the current stem be specially treated as explained in the comment
         * below. The previous Slice of the opposite direction (index 1) would shift
         * to index 0 to prepare for a future possible direction flip
         */
        Slice[] firstSliceOfPrevDirs = { new Slice(0, 0), new Slice(0, 0) };

        object lastBeaming = new Pair(Nil.Instance, new Pair(0L, Nil.Instance));
        Direction lastDir = Direction.Center;
        int lastRightBeamCount = 0;
        foreach (Grob thisStem in stems)
        {
            object thisBeaming = thisStem.GetProperty(BeamingSymbol);
            if (thisBeaming is Pair thisBeamingPair)
            {
                Direction thisDir = Stem.GetGrobDirection(thisStem);

                /* Gould's pg 316-317 explicitly allows beam corners
                 * when both cases of "outer notes of a subdivision [having]
                 * opposite stem direction" and "subdivided group [occuring]
                 * at the end of main beam." If special_shift is always false,
                 * then old behavior of "funky" beams that incrementally shift
                 * the main beam persists
                 */
                int rightBeaming = Stem.GetBeaming(thisStem, Direction.Positive);
                bool specialShift
                    = Direction.DirectedOpposite(thisDir, lastDir)

                      // treat specially if left side of current stem is subdivided
                      && lastRightBeamCount >= Stem.GetBeaming(thisStem, Direction.Negative)

                      // do not treat specially if previous stem had fractional beam
                      && lastRightBeamCount < rightBeaming;
                int startPoint = PositionWithMaximalCommonBeams(
                    lastBeaming, thisBeaming, lastDir.IsNonZero ? lastDir : thisDir,
                    thisDir, specialShift);
                if (specialShift)
                {
                    specialShift
                        = thisDir.Value * (firstSliceOfPrevDirs[0][thisDir] - startPoint) > 0;
                }

                Slice newSlice = Slice.Empty;
                foreach (Direction d in BothDirections)
                {
                    newSlice = Slice.Empty;
                    object cursor = Stem.IndexGetCell(thisBeamingPair, d);
                    while (cursor is Pair pair)
                    {
                        int s = (int)SchemeConvert.ToLong(pair.Car, "beam position");
                        int newBeamPos
                            = specialShift
                                ? firstSliceOfPrevDirs[1][-thisDir] + (thisDir.Value * s)
                                : startPoint - (thisDir.Value * s);
                        newSlice.AddPoint(newBeamPos);
                        pair.Car = (long)newBeamPos;

                        cursor = pair.Cdr;
                    }
                }

                if (!newSlice.IsEmpty && thisDir.IsNonZero && thisDir != lastDir)
                {
                    firstSliceOfPrevDirs
                        = new[] { firstSliceOfPrevDirs[1], newSlice };
                }

                if (ListLength(thisBeamingPair.Cdr) > 0)
                {
                    lastBeaming = thisBeaming;
                    lastDir = thisDir;
                    lastRightBeamCount = rightBeaming;
                }
            }
        }

        return Nil.Instance;
    }

    /// <summary>The accidentals attached to the beam's last stem's heads.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The accidental grobs.</returns>
    public static List<Grob> GetAccidentals(Grob me)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
        List<Grob> accs = new List<Grob>();
        if (stems.Count != 0)
        {
            Grob lastStem = stems[stems.Count - 1];
            if (lastStem != null)
            {
                foreach (Grob noteHead in
                         PointerGroupInterface.ExtractGrobSet(lastStem, NoteHeadsSymbol))
                {
                    if (noteHead.GetObject(AccidentalGrobSymbol) is Grob acc)
                    {
                        accs.Add(acc);
                    }
                }
            }
        }

        return accs;
    }

    /// <summary>The gap to leave at each end of a tremolo beam.</summary>
    /// <param name="me">The beam.</param>
    /// <param name="commonx">The common horizontal reference point.</param>
    /// <returns>The two gap lengths.</returns>
    public static DrulArray<double> GetGaps(Grob me, Grob commonx)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
        double gapLength = Stem.ToDouble(me.GetProperty(GapSymbol), 0.0);
        DrulArray<double> gapLengths = new DrulArray<double>(gapLength, gapLength);

        if (stems.Count != 0 && Stem.DurationLog(stems[0]) <= 0)
        {
            List<Grob> accs = GetAccidentals(me);

            if (accs.Count != 0)
            {
                Interval accsExt = AxisGroupInterface.RelativeGroupExtentOf(
                    accs, commonx, Axis.X);
                if (!accsExt.IsEmpty)
                {
                    double accsLength = accsExt.Length;
                    double accPadding
                        = Stem.ToDouble(me.GetProperty(AccidentalPaddingSymbol), 1.0);
                    gapLengths[Direction.Positive] += accsLength + accPadding;
                }
            }
        }

        return gapLengths;
    }

    /// <summary>
    /// Adds spacing rods for a chord tremolo whose heads carry accidentals —
    /// <c>ly:beam::tremolo-springs-and-rods</c>.
    /// </summary>
    /// <param name="me">The beam.</param>
    /// <returns>Unspecified.</returns>
    public static object TremoloSpringsAndRods(Spanner me)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);

        if (stems.Count != 0)
        {
            List<Grob> accs = GetAccidentals(me);

            if (accs.Count != 0 && Stem.DurationLog(stems[0]) <= 0)
            {
                Spanner.SetSpacingRods(me);
            }
        }

        return Unspecified.Instance;
    }

    /// <summary>
    /// Works out the beam's drawable segments — <c>ly:beam::calc-beam-segments</c>.
    /// </summary>
    /// <param name="me">The beam.</param>
    /// <returns>The segments, as a Scheme list of alists.</returns>
    public static object CalcBeamSegments(Spanner me)
    {
        /* ugh, this has a side-effect that we need to ensure that
           Stem.beaming is correct */
        _ = me.GetProperty(BeamingSymbol);

        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);

        Grob commonx = Stem.CommonRefpointOfArray(stems, me, Axis.X);
        foreach (Direction d in BothDirections)
        {
            commonx = CommonWithBound(me, commonx, d);
        }

        int gapCount = (int)ToLongOr(me.GetProperty(GapCountSymbol), 0);
        DrulArray<double> gapLengths = GetGaps(me, commonx);

        SortedDictionary<int, List<BeamStemSegment>> stemSegments
            = new SortedDictionary<int, List<BeamStemSegment>>();
        double lt = StaffSymbolReferencer.LineThickness(me);

        /* There are two concepts of "rank" that are used in the following code.
           The beam_rank is the vertical position of the beam (larger numbers are
           closer to the noteheads). Beam_stem_segment.rank_, on the other hand,
           is the horizontal position of the segment (this is incremented by two
           for each stem; the beam segment on the right side of the stem has
           a higher rank (by one) than its neighbour to the left). */
        Slice ranks = Slice.Empty;
        for (int i = 0; i < stems.Count; i++)
        {
            Grob stem = stems[i];
            double stemWidth = Stem.ToDouble(stem.GetProperty(ThicknessSymbol), 1.0) * lt;
            double stemX = stem.RelativeCoordinate(commonx, Axis.X);
            object beaming = stem.GetProperty(BeamingSymbol);

            // A stem whose `beaming' is not a pair contributes no segments, because
            // beaming is exactly what says where its segments are. Upstream's own
            // Beam::calc_beaming guards the same data the same way one function
            // earlier (`if (scm_is_pair (this_beaming))'); calc_beam_segments does
            // not, and reaches scm_car of the empty list. See PORT-COVERAGE,
            // SINGLE-STEM CHORD TREMOLO BEAMS.
            if (!(beaming is Pair))
            {
                continue;
            }

            foreach (Direction d in BothDirections)
            {
                // Find the maximum and minimum beam ranks.
                // Given that RANKS is never reset to empty, the interval will always be
                // smallest for the left beamlet of the first stem, and then it might grow.
                // Do we really want this? (It only affects the tremolo gaps) --jneem
                for (object s = Stem.IndexGetCell(beaming as Pair, d); s is Pair sp;
                     s = sp.Cdr)
                {
                    if (!IsInteger(sp.Car))
                    {
                        continue;
                    }

                    int beamRank = (int)SchemeConvert.ToLong(sp.Car, "beam rank");
                    ranks.AddPoint(beamRank);
                }

                for (object s = Stem.IndexGetCell(beaming as Pair, d); s is Pair sp;
                     s = sp.Cdr)
                {
                    if (!IsInteger(sp.Car))
                    {
                        continue;
                    }

                    int beamRank = (int)SchemeConvert.ToLong(sp.Car, "beam rank");
                    BeamStemSegment seg = new BeamStemSegment
                    {
                        Stem = stem,
                        StemX = stemX,
                        Rank = (2 * i) + (d == Direction.Positive ? 1 : 0),
                        Width = stemWidth,
                        StemIndex = i,
                        Dir = d,
                        MaxConnect
                            = (int)ToLongOr(
                                stem.GetProperty(MaxBeamConnectSymbol), 1000),
                    };

                    Direction stemDir = Stem.GetGrobDirection(stem);

                    seg.Gapped = stemDir.Value * beamRank
                                 < (stemDir.Value * ranks[-stemDir]) + gapCount;

                    if (!stemSegments.TryGetValue(beamRank, out List<BeamStemSegment> bucket))
                    {
                        bucket = new List<BeamStemSegment>();
                        stemSegments[beamRank] = bucket;
                    }

                    bucket.Add(seg);
                }
            }
        }

        DrulArray<double> breakOvershoot = ReadDrul(
            me.GetProperty(BreakOvershootSymbol), new DrulArray<double>(-0.5, 0.0));

        List<BeamSegment> segments = new List<BeamSegment>();
        foreach (KeyValuePair<int, List<BeamStemSegment>> entry in stemSegments)
        {
            int verticalCount = entry.Key;
            List<BeamStemSegment> segs = new List<BeamStemSegment>(entry.Value);
            segs.Sort((a, b) => a.Rank.CompareTo(b.Rank));

            BeamSegment current = new BeamSegment();

            // Iterate over all of the segments of the current beam rank,
            // merging the adjacent Beam_stem_segments into one Beam_segment
            // when appropriate.
            for (int j = 0; j < segs.Count; j++)
            {
                // Keeping track of the different directions here is a little tricky.
                // segs[j].dir_ is the direction of the beam segment relative to the stem
                // (ie. segs[j].dir_ == LEFT if the beam segment sticks out to the left of
                // its stem) whereas event_dir refers to the edge of the beam segment that
                // we are currently looking at (ie. if segs[j].dir_ == event_dir then we
                // are looking at that edge of the beam segment that is furthest from its
                // stem).
                BeamStemSegment seg = segs[j];
                foreach (Direction eventDir in BothDirections)
                {
                    // TODO: make names clearer? --jneem
                    // on_line_bound: whether the current segment is on the boundary of the WHOLE beam
                    // on_beam_bound: whether the current segment is on the boundary of just that part
                    //   of the beam with the current beam_rank
                    bool onLineBound = seg.Dir == Direction.Negative
                        ? seg.StemIndex == 0
                        : seg.StemIndex == stems.Count - 1;
                    bool onBeamBound
                        = eventDir == Direction.Negative ? j == 0 : j == segs.Count - 1;
                    bool insideStem = eventDir == Direction.Negative
                        ? seg.StemIndex > 0
                        : seg.StemIndex + 1 < stems.Count;

                    // The || chain must stay short-circuiting: segs[j + event_dir] is out
                    // of range exactly when on_beam_bound is true, which is what stops it
                    // being evaluated. Upstream depends on the same thing.
                    bool isEvent
                        = onBeamBound
                          || AbsDiff(seg.Rank, segs[j + eventDir.Value].Rank) > 1
                          || (Math.Abs(verticalCount) >= seg.MaxConnect
                              || Math.Abs(verticalCount) >= segs[j + eventDir.Value].MaxConnect);

                    if (!isEvent)
                    {
                        // Then this edge of the current segment is irrelevant because it will
                        // be connected with the next segment in the event_dir direction.
                        // If we skip the left edge here, the right edge of
                        // the previous segment has already been skipped since
                        // the conditions are symmetric
                        continue;
                    }

                    current.VerticalCount = verticalCount;
                    current.Horizontal[eventDir] = seg.StemX;
                    if (seg.Dir == eventDir)
                    {
                        // then we are examining the edge of a beam segment that is furthest
                        // from its stem.
                        if (onLineBound
                            && me.GetBound(eventDir) != null
                            && me.GetBound(eventDir).BreakStatusDirection().IsNonZero)
                        {
                            current.Horizontal[eventDir]
                                = AxisGroupInterfaceVertical.GenericBoundExtent(
                                      me.GetBound(eventDir), commonx, Axis.X)[Direction.Positive]
                                  + (eventDir.Value * breakOvershoot[eventDir]);
                        }
                        else
                        {
                            Grob stem = stems[seg.StemIndex];
                            DrulArray<double> beamletLength = ReadDrul(
                                stem.GetProperty(BeamletDefaultLengthSymbol),
                                new DrulArray<double>(1.1, 1.1));
                            DrulArray<double> maxProportion = ReadDrul(
                                stem.GetProperty(BeamletMaxLengthProportionSymbol),
                                new DrulArray<double>(0.75, 0.75));
                            double length = beamletLength[seg.Dir];

                            if (insideStem)
                            {
                                Grob neighborStem = stems[seg.StemIndex + eventDir.Value];
                                double neighborStemX
                                    = neighborStem.RelativeCoordinate(commonx, Axis.X);

                                length = Math.Min(
                                    length,
                                    Math.Abs(neighborStemX - seg.StemX) * maxProportion[seg.Dir]);
                            }

                            current.Horizontal[eventDir] += eventDir.Value * length;
                        }
                    }
                    else
                    {
                        // we are examining the edge of a beam segment that is closest
                        // (ie. touching, unless there is a gap) its stem.
                        current.Horizontal[eventDir] += eventDir.Value * seg.Width / 2;
                        if (seg.Gapped)
                        {
                            current.Horizontal[eventDir]
                                -= eventDir.Value * gapLengths[eventDir];

                            if (Stem.IsInvisible(seg.Stem))
                            {
                                /*
                                  Need to do this in case of whole notes. We don't want the
                                  heads to collide with the beams.
                                 */
                                foreach (Grob head in
                                         PointerGroupInterface.ExtractGrobSet(
                                             seg.Stem, NoteHeadsSymbol))
                                {
                                    current.Horizontal[eventDir]
                                        = eventDir.Value
                                          * Math.Min(
                                              eventDir.Value * current.Horizontal[eventDir],
                                              (-gapLengths[eventDir] / 2)
                                              + (eventDir.Value
                                                 * head.Extent(commonx, Axis.X)[-eventDir]));
                                }
                            }
                        }
                    }

                    if (eventDir == Direction.Positive)
                    {
                        segments.Add(current);
                        current = new BeamSegment();
                    }
                }
            }
        }

        object segmentsScm = Nil.Instance;

        for (int i = segments.Count; i-- > 0;)
        {
            object entry = new Pair(
                new Pair(VerticalCountSymbol, (long)segments[i].VerticalCount),
                new Pair(
                    new Pair(HorizontalSymbol, ToPair(segments[i].Horizontal)),
                    Nil.Instance));
            segmentsScm = new Pair(entry, segmentsScm);
        }

        return segmentsScm;
    }

    /// <summary>The beam's horizontal span — <c>ly:beam::calc-x-positions</c>.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The span, as a number pair.</returns>
    public static object CalcXPositions(Spanner me)
    {
        object segments = me.GetProperty(BeamSegmentsSymbol);
        Interval xPositions = Interval.Empty;
        xPositions.SetEmpty();
        for (object cursor = segments; cursor is Pair pair; cursor = pair.Cdr)
        {
            xPositions.Unite(ReadInterval(
                Stem.LyAssocGet(HorizontalSymbol, pair.Car, Nil.Instance),
                new Interval(0.0, 0.0)));
        }

        // Case for beams without segments (i.e. uniting two skips with a beam)
        // TODO: should issue a warning?  warning likely issued downstream, but couldn't hurt...
        if (xPositions.IsEmpty)
        {
            IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
            Grob commonX = Stem.CommonRefpointOfArray(stems, me, Axis.X);
            foreach (Direction d in BothDirections)
            {
                xPositions[d] = me.RelativeCoordinate(commonX, Axis.X);
            }
        }

        return ToPair(xPositions);
    }

    /// <summary>Reads the beam's stored segments back out of its property.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The segments.</returns>
    public static List<BeamSegment> GetBeamSegments(Grob me)
    {
        object segmentsScm = me.GetProperty(BeamSegmentsSymbol);
        List<BeamSegment> segments = new List<BeamSegment>();
        for (object cursor = segmentsScm; cursor is Pair pair; cursor = pair.Cdr)
        {
            BeamSegment segment = new BeamSegment
            {
                VerticalCount = (int)ToLongOr(
                    Stem.LyAssocGet(VerticalCountSymbol, pair.Car, Nil.Instance), 0),
                Horizontal = ReadInterval(
                    Stem.LyAssocGet(HorizontalSymbol, pair.Car, Nil.Instance),
                    new Interval(0.0, 0.0)),
            };
            segments.Add(segment);
        }

        return segments;
    }

    /// <summary>Draws the beam — <c>ly:beam::print</c>.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The stencil, or the empty list when there is nothing to draw.</returns>
    public static object Print(Spanner me)
    {
        /*
          TODO - mild code dup for all the commonx calls.
          Some use just common_refpoint_of_array, some (in print and
          calc_beam_segments) use this plus calls to get_bound.

          Figure out if there is any particular reason for this and
          consolidate in one Beam::get_common function.
        */
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
        Grob commonx = Stem.CommonRefpointOfArray(stems, me, Axis.X);
        foreach (Direction d in BothDirections)
        {
            commonx = CommonWithBound(me, commonx, d);
        }

        List<BeamSegment> segments = GetBeamSegments(me);

        if (segments.Count == 0)
        {
            return Nil.Instance;
        }

        double blot = me.Layout == null ? 0.0 : me.Layout.GetDimension(BlotDiameterSymbol);

        object posns = me.GetProperty(QuantizedPositionsSymbol);
        Interval span = ReadInterval(me.GetProperty(XPositionsSymbol), new Interval(0, 0));
        DrulArray<double> pos;
        if (!IsNumberPair(posns))
        {
            Warn.ProgrammingError("no beam positions?");
            pos = new DrulArray<double>(0.0, 0.0);
        }
        else
        {
            pos = ReadDrul(posns, new DrulArray<double>(0.0, 0.0));
        }

        ScaleDrul(ref pos, StaffSymbolReferencer.StaffSpace(me));

        double dy = pos[Direction.Positive] - pos[Direction.Negative];
        double slope = (dy != 0.0 && span.Length != 0.0) ? dy / span.Length : 0.0;

        double beamThickness = GetBeamThickness(me);
        double beamDy = GetBeamTranslation(me);

        Direction featherDir = Stem.FromScmDirection(me.GetProperty(GrowDirectionSymbol));

        Interval placements = ReadInterval(
            me.GetProperty(NormalizedEndpointsSymbol), new Interval(0.0, 0.0));

        Stencil theBeam = new Stencil();

        // Upstream stores this in a vsize and indexes `segments` with it, which is
        // undefined behaviour for a negative beam rank; the port clamps into range.
        // See PORT-COVERAGE, BEAM PRINT EXTREME INDEX.
        int extreme = segments[0].VerticalCount == 0
            ? segments[0].VerticalCount
            : segments[segments.Count - 1].VerticalCount;
        if (extreme < 0 || extreme >= segments.Count)
        {
            extreme = 0;
        }

        for (int i = 0; i < segments.Count; i++)
        {
            double localSlope = slope;

            /*
              Makes local slope proportional to the ratio of the length of this beam
              to the total length.
            */
            if (featherDir.IsNonZero)
            {
                localSlope += featherDir.Value * segments[i].VerticalCount * beamDy
                              * placements.Length / span.Length;
            }

            Stencil b = Lookup.Beam(
                localSlope, segments[i].Horizontal.Length, beamThickness, blot);

            b.TranslateAxis(segments[i].Horizontal[Direction.Negative], Axis.X);
            double multiplier = featherDir.IsNonZero ? placements[Direction.Negative] : 1.0;

            Interval weights = new Interval(1 - multiplier, multiplier);

            if (featherDir != Direction.Negative)
            {
                weights.Swap();
            }

            // we need two translations: the normal one and
            // the one of the lowest segment
            int[] idx = { i, extreme };
            double[] translations = new double[2];

            for (int j = 0; j < 2; j++)
            {
                translations[j]
                    = (slope * (segments[idx[j]].Horizontal[Direction.Negative] - span.Center))
                      + ((pos[Direction.Negative] + pos[Direction.Positive]) / 2)
                      + (beamDy * segments[idx[j]].VerticalCount);
            }

            double weightedAverage
                = (translations[0] * weights[Direction.Negative])
                  + (translations[1] * weights[Direction.Positive]);

            /*
              Tricky.  The manipulation of the variable `weighted_average' below ensures
              that beams with a RIGHT grow direction will start from the position of the
              lowest segment at 0, and this error will decrease and decrease over the
              course of the beam.  Something with a LEFT grow direction, on the other
              hand, will always start in the correct place but progressively accrue
              error at broken places.  This code shifts beams up given where they are
              in the total span length (controlled by the variable `multiplier').  To
              better understand what it does, try commenting it out: you'll see that
              all of the RIGHT growing beams immediately start too low and get better
              over line breaks, whereas all of the LEFT growing beams start just right
              and get worse over line breaks.
            */
            double factor = new Interval(multiplier, 1 - multiplier)
                .LinearCombination(featherDir.Value);

            if (segments[0].VerticalCount < 0 && featherDir.IsNonZero)
            {
                double n = segments.Count - 1;
                weightedAverage += beamDy * n * factor;
            }

            b.TranslateAxis(weightedAverage, Axis.Y);

            theBeam.AddStencil(b);
        }

        object annotation = me.GetProperty(AnnotationSymbol);
        if (annotation is string || annotation is MutableString)
        {
            /*
              This code prints the demerits for each beam. Perhaps this
              should be switchable for those who want to twiddle with the
              parameters.
            */
            Direction stemDir
                = stems.Count != 0
                    ? Stem.FromScmDirection(stems[0].GetProperty(DirectionSymbol))
                    : Direction.Positive;

            Stencil score = TextInterface.GrobInterpretMarkup(me, annotation);

            if (!score.IsEmpty)
            {
                score.TranslateAxis(me.RelativeCoordinate(commonx, Axis.X), Axis.X);
                theBeam.AddAtEdge(Axis.Y, stemDir, score, 1.0);
            }
        }

        theBeam.TranslateAxis(-me.RelativeCoordinate(commonx, Axis.X), Axis.X);
        return theBeam;
    }

    /// <summary>Works out which way the beam should point when nothing forces it.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The direction.</returns>
    internal static Direction GetDefaultDir(Grob me)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);

        DrulArray<double> extremes = new DrulArray<double>(0.0, 0.0);
        foreach (Grob s in stems)
        {
            Interval positions = Stem.HeadPositions(s);
            foreach (Direction d in DownUp)
            {
                if (new Direction(positions[d]) == d)
                {
                    extremes[d] = d.Value * Math.Max(d.Value * positions[d], d.Value * extremes[d]);
                }
            }
        }

        DrulArray<int> total = new DrulArray<int>(0, 0);
        DrulArray<int> count = new DrulArray<int>(0, 0);

        bool forceDir = false;
        foreach (Grob s in stems)
        {
            Direction stemDir;
            object stemDirScm = s.GetPropertyData(DirectionSymbol);
            if (SchemeConvert.IsNumber(stemDirScm))
            {
                stemDir = Stem.FromScmDirection(stemDirScm);
                forceDir = true;
            }
            else
            {
                stemDir = Stem.FromScmDirection(s.GetProperty(DefaultDirectionSymbol));
            }

            if (!stemDir.IsNonZero)
            {
                stemDir = Stem.FromScmDirection(s.GetProperty(NeutralDirectionSymbol));
            }

            if (stemDir.IsNonZero)
            {
                count[stemDir]++;
                total[stemDir] += Math.Max(
                    (int)(-stemDir.Value * Stem.HeadPositions(s)[-stemDir]), 0);
            }
        }

        if (!forceDir)
        {
            if (Math.Abs(extremes[Direction.Positive]) > -extremes[Direction.Negative])
            {
                return Direction.Negative;
            }
            else if (extremes[Direction.Positive] < -extremes[Direction.Negative])
            {
                return Direction.Positive;
            }
        }

        Direction dir = Direction.Center;
        Direction dd = new Direction(count[Direction.Positive] - count[Direction.Negative]);
        if (dd.IsNonZero)
        {
            dir = dd;
        }
        else if (count[Direction.Positive] != 0 && count[Direction.Negative] != 0
                 && (dd = new Direction(
                         (total[Direction.Positive] / count[Direction.Positive])
                         - (total[Direction.Negative] / count[Direction.Negative]))).IsNonZero)
        {
            dir = dd;
        }
        else if ((dd = new Direction(
                      total[Direction.Positive] - total[Direction.Negative])).IsNonZero)
        {
            dir = dd;
        }
        else
        {
            dir = Stem.FromScmDirection(me.GetProperty(NeutralDirectionSymbol));
        }

        return dir;
    }

    /* Set all stems with non-forced direction to beam direction.
       Urg: non-forced should become `without/with unforced' direction,
       once stem gets cleaned-up. */

    /// <summary>Points every stem that is not forced the beam's way.</summary>
    /// <param name="me">The beam.</param>
    /// <param name="d">The beam's direction.</param>
    internal static void SetStemDirections(Grob me, Direction d)
    {
        foreach (Grob s in PointerGroupInterface.ExtractGrobSet(me, StemsSymbol))
        {
            object forcedir = s.GetPropertyData(DirectionSymbol);
            if (!Stem.FromScmDirection(forcedir).IsNonZero)
            {
                Stem.SetGrobDirection(s, d);
            }
        }
    }

    /*
      Only try horizontal beams for knees.  No reliable detection of
      anything else is possible here, since we don't know funky-beaming
      settings, or X-distances (slopes!)  People that want sloped
      knee-beams, should set the directions manually.


      TODO:

      this routine should take into account the stemlength scoring
      of a possible knee/nonknee beam.
    */

    /// <summary>Splits the beam's stems across a wide gap, making a knee.</summary>
    /// <param name="me">The beam.</param>
    internal static void ConsiderAutoKnees(Grob me)
    {
        object scm = me.GetProperty(AutoKneeGapSymbol);
        if (!SchemeConvert.IsNumber(scm))
        {
            return;
        }

        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, NormalStemsSymbol);

        Grob common = Stem.CommonRefpointOfArray(stems, me, Axis.Y);
        double staffSpace = StaffSymbolReferencer.StaffSpace(me);

        List<Interval> headExtentsArray = new List<Interval>();
        foreach (Grob stem in stems)
        {
            Interval headExtents = Interval.Empty;
            if (Stem.HeadCount(stem) != 0)
            {
                headExtents = Stem.HeadPositions(stem);
                headExtents.Widen(1);
                headExtents = headExtents * (staffSpace * 0.5);

                /*
                  We could subtract beam Y position, but this routine only
                  sets stem directions, a constant shift does not have an
                  influence.
                */
                headExtents.Translate(
                    stem.PureRelativeYCoordinate(common, 0, int.MaxValue));

                if (Stem.FromScmDirection(stem.GetPropertyData(DirectionSymbol)).IsNonZero)
                {
                    Direction stemdir
                        = Stem.FromScmDirection(stem.GetProperty(DirectionSymbol));
                    headExtents[-stemdir] = -stemdir.Value * Interval.MaxSentinel;
                }
            }

            headExtentsArray.Add(headExtents);
        }

        Interval maxGap = Interval.Empty;
        double maxGapLen = 0.0;

        IReadOnlyList<Interval> allowedRegions
            = IntervalSet.IntervalUnion(headExtentsArray).Complement().Intervals;
        for (int i = allowedRegions.Count - 1; i >= 0; i--)
        {
            Interval gap = allowedRegions[i];

            /*
              the outer gaps are not knees.
            */
            if (double.IsInfinity(gap[Direction.Negative])
                || double.IsInfinity(gap[Direction.Positive]))
            {
                continue;
            }

            if (gap.Length >= maxGapLen)
            {
                maxGapLen = gap.Length;
                maxGap = gap;
            }
        }

        double beamTranslation = GetBeamTranslation(me);
        double beamThickness = GetBeamThickness(me);
        int beamCount = GetBeamCount(me);
        double heightOfBeams = (beamThickness / 2) + ((beamCount - 1) * beamTranslation);
        double threshold = Stem.ToDouble(scm, 0.0) + heightOfBeams;

        if (maxGapLen > threshold)
        {
            int j = 0;
            foreach (Grob stem in stems)
            {
                Interval headExtents = headExtentsArray[j++];

                Direction d = headExtents.Center < maxGap.Center
                    ? Direction.Positive
                    : Direction.Negative;

                stem.SetProperty(DirectionSymbol, (long)d.Value);

                headExtents.Intersect(maxGap);
            }
        }
    }

    /// <summary>
    /// How much the beam's stems are shortened — <c>ly:beam::calc-stem-shorten</c>.
    /// </summary>
    /// <param name="me">The beam.</param>
    /// <returns>The shortening.</returns>
    public static object CalcStemShorten(Grob me)
    {
        /*
          shortening looks silly for x staff beams
        */
        if (SchemeUtilities.ToBool(me.GetProperty(KneeSymbol)))
        {
            return 0L;
        }

        double forcedFraction = (double)ForcedStemCount(me) / NormalStemCount(me);

        int beamCount = GetBeamCount(me);

        object shortenList = me.GetProperty(BeamedStemShortenSymbol);
        if (!(shortenList is Pair))
        {
            return 0L;
        }

        double staffSpace = StaffSymbolReferencer.StaffSpace(me);

        object shortenElt = Stem.RobustListRef(beamCount - 1, shortenList);
        double shorten = Stem.ToDouble(shortenElt, 0.0) * staffSpace;

        shorten *= forcedFraction;

        if (shorten != 0.0)
        {
            return shorten;
        }

        return 0.0;
    }

    /// <summary>Runs the beam quanting scorer — <c>ly:beam::quanting</c>.</summary>
    /// <param name="me">The beam.</param>
    /// <param name="ysScm">The unquantized beam positions.</param>
    /// <param name="alignBrokenIntos">Whether broken pieces align.</param>
    /// <returns>The quantized positions, as a number pair.</returns>
    public static object Quanting(Grob me, object ysScm, object alignBrokenIntos)
    {
        DrulArray<double> ys = ReadDrul(
            ysScm, new DrulArray<double>(Interval.MaxSentinel, Interval.MinSentinel));
        bool cbs = SchemeUtilities.ToBool(alignBrokenIntos);

        BeamScoringProblem problem = new BeamScoringProblem(me, ys, cbs);
        ys = problem.Solve();

        return ToPair(ys);
    }

    /* Return stem end (length) information (structure Beam_stem_end):
       - Y position of the stem-end, given the Y-left, Y-right in POS for stem S.
         This Y position is relative to S.
       - In case of French beaming, individual stem length correction values will
         be set for stem S. */

    /// <summary>Works out where a stem must end to reach the beam.</summary>
    /// <param name="me">The beam.</param>
    /// <param name="stem">The stem.</param>
    /// <param name="common">The common reference points, indexed by axis.</param>
    /// <param name="xl">The beam's left edge.</param>
    /// <param name="xr">The beam's right edge.</param>
    /// <param name="featherDir">The feathering direction.</param>
    /// <param name="pos">The beam's two vertical positions.</param>
    /// <param name="frenchCount">How many beams a French-beamed stem stops short of.</param>
    /// <returns>The stem end.</returns>
    internal static BeamStemEnd CalcStemY(
        Grob me,
        Grob stem,
        Grob[] common,
        double xl,
        double xr,
        Direction featherDir,
        Interval pos,
        int frenchCount)
    {
        BeamStemEnd stemEnd = new BeamStemEnd();
        double beamTranslation = GetBeamTranslation(me);
        Direction stemDir = Stem.GetGrobDirection(stem);

        double dx = xr - xl;
        double relx
            = dx != 0.0
                ? (stem.RelativeCoordinate(common[(int)Axis.X], Axis.X) - xl) / dx
                : 0;
        double xdir = (2 * relx) - 1;

        double stemY = pos.LinearCombination(xdir);

        Slice beamSlice = Stem.BeamMultiplicity(stem);
        if (beamSlice.IsEmpty)
        {
            beamSlice = new Slice(0, 0);
        }

        Interval beamMultiplicity
            = new Interval(beamSlice[Direction.Negative], beamSlice[Direction.Positive]);

        /*
          feather dir = 1 , relx 0->1 : factor 0 -> 1
          feather dir = 0 , relx 0->1 : factor 1 -> 1
          feather dir = -1, relx 0->1 : factor 1 -> 0
         */
        double featherFactor = 1;
        if (featherDir > Direction.Center)
        {
            featherFactor = relx;
        }
        else if (featherDir < Direction.Center)
        {
            featherFactor = 1 - relx;
        }

        stemY += featherFactor * beamTranslation * beamMultiplicity[stemDir];
        double id = me.RelativeCoordinate(common[(int)Axis.Y], Axis.Y)
                    - stem.RelativeCoordinate(common[(int)Axis.Y], Axis.Y);
        stemEnd.StemY = stemY + id;

        /*
          Set French Beaming stem end shortening value for stems to be shortened
        */
        stemEnd.FrenchBeamingStemAdjustment = frenchCount * beamTranslation * featherFactor;

        return stemEnd;
    }

    /*
      Hmm.  At this time, beam position and slope are determined.  Maybe,
      stem directions and length should set to relative to the chord's
      position of the beam.  */

    /// <summary>Stretches every stem to the beam — <c>ly:beam::set-stem-lengths</c>.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The beam's positions.</returns>
    public static object SetStemLengths(Grob me)
    {
        /* trigger callbacks. */
        _ = me.GetProperty(DirectionSymbol);
        _ = me.GetProperty(BeamingSymbol);

        object posns = me.GetProperty(PositionsSymbol);

        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
        if (stems.Count == 0)
        {
            return posns;
        }

        Grob[] common = new Grob[Axes.Count];
        foreach (Axis a in new[] { Axis.X, Axis.Y })
        {
            common[(int)a] = Stem.CommonRefpointOfArray(stems, me, a);
        }

        double staffSpace = StaffSymbolReferencer.StaffSpace(me);
        DrulArray<double> p = ReadDrul(posns, new DrulArray<double>(0.0, 0.0));
        ScaleDrul(ref p, staffSpace);
        Interval pos = new Interval(p[Direction.Negative], p[Direction.Positive]);

        bool gap = false;
        double thick = 0.0;
        if (ToLongOr(me.GetProperty(GapCountSymbol), 0) != 0)
        {
            gap = true;
            thick = GetBeamThickness(me);
        }

        Interval xSpan = ReadInterval(me.GetProperty(XPositionsSymbol), new Interval(0, 0));
        Direction featherDir = Stem.FromScmDirection(me.GetProperty(GrowDirectionSymbol));

        foreach (Grob s in stems)
        {
            bool french = SchemeUtilities.ToBool(s.GetProperty(FrenchBeamingSymbol));
            int frenchCount = 0;
            if (french)
            {
                /*
                  french_count is the number of beams a particular stem length
                  must be shortened in French Beaming.  Determined by intersecting
                  left/right beaming information Slices.
                */
                object beaming = s.GetProperty(BeamingSymbol);
                Slice le = Stem.IntListToSlice((beaming as Pair)?.Car);
                Slice ri = Stem.IntListToSlice((beaming as Pair)?.Cdr);
                le.Intersect(ri);
                frenchCount = le.Length;
            }

            BeamStemEnd stemEnd = CalcStemY(
                me, s, common, xSpan[Direction.Negative], xSpan[Direction.Positive],
                featherDir, pos, frenchCount);
            double stemY = stemEnd.StemY;
            double fbStemAdjustment = stemEnd.FrenchBeamingStemAdjustment;

            /*
              Make the stems go up to the end of the beam. This doesn't matter
              for normal beams, but for tremolo beams it looks silly otherwise.
            */
            if (gap && !Stem.IsInvisible(s))
            {
                stemY += thick * 0.5 * Stem.GetGrobDirection(s).Value;
            }

            /*
              Do set_stem_positions for invisible stems too, so tuplet brackets
              have a reference point for sloping
             */
            Stem.SetStemPositions(
                s, 2 * stemY / staffSpace, 2 * fbStemAdjustment / staffSpace);
        }

        return posns;
    }

    /// <summary>Copies a beaming pattern's counts onto the beam's stems.</summary>
    /// <param name="me">The beam.</param>
    /// <param name="beaming">The pattern.</param>
    public static void SetBeaming(Grob me, BeamingPattern beaming)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);

        for (int i = 0; i < stems.Count; i++)
        {
            /*
              Don't overwrite user settings.
            */
            foreach (Direction d in BothDirections)
            {
                Grob stem = stems[i];
                object beamingProp = stem.GetProperty(BeamingSymbol);
                if (!(beamingProp is Pair beamingPair)
                    || !(Stem.IndexGetCell(beamingPair, d) is Pair))
                {
                    uint count = beaming.BeamletCount(i, d);
                    if (i > 0 && i + 1 < stems.Count && Stem.IsInvisible(stem))
                    {
                        count = Math.Min(count, beaming.BeamletCount(i, -d));
                    }

                    if (((i == 0 && d == Direction.Negative)
                         || (i == stems.Count - 1 && d == Direction.Positive))
                        && stems.Count > 1
                        && SchemeUtilities.ToBool(me.GetProperty(ClipEdgesSymbol)))
                    {
                        count = 0;
                    }

                    Stem.SetBeaming(stem, (int)count, d);
                }
            }
        }
    }

    /// <summary>How many of the beam's stems point against their default direction.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The count.</returns>
    internal static int ForcedStemCount(Grob me)
    {
        int f = 0;
        foreach (Grob s in PointerGroupInterface.ExtractGrobSet(me, NormalStemsSymbol))
        {
            /* I can imagine counting those boundaries as a half forced stem,
               but let's count them full for now. */
            Direction defdir = Stem.FromScmDirection(s.GetProperty(DefaultDirectionSymbol));

            if (Math.Abs(Stem.ChordStartY(s)) > 0.1 && defdir.IsNonZero
                && Stem.GetGrobDirection(s) != defdir)
            {
                f++;
            }
        }

        return f;
    }

    /// <summary>How many of the beam's stems are not invisible.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The count.</returns>
    public static int NormalStemCount(Grob me)
        => PointerGroupInterface.ExtractGrobSet(me, NormalStemsSymbol).Count;

    /// <summary>The first stem that is not invisible.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The stem, or <see langword="null"/>.</returns>
    public static Grob FirstNormalStem(Grob me)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, NormalStemsSymbol);
        return stems.Count != 0 ? stems[0] : null;
    }

    /// <summary>The last stem that is not invisible.</summary>
    /// <param name="me">The beam.</param>
    /// <returns>The stem, or <see langword="null"/>.</returns>
    public static Grob LastNormalStem(Grob me)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, NormalStemsSymbol);
        return stems.Count != 0 ? stems[stems.Count - 1] : null;
    }

    /*
      [TODO]

      handle rest under beam (do_post: beams are calculated now)
      what about combination of collisions and rest under beam.

      Should lookup

      rest -> stem -> beam -> interpolate_y_position ()
    */

    /// <summary>
    /// Pushes a rest clear of the beam above it — <c>ly:beam::rest-collision-callback</c>.
    /// </summary>
    /// <param name="rest">The rest.</param>
    /// <param name="prevOffset">The offset already applied.</param>
    /// <returns>The new offset.</returns>
    public static object RestCollisionCallback(Grob rest, object prevOffset)
    {
        if (!SchemeConvert.IsNumber(prevOffset))
        {
            prevOffset = 0L;
        }

        if (SchemeConvert.IsNumber(rest.GetProperty(StaffPositionSymbol)))
        {
            return prevOffset;
        }

        Grob stem = rest.GetObject(StemSymbol) as Grob;

        if (stem == null)
        {
            return prevOffset;
        }

        Grob beam = stem.GetObject(BeamSymbol) as Grob;
        if (beam == null || !beam.HasInterface(BeamInterfaceSymbol) || NormalStemCount(beam) == 0)
        {
            return prevOffset;
        }

        Grob commonY = rest.CommonRefpoint(beam, Axis.Y);

        DrulArray<double> pos = ReadDrul(
            beam.GetProperty(PositionsSymbol), new DrulArray<double>(0.0, 0.0));

        foreach (Direction dir in BothDirections)
        {
            pos[dir] += beam.RelativeCoordinate(commonY, Axis.Y);
        }

        double staffSpace = StaffSymbolReferencer.StaffSpace(rest);

        ScaleDrul(ref pos, staffSpace);

        double dy = pos[Direction.Positive] - pos[Direction.Negative];

        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(beam, StemsSymbol);
        Grob common = Stem.CommonRefpointOfArray(stems, beam, Axis.X);

        Interval xSpan = ReadInterval(
            beam.GetProperty(XPositionsSymbol), new Interval(0.0, 0.0));
        double x0 = xSpan[Direction.Negative];
        double dx = xSpan.Length;
        double slope = (dy != 0.0 && dx != 0.0) ? dy / dx : 0.0;

        Direction d = Stem.GetGrobDirection(stem);
        double stemY = pos[Direction.Negative]
                       + ((stem.RelativeCoordinate(common, Axis.X) - x0) * slope);

        double beamTranslation = GetBeamTranslation(beam);
        double beamThickness = GetBeamThickness(beam);

        /*
          TODO: this is not strictly correct for 16th knee beams.
        */
        int beamCount = Stem.BeamMultiplicity(stem).Length + 1;

        double heightOfMyBeams = (beamThickness / 2) + ((beamCount - 1) * beamTranslation);
        double beamY = stemY - (d.Value * heightOfMyBeams);

        double offset = Stem.ToDouble(prevOffset, 0.0);
        Interval restExtent = rest.Extent(rest, Axis.Y);
        restExtent.Translate(offset + rest.ParentRelative(commonY, Axis.Y));

        double restDim = restExtent[d];
        double minimumDistance
            = staffSpace
              * (Stem.ToDouble(stem.GetProperty(StemletLengthSymbol), 0.0)
                 + Stem.ToDouble(rest.GetProperty(MinimumDistanceSymbol), 0.0));

        double shift
            = d.Value * Math.Min(d.Value * (beamY - (d.Value * minimumDistance) - restDim), 0.0);

        shift /= staffSpace;

        /* Always move discretely by half spaces */
        shift = Math.Ceiling(Math.Abs(shift * 2.0)) / 2.0 * Math.Sign(shift);

        Interval staffSpan = StaffSymbolReferencer.StaffSpan(rest);
        staffSpan = staffSpan * (staffSpace / 2);

        /* Inside staff, move by whole spaces*/
        if (staffSpan.Contains(restExtent[d] + (staffSpace * shift))
            || staffSpan.Contains(restExtent[-d] + (staffSpace * shift)))
        {
            shift = Math.Ceiling(Math.Abs(shift)) * Math.Sign(shift);
        }

        return offset + (staffSpace * shift);
    }

    /*
      Estimate the position of a rest under a beam,
      using the average position of its neighboring heads.
    */

    /// <summary>
    /// Estimates a rest's position under a beam before the beam is calculated —
    /// <c>ly:beam::pure-rest-collision-callback</c>.
    /// </summary>
    /// <param name="me">The rest.</param>
    /// <param name="prevOffset">The offset already applied.</param>
    /// <returns>The new offset.</returns>
    public static object PureRestCollisionCallback(Grob me, object prevOffset)
    {
        if (!SchemeConvert.IsNumber(prevOffset))
        {
            prevOffset = 0L;
        }

        Grob stem = me.GetObject(StemSymbol) as Grob;
        if (stem == null)
        {
            return prevOffset;
        }

        Grob beam = stem.GetObject(BeamSymbol) as Grob;
        if (beam == null || NormalStemCount(beam) == 0
            || !SchemeConvert.IsNumber(beam.GetPropertyData(DirectionSymbol)))
        {
            return prevOffset;
        }

        double ss = StaffSymbolReferencer.StaffSpace(me);

        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(beam, StemsSymbol);
        List<Grob> myStems = new List<Grob>();
        int idx = -1;

        foreach (Grob s in stems)
        {
            if (Stem.HeadCount(s) != 0 || ReferenceEquals(s, stem))
            {
                myStems.Add(s);
            }

            if (ReferenceEquals(s, stem))
            {
                idx = myStems.Count - 1;
            }
        }

        Grob left;
        Grob right;

        if (idx < 0 || myStems.Count == 1)
        {
            return prevOffset;
        }
        else if (idx == 0)
        {
            left = right = myStems[1];
        }
        else if (idx == myStems.Count - 1)
        {
            left = right = myStems[idx - 1];
        }
        else
        {
            left = myStems[idx - 1];
            right = myStems[idx + 1];
        }

        /* Estimate the closest beam to be four positions away from the heads, */
        Direction beamdir = Stem.GetGrobDirection(beam);
        double beamPos = ((Stem.HeadPositions(left)[beamdir]
                           + Stem.HeadPositions(right)[beamdir]) / 2.0)
                         + (4.0 * beamdir.Value); // four staff-positions

        /* and that the closest beam never crosses staff center by more than two positions */
        beamPos = Math.Max(-2.0, beamPos * beamdir.Value) * beamdir.Value;

        double minimumDistance
            = ss
              * (Stem.ToDouble(stem.GetProperty(StemletLengthSymbol), 0.0)
                 + Stem.ToDouble(me.GetProperty(MinimumDistanceSymbol), 0.0));
        double offset = (beamPos * ss / 2.0) - (minimumDistance * beamdir.Value)
                        - me.Extent(me, Axis.Y)[beamdir];
        double previous = Stem.ToDouble(prevOffset, 0.0);

        /* Always move by a whole number of staff spaces, always away from the beam */
        offset
            = (Math.Floor(Math.Min(0.0, (offset - previous) / ss * beamdir.Value))
               * ss * beamdir.Value)
              + previous;

        return offset;
    }

    /// <summary>Whether any of the beam's stems sits on another staff.</summary>
    /// <param name="me">The beam.</param>
    /// <returns><see langword="true"/> when the beam crosses staves.</returns>
    public static bool IsCrossStaff(Grob me)
    {
        Grob staffSymbol = StaffSymbolReferencer.GetStaffSymbol(me);
        foreach (Grob s in PointerGroupInterface.ExtractGrobSet(me, StemsSymbol))
        {
            if (!ReferenceEquals(StaffSymbolReferencer.GetStaffSymbol(s), staffSymbol))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The maximum beam count among stems pointing one way.</summary>
    /// <param name="me">The beam.</param>
    /// <param name="d">The direction to count for.</param>
    /// <returns>The count.</returns>
    public static int GetDirectionBeamCount(Grob me, Direction d)
    {
        IReadOnlyList<Grob> stems = PointerGroupInterface.ExtractGrobSet(me, StemsSymbol);
        int bc = 0;

        for (int i = stems.Count; i-- > 0;)
        {
            /*
              Should we take invisible stems into account?
            */
            if (Stem.GetGrobDirection(stems[i]) == d)
            {
                bc = Math.Max(bc, Stem.BeamMultiplicity(stems[i]).Length + 1);
            }
        }

        return bc;
    }

    /// <summary>
    /// Folds one of the beam's bounds into the common horizontal reference point —
    /// upstream's <c>me->get_bound (d)->common_refpoint (commonx, X_AXIS)</c>.
    /// <para>
    /// The original null-bound guard was REMOVED AND RE-MEASURED once line breaking
    /// landed, which is what the inherit list asked for, and the measurement says
    /// KEEP IT. Upstream writes this dereference with no null check because by the time
    /// anything asks a beam to draw, the bound is guaranteed:
    /// <c>Spanner::do_break_processing</c> walks away from a spanner missing either
    /// bound, so it is never assigned to a system and never typeset. That function is
    /// ported now — and an unbounded beam STILL reaches here, on exactly one file,
    /// <c>whole-note-tremolo-direction.ly</c>, which dies with a null dereference
    /// without this line and produces its page with it.
    /// </para>
    /// <para>
    /// The cause is NOT diagnosed and it is the one the beam port recorded and could not
    /// chase: a chord-tremolo beam that reproduces only inside a FULL SWEEP and never when
    /// the file is run alone (the full-sweep-only trap). So the guard stays, its reason is
    /// now measured rather than assumed, and what it is waiting on is no longer line
    /// breaking but that
    /// diagnosis. A beam with no bounds has no stems either, so it yields no segments and
    /// draws nothing, which is the page upstream produces after <c>calc_direction</c>
    /// removes it.
    /// </para>
    /// </summary>
    internal static Grob CommonWithBound(Spanner me, Grob commonx, Direction d)
    {
        Item bound = me.GetBound(d);
        return bound != null ? bound.CommonRefpoint(commonx, Axis.X) : commonx;
    }

    // like abs(a - b) but works for both signed and unsigned
    private static int AbsDiff(int a, int b) => Math.Max(a, b) - Math.Min(a, b);

    /// <summary>
    /// <c>ly_memv</c> over a list of beam positions. The port's <c>Memq</c> compares by
    /// REFERENCE, which never matches a boxed number, so this compares by value the way
    /// <c>eqv?</c> does.
    /// </summary>
    private static bool Memv(int value, object list)
    {
        for (object cursor = list; cursor is Pair pair; cursor = pair.Cdr)
        {
            if (IsInteger(pair.Car)
                && SchemeConvert.ToLong(pair.Car, "beam position") == value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInteger(object value) => value is long || value is int;

    private static long ToLongOr(object value, long fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToLong(value, "beam property")
            : fallback;

    private static int ListLength(object list)
    {
        int n = 0;
        for (object cursor = list; cursor is Pair pair; cursor = pair.Cdr)
        {
            n++;
        }

        return n;
    }

    private static bool IsNumberPair(object value)
        => value is Pair pair
           && SchemeConvert.IsNumber(pair.Car)
           && SchemeConvert.IsNumber(pair.Cdr);

    private static void ScaleDrul(ref DrulArray<double> drul, double factor)
    {
        drul[Direction.Negative] *= factor;
        drul[Direction.Positive] *= factor;
    }

    private static DrulArray<double> ReadDrul(object value, DrulArray<double> fallback)
    {
        if (value is Pair pair
            && SchemeConvert.IsNumber(pair.Car)
            && SchemeConvert.IsNumber(pair.Cdr))
        {
            return new DrulArray<double>(
                SchemeConvert.ToDouble(pair.Car, "drul"),
                SchemeConvert.ToDouble(pair.Cdr, "drul"));
        }

        return fallback;
    }

    private static Interval ReadInterval(object value, Interval fallback)
    {
        DrulArray<double> drul = ReadDrul(value, new DrulArray<double>(
            fallback.Left, fallback.Right));
        return new Interval(drul[Direction.Negative], drul[Direction.Positive]);
    }

    private static Pair ToPair(Interval interval)
        => new Pair(interval.Left, interval.Right);

    private static Pair ToPair(DrulArray<double> drul)
        => new Pair(drul[Direction.Negative], drul[Direction.Positive]);

    /// <summary>One drawable run of beam at one vertical position.</summary>
    public sealed class BeamSegment
    {
        /// <summary>The beam's vertical rank: larger numbers sit closer to the heads.</summary>
        public int VerticalCount;

        /// <summary>The segment's horizontal extent.</summary>
        public Interval Horizontal;
    }

    /// <summary>Where a stem must end, and how far French beaming shortens it.</summary>
    public sealed class BeamStemEnd
    {
        /// <summary>The stem end's vertical position, relative to the stem.</summary>
        public double StemY;

        /// <summary>The French-beaming shortening for this stem.</summary>
        public double FrenchBeamingStemAdjustment;
    }

    /// <summary>One stem's contribution to a beam at one vertical position.</summary>
    private sealed class BeamStemSegment
    {
        internal Grob Stem;
        internal double Width;
        internal double StemX;
        internal int Rank;
        internal int StemIndex;
        internal bool Gapped;
        internal Direction Dir = Direction.Center;
        internal int MaxConnect = 1000; // infinity
    }
}
