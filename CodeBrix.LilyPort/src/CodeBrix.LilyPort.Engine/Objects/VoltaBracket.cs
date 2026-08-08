/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/volta-bracket.cc, lily/include/volta-bracket.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The volta bracket with its number: the horizontal line over an alternative, turned
/// down at each end, with the volta number set above its left edge.
/// </summary>
/// <remarks>
/// Upstream's own note, kept because it explains the shape of this code: this is too
/// complicated, and it is yet another version of side-positioning, badly implemented. It
/// should look for the system start delimiter to find the left edge of the staff.
/// </remarks>
public static class VoltaBracketInterface
{
    private static readonly Symbol BarsLeftSymbol = Symbol.Intern("bars-left");
    private static readonly Symbol BarsRightSymbol = Symbol.Intern("bars-right");
    private static readonly Symbol BracketFlareSymbol = Symbol.Intern("bracket-flare");
    private static readonly Symbol BreakAlignmentSymbol = Symbol.Intern("break-alignment");
    private static readonly Symbol EdgeHeightSymbol = Symbol.Intern("edge-height");
    private static readonly Symbol ShortenPairSymbol = Symbol.Intern("shorten-pair");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol VoltaNumberOffsetSymbol = Symbol.Intern("volta-number-offset");

    /// <summary>The <c>stencil</c> callback: the bracket, plus the number on unbroken
    /// and first-broken pieces.</summary>
    /// <param name="me">The volta bracket spanner.</param>
    /// <returns>The stencil, or the empty list when the grob killed itself.</returns>
    public static object Print(Spanner me)
    {
        Spanner originalSpan = me.Original;
        bool brokenFirstBracket = originalSpan != null
            && originalSpan.BrokenIntos.Count > 0
            && ReferenceEquals(originalSpan.BrokenIntos[0], me);

        Item bound = me.GetBound(Direction.Negative);

        // If the volta bracket appears after a line-break, make it start after the
        // prefatory matter.
        double left = 0.0;
        if (bound != null && bound.BreakStatusDirection() == Direction.Positive)
        {
            PaperColumn pc = bound.GetColumn();
            if (pc != null)
            {
                left = PaperColumn.BreakAlignWidth(pc, BreakAlignmentSymbol)[Direction.Positive]

                    // For some reason, break_align_width is relative to the x-parent of
                    // the column.
                    - bound.RelativeCoordinate(pc.GetParent(Axis.X), Axis.X);
            }
        }
        else
        {
            // The volta spanner is attached to the bar line, which is moved to the right.
            // We don't need to compensate for the left edge.
        }

        ModifyEdgeHeight(me);
        if (!me.IsLive)
        {
            return Nil.Instance;
        }

        DrulArray<double> edgeHeight = SchemeConvert.ToDrulDouble(
            me.GetProperty(EdgeHeightSymbol), new DrulArray<double>(2.0, 2.0));

        DrulArray<double> flare = SchemeConvert.ToDrulDouble(
            me.GetProperty(BracketFlareSymbol), new DrulArray<double>(0.0, 0.0));

        DrulArray<double> shorten = SchemeConvert.ToDrulDouble(
            me.GetProperty(ShortenPairSymbol), new DrulArray<double>(0.0, 0.0));

        double scale = -(int)DirectionalElementInterface.GetGrobDirection(me);
        edgeHeight[Direction.Negative] *= scale;
        edgeHeight[Direction.Positive] *= scale;

        Interval empty = Interval.Empty;
        Offset start = new Offset(me.SpannerLength() - left, 0);

        Stencil total = Bracket.MakeBracket(
            me, Axis.Y, start, edgeHeight, empty, flare, shorten);

        if (originalSpan == null || brokenFirstBracket)
        {
            object text = me.GetProperty(TextSymbol);
            Offset offset = ReadOffset(
                me.GetProperty(VoltaNumberOffsetSymbol), new Offset(1.0, -0.5));

            Stencil num = TextInterface.GrobInterpretMarkup(me, text);
            num.AlignTo(Axis.Y, 1.0);
            num.TranslateAxis(offset.Y, Axis.Y);
            total.AddAtEdge(
                Axis.X, Direction.Negative, num, -num.Extent(Axis.X).Length - offset.X);
        }

        total.TranslateAxis(left, Axis.X);
        return total;
    }

    /// <summary>
    /// Flattens the edges a broken piece should not draw, and kills a final piece that
    /// would draw nothing at all.
    /// </summary>
    /// <param name="me">The volta bracket spanner.</param>
    public static void ModifyEdgeHeight(Spanner me)
    {
        Spanner originalSpan = me.Original;

        bool brokenFirstBracket = originalSpan != null
            && originalSpan.BrokenIntos.Count > 0
            && ReferenceEquals(originalSpan.BrokenIntos[0], me);

        bool brokenLastBracket = originalSpan != null
            && originalSpan.BrokenIntos.Count > 0
            && ReferenceEquals(originalSpan.BrokenIntos[originalSpan.BrokenIntos.Count - 1], me);

        bool noVerticalStart = originalSpan != null && !brokenFirstBracket;
        bool noVerticalEnd = originalSpan != null && !brokenLastBracket;

        if (noVerticalEnd || noVerticalStart)
        {
            DrulArray<double> edgeHeight = SchemeConvert.ToDrulDouble(
                me.GetProperty(EdgeHeightSymbol), new DrulArray<double>(2.0, 2.0));

            if (noVerticalStart)
            {
                edgeHeight[Direction.Negative] = 0.0;
            }

            if (noVerticalEnd)
            {
                edgeHeight[Direction.Positive] = 0.0;
            }

            me.SetProperty(
                EdgeHeightSymbol,
                new Pair(edgeHeight[Direction.Negative], edgeHeight[Direction.Positive]));
        }

        if (brokenLastBracket && noVerticalEnd && noVerticalStart && !brokenFirstBracket)
        {
            me.Suicide();
        }
    }

    /// <summary>Records a bar line as one of the bracket's bounds.</summary>
    /// <param name="me">The volta bracket spanner.</param>
    /// <param name="bar">The bar line item.</param>
    /// <param name="direction">Which side the bar line is on.</param>
    public static void AddBar(Spanner me, Item bar, Direction direction)
    {
        Symbol bars = direction == Direction.Negative ? BarsLeftSymbol : BarsRightSymbol;
        PointerGroupInterface.AddGrob(me, bars, bar);
        Spanner.AddBoundItem(me, bar);
    }

    private static Offset ReadOffset(object value, Offset fallback)
    {
        if (value is Pair pair && SchemeConvert.IsNumber(pair.Car) && SchemeConvert.IsNumber(pair.Cdr))
        {
            return new Offset(
                SchemeConvert.ToDouble(pair.Car, "volta-bracket"),
                SchemeConvert.ToDouble(pair.Cdr, "volta-bracket"));
        }

        return fallback;
    }
}
