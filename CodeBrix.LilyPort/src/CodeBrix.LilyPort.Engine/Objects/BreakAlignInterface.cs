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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/break-alignment-interface.cc, lily/include/break-align-interface.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - Three upstream interfaces live in one .cc file and therefore in one .cs file:
//     Break_alignment_interface, Break_alignable_interface and Break_aligned_interface.
//     The port keeps them as three static classes with upstream's names.
//   - VPOS is upstream's unsigned "no index" sentinel; it is spelled NoIndex (-1) here,
//     with the same two meanings kept apart (see ConstrainedBreaking.cs for the same note).

/// <summary>
/// The object that lays out everything that can appear at a breakable moment — the clef,
/// the key signature, the time signature, the bar line — in the right order and with the
/// right gaps.
/// <para>
/// Ordering is not a property of when the grobs were created: a clef engraver and a key
/// engraver do not know about each other. Each breakable grob instead carries a
/// <c>break-align-symbol</c>, groups are formed by symbol, and the groups are placed in
/// the order <c>break-align-orders</c> gives for this side of the break. That vector has
/// three entries, one per break direction, because the order at the end of a line differs
/// from the order at the start of the next.
/// </para>
/// </summary>
public static class BreakAlignmentInterface
{
    /// <summary>The "no index" marker — upstream's <c>VPOS</c>.</summary>
    public const int NoIndex = -1;

    private static readonly Symbol BreakAlignOrdersSymbol = Symbol.Intern("break-align-orders");
    private static readonly Symbol BreakAlignSymbolSymbol = Symbol.Intern("break-align-symbol");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol SpaceAlistSymbol = Symbol.Intern("space-alist");
    private static readonly Symbol LeftEdgeSymbol = Symbol.Intern("left-edge");
    private static readonly Symbol RightEdgeSymbol = Symbol.Intern("right-edge");
    private static readonly Symbol ExtraSpaceSymbol = Symbol.Intern("extra-space");
    private static readonly Symbol MinimumSpaceSymbol = Symbol.Intern("minimum-space");
    private static readonly Symbol CauseSymbol = Symbol.Intern("cause");

    /// <summary>
    /// Reads the ordering vector's entry for this alignment's break direction.
    /// </summary>
    /// <param name="me">The break alignment.</param>
    /// <returns>The order list, or <see langword="false"/> when there is none.</returns>
    public static object BreakAlignOrder(Item me)
    {
        if (me == null)
        {
            return false;
        }

        object orderVec = me.GetProperty(BreakAlignOrdersSymbol);
        if (!(orderVec is object[] vector) || vector.Length < 3)
        {
            return false;
        }

        return vector[me.BreakStatusDirection().ToIndex];
    }

    /// <summary>
    /// Returns this alignment's elements in the order <c>break-align-orders</c> asks for.
    /// <para>
    /// It answers a NEW array rather than reordering <c>elements</c> in place, and
    /// upstream's comment says why in as many words: callers are iterating that same list,
    /// so reordering or resetting it would make their loops skip elements.
    /// </para>
    /// <para>
    /// Elements whose symbol is not named in the order are DROPPED, not appended — an
    /// element the order does not mention is not placed by this pass at all.
    /// </para>
    /// </summary>
    /// <param name="me">The break alignment.</param>
    /// <returns>The ordered elements.</returns>
    public static List<Grob> OrderedElements(Item me)
    {
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);

        object order = BreakAlignOrder(me);

        if (!SchemeUtilities.ToBool(order))
        {
            return new List<Grob>(elts);
        }

        List<Grob> writableElts = new List<Grob>(elts);
        /*
         Copy in order specified in BREAK-ALIGN-ORDER.
        */
        List<Grob> newElts = new List<Grob>();
        for (; order is Pair pair; order = pair.Cdr)
        {
            object sym = pair.Car;

            for (int i = writableElts.Count; i-- > 0;)
            {
                Grob g = writableElts[i];
                if (g != null && ReferenceEquals(sym, g.GetProperty(BreakAlignSymbolSymbol)))
                {
                    newElts.Add(g);
                    writableElts.RemoveAt(i);
                }
            }
        }

        return newElts;
    }

    /// <summary>Adds a group to this alignment.</summary>
    /// <param name="me">The break alignment.</param>
    /// <param name="toadd">The group to add.</param>
    public static void AddElement(Item me, Item toadd) => AlignInterface.AddElement(me, toadd);

    /// <summary>
    /// Finds the group carrying a given break-align symbol, provided it actually draws
    /// something — <c>ly:break-alignment-interface::find-nonempty-break-align-group</c>.
    /// <para>
    /// A group holding only omitted items has an EMPTY horizontal extent, and answering it
    /// would let a caller align against nothing; upstream answers <see langword="false"/>
    /// in that case and in the case where no such group exists at all.
    /// </para>
    /// </summary>
    /// <param name="me">The break alignment.</param>
    /// <param name="breakAlignSym">The symbol to look for.</param>
    /// <returns>The group, or <see langword="null"/>.</returns>
    public static Grob FindNonemptyBreakAlignGroup(Item me, object breakAlignSym)
    {
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        foreach (Grob group in elts)
        {
            if (ReferenceEquals(group.GetProperty(BreakAlignSymbolSymbol), breakAlignSym))
            {
                return !group.Extent(group, Axis.X).IsEmpty ? group : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Spaces the breakable items of one column according to their
    /// <c>space-alist</c> specifications —
    /// <c>ly:break-alignment-interface::calc-positioning-done</c>.
    /// <para>
    /// It walks the ordered groups left to right, skipping empty ones entirely, and for
    /// each adjacent pair looks up the gap the LEFT group's <c>space-alist</c> prescribes
    /// for the RIGHT group's symbol. <c>extra-space</c> means that much clear air after
    /// the left group; <c>minimum-space</c> means the right group starts at least that far
    /// from the origin. Upstream's own note calls minimum-space a candidate for removal.
    /// </para>
    /// <para>
    /// The alist is read from the group's ELEMENTS rather than from the group, and from the
    /// first element that has one. Upstream explains the indirection: it used to find the
    /// symbol through <c>cause</c>, and that broke whenever the causing grob had been
    /// suicided.
    /// </para>
    /// <para>
    /// Finally the whole row is shifted so that the <c>left-edge</c> group sits at zero —
    /// or, when the alignment ends a line, so that its right edge does.
    /// </para>
    /// </summary>
    /// <param name="me">The break alignment.</param>
    /// <returns><see langword="true"/>, which is what the property records.</returns>
    public static object CalcPositioningDone(Item me)
    {
        me.SetProperty(PositioningDoneSymbol, true);

        List<Grob> elems = OrderedElements(me);

        List<Interval> extents = new List<Interval>();
        foreach (Grob g in elems)
        {
            extents.Add(g.Extent(g, Axis.X));
        }

        int idx = 0;
        while (idx < extents.Count && extents[idx].IsEmpty)
        {
            idx++;
        }

        double[] offsets = new double[elems.Count];

        double extraRightSpace = 0.0;
        int edgeIdx = NoIndex;
        while (idx < elems.Count)
        {
            int nextIdx = idx + 1;
            while (nextIdx < elems.Count && extents[nextIdx].IsEmpty)
            {
                nextIdx++;
            }

            Grob l = elems[idx];
            Grob r = null;

            if (nextIdx < elems.Count)
            {
                r = elems[nextIdx];
            }

            object alist = Nil.Instance;

            /*
              Find the first grob with a space-alist entry.
            */
            IReadOnlyList<Grob> leftElts = PointerGroupInterface.ExtractGrobSet(l, ElementsSymbol);

            for (int i = leftElts.Count; i-- > 0;)
            {
                Grob elt = leftElts[i];

                if (edgeIdx == NoIndex
                    && ReferenceEquals(elt.GetProperty(BreakAlignSymbolSymbol), LeftEdgeSymbol))
                {
                    edgeIdx = idx;
                }

                object candidate = elt.GetProperty(SpaceAlistSymbol);
                if (candidate is Pair)
                {
                    alist = candidate;
                    break;
                }
            }

            object rsym = r != null ? Nil.Instance : (object)RightEdgeSymbol;

            /*
              We used to use 'cause to find out the symbol and the spacing
              table, but that gets icky when that grob is suicided for some
              reason.
            */
            if (r != null)
            {
                IReadOnlyList<Grob> rightElts
                    = PointerGroupInterface.ExtractGrobSet(r, ElementsSymbol);
                for (int i = rightElts.Count; !(rsym is Symbol) && i-- > 0;)
                {
                    Grob elt = rightElts[i];
                    rsym = elt.GetProperty(BreakAlignSymbolSymbol);
                }
            }

            if (ReferenceEquals(rsym, LeftEdgeSymbol))
            {
                edgeIdx = nextIdx;
            }

            Pair entry = null;
            if (rsym is Symbol rsymbol)
            {
                entry = SchemeUtilities.Assq(rsymbol, alist);
            }

            bool entryFound = entry != null;
            if (!entryFound)
            {
                string symString = rsym is Symbol named ? named.Name : string.Empty;

                string origString = l.GetProperty(CauseSymbol) is Grob causeGrob
                    ? causeGrob.Name
                    : string.Empty;

                Warn.ProgrammingError(
                    "No spacing entry from " + origString + " to `" + symString + "'");
            }

            double distance = 1.0;
            object type = ExtraSpaceSymbol;

            if (entryFound && entry.Cdr is Pair spec)
            {
                distance = SchemeConvert.IsNumber(spec.Cdr)
                    ? SchemeConvert.ToDouble(spec.Cdr, "space-alist distance")
                    : 0.0;
                type = spec.Car;
            }

            if (r != null)
            {
                if (ReferenceEquals(type, ExtraSpaceSymbol))
                {
                    offsets[nextIdx] = extents[idx].Right + distance - extents[nextIdx].Left;
                }
                else if (ReferenceEquals(type, MinimumSpaceSymbol))
                {
                    /* should probably junk minimum-space */
                    offsets[nextIdx] = Math.Max(extents[idx].Right, distance);
                }
            }
            else
            {
                extraRightSpace = distance;
                if (idx + 1 < offsets.Length)
                {
                    offsets[idx + 1] = extents[idx].Right + distance;
                }
            }

            idx = nextIdx;
        }

        double here = 0.0;
        Interval totalExtent = Interval.Empty;

        double alignmentOff = 0.0;
        for (int i = 0; i < offsets.Length; i++)
        {
            here += offsets[i];
            if (i == edgeIdx)
            {
                alignmentOff = -here;
            }

            Interval shifted = extents[i];
            shifted.Translate(here);
            totalExtent.Unite(shifted);
        }

        if (totalExtent.IsEmpty)
        {
            return true;
        }

        if (me.BreakStatusDirection() == Direction.Negative)
        {
            alignmentOff = -totalExtent.Right - extraRightSpace;
        }
        else if (edgeIdx == NoIndex)
        {
            alignmentOff = -totalExtent.Left;
        }

        here = alignmentOff;
        for (int i = 0; i < offsets.Length; i++)
        {
            here += offsets[i];
            elems[i].TranslateAxis(here, Axis.X);
        }

        return true;
    }
}

/// <summary>
/// Something that wants to be aligned ON a break alignment — a rehearsal mark or a
/// metronome mark, which should sit over the clef, or the bar line, or whatever the score
/// says.
/// </summary>
public static class BreakAlignableInterface
{
    private static readonly Symbol BreakAlignSymbolsSymbol = Symbol.Intern("break-align-symbols");
    private static readonly Symbol BreakAlignSymbolSymbol = Symbol.Intern("break-align-symbol");
    private static readonly Symbol BreakAlignAnchorSymbol = Symbol.Intern("break-align-anchor");
    private static readonly Symbol BreakAlignmentInterfaceSymbol
        = Symbol.Intern("break-alignment-interface");

    /// <summary>
    /// Finds the break-aligned group this grob should align to —
    /// <c>ly:break-alignable-interface::find-parent</c>.
    /// <para>
    /// <c>break-align-symbols</c> is a PREFERENCE LIST, walked in order. The first
    /// candidate that is both break-visible and actually draws something wins outright.
    /// Failing that, the FIRST candidate seen at all is used — so a mark still lands
    /// somewhere sensible when everything it would rather align to is invisible.
    /// </para>
    /// </summary>
    /// <param name="me">The alignable grob.</param>
    /// <returns>The item to align to, or <see langword="null"/>.</returns>
    public static Item FindParent(Grob me)
    {
        Item alignment = me.GetParent(Axis.X) as Item;
        if (alignment == null || !alignment.HasInterface(BreakAlignmentInterfaceSymbol))
        {
            return null;
        }

        List<Grob> elements = BreakAlignmentInterface.OrderedElements(alignment);
        if (elements.Count == 0)
        {
            return null;
        }

        Item breakAlignedGrob = null;
        object symbolList = me.GetProperty(BreakAlignSymbolsSymbol);
        for (object s = symbolList; s is Pair pair; s = pair.Cdr)
        {
            object sym = pair.Car;
            foreach (Grob g in elements)
            {
                if (ReferenceEquals(sym, g.GetProperty(BreakAlignSymbolSymbol)))
                {
                    // Someone would have to do something unusual in Scheme to get a
                    // Spanner here.
                    if (g is Item it)
                    {
                        if (it.BreakVisible() && !it.Extent(it, Axis.X).IsEmpty)
                        {
                            return it;
                        }

                        if (breakAlignedGrob == null)
                        {
                            breakAlignedGrob = it;
                        }
                    }
                }
            }
        }

        return breakAlignedGrob;
    }

    /// <summary>
    /// The horizontal offset that puts this grob over its alignment parent's anchor —
    /// <c>ly:break-alignable-interface::self-align-callback</c>.
    /// </summary>
    /// <param name="me">The alignable grob.</param>
    /// <returns>The offset.</returns>
    public static object SelfAlignCallback(Grob me)
    {
        Item alignmentParent = FindParent(me);
        if (alignmentParent == null)
        {
            return 0.0;
        }

        Grob common = me.CommonRefpoint(alignmentParent, Axis.X);
        object anchorValue = alignmentParent.GetProperty(BreakAlignAnchorSymbol);
        double anchor = SchemeConvert.IsNumber(anchorValue)
            ? SchemeConvert.ToDouble(anchorValue, "break-align-anchor")
            : 0.0;

        return alignmentParent.RelativeCoordinate(common, Axis.X)
            - me.RelativeCoordinate(common, Axis.X)
            + anchor;
    }
}

/// <summary>
/// A breakable item itself: the clef, the bar line, the key signature. It carries the
/// <c>break-align-symbol</c> that groups it and the anchor other grobs align to.
/// </summary>
public static class BreakAlignedInterface
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol BreakAlignAnchorSymbol = Symbol.Intern("break-align-anchor");
    private static readonly Symbol BreakAlignAnchorAlignmentSymbol
        = Symbol.Intern("break-align-anchor-alignment");

    private static readonly Symbol BreakVisibilitySymbol = Symbol.Intern("break-visibility");

    /// <summary>
    /// The anchor point of a GROUP, averaged from the anchors its members ask for —
    /// <c>ly:break-aligned-interface::calc-average-anchor</c>.
    /// <para>
    /// The average is taken in NORMALIZED coordinates — each member's anchor expressed as
    /// -1 at its own left edge and 1 at its right — so that when every member agrees on
    /// "my left edge" or "my centre", the group inherits exactly that intent regardless of
    /// how wide each member happens to be. The normalized average is then mapped back onto
    /// the group's extent.
    /// </para>
    /// <para>
    /// When members disagree, that mapping can land outside every member's actual anchor,
    /// pleasing nobody, so the result is CLAMPED to the range of anchors actually asked
    /// for. A group whose members have no extent to normalize against falls back on the
    /// plain average.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns>The anchor.</returns>
    public static double CalcAverageAnchor(Grob me)
    {
        // the range of anchor points requested by group members
        Interval absoluteRange = Interval.Empty;

        // the range of anchor points requested by group members, normalized to the
        // extent of each member: -1 at left, 1 at right (zero-width elements are ignored)
        Interval normalizedRange = Interval.Empty;

        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        foreach (Grob g in elts)
        {
            object anchorValue = g.GetProperty(BreakAlignAnchorSymbol);
            if (!SchemeConvert.IsNumber(anchorValue))
            {
                continue;
            }

            double anchor = SchemeConvert.ToDouble(anchorValue, "break-align-anchor");
            if (!double.IsNaN(anchor))
            {
                absoluteRange.AddPoint(anchor);

                Interval extent = g.Extent(g, Axis.X);
                double normalizedAnchor = extent.InverseLinearCombination(anchor);
                if (!double.IsNaN(normalizedAnchor) && !double.IsInfinity(normalizedAnchor))
                {
                    normalizedRange.AddPoint(normalizedAnchor);
                }
            }
        }

        if (!normalizedRange.IsEmpty)
        {
            Interval extent = me.Extent(me, Axis.X);
            double anchor = extent.LinearCombination(normalizedRange.Center);

            anchor = absoluteRange.Clamp(anchor);

            return anchor;
        }

        if (!absoluteRange.IsEmpty)
        {
            return absoluteRange.Center;
        }

        return 0;
    }

    /// <summary>
    /// The direction a group's members agree to anchor towards, or centre when they do not
    /// — <c>ly:break-aligned-interface::calc-joint-anchor-alignment</c>.
    /// <para>
    /// Upstream's own assessment is worth keeping: "just enough thought has been put into
    /// this algorithm to serve our immediate needs". Any disagreement in sign answers
    /// centre.
    /// </para>
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns>The agreed direction.</returns>
    public static Direction CalcJointAnchorAlignment(Grob me)
    {
        Direction direction = Direction.Center;

        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        for (int i = 0; i < elts.Count; i++)
        {
            object s = elts[i].GetProperty(BreakAlignAnchorAlignmentSymbol);
            double alignment = SchemeConvert.IsNumber(s)
                ? SchemeConvert.ToDouble(s, "break-align-anchor-alignment")
                : 0.0;
            if (alignment < 0)
            {
                if (direction > Direction.Center)
                {
                    return Direction.Center; // conflict
                }

                direction = Direction.Negative;
            }
            else if (alignment > 0)
            {
                if (direction < Direction.Center)
                {
                    return Direction.Center; // conflict
                }

                direction = Direction.Positive;
            }
        }

        return direction;
    }

    /// <summary>
    /// The anchor of a single break-aligned item, taken as a fraction of its own extent —
    /// <c>ly:break-aligned-interface::calc-extent-aligned-anchor</c>.
    /// </summary>
    /// <param name="me">The item.</param>
    /// <returns>The anchor.</returns>
    public static object CalcExtentAlignedAnchor(Grob me)
    {
        object alignmentValue = me.GetProperty(BreakAlignAnchorAlignmentSymbol);
        double alignment = SchemeConvert.IsNumber(alignmentValue)
            ? SchemeConvert.ToDouble(alignmentValue, "break-align-anchor-alignment")
            : 0.0;
        Interval iv = me.Extent(me, Axis.X);

        if (double.IsInfinity(iv.Left) && double.IsInfinity(iv.Right))
        {
            /* avoid NaN */
            return 0.0;
        }

        return iv.LinearCombination(alignment);
    }

    /// <summary>
    /// A group is break-visible in a given direction when ANY of its elements is —
    /// <c>ly:break-aligned-interface::calc-break-visibility</c>.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <returns>A three-element visibility vector.</returns>
    public static object CalcBreakVisibility(Grob me)
    {
        /* a BreakAlignGroup is break-visible if it has one element that is break-visible */
        object[] ret = new object[3];
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        for (int dir = 0; dir <= 2; dir++)
        {
            bool visible = false;
            for (int i = 0; i < elts.Count; i++)
            {
                object vis = elts[i].GetProperty(BreakVisibilitySymbol);
                if (vis is object[] vector
                    && dir < vector.Length
                    && SchemeUtilities.ToBool(vector[dir]))
                {
                    visible = true;
                }
            }

            ret[dir] = visible;
        }

        return ret;
    }
}
