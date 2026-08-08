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
using System.Globalization;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/hairpin.cc, lily/include/hairpin.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port.

/// <summary>A hairpin crescendo or decrescendo.</summary>
public static class Hairpin
{
    private static readonly Symbol HeightSymbol = Symbol.Intern("height");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol BoundPaddingSymbol = Symbol.Intern("bound-padding");
    private static readonly Symbol BrokenBoundPaddingSymbol
        = Symbol.Intern("broken-bound-padding");
    private static readonly Symbol GrowDirectionSymbol = Symbol.Intern("grow-direction");
    private static readonly Symbol CircledTipSymbol = Symbol.Intern("circled-tip");
    private static readonly Symbol ShortenPairSymbol = Symbol.Intern("shorten-pair");
    private static readonly Symbol EndpointAlignmentsSymbol
        = Symbol.Intern("endpoint-alignments");
    private static readonly Symbol ConcurrentHairpinsSymbol
        = Symbol.Intern("concurrent-hairpins");
    private static readonly Symbol AdjacentSpannersSymbol = Symbol.Intern("adjacent-spanners");
    private static readonly Symbol AfterLineBreakingSymbol = Symbol.Intern("after-line-breaking");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol HasSpanBarSymbol = Symbol.Intern("has-span-bar");
    private static readonly Symbol BarLineInterface = Symbol.Intern("bar-line-interface");
    private static readonly Symbol TextInterfaceSymbol = Symbol.Intern("text-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol HairpinInterface = Symbol.Intern("hairpin-interface");
    private static readonly Symbol CircleSymbol = Symbol.Intern("circle");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    /// <summary>
    /// The <c>pure-height</c> callback: how tall a hairpin is, before anything is drawn.
    /// </summary>
    /// <param name="me">The hairpin.</param>
    /// <param name="start">The starting column rank (ignored, as upstream).</param>
    /// <param name="end">The ending column rank (ignored, as upstream).</param>
    /// <returns>The vertical extent.</returns>
    public static Interval PureHeight(Grob me, int start, int end)
    {
        double height = ToDouble(me.GetProperty(HeightSymbol), 0.0)
                        * StaffSymbolReferencer.StaffSpace(me);

        double thickness = ToDouble(me.GetProperty(ThicknessSymbol), 1)
                           * StaffSymbolReferencer.LineThickness(me);

        height += thickness / 2;
        return new Interval(-height, height);
    }

    /// <summary>
    /// The <c>broken-bound-padding</c> callback: how far a hairpin broken across a system
    /// break must stay clear of the span bar it runs into.
    /// </summary>
    /// <param name="me">The hairpin.</param>
    /// <returns>The padding.</returns>
    public static double BrokenBoundPadding(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return 0.0;
        }

        Item rBound = spanner.GetBound(Direction.Positive);
        if (rBound.BreakStatusDirection() != Direction.Negative)
        {
            spanner.Warning("Asking for broken bound padding at a non-broken bound.");
            return 0.0;
        }

        SystemGrob sys = spanner.GetSystem();
        Direction dir = DirectionalElementInterface.GetGrobDirection(spanner.YParent);
        if (dir == Direction.Center)
        {
            return 0.0;
        }

        Grob myVerticalAxisGroup = SidePositionInterface.GetVerticalAxisGroup(spanner);
        DrulArray<Grob> verticalAxisGroups = new DrulArray<Grob>(null, null);
        foreach (Direction d in Both)
        {
            verticalAxisGroups[d] = d == dir
                ? SystemGrobVertical.GetNeighboringStaff(
                    sys, d, myVerticalAxisGroup, spanner.SpannedColumnRankInterval())
                : myVerticalAxisGroup;
        }

        if (verticalAxisGroups[dir] == null)
        {
            return 0.0;
        }

        DrulArray<Grob> spanBars = new DrulArray<Grob>(null, null);
        foreach (Direction d in Both)
        {
            IReadOnlyList<Grob> elts
                = PointerGroupInterface.ExtractGrobSet(verticalAxisGroups[d], ElementsSymbol);
            for (int i = elts.Count; i-- > 0;)
            {
                if (elts[i].HasInterface(BarLineInterface)
                    && elts[i] is Item barItem
                    && barItem.BreakStatusDirection() == Direction.Negative)
                {
                    object hsb = elts[i].GetObject(HasSpanBarSymbol);
                    if (!(hsb is Pair hsbPair))
                    {
                        break;
                    }

                    spanBars[d]
                        = (d == Direction.Positive ? hsbPair.Car : hsbPair.Cdr) as Grob;
                    break;
                }
            }

            if (spanBars[d] == null)
            {
                return 0.0;
            }
        }

        if (!ReferenceEquals(spanBars[Direction.Negative], spanBars[Direction.Positive]))
        {
            return 0.0;
        }

        return ToDouble(me.GetProperty(BoundPaddingSymbol), 0.5) / 2.0;
    }

    /// <summary>Draws the hairpin.</summary>
    /// <param name="me">The hairpin.</param>
    /// <returns>The stencil, or the empty list when it has no direction to grow in.</returns>
    public static object Print(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return Nil.Instance;
        }

        object s = spanner.GetProperty(GrowDirectionSymbol);
        if (!DirectionalElementInterface.IsDirection(s))
        {
            spanner.Suicide();
            return Nil.Instance;
        }

        Direction growDir = DirectionalElementInterface.FromScheme(s, Direction.Center);
        double padding = ToDouble(spanner.GetProperty(BoundPaddingSymbol), 0.5);

        DrulArray<Item> bounds = spanner.GetBounds();
        DrulArray<bool> broken = new DrulArray<bool>(false, false);
        foreach (Direction d in Both)
        {
            broken[d] = bounds[d].BreakStatusDirection() != Direction.Center;
        }

        if (broken[Direction.Positive])
        {
            Spanner next = spanner.BrokenNeighbor(Direction.Positive);

            // Hairpin-parts suicide in after-line-breaking if they need not be drawn
            if (next != null)
            {
                _ = next.GetProperty(AfterLineBreakingSymbol);
                broken[Direction.Positive] = next.IsLive;
            }
            else
            {
                broken[Direction.Positive] = false;
            }
        }

        Grob common = bounds[Direction.Negative]
            .CommonRefpoint(bounds[Direction.Positive], Axis.X);
        DrulArray<double> xPoints = new DrulArray<double>(0.0, 0.0);

        /*
          Use the height and thickness of the hairpin when making a circled tip
        */
        bool circledTip = SchemeUtilities.IsSchemeTrue(spanner.GetProperty(CircledTipSymbol));
        double height = ToDouble(spanner.GetProperty(HeightSymbol), 0.2)
                        * StaffSymbolReferencer.StaffSpace(spanner);

        /*
          FIXME: 0.525 is still just a guess...
                 same method is used in `circle-radius' of scm/output-lib.scm
        */
        double rad = height * 0.525;
        double thick = 1.0;
        if (circledTip)
        {
            thick = ToDouble(spanner.GetProperty(ThicknessSymbol), 1.0)
                    * StaffSymbolReferencer.LineThickness(spanner);
        }

        DrulArray<double> shorten = ToDrul(spanner.GetProperty(ShortenPairSymbol), 0.0, 0.0);

        DrulArray<double> endpointAlignments
            = ToDrul(spanner.GetProperty(EndpointAlignmentsSymbol), -1.0, 1.0);

        foreach (Direction d in Both)
        {
            double sanitizedAlignment = Math.Sign(endpointAlignments[d]);
            if (endpointAlignments[d] != sanitizedAlignment)
            {
                spanner.Warning(string.Format(
                    CultureInfo.InvariantCulture,
                    "hairpin: '{0:F6}' is not a valid direction for property"
                    + " 'endpoint-alignments', setting to '{1}'",
                    endpointAlignments[d],
                    (int)sanitizedAlignment));
                endpointAlignments[d] = sanitizedAlignment;
            }
        }

        foreach (Direction d in Both)
        {
            Item b = bounds[d];
            Interval e = AxisGroupInterfaceVertical.GenericBoundExtent(b, common, Axis.X);

            xPoints[d] = b.RelativeCoordinate(common, Axis.X);
            if (broken[d])
            {
                if (d == Direction.Negative)
                {
                    xPoints[d] = e[-d] + padding;
                }
                else
                {
                    double brokenBoundPadding
                        = ToDouble(spanner.GetProperty(BrokenBoundPaddingSymbol), 0.0);
                    IReadOnlyList<Grob> chp = PointerGroupInterface.ExtractGrobSet(
                        spanner, ConcurrentHairpinsSymbol);
                    for (int i = 0; i < chp.Count; i++)
                    {
                        if (chp[i] is Spanner spanElt
                            && spanElt.GetBound(Direction.Positive).BreakStatusDirection()
                                == Direction.Negative)
                        {
                            brokenBoundPadding = Math.Max(
                                brokenBoundPadding,
                                ToDouble(
                                    spanElt.GetProperty(BrokenBoundPaddingSymbol), 0.0));
                        }
                    }

                    xPoints[d] -= (int)d * brokenBoundPadding;
                }
            }
            else
            {
                if (b.HasInterface(TextInterfaceSymbol))
                {
                    if (!e.IsEmpty)
                    {
                        xPoints[d] = e[-d] - ((int)d * padding);
                    }
                }
                else
                {
                    bool neighborFound = false;
                    Spanner adjacent = null;
                    IReadOnlyList<Grob> neighbors
                        = PointerGroupInterface.ExtractGrobSet(spanner, AdjacentSpannersSymbol);
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        /*
                          FIXME: this will fuck up in case of polyphonic
                          notes in other voices. Need to look at note-columns
                          in the current staff/voice.
                        */
                        adjacent = neighbors[i] as Spanner;
                        if (adjacent != null
                            && ReferenceEquals(
                                adjacent.GetBound(-d).GetColumn(), b.GetColumn()))
                        {
                            neighborFound = true;
                            break;
                        }
                    }

                    if (neighborFound)
                    {
                        if (adjacent.HasInterface(HairpinInterface))
                        {
                            /*
                              Handle back-to-back hairpins with a circle in the middle
                            */
                            if (circledTip && growDir != d)
                            {
                                xPoints[d] = e.Center + ((int)d * (rad - (thick / 2.0)));
                                shorten[d] = 0.0;
                            }

                            /*
                              If we're hung on a paper column, that means we're not
                              adjacent to a text-dynamic, and we may move closer. We
                              make the padding a little smaller, here.
                            */
                            else
                            {
                                xPoints[d] = e.Center - ((int)d * padding / 3);
                            }
                        }

                        // Our neighbor is a dynamic text spanner.
                        // If we end on the text, pad as for text dynamics
                        else if (d == Direction.Positive)
                        {
                            xPoints[d] = e[-d] - ((int)d * padding);
                        }
                    }
                    else
                    {
                        if (d == Direction.Positive // end at the left edge of a rest
                            && b.HasInterface(NoteColumnInterface)
                            && NoteColumn.HasRests(b))
                        {
                            xPoints[d] = e[-d];
                        }
                        else
                        {
                            // Endpoint alignment relative to NoteColumn
                            if (endpointAlignments[d] == 0.0)
                            {
                                xPoints[d] = e.Center;
                            }
                            else if (endpointAlignments[d] != (int)d)
                            {
                                xPoints[d] = e[-d];
                            }
                            else
                            {
                                xPoints[d] = e[d];
                            }
                        }

                        if (Item.IsNonMusical(b))
                        {
                            xPoints[d] -= (int)d * padding;
                        }
                    }
                }
            }

            xPoints[d] -= shorten[d] * (int)d;
        }

        double width = xPoints[Direction.Positive] - xPoints[Direction.Negative];

        if (width < 0)
        {
            spanner.Warning(
                growDir == Direction.Negative ? "decrescendo too small" : "crescendo too small");
            width = 0;
        }

        bool continued = broken[-growDir];
        bool continuing = broken[growDir];

        double starth;
        double endh;
        if (growDir == Direction.Negative)
        {
            starth = continuing ? 2 * height / 3 : height;
            endh = continued ? height / 3 : 0.0;
        }
        else
        {
            starth = continued ? height / 3 : 0.0;
            endh = continuing ? 2 * height / 3 : height;
        }

        /*
          should do relative to staff-symbol staff-space?
        */
        double x = 0.0;

        /*
          Compensate for size of circle
        */
        Direction tipDir = -growDir;
        if (circledTip && !broken[tipDir])
        {
            if (growDir == Direction.Positive)
            {
                x = rad * 2.0;
            }
            else if (growDir == Direction.Negative)
            {
                width -= rad * 2.0;
            }
        }

        Stencil mol = LineInterface.Line(
            spanner, new Offset(x, starth), new Offset(width, endh));
        mol.AddStencil(LineInterface.Line(
            spanner, new Offset(x, -starth), new Offset(width, -endh)));

        /*
          Support al/del niente notation by putting a circle at the
          tip of the (de)crescendo.
        */
        if (circledTip)
        {
            Box extent = new Box(new Interval(-rad, rad), new Interval(-rad, rad));

            /* Hmmm, perhaps we should have a Lookup::circle () method? */
            Stencil circle = new Stencil(
                extent, Pair.List(CircleSymbol, rad, thick, false));

            /*
              don't add another circle if the hairpin is broken
            */
            if (!broken[tipDir])
            {
                mol.AddAtEdge(Axis.X, tipDir, circle, 0);
            }
        }

        mol.TranslateAxis(
            xPoints[Direction.Negative]
            - bounds[Direction.Negative].RelativeCoordinate(common, Axis.X),
            Axis.X);
        return mol;
    }

    private static DrulArray<double> ToDrul(object value, double left, double right)
        => Grob.TryNumberPair(value, out Interval pair)
            ? new DrulArray<double>(pair.Left, pair.Right)
            : new DrulArray<double>(left, right);

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "hairpin")
            : fallback;
}
