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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/piano-pedal-bracket.cc, lily/sustain-pedal.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the bracket and the sustain glyph share a file; both are single-callback structs.

/// <summary>
/// The bracket of the piano pedal. It can be tuned through the regular bracket
/// properties.
/// </summary>
public static class PianoPedalBracket
{
    private static readonly Symbol EdgeHeightSymbol = Symbol.Intern("edge-height");
    private static readonly Symbol ShortenPairSymbol = Symbol.Intern("shorten-pair");
    private static readonly Symbol BracketFlareSymbol = Symbol.Intern("bracket-flare");
    private static readonly Symbol PedalTextSymbol = Symbol.Intern("pedal-text");
    private static readonly Symbol BoundPaddingSymbol = Symbol.Intern("bound-padding");

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };

    /// <summary>Draws the pedal bracket.</summary>
    /// <param name="me">The bracket.</param>
    /// <returns>The stencil.</returns>
    public static object Print(Grob me)
    {
        if (!(me is Spanner spanner))
        {
            return Nil.Instance;
        }

        Spanner orig = spanner.Original;
        DrulArray<double> height = ToDrul(spanner.GetProperty(EdgeHeightSymbol), 0.0, 0.0);
        DrulArray<double> shorten = ToDrul(spanner.GetProperty(ShortenPairSymbol), 0.0, 0.0);
        DrulArray<double> flare = ToDrul(spanner.GetProperty(BracketFlareSymbol), 0.0, 0.0);

        DrulArray<bool> broken = new DrulArray<bool>(false, false);
        DrulArray<Item> bounds = spanner.GetBounds();
        Grob common = bounds[Direction.Negative]
            .CommonRefpoint(bounds[Direction.Positive], Axis.X);

        Grob textbit = spanner.GetObject(PedalTextSymbol) as Grob;
        if (textbit != null)
        {
            common = common.CommonRefpoint(textbit, Axis.X);
        }

        Interval spanPoints = new Interval(0, 0);
        foreach (Direction d in Both)
        {
            Item b = bounds[d];
            broken[d] = b.BreakStatusDirection() != Direction.Center;
            if (broken[d])
            {
                if (orig != null
                    && ((d == Direction.Positive
                         && spanner.BreakIndex != orig.BrokenIntos.Count - 1)
                        || (d == Direction.Negative && spanner.BreakIndex != 0)))
                {
                    height[d] = 0.0;
                }
                else
                {
                    flare[d] = 0.0;
                }

                spanPoints[d] = AxisGroupInterfaceVertical
                    .GenericBoundExtent(b, common, Axis.X)[Direction.Positive];
            }
            else
            {
                spanPoints[d] = b.RelativeCoordinate(common, Axis.X);
            }
        }

        /* For 'Mixed' style pedals, i.e.  a bracket preceded by text:  Ped._____|
           need to shorten by the extent of the text grob
        */
        if (textbit != null)
        {
            height[Direction.Negative] = 0;
            double padding = ToDouble(spanner.GetProperty(BoundPaddingSymbol), 0);
            spanPoints[Direction.Negative]
                = padding
                  + LooseColumns.RobustRelativeExtent(textbit, common, Axis.X)[
                      Direction.Positive];
        }

        Stencil m = Stencil.Empty;
        if (!spanPoints.IsEmpty && spanPoints.Length > 0.001)
        {
            m = Bracket.MakeBracket(
                spanner,
                Axis.Y,
                new Offset(spanPoints.Length, 0),
                height,
                Interval.Empty,
                flare,
                shorten);
        }

        m.TranslateAxis(
            spanPoints[Direction.Negative] - spanner.RelativeCoordinate(common, Axis.X),
            Axis.X);
        return m;
    }

    private static DrulArray<double> ToDrul(object value, double left, double right)
        => Grob.TryNumberPair(value, out Interval pair)
            ? new DrulArray<double>(pair.Left, pair.Right)
            : new DrulArray<double>(left, right);

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "piano-pedal-bracket")
            : fallback;
}

// update comment --hwn
/*
  Urg.

  This is almost text

  Problem is:

  * we have no kerning

  * symbols are at wrong place in font

  Properties:

  glyph -- text string (TODO: make one large glyph of the Ped symbol,
  removes need for member_print ())
*/

/*
  FIXME. Need to use markup.
*/

/// <summary>The sustain pedal sign, assembled glyph by glyph out of the music font.</summary>
public static class SustainPedal
{
    private static readonly Symbol TextSymbol = Symbol.Intern("text");

    /// <summary>Draws the pedal sign.</summary>
    /// <param name="e">The pedal grob.</param>
    /// <returns>The stencil.</returns>
    public static object Print(Grob e)
    {
        Stencil mol = Stencil.Empty;
        object glyph = e.GetProperty(TextSymbol);
        if (!(glyph is string text))
        {
            return mol;
        }

        for (int i = 0; i < text.Length; i++)
        {
            string idx = "pedal.";
            if (i + 3 <= text.Length && text.Substring(i, 3) == "Ped")
            {
                idx += "Ped";
                i += 2;
            }
            else
            {
                idx += text[i];
            }

            Stencil m = FontInterface.GetDefaultFont(e).FindByName(idx);
            if (!m.IsEmpty)
            {
                mol.AddAtEdge(Axis.X, Direction.Positive, m, 0);
            }
        }

        return mol;
    }
}
