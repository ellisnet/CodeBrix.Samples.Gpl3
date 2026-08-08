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

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/line-spanner.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - Line_spanner and Horizontal_line_spanner share a file upstream and share one here.
//     The horizontal one is nothing but three thin callbacks that pass horizontal = true.
//   - the two `unbroken-or-*-broken-spanner?` predicates are Scheme (scm/lily-library.scm),
//     reached by name rather than reimplemented, exactly as upstream's Lily::Variable does.
//   - upstream's `assert (extreme_bound_groups_there[dir])` is an ASSERT, which is
//     compiled out of a release build; the port raises nothing and takes the same branch
//     the assert permits, because a null there would already have been a null dereference
//     one line later either way. See PORT-COVERAGE.

/*
  There are two types of non-vertical line spanners we want to distinguish.

  * The normal ones (such as `Glissando` and `VoiceFollower`) usually
    compute Y positions automatically, and even when the positions are
    tweaked by the user, they try to make them relative to their containing
    vertical axis group.

  * The horizontal ones (`TextSpanner`, `DynamicTextSpanner`, etc.) don't
    try to compute reference points.  In particular, for these, user tweaks
    to Y values are always relative to the spanner itself.  This means that
    horizontal line spanners can be side-positioned without causing cyclic
    dependencies on their distance from the staff.
*/

/// <summary>
/// A generic line drawn between two objects, e.g., for use with glissandi.
/// </summary>
public static class LineSpanner
{
    private static readonly Symbol BoundDetailsSymbol = Symbol.Intern("bound-details");
    private static readonly Symbol LeftSymbol = Symbol.Intern("left");
    private static readonly Symbol RightSymbol = Symbol.Intern("right");
    private static readonly Symbol LeftBrokenSymbol = Symbol.Intern("left-broken");
    private static readonly Symbol RightBrokenSymbol = Symbol.Intern("right-broken");
    private static readonly Symbol DefaultSymbol = Symbol.Intern("default");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol XSymbol = Symbol.Intern("X");
    private static readonly Symbol YSymbol = Symbol.Intern("Y");
    private static readonly Symbol AttachDirSymbol = Symbol.Intern("attach-dir");
    private static readonly Symbol EndOnNoteSymbol = Symbol.Intern("end-on-note");
    private static readonly Symbol EndOnAccidentalSymbol = Symbol.Intern("end-on-accidental");
    private static readonly Symbol StartAtDotSymbol = Symbol.Intern("start-at-dot");
    private static readonly Symbol AdjustOnNeighborSymbol = Symbol.Intern("adjust-on-neighbor");
    private static readonly Symbol LeftNeighborSymbol = Symbol.Intern("left-neighbor");
    private static readonly Symbol RightNeighborSymbol = Symbol.Intern("right-neighbor");
    private static readonly Symbol NoteColumnsSymbol = Symbol.Intern("note-columns");
    private static readonly Symbol AxisGroupParentYSymbol = Symbol.Intern("axis-group-parent-Y");
    private static readonly Symbol DotSymbol = Symbol.Intern("dot");
    private static readonly Symbol CommonYSymbol = Symbol.Intern("common-Y");
    private static readonly Symbol ExtraDySymbol = Symbol.Intern("extra-dy");
    private static readonly Symbol FontSizeSymbol = Symbol.Intern("font-size");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol ArrowSymbol = Symbol.Intern("arrow");
    private static readonly Symbol StencilAlignDirYSymbol
        = Symbol.Intern("stencil-align-dir-y");
    private static readonly Symbol StencilOffsetSymbol = Symbol.Intern("stencil-offset");
    private static readonly Symbol NormalizedEndpointsSymbol
        = Symbol.Intern("normalized-endpoints");
    private static readonly Symbol LeftBoundInfoSymbol = Symbol.Intern("left-bound-info");
    private static readonly Symbol RightBoundInfoSymbol = Symbol.Intern("right-bound-info");
    private static readonly Symbol PaperColumnInterface = Symbol.Intern("paper-column-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol UnbrokenOrFirstBrokenSpannerSymbol
        = Symbol.Intern("unbroken-or-first-broken-spanner?");
    private static readonly Symbol UnbrokenOrLastBrokenSpannerSymbol
        = Symbol.Intern("unbroken-or-last-broken-spanner?");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    /// <summary>
    /// Reads two grobs' vertical coordinates against whichever common reference point
    /// they have, tolerating either being absent — <c>offsets_maybe</c>.
    /// </summary>
    /// <param name="grobs">The pair of grobs.</param>
    /// <param name="common">Receives the common reference point.</param>
    /// <returns>The pair of coordinates.</returns>
    public static DrulArray<double> OffsetsMaybe(DrulArray<Grob> grobs, out Grob common)
    {
        Grob g1 = grobs[Direction.Negative];
        Grob g2 = grobs[Direction.Positive];

        if (g1 != null && g2 != null)
        {
            common = g1.CommonRefpoint(g2, Axis.Y);
            double coord1 = g1.RelativeCoordinate(common, Axis.Y);
            double coord2 = g2.RelativeCoordinate(common, Axis.Y);
            return new DrulArray<double>(coord1, coord2);
        }

        if (g1 != null)
        {
            common = g1;
            double coord1 = g1.RelativeCoordinate(common, Axis.Y);

            // The 0.0 shouldn't get used.
            return new DrulArray<double>(coord1, 0.0);
        }

        if (g2 != null)
        {
            common = g2;
            double coord2 = g2.RelativeCoordinate(common, Axis.Y);
            return new DrulArray<double>(0.0, coord2);
        }

        common = null;
        return new DrulArray<double>(0.0, 0.0);
    }

    /// <summary>
    /// Works out where one end of the line sits, and what decoration goes there —
    /// <c>Line_spanner::calc_bound_info</c>.
    /// </summary>
    /// <param name="me">The line spanner.</param>
    /// <param name="dir">Which end.</param>
    /// <param name="horizontal">Whether this is a horizontal line spanner.</param>
    /// <returns>The bound-details alist for that end.</returns>
    public static object CalcBoundInfo(Spanner me, Direction dir, bool horizontal)
    {
        Item boundItem = me.GetBound(dir);

        object boundDetails = me.GetProperty(BoundDetailsSymbol);

        object details = SchemeUtilities.LyAssocGet(
            dir == Direction.Negative ? LeftSymbol : RightSymbol, boundDetails, false);

        // Don't use bound_item->break_status_dir (): a spanner running to the end of
        // the piece has a broken right bound, but should not get details from
        // right-broken.
        object checker = LilyPondScheme.LookupProcedure(
            dir == Direction.Negative
                ? UnbrokenOrFirstBrokenSpannerSymbol
                : UnbrokenOrLastBrokenSpannerSymbol);
        bool unfinishedAtBound
            = !SchemeUtilities.IsSchemeTrue(SchemeUtilities.CallCallback(checker, me));
        if (unfinishedAtBound)
        {
            object extra = SchemeUtilities.LyAssocGet(
                dir == Direction.Negative ? LeftBrokenSymbol : RightBrokenSymbol,
                boundDetails,
                Nil.Instance);

            details = SchemeUtilities.LyAppend(
                extra, SchemeUtilities.IsSchemeTrue(details) ? details : Nil.Instance);
        }

        if (!SchemeUtilities.IsSchemeTrue(details))
        {
            details = SchemeUtilities.LyAssocGet(DefaultSymbol, boundDetails, Nil.Instance);
        }

        object text = SchemeUtilities.LyAssocGet(TextSymbol, details, false);
        if (TextInterface.IsMarkup(text))
        {
            details = new Pair(
                new Pair(StencilSymbol, TextInterface.GrobInterpretMarkup(me, text)), details);
        }

        if (!SchemeConvert.IsNumber(SchemeUtilities.LyAssocGet(XSymbol, details, false)))
        {
            Grob commonx = me.GetBound(Direction.Negative)
                .CommonRefpoint(me.GetBound(Direction.Positive), Axis.X);
            commonx = me.CommonRefpoint(commonx, Axis.X);

            Direction attach = DirectionalElementInterface.FromScheme(
                SchemeUtilities.LyAssocGet(AttachDirSymbol, details, false), Direction.Center);

            Grob boundGrob = boundItem;
            object endNote = SchemeUtilities.LyAssocGet(EndOnNoteSymbol, details, false);
            if (SchemeUtilities.ToBool(endNote) && unfinishedAtBound)
            {
                IReadOnlyList<Grob> columns
                    = PointerGroupInterface.ExtractGrobSet(me, NoteColumnsSymbol);
                if (columns.Count > 0)
                {
                    boundGrob = dir == Direction.Negative
                        ? columns[0]
                        : columns[columns.Count - 1];
                }
            }

            double xCoord = (boundGrob.HasInterface(PaperColumnInterface)
                    ? AxisGroupInterfaceVertical.GenericBoundExtent(boundGrob, commonx, Axis.X)
                    : LooseColumns.RobustRelativeExtent(boundGrob, commonx, Axis.X))
                .LinearCombination((int)attach);

            object endAcc = SchemeUtilities.LyAssocGet(EndOnAccidentalSymbol, details, false);
            if (SchemeUtilities.ToBool(endAcc))
            {
                Grob maybeNoteColumn = null;

                // If the bound is already a note column, use it.
                if (boundGrob.HasInterface(NoteColumnInterface))
                {
                    maybeNoteColumn = boundGrob;
                }
                else
                {
                    /* Our bound may be a note head or rest, so try the parent
                       axis group. */
                    Grob ag = boundGrob.GetObject(AxisGroupParentYSymbol) as Grob;
                    if (ag != null && ag.HasInterface(NoteColumnInterface))
                    {
                        maybeNoteColumn = ag;
                    }
                }

                if (maybeNoteColumn != null)
                {
                    if (NoteColumn.Accidentals(maybeNoteColumn) is Grob accPlacement)
                    {
                        xCoord = LooseColumns
                            .RobustRelativeExtent(accPlacement, commonx, Axis.X)
                            .LinearCombination((int)attach);
                    }
                }
            }

            Grob dot = boundGrob.GetObject(DotSymbol) as Grob;
            if (dot != null
                && SchemeUtilities.ToBool(
                    SchemeUtilities.LyAssocGet(StartAtDotSymbol, details, false)))
            {
                xCoord = LooseColumns.RobustRelativeExtent(dot, commonx, Axis.X)
                    .LinearCombination((int)attach);
            }

            object adj = SchemeUtilities.LyAssocGet(AdjustOnNeighborSymbol, details, false);
            if (SchemeUtilities.ToBool(adj))
            {
                Symbol sym = dir == Direction.Negative
                    ? LeftNeighborSymbol
                    : RightNeighborSymbol;
                if (me.GetObject(sym) is Grob neighbor)
                {
                    Interval neighborExt = neighbor.Extent(commonx, Axis.X);
                    double neighborX = neighborExt[-dir];
                    xCoord = dir == Direction.Negative
                        ? Math.Max(xCoord, neighborX)
                        : Math.Min(xCoord, neighborX);
                }
            }

            details = new Pair(new Pair(XSymbol, xCoord), details);
        }

        Grob commonY;
        if (horizontal)
        {
            commonY = me;
        }
        else
        {
            bool yNeeded
                = !SchemeConvert.IsNumber(SchemeUtilities.LyAssocGet(YSymbol, details, false));

            // Even when we don't need to compute a Y value, run through part
            // of the code below in order to convey a reference point.  The
            // purpose is to make user tweaks to the Y value relative to the
            // relevant staff in the case of cross-staff line spanners.  Note
            // that this is not relevant for horizontal line spanners, the Y
            // value is always relative to the spanner itself.
            double y = 0.0;

            if (unfinishedAtBound)
            {
                /*
                  We want to compute the slope of something like a glissando
                  when broken across several systems.  We make it continuous,
                  giving the same slope to all pieces and choosing it such that
                  visually it could be one glissando line if the systems were
                  stuck together in a row.  For cross-staff glissandi the distance
                  between the two staves can vary across the break, and there is
                  not an obvious way to choose the common slope.  This code makes
                  the choice of Solomon: align the middles of each pair of staves.
                */

                Spanner orig = me.Original ?? me;
                SystemGrob sysHere = me.GetSystem();
                DrulArray<Item> extremeBounds = new DrulArray<Item>(null, null);
                DrulArray<Grob> extremeBoundGroups = new DrulArray<Grob>(null, null);
                foreach (Direction d in Both)
                {
                    if (orig.BrokenIntos.Count == 0)
                    {
                        return details;
                    }

                    Spanner extreme = d == Direction.Negative
                        ? orig.BrokenIntos[0]
                        : orig.BrokenIntos[orig.BrokenIntos.Count - 1];
                    extremeBounds[d] = extreme.GetBound(d);
                    extremeBoundGroups[d]
                        = SidePositionInterface.GetVerticalAxisGroup(extremeBounds[d]);
                    if (extremeBoundGroups[d] == null)
                    {
                        Warn.ProgrammingError(
                            "extremal broken spanner's bound has no parent"
                            + " vertical axis group");
                        return details;
                    }
                }

                SystemGrob sysThere = extremeBounds[dir].GetSystem();
                DrulArray<Grob> extremeBoundGroupsHere = new DrulArray<Grob>(null, null);
                DrulArray<Grob> extremeBoundGroupsThere = new DrulArray<Grob>(null, null);
                foreach (Direction d in Both)
                {
                    // This one can be null if the corresponding staff ended
                    // prematurely or started after the beginning of the score.
                    extremeBoundGroupsHere[d]
                        = (extremeBoundGroups[d].Original ?? extremeBoundGroups[d])
                            .FindBrokenPiece(sysHere);

                    // Can be null for the direction other than dir.
                    extremeBoundGroupsThere[d]
                        = (extremeBoundGroups[d].Original ?? extremeBoundGroups[d])
                            .FindBrokenPiece(sysThere);
                }

                DrulArray<double> offsetsHere
                    = OffsetsMaybe(extremeBoundGroupsHere, out Grob commonHere);
                DrulArray<double> offsetsThere
                    = OffsetsMaybe(extremeBoundGroupsThere, out Grob commonThere);

                if (yNeeded)
                {
                    // Here we have all weird edge cases that can happen
                    // when staves are added or removed midway.
                    if (extremeBoundGroupsHere[dir] == null
                        && extremeBoundGroupsHere[-dir] == null)
                    {
                        // If neither of the staves is present on this system, just
                        // disappear.  This can happen with contorted input that starts
                        // a glissando, stops that staff, then later spawns another
                        // staff and ends the glissando there.
                        me.Suicide();
                        return Unspecified.Instance;
                    }

                    double offsetHere;
                    double offsetThere;
                    if (extremeBoundGroupsThere[-dir] != null)
                    {
                        if (extremeBoundGroupsHere[dir] != null
                            && extremeBoundGroupsHere[-dir] != null)
                        {
                            offsetHere = (offsetsHere[Direction.Negative]
                                          + offsetsHere[Direction.Positive]) / 2;
                            offsetThere = (offsetsThere[Direction.Negative]
                                           + offsetsThere[Direction.Positive]) / 2;
                        }
                        else if (extremeBoundGroupsHere[dir] != null)
                        {
                            offsetHere = offsetsHere[dir];
                            offsetThere = offsetsThere[dir];
                        }
                        else
                        {
                            offsetHere = offsetsHere[-dir];
                            offsetThere = offsetsThere[-dir];
                        }
                    }
                    else
                    {
                        if (extremeBoundGroupsHere[dir] != null)
                        {
                            offsetHere = offsetsHere[dir];
                            offsetThere = offsetsThere[dir];
                        }
                        else
                        {
                            offsetHere = offsetsHere[-dir];
                            offsetThere = offsetsThere[dir];
                        }
                    }

                    Interval extent = extremeBounds[dir].Extent(commonThere, Axis.Y);
                    double coordThere = extent.Center;
                    y = coordThere - offsetThere + offsetHere;
                }

                if (extremeBoundGroupsHere[dir] != null)
                {
                    commonY = extremeBoundGroupsHere[dir];
                    if (yNeeded)
                    {
                        y -= offsetsHere[dir];
                    }
                }
                else
                {
                    commonY = commonHere;
                }
            }
            else
            {
                commonY = SidePositionInterface.GetVerticalAxisGroup(boundItem);
                if (commonY == null)
                {
                    Warn.ProgrammingError("bound item has no parent vertical axis group");
                    commonY = boundItem;
                }

                if (yNeeded)
                {
                    Interval ii = boundItem.Extent(commonY, Axis.Y);
                    if (!ii.IsEmpty)
                    {
                        y = ii.Center;
                    }
                }
            }

            if (yNeeded)
            {
                double extraDy = ToDouble(me.GetProperty(ExtraDySymbol), 0.0);
                y += (int)dir * extraDy / 2;
                details = new Pair(new Pair(YSymbol, y), details);
            }
        }

        details = new Pair(new Pair(CommonYSymbol, commonY), details);
        return details;
    }

    /// <summary>The <c>calc-right-bound-info</c> callback.</summary>
    /// <param name="me">The line spanner.</param>
    /// <returns>The bound-details alist for the right end.</returns>
    public static object CalcRightBoundInfo(Spanner me)
        => CalcBoundInfo(me, Direction.Positive, false);

    /// <summary>The <c>calc-left-bound-info</c> callback.</summary>
    /// <param name="me">The line spanner.</param>
    /// <returns>The bound-details alist for the left end.</returns>
    public static object CalcLeftBoundInfo(Spanner me)
        => CalcBoundInfo(me, Direction.Negative, false);

    /// <summary>The horizontal <c>calc-right-bound-info</c> callback.</summary>
    /// <param name="me">The line spanner.</param>
    /// <returns>The bound-details alist for the right end.</returns>
    public static object HorizontalCalcRightBoundInfo(Spanner me)
        => CalcBoundInfo(me, Direction.Positive, true);

    /// <summary>The horizontal <c>calc-left-bound-info</c> callback.</summary>
    /// <param name="me">The line spanner.</param>
    /// <returns>The bound-details alist for the left end.</returns>
    public static object HorizontalCalcLeftBoundInfo(Spanner me)
        => CalcBoundInfo(me, Direction.Negative, true);

    /// <summary>
    /// The left bound info, with the spanner's own <c>text</c> rendered into it when the
    /// details did not already carry a stencil.
    /// </summary>
    /// <param name="me">The line spanner.</param>
    /// <param name="horizontal">Whether this is a horizontal line spanner.</param>
    /// <returns>The bound-details alist for the left end.</returns>
    public static object CalcLeftBoundInfoAndText(Spanner me, bool horizontal)
    {
        object alist = CalcBoundInfo(me, Direction.Negative, horizontal);

        object text = me.GetProperty(TextSymbol);
        object checker = LilyPondScheme.LookupProcedure(UnbrokenOrFirstBrokenSpannerSymbol);
        if (TextInterface.IsMarkup(text)
            && SchemeUtilities.IsSchemeTrue(SchemeUtilities.CallCallback(checker, me))
            && !SchemeUtilities.IsSchemeTrue(
                SchemeUtilities.LyAssocGet(StencilSymbol, alist, false)))
        {
            alist = new Pair(
                new Pair(StencilSymbol, TextInterface.GrobInterpretMarkup(me, text)), alist);
        }

        return alist;
    }

    /// <summary>The <c>calc-left-bound-info-and-text</c> callback.</summary>
    /// <param name="me">The line spanner.</param>
    /// <returns>The bound-details alist for the left end.</returns>
    public static object CalcLeftBoundInfoAndText(Spanner me)
        => CalcLeftBoundInfoAndText(me, false);

    /// <summary>The horizontal <c>calc-left-bound-info-and-text</c> callback.</summary>
    /// <param name="me">The line spanner.</param>
    /// <returns>The bound-details alist for the left end.</returns>
    public static object HorizontalCalcLeftBoundInfoAndText(Spanner me)
        => CalcLeftBoundInfoAndText(me, true);

    // TODO: for horizontal line spanners, avoid looking at the
    // right bound, and never mark cross-staff.

    /// <summary>The <c>cross-staff</c> callback: the two ends sit on different staves.</summary>
    /// <param name="me">The line spanner.</param>
    /// <returns>Whether the line spans staves.</returns>
    public static bool CalcCrossStaff(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return false;
        }

        DrulArray<Item> bounds = spanner.GetBounds();
        return !ReferenceEquals(
            StaffSymbolReferencer.GetStaffSymbol(bounds[Direction.Negative]),
            StaffSymbolReferencer.GetStaffSymbol(bounds[Direction.Positive]));
    }

    /// <summary>Draws the line, with whatever decorations its two ends carry.</summary>
    /// <param name="me">The line spanner.</param>
    /// <returns>The stencil, or the empty list when the gaps swallow the line.</returns>
    public static object Print(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return Nil.Instance;
        }

        DrulArray<object> bounds = new DrulArray<object>(
            spanner.GetProperty(LeftBoundInfoSymbol),
            spanner.GetProperty(RightBoundInfoSymbol));

        Grob commonx = spanner.GetBound(Direction.Negative)
            .CommonRefpoint(spanner.GetBound(Direction.Positive), Axis.X);
        commonx = spanner.CommonRefpoint(commonx, Axis.X);

        DrulArray<Offset> spanPoints = new DrulArray<Offset>(Offset.Zero, Offset.Zero);

        foreach (Direction d in Both)
        {
            spanPoints[d] = new Offset(
                ToDouble(SchemeUtilities.LyAssocGet(XSymbol, bounds[d], false), 0.0),
                ToDouble(SchemeUtilities.LyAssocGet(YSymbol, bounds[d], false), 0.0));
        }

        DrulArray<double> gaps = new DrulArray<double>(0.0, 0.0);
        DrulArray<bool> arrows = new DrulArray<bool>(false, false);
        DrulArray<Stencil?> stencils = new DrulArray<Stencil?>(null, null);
        DrulArray<Grob> commonY = new DrulArray<Grob>(null, null);

        // For scaling of 'padding and 'stencil-offset
        double magstep = Math.Pow(2, ToDouble(spanner.GetProperty(FontSizeSymbol), 0.0) / 6);

        foreach (Direction d in Both)
        {
            gaps[d] = ToDouble(
                SchemeUtilities.LyAssocGet(PaddingSymbol, bounds[d], false), 0.0);
            arrows[d] = SchemeUtilities.ToBool(
                SchemeUtilities.LyAssocGet(ArrowSymbol, bounds[d], false));
            stencils[d]
                = SchemeUtilities.LyAssocGet(StencilSymbol, bounds[d], false) as Stencil?;
            commonY[d] = SchemeUtilities.LyAssocGet(CommonYSymbol, bounds[d], false) as Grob;
            if (commonY[d] == null)
            {
                Warn.ProgrammingError("no common-Y in bound details");
                commonY[d] = spanner;
            }
        }

        Grob myCommonY = commonY[Direction.Negative]
            .CommonRefpoint(commonY[Direction.Positive], Axis.Y);
        foreach (Direction d in Both)
        {
            spanPoints[d] = spanPoints[d].With(
                Axis.Y,
                spanPoints[d].Y + commonY[d].RelativeCoordinate(myCommonY, Axis.Y));
        }

        Interval normalizedEndpoints
            = Grob.TryNumberPair(spanner.GetProperty(NormalizedEndpointsSymbol), out Interval ne)
                ? ne
                : new Interval(0, 1);
        double yLength = spanPoints[Direction.Positive].Y - spanPoints[Direction.Negative].Y;

        spanPoints[Direction.Negative] = spanPoints[Direction.Negative].With(
            Axis.Y,
            spanPoints[Direction.Negative].Y + (normalizedEndpoints[Direction.Negative] * yLength));
        spanPoints[Direction.Positive] = spanPoints[Direction.Positive].With(
            Axis.Y,
            spanPoints[Direction.Positive].Y
            - ((1 - normalizedEndpoints[Direction.Positive]) * yLength));

        Offset dz = spanPoints[Direction.Positive] - spanPoints[Direction.Negative];
        Offset dzDir = dz.Direction();
        if (gaps[Direction.Negative] + gaps[Direction.Positive] > dz.Length)
        {
            return Nil.Instance;
        }

        Stencil line = Stencil.Empty;
        foreach (Direction d in Both)
        {
            spanPoints[d] += -(int)d * gaps[d] * magstep * dz.Direction();

            if (stencils[d].HasValue)
            {
                Stencil s = stencils[d].Value;
                object align = SchemeUtilities.LyAssocGet(
                    StencilAlignDirYSymbol, bounds[d], false);
                object off = SchemeUtilities.LyAssocGet(StencilOffsetSymbol, bounds[d], false);

                if (SchemeConvert.IsNumber(align))
                {
                    s.AlignTo(Axis.Y, ToDouble(align, 0.0));
                }

                if (Grob.TryNumberPair(off, out Interval offPair))
                {
                    s.Translate(new Offset(offPair.Left, offPair.Right) * magstep);
                }

                s.Translate(spanPoints[d]);

                line.AddStencil(s);
            }
        }

        foreach (Direction d in Both)
        {
            if (stencils[d].HasValue && !stencils[d].Value.IsEmpty)
            {
                spanPoints[d] += dzDir * (stencils[d].Value.Extent(Axis.X)[-d] / dzDir.X);
            }
        }

        Offset adjust = dz.Direction() * StaffSymbolReferencer.StaffSpace(spanner);
        Offset lineLeft = spanPoints[Direction.Negative]
                          + (arrows[Direction.Negative] ? adjust * 1.4 : Offset.Zero);
        Offset lineRight = spanPoints[Direction.Positive]
                           - (arrows[Direction.Positive] ? adjust * 0.55 : Offset.Zero);

        if (lineRight.X > lineLeft.X)
        {
            line.AddStencil(LineInterface.Line(spanner, lineLeft, lineRight));

            line.AddStencil(LineInterface.Arrows(
                spanner,
                spanPoints[Direction.Negative],
                spanPoints[Direction.Positive],
                arrows[Direction.Negative],
                arrows[Direction.Positive]));
        }

        line.Translate(new Offset(
            -spanner.RelativeCoordinate(commonx, Axis.X),
            -spanner.RelativeCoordinate(myCommonY, Axis.Y)));

        return line;
    }

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "line-spanner")
            : fallback;
}
