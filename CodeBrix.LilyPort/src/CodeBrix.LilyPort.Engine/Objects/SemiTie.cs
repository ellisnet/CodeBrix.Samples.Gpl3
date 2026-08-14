/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/semi-tie.cc, lily/include/semi-tie.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A tie attached to a note head on ONE side only — a laissez-vibrer tie hanging off the
/// right of a head, or a repeat tie arriving at its left.
/// </summary>
public static class SemiTie
{
    private static readonly Symbol NoteHeadSymbol = Symbol.Intern("note-head");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol ControlPointsSymbol = Symbol.Intern("control-points");
    private static readonly Symbol SemiTieColumnInterface
        = Symbol.Intern("semi-tie-column-interface");

    /// <summary>The <c>control-points</c> callback: defers to the semi-tie's column.</summary>
    /// <param name="me">The semi-tie.</param>
    /// <returns>The control points the column computed.</returns>
    public static object CalcControlPoints(Item me)
    {
        me.GetProperty(DirectionSymbol);

        Grob yparent = me.YParent;
        if (yparent != null && yparent.HasInterface(SemiTieColumnInterface))
        {
            // trigger positioning.
            yparent.GetProperty(PositioningDoneSymbol);

            return me.GetPropertyData(ControlPointsSymbol);
        }

        Warn.ProgrammingError("lv tie without Semi_tie_column.  Killing lv tie.");
        me.Suicide();
        return Nil.Instance;
    }

    /// <summary>Returns the paper-column rank the semi-tie sits at.</summary>
    /// <param name="me">The semi-tie.</param>
    /// <returns>The rank.</returns>
    public static int GetColumnRank(Item me) => me.GetColumn().Rank;

    /// <summary>Returns the staff position of the semi-tie's note head.</summary>
    /// <param name="me">The semi-tie.</param>
    /// <returns>The staff position.</returns>
    public static int GetPosition(Item me)
        => (int)Math.Round(
            StaffSymbolReferencer.GetPosition(Head(me)), MidpointRounding.ToEven);

    /// <summary>Returns the note head the semi-tie hangs off.</summary>
    /// <param name="me">The semi-tie.</param>
    /// <returns>The note head, or <see langword="null"/>.</returns>
    public static Item Head(Item me) => me.GetObject(NoteHeadSymbol) as Item;

    /// <summary>Orders two semi-ties by staff position.</summary>
    /// <param name="a">The first semi-tie.</param>
    /// <param name="b">The second semi-tie.</param>
    /// <returns><see langword="true"/> when the first sits lower.</returns>
    public static bool Less(Item a, Item b) => GetPosition(a) < GetPosition(b);
}
