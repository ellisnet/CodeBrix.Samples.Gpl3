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
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/rest.cc, lily/include/rest.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A rest symbol: its glyph choice — style, duration, whether it needs a ledger — and
/// its vertical placement against the staff lines.
/// </summary>
public static class Rest
{
    private static readonly Symbol DurationLogSymbol = Symbol.Intern("duration-log");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol VoicedPositionSymbol = Symbol.Intern("voiced-position");
    private static readonly Symbol LinePositionsSymbol = Symbol.Intern("line-positions");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol FontSizeSymbol = Symbol.Intern("font-size");

    // -> offset callback

    /// <summary>
    /// The <c>Y-offset</c> callback: half the staff space per staff position.
    /// </summary>
    /// <param name="me">The rest.</param>
    /// <returns>The vertical offset.</returns>
    public static double YOffsetCallback(Grob me)
    {
        int durationLog = RobustInt(me.GetProperty(DurationLogSymbol), 0);
        double ss = StaffSymbolReferencer.StaffSpace(me);

        return ss * 0.5
               * StaffPositionInternal(me, durationLog, DirectionalElementInterface.GetGrobDirection(me));
    }

    /// <summary>
    /// Computes where a rest of a given duration sits: an explicit
    /// <c>staff-position</c> wins, otherwise the voiced position snapped to the staff's
    /// own line positions — semibreves hang from a line, everything longer than a
    /// quarter lies on one.
    /// </summary>
    /// <param name="me">The rest.</param>
    /// <param name="durationLog">The duration log the position is asked for.</param>
    /// <param name="dir">The voice direction.</param>
    /// <returns>The staff position, in half staff-spaces.</returns>
    public static double StaffPositionInternal(Grob me, int durationLog, Direction dir)
    {
        if (me == null)
        {
            return 0;
        }

        bool positionOverride = SchemeConvert.IsNumber(me.GetProperty(StaffPositionSymbol));
        double pos;

        if (positionOverride)
        {
            pos = RobustDouble(me.GetProperty(StaffPositionSymbol), 0);

            /*
              semibreve rests are positioned one staff line off
            */
            if (durationLog == 0)
            {
                return pos + 2;
            }

            /*
              trust the client on good positioning;
              would be tempting to adjust position of rests longer than a quarter
              to be properly aligned to staff lines,
              but custom rest shapes may not need that sort of care.
            */

            return pos;
        }

        double vpos = dir.Value * RobustDouble(me.GetProperty(VoicedPositionSymbol), 0);
        pos = vpos;

        if (durationLog > 1)
        {
            /* Only half notes or longer want alignment with staff lines */
            return pos;
        }

        /*
          We need a staff symbol for actually aligning anything
        */
        Grob staff = StaffSymbolReferencer.GetStaffSymbol(me);
        if (staff == null)
        {
            return pos;
        }

        List<double> linepos = new List<double>();
        foreach (object entry in Pair.ToList(staff.GetProperty(LinePositionsSymbol)))
        {
            if (SchemeConvert.IsNumber(entry))
            {
                linepos.Add(SchemeConvert.ToDouble(entry, "line-positions"));
            }
        }

        if (linepos.Count == 0)
        {
            return pos;
        }

        if (linepos.Count == 1 && durationLog < 0 && !DirectionalElementInterface.GetGrobDirection(me).IsNonZero)
        {
            return linepos[0] - 2;
        }

        linepos.Sort();

        if (durationLog == 0)
        {
            /*
              lower voice semibreve rests generally hang a line lower
            */

            if (dir < Direction.Center)
            {
                pos -= 2;
            }

            /*
              make a semibreve rest hang from the next available line,
              except when there is none.
            */

            int index = UpperBound(linepos, pos);
            if (index < linepos.Count)
            {
                pos = linepos[index];
            }
            else
            {
                pos = linepos[linepos.Count - 1];
            }
        }
        else
        {
            int index = UpperBound(linepos, pos);
            if (index != 0)
            {
                index--;
            }

            pos = linepos[index];
        }

        /* Finished for neutral position */
        if (!dir.IsNonZero)
        {
            return pos;
        }

        /* If we have a voiced position, make sure that it's on the
           proper side of neutral before using it.
        */

        double neutral = StaffPositionInternal(me, durationLog, Direction.Center);

        if (dir.Value * (pos - neutral) > 0)
        {
            return pos;
        }
        else
        {
            return neutral + vpos;
        }
    }

    /* A rest might lie under a beam, in which case it should be cross-staff if
       the beam is cross-staff because the rest's position depends on the
       formatting of the beam. */

    /// <summary>The <c>cross-staff</c> callback: the stem's answer, when there is one.</summary>
    /// <param name="me">The rest.</param>
    /// <returns>The stem's <c>cross-staff</c> value, or <see langword="false"/>.</returns>
    public static object CalcCrossStaff(Grob me)
    {
        Grob stem = me.GetObject(StemSymbol) as Grob;

        if (stem == null)
        {
            return false;
        }

        return stem.GetProperty(CrossStaffSymbol);
    }

    /*
      make this function easily usable in C++
    */

    /// <summary>
    /// Builds the glyph name for a rest: <c>rests.&lt;durlog&gt;</c>, with an <c>o</c>
    /// suffix when it needs its own ledger and the style appended.
    /// </summary>
    /// <param name="me">The rest.</param>
    /// <param name="durlog">The duration log.</param>
    /// <param name="style">The style name.</param>
    /// <param name="tryLedgers">Whether ledgered variants may be chosen.</param>
    /// <param name="offset">A position offset applied before the ledger test.</param>
    /// <returns>The glyph name.</returns>
    public static string GlyphName(Grob me, int durlog, string style, bool tryLedgers, double offset)
    {
        bool isLedgered = false;
        if (tryLedgers && (durlog == -1 || durlog == 0 || durlog == 1))
        {
            int pos = (int)(StaffSymbolReferencer.GetPosition(me) + offset);

            /*
              half rests need ledger if not lying on a staff line,
              whole rests need ledger if not hanging from a staff line,
              breve rests need ledger if neither lying on nor hanging from a staff line
            */
            if (-1 <= durlog && durlog <= 1)
            {
                isLedgered = !StaffSymbolReferencer.OnStaffLine(me, pos)
                    && !(durlog == -1 && StaffSymbolReferencer.OnStaffLine(me, pos + 2));
            }
        }

        string actualStyle = style;

        if (style == "mensural" || style == "neomensural")
        {
            /*
              FIXME: Currently, ancient font does not provide ledgered rests;
              hence the "o" suffix in the glyph name is bogus.  But do we need
              ledgered rests at all now that we can draw ledger lines with
              variable width, length and blotdiameter? -- jr
            */
            isLedgered = false;

            /*
              There are no 32th/64th/128th mensural/neomensural rests.  In
              these cases, revert back to default style.
            */
            if (durlog > 4)
            {
                actualStyle = string.Empty;
            }
        }

        if ((style == "classical" || style == "z") && durlog != 2)
        {
            /*
              these styles differ from the default in quarter rests only
            */
            actualStyle = string.Empty;
        }

        if (style == "default")
        {
            /*
              Some parts of lily still prefer style "default" over "".
              Correct this here. -- jr
            */
            actualStyle = string.Empty;
        }

        return "rests." + durlog.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (isLedgered ? "o" : string.Empty) + actualStyle;
    }

    /// <summary>Draws the rest glyph, optionally with its ledgered variant.</summary>
    /// <param name="me">The rest.</param>
    /// <param name="ledgered">Whether the ledgered glyph may be selected.</param>
    /// <returns>The stencil.</returns>
    public static Stencil BrewInternalStencil(Grob me, bool ledgered)
    {
        object durlogScm = me.GetProperty(DurationLogSymbol);
        if (!SchemeConvert.IsNumber(durlogScm))
        {
            return Stencil.Empty;
        }

        int durlog = SchemeConvert.ToInt(durlogScm, "duration-log");

        string style = SchemeUtilities.RobustSymbolToString(me.GetProperty(StyleSymbol), "default");

        FontMetric fm = FontInterface.GetDefaultFont(me);
        string fontChar = GlyphName(me, durlog, style, ledgered, 0.0);
        Stencil result = fm != null ? fm.FindByName(fontChar) : Stencil.Empty;
        if (result.IsEmpty)
        {
            Warn.Warning("rest `" + fontChar + "' not found");
        }

        if (durlog < 0)
        {
            double fs = Math.Pow(2, RobustDouble(me.GetProperty(FontSizeSymbol), 0) / 6);
            double ss = StaffSymbolReferencer.StaffSpace(me);
            result.TranslateAxis(ss - fs, Axis.Y);
        }

        return result;
    }

    /**
       translate the rest vertically by amount DY, but only if
       it doesn't have staff-position set.
    */

    /// <summary>Moves the rest by whole staff positions, unless it was placed by hand.</summary>
    /// <param name="me">The rest.</param>
    /// <param name="dy">The distance, in half staff-spaces.</param>
    public static void Translate(Grob me, int dy)
    {
        if (!SchemeConvert.IsNumber(me.GetProperty(StaffPositionSymbol)))
        {
            me.TranslateAxis(dy * StaffSymbolReferencer.StaffSpace(me) / 2.0, Axis.Y);
            Grob p = me.YParent;
            p?.FlushExtentCache(Axis.Y);
        }
    }

    /// <summary>The <c>stencil</c> callback.</summary>
    /// <param name="me">The rest.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Print(Grob me) => BrewInternalStencil(me, true);

    /*
      We need the callback. The real stencil has ledgers depending on
      Y-position. The Y-position is known only after line breaking.  */

    /// <summary>The <c>X-extent</c> callback.</summary>
    /// <param name="me">The rest.</param>
    /// <returns>The extent.</returns>
    public static Interval Width(Grob me) => GenericExtentCallback(me, Axis.X);

    /// <summary>The <c>Y-extent</c> callback.</summary>
    /// <param name="me">The rest.</param>
    /// <returns>The extent.</returns>
    public static Interval Height(Grob me) => GenericExtentCallback(me, Axis.Y);

    /*
      We need the callback. The real stencil has ledgers depending on
      Y-position. The Y-position is known only after line breaking.  */

    /// <summary>
    /// Measures the rest WITHOUT its ledger on the X axis.
    /// <para>
    /// Upstream's comment: ledgers depend on Y position, which depends on rest
    /// collision, which depends on stem size, which depends on the beam of the opposite
    /// note column — so the X extent deliberately measures the unledgered glyph and may
    /// come out slightly small.
    /// </para>
    /// </summary>
    /// <param name="me">The rest.</param>
    /// <param name="axis">The axis to measure.</param>
    /// <returns>The extent.</returns>
    public static Interval GenericExtentCallback(Grob me, Axis axis)
    {
        Stencil m = BrewInternalStencil(me, axis != Axis.X);
        return m.Extent(axis);
    }

    /// <summary>
    /// The <c>pure-height</c> callback: the unledgered glyph's Y extent, which is
    /// well-defined without the line breaker. The begin and end columns are ignored,
    /// exactly as upstream ignores them.
    /// </summary>
    /// <param name="me">The rest.</param>
    /// <returns>The extent.</returns>
    public static Interval PureHeight(Grob me)
    {
        Stencil m = BrewInternalStencil(me, false);
        return m.Extent(Axis.Y);
    }

    private static int RobustInt(object value, int fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToInt(value, "rest") : fallback;

    private static double RobustDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "rest") : fallback;

    /// <summary>
    /// <c>std::upper_bound</c>: the index of the first element strictly greater than
    /// the value, or the count when there is none.
    /// </summary>
    private static int UpperBound(List<double> sorted, double value)
    {
        int lo = 0;
        int hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] <= value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
