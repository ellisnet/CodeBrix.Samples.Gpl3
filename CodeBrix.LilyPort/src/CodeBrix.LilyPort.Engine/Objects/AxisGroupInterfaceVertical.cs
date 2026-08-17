/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/axis-group-interface.cc (the half not carried by Objects/AxisGroupInterface.cs);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The vertical-organization half of <c>lily/axis-group-interface.cc</c>: skyline
/// spacing (which is also what MOVES outside-staff grobs), skyline combination for
/// alignments, bound extents, common-refpoint callbacks and the staff-staff-spacing
/// resolution.
/// <para>
/// It sits in its OWN class because <c>Objects/AxisGroupInterface.cs</c> — the first
/// half of the same upstream file — was ported earlier and stays closed in this pass;
/// the split is recorded in PORT-COVERAGE. The pure-height family
/// (<c>adjacent_pure_heights</c>, <c>pure_height</c>,
/// <c>calc_pure_relevant_grobs</c>, <c>calc_pure_y_common</c>,
/// <c>calc_pure_staff_staff_spacing</c>) is NOT here: it needs the line-breaking group's pure/broken
/// machinery, and its Scheme names deliberately stay stubs.
/// </para>
/// </summary>
public static class AxisGroupInterfaceVertical
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol VerticalSkylinesSymbol = Symbol.Intern("vertical-skylines");
    private static readonly Symbol HorizontalSkylinesSymbol
        = Symbol.Intern("horizontal-skylines");

    private static readonly Symbol VerticalSkylineElementsSymbol
        = Symbol.Intern("vertical-skyline-elements");

    private static readonly Symbol BoundAlignmentInterfaces
        = Symbol.Intern("bound-alignment-interfaces");

    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol AxisGroupInterfaceSymbol
        = Symbol.Intern("axis-group-interface");

    private static readonly Symbol OutsideStaffPriority = Symbol.Intern("outside-staff-priority");
    private static readonly Symbol OutsideStaffPadding = Symbol.Intern("outside-staff-padding");
    private static readonly Symbol OutsideStaffHorizontalPadding
        = Symbol.Intern("outside-staff-horizontal-padding");

    private static readonly Symbol OutsideStaffPlacementDirective
        = Symbol.Intern("outside-staff-placement-directive");

    private static readonly Symbol LeftToRightGreedy = Symbol.Intern("left-to-right-greedy");
    private static readonly Symbol LeftToRightPolite = Symbol.Intern("left-to-right-polite");
    private static readonly Symbol RightToLeftGreedy = Symbol.Intern("right-to-left-greedy");
    private static readonly Symbol RightToLeftPolite = Symbol.Intern("right-to-left-polite");
    private static readonly Symbol StaffGrouperSymbol = Symbol.Intern("staff-grouper");
    private static readonly Symbol StaffStaffSpacingSymbol
        = Symbol.Intern("staff-staff-spacing");

    private static readonly Symbol StaffgroupStaffSpacingSymbol
        = Symbol.Intern("staffgroup-staff-spacing");

    private static readonly Symbol DefaultStaffStaffSpacingSymbol
        = Symbol.Intern("default-staff-staff-spacing");

    private static readonly double DefaultOutsideStaffPadding = 0.46;

    /// <summary>Gets the padding an outside-staff grob keeps when it declares none.</summary>
    /// <returns>The default padding.</returns>
    public static double GetDefaultOutsideStaffPadding() => DefaultOutsideStaffPadding;

    /// <summary>
    /// Reads a grob's skyline pair the way upstream can rely on reading one.
    /// <para>
    /// Upstream's <c>Grob</c> constructor installs
    /// <c>simple-skylines-from-extents</c> as the default value of both skyline
    /// properties, so a property read always answers a pair. The port's
    /// <c>Grob</c> deliberately does not install those defaults yet (the recorded
    /// first-light gap), so where the property is unset this computes exactly what
    /// upstream's default callback would have — the flat skylines of the grob's
    /// extent box.
    /// </para>
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <param name="a">The axis of the wanted skylines: Y for vertical.</param>
    /// <returns>The pair; never <see langword="null"/>.</returns>
    public static SkylinePair ReadSkylinePair(Grob grob, Axis a)
    {
        object value = grob.GetProperty(
            a == Axis.Y ? VerticalSkylinesSymbol : HorizontalSkylinesSymbol);
        SkylinePair pair = SkylinePair.FromScheme(value);
        if (pair != null)
        {
            return pair;
        }

        return FallbackSkylines(grob, a);
    }

    /// <summary>
    /// <c>simple_skylines_from_extents</c> over what the extents WOULD be with
    /// upstream's constructor defaults installed: a grob whose definition names no
    /// extent falls back on its stencil's, which is exactly what upstream's default
    /// <c>X-extent</c>/<c>Y-extent</c> callbacks answer.
    /// </summary>
    private static SkylinePair FallbackSkylines(Grob grob, Axis a)
    {
        Interval x = ExtentWithStencilFallback(grob, Axis.X);
        Interval y = ExtentWithStencilFallback(grob, Axis.Y);
        List<Box> boxes = new List<Box>();
        if (!x.IsEmpty && !y.IsEmpty)
        {
            // The horizontal variant widens a cross-staff grob to the whole horizon,
            // as upstream's simple-horizontal-skylines-from-extents does.
            if (a == Axis.X && SchemeUtilities.ToBool(grob.GetProperty(CrossStaffSymbol)))
            {
                y.SetFull();
            }

            boxes.Add(new Box(x, y));
        }

        return new SkylinePair(boxes, a == Axis.Y ? Axis.X : Axis.Y);
    }

    private static Interval ExtentWithStencilFallback(Grob grob, Axis axis)
    {
        Interval extent = grob.Extent(grob, axis);
        if (!extent.IsEmpty)
        {
            return extent;
        }

        Stencil? stencil = grob.GetStencil();
        return stencil.HasValue ? stencil.Value.Extent(axis) : Interval.Empty;
    }

    /// <summary>
    /// Returns the extent of a set of grobs, where axis-group members are measured as
    /// BOUNDS when asked (<paramref name="bound"/>): a group's bound extent counts
    /// only children carrying one of its <c>bound-alignment-interfaces</c>.
    /// </summary>
    /// <param name="elts">The grobs.</param>
    /// <param name="common">The reference grob.</param>
    /// <param name="a">The axis to measure.</param>
    /// <param name="bound">Whether groups are measured as bounds.</param>
    /// <returns>The extent.</returns>
    public static Interval RelativeMaybeBoundGroupExtent(
        IReadOnlyList<Grob> elts,
        Grob common,
        Axis a,
        bool bound)
    {
        Interval r = Interval.Empty;
        for (int i = 0; i < elts.Count; i++)
        {
            Grob se = elts[i];
            if (!SchemeUtilities.ToBool(se.GetProperty(CrossStaffSymbol)))
            {
                Interval dims = bound && se.HasInterface(AxisGroupInterfaceSymbol)
                    ? GenericBoundExtent(se, common, a)
                    : se.Extent(common, a);
                if (!dims.IsEmpty)
                {
                    r.Unite(dims);
                }
            }
        }

        return r;
    }

    /// <summary>
    /// The extent a grob presents AS A BOUND — what <c>ly:generic-bound-extent</c>
    /// answers. A group with <c>bound-alignment-interfaces</c> counts only the
    /// matching children; anything else answers its robust extent.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <param name="common">The reference grob, or <see langword="null"/> to find one.</param>
    /// <param name="a">The axis to measure.</param>
    /// <returns>The extent.</returns>
    public static Interval GenericBoundExtent(Grob me, Grob common, Axis a)
    {
        /* trigger the callback to do skyline-spacing on the children */
        if (a == Axis.Y)
        {
            me.GetProperty(VerticalSkylinesSymbol);
        }

        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        List<Grob> newElts = new List<Grob>();

        object interfaces = me.GetProperty(BoundAlignmentInterfaces);

        for (int i = 0; i < elts.Count; i++)
        {
            object cursor = interfaces;
            while (cursor is Pair pair)
            {
                if (pair.Car is Symbol alignmentInterface
                    && elts[i].HasInterface(alignmentInterface))
                {
                    newElts.Add(elts[i]);
                }

                cursor = pair.Cdr;
            }
        }

        if (newElts.Count == 0)
        {
            return LooseColumns.RobustRelativeExtent(me, common, a);
        }

        if (common == null)
        {
            common = AxisGroupInterface.CommonRefpointOfArray(newElts, me, a);
        }

        return RelativeMaybeBoundGroupExtent(newElts, common, a, true);
    }

    /// <summary>
    /// The common-refpoint object callbacks (<c>calc-x-common</c> /
    /// <c>calc-y-common</c>): the reference point every element of a group shares.
    /// </summary>
    /// <param name="me">The group.</param>
    /// <param name="axis">The axis whose parent chains to walk.</param>
    /// <returns>The common grob, or <see langword="null"/> with a diagnostic.</returns>
    public static Grob CalcCommon(Grob me, Axis axis)
    {
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        Grob common = AxisGroupInterface.CommonRefpointOfArray(elts, me, axis);
        if (common == null)
        {
            Warn.ProgrammingError("No common parent found in calc_common axis.");
            return null;
        }

        return common;
    }

    /* whereas calc_skylines calculates skylines for axis-groups with a lot of
       visible children, combine_skylines is designed for axis-groups whose only
       children are other axis-groups (ie. VerticalAlignment). Rather than
       calculating all the skylines from scratch, we just merge the skylines
       of the children.
    */

    /// <summary>The <c>combine-skylines</c> callback body.</summary>
    /// <param name="me">The alignment whose children's skylines are merged.</param>
    /// <returns>The merged pair.</returns>
    public static SkylinePair CombineSkylines(Grob me)
    {
        IReadOnlyList<Grob> elements
            = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        Grob yCommon = AxisGroupInterface.CommonRefpointOfArray(elements, me, Axis.Y);
        Grob xCommon = AxisGroupInterface.CommonRefpointOfArray(elements, me, Axis.X);

        if (!ReferenceEquals(yCommon, me))
        {
            Warn.ProgrammingError("combining skylines that don't belong to me");
        }

        SkylinePair ret = new SkylinePair();
        for (int i = 0; i < elements.Count; i++)
        {
            SkylinePair skyp = ReadSkylinePair(elements[i], Axis.Y);
            double offset = elements[i].RelativeCoordinate(yCommon, Axis.Y);
            skyp.Raise(offset);
            skyp.Shift(elements[i].RelativeCoordinate(xCommon, Axis.X));
            ret.Merge(skyp);
        }

        ret.Shift(-me.RelativeCoordinate(xCommon, Axis.X));
        return ret;
    }

    // If the Grob has a Y-ancestor with outside-staff-priority, return it.
    // Otherwise, return 0.

    /// <summary>Returns a grob's nearest Y ancestor carrying
    /// <c>outside-staff-priority</c>, or <see langword="null"/>.</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The ancestor, or <see langword="null"/>.</returns>
    public static Grob OutsideStaffAncestor(Grob me)
    {
        Grob parent = me.GetParent(Axis.Y);
        if (parent == null)
        {
            return null;
        }

        if (SchemeConvert.IsNumber(parent.GetProperty(OutsideStaffPriority)))
        {
            return parent;
        }

        return OutsideStaffAncestor(parent);
    }

    private static Symbol ValidOutsideStaffPlacementDirective(Grob me)
    {
        object directive = me.GetProperty(OutsideStaffPlacementDirective);

        if (ReferenceEquals(directive, LeftToRightGreedy)
            || ReferenceEquals(directive, LeftToRightPolite)
            || ReferenceEquals(directive, RightToLeftGreedy)
            || ReferenceEquals(directive, RightToLeftPolite))
        {
            return (Symbol)directive;
        }

        Warn.Warning(
            "\"" + (directive is Symbol symbol ? symbol.Name : string.Empty)
            + "\" is not a valid outside-staff-placement-directive");

        return LeftToRightPolite;
    }

    // Raises the grob elt (whose skylines are given by v_skyline)
    // so that it doesn't intersect with staff_skyline,
    // or with anything in other_v_skylines.
    private static void AvoidOutsideStaffCollisions(
        Grob elt,
        SkylinePair vSkyline,
        double padding,
        double horizonPadding,
        List<SkylinePair> otherVSkylines,
        List<double> otherPadding,
        List<double> otherHorizonPadding,
        Direction dir)
    {
        List<Interval> forbiddenIntervals = new List<Interval>();
        for (int j = 0; j < otherVSkylines.Count; j++)
        {
            SkylinePair vOther = otherVSkylines[j];
            double pad = System.Math.Max(padding, otherPadding[j]);
            double horizonPad = System.Math.Max(horizonPadding, otherHorizonPadding[j]);

            // We need to push elt up by at least this much to be above v_other.
            double up = vSkyline[Direction.Negative]
                .Distance(vOther[Direction.Positive], horizonPad) + pad;

            // We need to push elt down by at least this much to be below v_other.
            double down = vSkyline[Direction.Positive]
                .Distance(vOther[Direction.Negative], horizonPad) + pad;

            forbiddenIntervals.Add(new Interval(-down, up));
        }

        IntervalSet allowedShifts
            = IntervalSet.IntervalUnion(forbiddenIntervals).Complement();
        double move = allowedShifts.NearestPoint(0, dir);
        vSkyline.Raise(move);
        elt.TranslateAxis(move, Axis.Y);
    }

    // Shifts the grobs in elements to ensure that they (and any
    // connected riders) don't collide with the staff skylines
    // or anything in all_X_skylines.  Afterwards, the skylines
    // of the grobs in elements will be added to all_v_skylines.
    private static void AddGrobsOfOnePriority(
        Grob me,
        DrulArray<List<SkylinePair>> allVSkylines,
        DrulArray<List<double>> allPaddings,
        DrulArray<List<double>> allHorizonPaddings,
        List<Grob> elements,
        Grob xCommon,
        Grob yCommon,
        Dictionary<Grob, List<Grob>> riders)
    {
        Symbol directive = ValidOutsideStaffPlacementDirective(me);

        bool l2r = ReferenceEquals(directive, LeftToRightGreedy)
                   || ReferenceEquals(directive, LeftToRightPolite);

        bool polite = ReferenceEquals(directive, LeftToRightPolite)
                      || ReferenceEquals(directive, RightToLeftPolite);

        // We want to avoid situations like this:
        //           still more text
        //      more text
        //   text
        //   -------------------
        //   staff
        //   -------------------

        // The point is that "still more text" should be positioned under
        // "more text".  In order to achieve this, we place the grobs in several
        // passes.  We keep track of the right-most horizontal position that has been
        // affected by the current pass so far (actually we keep track of 2
        // positions, one for above the staff, one for below).

        // In each pass, we loop through the unplaced grobs from left to right.
        // If the grob doesn't overlap the right-most affected position, we place it
        // (and then update the right-most affected position to point to the right
        // edge of the just-placed grob).  Otherwise, we skip it until the next pass.
        while (elements.Count > 0)
        {
            DrulArray<double> lastEnd = new DrulArray<double>(
                double.NegativeInfinity, double.NegativeInfinity);
            List<Grob> skippedElements = new List<Grob>();
            for (int i = l2r ? 0 : elements.Count - 1;
                 l2r ? i < elements.Count : i >= 0;
                 i += l2r ? 1 : -1)
            {
                Grob elt = elements[i];
                double padding = NumberOr(
                    elt.GetProperty(OutsideStaffPadding),
                    GetDefaultOutsideStaffPadding());
                double horizonPadding = NumberOr(
                    elt.GetProperty(OutsideStaffHorizontalPadding), 0.0);
                Interval xExtent = elt.Extent(xCommon, Axis.X);
                xExtent.Widen(horizonPadding);

                Direction dir = DirectionalElementInterface.GetGrobDirection(elt);
                if (dir == Direction.Center)
                {
                    Warn.Warning(
                        "an outside-staff object should have a direction, "
                        + "defaulting to up");
                    dir = Direction.Positive;
                }

                if (xExtent[Direction.Negative] <= lastEnd[dir] && polite)
                {
                    skippedElements.Add(elt);
                    continue;
                }

                lastEnd[dir] = xExtent[Direction.Positive];

                SkylinePair vSkylines = ReadSkylinePair(elt, Axis.Y);
                if (vSkylines.IsEmpty)
                {
                    continue;
                }

                // Find the riders associated with this grob, and merge their
                // skylines with elt's skyline.
                List<SkylinePair> riderVSkylines = new List<SkylinePair>();
                if (riders.TryGetValue(elt, out List<Grob> eltRiders))
                {
                    foreach (Grob rider in eltRiders)
                    {
                        SkylinePair vRider = ReadSkylinePair(rider, Axis.Y);
                        vRider.Shift(rider.RelativeCoordinate(xCommon, Axis.X));
                        vRider.Raise(rider.RelativeCoordinate(yCommon, Axis.Y));
                        riderVSkylines.Add(vRider);
                    }
                }

                vSkylines.Shift(elt.RelativeCoordinate(xCommon, Axis.X));
                vSkylines.Raise(elt.RelativeCoordinate(yCommon, Axis.Y));
                vSkylines.Merge(new SkylinePair(riderVSkylines));

                AvoidOutsideStaffCollisions(
                    elt, vSkylines, padding, horizonPadding, allVSkylines[dir],
                    allPaddings[dir], allHorizonPaddings[dir], dir);

                elt.SetProperty(OutsideStaffPriority, false);
                allVSkylines[dir].Add(vSkylines);
                allPaddings[dir].Add(padding);
                allHorizonPaddings[dir].Add(horizonPadding);
            }

            elements = skippedElements;
        }
    }

    private readonly struct SkylineKey
    {
        internal SkylineKey(Grob grob, double priority, double leftExtent)
        {
            Grob = grob;
            Priority = priority;
            LeftExtent = leftExtent;
        }

        internal Grob Grob { get; }

        internal double Priority { get; }

        internal double LeftExtent { get; }
    }

    // It is tricky to correctly handle skyline placement of cross-staff grobs.
    // For example, cross-staff beams cannot be formatted until the distance between
    // staves is known and therefore any grobs that depend on the beam cannot be placed
    // until the skylines are known. On the other hand, the distance between staves should
    // really depend on position of the cross-staff grobs that lie between them.
    // Currently, we just leave cross-staff grobs out of the
    // skyline altogether, but this could mean that staves are placed so close together
    // that there is no room for the cross-staff grob. It also means, of course, that
    // we don't get the benefits of skyline placement for cross-staff grobs.

    /// <summary>
    /// The <c>calc-skylines</c> callback body: build the group's skylines from its
    /// inside-staff children, then place each outside-staff child — in
    /// <c>outside-staff-priority</c> order — just clear of everything already placed,
    /// MOVING the child as a side effect.
    /// </summary>
    /// <param name="me">The axis group.</param>
    /// <returns>The group's skyline pair.</returns>
    public static SkylinePair SkylineSpacing(Grob me)
    {
        Symbol elementsKey = me.GetObject(VerticalSkylineElementsSymbol) is GrobArray
            ? VerticalSkylineElementsSymbol
            : ElementsSymbol;
        IReadOnlyList<Grob> origElements
            = PointerGroupInterface.ExtractGrobSet(me, elementsKey);
        Grob xCommon = AxisGroupInterface.CommonRefpointOfArray(origElements, me, Axis.X);
        Grob yCommon = AxisGroupInterface.CommonRefpointOfArray(origElements, me, Axis.Y);

        if (!ReferenceEquals(yCommon, me))
        {
            Warn.ProgrammingError(
                "Some of my vertical-skyline-elements are outside my VerticalAxisGroup.");
            yCommon = me;
        }

        List<SkylineKey> elements = new List<SkylineKey>(origElements.Count);
        foreach (Grob g in origElements)
        {
            /*
              As a sanity check, we make sure that no grob with an outside staff priority
              has a Y-parent that also has an outside staff priority, which would result
              in two movings.
            */
            double priority = NumberOr(
                g.GetProperty(OutsideStaffPriority), double.NegativeInfinity);
            double leftExtent = 0;
            if (!double.IsInfinity(priority))
            {
                if (OutsideStaffAncestor(g) != null)
                {
                    Warn.Warning(
                        "Cannot set outside-staff-priority for element "
                        + "and elements' Y parent.");
                    g.SetProperty(OutsideStaffPriority, false);
                    priority = double.NegativeInfinity;
                }
                else
                {
                    leftExtent = g.Extent(xCommon, Axis.X)[Direction.Negative];
                }
            }

            elements.Add(new SkylineKey(g, priority, leftExtent));
        }

        // Upstream uses std::stable_sort; .NET's List.Sort is not stable, so ties on
        // BOTH keys are broken by the original index to reproduce the same order.
        int[] order = new int[elements.Count];
        for (int k = 0; k < order.Length; k++)
        {
            order[k] = k;
        }

        List<SkylineKey> unsorted = new List<SkylineKey>(elements);
        System.Array.Sort(order, (a, b) =>
        {
            int byPriority = unsorted[a].Priority.CompareTo(unsorted[b].Priority);
            if (byPriority != 0)
            {
                return byPriority;
            }

            int byLeft = unsorted[a].LeftExtent.CompareTo(unsorted[b].LeftExtent);
            return byLeft != 0 ? byLeft : a.CompareTo(b);
        });
        for (int k = 0; k < order.Length; k++)
        {
            elements[k] = unsorted[order[k]];
        }

        // A rider is a grob that is not outside-staff, but has an outside-staff
        // ancestor.  In that case, the rider gets moved along with its ancestor.
        Dictionary<Grob, List<Grob>> riders = new Dictionary<Grob, List<Grob>>();

        int i = 0;
        List<SkylinePair> insideStaffSkylines = new List<SkylinePair>();

        for (i = 0; i < elements.Count && double.IsInfinity(elements[i].Priority); i++)
        {
            Grob elt = elements[i].Grob;
            Grob ancestor = OutsideStaffAncestor(elt);
            if (ancestor != null)
            {
                if (!riders.TryGetValue(ancestor, out List<Grob> list))
                {
                    list = new List<Grob>();
                    riders[ancestor] = list;
                }

                list.Add(elt);
            }
            else if (!SchemeUtilities.ToBool(elt.GetProperty(CrossStaffSymbol)))
            {
                SkylinePair skyp = ReadSkylinePair(elt, Axis.Y);
                if (skyp.IsEmpty)
                {
                    continue;
                }

                skyp.Shift(elt.RelativeCoordinate(xCommon, Axis.X));
                skyp.Raise(elt.RelativeCoordinate(yCommon, Axis.Y));
                insideStaffSkylines.Add(skyp);
            }
        }

        SkylinePair skylines = new SkylinePair(insideStaffSkylines);

        // These are the skylines of all outside-staff grobs
        // that have already been processed.  We keep them around in order to
        // check them for collisions with the currently active outside-staff grob.
        DrulArray<List<SkylinePair>> allVSkylines = new DrulArray<List<SkylinePair>>(
            new List<SkylinePair>(), new List<SkylinePair>());
        DrulArray<List<double>> allPaddings = new DrulArray<List<double>>(
            new List<double>(), new List<double>());
        DrulArray<List<double>> allHorizonPaddings = new DrulArray<List<double>>(
            new List<double>(), new List<double>());
        foreach (Direction d in new[] { Direction.Positive, Direction.Negative })
        {
            // Upstream pushes a COPY of the running pair; SkylinePair is a class
            // here, so the copy is spelled out — the setup entry must not see the
            // final merge below through an alias.
            allVSkylines[d].Add(new SkylinePair(new[] { skylines }));
            allPaddings[d].Add(0);
            allHorizonPaddings[d].Add(0);
        }

        for (; i < elements.Count; i++)
        {
            if (SchemeUtilities.ToBool(elements[i].Grob.GetProperty(CrossStaffSymbol)))
            {
                continue;
            }

            // Collect all the outside-staff grobs that have a particular priority.
            List<Grob> currentElts = new List<Grob>();
            currentElts.Add(elements[i].Grob);
            while (i + 1 < elements.Count
                   && elements[i].Priority == elements[i + 1].Priority)
            {
                if (!SchemeUtilities.ToBool(
                        elements[i + 1].Grob.GetProperty(CrossStaffSymbol)))
                {
                    currentElts.Add(elements[i + 1].Grob);
                }

                ++i;
            }

            AddGrobsOfOnePriority(
                me, allVSkylines, allPaddings, allHorizonPaddings, currentElts,
                xCommon, yCommon, riders);
        }

        // Now everything in all_v_skylines has been shifted appropriately; merge
        // them all into skylines to get the complete outline.
        SkylinePair otherSkylines = new SkylinePair(allVSkylines[Direction.Positive]);
        otherSkylines.Merge(new SkylinePair(allVSkylines[Direction.Negative]));
        skylines.Merge(otherSkylines);

        // We began by shifting my skyline to be relative to the common refpoint; now
        // shift it back.
        skylines.Shift(-me.RelativeCoordinate(xCommon, Axis.X));

        return skylines;
    }

    /// <summary>
    /// The <c>calc-staff-staff-spacing</c> resolution: within a staff group the
    /// grouper's <c>staff-staff-spacing</c> rules, below its last staff the grouper's
    /// <c>staffgroup-staff-spacing</c>, and a groupless staff falls back on its own
    /// default.
    /// </summary>
    /// <param name="me">The staff's axis group.</param>
    /// <param name="pure">Whether this is a pure lookup.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <returns>The spacing spec.</returns>
    public static object CalcMaybePureStaffStaffSpacing(Grob me, bool pure, int start, int end)
    {
        Grob grouper = me.GetObject(StaffGrouperSymbol) as Grob;

        if (grouper != null)
        {
            bool withinGroup = StaffGrouperInterface.MaybePureWithinGroup(
                grouper, me, pure, start, end);
            if (withinGroup)
            {
                return grouper.GetMaybePureProperty(StaffStaffSpacingSymbol, pure, start, end);
            }
            else
            {
                return grouper.GetMaybePureProperty(
                    StaffgroupStaffSpacingSymbol, pure, start, end);
            }
        }

        return me.GetMaybePureProperty(DefaultStaffStaffSpacingSymbol, pure, start, end);
    }

    private static double NumberOr(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "axis-group-interface")
            : fallback;
}
