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

using System.Globalization;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/include/dimensions.hh, lily/dimensions.cc;

// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port.

/// <summary>
/// The unit conversions LilyPond measures layout in.
/// <para>
/// LilyPond's internal unit is the millimetre, so each of these constants answers "how
/// many internal units is one of this unit". Note that an inch is 72.27 points here, the
/// TeX point, not the 72-point PostScript big point -- both exist and they are not the
/// same, which is exactly why <c>bp</c> has its own constant.
/// </para>
/// </summary>
public static class Dimensions
{
    /// <summary>Points per inch, using the TeX point.</summary>
    public const double InchToPoint = 72.270;

    /// <summary>Points per centimetre.</summary>
    public const double CentimetreToPoint = InchToPoint / 2.54;

    /// <summary>Points per millimetre.</summary>
    public const double MillimetreToPoint = CentimetreToPoint / 10;

    /// <summary>Big points per inch, using the PostScript point.</summary>
    public const double InchToBigPoint = 72;

    /// <summary>Points per big point.</summary>
    public const double BigPointToPoint = InchToPoint / InchToBigPoint;

    /// <summary>Millimetres per point.</summary>
    public const double PointToMillimetre = 1.0 / MillimetreToPoint;

    /// <summary>One point, in internal units.</summary>
    public const double Point = 1.0 * PointToMillimetre;

    /// <summary>One millimetre, in internal units.</summary>
    public const double Millimetre = MillimetreToPoint * PointToMillimetre;

    /// <summary>One centimetre, in internal units.</summary>
    public const double Centimetre = CentimetreToPoint * PointToMillimetre;

    /// <summary>One inch, in internal units.</summary>
    public const double Inch = InchToPoint * PointToMillimetre;

    /// <summary>One big point, in internal units.</summary>
    public const double BigPoint = BigPointToPoint * PointToMillimetre;

    /// <summary>One character width, in internal units.</summary>
    public const double Character = 1.0 * PointToMillimetre;

    /// <summary>Formats a dimension the way LilyPond writes one, in staff spaces.</summary>
    /// <param name="value">The dimension in internal units.</param>
    /// <returns>The formatted dimension.</returns>
    public static string Print(double value)
        => (value / Millimetre).ToString("0.0000", CultureInfo.InvariantCulture) + "\\mm";
}
