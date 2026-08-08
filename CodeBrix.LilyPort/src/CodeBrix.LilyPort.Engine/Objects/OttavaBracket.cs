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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/ottava-bracket.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port.

/*
  TODO: the string for ottava shoudl depend on the available space, ie.

  Long: 15ma        Short: 15ma    Empty: 15
  8va                8va            8
  8va bassa          8ba            8
*/

/// <summary>An ottava bracket.</summary>
public static class OttavaBracket
{
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol ShortenPairSymbol = Symbol.Intern("shorten-pair");
    private static readonly Symbol MinimumLengthSymbol = Symbol.Intern("minimum-length");
    private static readonly Symbol EdgeHeightSymbol = Symbol.Intern("edge-height");
    private static readonly Symbol BracketFlareSymbol = Symbol.Intern("bracket-flare");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    /// <summary>Draws the bracket, with its "8va"-style text at the left.</summary>
    /// <param name="me">The ottava bracket.</param>
    /// <returns>The stencil.</returns>
    public static object Print(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return Nil.Instance;
        }

        Interval spanPoints = Interval.Empty;

        DrulArray<Item> bounds = spanner.GetBounds();
        Grob common = bounds[Direction.Negative]
            .CommonRefpoint(bounds[Direction.Positive], Axis.X);

        DrulArray<bool> broken = new DrulArray<bool>(false, false);
        foreach (Direction d in Both)
        {
            Item b = bounds[d];
            broken[d] = b.BreakStatusDirection() != Direction.Center;

            if (b.HasInterface(NoteColumnInterface))
            {
                IReadOnlyList<Grob> heads
                    = PointerGroupInterface.ExtractGrobSet(b, NoteHeadsSymbol);
                common = AxisGroupInterface.CommonRefpointOfArray(heads, common, Axis.X);
                for (int i = 0; i < heads.Count; i++)
                {
                    Grob dots = RhythmicHead.GetDots(heads[i]);
                    if (dots != null)
                    {
                        common = dots.CommonRefpoint(common, Axis.X);
                    }
                }
            }
        }

        object markup = spanner.GetProperty(TextSymbol);
        Stencil text = Stencil.Empty;
        if (TextInterface.IsMarkup(markup))
        {
            text = TextInterface.GrobInterpretMarkup(spanner, markup);
        }

        DrulArray<double> shorten = ToDrul(spanner.GetProperty(ShortenPairSymbol), 0.0, 0.0);

        /*
          TODO: we should check if there are ledgers, and modify length of
          the spanner to that.
        */
        foreach (Direction d in Both)
        {
            Item b = bounds[d];

            Interval ext = Interval.Empty;
            if (b.HasInterface(NoteColumnInterface))
            {
                IReadOnlyList<Grob> heads
                    = PointerGroupInterface.ExtractGrobSet(b, NoteHeadsSymbol);
                for (int i = 0; i < heads.Count; i++)
                {
                    Grob h = heads[i];
                    ext.Unite(h.Extent(common, Axis.X));
                    Grob dots = RhythmicHead.GetDots(h);

                    if (dots != null && d == Direction.Positive)
                    {
                        ext.Unite(dots.Extent(common, Axis.X));
                    }
                }
            }

            if (ext.IsEmpty)
            {
                ext = LooseColumns.RobustRelativeExtent(b, common, Axis.X);
            }

            if (broken[d])
            {
                spanPoints[d] = AxisGroupInterfaceVertical
                    .GenericBoundExtent(b, common, Axis.X)[Direction.Positive];
                shorten[d] = 0.0;
            }
            else
            {
                spanPoints[d] = ext[d];
            }

            spanPoints[d] -= (int)d * shorten[d];
        }

        /*
          0.3 is ~ italic correction.
        */
        double textSize = text.Extent(Axis.X).IsEmpty
            ? 0.0
            : text.Extent(Axis.X)[Direction.Positive] + 0.3;

        spanPoints[Direction.Negative] = Math.Min(
            spanPoints[Direction.Negative],
            spanPoints[Direction.Positive]
            - textSize
            - ToDouble(spanner.GetProperty(MinimumLengthSymbol), -1.0));

        Interval bracketSpanPoints = spanPoints;
        bracketSpanPoints[Direction.Negative] += textSize;

        DrulArray<double> edgeHeight = ToDrul(spanner.GetProperty(EdgeHeightSymbol), 1.0, 1.0);
        DrulArray<double> flare = ToDrul(spanner.GetProperty(BracketFlareSymbol), 0.0, 0.0);

        foreach (Direction d in Both)
        {
            edgeHeight[d] *= -(int)DirectionalElementInterface.GetGrobDirection(spanner);
            if (broken[d])
            {
                edgeHeight[d] = 0.0;
            }
        }

        Stencil b2 = Stencil.Empty;
        Interval empty = Interval.Empty;

        if (!bracketSpanPoints.IsEmpty && bracketSpanPoints.Length > 0.001)
        {
            b2 = Bracket.MakeBracket(
                spanner,
                Axis.Y,
                new Offset(bracketSpanPoints.Length, 0),
                edgeHeight,
                empty,
                flare,
                new DrulArray<double>(0.0, 0.0));
        }

        /*
         * The vertical lines should not take space, for the following scenario:
         *
         * 8 -----+
         *     o  |
         *    |
         *    |
         *
         * Just a small amount, yes.  In tight situations, it is even
         * possible to center the `8' directly below the note, dropping the
         * ottava line completely...
        */

        b2 = new Stencil(
            new Box(b2.Extent(Axis.X), new Interval(0.1, 0.1)), b2.Expression);

        b2.TranslateAxis(bracketSpanPoints[Direction.Negative], Axis.X);
        text.TranslateAxis(spanPoints[Direction.Negative], Axis.X);
        text.AlignTo(Axis.Y, 0.0);
        b2.AddStencil(text);

        b2.TranslateAxis(-spanner.RelativeCoordinate(common, Axis.X), Axis.X);

        return b2;
    }

    private static DrulArray<double> ToDrul(object value, double left, double right)
        => Grob.TryNumberPair(value, out Interval pair)
            ? new DrulArray<double>(pair.Left, pair.Right)
            : new DrulArray<double>(left, right);

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "ottava-bracket")
            : fallback;
}
