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

using System.Numerics;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/directional-element-interface.cc, lily/include/directional-element-interface.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// Reads and writes a grob's <c>direction</c> property.
/// <para>
/// Upstream these are free functions (<c>get_grob_direction</c> and friends); the port
/// hosts them on a static class named after the grob interface they serve. The Scheme
/// direction conversions live here too, standing in for
/// <c>scm_conversions&lt;Direction&gt;</c> in <c>lily-guile.hh</c>: a Scheme value IS a
/// direction exactly when it is an exact integer between -1 and 1, so <c>1.0</c> — a
/// real — is NOT one, and neither is an unset property's <c>'()</c>.
/// </para>
/// </summary>
public static class DirectionalElementInterface
{
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");

    /// <summary>Determines whether a Scheme value is a direction.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for an exact integer in [-1, 1].</returns>
    public static bool IsDirection(object value)
    {
        switch (value)
        {
            case long l:
                return l >= -1 && l <= 1;
            case int i:
                return i >= -1 && i <= 1;
            case BigInteger b:
                return b >= -1 && b <= 1;
            default:
                return false;
        }
    }

    /// <summary>Reads a Scheme value as a direction, with a fallback.</summary>
    /// <param name="value">The value to read.</param>
    /// <param name="fallback">The direction to answer when the value is not one.</param>
    /// <returns>The direction.</returns>
    public static Direction FromScheme(object value, Direction fallback)
    {
        switch (value)
        {
            case long l when l >= -1 && l <= 1:
                return new Direction(l);
            case int i when i >= -1 && i <= 1:
                return new Direction((long)i);
            case BigInteger b when b >= -1 && b <= 1:
                return new Direction((long)b);
            default:
                return fallback;
        }
    }

    private static Direction InternalGetGrobDirection(Grob me, bool strict)
    {
        object d = me.GetProperty(DirectionSymbol);
        Direction dir = FromScheme(d, Direction.Center);
        if (strict && dir == Direction.Center)
        {
            // Upstream reports this through me->warning (), so it carries the grob's
            // origin. Reporting it bare -- as this did until EPG22 -- names no file and
            // no bar, which is the difference between a usable diagnostic and noise.
            me.Warning(
                "direction of grob " + me.Name + " must be UP or DOWN; using UP");
            SetGrobDirection(me, Direction.Positive);
            return Direction.Positive;
        }

        return FromScheme(d, Direction.Center);
    }

    /// <summary>Returns a grob's direction, with centre for "not decided".</summary>
    /// <param name="me">The grob.</param>
    /// <returns>The direction.</returns>
    public static Direction GetGrobDirection(Grob me)
        => InternalGetGrobDirection(me, false);

    /*
      Use this function when your call site cannot sensibly continue
      with CENTER as a direction (e.g., when using the result as an
      index to a Drul_array), to avoid crashes.  Stay with get_grob_direction
      for grobs for which CENTER is a meaningful direction and the
      absence of an explicitly set direction should be interpreted
      like that.
    */

    /// <summary>
    /// Returns a grob's direction, warning and substituting UP when it is centre.
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <returns>UP or DOWN, never centre.</returns>
    public static Direction GetStrictGrobDirection(Grob me)
        => InternalGetGrobDirection(me, true);

    /// <summary>Writes a grob's direction.</summary>
    /// <param name="me">The grob.</param>
    /// <param name="direction">The direction to store.</param>
    public static void SetGrobDirection(Grob me, Direction direction)
        => me.SetProperty(DirectionSymbol, (long)(int)direction);
}
