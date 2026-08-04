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
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");

    /// <summary>Returns the staff symbol a grob is measured against.</summary>
    /// <param name="grob">The grob.</param>
    /// <returns>The staff symbol, or <see langword="null"/> when it has none.</returns>
    public static Grob GetStaffSymbol(Grob grob) => grob?.GetObject(StaffSymbolSymbol) as Grob;

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
}
