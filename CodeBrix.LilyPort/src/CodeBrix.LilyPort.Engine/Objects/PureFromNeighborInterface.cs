/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2011--2026 Mike Solomon <mike@mikesolomon.org>

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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/pure-from-neighbor-interface.cc, lily/include/pure-from-neighbor-interface.hh;

/// <summary>
/// Lets a grob take its height, pure and otherwise, from the grobs beside it.
/// <para>
/// The neighbours are filled in by the <c>Pure_from_neighbor_engraver</c>; this interface
/// is the read side, and its one callback turns that raw list into the ordered
/// pure-relevant set the axis-group machinery expects.
/// </para>
/// </summary>
public static class PureFromNeighborInterface
{
    private static readonly Symbol NeighborsSymbol = Symbol.Intern("neighbors");

    /// <summary>
    /// Builds the pure-relevant grob list out of this grob's neighbours —
    /// <c>ly:pure-from-neighbor-interface::calc-pure-relevant-grobs</c>.
    /// <para>
    /// It reads the neighbours off the ORIGINAL when there is a live one, because the
    /// engraver attached them to the unbroken grob and a prebroken clone inherits none.
    /// It then writes that list back onto this grob before delegating, so the clone has
    /// its own copy to be measured through.
    /// </para>
    /// </summary>
    /// <param name="me">The grob.</param>
    /// <returns>The pure-relevant grobs.</returns>
    public static object CalcPureRelevantGrobs(Grob me)
    {
        Grob source = me.Original != null && me.Original.IsLive ? me.Original : me;
        IReadOnlyList<Grob> elts = PointerGroupInterface.ExtractGrobSet(source, NeighborsSymbol);

        List<Grob> newElts = new List<Grob>(elts);

        if (me.GetObject(NeighborsSymbol) is GrobArray a)
        {
            a.SetArray(newElts);
        }

        return AxisGroupInterfacePure.InternalCalcPureRelevantGrobs(me, NeighborsSymbol);
    }
}
