/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>
  Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/bracket.cc, lily/include/bracket.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port:
//   - PULLED FORWARD FROM EPG14 by EPG17's demand loop. Volta_bracket_interface::print
//     and Tuplet_bracket::print both draw through make_bracket, so neither of EPG17's
//     bracket grobs can exist without this file. It declares no Scheme callbacks at all
//     — it is pure static drawing helpers — so pulling it forward costs EPG14 nothing
//     and leaves its own grobs (HorizontalBracket, BassFigureBracket, ottava, piano
//     pedal) exactly where they were.

/// <summary>
/// Draws the bracket shapes: a spine with a flared, turned-down edge at each end.
/// </summary>
/// <remarks>
/// Upstream's own note, kept: this should probably move to <c>Lookup</c>, and it fails
/// for brackets shorter than their own flare.
/// </remarks>
public static class Bracket
{
    private static readonly Symbol BreakOvershootSymbol = Symbol.Intern("break-overshoot");
    private static readonly Symbol BracketFlareSymbol = Symbol.Intern("bracket-flare");
    private static readonly Symbol ConnectToNeighborSymbol = Symbol.Intern("connect-to-neighbor");
    private static readonly Symbol DashedEdgeSymbol = Symbol.Intern("dashed-edge");
    private static readonly Symbol DashedLineStyle = Symbol.Intern("dashed-line");
    private static readonly Symbol EdgeHeightSymbol = Symbol.Intern("edge-height");
    private static readonly Symbol LineStyle = Symbol.Intern("line");
    private static readonly Symbol ShortenPairSymbol = Symbol.Intern("shorten-pair");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");

    /// <summary>Builds a bracket along a vector, with a turned edge at each end.</summary>
    /// <param name="me">The grob, read for the line properties.</param>
    /// <param name="protrusionAxis">The axis the edges turn along.</param>
    /// <param name="dz">The vector from one end of the spine to the other.</param>
    /// <param name="height">How far each edge protrudes.</param>
    /// <param name="gap">A gap to leave in the middle of the spine; empty for none.</param>
    /// <param name="flare">How far each end flares outward.</param>
    /// <param name="shorten">How far to pull each end in.</param>
    /// <returns>The bracket stencil.</returns>
    public static Stencil MakeBracket(
        Grob me,
        Axis protrusionAxis,
        Offset dz,
        DrulArray<double> height,
        Interval gap,
        DrulArray<double> flare,
        DrulArray<double> shorten)
    {
        DrulArray<Offset> corners = new DrulArray<Offset>(new Offset(0, 0), dz);

        double length = dz.Length;
        DrulArray<Offset> gapCorners = new DrulArray<Offset>(Offset.Zero, Offset.Zero);

        Axis bracketAxis = OtherAxis(protrusionAxis);

        DrulArray<Offset> straightCorners = corners;

        // EPG17's zero-length guard is GONE (EPG15 close-out, 2026-08-08) and this is
        // upstream's own division again. It existed because the port drew brackets while
        // horizontal spacing was incomplete, so a spanner's two bounds could share a
        // coordinate, dz was (0, 0), and upstream's expression evaluated 0/0 -- the NaN
        // reaching the stencil's extent box and killing the file later in skyline
        // building with "slope is not finite" (volta-multi-staff-inner-staff.ly,
        // 2026-08-07). EPG15's line breaking places the columns before any stencil is
        // asked for, which is the condition upstream relies on.
        double inverseLength = 1.0 / length;

        foreach (Direction d in Directions)
        {
            straightCorners[d] += dz * (-(int)d * shorten[d] * inverseLength);
        }

        if (!gap.IsEmpty)
        {
            foreach (Direction d in Directions)
            {
                gapCorners[d] = (dz * 0.5) + (dz * (gap[d] * inverseLength));
            }
        }

        DrulArray<Offset> flareCorners = straightCorners;
        foreach (Direction d in Directions)
        {
            flareCorners[d] = WithAxis(
                flareCorners[d], bracketAxis, straightCorners[d][bracketAxis]);

            flareCorners[d] = WithAxis(
                flareCorners[d], protrusionAxis, flareCorners[d][protrusionAxis] + height[d]);

            straightCorners[d] = WithAxis(
                straightCorners[d],
                bracketAxis,
                straightCorners[d][bracketAxis] + (-(int)d * flare[d]));
        }

        Stencil m = new Stencil();
        if (!gap.IsEmpty)
        {
            foreach (Direction d in Directions)
            {
                m.AddStencil(LineInterface.Line(me, straightCorners[d], gapCorners[d]));
            }
        }
        else
        {
            m.AddStencil(LineInterface.Line(
                me, straightCorners[Direction.Negative], straightCorners[Direction.Positive]));
        }

        if (ReferenceEquals(me.GetProperty(StyleSymbol), DashedLineStyle)
            && !(me.GetProperty(DashedEdgeSymbol) is bool dashedEdge && dashedEdge))
        {
            me.SetProperty(StyleSymbol, LineStyle);
        }

        foreach (Direction d in Directions)
        {
            m.AddStencil(LineInterface.Line(me, straightCorners[d], flareCorners[d]));
        }

        return m;
    }

    /// <summary>
    /// Builds a bracket oriented along either axis. Passing an empty gap creates an
    /// unbroken bracket.
    /// </summary>
    /// <param name="me">The grob, read for edge height, flare, shortening and overshoot.</param>
    /// <param name="length">The length of the spine.</param>
    /// <param name="axis">The axis the spine runs along.</param>
    /// <param name="direction">Which way the edges turn.</param>
    /// <param name="gap">A gap to leave in the middle of the spine; empty for none.</param>
    /// <returns>The bracket stencil.</returns>
    public static Stencil MakeAxisConstrainedBracket(
        Grob me, double length, Axis axis, Direction direction, Interval gap)
    {
        DrulArray<double> edgeHeight = SchemeConvert.ToDrulDouble(
            me.GetProperty(EdgeHeightSymbol), new DrulArray<double>(1.0, 1.0));

        DrulArray<double> flare = SchemeConvert.ToDrulDouble(
            me.GetProperty(BracketFlareSymbol), new DrulArray<double>(0.0, 0.0));

        DrulArray<double> shorten = SchemeConvert.ToDrulDouble(
            me.GetProperty(ShortenPairSymbol), new DrulArray<double>(0.0, 0.0));

        DrulArray<double> overshoot = SchemeConvert.ToDrulDouble(
            me.GetProperty(BreakOvershootSymbol), new DrulArray<double>(0.0, 0.0));

        // Make sure that it points in the correct direction.
        ScaleDrul(ref edgeHeight, -(int)direction);

        Offset start = axis == Axis.X ? new Offset(length, 0) : new Offset(0, length);

        DrulArray<bool> connectToOther
            = SchemeConvert.ToDrulBool(me.GetProperty(ConnectToNeighborSymbol));

        foreach (Direction d in Directions)
        {
            if (connectToOther[d])
            {
                edgeHeight[d] = 0.0;
                flare[d] = 0.0;
                shorten[d] = -overshoot[d];
            }
        }

        return MakeBracket(me, OtherAxis(axis), start, edgeHeight, gap, flare, shorten);
    }

    /// <summary>
    /// Builds an axis-constrained, ungapped bracket enclosing a group of grobs. Used for
    /// analysis brackets and figured bass.
    /// </summary>
    /// <param name="me">The grob, read for the bracket's own properties.</param>
    /// <param name="refpoint">The grob the result is positioned relative to.</param>
    /// <param name="grobs">The grobs to enclose.</param>
    /// <param name="axis">The axis the bracket runs along.</param>
    /// <param name="direction">Which way the edges turn.</param>
    /// <returns>The bracket stencil, or an empty one when the group has no extent.</returns>
    public static Stencil MakeEnclosingBracket(
        Grob me, Grob refpoint, IReadOnlyList<Grob> grobs, Axis axis, Direction direction)
    {
        Grob common = AxisGroupInterface.CommonRefpointOfArray(grobs, refpoint, axis);
        Interval extent = AxisGroupInterface.RelativeGroupExtentOf(grobs, common, axis);

        if (extent.IsEmpty)
        {
            me.ProgrammingError("Can't enclose empty extents with bracket");
            return new Stencil();
        }

        Stencil b = MakeAxisConstrainedBracket(
            me, extent.Length, axis, direction, Interval.Empty);

        b.TranslateAxis(extent.Left - refpoint.RelativeCoordinate(common, axis), axis);
        return b;
    }

    // Upstream's misc.hh template, at the two instantiations this file needs.
    private static void ScaleDrul(ref DrulArray<double> drul, double factor)
    {
        drul[Direction.Negative] *= factor;
        drul[Direction.Positive] *= factor;
    }

    private static Axis OtherAxis(Axis axis) => axis == Axis.X ? Axis.Y : Axis.X;

    private static Offset WithAxis(Offset offset, Axis axis, double value)
        => axis == Axis.X ? new Offset(value, offset.Y) : new Offset(offset.X, value);

    private static Direction[] Directions { get; }
        = { Direction.Negative, Direction.Positive };
}
