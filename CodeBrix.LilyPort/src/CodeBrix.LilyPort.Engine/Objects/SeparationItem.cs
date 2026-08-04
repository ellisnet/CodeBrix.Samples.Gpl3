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

using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/separation-item.cc, lily/include/separation-item.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// Collects the items a paper column has to keep clear of its neighbours.
/// <para>
/// A column's horizontal extent and its horizontal skylines are both computed from
/// this set, which is why an item that never reaches it costs the column all of its
/// width — the column then looks empty to the spacing pipeline.
/// </para>
/// <para>
/// PARTIAL PORT, recorded in PORT-COVERAGE. <see cref="AddItem"/> is complete.
/// <c>calc_skylines</c> and <c>add_conditional_item</c> are NOT ported: the skyline
/// half needs the conditional-item merge that <c>Paper_column::minimum_distance</c>
/// also omits, and the two belong together.
/// </para>
/// </summary>
public static class SeparationItem
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");

    /// <summary>Records an item as something the column must make room for.</summary>
    /// <param name="column">The column.</param>
    /// <param name="item">The item.</param>
    public static void AddItem(Grob column, Item item)
    {
        if (column == null || item == null)
        {
            return;
        }

        PointerGroupInterface.AddGrob(column, ElementsSymbol, item);
    }
}
