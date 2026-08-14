/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2019--2026 David Nalesnik <david.nalesnik@gmail.com>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/measure-spanner.cc, lily/include/measure-spanner.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A bracket aligned to a measure or measures.
/// <para>
/// COMPLETE. The bracket half stood as a loud throw naming
/// <c>Bracket::make_axis_constrained_bracket</c> as owed elsewhere; the volta/tuplet group landed
/// lily/bracket.cc WHOLE — including that function — while pulling it forward for its
/// own two bracket grobs, and the throw was never taken back down. Nothing demanded it
/// in between, because a toplevel <c>\layout</c> block's <c>\consists</c> was being
/// discarded and <c>Measure_spanner_engraver</c> therefore never ran.
/// </para>
/// </summary>
public static class MeasureSpanner
{
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol BracketVisibilitySymbol
        = Symbol.Intern("bracket-visibility");

    private static readonly Symbol SpacingPairSymbol = Symbol.Intern("spacing-pair");
    private static readonly Symbol StaffBarSymbol = Symbol.Intern("staff-bar");

    /// <summary>
    /// The <c>stencil</c> callback: a bracket between the break-align points of the
    /// spanner's bounds, with an optional centered text in a gap.
    /// </summary>
    /// <param name="grob">The measure spanner.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Print(Spanner grob)
    {
        Stencil mol = default;
        Stencil brack = default;
        object txt = grob.GetProperty(TextSymbol);

        object visible = grob.GetProperty(BracketVisibilitySymbol);

        Item leftBound = grob.GetBound(Direction.Negative);
        Item rightBound = grob.GetBound(Direction.Positive);

        /* should store note columns in engraver? */
        Grob commonX = leftBound.CommonRefpoint(rightBound, Axis.X);
        double leftPoint;
        double rightPoint;

        object sp = grob.GetProperty(SpacingPairSymbol);

        {
            object alignSyms = sp is Pair leftPair ? leftPair.Car : StaffBarSymbol;
            leftPoint = PaperColumn
                .BreakAlignWidth(leftBound, alignSyms)[Direction.Positive];

            alignSyms = sp is Pair rightPair ? rightPair.Cdr : StaffBarSymbol;
            rightPoint = PaperColumn
                .BreakAlignWidth(rightBound, alignSyms)[Direction.Negative];
        }

        Stencil bracketText = default;
        Interval gapInterval = Interval.Empty;

        if (TextInterface.IsMarkup(txt))
        {
            bracketText = TextInterface.GrobInterpretMarkup(grob, txt);
            bracketText.AlignTo(Axis.X, 0.0);
            Interval stilYExt = bracketText.Extent(Axis.Y);
            bracketText.TranslateAxis((rightPoint - leftPoint) / 2.0, Axis.X);
            bracketText.TranslateAxis(-stilYExt.Right / 2.0, Axis.Y);
            double gap = bracketText.Extent(Axis.X).Length;
            gapInterval = new Interval(-0.5 * gap, 0.5 * gap);
            gapInterval.Widen(0.6);
        }

        if (SchemeUtilities.IsSchemeTrue(visible))
        {
            brack = Bracket.MakeAxisConstrainedBracket(
                grob,
                rightPoint - leftPoint,
                Axis.X,
                DirectionalElementInterface.GetGrobDirection(grob),
                gapInterval);
        }

        if (!bracketText.IsEmpty)
        {
            brack.AddStencil(bracketText);
        }

        mol.AddStencil(brack);

        double meCoord = grob.RelativeCoordinate(commonX, Axis.X);

        mol.TranslateAxis(leftPoint - meCoord, Axis.X);

        return mol;
    }
}
