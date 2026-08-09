/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2013--2026 Aleksandr Andreev <aleksandr.andreev@gmail.com>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/kievan-ligature-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-09 as part of the CodeBrix port:
//   - fold_up_primitives returns the minimum length instead of writing through a Real&,
//     which is what its single caller does with it.

/// <summary>
/// Glues Kievan heads together into a melisma: the heads keep a fixed small distance
/// rather than fusing, and the ligature owns the spacing rod that holds them there.
/// </summary>
/// <remarks>
/// Kievan is the one ligature style whose accidentals stay WITH their heads — a B-flat
/// may be part of the ligature itself — which is why the head widths here have to make
/// room for them as they go.
/// </remarks>
public sealed class KievanLigatureEngraver : CoherentLigatureEngraver
{
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol DotStencilSymbol = Symbol.Intern("dot-stencil");
    private static readonly Symbol MinimumLengthSymbol = Symbol.Intern("minimum-length");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public KievanLigatureEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Kievan_ligature_engraver";

    /// <summary>Makes the <c>KievanLigature</c> spanner.</summary>
    /// <returns>The ligature spanner.</returns>
    protected override Spanner CreateLigatureSpanner() => MakeSpanner("KievanLigature", Nil.Instance);

    /// <summary>Lines the heads up and claims the horizontal room the result needs.</summary>
    /// <param name="ligature">The ligature spanner.</param>
    /// <param name="primitives">The heads, in time order.</param>
    protected override void BuildLigature(Spanner ligature, IReadOnlyList<Item> primitives)
    {
        double padding = SchemeConvert.ToDouble(ligature.GetProperty(PaddingSymbol), 0.0);
        double minLength = FoldUpPrimitives(primitives, padding);
        if (SchemeConvert.ToDouble(ligature.GetProperty(MinimumLengthSymbol), 0.0) < minLength)
        {
            ligature.SetProperty(MinimumLengthSymbol, minLength);
        }
    }

    private static double FoldUpPrimitives(IReadOnlyList<Item> primitives, double padding)
    {
        Item first = null;
        double accumulAccSpace = 0.0;

        // start us off with some padding on the left
        double minLength = padding;

        for (int i = 0; i < primitives.Count; i++)
        {
            Item current = primitives[i];
            Interval myExt = current.Extent(current, Axis.X);
            double headWidth = myExt.Length;
            if (i == 0)
            {
                first = current;
            }

            // must keep track of accidentals in spacing problem
            if (current.GetObject(AccidentalGrobSymbol) is Grob accGrob && i > 0)
            {
                Interval accExt = accGrob.Extent(accGrob, Axis.X);
                accumulAccSpace += accExt.Length;
            }

            MoveRelatedItemsToColumn(current, first.GetColumn(), minLength);

            // check if we have any dots
            if (RhythmicHead.DotCount(current) != 0)
            {
                Grob dotGrob = RhythmicHead.GetDots(current);

                /*
                  This is ugly and should probably be handled by configuring
                  the DotColumn appropriately.  Note that these dots will
                  be disconnected from their dot column.  See
                  MoveRelatedItemsToColumn.

                  This also means the padding isn't configurable as DotColumn.padding is.
                */
                double dotWidth = dotGrob.GetProperty(DotStencilSymbol) is Stencil stil
                    ? stil.Extent(Axis.X).Length
                    : 0.0;
                headWidth += dotWidth - (0.5 * (padding - accumulAccSpace));

                dotGrob.TranslateAxis(
                    (0.5 * (padding - accumulAccSpace)) + dotWidth, Axis.X);
            }

            // add more padding if we have an accidental coming up
            if (i < primitives.Count - 1)
            {
                Item next = primitives[i + 1];
                if (next.GetObject(AccidentalGrobSymbol) is Grob nextAccGrob)
                {
                    Interval accExt = nextAccGrob.Extent(nextAccGrob, Axis.X);
                    padding += accExt.Length;
                }
            }

            minLength += headWidth + padding - accumulAccSpace;
        }

        return minLength;
    }
}
