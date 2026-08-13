/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/staff-symbol-referencer.cc, lily/include/staff-symbol-referencer.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// Anything positioned relative to a staff: note heads, clefs, accidentals, rests.
/// <para>
/// A referencer carries a <c>staff-position</c> in half staff-spaces, and
/// <see cref="Callback"/> — the <c>Y-offset</c> callback almost every such grob uses —
/// turns that into a real vertical offset. Handing every grob the staff symbol it
/// belongs to is what the Staff_symbol_engraver does on every acknowledgement, and it
/// is why a note head knows how tall a staff space is without being told.
/// </para>
/// </summary>
public static class StaffSymbolReferencer
{
    private static readonly Symbol StaffSymbolSymbol = Symbol.Intern("staff-symbol");
    private static readonly Symbol StaffSymbolInterfaceSymbol
        = Symbol.Intern("staff-symbol-interface");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");

    /// <summary>Returns the staff symbol a grob is measured against.</summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The staff symbol, or <see langword="null"/> when it has none.</returns>
    public static Grob GetStaffSymbol(Grob grob)
    {
        // Upstream's identity branch: a staff symbol asked for its own staff symbol
        // answers ITSELF (has_interface<Staff_symbol> (me) -> me). Missing, this
        // made a staff symbol read the 1.0 staff-space fallback instead of its own
        // property. Found by EPG7, fixed centrally 2026-08-07.
        if (grob != null && grob.HasInterface(StaffSymbolInterfaceSymbol))
        {
            return grob;
        }

        return grob?.GetObject(StaffSymbolSymbol) as Grob;
    }

    /// <summary>Determines whether a staff position falls on a staff or ledger line.</summary>
    /// <param name="grob">The grob.</param>
    /// <param name="position">The staff position.</param>
    /// <returns><see langword="true"/> when it is on a line.</returns>
    public static bool OnLine(Grob grob, int position)
    {
        Grob staff = GetStaffSymbol(grob);
        return staff != null && StaffSymbol.OnLine(staff, position);
    }

    /// <summary>Determines whether a staff position falls on a STAFF line specifically.</summary>
    /// <param name="grob">The grob.</param>
    /// <param name="position">The staff position.</param>
    /// <returns><see langword="true"/> when it is on a staff line.</returns>
    public static bool OnStaffLine(Grob grob, int position)
    {
        Grob staff = GetStaffSymbol(grob);
        return staff != null && StaffSymbol.OnLine(staff, position, false);
    }

    /// <summary>
    /// Returns the staff space a grob is measured in, defaulting to one when the grob
    /// belongs to no staff.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The staff space.</returns>
    public static double StaffSpace(Grob grob)
    {
        Grob staff = GetStaffSymbol(grob);
        return staff != null ? StaffSymbol.StaffSpace(staff) : 1.0;
    }

    /// <summary>Returns the line thickness a grob should draw with.</summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The thickness.</returns>
    public static double LineThickness(Grob grob)
    {
        Grob staff = GetStaffSymbol(grob);
        if (staff != null)
        {
            return StaffSymbol.GetLineThickness(staff);
        }

        return grob.Layout == null ? 0.0 : grob.Layout.GetDimension(LineThicknessSymbol);
    }

    /// <summary>
    /// Returns a grob's staff position: its vertical offset from the staff symbol,
    /// measured in half staff-spaces.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The position.</returns>
    public static double GetPosition(Grob grob)
    {
        double p = 0.0;
        Grob staff = GetStaffSymbol(grob);
        Grob common = staff != null ? grob.CommonRefpoint(staff, Axis.Y) : null;

        if (staff != null && common != null)
        {
            double y = grob.RelativeCoordinate(common, Axis.Y)
                       - staff.RelativeCoordinate(common, Axis.Y);
            double space = StaffSymbol.StaffSpace(staff);
            p = space == 0 ? 0 : 2.0 * y / space;
            return p;
        }

        if (staff == null)
        {
            return grob.RelativeCoordinate(grob.GetParent(Axis.Y), Axis.Y) * 2;
        }

        object position = grob.GetProperty(StaffPositionSymbol);
        return SchemeConvert.IsNumber(position)
            ? SchemeConvert.ToDouble(position, "staff-position")
            : p;
    }

    /// <summary>Returns a grob's staff position, rounded to a whole position.</summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The rounded position.</returns>
    public static int GetRoundedPosition(Grob grob) => (int)Math.Round(GetPosition(grob), MidpointRounding.ToEven);

    /// <summary>Returns a grob's vertical extent measured against its staff symbol.</summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The extent, empty when there is no staff symbol.</returns>
    public static Interval ExtentInStaff(Grob grob)
    {
        Grob staff = GetStaffSymbol(grob);
        Grob common = staff != null ? grob.CommonRefpoint(staff, Axis.Y) : null;

        Interval result = Interval.Empty;
        if (staff != null && common != null)
        {
            result = grob.Extent(common, Axis.Y) - staff.RelativeCoordinate(common, Axis.Y);
        }

        return result;
    }

    /// <summary>
    /// The <c>Y-offset</c> callback: turns a <c>staff-position</c> in half staff-spaces
    /// into a vertical offset.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The offset.</returns>
    public static double Callback(Grob grob)
    {
        object position = grob.GetProperty(StaffPositionSymbol);
        double offset = 0.0;

        if (SchemeConvert.IsNumber(position))
        {
            double space = StaffSpace(grob);
            offset = SchemeConvert.ToDouble(position, "staff-position") * space / 2.0;
        }

        return offset;
    }

    /// <summary>
    /// <c>pure_get_position</c>. The port has no pure-property machinery yet
    /// (<c>unpure-pure-container.cc</c>, EPG15), so this answers the ORDINARY position —
    /// the same standing divergence every pure variant takes today.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The staff position.</returns>
    public static double PureGetPosition(Grob grob) => GetPosition(grob);

    /// <summary><c>pure_get_rounded_position</c>.</summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The rounded staff position.</returns>
    public static int PureGetRoundedPosition(Grob grob)
        => (int)Math.Round(PureGetPosition(grob), MidpointRounding.ToEven);

    /// <summary><c>set_position</c>.</summary>
    /// <param name="grob">The grob.</param>
    /// <param name="p">The staff position to move it to.</param>
    public static void SetPosition(Grob grob, double p) => InternalSetPosition(grob, p, false);

    /// <summary><c>pure_set_position</c>.</summary>
    /// <param name="grob">The grob.</param>
    /// <param name="p">The staff position to move it to.</param>
    public static void PureSetPosition(Grob grob, double p) => InternalSetPosition(grob, p, true);

    /// <summary><c>staff_span</c>: the staff symbol's line span, empty when there is none.</summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The span.</returns>
    public static Interval StaffSpan(Grob grob)
    {
        Interval result = Interval.Empty;
        if (grob != null)
        {
            Grob symbol = GetStaffSymbol(grob);
            if (symbol != null)
            {
                result = StaffSymbol.LineSpan(symbol);
            }
        }

        return result;
    }

    /// <summary>
    /// <c>staff_radius</c>: half the staff's line span, in STAFF SPACES.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The radius.</returns>
    /// <remarks>
    /// The divisor is 4, not 2, and that is not a typo of upstream's: the line span is
    /// measured in PITCH STEPS, of which there are two to the staff space, so halving the
    /// span and converting steps to spaces divides by four in one step. Upstream carries
    /// the same comment for the same reason.
    /// <para>
    /// Added by EPG23 with <c>ly:staff-symbol-staff-radius</c>. The algorithm was already
    /// in the engine as <c>Stem.StaffRadius</c> — a private copy in the wrong file, which
    /// now delegates here, because two implementations of one upstream function is the
    /// shape standing trap 11 records (a fix applied to one copy never reaches the other).
    /// </para>
    /// </remarks>
    public static double StaffRadius(Grob grob) => StaffSpan(grob).Length / 4.0;

    /// <summary><c>pure_position_less</c>: orders grobs by pure staff position.</summary>
    /// <param name="a">The first grob.</param>
    /// <param name="b">The second grob.</param>
    /// <returns><see langword="true"/> when <paramref name="a"/> sits lower.</returns>
    public static bool PurePositionLess(Grob a, Grob b)
        => PureGetPosition(a) < PureGetPosition(b);

    /*  This sets the position relative to the center of the staff symbol.

    The function is hairy, because it can be called in two situations:

    1. There is no staff yet; we must set staff-position

    2. There is a staff, and perhaps someone even applied a
    translate_axis (). Then we must compensate for the translation

    In either case, we set a callback to be sure that our new position
    will be extracted from staff-position */
    private static void InternalSetPosition(Grob grob, double p, bool pure)
    {
        Grob st = GetStaffSymbol(grob);
        double oldpos = 0.0;
        if (st != null && grob.CommonRefpoint(st, Axis.Y) != null)
        {
            oldpos = pure ? PureGetPosition(grob) : GetPosition(grob);
        }

        double ss = StaffSpace(grob);
        grob.TranslateAxis((p - oldpos) * ss * 0.5, Axis.Y);
    }
}
