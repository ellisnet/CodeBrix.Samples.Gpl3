/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2003--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/lyric-hyphen.cc, lily/include/lyric-hyphen.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  TODO: should extract hyphen dimensions or hyphen glyph from the
  font.
 */

/// <summary>
/// The centred hyphen between two syllables of one word — drawn as a row of dashes, not
/// a single line, so that a wide gap between syllables stays legible.
/// <para>
/// The dash count is chosen so the row fills the gap at roughly the requested period, and
/// the leftover space is split evenly at both ends. If the gap is too narrow to hold even
/// one dash the hyphen VANISHES — except at a line end, where it must stay visible to show
/// the word continues.
/// </para>
/// </summary>
public static class LyricHyphen
{
    private static readonly Symbol AfterLineBreakingSymbol = Symbol.Intern("after-line-breaking");
    private static readonly Symbol DashPeriodSymbol = Symbol.Intern("dash-period");
    private static readonly Symbol FontSizeSymbol = Symbol.Intern("font-size");
    private static readonly Symbol HeightSymbol = Symbol.Intern("height");
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol MinimumDistanceSymbol = Symbol.Intern("minimum-distance");
    private static readonly Symbol MinimumLengthSymbol = Symbol.Intern("minimum-length");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol WhiteoutSymbol = Symbol.Intern("whiteout");
    private static readonly Symbol WhiteoutColorSymbol = Symbol.Intern("whiteout-color");

    /// <summary>The <c>stencil</c> callback: the row of dashes, with optional whiteout
    /// behind each one.</summary>
    /// <param name="me">The hyphen spanner.</param>
    /// <returns>The stencil, or the empty list when the hyphen draws nothing.</returns>
    public static object Print(Spanner me)
    {
        DrulArray<Item> bounds = me.GetBounds();

        // FIXME: does this bring anything more than
        // ly:spanner::kill-zero-spanned-time in
        // after-line-breaking? --JeanAS
        if (bounds[Direction.Negative] != null
            && bounds[Direction.Negative].BreakStatusDirection() != Direction.Center
            && PaperColumn.WhenMoment(bounds[Direction.Negative])
                == PaperColumn.WhenMoment(bounds[Direction.Positive].GetColumn())
            && !SchemeUtilities.ToBool(me.GetProperty(AfterLineBreakingSymbol)))
        {
            return Nil.Instance;
        }

        Grob common = bounds[Direction.Negative].CommonRefpoint(bounds[Direction.Positive], Axis.X);

        Interval spanPoints = new Interval();
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            Interval iv = AxisGroupInterfaceVertical.GenericBoundExtent(bounds[d], common, Axis.X);

            spanPoints[d] = iv.IsEmpty
                ? bounds[d].RelativeCoordinate(common, Axis.X)
                : iv[-d];
        }

        double lt = me.Layout == null ? 0.0 : me.Layout.GetDimension(LineThicknessSymbol);
        double th = RobustDouble(me.GetProperty(ThicknessSymbol), 1) * lt;
        double fontSizeStep = RobustDouble(me.GetProperty(FontSizeSymbol), 0.0);
        double h = RobustDouble(me.GetProperty(HeightSymbol), 0.5)
                   * Math.Pow(2.0, fontSizeStep / 6.0);

        // interval?

        double dashPeriod = RobustDouble(me.GetProperty(DashPeriodSymbol), 1.0);
        double dashLength = RobustDouble(me.GetProperty(LengthSymbol), .5);
        double padding = RobustDouble(me.GetProperty(PaddingSymbol), 0.1);
        double whiteout = RobustDouble(me.GetProperty(WhiteoutSymbol), -1);

        double whiteoutColorR = 1.0;
        double whiteoutColorG = 1.0;
        double whiteoutColorB = 1.0;
        double whiteoutColorA = 1.0;
        string whiteoutColorString = string.Empty;

        object whiteoutColor = me.GetProperty(WhiteoutColorSymbol);
        if (whiteoutColor is MutableString || whiteoutColor is string)
        {
            whiteoutColorString = whiteoutColor.ToString();
        }
        else if (whiteoutColor is Pair colorPair)
        {
            whiteoutColorR = SchemeConvert.ToDouble(colorPair.Car, "lyric hyphen");
            Pair rest1 = colorPair.Cdr as Pair;
            whiteoutColorG = SchemeConvert.ToDouble(rest1.Car, "lyric hyphen");
            Pair rest2 = rest1.Cdr as Pair;
            whiteoutColorB = SchemeConvert.ToDouble(rest2.Car, "lyric hyphen");
            whiteoutColorA = rest2.Cdr is Pair rest3
                ? SchemeConvert.ToDouble(rest3.Car, "lyric hyphen")
                : 1.0;
        }

        if (dashPeriod < dashLength)
        {
            dashPeriod = 1.5 * dashLength;
        }

        double l = spanPoints.Length;

        int n = (int)Math.Ceiling((l / dashPeriod) - 0.5);
        if (n <= 0)
        {
            n = 1;
        }

        if (l < dashLength + (2 * padding)
            && bounds[Direction.Positive].BreakStatusDirection() == Direction.Center)
        {
            double minimumLength = RobustDouble(me.GetProperty(MinimumLengthSymbol), .3);
            dashLength = Math.Max(l - (2 * padding), minimumLength);
        }

        double spaceLeft = l - dashLength - ((n - 1) * dashPeriod);

        /*
          If there is not enough space, the hyphen should disappear, but not
          at the end of the line.
        */
        if (spaceLeft < 0.0
            && bounds[Direction.Positive].BreakStatusDirection() == Direction.Center)
        {
            return Nil.Instance;
        }

        spaceLeft = Math.Max(spaceLeft, 0.0);

        Box b = new Box(new Interval(0, dashLength), new Interval(h, h + th));
        Stencil dashMol = Lookup.RoundFilledBox(b, 0.8 * lt);

        // Stencil is a struct, so assignment already gives each iteration the private copy
        // upstream gets from `Stencil m (dash_mol)'.
        Stencil total = Stencil.Empty;
        for (int i = 0; i < n; i++)
        {
            Stencil m = dashMol;
            m.TranslateAxis(
                spanPoints[Direction.Negative] + (i * dashPeriod) + (spaceLeft / 2), Axis.X);

            total.AddStencil(m);
            if (whiteout > 0.0)
            {
                Box c = new Box(
                    new Interval(0, dashLength + (2 * whiteout * lt)),
                    new Interval(h - (whiteout * lt), h + th + (whiteout * lt)));

                Stencil w = Lookup.RoundFilledBox(c, 0.8 * lt);
                w = whiteoutColorString.Length > 0
                    ? w.InColor(whiteoutColorString)
                    : w.InColor(whiteoutColorR, whiteoutColorG, whiteoutColorB, whiteoutColorA);

                w.TranslateAxis(
                    spanPoints[Direction.Negative] + (i * dashPeriod) + (spaceLeft / 2)
                        - (whiteout * lt),
                    Axis.X);

                total.AddStencil(w);
            }
        }

        total.TranslateAxis(-me.RelativeCoordinate(common, Axis.X), Axis.X);
        return total;
    }

    /// <summary>The <c>springs-and-rods</c> callback: keeps the two syllables far enough
    /// apart for the hyphen, and again after a line break.</summary>
    /// <param name="me">The hyphen spanner.</param>
    /// <returns>The unspecified value.</returns>
    public static object SetSpacingRods(Spanner me)
    {
        SystemGrob root = SystemGrob.GetRootSystem(me);
        DrulArray<Item> bounds = me.GetBounds();
        if (bounds[Direction.Negative] == null || bounds[Direction.Positive] == null)
        {
            return Unspecified.Instance;
        }

        List<PaperColumn> cols = root.BrokenColumnRange(
            bounds[Direction.Negative].GetColumn(), bounds[Direction.Positive].GetColumn());

        Rod rod = default;
        rod.Distance = RobustDouble(me.GetProperty(MinimumDistanceSymbol), 0);
        rod.ItemDrul = bounds;
        rod.Distance += rod.BoundsProtrusion();
        rod.AddToColumns();

        if (cols.Count > 0
            && SchemeUtilities.ToBool(me.GetPropertyData(AfterLineBreakingSymbol)))
        {
            Rod rodAfterBreak = default;
            rodAfterBreak.ItemDrul[Direction.Negative]
                = cols[cols.Count - 1].FindPrebrokenPiece(Direction.Positive);

            rodAfterBreak.ItemDrul[Direction.Positive] = bounds[Direction.Positive];
            rodAfterBreak.Distance = RobustDouble(me.GetProperty(LengthSymbol), 0.5);
            rodAfterBreak.Distance += RobustDouble(me.GetProperty(PaddingSymbol), 0.1) * 2;
            rodAfterBreak.Distance += rodAfterBreak.BoundsProtrusion();
            rodAfterBreak.AddToColumns();
        }

        return Unspecified.Instance;
    }

    private static double RobustDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "lyric hyphen")
            : fallback;
}
