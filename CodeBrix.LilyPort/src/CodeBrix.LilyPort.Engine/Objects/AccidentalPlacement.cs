/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/accidental-placement.cc, lily/include/accidental-placement.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.
// Modified by Jeremy Ellis on 2026-08-11 as part of the CodeBrix port:
//   - build_heads_skyline reads the PURE Y extent, as upstream; the EPG13-era
//     ordinary stand-in retired with its class. See PORT-COVERAGE, STAFF-LINES.

/*
  This routine computes placements of accidentals. During
  add_accidental (), accidentals are already grouped by note, so that
  octaves are placed above each other; they form columns. Then the
  columns are sorted: the biggest columns go closest to the note.
  Then the columns are spaced as closely as possible (using skyline
  spacing).


  TODO: more advanced placement. Typically, the accs should be placed
  to form a C shape, like this

  *     ##
  *  b b
  * # #
  *  b
  *    b b

  The naturals should be left of the C as well; they should
  be separate accs.

  Note that this placement problem looks NP hard, so we just use a
  simple strategy, not an optimal choice.
*/

/// <summary>
/// Resolves accidental collisions: the decades-tuned packing algorithm that places
/// every accidental of a timestep left of the note heads without any two overlapping.
/// <para>
/// Accidentals are grouped by note NAME (octaves of one name form a column and stay
/// octave-aligned), the columns are sorted so the biggest go closest to the notes, and
/// the columns are then packed right-to-left by skyline distance. The tie-breaks and
/// the stagger pass are upstream's exactly; where upstream uses <c>std::sort</c> the
/// port sorts STABLY, the same choice every other ported sort has made — .NET's
/// introsort validates its comparer where C++ carries on, and a stable order is a
/// reproducible one.
/// </para>
/// </summary>
public static class AccidentalPlacement
{
    private static readonly Symbol AccidentalGrobsSymbol = Symbol.Intern("accidental-grobs");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol TieSymbol = Symbol.Intern("tie");
    private static readonly Symbol ForcedSymbol = Symbol.Intern("forced");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol RightPaddingSymbol = Symbol.Intern("right-padding");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol HorizontalSkylinesSymbol = Symbol.Intern("horizontal-skylines");
    private static readonly Symbol XExtentSymbol = Symbol.Intern("X-extent");
    private static readonly Symbol NoteColumnInterfaceSymbol = Symbol.Intern("note-column-interface");
    private static readonly Symbol NoteCollisionInterfaceSymbol
        = Symbol.Intern("note-collision-interface");

    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");

    /// <summary>
    /// One packing column: the accidentals of one note name (with one grouping key) and
    /// the merged skylines of their drawings.
    /// </summary>
    private sealed class AccidentalPlacementEntry
    {
        internal SkylinePair HorizontalSkylines { get; } = new SkylinePair();

        internal List<Grob> Grobs { get; } = new List<Grob>();
    }

    private static Pitch AccidentalPitch(Grob acc)
    {
        StreamEvent mcause = acc.YParent?.EventCause();

        if (mcause == null)
        {
            Warn.ProgrammingError("note head has no event cause");
            return null;
        }

        return mcause.GetProperty(PitchSymbol) as Pitch;
    }

    /// <summary>
    /// Files an accidental under the placement grob, keyed by note name plus grouping
    /// so that octaves of one name land in one column.
    /// </summary>
    /// <param name="me">The <c>AccidentalPlacement</c> grob.</param>
    /// <param name="a">The accidental.</param>
    /// <param name="stagger">
    /// Whether same-name accidentals from different sources should form separate
    /// columns; when <see langword="false"/> they share one.
    /// </param>
    /// <param name="hashKey">
    /// What distinguishes the sources when staggering — upstream hashes the engraver's
    /// address; the port hashes the object's identity hash. The value only has to
    /// DIFFER between sources, and does.
    /// </param>
    public static void AddAccidental(Grob me, Grob a, bool stagger, object hashKey)
    {
        Pitch p = AccidentalPitch(a);
        if (p == null)
        {
            return;
        }

        a.XParent = me;

        object accs = me.GetObject(AccidentalGrobsSymbol);
        object key = new Pair(
            (long)p.NoteName,
            stagger
                ? (long)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(hashKey)
                : 1L);

        // assoc because we're dealing with pairs
        Pair found = AssocEqual(key, accs);
        object entry = found == null ? (object)Nil.Instance : found.Cdr;

        entry = new Pair(a, entry);

        accs = AssocSetX(accs, key, entry);

        me.SetObject(AccidentalGrobsSymbol, accs);
    }

    /*
      Split into break reminders.
    */

    /// <summary>
    /// Splits a placement's accidentals into the break reminders — tied, unforced ones
    /// that only matter at a line break — and the real accidentals.
    /// </summary>
    /// <param name="accs">The <c>AccidentalPlacement</c> grob.</param>
    /// <param name="breakReminder">Receives the tied, unforced accidentals.</param>
    /// <param name="realAcc">Receives the rest.</param>
    public static void SplitAccidentals(Grob accs, List<Grob> breakReminder, List<Grob> realAcc)
    {
        object cursor = accs.GetObject(AccidentalGrobsSymbol);
        while (cursor is Pair pair)
        {
            object inner = pair.Car is Pair entry ? entry.Cdr : Nil.Instance;
            while (inner is Pair innerPair)
            {
                if (innerPair.Car is Grob a)
                {
                    if (a.GetObject(TieSymbol) is Grob
                        && !SchemeUtilities.ToBool(a.GetProperty(ForcedSymbol)))
                    {
                        breakReminder.Add(a);
                    }
                    else
                    {
                        realAcc.Add(a);
                    }
                }

                inner = innerPair.Cdr;
            }

            cursor = pair.Cdr;
        }
    }

    /// <summary>
    /// Returns the accidentals a spacing calculation should see against a column: the
    /// real ones always, the break reminders only when the column starts a line.
    /// <para>
    /// The accumulators are shared across the element loop WITHOUT being cleared, so a
    /// second element re-contributes the first one's accidentals. That is upstream's
    /// own control flow, kept because the faithfulness rule says a plausible tidy-up is
    /// a parity bug; in practice a column carries one placement grob and the loop runs
    /// once.
    /// </para>
    /// </summary>
    /// <param name="elts">The <c>AccidentalPlacement</c> grobs of the column.</param>
    /// <param name="left">The item on the left, whose break status decides.</param>
    /// <returns>The relevant accidentals.</returns>
    public static List<Grob> GetRelevantAccidentals(IReadOnlyList<Grob> elts, Grob left)
    {
        List<Grob> br = new List<Grob>();
        List<Grob> ra = new List<Grob>();
        List<Grob> ret = new List<Grob>();
        bool right = left is Item item && item.BreakStatusDirection() == Direction.Positive;

        for (int i = 0; i < elts.Count; i++)
        {
            SplitAccidentals(elts[i], br, ra);

            ret.AddRange(ra);

            if (right)
            {
                ret.AddRange(br);
            }
        }

        return ret;
    }

    private static double ApePriority(AccidentalPlacementEntry a)
    {
        // right is up because we're horizontal
        return a.HorizontalSkylines.Right();
    }

    private static bool ApeLess(AccidentalPlacementEntry a, AccidentalPlacementEntry b)
    {
        int sizeA = a.Grobs.Count;
        int sizeB = b.Grobs.Count;
        if (sizeA != sizeB)
        {
            return sizeB < sizeA;
        }

        return ApePriority(a) < ApePriority(b);
    }

    /*
      This function provides a method for sorting accidentals that belong to the
      same note. The accidentals that this function considers to be "smallest"
      will be placed to the left of the "larger" accidentals.

      Naturals are the largest (so that they don't get confused with cancellation
      naturals); apart from that, we order according to the alteration (so
      double-flats are the smallest).

      Precondition: the accidentals are attached to NoteHeads of the same note
      name -- the octaves, however, may be different.
    */
    private static bool AccLess(Grob a, Grob b)
    {
        Pitch p = AccidentalPitch(a);
        Pitch q = AccidentalPitch(b);

        if (p == null || q == null)
        {
            Warn.ProgrammingError("these accidentals do not have a pitch");
            return false;
        }

        if (p.Octave != q.Octave)
        {
            return p.Octave < q.Octave;
        }

        if (p.Alteration == Rational.Zero)
        {
            return false;
        }

        if (q.Alteration == Rational.Zero)
        {
            return true;
        }

        return p.Alteration < q.Alteration;
    }

    /*
      TODO: should favor

      *  b
      * b

      placement
    */
    private static void StaggerApes(List<AccidentalPlacementEntry> apes)
    {
        StableSort(apes, ApeLess);

        // we do the staggering below based on size
        // this ensures that if a placement has 4 entries, it will
        // always be closer to the NoteColumn than a placement with 1
        // this allows accidentals to be on-average closer to notes
        // while still preserving octave alignment
        List<List<AccidentalPlacementEntry>> ascs = new List<List<AccidentalPlacementEntry>>();

        int sz = int.MaxValue;
        for (int i = 0; i < apes.Count; i++)
        {
            AccidentalPlacementEntry a = apes[i];
            int mySz = a.Grobs.Count;
            if (sz != mySz)
            {
                ascs.Add(new List<AccidentalPlacementEntry>());
            }

            ascs[ascs.Count - 1].Add(a);
            sz = mySz;
        }

        apes.Clear();

        for (int i = 0; i < ascs.Count; i++)
        {
            bool parity = true;
            for (int j = 0; j < ascs[i].Count;)
            {
                AccidentalPlacementEntry a;
                if (parity)
                {
                    a = ascs[i][ascs[i].Count - 1];
                    ascs[i].RemoveAt(ascs[i].Count - 1);
                }
                else
                {
                    a = ascs[i][j++];
                }

                apes.Add(a);
                parity = !parity;
            }
        }

        apes.Reverse();
    }

    private static List<AccidentalPlacementEntry> BuildApes(object accs)
    {
        List<AccidentalPlacementEntry> apes = new List<AccidentalPlacementEntry>();
        object cursor = accs;
        while (cursor is Pair pair)
        {
            AccidentalPlacementEntry ape = new AccidentalPlacementEntry();
            object inner = pair.Car is Pair entry ? entry.Cdr : Nil.Instance;
            while (inner is Pair innerPair)
            {
                if (innerPair.Car is Grob g)
                {
                    ape.Grobs.Add(g);
                }

                inner = innerPair.Cdr;
            }

            apes.Add(ape);
            cursor = pair.Cdr;
        }

        return apes;
    }

    private static void SetApeSkylines(
        AccidentalPlacementEntry ape, Grob commonX, Grob commonY, double padding)
    {
        List<Grob> accs = new List<Grob>(ape.Grobs);
        StableSort(accs, AccLess);

        /* We know that each accidental has the same note name and we assume that
           accidentals in different octaves won't collide. If two or more
           accidentals are in the same octave:
           1) if they are the same accidental, print them in overstrike
           2) otherwise, shift one to the left so they don't overlap. */
        int lastOctave = 0;
        double offset = 0;
        double lastOffset = 0;
        Rational lastAlteration = Rational.Zero;
        for (int i = accs.Count; i-- > 0;)
        {
            Grob a = accs[i];
            Pitch p = AccidentalPitch(a);

            if (p == null)
            {
                continue;
            }

            if (i == accs.Count - 1 || p.Octave != lastOctave)
            {
                lastOffset = 0;
                offset = a.Extent(a, Axis.X)[Direction.Negative] - padding;
            }
            else if (p.Alteration == lastAlteration)
            {
                a.TranslateAxis(lastOffset, Axis.X);
            }
            else /* Our alteration is different from the last one */
            {
                double thisOffset = offset - a.Extent(a, Axis.X)[Direction.Positive];
                a.TranslateAxis(thisOffset, Axis.X);

                lastOffset = thisOffset;
                offset -= a.Extent(a, Axis.X).Length + padding;
            }

            SkylinePair skyps
                = SkylinePair.FromScheme(a.GetProperty(HorizontalSkylinesSymbol))
                  ?? new SkylinePair();
            skyps.Raise(a.RelativeCoordinate(commonX, Axis.X));
            skyps.Shift(a.RelativeCoordinate(commonY, Axis.Y));
            ape.HorizontalSkylines.Merge(skyps);

            lastOctave = p.Octave;
            lastAlteration = p.Alteration;
        }
    }

    private static List<Grob> ExtractHeadsAndStems(List<AccidentalPlacementEntry> apes)
    {
        List<Grob> noteCols = new List<Grob>();
        List<Grob> ret = new List<Grob>();

        for (int i = apes.Count; i-- > 0;)
        {
            AccidentalPlacementEntry ape = apes[i];
            for (int j = ape.Grobs.Count; j-- > 0;)
            {
                Grob acc = ape.Grobs[j];
                Grob head = acc.YParent;
                Grob col = head?.XParent;

                if (col != null && col.HasInterface(NoteColumnInterfaceSymbol))
                {
                    noteCols.Add(col);
                }
                else if (head != null)
                {
                    ret.Add(head);
                }
            }
        }

        /*
          This is a little kludgy: in case there are note columns without
          accidentals, we get them from the Note_collision objects.
        */
        for (int i = noteCols.Count; i-- > 0;)
        {
            Grob c = noteCols[i].XParent;
            if (c != null && c.HasInterface(NoteCollisionInterfaceSymbol))
            {
                IReadOnlyList<Grob> columns = PointerGroupInterface.ExtractGrobSet(c, ElementsSymbol);
                noteCols.AddRange(columns);
            }
        }

        /* Now that we have all of the columns, grab all of the note-heads */
        for (int i = noteCols.Count; i-- > 0;)
        {
            IReadOnlyList<Grob> noteHeads
                = PointerGroupInterface.ExtractGrobSet(noteCols[i], NoteHeadsSymbol);
            ret.AddRange(noteHeads);
        }

        /* Now that we have all of the heads, grab all of the stems */
        for (int i = ret.Count; i-- > 0;)
        {
            // Rhythmic_head::get_stem, inline: the head's `stem' object.
            if (ret[i].GetObject(StemSymbol) is Grob s)
            {
                ret.Add(s);
            }
        }

        // Upstream's uniquify sorts the vector BY POINTER and drops duplicates, which
        // leaves an address-dependent order the algorithm never relies on — the list
        // only ever becomes a set of boxes. The port dedupes keeping first occurrence,
        // which is a deterministic member of the same equivalence class.
        HashSet<Grob> seen = new HashSet<Grob>(ReferenceComparer<Grob>.Instance);
        List<Grob> unique = new List<Grob>();
        foreach (Grob g in ret)
        {
            if (seen.Add(g))
            {
                unique.Add(g);
            }
        }

        return unique;
    }

    private static Grob CommonRefpointOfAccidentals(List<AccidentalPlacementEntry> apes, Axis a)
    {
        Grob ret = null;

        for (int i = apes.Count; i-- > 0;)
        {
            for (int j = apes[i].Grobs.Count; j-- > 0;)
            {
                if (ret == null)
                {
                    ret = apes[i].Grobs[j];
                }
                else
                {
                    ret = ret.CommonRefpoint(apes[i].Grobs[j], a);
                }
            }
        }

        return ret;
    }

    private static Skyline BuildHeadsSkyline(List<Grob> headsAndStems, Grob commonX, Grob commonY)
    {
        List<Box> headExtents = new List<Box>();
        for (int i = headsAndStems.Count; i-- > 0;)
        {
            // Upstream reads the PURE Y extent over the whole piece
            // (`pure_y_extent (common[Y_AXIS], 0, INT_MAX)`): accidental placement
            // runs during horizontal spacing, BEFORE line breaking, and an ordinary Y
            // read here drags stencil computation in over still-unplaced columns.
            // The EPG13-era ordinary stand-in retired with the STAFF-LINES session
            // (2026-08-11), EPG15's pure machinery having long since landed.
            headExtents.Add(new Box(
                headsAndStems[i].Extent(commonX, Axis.X),
                headsAndStems[i].PureYExtent(commonY, 0, int.MaxValue)));
        }

        return new Skyline(headExtents, Axis.Y, Direction.Negative);
    }

    /*
      Position the apes, starting from the right, so that they don't collide.
      Return the total width.
    */
    private static Interval PositionApes(
        Grob me, List<AccidentalPlacementEntry> apes, Skyline headsSkyline)
    {
        double padding = RobustDouble(me.GetProperty(PaddingSymbol), 0.2);
        Skyline leftSkyline = headsSkyline.Copy();
        leftSkyline.Raise(-RobustDouble(me.GetProperty(RightPaddingSymbol), 0));

        /*
          Add accs entries right-to-left.
        */
        Interval width = Interval.Empty;
        double lastOffset = 0.0;
        for (int i = apes.Count; i-- > 0;)
        {
            AccidentalPlacementEntry ape = apes[i];

            double offset
                = -ape.HorizontalSkylines[Direction.Positive].Distance(leftSkyline, 0.1);
            if (double.IsInfinity(offset))
            {
                offset = lastOffset;
            }
            else
            {
                offset -= padding;
            }

            Skyline newLeftSkyline = ape.HorizontalSkylines[Direction.Negative].Copy();
            newLeftSkyline.Raise(offset);
            newLeftSkyline.Merge(leftSkyline);
            leftSkyline = newLeftSkyline;

            /* Shift all of the accidentals in this ape */
            for (int j = ape.Grobs.Count; j-- > 0;)
            {
                ape.Grobs[j].TranslateAxis(offset, Axis.X);
            }

            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                double mh = ape.HorizontalSkylines[d].MaxHeight();
                if (!double.IsInfinity(mh))
                {
                    width.AddPoint(mh + offset);
                }
            }

            lastOffset = offset;
        }

        return width;
    }

    /// <summary>
    /// The <c>positioning-done</c> callback: places every filed accidental and records
    /// the total width as the placement grob's <c>X-extent</c>.
    /// </summary>
    /// <param name="me">The <c>AccidentalPlacement</c> grob.</param>
    /// <returns><see langword="true"/>, always, as upstream answers <c>#t</c>.</returns>
    public static object CalcPositioningDone(Grob me)
    {
        if (!me.IsLive)
        {
            return true;
        }

        me.SetProperty(PositioningDoneSymbol, true);

        object accs = me.GetObject(AccidentalGrobsSymbol);
        if (!(accs is Pair))
        {
            return true;
        }

        List<AccidentalPlacementEntry> apes = BuildApes(accs);

        List<Grob> headsAndStems = ExtractHeadsAndStems(apes);

        Grob commonY = CommonRefpointOfAccidentals(apes, Axis.Y);
        commonY = AxisGroupInterface.CommonRefpointOfArray(headsAndStems, commonY, Axis.Y);
        Grob commonX = AxisGroupInterface.CommonRefpointOfArray(headsAndStems, me, Axis.X);
        double padding = RobustDouble(me.GetProperty(PaddingSymbol), 0.2);

        for (int i = apes.Count; i-- > 0;)
        {
            SetApeSkylines(apes[i], commonX, commonY, padding);
        }

        Skyline headsSkyline = BuildHeadsSkyline(headsAndStems, commonX, commonY);

        StaggerApes(apes);
        Interval width = PositionApes(me, apes, headsSkyline);

        me.FlushExtentCache(Axis.X);
        me.SetProperty(XExtentSymbol, new Pair(width.Left, width.Right));

        return true;
    }

    /// <summary>Looks a key up in an association list by <c>equal?</c> — upstream's
    /// <c>ly_assoc</c> for non-symbol keys.</summary>
    private static Pair AssocEqual(object key, object alist)
    {
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && SchemeUtilities.IsEqual(entry.Car, key))
            {
                return entry;
            }

            cursor = pair.Cdr;
        }

        return null;
    }

    /// <summary>Sets a key in an association list by <c>equal?</c> — <c>scm_assoc_set_x</c>:
    /// the found pair is mutated in place, a missing key is consed onto the front.</summary>
    private static object AssocSetX(object alist, object key, object value)
    {
        Pair entry = AssocEqual(key, alist);
        if (entry != null)
        {
            entry.Cdr = value;
            return alist is Pair ? alist : Nil.Instance;
        }

        return new Pair(new Pair(key, value), alist is Pair ? alist : (object)Nil.Instance);
    }

    private static double RobustDouble(object value, double fallback)
        => Bootstrap.SchemeConvert.IsNumber(value)
            ? Bootstrap.SchemeConvert.ToDouble(value, "accidental placement")
            : fallback;

    /// <summary>
    /// A stable insertion-style merge sort over a "precedes" predicate, the same device
    /// Skyline and SimpleSpacer use: it only ever asks "does b precede a", so a
    /// comparison that is not a strict weak ordering (AccLess ties every pair of
    /// naturals) cannot make it throw the way List&lt;T&gt;.Sort's validation would.
    /// </summary>
    private static void StableSort<T>(List<T> items, System.Func<T, T, bool> less)
    {
        if (items.Count < 2)
        {
            return;
        }

        List<T> result = new List<T>(items.Count);
        foreach (T item in items)
        {
            int at = result.Count;
            while (at > 0 && less(item, result[at - 1]))
            {
                at--;
            }

            result.Insert(at, item);
        }

        items.Clear();
        items.AddRange(result);
    }

    /// <summary>Reference-identity comparer for the head/stem dedupe.</summary>
    /// <typeparam name="T">The reference type compared.</typeparam>
    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

        public bool Equals(T x, T y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj)
            => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
