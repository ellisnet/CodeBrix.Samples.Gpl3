/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
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

using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/slur-scoring.cc, lily/include/slur-scoring.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port.
//
// SCORER — standing rule 2 applies to this whole file.
//
// Upstream's own TODO list, kept because it is the map of what this code does NOT do:
//   - curve around flag for y coordinate
//   - short-cut: try a smaller region first
//   - handle non-visible stems better
//   - try to prune number of scoring criteria
//   - take encompass-objects more into account when determining slur shape
//   - calculate encompass scoring directly after determining slur shape
//   - optimize.
//
//   - Slur::calc_control_points is DEFINED IN THIS FILE upstream even though it belongs to
//     slur.cc's class, so `Slur` is a partial static class and its scoring half lives here.
//     Splitting it the other way would put the scorer's entry point in a file that knows
//     nothing about scoring.

/// <summary>The notes a slur covers, reduced to the geometry the scorer needs.</summary>
public sealed class EncompassInfo
{
    /// <summary>The horizontal position to measure the slur at.</summary>
    public double X;

    /// <summary>The height of the stem end on the slur's side.</summary>
    public double Stem;

    /// <summary>The height of the note head on the slur's side.</summary>
    public double Head;

    /// <summary>Initializes an empty record.</summary>
    public EncompassInfo()
    {
        X = 0.0;
        Stem = 0.0;
        Head = 0.0;
    }

    /// <summary>Returns whichever of head and stem lies further in a direction.</summary>
    /// <param name="dir">The direction to take the extreme in.</param>
    /// <returns>The extreme height.</returns>
    public double GetPoint(Direction dir)
    {
        Interval y = Interval.Empty;
        y.AddPoint(Stem);
        y.AddPoint(Head);
        return y[dir];
    }
}

/// <summary>What sits at one end of a slur.</summary>
public sealed class BoundInfo
{
    /// <summary>The stem's extents on both axes.</summary>
    public Box StemExtent;

    /// <summary>Which way the stem points.</summary>
    public Direction StemDir;

    /// <summary>The grob the slur is bound to.</summary>
    public Item Bound;

    /// <summary>The note column at this end, when there is one.</summary>
    public Grob NoteColumn;

    /// <summary>The note head the slur attaches to.</summary>
    public Grob SlurHead;

    /// <summary>The staff that note head belongs to.</summary>
    public Grob Staff;

    /// <summary>The stem at this end, when there is one.</summary>
    public Grob Stem;

    /// <summary>The flag at this end, when there is one.</summary>
    public Grob Flag;

    /// <summary>The note head's horizontal extent.</summary>
    public Interval SlurHeadXExtent;

    /// <summary>Initializes an empty record with upstream's defaults.</summary>
    public BoundInfo()
    {
        Stem = null;
        Staff = null;
        SlurHead = null;
        StemDir = Direction.Center;
        NoteColumn = null;
        StemExtent = new Box(Interval.Empty, Interval.Empty);
        SlurHeadXExtent = Interval.Empty;
    }
}

/// <summary>A grob other than a note that the slur must avoid.</summary>
public sealed class ExtraCollisionInfo
{
    /// <summary>Where within the horizontal extent the collision matters, from -1 to 1.</summary>
    public double Idx;

    /// <summary>The grob's extents on both axes.</summary>
    public Box Extents;

    /// <summary>The demerit factor, so accidentals can be treated specially.</summary>
    public double Penalty;

    /// <summary>The colliding grob.</summary>
    public Grob Grob;

    /// <summary>How the collision should be resolved — the grob's <c>avoid-slur</c>.</summary>
    public object Type;

    private static readonly Symbol AvoidSlurSymbol = Symbol.Intern("avoid-slur");

    /// <summary>Initializes an empty record.</summary>
    public ExtraCollisionInfo()
    {
        Idx = 0.0;
        Penalty = 0.0;
        Grob = null;
        Type = Nil.Instance;
        Extents = new Box(Interval.Empty, Interval.Empty);
    }

    /// <summary>Initializes a record for a grob.</summary>
    /// <param name="g">The colliding grob.</param>
    /// <param name="idx">Where within the horizontal extent the collision matters.</param>
    /// <param name="x">The grob's horizontal extent.</param>
    /// <param name="y">The grob's vertical extent.</param>
    /// <param name="p">The demerit factor.</param>
    public ExtraCollisionInfo(Grob g, double idx, Interval x, Interval y, double p)
    {
        Idx = idx;
        Extents = new Box(x, y);
        Penalty = p;
        Grob = g;
        Type = g.GetProperty(AvoidSlurSymbol);
    }
}

/// <summary>
/// Everything known about one slur's placement problem, and the enumeration of candidate
/// curves that the scorers then rank.
/// </summary>
public sealed class SlurScoreState
{
    /// <summary>The slur being placed.</summary>
    public Spanner Slur;

    /// <summary>The reference points every coordinate here is measured from, per axis.</summary>
    public Grob[] Common = new Grob[2];

    /// <summary>Whether the problem was stated successfully.</summary>
    public bool Valid;

    /// <summary>Whether either end of the slur carries a beam.</summary>
    public bool EdgeHasBeams;

    /// <summary>Whether the slur runs across a line break.</summary>
    public bool IsBroken;

    /// <summary>Whether both ends share one beam.</summary>
    public bool HasSameBeam;

    /// <summary>The height difference the music itself implies, end to end.</summary>
    public double MusicalDy;

    /// <summary>The note columns the slur covers.</summary>
    public List<Grob> NoteColumns = new List<Grob>();

    /// <summary>One record per covered note column.</summary>
    public List<EncompassInfo> EncompassInfos = new List<EncompassInfo>();

    /// <summary>One record per non-note grob to avoid.</summary>
    public List<ExtraCollisionInfo> ExtraEncompassInfos = new List<ExtraCollisionInfo>();

    /// <summary>Which way the slur bends.</summary>
    public Direction Dir;

    /// <summary>The scorer's tunables.</summary>
    public SlurScoreParameters Parameters = new SlurScoreParameters();

    /// <summary>What sits at each end.</summary>
    public DrulArray<BoundInfo> Extremes = new DrulArray<BoundInfo>(new BoundInfo(), new BoundInfo());

    /// <summary>Where each end would attach if nothing pushed it away.</summary>
    public DrulArray<Offset> BaseAttachments;

    /// <summary>Every candidate curve.</summary>
    public List<SlurConfiguration> Configurations = new List<SlurConfiguration>();

    /// <summary>One staff space, in output units.</summary>
    public double StaffSpace;

    /// <summary>The paper's line thickness.</summary>
    public double LineThickness;

    /// <summary>The slur's own thickness.</summary>
    public double Thickness;

    private static readonly Symbol NoteColumnsSymbol = Symbol.Intern("note-columns");
    private static readonly Symbol EncompassObjectsSymbol = Symbol.Intern("encompass-objects");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol MinimumLengthSymbol = Symbol.Intern("minimum-length");
    private static readonly Symbol RatioSymbol = Symbol.Intern("ratio");
    private static readonly Symbol HeightLimitSymbol = Symbol.Intern("height-limit");
    private static readonly Symbol AvoidSlurSymbol = Symbol.Intern("avoid-slur");
    private static readonly Symbol InsideSymbol = Symbol.Intern("inside");
    private static readonly Symbol AlterationSymbol = Symbol.Intern("alteration");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol ParenthesizedSymbol = Symbol.Intern("parenthesized");
    private static readonly Symbol RestoreFirstSymbol = Symbol.Intern("restore-first");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");
    private static readonly Symbol SlurInterface = Symbol.Intern("slur-interface");
    private static readonly Symbol DotsInterface = Symbol.Intern("dots-interface");
    private static readonly Symbol KeySignatureInterface = Symbol.Intern("key-signature-interface");
    private static readonly Symbol TimeSignatureInterface = Symbol.Intern("time-signature-interface");
    private static readonly Symbol ClefInterface = Symbol.Intern("clef-interface");
    private static readonly Symbol AccidentalInterfaceSymbol = Symbol.Intern("accidental-interface");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };
    private static readonly Axis[] BothAxes = { Axis.X, Axis.Y };

    /// <summary>Initializes an empty state.</summary>
    public SlurScoreState()
    {
        MusicalDy = 0.0;
        Valid = false;
        EdgeHasBeams = false;
        HasSameBeam = false;
        IsBroken = false;
        Dir = Direction.Center;
        Slur = null;
        Common[(int)Axis.X] = null;
        Common[(int)Axis.Y] = null;
    }

    /// <summary>
    /// Returns which way the slur bends, forcing a broken slur's two halves to agree.
    /// </summary>
    /// <returns>The direction.</returns>
    public Direction SlurDirection()
    {
        Grob leftNeighbor = Slur.BrokenNeighbor(Direction.Negative);

        if (leftNeighbor != null && leftNeighbor.IsLive)
        {
            return DirectionalElementInterface.GetGrobDirection(leftNeighbor);
        }

        Direction dir = DirectionalElementInterface.GetGrobDirection(Slur);

        Spanner rightNeighbor = Slur.BrokenNeighbor(Direction.Positive);
        if (rightNeighbor != null)
        {
            DirectionalElementInterface.SetGrobDirection(rightNeighbor, dir);
        }

        return dir;
    }

    /// <summary>Reduces one covered note column to the geometry the scorer needs.</summary>
    /// <param name="notecol">The note column.</param>
    /// <returns>The record.</returns>
    public EncompassInfo GetEncompassInfo(Grob notecol)
    {
        Grob stem = notecol.GetObject(StemSymbol) as Grob;
        EncompassInfo ei = new EncompassInfo();

        if (stem == null)
        {
            ei.X = notecol.RelativeCoordinate(Common[(int)Axis.X], Axis.X);
            ei.Head = ei.Stem = notecol.Extent(Common[(int)Axis.Y], Axis.Y)[Dir];
            return ei;
        }

        Direction stemDir = DirectionalElementInterface.GetGrobDirection(stem);

        Grob firstHead = NoteColumn.FirstHead(notecol);
        if (firstHead != null)
        {
            Interval headExt = firstHead.Extent(Common[(int)Axis.X], Axis.X);

            // FIXME: Is there a better option than setting to 0?
            ei.X = headExt.IsEmpty ? 0 : headExt.Center;
        }
        else
        {
            ei.X = notecol.Extent(Common[(int)Axis.X], Axis.X).Center;
        }

        Grob h = Objects.Stem.ExtremalHeads(stem)[Dir];
        if (h == null)
        {
            ei.Head = ei.Stem = notecol.Extent(Common[(int)Axis.Y], Axis.Y)[Dir];
            return ei;
        }

        ei.Head = h.Extent(Common[(int)Axis.Y], Axis.Y)[Dir];

        if (stemDir == Dir && !stem.Extent(stem, Axis.Y).IsEmpty)
        {
            ei.Stem = stem.Extent(Common[(int)Axis.Y], Axis.Y)[Dir];
            Grob b = Objects.Stem.GetBeam(stem);
            if (b != null)
            {
                ei.Stem += (int)stemDir * 0.5 * Beam.GetBeamThickness(b);
            }

            Interval x = stem.Extent(Common[(int)Axis.X], Axis.X);
            ei.X = x.IsEmpty
                ? stem.RelativeCoordinate(Common[(int)Axis.X], Axis.X)
                : x.Center;
        }
        else
        {
            ei.Stem = ei.Head;
        }

        return ei;
    }

    /// <summary>Reads what sits at each end of the slur.</summary>
    /// <returns>The two records.</returns>
    public DrulArray<BoundInfo> GetBoundInfo()
    {
        DrulArray<BoundInfo> extremes = new DrulArray<BoundInfo>(new BoundInfo(), new BoundInfo());

        Direction slurDir = Dir;

        foreach (Direction boundDir in Both)
        {
            BoundInfo info = extremes[boundDir];
            Item bound = Slur.GetBound(boundDir);
            info.Bound = bound;
            if (bound != null && bound.HasInterface(NoteColumnInterface))
            {
                Grob noteCol = bound;
                info.NoteColumn = noteCol;
                Grob stem = NoteColumn.GetStem(noteCol);
                info.Stem = stem;
                Grob flag = NoteColumn.GetFlag(noteCol);
                info.Flag = flag;

                if (stem != null)
                {
                    info.StemDir = DirectionalElementInterface.GetGrobDirection(stem);

                    foreach (Axis ax in BothAxes)
                    {
                        Interval s = stem.Extent(Common[(int)ax], ax);
                        if (flag != null)
                        {
                            s.Unite(flag.Extent(Common[(int)ax], ax));
                        }

                        if (s.IsEmpty)
                        {
                            /*
                              do not issue warning. This happens for rests and
                              whole notes.
                            */
                            s = new Interval(0, 0);
                            s.Translate(stem.RelativeCoordinate(Common[(int)ax], ax));
                        }

                        Box stemExtent = info.StemExtent;
                        stemExtent[ax] = s;
                        info.StemExtent = stemExtent;
                    }

                    info.SlurHead = Objects.Stem.ExtremalHeads(stem)[slurDir];
                }
                else
                {
                    info.SlurHead = NoteColumn.ExtremalHeads(noteCol)[slurDir];
                }

                if (info.SlurHead == null)
                {
                    info.SlurHead = NoteColumn.GetRest(noteCol);
                }
            }
            else if (bound != null && bound.HasInterface(NoteHeadInterface))
            {
                info.SlurHead = bound;
            }

            if (info.SlurHead != null)
            {
                info.SlurHeadXExtent = info.SlurHead.Extent(Common[(int)Axis.X], Axis.X);
                info.Staff = StaffSymbolReferencer.GetStaffSymbol(info.SlurHead);
            }
        }

        return extremes;
    }

    /// <summary>States the problem for one slur.</summary>
    /// <param name="me">The slur.</param>
    public void Fill(Spanner me)
    {
        Slur = me;
        NoteColumns = new List<Grob>(PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol));

        if (NoteColumns.Count == 0)
        {
            me.Suicide();
            return;
        }

        Objects.Slur.ReplaceBreakableEncompassObjects(me);
        StaffSpace = StaffSymbolReferencer.StaffSpace(me);
        LineThickness = me.Layout == null ? 0.0 : me.Layout.GetDimension(LineThicknessSymbol);
        Thickness = ReadReal(me.GetProperty(ThicknessSymbol), 1.0) * LineThickness;

        Dir = SlurDirection();
        Parameters.Fill(me);

        IReadOnlyList<Grob> columns = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);
        IReadOnlyList<Grob> extraObjects
            = PointerGroupInterface.ExtractGrobSet(me, EncompassObjectsSymbol);

        foreach (Axis a in BothAxes)
        {
            Common[(int)a] = AxisGroupInterface.CommonRefpointOfArray(columns, me, a);
            Common[(int)a]
                = AxisGroupInterface.CommonRefpointOfArray(extraObjects, Common[(int)a], a);

            foreach (Direction d in Both)
            {
                /*
                  If bound is not in note-columns, we don't want to know about
                  its Y-position
                */
                if (a != Axis.Y)
                {
                    Common[(int)a] = Common[(int)a].CommonRefpoint(me.GetBound(d), a);
                }
            }
        }

        Extremes = GetBoundInfo();
        IsBroken
            = (Extremes[Direction.Negative].NoteColumn == null
               && Extremes[Direction.Negative].SlurHead == null)
              || (Extremes[Direction.Positive].NoteColumn == null
                  && Extremes[Direction.Positive].SlurHead == null);

        HasSameBeam
            = Extremes[Direction.Negative].Stem != null
              && Extremes[Direction.Positive].Stem != null
              && Objects.Stem.GetBeam(Extremes[Direction.Negative].Stem)
                 == Objects.Stem.GetBeam(Extremes[Direction.Positive].Stem);

        BaseAttachments = GetBaseAttachments();

        DrulArray<double> endYs = GetYAttachmentRange();

        ExtraEncompassInfos = GetExtraEncompassInfos();

        Interval additionalYs = new Interval(0.0, 0.0);

        /*
          THE TWO GUARDS EPG12 PUT HERE ARE GONE (EPG15 close-out, 2026-08-08) and this
          loop is upstream's expression, character for character.

          They were put in because the port scored slurs while horizontal spacing was
          incomplete: the loop interpolates ALONG the slur, dividing by (right base
          attachment X - left base attachment X), and with most columns still at x = 0
          that span came out 0.037 on phrasing-slur-tuplet, or exactly zero. end_ys
          reached +-5455 where upstream's is +-4, and enumerate_attachments stepped half
          a staff space across it twice nested -- about 119 million configurations, which
          reads as a hang rather than as an error. Upstream may divide unguarded because
          spacing has ALWAYS run by the time a slur is scored.

          It runs here now too: EPG15 landed Paper_score::calc_breaking and
          System::break_into_pieces, so columns hold real positions before any stencil is
          asked for. Removing the guards is the re-measurement EPG12 asked for, and the
          sweep is what checks it -- a stall here would show up as a file that never
          finishes, not as a wrong page.
        */
        for (int i = 0; i < ExtraEncompassInfos.Count; i++)
        {
            if (ExtraEncompassInfos[i].Extents[Axis.X].IsEmpty)
            {
                continue;
            }

            double yPlace = Misc.LinearInterpolate(
                ExtraEncompassInfos[i].Extents[Axis.X].Center,
                BaseAttachments[Direction.Positive][Axis.X],
                BaseAttachments[Direction.Negative][Axis.X],
                endYs[Direction.Positive],
                endYs[Direction.Negative]);
            double encompassPlace = ExtraEncompassInfos[i].Extents[Axis.Y][Dir];
            if (ReferenceEquals(ExtraEncompassInfos[i].Type, InsideSymbol)
                && Direction.MinMax(Dir, encompassPlace, yPlace) == encompassPlace
                && !ExtraEncompassInfos[i].Grob.HasInterface(KeySignatureInterface)
                && !ExtraEncompassInfos[i].Grob.HasInterface(ClefInterface)
                && !ExtraEncompassInfos[i].Grob.HasInterface(TimeSignatureInterface))
            {
                foreach (Direction d in Both)
                {
                    double contribution
                        = (int)Dir
                          * (Parameters.EncompassObjectRangeOvershoot
                             + ((yPlace - encompassPlace)
                                * (Misc.Normalize(
                                       ExtraEncompassInfos[i].Extents[Axis.X].Center,
                                       BaseAttachments[Direction.Positive][Axis.X],
                                       BaseAttachments[Direction.Negative][Axis.X])
                                   + (Dir == Direction.Negative ? 0 : -1))));

                    // EPG12's second guard -- a finite-contribution test -- is gone with
                    // the first (EPG15 close-out, 2026-08-08). See the comment above the
                    // loop: it existed only for a world in which the two base attachments
                    // could share a coordinate, which line breaking has ended.
                    additionalYs[d] = Direction.MinMax(Dir, additionalYs[d], contribution);
                }
            }
        }

        foreach (Direction d in Both)
        {
            endYs[d] += additionalYs[d];
        }

        Configurations = EnumerateAttachments(endYs);
        for (int i = 0; i < NoteColumns.Count; i++)
        {
            EncompassInfos.Add(GetEncompassInfo(NoteColumns[i]));
        }

        Valid = true;

        MusicalDy = 0.0;
        foreach (Direction d in Both)
        {
            if (!IsBroken && Extremes[d].SlurHead != null)
            {
                MusicalDy += (int)d
                             * Extremes[d].SlurHead.RelativeCoordinate(
                                 Common[(int)Axis.Y], Axis.Y);
            }
        }

        EdgeHasBeams
            = (Extremes[Direction.Negative].Stem != null
               && Objects.Stem.GetBeam(Extremes[Direction.Negative].Stem) != null)
              || (Extremes[Direction.Positive].Stem != null
                  && Objects.Stem.GetBeam(Extremes[Direction.Positive].Stem) != null);

        if (IsBroken)
        {
            MusicalDy = 0.0;
        }
    }

    /// <summary>Returns the candidate closest to a pair of forced end heights.</summary>
    /// <param name="ys">The forced heights.</param>
    /// <returns>The configuration.</returns>
    public SlurConfiguration GetForcedConfiguration(Interval ys)
    {
        SlurConfiguration best = null;
        double mindist = 1e6;
        for (int i = 0; i < Configurations.Count; i++)
        {
            double d = Math.Abs(
                           Configurations[i].Attachment[Direction.Negative][Axis.Y]
                           - ys[Direction.Negative])
                       + Math.Abs(
                           Configurations[i].Attachment[Direction.Positive][Axis.Y]
                           - ys[Direction.Positive]);
            if (d < mindist)
            {
                best = Configurations[i];
                mindist = d;
            }
        }

        while (!best.Done())
        {
            best.RunNextScorer(this);
        }

        if (mindist > 1e5)
        {
            Warn.ProgrammingError("cannot find quant");
        }

        return best;
    }

    /// <summary>
    /// Runs the scorers lazily and returns the candidate that survives.
    /// </summary>
    /// <remarks>
    /// The scorers are ordered by cost, and a configuration is only ever advanced to the
    /// next one while it is still the cheapest on the heap. That is why the heap has to be
    /// libstdc++'s: a tie in demerits decides which of two candidates gets scored further,
    /// and therefore which one wins.
    /// </remarks>
    /// <returns>The best configuration.</returns>
    public SlurConfiguration GetBestCurve()
    {
        // Slur_configuration_less: "Invert" — so the heap's top is the SMALLEST score.
        ConfigurationHeap<SlurConfiguration> queue
            = new ConfigurationHeap<SlurConfiguration>((l, r) => l.Score() > r.Score());
        for (int i = 0; i < Configurations.Count; i++)
        {
            queue.Push(Configurations[i]);
        }

        SlurConfiguration best;
        while (true)
        {
            best = queue.Top();
            if (best.Done())
            {
                break;
            }

            queue.Pop();
            best.RunNextScorer(this);
            queue.Push(best);
        }

        return best;
    }

    /// <summary>Returns the horizontal reach of the objects at a breakable bound.</summary>
    /// <param name="d">Which end.</param>
    /// <returns>The extent.</returns>
    public Interval BreakableBoundExtent(Direction d)
    {
        Grob paperCol = Slur.GetBound(d).GetColumn();
        Interval ret = Interval.Empty;

        IReadOnlyList<Grob> extraEncompasses
            = PointerGroupInterface.ExtractGrobSet(Slur, EncompassObjectsSymbol);

        for (int i = 0; i < extraEncompasses.Count; i++)
        {
            if (extraEncompasses[i] is Item item && ReferenceEquals(paperCol, item.GetColumn()))
            {
                ret.Unite(LooseColumns.RobustRelativeExtent(item, Common[(int)Axis.X], Axis.X));
            }
        }

        return ret;
    }

    /*
      TODO: should analyse encompasses to determine sensible region, and
      should limit slopes available.
    */

    /// <summary>Returns how far each end may travel from its base attachment.</summary>
    /// <returns>The two heights.</returns>
    public DrulArray<double> GetYAttachmentRange()
    {
        DrulArray<double> endYs = new DrulArray<double>(0.0, 0.0);
        foreach (Direction d in Both)
        {
            if (Extremes[d].NoteColumn != null)
            {
                Interval ncExtent = Extremes[d].NoteColumn.Extent(Common[(int)Axis.Y], Axis.Y);
                if (ncExtent.IsEmpty)
                {
                    Slur.Warning("slur trying to encompass an empty note column.");
                }
                else
                {
                    endYs[d]
                        = (int)Dir
                          * Math.Max(
                              Math.Max(
                                  (int)Dir
                                    * (BaseAttachments[d][Axis.Y]
                                       + (Parameters.RegionSize * (int)Dir)),
                                  (int)Dir * ((int)Dir + ncExtent[Dir])),
                              (int)Dir * BaseAttachments[-d][Axis.Y]);
                }
            }
            else if (Extremes[d].SlurHead != null)
            {
                // allow only minimal movement
                endYs[d] = BaseAttachments[d][Axis.Y] + (0.3 * (int)Dir);
            }
            else
            {
                endYs[d] = BaseAttachments[d][Axis.Y] + (Parameters.RegionSize * (int)Dir);
            }
        }

        return endYs;
    }

    /// <summary>Returns where each end would attach if nothing pushed it away.</summary>
    /// <returns>The two attachment points.</returns>
    public DrulArray<Offset> GetBaseAttachments()
    {
        DrulArray<Offset> baseAttachment = new DrulArray<Offset>(Offset.Zero, Offset.Zero);
        foreach (Direction d in Both)
        {
            Grob stem = Extremes[d].Stem;
            Grob head = Extremes[d].SlurHead;

            double x = 0.0;
            double y = 0.0;
            if (Extremes[d].NoteColumn != null)
            {
                // fixme: X coord should also be set in this case.
                if (stem != null && !Objects.Stem.IsInvisible(stem)
                    && Extremes[d].StemDir == Dir
                    && Objects.Stem.GetBeaming(stem, -d) != 0
                    && Objects.Stem.GetBeam(stem) != null
                    && (!SpannerLess(Slur, Objects.Stem.GetBeam(stem)) || HasSameBeam))
                {
                    y = Extremes[d].StemExtent[Axis.Y][Dir];
                }
                else if (head != null)
                {
                    y = head.Extent(Common[(int)Axis.Y], Axis.Y)[Dir];
                }

                y += (int)Dir * 0.5 * StaffSpace;

                y = MoveAwayFromStaffline(y, head);

                Grob fh = NoteColumn.FirstHead(Extremes[d].NoteColumn);
                x = (fh != null
                        ? fh.Extent(Common[(int)Axis.X], Axis.X)
                        : Extremes[d].Bound.Extent(Common[(int)Axis.X], Axis.X))
                    .Center;
                if (double.IsNaN(x) || double.IsInfinity(x))
                {
                    x = Extremes[d].NoteColumn.Extent(Common[(int)Axis.X], Axis.X).Center;
                }

                if (double.IsNaN(y) || double.IsInfinity(y))
                {
                    y = Extremes[d].NoteColumn.Extent(Common[(int)Axis.Y], Axis.Y).Center;
                }
            }
            else if (head != null)
            {
                y = head.Extent(Common[(int)Axis.Y], Axis.Y).LinearCombination(0.5 * (int)Dir);

                // Don't "move_away_from_staffline" because that makes it
                // harder to recognize the specific attachment point
                x = head.Extent(Common[(int)Axis.X], Axis.X)[-d];
            }

            baseAttachment[d] = new Offset(x, y);
        }

        foreach (Direction d in Both)
        {
            if (Extremes[d].NoteColumn == null && Extremes[d].SlurHead == null)
            {
                double x = 0;
                double y = 0;

                Interval ext = BreakableBoundExtent(d);
                if (ext.IsEmpty)
                {
                    ext = AxisGroupInterfaceVertical.GenericBoundExtent(
                        Extremes[d].Bound, Common[(int)Axis.X], Axis.X);
                }

                x = ext[-d];

                Grob col = d == Direction.Negative
                    ? NoteColumns[0]
                    : NoteColumns[NoteColumns.Count - 1];

                if (!ReferenceEquals(Extremes[-d].Bound, col))
                {
                    y = LooseColumns.RobustRelativeExtent(col, Common[(int)Axis.Y], Axis.Y)[Dir];
                    y += (int)Dir * 0.5 * StaffSpace;

                    // FIXME: dead code? NoteColumn doesn't have a direction defined.
                    if (DirectionalElementInterface.GetGrobDirection(col) == Dir
                        && NoteColumn.GetStem(col) != null
                        && !Objects.Stem.IsInvisible(NoteColumn.GetStem(col)))
                    {
                        y -= (int)Dir * 1.5 * StaffSpace;
                    }
                }
                else
                {
                    y = baseAttachment[-d][Axis.Y];
                }

                y = MoveAwayFromStaffline(y, col);

                baseAttachment[d] = new Offset(x, y);
            }
        }

        foreach (Direction d in Both)
        {
            double bx = baseAttachment[d][Axis.X];
            double by = baseAttachment[d][Axis.Y];

            if (double.IsNaN(bx) || double.IsInfinity(bx))
            {
                Warn.ProgrammingError("slur attachment is inf/nan");
                bx = 0.0;
            }

            if (double.IsNaN(by) || double.IsInfinity(by))
            {
                Warn.ProgrammingError("slur attachment is inf/nan");
                by = 0.0;
            }

            baseAttachment[d] = new Offset(bx, by);
        }

        return baseAttachment;
    }

    /// <summary>Nudges a height off a staff line it would otherwise graze.</summary>
    /// <param name="y">The height.</param>
    /// <param name="onStaff">The grob whose staff to measure against.</param>
    /// <returns>The nudged height.</returns>
    public double MoveAwayFromStaffline(double y, Grob onStaff)
    {
        if (onStaff == null)
        {
            return y;
        }

        Grob staffSymbol = StaffSymbolReferencer.GetStaffSymbol(onStaff);
        if (staffSymbol == null)
        {
            return y;
        }

        double pos = (y - staffSymbol.RelativeCoordinate(Common[(int)Axis.Y], Axis.Y))
                     * 2.0 / StaffSpace;

        if (Math.Abs(pos - LibcExtension.RoundHalfwayUp(pos)) < 0.2
            && StaffSymbolReferencer.OnStaffLine(
                onStaff, (int)Math.Round(pos, MidpointRounding.ToEven)))
        {
            y += 1.5 * StaffSpace * (int)Dir / 10;
        }

        return y;
    }

    /// <summary>Returns the points the curve must stay clear of when it is shaped.</summary>
    /// <returns>The points.</returns>
    public List<Offset> GenerateAvoidOffsets()
    {
        List<Offset> avoid = new List<Offset>();
        List<Grob> encompasses = NoteColumns;

        for (int i = 0; i < encompasses.Count; i++)
        {
            if (ReferenceEquals(Extremes[Direction.Negative].NoteColumn, encompasses[i])
                || ReferenceEquals(Extremes[Direction.Positive].NoteColumn, encompasses[i]))
            {
                continue;
            }

            EncompassInfo inf = GetEncompassInfo(encompasses[i]);
            double y = (int)Dir * Math.Max((int)Dir * inf.Head, (int)Dir * inf.Stem);

            avoid.Add(new Offset(inf.X, y + ((int)Dir * Parameters.FreeHeadDistance)));
        }

        IReadOnlyList<Grob> extraEncompasses
            = PointerGroupInterface.ExtractGrobSet(Slur, EncompassObjectsSymbol);
        for (int i = 0; i < extraEncompasses.Count; i++)
        {
            if (extraEncompasses[i].HasInterface(SlurInterface))
            {
                Grob smallSlur = extraEncompasses[i];
                Bezier b = Objects.Slur.GetCurve(smallSlur);

                Offset z = b.CurvePoint(0.5);
                z += new Offset(
                    smallSlur.RelativeCoordinate(Common[(int)Axis.X], Axis.X),
                    smallSlur.RelativeCoordinate(Common[(int)Axis.Y], Axis.Y));

                z = new Offset(z.X, z.Y + ((int)Dir * Parameters.FreeSlurDistance));
                avoid.Add(z);
            }
            else if (ReferenceEquals(
                         extraEncompasses[i].GetProperty(AvoidSlurSymbol), InsideSymbol))
            {
                Grob g = extraEncompasses[i];
                Interval xe = g.Extent(Common[(int)Axis.X], Axis.X);
                Interval ye = g.Extent(Common[(int)Axis.Y], Axis.Y);

                if (!xe.IsEmpty && !ye.IsEmpty)
                {
                    avoid.Add(new Offset(xe.Center, ye[Dir]));
                }
            }
        }

        return avoid;
    }

    /// <summary>Shapes every candidate curve.</summary>
    public void GenerateCurves()
    {
        double r0 = ReadReal(Slur.GetProperty(RatioSymbol), 0.33);
        double hInf = StaffSpace * ReadReal(Slur.GetProperty(HeightLimitSymbol), 0.0);

        List<Offset> avoid = GenerateAvoidOffsets();
        for (int i = 0; i < Configurations.Count; i++)
        {
            Configurations[i].GenerateCurve(this, r0, hInf, avoid);
        }
    }

    /// <summary>Enumerates every candidate pair of endpoints.</summary>
    /// <param name="endYs">How far each end may travel.</param>
    /// <returns>The candidates.</returns>
    public List<SlurConfiguration> EnumerateAttachments(DrulArray<double> endYs)
    {
        List<SlurConfiguration> scores = new List<SlurConfiguration>();

        // Belt and braces for the same hazard the additional-Y guard above describes: a
        // non-finite range would make these loops step towards infinity for ever. An
        // endpoint with no finite room to move simply does not move.
        foreach (Direction guardDir in Both)
        {
            if (!double.IsFinite(endYs[guardDir]))
            {
                endYs[guardDir] = BaseAttachments[guardDir][Axis.Y];
            }
        }

        DrulArray<Offset> os = new DrulArray<Offset>(Offset.Zero, Offset.Zero);
        os[Direction.Negative] = BaseAttachments[Direction.Negative];
        double minimumLength
            = StaffSpace * ReadReal(Slur.GetProperty(MinimumLengthSymbol), 2.0);

        while ((int)Dir * os[Direction.Negative][Axis.Y]
               <= (int)Dir * endYs[Direction.Negative])
        {
            os[Direction.Positive] = BaseAttachments[Direction.Positive];

            while ((int)Dir * os[Direction.Positive][Axis.Y]
                   <= (int)Dir * endYs[Direction.Positive])
            {
                DrulArray<bool> attachToStem = new DrulArray<bool>(false, false);
                foreach (Direction d in Both)
                {
                    os[d] = new Offset(BaseAttachments[d][Axis.X], os[d][Axis.Y]);
                    if (Extremes[d].Stem != null
                        && !Objects.Stem.IsInvisible(Extremes[d].Stem)
                        && Extremes[d].StemDir == Dir)
                    {
                        Interval stemY = Extremes[d].StemExtent[Axis.Y];
                        stemY.Widen(0.25 * StaffSpace);
                        if (stemY.Contains(os[d][Axis.Y]))
                        {
                            os[d] = new Offset(
                                Extremes[d].StemExtent[Axis.X][-d] - ((int)d * 0.3),
                                os[d][Axis.Y]);
                            attachToStem[d] = true;
                        }
                        else if ((int)Dir * Extremes[d].StemExtent[Axis.Y][Dir]
                                   < (int)Dir * os[d][Axis.Y]
                                 && !Extremes[d].StemExtent[Axis.X].IsEmpty)
                        {
                            os[d] = new Offset(
                                Extremes[d].StemExtent[Axis.X].Center, os[d][Axis.Y]);
                        }
                    }
                }

                Offset dz;
                dz = os[Direction.Positive] - os[Direction.Negative];
                if (dz[Axis.X] < minimumLength
                    || Math.Abs(dz[Axis.Y] / dz[Axis.X]) > Parameters.MaxSlope)
                {
                    foreach (Direction d in Both)
                    {
                        if (Extremes[d].SlurHead != null && !Extremes[d].SlurHeadXExtent.IsEmpty)
                        {
                            os[d] = new Offset(
                                Extremes[d].SlurHeadXExtent.Center, os[d][Axis.Y]);
                            attachToStem[d] = false;
                        }
                    }
                }

                dz = (os[Direction.Positive] - os[Direction.Negative]).Direction();
                foreach (Direction d in Both)
                {
                    if (Extremes[d].SlurHead != null && !attachToStem[d])
                    {
                        /* Horizontally move tilted slurs a little.  Move
                           more for bigger tilts.

                           TODO: parameter */
                        os[d] = new Offset(
                            os[d][Axis.X]
                              - ((int)Dir * Extremes[d].SlurHeadXExtent.Length * dz[Axis.Y] / 3),
                            os[d][Axis.Y]);
                    }
                }

                scores.Add(SlurConfiguration.NewConfig(os, scores.Count));

                os[Direction.Positive] = new Offset(
                    os[Direction.Positive][Axis.X],
                    os[Direction.Positive][Axis.Y] + ((int)Dir * StaffSpace / 2));
            }

            os[Direction.Negative] = new Offset(
                os[Direction.Negative][Axis.X],
                os[Direction.Negative][Axis.Y] + ((int)Dir * StaffSpace / 2));
        }

        return scores;
    }

    /// <summary>Reduces every non-note grob the slur must avoid to a collision box.</summary>
    /// <returns>The records.</returns>
    public List<ExtraCollisionInfo> GetExtraEncompassInfos()
    {
        IReadOnlyList<Grob> encompasses
            = PointerGroupInterface.ExtractGrobSet(Slur, EncompassObjectsSymbol);
        List<ExtraCollisionInfo> collisionInfos = new List<ExtraCollisionInfo>();
        for (int i = encompasses.Count; i-- > 0;)
        {
            if (encompasses[i].HasInterface(SlurInterface))
            {
                Spanner smallSlur = encompasses[i] as Spanner;
                Bezier b = Objects.Slur.GetCurve(smallSlur);

                Offset relative = new Offset(
                    smallSlur.RelativeCoordinate(Common[(int)Axis.X], Axis.X),
                    smallSlur.RelativeCoordinate(Common[(int)Axis.Y], Axis.Y));

                for (int k = 0; k < 3; k++)
                {
                    Direction hdir = new Direction((long)(k - 1));

                    /*
                      Only take bound into account if small slur starts
                      together with big slur.
                    */
                    if (hdir != Direction.Center
                        && !ReferenceEquals(smallSlur.GetBound(hdir), Slur.GetBound(hdir)))
                    {
                        continue;
                    }

                    Offset z = b.CurvePoint(k / 2.0);
                    z += relative;

                    Interval yext = Interval.Empty;
                    yext.SetFull();
                    yext[Dir] = z[Axis.Y] + ((int)Dir * Thickness * 1.0);

                    Interval xext = new Interval(-1, 1);
                    xext = new Interval(
                        (xext.Left * (Thickness * 2)) + z[Axis.X],
                        (xext.Right * (Thickness * 2)) + z[Axis.X]);
                    ExtraCollisionInfo info = new ExtraCollisionInfo(
                        smallSlur, hdir, xext, yext, Parameters.ExtraObjectCollisionPenalty);
                    collisionInfos.Add(info);
                }
            }
            else
            {
                Grob g = encompasses[i];
                Interval xe = g.Extent(Common[(int)Axis.X], Axis.X);
                Interval ye = g.Extent(Common[(int)Axis.Y], Axis.Y);
                if (g.HasInterface(DotsInterface))
                {
                    ye.Widen(0.2);
                }

                double xp = 0.0;
                double penalty = Parameters.ExtraObjectCollisionPenalty;
                if (g.HasInterface(AccidentalInterfaceSymbol))
                {
                    penalty = Parameters.AccidentalCollision;

                    Rational alt = ReadAlteration(g.GetProperty(AlterationSymbol));
                    object scmStyle = g.GetProperty(StyleSymbol);
                    if (!(scmStyle is Symbol)
                        && !SchemeUtilities.ToBool(g.GetProperty(ParenthesizedSymbol))
                        && !SchemeUtilities.ToBool(g.GetProperty(RestoreFirstSymbol)))
                    {
                        if (alt == Pitch.FlatAlteration || alt == Pitch.DoubleFlatAlteration)
                        {
                            xp = (int)Direction.Negative;
                        }
                        else if (alt == Pitch.SharpAlteration)
                        {
                            xp = 0.5 * (int)Dir;
                        }
                        else if (alt == Pitch.NaturalAlteration)
                        {
                            xp = -(int)Dir;
                        }
                    }
                }

                ye.Widen(Thickness * 0.5);
                xe.Widen(Thickness * 1.0);
                ExtraCollisionInfo info = new ExtraCollisionInfo(g, xp, xe, ye, penalty);
                collisionInfos.Add(info);
            }
        }

        return collisionInfos;
    }

    // upstream's free spanner_less (slur-scoring.cc): true when s2 strictly encloses s1.
    private static bool SpannerLess(Spanner s1, Spanner s2)
    {
        Slice b1 = Slice.Empty;
        Slice b2 = Slice.Empty;
        foreach (Direction d in Both)
        {
            b1[d] = s1.GetBound(d).GetColumn().Rank;
            b2[d] = s2.GetBound(d).GetColumn().Rank;
        }

        return b2[Direction.Negative] <= b1[Direction.Negative]
               && b2[Direction.Positive] >= b1[Direction.Positive]
               && (b2[Direction.Negative] != b1[Direction.Negative]
                   || b2[Direction.Positive] != b1[Direction.Positive]);
    }

    private static double ReadReal(object value, double fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "slur") : fallback;

    private static Rational ReadAlteration(object value)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToRational(value, "alteration")
            : new Rational(0);
}

/// <summary>The scoring half of <see cref="Slur"/>, which upstream also keeps here.</summary>
public static partial class Slur
{
    private static readonly Symbol PositionsSymbol = Symbol.Intern("positions");
    private static readonly Symbol InspectQuantsSymbol = Symbol.Intern("inspect-quants");
    private static readonly Symbol DebugSlurScoringSymbol = Symbol.Intern("debug-slur-scoring");
    private static readonly Symbol SlurAnnotationSymbol = Symbol.Intern("annotation");

    /// <summary>The <c>control-points</c> callback: states the problem and picks a curve.</summary>
    /// <param name="me">The slur.</param>
    /// <returns>The control points, as a Scheme list.</returns>
    public static object CalcControlPoints(Spanner me)
    {
        SlurScoreState state = new SlurScoreState();
        state.Fill(me);

        if (!state.Valid)
        {
            return Nil.Instance;
        }

        if (state.Configurations.Count == 0)
        {
            me.Warning("no viable slur configuration found");
            return Nil.Instance;
        }

        state.GenerateCurves();

        object endYs = me.GetProperty(PositionsSymbol);
        object inspectQuants = me.GetProperty(InspectQuantsSymbol);
        bool debugSlurs = SchemeUtilities.ToBool(
            me.Layout?.LookupVariable(DebugSlurScoringSymbol));

        if (Grob.TryNumberPair(inspectQuants, out Interval inspected))
        {
            debugSlurs = true;
            endYs = inspectQuants;
        }

        SlurConfiguration best;
        if (Grob.TryNumberPair(endYs, out Interval forced))
        {
            best = state.GetForcedConfiguration(forced);
        }
        else
        {
            best = state.GetBestCurve();
        }

        if (debugSlurs)
        {
            string total = best.Card();
            total += " TOTAL=" + best.Score().ToString("F2", CultureInfo.InvariantCulture)
                     + " idx=" + best.Index.ToString(CultureInfo.InvariantCulture);
            me.SetProperty(SlurAnnotationSymbol, total);
        }

        object controls = Nil.Instance;
        for (int i = 4; i-- > 0;)
        {
            Offset o = best.Curve[i]
                       - new Offset(
                           me.RelativeCoordinate(state.Common[(int)Axis.X], Axis.X),
                           me.RelativeCoordinate(state.Common[(int)Axis.Y], Axis.Y));
            controls = new Pair(Stencil.OffsetToScm(o), controls);
        }

        return controls;
    }
}
