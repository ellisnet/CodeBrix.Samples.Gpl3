/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/staff-symbol.cc, lily/include/staff-symbol.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// The staff lines, and the vertical unit everything on the staff is measured in.
/// <para>
/// The staff symbol defines the STAFF SPACE — the distance between two adjacent lines —
/// and half of one is a POSITION. Every vertical placement in the engraver layer is
/// expressed in positions, with zero at the middle line, which is why almost nothing
/// else has to know how tall a staff is.
/// </para>
/// </summary>
public static class StaffSymbol
{
    private static readonly Symbol LinePositionsSymbol = Symbol.Intern("line-positions");
    private static readonly Symbol LineCountSymbol = Symbol.Intern("line-count");
    private static readonly Symbol StaffSpaceSymbol = Symbol.Intern("staff-space");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol LedgerLineThicknessSymbol = Symbol.Intern("ledger-line-thickness");
    private static readonly Symbol LedgerPositionsSymbol = Symbol.Intern("ledger-positions");
    private static readonly Symbol LedgerPositionsFunctionSymbol
        = Symbol.Intern("ledger-positions-function");
    private static readonly Symbol LedgerExtraSymbol = Symbol.Intern("ledger-extra");
    private static readonly Symbol WidthSymbol = Symbol.Intern("width");
    private static readonly Symbol BreakAlignSymbolsSymbol = Symbol.Intern("break-align-symbols");
    private static readonly Symbol BreakAlignmentSymbol = Symbol.Intern("break-alignment");

    /// <summary>
    /// The <c>stencil</c> callback: draws one horizontal line per staff-line position.
    /// </summary>
    /// <param name="grob">The staff symbol, which is a spanner.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Print(Grob grob)
    {
        if (!(grob is Spanner me))
        {
            Warn.ProgrammingError("staff symbol must be a spanner");
            return Stencil.Empty;
        }

        DrulArray<Item> bounds = me.GetBounds();
        if (bounds[Direction.Negative] == null || bounds[Direction.Positive] == null)
        {
            Warn.ProgrammingError("staff symbol with no bounds");
            return Stencil.Empty;
        }

        Grob common = bounds[Direction.Negative].CommonRefpoint(bounds[Direction.Positive], Axis.X);

        Interval spanPoints = new Interval(0, 0);

        /*
          For raggedright without ragged staves, simply set width to the linewidth.

          (ok -- lousy UI, since width is in staff spaces)

          --hwn.
        */
        double t = me.Layout == null ? 0.0 : me.Layout.GetDimension(LineThicknessSymbol);
        t *= DoubleOr(me.GetProperty(ThicknessSymbol), 1.0);

        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            object width = me.GetProperty(WidthSymbol);
            if (d == Direction.Positive && SchemeConvert.IsNumber(width))
            {
                /*
                  don't multiply by Staff_symbol_referencer::staff_space (me),
                  since that would make aligning staff symbols of different sizes to
                  one right margin hell.
                */
                spanPoints[Direction.Positive] = SchemeConvert.ToDouble(width, "width");
            }
            else
            {
                Item x = bounds[d];
                if (x.Extent(x, Axis.X).IsEmpty
                    || (x.BreakStatusDirection().IsNonZero && me.BrokenNeighbor(d) != null))
                {
                    spanPoints[d] = x.RelativeCoordinate(common, Axis.X);
                }
                else
                {
                    // What the default implementation of to-barline does for
                    // spanners is not really in usefully recognizable shape by
                    // now, so we just reimplement.
                    object where = d == Direction.Positive
                        ? me.GetProperty(BreakAlignSymbolsSymbol)
                        : BreakAlignmentSymbol;
                    spanPoints[d] = PaperColumn.BreakAlignWidth(x, where)[d];
                }
            }

            spanPoints[d] -= d.Value * t / 2;
        }

        Stencil m = Stencil.Empty;

        List<double> linePositions = Numbers(me.GetProperty(LinePositionsSymbol));

        Stencil line = Lookup.HorizontalLine(
            spanPoints - me.RelativeCoordinate(common, Axis.X), t);

        double space = StaffSpace(me);
        foreach (double p in linePositions)
        {
            Stencil b = line;
            b.TranslateAxis(p * 0.5 * space, Axis.Y);
            m.AddStencil(b);
        }

        return m;
    }

    /// <summary>
    /// The <c>line-positions</c> callback: evenly spaced positions symmetric about zero,
    /// derived from <c>line-count</c>.
    /// </summary>
    /// <param name="grob">The staff symbol.</param>
    /// <returns>The positions, as a Scheme list.</returns>
    public static object CalcLinePositions(Grob grob)
    {
        object count = grob.GetProperty(LineCountSymbol);
        int lineCount = SchemeConvert.IsNumber(count)
            ? SchemeConvert.ToInt(count, "line-count")
            : 0;

        double height = lineCount - 1;
        List<object> values = new List<object>(Math.Max(lineCount, 0));
        for (int i = 0; i < lineCount; i++)
        {
            values.Add(height - (i * 2));
        }

        return Pair.ListFrom(values);
    }

    /// <summary>
    /// The <c>Y-extent</c> callback: how tall the staff is, including the outermost
    /// lines' own thickness.
    /// </summary>
    /// <param name="grob">The staff symbol.</param>
    /// <returns>The vertical extent.</returns>
    public static Interval Height(Grob grob)
    {
        Interval yExtent = LineSpan(grob); // units of staff position
        if (!yExtent.IsEmpty)              // line count > 0
        {
            // convert staff position to height
            yExtent *= 0.5 * StaffSpace(grob);

            // account for top and bottom line thickness
            double t = grob.Layout == null ? 0.0 : grob.Layout.GetDimension(LineThicknessSymbol);
            t *= DoubleOr(grob.GetProperty(ThicknessSymbol), 1.0);
            yExtent.Widen(t / 2);
        }
        else
        {
            yExtent = new Interval(0, 0);
        }

        return yExtent;
    }

    /// <summary>Returns the distance between two adjacent staff lines.</summary>
    /// <param name="grob">The staff symbol.</param>
    /// <returns>The staff space.</returns>
    public static double StaffSpace(Grob grob)
    {
        double ss = grob.Layout == null ? 1.0 : grob.Layout.GetDimension(StaffSpaceSymbol);
        return DoubleOr(grob.GetProperty(StaffSpaceSymbol), 1.0) * ss;
    }

    /// <summary>Returns how thick a staff line is drawn.</summary>
    /// <param name="grob">The staff symbol.</param>
    /// <returns>The thickness.</returns>
    public static double GetLineThickness(Grob grob)
    {
        double lt = grob.Layout == null ? 0.0 : grob.Layout.GetDimension(LineThicknessSymbol);
        return DoubleOr(grob.GetProperty(ThicknessSymbol), 1.0) * lt;
    }

    /// <summary>
    /// Returns how thick a ledger line is drawn: part line thickness, part staff space,
    /// so it scales sensibly at any staff size.
    /// </summary>
    /// <param name="grob">The staff symbol.</param>
    /// <returns>The thickness.</returns>
    public static double GetLedgerLineThickness(Grob grob)
    {
        object pair = grob.GetProperty(LedgerLineThicknessSymbol);
        Offset z = pair is Pair p
            ? new Offset(DoubleOr(p.Car, 1.0), DoubleOr(p.Cdr, 0.1))
            : new Offset(1.0, 0.1);

        return (z[Axis.X] * GetLineThickness(grob)) + (z[Axis.Y] * StaffSpace(grob));
    }

    /// <summary>
    /// Returns the range of staff positions the lines occupy.
    /// </summary>
    /// <param name="grob">The staff symbol.</param>
    /// <returns>The span, or the empty interval <c>(1 . -1)</c> when there are no lines.</returns>
    public static Interval LineSpan(Grob grob)
    {
        List<double> linePositions = Numbers(grob.GetProperty(LinePositionsSymbol));

        // This stems from history.  We used to compute this from the line-count
        // property with [-(line-count) + 1, line-count - 1].  This would give the
        // empty interval [1, -1] for line-count == 0.  It could make more sense to
        // remove these two lines, which would make the code use the more conventional
        // interval [+infinity, -infinity] in this case.  If you change this, be sure
        // to check that all callers will do something sane with it.  See also similar
        // code in bar-line.scm.
        if (linePositions.Count == 0)
        {
            return new Interval(1, -1);
        }

        Interval iv = Interval.Empty;
        foreach (double p in linePositions)
        {
            iv.AddPoint(p);
        }

        return iv;
    }

    /// <summary>
    /// Determines whether a staff position falls on a line — a staff line, or
    /// optionally a ledger line.
    /// </summary>
    /// <param name="grob">The staff symbol.</param>
    /// <param name="position">The staff position.</param>
    /// <param name="allowLedger">Whether a ledger line counts.</param>
    /// <returns><see langword="true"/> when the position is on a line.</returns>
    public static bool OnLine(Grob grob, int position, bool allowLedger = true)
    {
        // staff lines
        foreach (double line in Numbers(grob.GetProperty(LinePositionsSymbol)))
        {
            if (position == line)
            {
                return true;
            }
        }

        // ledger lines
        if (allowLedger)
        {
            List<double> ledgers = LedgerPositions(grob, position, null);
            if (ledgers.Count == 0)
            {
                return false;
            }

            foreach (double line in ledgers)
            {
                if (position == line)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the staff positions at which ledger lines are drawn for a note at a
    /// position.
    /// <para>
    /// A note head may override the answer outright through its own
    /// <c>ledger-positions</c>. Otherwise the lines fill from the nearest staff line
    /// out to the note, on the same parity as that line, and any that would land on a
    /// staff line are dropped.
    /// </para>
    /// </summary>
    /// <param name="grob">The staff symbol.</param>
    /// <param name="position">The note's staff position.</param>
    /// <param name="head">The note head, when there is one.</param>
    /// <returns>The ledger positions.</returns>
    public static List<double> LedgerPositions(Grob grob, int position, Item head)
    {
        // allow override of ledger positions via note head grob...
        if (head != null && head.GetProperty(LedgerPositionsSymbol) is Pair headPositions)
        {
            return Numbers(headPositions);
        }

        // ...or via custom ledger positions function. It is stored UNEVALUATED, so it
        // has to be evaluated before it can be called -- upstream uses the interaction
        // environment, the port uses the ambient interpreter's current module.
        if (grob.GetProperty(LedgerPositionsFunctionSymbol) is Pair function)
        {
            CodeBrix.LilyScheme.Interpreter interpreter = LilyPondScheme.Current;
            object procedure = interpreter?.Eval(function);
            if (procedure is Procedure)
            {
                return Numbers(SchemeUtilities.CallCallback(procedure, grob, (long)position));
            }
        }

        object ledgerPositions = grob.GetProperty(LedgerPositionsSymbol);

        // allow override of `ledger-extra` via note head grob...
        object ledgerExtraValue = head?.GetProperty(LedgerExtraSymbol) ?? Nil.Instance;
        if (ledgerExtraValue is Nil)
        {
            ledgerExtraValue = grob.GetProperty(LedgerExtraSymbol);
        }

        double ledgerExtra = DoubleOr(ledgerExtraValue, 0);

        List<double> linePositions = Numbers(grob.GetProperty(LinePositionsSymbol));
        List<double> values = new List<double>();

        if (linePositions.Count == 0)
        {
            return values;
        }

        // find the staff line nearest to note position
        double nearestLine = 0;
        double lineDistance = double.PositiveInfinity;
        foreach (double p in linePositions)
        {
            double distance = Math.Abs(p - position);

            // prefer values nearer to the middle staff line
            if ((p >= 0 && position > 0 && p < position)
                || (p <= 0 && position < 0 && p > position))
            {
                distance = Math.BitDecrement(distance);
            }

            if (distance < lineDistance)
            {
                nearestLine = p;
                lineDistance = distance;
            }
        }

        // nothing to do for notes on a staff line and normal ledger lines
        if (lineDistance == 0 && !(ledgerPositions is Pair) && ledgerExtra == 0)
        {
            return values;
        }

        Direction extraDirection = position - nearestLine > 0
            ? Direction.Positive
            : position - nearestLine < 0
                ? Direction.Negative
                : nearestLine > 0
                    ? Direction.Positive
                    : Direction.Negative;

        // construct an interval that spans up the vertical range of ledger
        // lines, normally from the nearest staff line to the note head
        double extraPosition = position + (ledgerExtra * extraDirection.Value);
        if ((extraPosition - nearestLine) * extraDirection.Value < 0)
        {
            extraPosition = nearestLine;
        }

        Interval ledgerFill = Interval.Empty;
        ledgerFill.AddPoint(nearestLine);
        ledgerFill.AddPoint(extraPosition);

        if (ledgerPositions is Pair)
        {
            values.AddRange(CustomLedgerPositions(ledgerPositions, ledgerFill));
        }
        else
        {
            // normal ledger lines
            int bottom = (int)Math.Ceiling(ledgerFill[Direction.Negative]);
            int top = (int)Math.Floor(ledgerFill[Direction.Positive]);
            double nearest = nearestLine < 0 ? Math.Floor(nearestLine) : Math.Ceiling(nearestLine);
            int oddStart = (int)nearest & 1;
            int ledgerCount = (top - bottom + 2 - ((bottom & 1) != oddStart ? 1 : 0)) / 2;

            int value = bottom + ((bottom & 1) != oddStart ? 1 : 0);
            for (int i = 0; i < ledgerCount; i++)
            {
                values.Add(value);
                value += 2;
            }
        }

        // remove all ledger lines that would fall on staff lines
        List<double> finalValues = new List<double>();
        foreach (double v in values)
        {
            if (!linePositions.Contains(v))
            {
                finalValues.Add(v);
            }
        }

        return finalValues;
    }

    private static List<double> CustomLedgerPositions(object ledgerPositions, Interval ledgerFill)
    {
        List<double> values = new List<double>();

        double minPosition = double.PositiveInfinity;
        double maxPosition = double.NegativeInfinity;

        // find the extent of the ledger pattern
        object cursor = ledgerPositions;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair group)
            {
                foreach (double current in Numbers(group))
                {
                    maxPosition = Math.Max(maxPosition, current);
                    minPosition = Math.Min(minPosition, current);
                }
            }
            else if (SchemeConvert.IsNumber(pair.Car))
            {
                double current = SchemeConvert.ToDouble(pair.Car, "ledger-positions");
                maxPosition = Math.Max(maxPosition, current);
                minPosition = Math.Min(minPosition, current);
            }

            cursor = pair.Cdr;
        }

        double cycle = maxPosition - minPosition;
        if (!(ledgerPositions is Pair) || cycle < 0.1)
        {
            return values;
        }

        // fill the interval `ledgerFill` with ledger lines; we start at a
        // multiple of the pattern cycle length that is at the edge or below
        // the `ledgerFill` range
        double n = (ledgerFill[Direction.Negative] - minPosition) / cycle;
        double offset = Math.Floor(n) * cycle;

        object entry = ledgerPositions;
        do
        {
            object head = ((Pair)entry).Car;
            if (SchemeConvert.IsNumber(head))
            {
                double current = SchemeConvert.ToDouble(head, "ledger-positions") + offset;
                if (ledgerFill.Contains(current))
                {
                    values.Add(current);
                }
            }
            else
            {
                // grouped ledger lines, either add all or none
                List<double> grouped = new List<double>();
                foreach (double member in Numbers(head))
                {
                    grouped.Add(member + offset);
                }

                bool anyInside = false;
                foreach (double v in grouped)
                {
                    if (ledgerFill.Contains(v))
                    {
                        anyInside = true;
                        break;
                    }
                }

                if (anyInside)
                {
                    values.AddRange(grouped);
                }
            }

            entry = ((Pair)entry).Cdr;
            if (!(entry is Pair))
            {
                entry = ledgerPositions;
                offset += cycle;
            }
        }
        while (offset + minPosition <= ledgerFill[Direction.Positive]);

        return values;
    }

    private static List<double> Numbers(object list)
    {
        List<double> values = new List<double>();
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (SchemeConvert.IsNumber(pair.Car))
            {
                values.Add(SchemeConvert.ToDouble(pair.Car, "line-positions"));
            }

            cursor = pair.Cdr;
        }

        return values;
    }

    private static double DoubleOr(object value, double fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "staff-symbol") : fallback;
}
