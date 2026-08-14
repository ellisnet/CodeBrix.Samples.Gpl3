/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/multi-measure-rest.cc, lily/include/multi-measure-rest.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A rest spanning a whole number of measures: drawn as the thick horizontal
/// <c>big rest</c> beyond the expansion limit, and as a row of church rests —
/// longa/breve/semibreve glyphs — below it.
/// </summary>
public static class MultiMeasureRest
{
    private static readonly Symbol SpacingPairSymbol = Symbol.Intern("spacing-pair");
    private static readonly Symbol StaffBarSymbol = Symbol.Intern("staff-bar");
    private static readonly Symbol MeasureCountSymbol = Symbol.Intern("measure-count");
    private static readonly Symbol ExpandLimitSymbol = Symbol.Intern("expand-limit");
    private static readonly Symbol MeasureLengthSymbol = Symbol.Intern("measure-length");
    private static readonly Symbol RoundUpExceptionsSymbol = Symbol.Intern("round-up-exceptions");
    private static readonly Symbol RoundUpToLongerRestSymbol
        = Symbol.Intern("round-up-to-longer-rest");
    private static readonly Symbol UsableDurationLogsSymbol
        = Symbol.Intern("usable-duration-logs");
    private static readonly Symbol ThickThicknessSymbol = Symbol.Intern("thick-thickness");
    private static readonly Symbol HairThicknessSymbol = Symbol.Intern("hair-thickness");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol LinePositionsSymbol = Symbol.Intern("line-positions");
    private static readonly Symbol FontSizeSymbol = Symbol.Intern("font-size");
    private static readonly Symbol MaxSymbolSeparationSymbol
        = Symbol.Intern("max-symbol-separation");
    private static readonly Symbol SpacingSymbol = Symbol.Intern("spacing");
    private static readonly Symbol FullMeasureExtraSpaceSymbol
        = Symbol.Intern("full-measure-extra-space");
    private static readonly Symbol SpaceIncrementSymbol = Symbol.Intern("space-increment");
    private static readonly Symbol BoundPaddingSymbol = Symbol.Intern("bound-padding");
    private static readonly Symbol MinimumLengthSymbol = Symbol.Intern("minimum-length");

    /// <summary>
    /// Measures the span the rest must fill: from the right side of the left bound's
    /// break-aligned column to the left side of the right bound's.
    /// </summary>
    /// <param name="me">The rest spanner.</param>
    /// <returns>The interval, in the root's coordinates.</returns>
    public static Interval BarWidth(Spanner me)
    {
        object spacingPair = me.GetProperty(SpacingPairSymbol);
        Interval iv = Interval.Empty;
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            Item col = me.GetBound(d)?.GetColumn();
            object alignSym = spacingPair is Pair pair
                ? (d == Direction.Negative ? pair.Car : pair.Cdr)
                : StaffBarSymbol;
            Interval coldim = col != null
                ? PaperColumn.BreakAlignWidth(col, alignSym)
                : Interval.Empty;

            iv[d] = coldim[-d];
        }

        return iv;
    }

    /// <summary>The <c>stencil</c> callback.</summary>
    /// <param name="me">The rest spanner.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Print(Spanner me)
    {
        Interval spIv = BarWidth(me);
        double space = spIv.Length;

        double rx = me.GetBound(Direction.Negative)?.RelativeCoordinate(null, Axis.X) ?? 0.0;

        /*
          we gotta stay clear of sp_iv, so move a bit to the right if
          needed.
        */
        double xOff = Math.Max(spIv[Direction.Negative] - rx, 0.0);

        Stencil mol = Stencil.Empty;
        mol.AddStencil(SymbolStencil(me, space));

        mol.TranslateAxis(xOff, Axis.X);
        return mol;
    }

    /// <summary>The <c>Y-extent</c> callback: the symbol's height at any width.</summary>
    /// <param name="me">The rest spanner.</param>
    /// <returns>The extent.</returns>
    public static Interval Height(Spanner me)
    {
        double space = 1000000; // something very large...

        Stencil mol = Stencil.Empty;
        mol.AddStencil(SymbolStencil(me, space));

        return mol.Extent(Axis.Y);
    }

    private static int CalcMeasureDurationLog(Spanner me)
    {
        // TODO: Getting the measure length from a Paper_column, which is engraved in
        // Score context, is bogus in polymetric scores, where the Timing_translator
        // operates in Staff context rather than Score context.  See issue #4633.
        object sml = me.GetBound(Direction.Negative)?.GetProperty(MeasureLengthSymbol);
        Rational ml = sml is Moment moment ? moment.MainPart : Rational.One;
        double duration = ml.ToDouble();
        bool roundUp = MemberOfExceptions(ml, me.GetProperty(RoundUpExceptionsSymbol))
            || SchemeUtilities.ToBool(me.GetProperty(RoundUpToLongerRestSymbol));
        int closestUsableDurationLog;

        // Out of range initial values.
        if (roundUp)
        {
            closestUsableDurationLog = -15; // high value
        }
        else
        {
            closestUsableDurationLog = 15; // low value
        }

        int minimumUsableDurationLog = -15;
        int maximumUsableDurationLog = 15;

        object durationLogsList = me.GetProperty(UsableDurationLogsSymbol);
        if (durationLogsList is Nil || !(durationLogsList is Pair))
        {
            Warn.Warning("usable-duration-logs must be a non-empty list."
                         + "  Falling back to whole rests.");
            closestUsableDurationLog = 0;
        }
        else
        {
            foreach (object entry in Pair.ToList(durationLogsList))
            {
                if (!SchemeConvert.IsNumber(entry))
                {
                    continue;
                }

                int durLog = SchemeConvert.ToInt(entry, "usable-duration-logs");
                if (durLog > minimumUsableDurationLog)
                {
                    minimumUsableDurationLog = durLog;
                }

                if (durLog < maximumUsableDurationLog)
                {
                    maximumUsableDurationLog = durLog;
                }

                double dur = Math.Pow(2.0, -durLog);
                if (roundUp)
                {
                    if (duration <= dur && durLog > closestUsableDurationLog)
                    {
                        closestUsableDurationLog = durLog;
                    }
                }
                else
                {
                    if (duration >= dur && durLog < closestUsableDurationLog)
                    {
                        closestUsableDurationLog = durLog;
                    }
                }
            }
        }

        if (closestUsableDurationLog == 15)
        {
            closestUsableDurationLog = minimumUsableDurationLog;
        }

        if (closestUsableDurationLog == -15)
        {
            closestUsableDurationLog = maximumUsableDurationLog;
        }

        return closestUsableDurationLog;
    }

    /// <summary>
    /// <c>scm_member</c> of <c>(numerator . denominator)</c> in the
    /// <c>round-up-exceptions</c> list, by <c>equal?</c>.
    /// </summary>
    private static bool MemberOfExceptions(Rational ml, object exceptions)
    {
        Pair probe = new Pair(ml.Numerator, ml.Denominator);
        object cursor = exceptions;
        while (cursor is Pair pair)
        {
            if (SchemeUtilities.IsEqual(pair.Car, probe))
            {
                return true;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>Draws the rest symbol into a given width.</summary>
    /// <param name="me">The rest spanner.</param>
    /// <param name="space">The width to fill; zero measures the minimum size.</param>
    /// <returns>The stencil.</returns>
    public static Stencil SymbolStencil(Spanner me, double space)
    {
        int measureCount = 0;
        object m = me.GetProperty(MeasureCountSymbol);
        if (SchemeConvert.IsNumber(m))
        {
            measureCount = SchemeConvert.ToInt(m, "measure-count");
        }

        if (measureCount <= 0)
        {
            return Stencil.Empty;
        }

        object limit = me.GetProperty(ExpandLimitSymbol);
        int expandLimit = SchemeConvert.IsNumber(limit)
            ? SchemeConvert.ToInt(limit, "expand-limit")
            : 0;
        if (measureCount > expandLimit)
        {
            double padding = 0.15;
            Stencil s = BigRest(me, (1.0 - 2 * padding) * space);
            s.TranslateAxis(padding * space, Axis.X);
            return s;
        }
        else
        {
            FontMetric musfont = FontInterface.GetDefaultFont(me);
            int mdl = CalcMeasureDurationLog(me);
            return ChurchRest(me, musfont, measureCount, mdl, space);
        }
    }

    /*
      WIDTH can also be 0 to determine the minimum size of the object.
    */

    /// <summary>Draws the thick full-width rest used beyond the expansion limit.</summary>
    /// <param name="me">The rest grob.</param>
    /// <param name="width">The width to fill.</param>
    /// <returns>The stencil.</returns>
    public static Stencil BigRest(Grob me, double width)
    {
        double thickThick = RobustDouble(me.GetProperty(ThickThicknessSymbol), 1.0);
        double hairThick = RobustDouble(me.GetProperty(HairThicknessSymbol), 0.1);

        double ss = StaffSymbolReferencer.StaffSpace(me);
        double slt = me.Layout != null ? me.Layout.GetDimension(LineThicknessSymbol) : 0.0;
        double y = slt * thickThick / 2 * ss;
        double ythick = hairThick * slt * ss;
        Box b = new Box(
            new Interval(0.0, Math.Max(0.0, width - 2 * ythick)),
            new Interval(-y, y));

        double blot = width != 0.0 ? 0.8 * Math.Min(y, ythick) : 0.0;

        Stencil m = Stencil.Empty;
        Stencil box = Lookup.FilledBox(b);
        m.AddStencil(box);
        Stencil yb = Lookup.RoundFilledBox(
            new Box(new Interval(-0.5 * ythick, 0.5 * ythick), new Interval(-ss, ss)),
            blot);

        m.AddAtEdge(Axis.X, Direction.Positive, yb, 0);
        m.AddAtEdge(Axis.X, Direction.Negative, yb, 0);

        m.AlignTo(Axis.X, Direction.Negative.Value);

        return m;
    }

    /*
      Kirchenpause (?)
    */

    /// <summary>Draws the row of longa/breve/semibreve rests for a short multi-measure rest.</summary>
    /// <param name="me">The rest grob.</param>
    /// <param name="musfont">The music font.</param>
    /// <param name="measureCount">How many measures are being rested.</param>
    /// <param name="mdl">The measure's duration log.</param>
    /// <param name="space">The width to fill.</param>
    /// <returns>The stencil.</returns>
    public static Stencil ChurchRest(
        Grob me, FontMetric musfont, int measureCount, int mdl, double space)
    {
        // using double here is not less exact than rationals because
        // only simple, unscaled durations are used for representation
        // even if you have \time 10/6
        double displayedDuration = measureCount * Math.Pow(2.0, -mdl);
        List<Stencil> mols = new List<Stencil>();
        int symbolCount = 0;
        double symbolsWidth = 0.0;
        Direction dir = DirectionalElementInterface.GetGrobDirection(me);

        object sp = me.GetProperty(StaffPositionSymbol);
        double pos;

        Grob staff = StaffSymbolReferencer.GetStaffSymbol(me);
        bool oneline;
        if (staff != null)
        {
            object linePositions = staff.GetProperty(LinePositionsSymbol);
            oneline = Pair.ToList(linePositions).Count < 2;
        }
        else
        {
            // If there is no StaffSymbol, print MMrests on one (invisible) line.
            oneline = true;
        }

        if (sp is Nil)
        {
            if (1 <= displayedDuration
                && displayedDuration < 2) // i. e. longest rest symbol is semibreve
            {
                pos = Rest.StaffPositionInternal(me, 0, dir) - (oneline ? 0 : 2);
            }
            else
            {
                pos = Rest.StaffPositionInternal(me, 1, dir);
            }

            me.SetProperty(StaffPositionSymbol, pos);
        }
        else
        {
            pos = RobustDouble(sp, 0.0);
        }

        int dl = -3;
        while (displayedDuration > 0)
        {
            double duration = Math.Pow(2.0, -dl);

            if (displayedDuration < duration)
            {
                dl++;
                continue;
            }

            displayedDuration -= duration;

            double ss = StaffSymbolReferencer.StaffSpace(me);
            double spi = Rest.StaffPositionInternal(me, dl, dir);
            Stencil r;
            if (oneline && (dl == 0 || (dl < 0 && !dir.IsNonZero)))
            {
                spi -= 2;
                r = musfont != null
                    ? musfont.FindByName(
                        Rest.GlyphName(me, dl, string.Empty, true, dl == 0 ? 0 : -2))
                    : Stencil.Empty;
            }
            else
            {
                r = musfont != null
                    ? musfont.FindByName(
                        Rest.GlyphName(me, dl, string.Empty, true, dl == 0 ? 2 : 0))
                    : Stencil.Empty;
            }

            if (dl < 0)
            {
                double fs = Math.Pow(
                    2, RobustDouble(me.GetProperty(FontSizeSymbol), 0) / 6);
                r.TranslateAxis(ss * 0.5 * (spi - pos) + (ss - fs), Axis.Y);
            }
            else
            {
                r.TranslateAxis(ss * 0.5 * (spi - pos), Axis.Y);
            }

            symbolsWidth += r.Extent(Axis.X).Length;

            // Upstream conses each stencil onto the FRONT of the list; the assembly
            // loop below therefore walks newest-first, exactly as upstream's
            // as_ly_smob_list does.
            mols.Insert(0, r);
            symbolCount++;
        }

        /*
          When symbols spread to fullest extent, outer padding is this much
          bigger.
        */
        double outerPaddingFactor = 1.5;

        /* Widest gap between symbols; to be limited by max-symbol-separation */
        double innerPadding = (space - symbolsWidth)
            / (2 * outerPaddingFactor + (symbolCount - 1));
        if (innerPadding < 0)
        {
            innerPadding = 1.0;
        }

        double maxSeparation = Math.Max(
            RobustDouble(me.GetProperty(MaxSymbolSeparationSymbol), 8.0), 1.0);

        innerPadding = Math.Min(innerPadding, maxSeparation);
        double leftOffset
            = (space - symbolsWidth - (innerPadding * (symbolCount - 1))) / 2;

        Stencil mol = Stencil.Empty;
        foreach (Stencil s in mols)
        {
            mol.AddAtEdge(Axis.X, Direction.Negative, s, innerPadding);
        }

        mol.AlignTo(Axis.X, Direction.Negative.Value);
        mol.TranslateAxis(leftOffset, Axis.X);

        return mol;
    }

    /// <summary>Extends the spanner over one more column.</summary>
    /// <param name="me">The rest spanner.</param>
    /// <param name="c">The column.</param>
    public static void AddColumn(Spanner me, Item c)
    {
        Spanner.AddBoundItem(me, c);
    }

    /// <summary>
    /// States the rods that keep the measures wide enough for the rest and its
    /// per-measure spacing allowance.
    /// </summary>
    /// <param name="me">The rest spanner.</param>
    /// <param name="length">The symbol width the rods must at least allow.</param>
    public static void CalculateSpacingRods(Spanner me, double length)
    {
        DrulArray<Item> bounds = me.GetBounds();
        if (!(bounds[Direction.Negative] != null && bounds[Direction.Positive] != null))
        {
            Warn.ProgrammingError("Multi measure rest seems misplaced.");
            return;
        }

        PaperColumn lc = bounds[Direction.Negative].GetColumn();
        PaperColumn rc = bounds[Direction.Positive].GetColumn();
        if (lc == null || rc == null)
        {
            return;
        }

        Grob spacing = lc.GetObject(SpacingSymbol) as Grob;
        if (spacing == null)
        {
            spacing = rc.GetObject(SpacingSymbol) as Grob;
        }

        if (spacing != null)
        {
            SpacingOptions options = new SpacingOptions();
            options.InitFromGrob(me);
            Moment mlen = lc.GetProperty(MeasureLengthSymbol) is Moment stored
                ? stored
                : new Moment(Rational.One);

            object measureCount = me.GetProperty(MeasureCountSymbol);
            double count = SchemeConvert.IsNumber(measureCount)
                ? SchemeConvert.ToDouble(measureCount, "measure-count")
                : 1.0;

            length += RobustDouble(lc.GetProperty(FullMeasureExtraSpaceSymbol), 0.0)
                + options.GetDurationSpace(mlen.MainPart)
                + (RobustDouble(me.GetProperty(SpaceIncrementSymbol), 0.0)
                   * Math.Log2(count));
        }

        length += 2 * RobustDouble(me.GetProperty(BoundPaddingSymbol), 0.0);

        double minlen = RobustDouble(me.GetProperty(MinimumLengthSymbol), 0.0);

        PaperColumn lb = lc.FindPrebrokenPiece(Direction.Positive);
        PaperColumn rb = rc.FindPrebrokenPiece(Direction.Negative);
        foreach (PaperColumn li in new[] { lc, lb })
        {
            if (li == null)
            {
                continue;
            }

            foreach (PaperColumn ri in new[] { rc, rb })
            {
                if (ri == null)
                {
                    continue;
                }

                Rod rod = new Rod(li, ri);
                rod.Distance = Math.Max(
                    PaperColumn.MinimumDistance(li, ri) + length, minlen);
                rod.AddToColumns();
            }
        }
    }

    /// <summary>
    /// The <c>springs-and-rods</c> callback: rods sized from the rest symbol itself.
    /// </summary>
    /// <param name="me">The rest spanner.</param>
    public static void SetSpacingRods(Spanner me)
    {
        double symWidth = SymbolStencil(me, 0.0).Extent(Axis.X).Length;
        CalculateSpacingRods(me, symWidth);
    }

    /// <summary>
    /// The <c>springs-and-rods</c> callback for the rest's texts: rods sized from the
    /// finished stencil.
    /// </summary>
    /// <param name="me">The text spanner.</param>
    public static void SetTextRods(Spanner me)
    {
        Stencil? stil = me.GetStencil();

        /* FIXME uncached */
        double len = stil.HasValue && !stil.Value.Extent(Axis.X).IsEmpty
            ? stil.Value.Extent(Axis.X).Length
            : 0.0;
        CalculateSpacingRods(me, len);
    }

    private static double RobustDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "multi-measure-rest")
            : fallback;
}
