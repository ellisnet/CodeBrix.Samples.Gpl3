/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Jan Nieuwenhuizen <janneke@gnu.org>

  TODO: This is way too hairy

  TODO: fix naming.

  Stem-end, chord-start, etc. is all confusing naming.

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
  Note that several internal functions have a calc_beam bool argument.
  This argument means: "If set, acknowledge the fact that there is a beam
  and deal with it.  If not, give me the measurements as if there is no beam."
  Most pure functions are called WITHOUT calc_beam, whereas non-pure functions
  are called WITH calc_beam.

  The only exception to this is ::pure_height, which calls internal_pure_height
  with "true" for calc_beam in order to trigger the calculations of other
  pure heights in case there is a beam.  It passes false, however, to
  internal_height and internal_pure_height for all subsequent iterations.
*/

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/stem.cc, lily/include/stem.hh, lily/include/stem-info.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/*
  Parameters for a stem, (multiply with stemdirection, to get real values
  for a downstem.)
*/

/// <summary>
/// Parameters for a stem under a beam: the vertical position the beam would like the
/// stem to end at and the shortest it may be squeezed to, both computed as if the stem
/// pointed UP. Multiply by the direction to get real values for a downstem.
/// </summary>
public struct StemInfo
{
    /// <summary>Gets or sets the stem's direction.</summary>
    public Direction Dir { get; set; }

    /// <summary>Gets or sets the ideal stem-end position.</summary>
    public double IdealY { get; set; }

    /// <summary>Gets or sets the shortest acceptable stem-end position.</summary>
    public double ShortestY { get; set; }

    /// <summary>Scales both positions, which is how grace-note stems shrink.</summary>
    /// <param name="x">The factor.</param>
    public void Scale(double x)
    {
        IdealY *= x;
        ShortestY *= x;
    }
}

/// <summary>
/// The stem: the graphical stem itself, and the internal connection point between note
/// heads, beams and tremolos. Rests and whole notes have INVISIBLE stems — the grob
/// still exists, and downstream spacing depends on its presence.
/// <para>
/// Grobs are generic <see cref="Item"/> instances; this class carries the logic, as
/// upstream's static <c>Stem</c> members do.
/// </para>
/// </summary>
public static class Stem
{
    private static readonly Symbol BeamingSymbol = Symbol.Intern("beaming");
    private static readonly Symbol BeamSymbol = Symbol.Intern("beam");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol RestsSymbol = Symbol.Intern("rests");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol StemletLengthSymbol = Symbol.Intern("stemlet-length");
    private static readonly Symbol StemBeginPositionSymbol = Symbol.Intern("stem-begin-position");
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");
    private static readonly Symbol FrenchBeamingStemAdjustmentSymbol
        = Symbol.Intern("french-beaming-stem-adjustment");

    private static readonly Symbol DurationLogSymbol = Symbol.Intern("duration-log");
    private static readonly Symbol DetailsSymbol = Symbol.Intern("details");
    private static readonly Symbol LengthsSymbol = Symbol.Intern("lengths");
    private static readonly Symbol StemShortenSymbol = Symbol.Intern("stem-shorten");
    private static readonly Symbol LengthFractionSymbol = Symbol.Intern("length-fraction");
    private static readonly Symbol TremoloFlagSymbol = Symbol.Intern("tremolo-flag");
    private static readonly Symbol NoStemExtendSymbol = Symbol.Intern("no-stem-extend");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol HarmonicSymbol = Symbol.Intern("harmonic");
    private static readonly Symbol NoteCollisionThresholdSymbol
        = Symbol.Intern("note-collision-threshold");

    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol DefaultDirectionSymbol = Symbol.Intern("default-direction");
    private static readonly Symbol NeutralDirectionSymbol = Symbol.Intern("neutral-direction");
    private static readonly Symbol AvoidNoteHeadSymbol = Symbol.Intern("avoid-note-head");
    private static readonly Symbol QuantizedPositionsSymbol = Symbol.Intern("quantized-positions");
    private static readonly Symbol NormalStemsSymbol = Symbol.Intern("normal-stems");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol BlotDiameterSymbol = Symbol.Intern("blot-diameter");
    private static readonly Symbol StemInfoSymbol = Symbol.Intern("stem-info");
    private static readonly Symbol BeamedLengthsSymbol = Symbol.Intern("beamed-lengths");
    private static readonly Symbol BeamedMinimumFreeLengthsSymbol
        = Symbol.Intern("beamed-minimum-free-lengths");

    private static readonly Symbol BeamedExtremeMinimumFreeLengthsSymbol
        = Symbol.Intern("beamed-extreme-minimum-free-lengths");

    private static readonly Symbol KneeSymbol = Symbol.Intern("knee");
    private static readonly Symbol ShortenSymbol = Symbol.Intern("shorten");
    private static readonly Symbol FlagSymbol = Symbol.Intern("flag");
    private static readonly Symbol NoteHeadInterfaceSymbol = Symbol.Intern("note-head-interface");
    private static readonly Symbol RestInterfaceSymbol = Symbol.Intern("rest-interface");

    /// <summary>
    /// Records how many beams start or end at this stem on one side, as the
    /// <c>beaming</c> property's two lists.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <param name="beamCount">The number of beams; zero stores <c>#f</c> on that side.</param>
    /// <param name="d">Which side of the stem.</param>
    public static void SetBeaming(Grob me, int beamCount, Direction d)
    {
        object pair = me.GetProperty(BeamingSymbol);

        if (!(pair is Pair))
        {
            pair = new Pair(Nil.Instance, Nil.Instance);
            me.SetProperty(BeamingSymbol, pair);
        }

        Pair cell = (Pair)pair;
        object lst = IndexGetCell(cell, d);
        if (beamCount != 0)
        {
            for (int i = 0; i < beamCount; i++)
            {
                lst = new Pair((long)i, lst);
            }
        }
        else
        {
            lst = false;
        }

        IndexSetCell(cell, d, lst);
    }

    /// <summary>Returns how many beams start or end at this stem on one side.</summary>
    /// <param name="me">The stem.</param>
    /// <param name="d">Which side of the stem.</param>
    /// <returns>The count.</returns>
    public static int GetBeaming(Grob me, Direction d)
    {
        object pair = me.GetProperty(BeamingSymbol);
        if (!(pair is Pair cell))
        {
            return 0;
        }

        object lst = IndexGetCell(cell, d);
        if (lst is bool flag && !flag)
        {
            return 0;
        }

        // This list represents the vertical positions at which beams start/end at
        // this stem, so the O(n) cost of walking it is fine.
        int count = 0;
        while (lst is Pair p)
        {
            count++;
            lst = p.Cdr;
        }

        return count;
    }

    /// <summary>Returns the staff positions of the lowest and highest heads.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The positions, empty when the stem has no heads.</returns>
    public static Interval HeadPositions(Grob me)
    {
        if (HeadCount(me) != 0)
        {
            DrulArray<Grob> e = ExtremalHeads(me);
            return new Interval(
                StaffSymbolReferencer.GetPosition(e[Direction.Negative]),
                StaffSymbolReferencer.GetPosition(e[Direction.Positive]));
        }

        return Interval.Empty;
    }

    /// <summary>Returns the vertical coordinate of the head the stem starts from.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The coordinate, or zero for a headless stem.</returns>
    public static double ChordStartY(Grob me)
    {
        if (HeadCount(me) != 0)
        {
            return StaffSymbolReferencer.GetPosition(LastHead(me))
                   * StaffSymbolReferencer.StaffSpace(me) * 0.5;
        }

        return 0;
    }

    /// <summary>
    /// Stores the stem positions a beam decided on, in half staff-spaces, including the
    /// stemlet and French-beaming bookkeeping.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <param name="se">The stem-end position.</param>
    /// <param name="fc">The French-beaming stem adjustment, or zero for none.</param>
    public static void SetStemPositions(Grob me, double se, double fc)
    {
        // todo: margins
        Direction d = GetGrobDirection(me);

        Grob beam = me.GetObject(BeamSymbol) as Grob;
        if (d.IsNonZero && IsNormalStem(me)
            && d.Value * HeadPositions(me)[GetGrobDirection(me)] >= se * d.Value)
        {
            Warn.Warning("weird stem size, check for narrow beams");
        }

        // trigger note collision mechanisms
        double stemBeg = InternalCalcStemBeginPosition(me, false);
        double staffSpace = StaffSymbolReferencer.StaffSpace(me);
        double halfSpace = staffSpace * 0.5;

        Interval height = Interval.Empty;
        height[-d] = stemBeg * halfSpace;
        height[d] = se * halfSpace + BeamEndCorrective(me);

        double stemletLength = ToDouble(me.GetProperty(StemletLengthSymbol), 0.0);
        bool stemlet = stemletLength > 0.0;

        Grob lh = GetReferenceHead(me);

        if (lh == null)
        {
            if (stemlet && beam != null)
            {
                double beamTranslation = Beam.GetBeamTranslation(beam);
                double beamThickness = Beam.GetBeamThickness(beam);
                int beamCount = BeamMultiplicity(me).Length + 1;

                height[-d]
                    = height[d]
                      - d.Value
                          * (0.5 * beamThickness
                             + beamTranslation * Math.Max(0, beamCount - 1)
                             + stemletLength);
            }
            else if (!stemlet && beam != null)
            {
                height[-d] = height[d];
            }
            else if (stemlet && beam == null)
            {
                Warn.ProgrammingError("Can't have a stemlet without a beam.");
            }
        }

        me.SetProperty(StemBeginPositionSymbol, height[-d] * 2 / staffSpace);
        me.SetProperty(LengthSymbol, height.Length * 2 / staffSpace);

        if (fc != 0.0)
        {
            me.SetProperty(FrenchBeamingStemAdjustmentSymbol, fc);
        }
    }

    /* Note head that determines hshift for upstems
       WARNING: triggers direction  */

    /// <summary>Returns the note head that determines the stem's horizontal position.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The head with the widest part inside the stem.</returns>
    public static Grob SupportHead(Grob me)
    {
        IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol);
        if (heads.Count == 1)
        {
            return heads[0];
        }

        Direction d = GetGrobDirection(me);

        // Calculate the width of the part of a head that is inside the stem, i.e.,
        // leftward of an up-stem or rightward of a down-stem.
        double InsideWidth(Grob head)
        {
            Interval xExt = head.Extent(head, Axis.X);
            double attach = InternalCalcStemOffsetFromHead(me, head);
            return Math.Abs(xExt[-d] - attach);
        }

        // Choose the head with the widest part inside the stem. Upstream's
        // max_element keeps the FIRST of equal maxima, which this loop reproduces.
        Grob best = null;
        double bestWidth = double.NegativeInfinity;
        foreach (Grob head in heads)
        {
            double width = InsideWidth(head);
            if (best == null || width > bestWidth)
            {
                best = head;
                bestWidth = width;
            }
        }

        return best ?? FirstHead(me);
    }

    /// <summary>Returns how many note heads the stem carries.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The count.</returns>
    public static int HeadCount(Grob me)
        => PointerGroupInterface.Count(me, NoteHeadsSymbol);

    /* The note head which forms one end of the stem.
       WARNING: triggers direction  */

    /// <summary>Returns the head the stem BEGINS at, which depends on its direction.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The head, or <see langword="null"/> when the direction is unset.</returns>
    public static Grob FirstHead(Grob me)
    {
        Direction d = GetGrobDirection(me);
        if (d.IsNonZero)
        {
            return ExtremalHeads(me)[-d];
        }

        return null;
    }

    /* The note head opposite to the first head.  */

    /// <summary>Returns the head at the stem's far end.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The head, or <see langword="null"/> when the direction is unset.</returns>
    public static Grob LastHead(Grob me)
    {
        Direction d = GetGrobDirection(me);
        if (d.IsNonZero)
        {
            return ExtremalHeads(me)[d];
        }

        return null;
    }

    // Return a drul with (bottom-head, top-head), accepting only the heads that
    // satisfy the predicate.  The pointers are null if no head satisfies it.

    /// <summary>
    /// Returns the lowest and highest heads among those satisfying a predicate.
    /// </summary>
    /// <param name="me">The stem — or a NoteColumn, which upstream notes is not very
    /// clean but is how it was first implemented.</param>
    /// <param name="predicate">Which heads to consider.</param>
    /// <returns>The pair; either side is <see langword="null"/> when nothing matched.</returns>
    public static DrulArray<Grob> ExtremalHeadsIf(Grob me, Func<Grob, bool> predicate)
    {
        // N.B. `me` could be a NoteColumn rather than a Stem.  This isn't very clean,
        // but this was implemented here first, and rearranging it without rearranging
        // a bunch of other things might do more harm than good. [DE]
        const int inf = int.MaxValue;
        DrulArray<int> extpos = new DrulArray<int>(inf, -inf);

        DrulArray<Grob> exthead = new DrulArray<Grob>(null, null);
        IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol);

        foreach (Grob n in heads)
        {
            if (!predicate(n))
            {
                continue;
            }

            int p = StaffSymbolReferencer.GetRoundedPosition(n);

            if (p < extpos[Direction.Negative]) /* < lowest note unison: take FIRST one */
            {
                exthead[Direction.Negative] = n;
                extpos[Direction.Negative] = p;
            }

            if (p >= extpos[Direction.Positive]) /* >= highest note unison: take LAST one */
            {
                exthead[Direction.Positive] = n;
                extpos[Direction.Positive] = p;
            }
        }

        return exthead;
    }

    /*
      This function returns a drul with (bottom-head, top-head).
    */

    /// <summary>Returns the lowest and highest heads on this stem.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The pair.</returns>
    public static DrulArray<Grob> ExtremalHeads(Grob me)
        => ExtremalHeadsIf(me, _ => true);

    /* The staff positions, in ascending order.
     * If FILTER, include the main column of noteheads only */

    /// <summary>Returns the heads' staff positions, in ascending order.</summary>
    /// <param name="me">The stem.</param>
    /// <param name="filter">
    /// When set, include only the main column of note heads — the ones not shifted off
    /// the stem line.
    /// </param>
    /// <returns>The positions.</returns>
    public static List<int> NoteHeadPositions(Grob me, bool filter = false)
    {
        List<int> ps = new List<int>();
        IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol);
        Grob xref = CommonRefpointOfArray(heads, me, Axis.X);
        bool filterStemless = filter && IsInvisible(me);

        for (int i = heads.Count; i-- > 0;)
        {
            Grob n = heads[i];
            if (filter)
            {
                // The main column can include smaller notes that are shifted to align
                // with normal notes.  For example, using Emmentaler, a quarter-note
                // head sharing an up-stem with a half-note head is offset by about
                // 0.0732, and a harmonic head centered on a normal whole note is
                // offset by about 0.331.  This allowance might need to be tuned as
                // other cases are tested.
                //
                // It is illogical to limit this check to whole-note harmonics, but it
                // is being done to avoid changing the decisions of
                // Note_collision_interface in other cases.  Some of those decisions
                // do require improvement, but without a lot of diligence, the risk of
                // regressions is high.
                bool filterCentered
                    = filterStemless
                      && ReferenceEquals(n.GetProperty(StyleSymbol), HarmonicSymbol);
                Interval tolerated
                    = filterCentered ? new Interval(0.0, 0.375) : new Interval(0.0);
                double x = n.RelativeCoordinate(xref, Axis.X);
                if (!tolerated.Contains(x))
                {
                    continue;
                }
            }

            int p = StaffSymbolReferencer.GetRoundedPosition(n);
            ps.Add(p);
        }

        ps.Sort();
        return ps;
    }

    /// <summary>Attaches a note head or rest to the stem, and the stem to it.</summary>
    /// <param name="me">The stem.</param>
    /// <param name="n">The head or rest.</param>
    public static void AddHead(Grob me, Grob n)
    {
        n.SetObject(StemSymbol, me);

        if (n.HasInterface(NoteHeadInterfaceSymbol))
        {
            PointerGroupInterface.AddGrob(me, NoteHeadsSymbol, n);
        }
        else if (n.HasInterface(RestInterfaceSymbol))
        {
            PointerGroupInterface.AddGrob(me, RestsSymbol, n);
        }
    }

    /// <summary>
    /// Determines whether the stem draws nothing: whole notes and rests have stems, but
    /// invisible ones.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <returns><see langword="true"/> when nothing is drawn.</returns>
    public static bool IsInvisible(Grob me)
    {
        if (IsNormalStem(me))
        {
            return false;
        }
        else if (HeadCount(me) != 0)
        {
            return true;
        }
        else
        {
            // if there are no note-heads, we might want stemlets
            return 0.0 == ToDouble(me.GetProperty(StemletLengthSymbol), 0.0);
        }
    }

    /// <summary>
    /// Determines whether this is an ordinary drawn stem: it has heads and its duration
    /// is a half note or shorter.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <returns><see langword="true"/> for a normal stem.</returns>
    public static bool IsNormalStem(Grob me)
    {
        if (HeadCount(me) == 0)
        {
            return false;
        }

        object log = me.GetProperty(DurationLogSymbol);
        return (SchemeConvert.IsNumber(log) ? SchemeConvert.ToInt(log, "duration-log") : 0) >= 1;
    }

    /// <summary>
    /// The height of the stem as it will be before line breaking, including the other
    /// stems of its beam when it has one.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <param name="calcBeam">Whether to acknowledge a beam's effect on the answer.</param>
    /// <returns>The extent.</returns>
    public static Interval InternalPureHeight(Grob me, bool calcBeam)
    {
        if (!IsNormalStem(me))
        {
            return new Interval(0.0, 0.0);
        }

        Grob beam = me.GetObject(BeamSymbol) as Grob;

        Interval iv = InternalHeight(me, false);

        if (beam == null)
        {
            return iv;
        }

        if (calcBeam)
        {
            Interval overshoot = Interval.Empty;
            Direction dir = GetGrobDirection(me);
            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                overshoot[d] = d == dir ? dir.Value * double.PositiveInfinity : iv[d];
            }

            List<Interval> heights = new List<Interval>();
            List<Grob> myStems = new List<Grob>();
            IReadOnlyList<Grob> normalStems
                = PointerGroupInterface.ExtractGrobSet(beam, NormalStemsSymbol);
            for (int i = 0; i < normalStems.Count; i++)
            {
                if (GetGrobDirection(normalStems[i]) == dir)
                {
                    if (!ReferenceEquals(normalStems[i], me))
                    {
                        heights.Add(InternalPureHeight(normalStems[i], false));
                    }
                    else
                    {
                        heights.Add(iv);
                    }

                    myStems.Add(normalStems[i]);
                }
            }

            //iv.unite (heights.back ());
            // look for cross staff effects
            List<double> coords = new List<double>();
            Grob common = CommonRefpointOfArray(myStems, me, Axis.Y);
            double minPos = double.PositiveInfinity;
            double maxPos = double.NegativeInfinity;
            for (int i = 0; i < myStems.Count; i++)
            {
                // Upstream reads the PURE relative Y coordinate. The pure machinery is
                // EPG15's (unpure-pure-container.cc); the real coordinate is the same
                // answer for every grob with no separate pure callback, which is the
                // EPG4-recorded fallback. See PORT-COVERAGE.
                coords.Add(myStems[i].RelativeCoordinate(common, Axis.Y));
                minPos = Math.Min(minPos, coords[i]);
                maxPos = Math.Max(maxPos, coords[i]);
            }

            for (int i = 0; i < heights.Count; i++)
            {
                Interval h = heights[i];
                h[dir] += dir == Direction.Negative ? coords[i] - maxPos : coords[i] - minPos;
                heights[i] = h;
            }

            for (int i = 0; i < heights.Count; i++)
            {
                iv.Unite(heights[i]);
            }

            for (int i = 0; i < myStems.Count; i++)
            {
                CachePureHeight(myStems[i], iv, heights[i]);
            }

            iv.Intersect(overshoot);
        }

        return iv;
    }

    /// <summary>
    /// Caches a stem's pure height, clipped so a stem never claims to overshoot in its
    /// own direction.
    /// <para>
    /// Upstream stores the result on the ITEM's pure-height cache. That cache is
    /// EPG15's (the pure-property machinery); until it lands the clipped interval is
    /// computed faithfully and dropped, which only matters for beams — EPG10 — and is
    /// recorded in PORT-COVERAGE and this group's report.
    /// </para>
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <param name="iv">The whole beam's interval.</param>
    /// <param name="myIv">This stem's own interval.</param>
    public static void CachePureHeight(Grob me, Interval iv, Interval myIv)
    {
        Interval overshoot = Interval.Empty;
        Direction dir = GetGrobDirection(me);
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            overshoot[d] = d == dir ? dir.Value * double.PositiveInfinity : myIv[d];
        }

        iv.Intersect(overshoot);

        // Upstream: dynamic_cast<Item *> (me)->cache_pure_height (iv);
        // The Item pure-height cache arrives with EPG15; see the XML remarks above.
        _ = iv;
    }

    /// <summary>
    /// Computes where the stem ends, in half staff-spaces: the standard length for the
    /// duration, shortened for forced directions, stretched for tremolo flags.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <param name="calcBeam">Whether to acknowledge a beam's decision instead.</param>
    /// <returns>The stem-end position.</returns>
    public static double InternalCalcStemEndPosition(Grob me, bool calcBeam)
    {
        if (HeadCount(me) == 0)
        {
            return 0.0;
        }

        Grob beam = GetBeam(me);
        double ss = StaffSymbolReferencer.StaffSpace(me);
        Direction dir = GetGrobDirection(me);

        if (beam != null && calcBeam)
        {
            _ = beam.GetProperty(QuantizedPositionsSymbol);
            return ToDouble(me.GetProperty(LengthSymbol), 0.0)
                   + dir.Value * ToDouble(me.GetProperty(StemBeginPositionSymbol), 0.0);
        }

        /* WARNING: IN HALF SPACES */
        object details = me.GetProperty(DetailsSymbol);
        int durlog = DurationLog(me);

        double staffRad = StaffRadius(me);
        double length = 7;
        object s = LyAssocGet(LengthsSymbol, details, Nil.Instance);
        if (s is Pair)
        {
            object elem = RobustListRef(durlog - 2, s);
            object len;
            if (elem is Pair elemPair)
            {
                len = dir == Direction.Negative ? elemPair.Cdr : elemPair.Car;
            }
            else
            {
                len = elem;
            }

            length = 2 * ToDouble(len, 0.0);
        }

        /* Stems in unnatural (forced) direction should be shortened,
           according to [Roush & Gourlay] */
        Interval hp = HeadPositions(me);
        if (dir.IsNonZero && dir.Value * hp[dir] >= 0)
        {
            object sshorten = LyAssocGet(StemShortenSymbol, details, Nil.Instance);
            object scmShorten = sshorten is Pair
                ? RobustListRef(Math.Max(DurationLog(me) - 2, 0), sshorten)
                : Nil.Instance;
            double shortenProperty = 2 * ToDouble(scmShorten, 0);

            /*  change in length between full-size and shortened stems is executed gradually.
                "transition area" = stems between full-sized and fully-shortened.
                */
            double quarterStemLength = 2 * ToDouble(RobustListRef(0, s), 0.0);

            /*  shortening_step = difference in length between consecutive stem lengths
                in transition area. The bigger the difference between full-sized
                and shortened stems, the bigger shortening_step is.
                (but not greater than 1/2 and not smaller than 1/4).
                value 6 is heuristic; it determines the suggested transition slope steepnesas.
                */
            double shorteningStep = Math.Min(Math.Max(0.25, shortenProperty / 6), 0.5);

            /*  Shortening of unflagged stems should begin on the first stem that sticks
                more than 1 staffspace (2 units) out of the staff.
                Shortening of flagged stems begins in the same moment as unflagged ones,
                but not earlier than on the middle line note.
                */
            double whichStep
                = Math.Min(1.0, quarterStemLength - (2 * staffRad) - 2.0)
                  + Math.Abs(hp[dir]);
            double shorten = Math.Min(Math.Max(0.0, shorteningStep * whichStep),
                                      shortenProperty);

            length -= shorten;
        }

        length *= ToDouble(me.GetProperty(LengthFractionSymbol), 1.0);

        /* Tremolo stuff.  */
        Grob tFlag = me.GetObject(TremoloFlagSymbol) as Grob;
        if (tFlag != null && (!(me.GetObject(BeamSymbol) is Grob) || !calcBeam))
        {
            /* Crude hack: add extra space if tremolo flag is there.

            We can't do this for the beam, since we get into a loop
            (Stem_tremolo::raw_stencil () looks at the beam.) --hwn  */

            double minlen = 1.0 + 2 * StemTremolo.VerticalLength(tFlag) / ss;

            /* We don't want to add the whole extent of the flag because the trem
               and the flag can overlap partly. beam_translation gives a good
               approximation */
            if (durlog >= 3)
            {
                double beamTrans = StemTremolo.GetBeamTranslation(tFlag);

                /* the obvious choice is (durlog - 2) here, but we need a bit more space. */
                minlen += 2 * (durlog - 1.5) * beamTrans;

                /* up-stems need even a little more space to avoid collisions. This
                   needs to be in sync with the tremolo positioning code in
                   Stem_tremolo::print */
                if (dir == Direction.Positive)
                {
                    minlen += beamTrans;
                }
            }

            length = Math.Max(length, minlen + 1.0);
        }

        double stemEnd = dir.IsNonZero ? hp[dir] + dir.Value * length : 0;

        /* TODO: change name  to extend-stems to staff/center/'()  */
        bool noExtend = SchemeUtilities.ToBool(me.GetProperty(NoStemExtendSymbol));
        if (!noExtend && dir.Value * stemEnd < 0)
        {
            stemEnd = 0.0;
        }

        return stemEnd;
    }

    /* The log of the duration (Number of hooks on the flag minus two)  */

    /// <summary>Returns the stem's duration log: the number of flag hooks plus two.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The log, defaulting to 2 — a quarter note.</returns>
    public static int DurationLog(Grob me)
    {
        object s = me.GetProperty(DurationLogSymbol);
        return SchemeConvert.IsNumber(s) ? SchemeConvert.ToInt(s, "duration-log") : 2;
    }

    /// <summary>
    /// The <c>positioning-done</c> callback: aligns the heads on the stem and shifts
    /// clashing seconds to the other side of it.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool CalcPositioningDone(Grob me)
    {
        if (HeadCount(me) == 0)
        {
            return true;
        }

        me.SetProperty(PositioningDoneSymbol, true);

        List<Grob> heads
            = new List<Grob>(PointerGroupInterface.ExtractGrobSet(me, NoteHeadsSymbol));
        heads.Sort(PositionLess);
        Direction dir = GetStrictGrobDirection(me);

        if (dir < Direction.Center)
        {
            heads.Reverse();
        }

        double thick = Thickness(me);

        // Align other heads relative to the "support head."
        if (heads.Count > 1)
        {
            bool iAmInvisible = IsInvisible(me);
            Grob sup = SupportHead(me);
            double stemOffset = InternalCalcStemOffsetFromHead(me, sup);
            foreach (Grob h in heads)
            {
                if (ReferenceEquals(h, sup))
                {
                    // Skipping the calculations below is a performance optimization.
                    // Even if they were performed, they shouldn't move the support
                    // head relative to itself.
                    continue;
                }

                double amount;

                // In a whole-note chord, center harmonic heads on the normal heads.
                // Otherwise, attach all heads to the stem as defined by the font.
                if (iAmInvisible && ReferenceEquals(h.GetProperty(StyleSymbol), HarmonicSymbol))
                {
                    Interval supExtent = sup.Extent(sup, Axis.X);
                    Interval hExtent = h.Extent(h, Axis.X);
                    amount = supExtent.Center - hExtent.Center;
                }
                else
                {
                    double hOffset = InternalCalcStemOffsetFromHead(me, h);
                    amount = stemOffset - hOffset;
                }

                if (!double.IsNaN(amount)) // empty heads can produce NaN
                {
                    h.TranslateAxis(amount, Axis.X);
                }
            }
        }

        bool parity = true;
        double lastpos = StaffSymbolReferencer.GetPosition(heads[0]);
        object thresholdValue = me.GetProperty(NoteCollisionThresholdSymbol);
        int threshold = SchemeConvert.IsNumber(thresholdValue)
            ? SchemeConvert.ToInt(thresholdValue, "note-collision-threshold")
            : 1;
        for (int i = 1; i < heads.Count; i++)
        {
            double p = StaffSymbolReferencer.GetPosition(heads[i]);
            double dy = Math.Abs(lastpos - p);

            /*
              dy should always be 0.5, 0.0, 1.0, but provide safety margin
              for rounding errors.
            */
            if (dy < 0.1 + threshold)
            {
                if (parity)
                {
                    // Don't include the glyph's 'breapth' value.
                    double ell = heads[i].Extent(heads[i], Axis.X).Right;

                    Direction d = GetGrobDirection(me);

                    /*
                      Reversed heads (i.e., heads on the other side of the
                      stem) should be shifted by `ell - thickness`, but this
                      looks too crowded, so we only shift by `ell -
                      0.5*thickness`.

                      This leads to an asymmetry: Normal heads overlap the
                      stem by 100%, whereas reversed heads only overlap by
                      50%.
                    */
                    double reverseOverlap = 0.5;

                    /*
                      However, the first reverse head has to be shifted even
                      less if it has the same vertical position as the first
                      head, or there will be a gap because of the head slant
                      (issue 346).
                    */

                    if (i == 1 && dy < 0.1)
                    {
                        reverseOverlap = 1.1;
                    }

                    if (IsInvisible(me))
                    {
                        if (DurationLog(me) >= 0)
                        {
                            /*
                              Semibreves are positioned considerably nearer
                              to be recognizable as part of the chord rather
                              than being a parallel voice.  During the
                              course of issue 346 there was a discussion to
                              change this for unisons (i.e., dy < 0.1) to
                              reduce overlap but without reaching agreement,
                              and with Gould being rather on the overlapping
                              front.
                            */
                            reverseOverlap = 2;
                        }
                        else
                        {
                            /*
                              Breves and longer are offset 'exactly' so that
                              the vertical lines to the left and right of
                              the note heads align.  This is guaranteed by
                              the glyphs themselves: the left vertical
                              line(s) are in the 'breapth' area, touching
                              the horizontal origin.
                            */
                            reverseOverlap = 0;
                        }
                    }

                    heads[i].TranslateAxis((ell - thick * reverseOverlap) * d.Value, Axis.X);

                    /* TODO:

                    For some cases we should kern some more: when the
                    distance between the next or prev note is too large, we'd
                    get large white gaps, eg.

                    |
                    X|
                    |X  <- kern this.
                    |
                    X

                    */
                }

                parity = !parity;
            }
            else
            {
                parity = true;
            }

            lastpos = p;
        }

        return true;
    }

    /// <summary>
    /// The <c>direction</c> callback: a beam's decision when there is one, the natural
    /// direction otherwise, and the context's neutral direction on the middle line.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <returns>The direction, as a Scheme value.</returns>
    public static object CalcDirection(Grob me)
    {
        Direction dir = Direction.Center;
        if (me.GetObject(BeamSymbol) is Grob beam)
        {
            object ignoreMe = beam.GetProperty(DirectionSymbol);
            _ = ignoreMe;
            dir = GetGrobDirection(me);
        }
        else
        {
            object dd = me.GetProperty(DefaultDirectionSymbol);
            dir = FromScmDirection(dd);
            if (!dir.IsNonZero)
            {
                return me.GetProperty(NeutralDirectionSymbol);
            }
        }

        return (long)dir.Value;
    }

    /// <summary>
    /// The <c>default-direction</c> callback: down when the heads sit high, up when
    /// they sit low.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <returns>The direction.</returns>
    public static Direction CalcDefaultDirection(Grob me)
    {
        Direction dir = Direction.Center;
        if (HeadCount(me) != 0)
        {
            const int staffCenter = 0;
            Interval hp = HeadPositions(me);
            int udistance = (int)(hp[Direction.Positive] - staffCenter);
            int ddistance = (int)(-hp[Direction.Negative] - staffCenter);

            dir = new Direction((long)Math.Sign(ddistance - udistance));
        }

        return dir;
    }

    // note - height property necessary to trigger quantized beam positions
    // otherwise, we could just use Grob::stencil_height_proc

    /// <summary>Returns the head that decides where the stem begins.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The reference head.</returns>
    public static Grob GetReferenceHead(Grob me)
        => SchemeUtilities.ToBool(me.GetProperty(AvoidNoteHeadSymbol))
            ? LastHead(me)
            : FirstHead(me);

    /// <summary>
    /// Returns how far past the beam's centre line the stem must be drawn: half the
    /// beam's thickness, signed by direction.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <returns>The correction, or zero without a beam.</returns>
    public static double BeamEndCorrective(Grob me)
    {
        Grob beam = me.GetObject(BeamSymbol) as Grob;
        Direction dir = GetGrobDirection(me);
        if (beam != null)
        {
            if (!dir.IsNonZero)
            {
                Warn.ProgrammingError("no stem direction");
                dir = Direction.Positive;
            }

            return dir.Value * Beam.GetBeamThickness(beam) * 0.5;
        }

        return 0.0;
    }

    /// <summary>Returns the stem's vertical extent.</summary>
    /// <param name="me">The stem.</param>
    /// <param name="calcBeam">Whether to let a beam's quantized positions decide.</param>
    /// <returns>The extent.</returns>
    public static Interval InternalHeight(Grob me, bool calcBeam)
    {
        Grob beam = GetBeam(me);
        if (!IsValidStem(me) && beam == null)
        {
            return Interval.Empty;
        }

        Direction dir = GetGrobDirection(me);

        if (beam != null && calcBeam)
        {
            /* trigger set-stem-lengths. */
            _ = beam.GetProperty(QuantizedPositionsSymbol);
        }

        /*
          If there is a beam but no stem, slope calculations depend on this
          routine to return where the stem end /would/ be.
        */
        if (calcBeam && beam == null && !(me.GetProperty(StencilSymbol) is Stencil))
        {
            return Interval.Empty;
        }

        double y1 = ToDouble(
            calcBeam
                ? me.GetProperty(StemBeginPositionSymbol)
                : GetPureProperty(me, StemBeginPositionSymbol),
            0.0);

        double y2 = dir.Value
                    * ToDouble(
                        calcBeam
                            ? me.GetProperty(LengthSymbol)
                            : GetPureProperty(me, LengthSymbol),
                        0.0)
                    + y1;

        double halfSpace = StaffSymbolReferencer.StaffSpace(me) * 0.5;

        Interval stemY
            = new Interval(Math.Min(y1, y2), Math.Max(y2, y1)) * halfSpace;

        return stemY;
    }

    /// <summary>The <c>X-extent</c> callback: the stem's own width.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The extent — empty for an invisible stem.</returns>
    public static Interval Width(Grob me)
    {
        Interval r;

        if (IsInvisible(me))
        {
            r = Interval.Empty;
        }
        else
        {
            r = new Interval(-1, 1);
            r *= Thickness(me) / 2;
        }

        return r;
    }

    /// <summary>Returns the stem's line thickness.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The thickness.</returns>
    public static double Thickness(Grob me)
        => ToDouble(me.GetProperty(ThicknessSymbol), 0.0)
           * StaffSymbolReferencer.LineThickness(me);

    /// <summary>
    /// Computes where the stem begins, in half staff-spaces: at the reference head's
    /// position, adjusted by the font's stem attachment point.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <param name="calcBeam">Whether to acknowledge a beam's decision instead.</param>
    /// <returns>The stem-begin position.</returns>
    public static double InternalCalcStemBeginPosition(Grob me, bool calcBeam)
    {
        Grob beam = GetBeam(me);
        double ss = StaffSymbolReferencer.StaffSpace(me);
        if (beam != null && calcBeam)
        {
            _ = beam.GetProperty(QuantizedPositionsSymbol);
            return ToDouble(me.GetProperty(StemBeginPositionSymbol), 0.0);
        }

        Grob lh = GetReferenceHead(me);

        if (lh == null)
        {
            return 0.0;
        }

        double pos = StaffSymbolReferencer.GetPosition(lh);

        if (FirstHead(me) is Grob head)
        {
            Interval headHeight = head.Extent(head, Axis.Y);
            double yAttach = NoteHead.StemAttachmentCoordinate(head, Axis.Y);

            yAttach = headHeight.LinearCombination(yAttach);
            if (double.IsFinite(yAttach)) // empty heads
            {
                pos += yAttach * 2 / ss;
            }
        }

        return pos;
    }

    /// <summary>The <c>length</c> callback's pure half.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The length, in half staff-spaces.</returns>
    public static double PureCalcLength(Grob me)
    {
        double beg = ToDouble(GetPureProperty(me, StemBeginPositionSymbol), 0.0);
        double res = Math.Abs(InternalCalcStemEndPosition(me, false) - beg);
        return res;
    }

    /// <summary>The <c>length</c> callback: from begin position to end position.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The length, in half staff-spaces.</returns>
    public static double CalcLength(Grob me)
    {
        if (me.GetObject(BeamSymbol) is Grob)
        {
            Warn.ProgrammingError(
                "ly:stem::calc-length called but will not be used for beamed stem.");
            return 0.0;
        }

        double beg = ToDouble(me.GetProperty(StemBeginPositionSymbol), 0.0);
        double res = Math.Abs(InternalCalcStemEndPosition(me, true) - beg);
        return res;
    }

    /// <summary>
    /// Determines whether there is anything to draw: a reference head or a beam, and
    /// not an invisible stem.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <returns><see langword="true"/> when the stem draws.</returns>
    public static bool IsValidStem(Grob me)
    {
        /* TODO: make the stem start a direction ?
           This is required to avoid stems passing in tablature chords.  */
        if (me == null)
        {
            return false;
        }

        Grob lh = GetReferenceHead(me);
        Grob beam = me.GetObject(BeamSymbol) as Grob;

        if (lh == null && beam == null)
        {
            return false;
        }

        if (IsInvisible(me))
        {
            return false;
        }

        return true;
    }

    /// <summary>The <c>stencil</c> callback: the stem as a rounded filled box.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The stencil, or the empty list for an invisible stem.</returns>
    public static object Print(Grob me)
    {
        if (!IsValidStem(me))
        {
            return Nil.Instance;
        }

        Direction dir = GetGrobDirection(me);
        double y1 = ToDouble(me.GetProperty(StemBeginPositionSymbol), 0.0);
        double stemLength = ToDouble(me.GetProperty(LengthSymbol), 0.0);
        double fbStemAdjustment = ToDouble(
            me.GetProperty(FrenchBeamingStemAdjustmentSymbol), 0.0);
        double halfSpace = StaffSymbolReferencer.StaffSpace(me) * 0.5;

        /* Shorten inner French Beams (for printing) */
        stemLength -= fbStemAdjustment;

        double y2 = dir.Value * stemLength + y1;

        Interval stemY
            = new Interval(Math.Min(y1, y2), Math.Max(y2, y1)) * halfSpace;

        stemY[dir] -= BeamEndCorrective(me);

        // URG
        double stemWidth = Thickness(me);
        double blot = me.Layout == null ? 0.0 : me.Layout.GetDimension(BlotDiameterSymbol);

        Box b = new Box(new Interval(-stemWidth / 2, stemWidth / 2), stemY);

        Stencil mol = Stencil.Empty;
        Stencil ss = Lookup.RoundFilledBox(b, blot);
        mol.AddStencil(ss);

        return mol;
    }

    /// <summary>
    /// Returns the horizontal distance from a head's origin to where the stem meets it.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <param name="head">The head to measure against.</param>
    /// <param name="centerInvisible">Whether an invisible stem centres on the head.</param>
    /// <returns>The offset.</returns>
    public static double InternalCalcStemOffsetFromHead(
        Grob me, Grob head, bool centerInvisible = false)
    {
        Interval headWid = head.Extent(head, Axis.X);
        double attach;

        // To align the notes associated with this stem (whether the stem is visible
        // or not), we always use the attachment point specified by the font.  When
        // finally setting the stem offset, we center an invisible stem on the support
        // head because some things depend on that (e.g., tremolo marks).  It might be
        // cleaner overall for those things to compensate on their own: if the stem is
        // invisible, center on the support head.
        if (centerInvisible && IsInvisible(me))
        {
            attach = 0.0;
        }
        else
        {
            attach = NoteHead.StemAttachmentCoordinate(head, Axis.X);
        }

        double realAttach = headWid.LinearCombination(attach);
        double r = double.IsNaN(realAttach) ? 0.0 : realAttach;

        /* If not centered: correct for stem thickness.  */
        const double epsilon = 1e-3; // compensate rounding in font
        string style = RobustSymbolToString(head.GetProperty(StyleSymbol), "default");
        if (Math.Abs(attach) > epsilon && style != "neomensural"
            && style != "petrucci" && style != "blackpetrucci"
            && style != "semipetrucci")
        {
            Direction d = GetGrobDirection(me);
            double ruleThick = Thickness(me);
            if (style == "mensural")
            {
                ruleThick /= -2;
            }

            r += -d.Value * ruleThick * 0.5;
        }

        return r;
    }

    /*
      move the stem to right of the notehead if it is up.
    */

    /// <summary>The <c>X-offset</c> callback: over the rest, or on the support head.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The offset.</returns>
    public static double OffsetCallback(Grob me)
    {
        IReadOnlyList<Grob> rests = PointerGroupInterface.ExtractGrobSet(me, RestsSymbol);
        if (rests.Count != 0)
        {
            Grob rest = rests[rests.Count - 1];
            double r = LooseColumns.RobustRelativeExtent(rest, rest, Axis.X).Center;
            return r;
        }

        if (SupportHead(me) is Grob head)
        {
            return InternalCalcStemOffsetFromHead(me, head, true);
        }

        Warn.ProgrammingError("Weird stem.");
        return 0.0;
    }

    /// <summary>Returns the beam this stem belongs to, if any.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The beam spanner, or <see langword="null"/>.</returns>
    public static Spanner GetBeam(Grob me)
        => me.GetObject(BeamSymbol) as Spanner;

    /// <summary>Reads the computed <c>stem-info</c> back off the grob.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The stem parameters.</returns>
    public static StemInfo GetStemInfo(Grob me)
    {
        StemInfo si = default;
        si.Dir = GetGrobDirection(me);

        object scmInfo = me.GetProperty(StemInfoSymbol);
        if (scmInfo is Pair info)
        {
            si.IdealY = ToDouble(info.Car, 0.0);
            if (info.Cdr is Pair rest)
            {
                si.ShortestY = ToDouble(rest.Car, 0.0);
            }
        }

        return si;
    }

    /// <summary>
    /// The <c>stem-info</c> callback: the ideal and minimum stem-end positions under a
    /// beam, from the <c>beamed-*</c> length tables tuned by decades of engraving.
    /// </summary>
    /// <param name="me">The stem.</param>
    /// <returns>A two-element list: ideal Y, then shortest Y.</returns>
    public static object CalcStemInfo(Grob me)
    {
        Direction myDir = GetGrobDirection(me);

        if (!myDir.IsNonZero)
        {
            Warn.ProgrammingError("no stem dir set");
            myDir = Direction.Positive;
        }

        double staffSpace = StaffSymbolReferencer.StaffSpace(me);
        Grob beam = GetBeam(me);

        if (beam != null)
        {
            _ = beam.GetProperty(BeamingSymbol);
        }

        double beamTranslation = Beam.GetBeamTranslation(beam);
        double beamThickness = Beam.GetBeamThickness(beam);
        int beamCount = Beam.GetDirectionBeamCount(beam, myDir);
        double lengthFraction = ToDouble(me.GetProperty(LengthFractionSymbol), 1.0);

        /* Simple standard stem length */
        object details = me.GetProperty(DetailsSymbol);
        object lengths = LyAssocGet(BeamedLengthsSymbol, details, Nil.Instance);

        double idealLength
            = lengths is Pair
                ? (ToDouble(RobustListRef(beamCount - 1, lengths), 0.0)
                     * staffSpace * lengthFraction
                   /*
                   stem only extends to center of beam
                 */
                   - 0.5 * beamThickness)
                : 0.0;

        /* Condition: sane minimum free stem length (chord to beams) */
        lengths = LyAssocGet(BeamedMinimumFreeLengthsSymbol, details, Nil.Instance);

        double idealMinimumFree
            = lengths is Pair
                ? (ToDouble(RobustListRef(beamCount - 1, lengths), 0.0)
                   * staffSpace * lengthFraction)
                : 0.0;

        double heightOfMyTrem = 0.0;
        Grob trem = me.GetObject(TremoloFlagSymbol) as Grob;
        if (trem != null)
        {
            heightOfMyTrem = StemTremolo.VerticalLength(trem)
                             /* hack a bit of space around the trem. */
                             + beamTranslation;
        }

        /* UGH
           It seems that also for ideal minimum length, we must use
           the maximum beam count (for this direction):

           \score { \relative c'' { a8[ a32] } }

           must be horizontal. */
        double heightOfMyBeams
            = beamThickness + (beamCount - 1) * beamTranslation;

        double idealMinimumLength = idealMinimumFree + heightOfMyBeams
                                    + heightOfMyTrem
                                    /* stem only extends to center of beam */
                                    - 0.5 * beamThickness;

        idealLength = Math.Max(idealLength, idealMinimumLength);

        /* Convert to Y position, calculate for dir == UP */
        double noteStart = /* staff positions */
            HeadPositions(me)[myDir] * 0.5 * myDir.Value * staffSpace;
        double idealY = noteStart + idealLength;

        /* Conditions for Y position */

        /* Lowest beam of (UP) beam must never be lower than second staffline

        Reference?

        Although this (additional) rule is probably correct,
        I expect that highest beam (UP) should also never be lower
        than middle staffline, just as normal stems.

        Reference?

        Obviously not for grace beams.

        Also, not for knees.  Seems to be a good thing. */
        bool noExtend = SchemeUtilities.ToBool(me.GetProperty(NoStemExtendSymbol));
        bool isKnee = beam != null && SchemeUtilities.ToBool(beam.GetProperty(KneeSymbol));
        if (!noExtend && !isKnee)
        {
            /* Highest beam of (UP) beam must never be lower than middle
               staffline */
            idealY = Math.Max(idealY, 0.0);

            /* Lowest beam of (UP) beam must never be lower than second staffline */
            idealY = Math.Max(
                idealY, -staffSpace - beamThickness + heightOfMyBeams);
        }

        idealY -= beam == null ? 0.0 : ToDouble(beam.GetProperty(ShortenSymbol), 0);

        object bemfl = LyAssocGet(
            BeamedExtremeMinimumFreeLengthsSymbol, details, Nil.Instance);

        double minimumFree
            = bemfl is Pair
                ? (ToDouble(RobustListRef(beamCount - 1, bemfl), 0.0)
                   * staffSpace * lengthFraction)
                : 0.0;

        double minimumLength = Math.Max(minimumFree, heightOfMyTrem)
                               + heightOfMyBeams
                               /* stem only extends to center of beam */
                               - 0.5 * beamThickness;

        idealY *= myDir.Value;
        double minimumY = noteStart + minimumLength;
        double shortestY = minimumY * myDir.Value;

        return Pair.List(idealY, shortestY);
    }

    /// <summary>Returns which beam positions the stem takes part in, on both sides.</summary>
    /// <param name="stem">The stem.</param>
    /// <returns>The united slice.</returns>
    public static Slice BeamMultiplicity(Grob stem)
    {
        object beaming = stem.GetProperty(BeamingSymbol);
        Pair pair = beaming as Pair;
        Slice le = IntListToSlice(pair?.Car);
        Slice ri = IntListToSlice(pair?.Cdr);
        le.Unite(ri);
        return le;
    }

    /// <summary>Determines whether the stem's beam crosses staves.</summary>
    /// <param name="stem">The stem.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool IsCrossStaff(Grob stem)
    {
        Grob beam = stem.GetObject(BeamSymbol) as Grob;
        return beam != null && Beam.IsCrossStaff(beam);
    }

    /// <summary>Returns the flag attached to this stem, if any.</summary>
    /// <param name="me">The stem.</param>
    /// <returns>The flag grob, or <see langword="null"/>.</returns>
    public static Grob FlagGrob(Grob me)
        => me.GetObject(FlagSymbol) as Grob;

    /*
      Shared helpers, internal so flag.cc's and stem-tremolo.cc's ports use ONE copy.
      Upstream keeps them in directional-element-interface.cc, lily-guile.cc,
      staff-symbol-referencer.cc and grob-property.cc respectively.
    */

    /// <summary>Returns a grob's direction, CENTER when unset — <c>get_grob_direction</c>.</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The direction.</returns>
    internal static Direction GetGrobDirection(Grob me)
    {
        object d = me?.GetProperty(DirectionSymbol);
        return FromScmDirection(d);
    }

    /// <summary>
    /// Returns a grob's direction, warning and forcing UP when it is CENTER —
    /// <c>get_strict_grob_direction</c>.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <returns>The direction, never CENTER.</returns>
    internal static Direction GetStrictGrobDirection(Grob me)
    {
        Direction dir = GetGrobDirection(me);
        if (!dir.IsNonZero)
        {
            Warn.Warning(
                "direction of grob " + me.Name + " must be UP or DOWN; using UP");
            SetGrobDirection(me, Direction.Positive);
            return Direction.Positive;
        }

        return dir;
    }

    /// <summary>Stores a grob's direction — <c>set_grob_direction</c>.</summary>
    /// <param name="me">The grob.</param>
    /// <param name="d">The direction.</param>
    internal static void SetGrobDirection(Grob me, Direction d)
        => me.SetProperty(DirectionSymbol, (long)d.Value);

    /// <summary>Reads a Scheme value as a direction, CENTER when it is not a number.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The direction.</returns>
    internal static Direction FromScmDirection(object value)
        => SchemeConvert.IsNumber(value)
            ? new Direction(SchemeConvert.ToLong(value, "direction"))
            : Direction.Center;

    /// <summary>Reads a Scheme value as a real, with a fallback — <c>from_scm&lt;double&gt;</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="fallback">What a non-number answers.</param>
    /// <returns>The number.</returns>
    internal static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "stem")
            : fallback;

    /// <summary>Looks a key up in an alist — <c>ly_assoc_get</c>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="alist">The alist.</param>
    /// <param name="fallback">What a missing key answers.</param>
    /// <returns>The value.</returns>
    internal static object LyAssocGet(Symbol key, object alist, object fallback)
    {
        Pair entry = SchemeUtilities.Assq(key, alist);
        return entry != null ? entry.Cdr : fallback;
    }

    /* Return I-th element, or last elt L. If I < 0, then we take the first
       element.

       PRE: length (L) > 0  */

    /// <summary>Returns a list's i-th element, clamped to its ends — <c>robust_list_ref</c>.</summary>
    /// <param name="i">The index.</param>
    /// <param name="l">The list.</param>
    /// <returns>The element.</returns>
    internal static object RobustListRef(int i, object l)
    {
        while (i-- > 0 && l is Pair p && p.Cdr is Pair)
        {
            l = p.Cdr;
        }

        return l is Pair head ? head.Car : Nil.Instance;
    }

    /// <summary>Collects a list of integers into a slice — <c>int_list_to_slice</c>.</summary>
    /// <param name="l">The list.</param>
    /// <returns>The slice.</returns>
    internal static Slice IntListToSlice(object l)
    {
        Slice s = Slice.Empty;
        for (; l is Pair p; l = p.Cdr)
        {
            if (SchemeConvert.IsNumber(p.Car))
            {
                s.AddPoint(SchemeConvert.ToInt(p.Car, "int-list-to-slice"));
            }
        }

        return s;
    }

    /// <summary>Reads a symbol property as a string — <c>robust_symbol2string</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="fallback">What a non-symbol answers.</param>
    /// <returns>The name.</returns>
    internal static string RobustSymbolToString(object value, string fallback)
        => value is Symbol symbol ? symbol.Name : fallback;

    /// <summary>Half the staff's line span — <c>Staff_symbol_referencer::staff_radius</c>.</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The radius, in staff spaces.</returns>
    internal static double StaffRadius(Grob me)
    {
        /*
          line_span is measured in pitch steps, not in staff spaces
        */
        Grob symbol = StaffSymbolReferencer.GetStaffSymbol(me);
        Interval span = symbol != null ? StaffSymbol.LineSpan(symbol) : Interval.Empty;
        return span.Length / 4.0;
    }

    /// <summary>One side of a two-cell pair — <c>index_get_cell</c>.</summary>
    /// <param name="cell">The pair.</param>
    /// <param name="d">Negative for the car, positive for the cdr.</param>
    /// <returns>The cell's value.</returns>
    internal static object IndexGetCell(Pair cell, Direction d)
        => d == Direction.Negative ? cell.Car : cell.Cdr;

    /// <summary>Writes one side of a two-cell pair — <c>index_set_cell</c>.</summary>
    /// <param name="cell">The pair.</param>
    /// <param name="d">Negative for the car, positive for the cdr.</param>
    /// <param name="value">The value to store.</param>
    internal static void IndexSetCell(Pair cell, Direction d, object value)
    {
        if (d == Direction.Negative)
        {
            cell.Car = value;
        }
        else
        {
            cell.Cdr = value;
        }
    }

    /// <summary>Sorts grobs by staff position — <c>position_less</c>.</summary>
    /// <param name="a">The first grob.</param>
    /// <param name="b">The second grob.</param>
    /// <returns>The comparison result.</returns>
    internal static int PositionLess(Grob a, Grob b)
        => StaffSymbolReferencer.GetPosition(a).CompareTo(StaffSymbolReferencer.GetPosition(b));

    /// <summary>Folds a set of grobs onto one common reference point — <c>common_refpoint_of_array</c>.</summary>
    /// <param name="grobs">The grobs.</param>
    /// <param name="common">The starting point.</param>
    /// <param name="axis">The axis.</param>
    /// <returns>The common reference point.</returns>
    internal static Grob CommonRefpointOfArray(IReadOnlyList<Grob> grobs, Grob common, Axis axis)
    {
        Grob result = common;
        foreach (Grob grob in grobs)
        {
            result = result == null ? grob : result.CommonRefpoint(grob, axis);
        }

        return result;
    }

    /// <summary>
    /// Reads a property's PURE value — <c>get_pure_property</c>, minimally: the pure
    /// half of an unpure-pure container is called, a cached value answers directly,
    /// and a plain procedure answers nothing. The full <c>call_pure_function</c>
    /// machinery is EPG15's (unpure-pure-container.cc); recorded in this group's
    /// report.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <param name="symbol">The property name.</param>
    /// <returns>The pure value.</returns>
    internal static object GetPureProperty(Grob me, Symbol symbol)
    {
        object value = me.GetPropertyData(symbol);
        if (value is UnpurePureContainer container)
        {
            return SchemeUtilities.CallCallback(
                container.Pure, me, 0L, (long)int.MaxValue);
        }

        if (value is Procedure)
        {
            return Nil.Instance;
        }

        return value;
    }

    /// <summary>
    /// A grob's pure vertical extent — <c>pure_y_extent</c>. The pure machinery is
    /// EPG15's; the ordinary extent is what a grob with no pure callback answers, the
    /// same fallback the EPG13 skyline callbacks take. Recorded in PORT-COVERAGE.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <returns>The extent.</returns>
    internal static Interval PureYExtent(Grob me)
        => me.Extent(me, Axis.Y);
}
