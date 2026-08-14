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

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/measure-grouping-spanner.cc, lily/include/measure-grouping-spanner.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// An object indicating groups of beats. Valid choices for <c>style</c> are
/// <c>bracket</c> and <c>triangle</c>.
/// </summary>
public static class MeasureGrouping
{
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol HeightSymbol = Symbol.Intern("height");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol BracketSymbol = Symbol.Intern("bracket");
    private static readonly Symbol TriangleSymbol = Symbol.Intern("triangle");

    /// <summary>
    /// The <c>stencil</c> callback: a bracket or triangle from the spanner's left
    /// bound to the center of its right bound.
    /// </summary>
    /// <param name="grob">The measure grouping spanner.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Print(Spanner grob)
    {
        object which = grob.GetProperty(StyleSymbol);
        double height = TranslatorSchemeHelpers.ToDouble(grob.GetProperty(HeightSymbol), 1.0);

        double t = StaffSymbolReferencer.LineThickness(grob)
            * TranslatorSchemeHelpers.ToDouble(grob.GetProperty(ThicknessSymbol), 1.0);

        Item lb = grob.GetBound(Direction.Negative);
        Item rb = grob.GetBound(Direction.Positive);
        Grob common = lb.CommonRefpoint(rb, Axis.X);

        double rightPoint = LooseColumns.RobustRelativeExtent(rb, common, Axis.X).Center;
        double leftPoint = lb.RelativeCoordinate(common, Axis.X);

        Interval iv = new Interval(leftPoint, rightPoint);
        Stencil m = default;

        /*
          TODO: use line interface
        */
        if (ReferenceEquals(which, BracketSymbol))
        {
            m = Lookup.Bracket(Axis.X, iv, t, -height, t);
        }
        else if (ReferenceEquals(which, TriangleSymbol))
        {
            m = Lookup.Triangle(iv, t, height);
        }

        m.AlignTo(Axis.Y, -1.0);
        m.TranslateAxis(-grob.RelativeCoordinate(common, Axis.X), Axis.X);
        return m;
    }
}
