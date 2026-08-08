/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/side-position-interface.cc, lily/include/side-position-interface.hh, lily/grob.cc (get_vertical_axis_group only), lily/misc.cc (directed_round only);

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// Positions a victim grob NEXT TO a set of support grobs: above or below them
/// (<c>direction</c> decides which), just far enough away that their skylines clear,
/// padding, minimum spaces and staff quantization included. Scripts, dynamics,
/// fingerings and the system-start delimiters all sit on this.
/// <para>
/// PURE lookups — pure properties, pure coordinates, pure extents — take the ordinary
/// answers throughout, the same EPG15 stand-in the rest of the port uses and records
/// in PORT-COVERAGE. The <c>pure</c> flag's CONTROL-FLOW effects (which stems are
/// skipped, which X coordinate is used) are kept exactly.
/// </para>
/// </summary>
public static class SidePositionInterface
{
    private static readonly Symbol SideSupportElements = Symbol.Intern("side-support-elements");
    private static readonly Symbol AccidentalGrobs = Symbol.Intern("accidental-grobs");
    private static readonly Symbol AccidentalPlacementInterface
        = Symbol.Intern("accidental-placement-interface");

    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol QuantizePosition = Symbol.Intern("quantize-position");
    private static readonly Symbol StaffPadding = Symbol.Intern("staff-padding");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol AddStemSupport = Symbol.Intern("add-stem-support");
    private static readonly Symbol HorizonPadding = Symbol.Intern("horizon-padding");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol XPaddingSymbol = Symbol.Intern("X-padding");
    private static readonly Symbol MinimumSpace = Symbol.Intern("minimum-space");
    private static readonly Symbol MinimumXSpace = Symbol.Intern("minimum-X-space");
    private static readonly Symbol SideAxisSymbol = Symbol.Intern("side-axis");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");
    private static readonly Symbol AxisGroupInterfaceSymbol
        = Symbol.Intern("axis-group-interface");

    private static readonly Symbol AlignInterfaceSymbol = Symbol.Intern("align-interface");
    private static readonly Symbol StaffGrouperInterfaceSymbol
        = Symbol.Intern("staff-grouper-interface");

    private static readonly Symbol VerticalAlignment = Symbol.Intern("vertical-alignment");
    private static readonly Symbol XAlignedSideSymbol
        = Symbol.Intern("ly:side-position-interface::x-aligned-side");

    private static readonly Symbol YAlignedSideSymbol
        = Symbol.Intern("ly:side-position-interface::y-aligned-side");

    private static readonly Symbol PureYAlignedSideSymbol
        = Symbol.Intern("ly:side-position-interface::pure-y-aligned-side");

    /// <summary>Adds one grob to another's support set.</summary>
    /// <param name="me">The positioned grob.</param>
    /// <param name="e">The supporting grob.</param>
    public static void AddSupport(Grob me, Grob e)
    {
        PointerGroupInterface.AddUnorderedGrob(me, SideSupportElements, e);
    }

    /// <summary>
    /// Returns the support set, with each <c>AccidentalPlacement</c> expanded to the
    /// accidentals it carries.
    /// <para>
    /// Upstream collects into an <c>unordered_set</c>, whose iteration order is
    /// unspecified; the port keeps FIRST-OCCURRENCE order instead — deterministic, and
    /// the skyline arithmetic downstream is order-independent. Same choice
    /// <c>Grob_array::remove_duplicates</c> already made.
    /// </para>
    /// </summary>
    /// <param name="me">The positioned grob.</param>
    /// <returns>The distinct supports.</returns>
    public static IReadOnlyList<Grob> GetSupportSet(Grob me)
    {
        // Only slightly kludgy heuristic...
        // We want to make sure that all AccidentalPlacements'
        // accidentals make it into the side support
        IReadOnlyList<Grob> protoSupport
            = PointerGroupInterface.ExtractGrobSet(me, SideSupportElements);
        List<Grob> support = new List<Grob>();
        HashSet<Grob> seen = new HashSet<Grob>();

        void AddOnce(Grob grob)
        {
            if (grob != null && seen.Add(grob))
            {
                support.Add(grob);
            }
        }

        for (int i = 0; i < protoSupport.Count; i++)
        {
            if (protoSupport[i].HasInterface(AccidentalPlacementInterface))
            {
                Grob accs = protoSupport[i];
                object acs = accs.GetObject(AccidentalGrobs);
                while (acs is Pair acsPair)
                {
                    object s = acsPair.Car is Pair entry ? entry.Cdr : Nil.Instance;
                    while (s is Pair sPair)
                    {
                        AddOnce(sPair.Car as Grob);
                        s = sPair.Cdr;
                    }

                    acs = acsPair.Cdr;
                }
            }
            else
            {
                AddOnce(protoSupport[i]);
            }
        }

        return support;
    }

    /*
      Position next to support, taking into account my own dimensions and padding.
    */
    private static object AxisAlignedSideHelper(
        Grob me,
        Axis a,
        bool pure,
        int start,
        int end,
        object currentOffScm)
    {
        double? currentOff = null;
        if (SchemeConvert.IsNumber(currentOffScm))
        {
            currentOff = SchemeConvert.ToDouble(currentOffScm, "aligned-side");
        }

        // We will only ever want widths of spanners after line breaking
        // so we can set pure to false
        if (a == Axis.X && me is Spanner)
        {
            pure = false;
        }

        return AlignedSide(me, a, pure, start, end, currentOff);
    }

    /// <summary>The <c>x-aligned-side</c> callback body.
    /// <para>
    /// Upstream's comment: because horizontal skylines need vertical heights, an
    /// unpure call before line breaking would trigger too much, so X positioning
    /// always asks PURE. The pure flag's property lookups fall back to the unpure
    /// answers here (EPG15 stand-in), but its control flow is kept.
    /// </para>
    /// </summary>
    /// <param name="me">The grob to position.</param>
    /// <param name="currentOff">The current offset, or <see langword="null"/>.</param>
    /// <returns>The offset, as a Scheme number.</returns>
    public static object XAlignedSide(Grob me, object currentOff)
        => AxisAlignedSideHelper(me, Axis.X, true, 0, 0, currentOff);

    /// <summary>The <c>y-aligned-side</c> callback body.</summary>
    /// <param name="me">The grob to position.</param>
    /// <param name="currentOff">The current offset, or <see langword="null"/>.</param>
    /// <returns>The offset, as a Scheme number.</returns>
    public static object YAlignedSide(Grob me, object currentOff)
        => AxisAlignedSideHelper(me, Axis.Y, false, 0, 0, currentOff);

    /// <summary>The <c>pure-y-aligned-side</c> callback body.</summary>
    /// <param name="me">The grob to position.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <param name="currentOff">The current offset, or <see langword="null"/>.</param>
    /// <returns>The offset, as a Scheme number.</returns>
    public static object PureYAlignedSide(Grob me, int start, int end, object currentOff)
        => AxisAlignedSideHelper(me, Axis.Y, true, start, end, currentOff);

    /// <summary>
    /// The <c>calc-cross-staff</c> callback body: a side-positioned grob is
    /// cross-staff when any support is cross-staff and free to move with the staff
    /// spacing, or when any support hangs from a different vertical axis group.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <returns>Whether the grob is cross-staff.</returns>
    public static bool CalcCrossStaff(Grob me)
    {
        IReadOnlyList<Grob> elts
            = PointerGroupInterface.ExtractGrobSet(me, SideSupportElements);

        Direction myDir = DirectionalElementInterface.GetGrobDirection(me);

        for (int i = 0; i < elts.Count; i++)
        {
            /*
              If 'me' is placed relative to any cross-staff element with a
              'direction callback defined, the placement of 'me' is likely
              to depend on staff-spacing, thus 'me' should be considered
              cross-staff.
            */
            if (SchemeUtilities.ToBool(elts[i].GetProperty(CrossStaffSymbol))
                && !DirectionalElementInterface.IsDirection(
                    elts[i].GetPropertyData(DirectionSymbol)))
            {
                return true;
            }

            /*
              If elts[i] is cross-staff and is pointing in the same
              direction as 'me', we assume that the alignment
              of 'me' is influenced the cross-staffitude of elts[i]
              and thus we mark 'me' as cross-staff.
            */
            if (SchemeUtilities.ToBool(elts[i].GetProperty(CrossStaffSymbol))
                && myDir == DirectionalElementInterface.GetGrobDirection(elts[i]))
            {
                return true;
            }
        }

        Grob myVag = GetVerticalAxisGroup(me);
        for (int i = 0; i < elts.Count; i++)
        {
            if (!ReferenceEquals(myVag, GetVerticalAxisGroup(elts[i])))
            {
                return true;
            }
        }

        return false;
    }

    // long function - each stage is clearly marked

    /// <summary>Positions a grob next to its supports. The workhorse.</summary>
    /// <param name="me">The grob to position.</param>
    /// <param name="a">The axis to move along.</param>
    /// <param name="pure">Whether this is a pure computation.</param>
    /// <param name="start">The starting column rank of the pure range.</param>
    /// <param name="end">The ending column rank of the pure range.</param>
    /// <param name="currentOff">The already-computed offset, or <see langword="null"/>.</param>
    /// <returns>The offset, as a Scheme number.</returns>
    public static object AlignedSide(
        Grob me,
        Axis a,
        bool pure,
        int start,
        int end,
        double? currentOff)
    {
        Direction dir = DirectionalElementInterface.GetGrobDirection(me);

        if (!dir.IsNonZero)
        {
            // This is occasionally useful, for example to place
            // scripts in the middle of two piano staves using a
            // Dynamics context.
            return currentOff ?? 0.0;
        }

        IReadOnlyList<Grob> support = GetSupportSet(me);

        Grob[] common = new Grob[2];
        foreach (Axis ax in new[] { Axis.X, Axis.Y })
        {
            common[(int)ax] = AxisGroupInterface.CommonRefpointOfArray(
                support, ax == a ? me.GetParent(ax) : me, ax);
        }

        Grob staffSymbol = StaffSymbolReferencer.GetStaffSymbol(me);
        bool quantizePosition = SchemeUtilities.ToBool(me.GetProperty(QuantizePosition));
        bool meCrossStaff = SchemeUtilities.ToBool(me.GetProperty(CrossStaffSymbol));

        bool includeStaff = staffSymbol != null && a == Axis.Y
                            && SchemeConvert.IsNumber(me.GetProperty(StaffPadding))
                            && !quantizePosition;

        if (includeStaff)
        {
            common[(int)Axis.Y] = staffSymbol.CommonRefpoint(common[(int)Axis.Y], Axis.Y);
        }

        Skyline myDim = new Skyline(-dir);

        // ReadSkylinePair stands in for upstream's constructor-default skyline
        // callbacks, so — as upstream — the read always answers a pair. For the X
        // axis the pair wanted is the horizontal one, which is what the helper's
        // axis argument selects.
        SkylinePair myPair = AxisGroupInterfaceVertical.ReadSkylinePair(me, a);
        {
            // for spanner pure heights, we don't know horizontal spacing,
            // so a spanner can never have a meaningful x coordiante
            // we just give it the parents' coordinate because its
            // skyline will likely be of infinite width anyway
            // and we don't want to prematurely trigger H spacing
            double xc;
            if (a == Axis.X)
            {
                xc = me.ParentRelative(common[(int)Axis.X], Axis.X);
            }
            else // Y_AXIS
            {
                if (!pure)
                {
                    xc = me.RelativeCoordinate(common[(int)Axis.X], Axis.X);
                }
                else
                {
                    // Not safe to call X-offset callbacks here as that may
                    // trigger stem and beam direction, so just set to 0
                    xc = 0;
                }
            }

            // same here, for X_AXIS spacing, if it's happening, it should only be
            // before line breaking.  because there is no thing as "pure" x spacing,
            // we assume that it is all pure
            double yc = a == Axis.X
                ? me.RelativeCoordinate(common[(int)Axis.Y], Axis.Y)
                : me.GetParent(Axis.Y).RelativeCoordinate(common[(int)Axis.Y], Axis.Y);
            myPair.Shift(a == Axis.X ? yc : xc);
            myPair.Raise(a == Axis.X ? xc : yc);
            myDim = myPair[-dir];
        }

        List<Box> boxes = new List<Box>();
        List<SkylinePair> skyps = new List<SkylinePair>();

        foreach (Grob e in support)
        {
            bool crossStaff = SchemeUtilities.ToBool(e.GetProperty(CrossStaffSymbol));
            if (a == Axis.Y
                && !meCrossStaff // 'me' promised not to adapt to staff-spacing
                && crossStaff)   // but 'e' might move based on staff-pacing
            {
                continue;        // so 'me' may not move in response to 'e'
            }

            if (a == Axis.Y && e.HasInterface(StemInterface))
            {
                // If called as 'pure' we may not force a stem to set its direction,
                if (pure && !DirectionalElementInterface.IsDirection(
                        e.GetPropertyData(DirectionSymbol)))
                {
                    continue;
                }

                // There is no need to consider stems pointing away.
                if (dir == -DirectionalElementInterface.GetGrobDirection(e))
                {
                    continue;
                }
            }

            SkylinePair skyp = AxisGroupInterfaceVertical.ReadSkylinePair(e, a);

            {
                double xc = pure && e is Spanner
                    ? e.ParentRelative(common[(int)Axis.X], Axis.X)
                    : e.RelativeCoordinate(common[(int)Axis.X], Axis.X);

                // same logic as above
                // we assume horizontal spacing is always pure
                double yc = a == Axis.X
                    ? e.RelativeCoordinate(common[(int)Axis.Y], Axis.Y)
                    : e.RelativeCoordinate(common[(int)Axis.Y], Axis.Y);
                if (a == Axis.Y && e.HasInterface(StemInterface)
                    && SchemeUtilities.ToBool(me.GetProperty(AddStemSupport)))
                {
                    skyp[dir].SetMinimumHeight(skyp[dir].MaxHeight());
                }

                skyp.Shift(a == Axis.X ? yc : xc);
                skyp.Raise(a == Axis.X ? xc : yc);
                skyps.Add(skyp);
            }
        }

        Skyline dim = new Skyline(boxes, OtherAxis(a), dir);
        if (skyps.Count > 0)
        {
            SkylinePair merged = new SkylinePair(skyps);
            dim.Merge(merged[dir]);
        }

        if (includeStaff)
        {
            common[(int)Axis.Y] = staffSymbol.CommonRefpoint(common[(int)Axis.Y], Axis.Y);
            Interval staffExtents = staffSymbol.Extent(common[(int)Axis.Y], Axis.Y);
            dim.SetMinimumHeight(staffExtents[dir]);
        }

        // Sometimes, we want to side position for grobs but they
        // don't position against anything.  Some cases where this is true:
        //   - StanzaNumber if the supporting lyrics are hara-kiri'd
        //     SystemStartBracket
        //     InstrumentName
        // In all these cases, we set the height of the support to 0.
        // This becomes then like the self-alignment-interface with the
        // caveat that there is padding added.
        // TODO: if there is a grob that never has side-support-elements
        // (like InstrumentName), why are we using this function? Isn't it
        // overkill? A function like self-alignment-interface with padding
        // works just fine.
        // One could even imagine the two interfaces merged, as the only
        // difference is that in self-alignment-interface we align on the parent
        // where as here we align on a group of grobs.
        if (dim.IsEmpty)
        {
            dim = new Skyline(dim.GetDirection());
            dim.SetMinimumHeight(0.0);
        }

        double ss = StaffSymbolReferencer.StaffSpace(me);
        double dist = dim.Distance(
            myDim, NumberOr(me.GetProperty(HorizonPadding), 0.0));
        double totalOff = !double.IsInfinity(dist) ? dir * dist : 0.0;

        double padding = NumberOr(me.GetProperty(PaddingSymbol), 0.0);
        if (a == Axis.X)
        {
            object xPadding = me.GetProperty(XPaddingSymbol);
            if (SchemeConvert.IsNumber(xPadding))
            {
                padding = SchemeConvert.ToDouble(xPadding, "X-padding");
            }
        }

        totalOff += dir * ss * padding;

        double minimumSpace = ss * NumberOr(me.GetProperty(MinimumSpace), -1);
        if (a == Axis.X)
        {
            object minimumXSpace = me.GetProperty(MinimumXSpace);
            if (SchemeConvert.IsNumber(minimumXSpace))
            {
                minimumSpace = ss * SchemeConvert.ToDouble(minimumXSpace, "minimum-X-space");
            }
        }

        if (minimumSpace >= 0 && dir.IsNonZero && totalOff * dir < minimumSpace)
        {
            totalOff = minimumSpace * dir;
        }

        if (currentOff.HasValue)
        {
            totalOff = dir * Math.Max(dir * totalOff, dir * currentOff.Value);
        }

        /* FIXME: 1000 should relate to paper size.  */
        if (Math.Abs(totalOff) > 1000)
        {
            // Upstream can additionally raise a Scheme error here under
            // -dstrict-infinity-checking; the port carries no such option yet, so
            // only the diagnostic half is reachable.
            Warn.ProgrammingError(
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Improbable offset for grob {0}: {1:F6}",
                    me.Name,
                    totalOff));
        }

        /*
          Ensure 'staff-padding' from my refpoint to the staff.  This is similar to
          side-position with padding, but it will put adjoining objects on a row if
          stuff sticks out of the staff a little.
        */
        Grob staff = StaffSymbolReferencer.GetStaffSymbol(me);
        if (staff != null && a == Axis.Y)
        {
            if (quantizePosition)
            {
                Grob quantizeCommon = me.CommonRefpoint(staff, Axis.Y);
                double myOff = me.GetParent(Axis.Y)
                    .RelativeCoordinate(quantizeCommon, Axis.Y);
                double staffOff = staff.RelativeCoordinate(quantizeCommon, Axis.Y);
                double staffSpace = StaffSymbol.StaffSpace(staff);
                double position = 2 * (myOff + totalOff - staffOff) / staffSpace;
                double rounded = DirectedRound(position, dir);
                Grob head = me.GetParent(Axis.X);

                Interval staffSpan = StaffSymbol.LineSpan(staff);
                staffSpan.Widen(1);
                if (
                    staffSpan.Contains(position)
                    /* If we are between notehead and staff, quantize for ledger lines. */
                    || (head != null && head.HasInterface(NoteHeadInterface)
                        && dir * position < 0))
                {
                    totalOff += (rounded - position) * 0.5 * staffSpace;
                    if (StaffSymbolReferencer.OnLine(me, (int)rounded))
                    {
                        totalOff += dir * 0.5 * staffSpace;
                    }
                }
            }
            else if (SchemeConvert.IsNumber(me.GetProperty(StaffPadding)) && dir.IsNonZero)
            {
                double staffPadding = StaffSymbolReferencer.StaffSpace(me)
                                      * SchemeConvert.ToDouble(
                                          me.GetProperty(StaffPadding), "staff-padding");

                Grob parent = me.GetParent(Axis.Y);
                Grob paddingCommon = me.CommonRefpoint(staff, Axis.Y);
                double parentPosition = parent.RelativeCoordinate(paddingCommon, Axis.Y);
                double staffPosition = staff.RelativeCoordinate(paddingCommon, Axis.Y);
                Interval staffExtent = staff.Extent(staff, a);
                double diff
                    = (dir * staffExtent[dir]) + staffPadding - (dir * totalOff)
                      + (dir * (staffPosition - parentPosition));
                totalOff += dir * Math.Max(diff, 0.0);
            }
        }

        return totalOff;
    }

    /// <summary>
    /// Declares which axis a grob is side-positioned on and installs the matching
    /// offset callback — chained, so an existing callback still runs underneath.
    /// The Y callback goes in as an unpure/pure pair, exactly as upstream wraps it.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <param name="a">The axis to position on.</param>
    public static void SetAxis(Grob me, Axis a)
    {
        if (!SchemeConvert.IsNumber(me.GetProperty(SideAxisSymbol)))
        {
            me.SetProperty(SideAxisSymbol, (long)(int)a);
            object proc;
            if (a == Axis.X)
            {
                proc = LilyPondScheme.LookupProcedure(XAlignedSideSymbol);
            }
            else
            {
                proc = new UnpurePureContainer(
                    LilyPondScheme.LookupProcedure(YAlignedSideSymbol),
                    LilyPondScheme.LookupProcedure(PureYAlignedSideSymbol));
            }

            GrobClosure.ChainOffsetCallback(me, proc, a);
        }
    }

    private static bool IsOnAxis(Grob me, Axis a)
    {
        object axisScm = me.GetProperty(SideAxisSymbol);
        if (IsAxis(axisScm))
        {
            return ToAxis(axisScm) == a;
        }

        // scm_is_true upstream: only #f is false, so a grob whose stencil property is
        // merely UNSET ('()) still earns the diagnostic.
        if (SchemeUtilities.IsSchemeTrue(me.GetProperty(StencilSymbol)))
        {
            Warn.ProgrammingError(
                "no side-axis setting found for grob " + me.Name + ".");
        }

        return false;
    }

    /// <summary>Determines whether a grob is side-positioned horizontally.</summary>
    /// <param name="me">The grob.</param>
    /// <returns><see langword="true"/> when its <c>side-axis</c> is X.</returns>
    public static bool IsOnXAxis(Grob me) => IsOnAxis(me, Axis.X);

    /// <summary>Determines whether a grob is side-positioned vertically.</summary>
    /// <param name="me">The grob.</param>
    /// <returns><see langword="true"/> when its <c>side-axis</c> is Y.</returns>
    public static bool IsOnYAxis(Grob me) => IsOnAxis(me, Axis.Y);

    /// <summary>
    /// The <c>move-to-extremal-staff</c> callback body: reparent a mark-like grob to
    /// the topmost (or bottommost) staff its horizontal span overlaps.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <returns>Whether the grob was moved.</returns>
    public static bool MoveToExtremalStaff(Grob me)
    {
        Direction dir = DirectionalElementInterface.GetGrobDirection(me);
        if (dir != Direction.Negative)
        {
            dir = Direction.Positive;
        }

        SystemGrob sys = me.GetSystem();
        Interval iv = me.Extent(sys, Axis.X);
        iv.Widen(1.0);

        Grob grouper = me.GetParent(Axis.Y);
        if (grouper != null && grouper.HasInterface(StaffGrouperInterfaceSymbol))
        {
            // find the extremal staff of this group
        }
        else if (ReferenceEquals(grouper, sys))
        {
            // find the extremal staff of the whole system
            grouper = sys.GetObject(VerticalAlignment) as Grob;
            if (grouper == null)
            {
                return false;
            }
        }
        else // do not move marks from other staves to the top staff
        {
            return false;
        }

        // N.B. It's ugly to pass a VerticalAlignment to this staff-grouper function.
        // Read the comments in the function for more detail.
        Grob topStaff = StaffGrouperInterface.GetExtremalStaff(grouper, sys, dir, iv);
        if (topStaff == null)
        {
            return false;
        }

        me.SetParent(topStaff, Axis.Y);
        me.FlushExtentCache(Axis.Y);
        AxisGroupInterface.AddElement(topStaff, me);

        // Remove any cross-staff side-support dependencies
        GrobArray ga = me.GetObject(SideSupportElements) as GrobArray;
        if (ga != null)
        {
            List<Grob> newElts = new List<Grob>();
            foreach (Grob g in ga)
            {
                if (ReferenceEquals(me.CommonRefpoint(g, Axis.Y), topStaff))
                {
                    newElts.Add(g);
                }
            }

            ga.SetArray(newElts);
        }

        return true;
    }

    /// <summary>
    /// <c>Grob::get_vertical_axis_group</c>, carried here from <c>lily/grob.cc</c>
    /// because <c>Objects/Grob.cs</c> predates it and stays closed in this pass:
    /// walk Y parents until finding an axis group whose own Y parent is an alignment.
    /// </summary>
    /// <param name="g">The grob to start from.</param>
    /// <returns>The vertical axis group, or <see langword="null"/>.</returns>
    public static Grob GetVerticalAxisGroup(Grob g)
    {
        if (g == null)
        {
            return null;
        }

        if (g.GetParent(Axis.Y) == null)
        {
            return null;
        }

        if (g.HasInterface(AxisGroupInterfaceSymbol)
            && g.GetParent(Axis.Y).HasInterface(AlignInterfaceSymbol))
        {
            return g;
        }

        return GetVerticalAxisGroup(g.GetParent(Axis.Y));
    }

    /// <summary>
    /// <c>directed_round</c> from <c>lily/misc.cc</c>: floor for DOWN, ceiling
    /// otherwise.
    /// </summary>
    /// <param name="f">The value to round.</param>
    /// <param name="d">The direction to round toward.</param>
    /// <returns>The rounded value.</returns>
    public static double DirectedRound(double f, Direction d)
    {
        if ((int)d < 0)
        {
            return Math.Floor(f);
        }
        else
        {
            return Math.Ceiling(f);
        }
    }

    private static Axis OtherAxis(Axis axis) => axis == Axis.X ? Axis.Y : Axis.X;

    private static bool IsAxis(object value)
        => (value is long l && (l == 0 || l == 1)) || (value is int i && (i == 0 || i == 1));

    private static Axis ToAxis(object value)
        => SchemeConvert.ToLong(value, "side-axis") == 0 ? Axis.X : Axis.Y;

    private static double NumberOr(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "side-position-interface")
            : fallback;
}
