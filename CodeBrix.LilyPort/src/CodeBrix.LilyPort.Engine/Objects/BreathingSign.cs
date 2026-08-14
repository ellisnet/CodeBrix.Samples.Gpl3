/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Michael Krause
  Extensions for ancient notation (c) 2003--2026 by Juergen Reuter

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/breathing-sign.cc, lily/include/breathing-sign.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A breathing sign: the divisiones of Gregorian chant, and the property plumbing the
/// caesura architecture configures a <c>BreathingSign</c> through.
/// </summary>
public static class BreathingSign
{
    private static readonly Symbol BreathMarkDefinitionsSymbol
        = Symbol.Intern("breathMarkDefinitions");

    private static readonly Symbol BackendTypeSymbol = Symbol.Intern("backend-type?");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");
    private static readonly Symbol BlotDiameterSymbol = Symbol.Intern("blot-diameter");
    private static readonly Symbol LinePositionsSymbol = Symbol.Intern("line-positions");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");

    /// <summary>
    /// Applies a breath definition from <c>breathMarkDefinitions</c> to a grob.
    /// <para>
    /// This is modeled on similar (but more complicated) code in
    /// <c>Script_engraver</c>. A change here might warrant a change there. Only
    /// properties whose current value fails their own type predicate are overwritten,
    /// so an <c>\override</c> wins over the definition.
    /// </para>
    /// </summary>
    /// <param name="grob">The <c>BreathingSign</c> to configure.</param>
    /// <param name="context">The context holding <c>breathMarkDefinitions</c>.</param>
    /// <param name="breathType">The breath type symbol to look up.</param>
    public static void SetBreathProperties(Grob grob, Context context, object breathType)
    {
        if (!(breathType is Symbol))
        {
            throw SchemeErrors.WrongType(
                "ly:breathing-sign::set-breath-properties", "symbol", breathType);
        }

        object alist = context?.GetProperty(BreathMarkDefinitionsSymbol) ?? Nil.Instance;
        Pair entry = SchemeUtilities.Assq(breathType, alist);

        if (entry == null)
        {
            grob.Warning(
                "do not know how to interpret breath type: "
                + ((Symbol)breathType).Name);
            return;
        }

        object cursor = entry.Cdr;
        while (cursor is Pair propList)
        {
            if (propList.Car is Pair propPair)
            {
                object sym = propPair.Car;
                Interpreter interpreter = LilyPondScheme.Current;
                object type = SchemeUtilities.ObjectProperty(
                    interpreter, sym, BackendTypeSymbol);
                if (!SchemeUtilities.IsProcedure(type))
                {
                    Warn.ProgrammingError(
                        "invalid grob property name in breath definition: "
                        + Printer.Write(sym));
                    cursor = propList.Cdr;
                    continue;
                }

                object val = propPair.Cdr;

                object preset = sym is Symbol symbol
                    ? grob.GetPropertyData(symbol)
                    : Nil.Instance;
                object typeOk = SchemeUtilities.CallCallback(type, preset);
                if (val is Nil || (typeOk is bool flag && !flag))
                {
                    if (sym is Symbol target)
                    {
                        grob.SetProperty(target, val);
                    }
                }
            }

            cursor = propList.Cdr;
        }
    }

    /*
      UGH : this is full of C&P code. Consolidate!  --hwn
    */

    /*
      Gregorian chant divisio minima.  (Actually, this was the original
      breathing sign by Michael. -- jr)
    */

    /// <summary>Draws a small vertical line through the outermost staff line.</summary>
    /// <param name="grob">The breathing sign.</param>
    /// <returns>The stencil.</returns>
    public static Stencil DivisioMinima(Grob grob)
    {
        double staffSpace = StaffSymbolReferencer.StaffSpace(grob);

        double thickness = StaffSymbolReferencer.LineThickness(grob);
        thickness *= TranslatorSchemeHelpers.ToDouble(grob.GetProperty(ThicknessSymbol), 1.0);

        double blotdiameter = GetLayoutDimension(grob, BlotDiameterSymbol);

        /*
         * Draw a small vertical line through the uppermost (or, depending
         * on direction, lowermost) staff line.
         */
        Interval xdim = new Interval(0, thickness);
        Interval ydim = new Interval(-0.5 * staffSpace, +0.5 * staffSpace);
        Box b = new Box(xdim, ydim);
        return Lookup.RoundFilledBox(b, blotdiameter);
    }

    /*
      Gregorian chant divisio maior.
    */

    /// <summary>
    /// Draws a vertical line roughly centered in the staff, at least half the staff's
    /// height, both ends in the middle of a staff space.
    /// </summary>
    /// <param name="grob">The breathing sign.</param>
    /// <returns>The stencil.</returns>
    public static Stencil DivisioMaior(Grob grob)
    {
        double thickness = StaffSymbolReferencer.LineThickness(grob);
        thickness *= TranslatorSchemeHelpers.ToDouble(grob.GetProperty(ThicknessSymbol), 1.0);

        double blotdiameter = GetLayoutDimension(grob, BlotDiameterSymbol);

        /*
          Draw a vertical line that is roughly centered vertically in
          the staff (just like a bar) with the following requirements:
          1. length should be at least half the size of the staff
          2. both ends should be in the middle of a staff space.

          These two requirements contradict if the first or last space is
          larger than half of the whole staff (e.g. the staff consists of
          two lines only); in such cases the first prescription wins.
        */
        Interval ydim = new Interval(0.0, 0.0);
        Grob staff = StaffSymbolReferencer.GetStaffSymbol(grob);
        if (staff != null)
        {
            List<double> linePositions = new List<double>();
            object cursor = staff.GetProperty(LinePositionsSymbol);
            while (cursor is Pair pair)
            {
                if (SchemeConvert.IsNumber(pair.Car))
                {
                    linePositions.Add(SchemeConvert.ToDouble(pair.Car, "line-positions"));
                }

                cursor = pair.Cdr;
            }

            if (linePositions.Count > 0)
            {
                linePositions.Sort();
                ydim = new Interval(
                    linePositions[0], linePositions[linePositions.Count - 1]);

                double height = ydim.Length;
                if (height != 0.0)
                {
                    ydim.Widen(-0.25 * height);

                    /*
                      ydim has now the required height; to satisfy req. 2
                      find the staff spaces containing current endpoints.

                      standard algorithms are suitable to find the upper
                      line of these spaces; we must choose between
                      upper_bound and lower_bound considering that if
                      there's a line exactly at quarter of the staff (the
                      lower end) then we need the space below it, while if
                      there's a line exactly at three quarters of the staff
                      (upper end) then we need the space above it.

                      if the middle of the space found is not low/high
                      enough, take the next space (if there are no more
                      spaces, ydim won't be enlarged further).
                    */
                    int it = LowerBound(linePositions, ydim[Direction.Negative]);
                    double val = (linePositions[it - 1] + linePositions[it]) / 2;
                    if (ydim[Direction.Negative] < val && 0 < it - 1)
                    {
                        val = (linePositions[it - 2] + linePositions[it - 1]) / 2;
                    }

                    ydim.AddPoint(val);

                    it = UpperBound(linePositions, ydim[Direction.Positive]);
                    val = (linePositions[it - 1] + linePositions[it]) / 2;
                    if (val < ydim[Direction.Positive] && it + 1 < linePositions.Count)
                    {
                        val = (linePositions[it] + linePositions[it + 1]) / 2;
                    }

                    ydim.AddPoint(val);
                }
            }
        }

        double half = StaffSymbolReferencer.StaffSpace(grob) / 2;
        ydim = new Interval(ydim.Left * half, ydim.Right * half);

        Interval xdim = new Interval(0, thickness);
        Box b = new Box(xdim, ydim);
        return Lookup.RoundFilledBox(b, blotdiameter);
    }

    /*
      Gregorian chant divisio maxima.
    */

    /// <summary>Draws a line spanning the whole staff, like a <c>|</c> bar.</summary>
    /// <param name="grob">The breathing sign.</param>
    /// <returns>The stencil.</returns>
    public static Stencil DivisioMaxima(Grob grob)
    {
        double staffSpace = StaffSymbolReferencer.StaffSpace(grob);
        double thickness = StaffSymbolReferencer.LineThickness(grob);
        thickness *= TranslatorSchemeHelpers.ToDouble(grob.GetProperty(ThicknessSymbol), 1.0);

        double blotdiameter = GetLayoutDimension(grob, BlotDiameterSymbol);

        // like a "|" type bar
        Interval xdim = new Interval(0, thickness);
        Interval ydim = StaffSymbolReferencer.StaffSpan(grob);
        ydim = new Interval(ydim.Left * staffSpace / 2, ydim.Right * staffSpace / 2);
        Box b = new Box(xdim, ydim);
        return Lookup.RoundFilledBox(b, blotdiameter);
    }

    /*
      Gregorian chant finalis.
    */

    /// <summary>Draws two staff-spanning lines, like a <c>||</c> bar.</summary>
    /// <param name="grob">The breathing sign.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Finalis(Grob grob)
    {
        double staffSpace = StaffSymbolReferencer.StaffSpace(grob);
        double thickness = StaffSymbolReferencer.LineThickness(grob);
        thickness *= TranslatorSchemeHelpers.ToDouble(grob.GetProperty(ThicknessSymbol), 1.0);

        double blotdiameter = GetLayoutDimension(grob, BlotDiameterSymbol);

        // like a "||" type bar
        Interval xdim = new Interval(0, thickness);
        Interval ydim = StaffSymbolReferencer.StaffSpan(grob);
        ydim = new Interval(ydim.Left * staffSpace / 2, ydim.Right * staffSpace / 2);
        Box b = new Box(xdim, ydim);
        Stencil line1 = Lookup.RoundFilledBox(b, blotdiameter);
        Stencil line2 = line1;
        line2.TranslateAxis(0.5 * staffSpace, Axis.X);
        line1.AddStencil(line2);

        return line1;
    }

    /// <summary>
    /// The <c>Y-offset</c> callback: puts the sign on the staff line its
    /// <c>direction</c> selects.
    /// </summary>
    /// <param name="grob">The breathing sign.</param>
    /// <returns>The vertical offset.</returns>
    public static double OffsetCallback(Grob grob)
    {
        Direction d = DirectionalElementInterface.GetStrictGrobDirection(grob);

        Grob staff = StaffSymbolReferencer.GetStaffSymbol(grob);
        if (staff != null)
        {
            Interval iv = StaffSymbol.LineSpan(staff);
            double inter = StaffSymbol.StaffSpace(staff) / 2;
            return inter * iv[d];
        }

        return 0.0;
    }

    private static double GetLayoutDimension(Grob grob, Symbol name)
    {
        OutputDef layout = grob.Layout;
        return layout != null
            ? layout.GetDimension(name)
            : 0.0;
    }

    private static int LowerBound(List<double> values, double value)
    {
        int low = 0;
        int high = values.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (values[mid] < value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private static int UpperBound(List<double> values, double value)
    {
        int low = 0;
        int high = values.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (values[mid] <= value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
