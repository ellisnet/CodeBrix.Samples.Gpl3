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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/horizontal-bracket.cc, lily/enclosing-bracket.cc, lily/include/horizontal-bracket.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - the two bracket grobs share a file; both are thin callers of Bracket, which EPG17
//     already pulled forward.

/// <summary>A horizontal bracket encompassing notes.</summary>
public static class HorizontalBracket
{
    private static readonly Symbol ColumnsSymbol = Symbol.Intern("columns");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    /// <summary>Draws the bracket around the columns it collected.</summary>
    /// <param name="me">The bracket.</param>
    /// <returns>The stencil, or the empty list when it encompasses nothing.</returns>
    public static object Print(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return Nil.Instance;
        }

        IReadOnlyList<Grob> gs = PointerGroupInterface.ExtractGrobSet(spanner, ColumnsSymbol);
        List<Grob> enclosed = new List<Grob>(gs);
        if (gs.Count == 0)
        {
            spanner.Suicide();
            return Nil.Instance;
        }

        foreach (Direction d in Both)
        {
            Item b = spanner.GetBound(d);
            if (b.BreakStatusDirection() != Direction.Center)
            {
                enclosed.Add(b);
            }
        }

        return Bracket.MakeEnclosingBracket(
            spanner, spanner, enclosed, Axis.X,
            DirectionalElementInterface.GetGrobDirection(spanner));
    }
}

/// <summary>Brackets alongside bass figures.</summary>
public static class EnclosingBracket
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");

    /* ugh: should make bracket interface. */

    /// <summary>The <c>width</c> callback: how wide the pair of brackets is.</summary>
    /// <param name="me">The bracket grob.</param>
    /// <returns>The horizontal extent, or the empty list when there is nothing to enclose.</returns>
    public static object Width(Grob me)
    {
        /*
           UGH. cut & paste code.
        */
        IReadOnlyList<Grob> elements
            = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        if (elements.Count == 0)
        {
            me.Suicide();
            return Nil.Instance;
        }

        Grob commonX = AxisGroupInterface.CommonRefpointOfArray(elements, me, Axis.X);
        Interval xext = AxisGroupInterfaceVertical.RelativeMaybeBoundGroupExtent(
            elements, commonX, Axis.X, false);

        Stencil leftBr = Bracket.MakeAxisConstrainedBracket(
            me, 10.0, Axis.Y, Direction.Negative, Interval.Empty);
        Stencil rightBr = Bracket.MakeAxisConstrainedBracket(
            me, 10.0, Axis.Y, Direction.Negative, Interval.Empty);

        xext.Widen(ToDouble(me.GetProperty(PaddingSymbol), 0.25));
        leftBr.TranslateAxis(xext[Direction.Negative], Axis.X);
        rightBr.TranslateAxis(xext[Direction.Positive], Axis.X);
        leftBr.AddStencil(rightBr);
        leftBr.TranslateAxis(-me.RelativeCoordinate(commonX, Axis.X), Axis.X);

        Interval result = leftBr.Extent(Axis.X);
        return new Pair(result.Left, result.Right);
    }

    /// <summary>Draws the pair of brackets.</summary>
    /// <param name="me">The bracket grob.</param>
    /// <returns>The stencil, or the empty list when there is nothing to enclose.</returns>
    public static object Print(Grob me)
    {
        IReadOnlyList<Grob> elements
            = PointerGroupInterface.ExtractGrobSet(me, ElementsSymbol);
        if (elements.Count == 0)
        {
            me.Suicide();
            return Nil.Instance;
        }

        Grob commonX = AxisGroupInterface.CommonRefpointOfArray(elements, me, Axis.X);
        Interval xext = AxisGroupInterfaceVertical.RelativeMaybeBoundGroupExtent(
            elements, commonX, Axis.X, false);
        if (xext.IsEmpty)
        {
            me.ProgrammingError("elements have no X extent.");
            xext = new Interval(0, 0);
        }

        Stencil leftBr = Bracket.MakeEnclosingBracket(
            me, me, elements, Axis.Y, Direction.Negative);
        Stencil rightBr = Bracket.MakeEnclosingBracket(
            me, me, elements, Axis.Y, Direction.Positive);

        xext.Widen(ToDouble(me.GetProperty(PaddingSymbol), 0.25));
        leftBr.TranslateAxis(xext[Direction.Negative], Axis.X);
        rightBr.TranslateAxis(xext[Direction.Positive], Axis.X);
        leftBr.AddStencil(rightBr);
        leftBr.TranslateAxis(-me.RelativeCoordinate(commonX, Axis.X), Axis.X);

        return leftBr;
    }

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "enclosing-bracket")
            : fallback;
}
