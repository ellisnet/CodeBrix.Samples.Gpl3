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

using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/rod.cc, lily/include/rod.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// A hard minimum distance between two items, on its way to becoming a constraint
/// between their columns.
/// <para>
/// A rod is stated between ITEMS, because that is where the collision is, but the
/// spacing problem is solved between COLUMNS. <see cref="AddToColumns"/> is the
/// conversion: it walks each end out to its column and folds the item's offset within
/// that column into the distance, so the constraint means the same thing after the
/// change of reference point.
/// </para>
/// </summary>
public struct Rod
{
    /// <summary>The two items the rod runs between.</summary>
    public DrulArray<Item> ItemDrul;

    /// <summary>The minimum distance between them.</summary>
    public double Distance;

    /// <summary>Initializes an empty rod.</summary>
    /// <param name="left">The left item.</param>
    /// <param name="right">The right item.</param>
    public Rod(Item left, Item right)
    {
        ItemDrul = new DrulArray<Item>(left, right);
        Distance = 0.0;
    }

    /// <summary>
    /// Converts the rod to a constraint between the two items' columns and records it
    /// on the left column.
    /// <para>
    /// A rod whose ends land in the SAME column states nothing about the spacing
    /// problem and is dropped, which is also what keeps a column from constraining
    /// itself.
    /// </para>
    /// </summary>
    public void AddToColumns()
    {
        if (ItemDrul[Direction.Negative] == null || ItemDrul[Direction.Positive] == null)
        {
            return;
        }

        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            Item item = ItemDrul[d];
            PaperColumn pc = item.GetColumn();
            if (pc == null)
            {
                return;
            }

            Distance += -d * item.RelativeCoordinate(pc, Axis.X);
            ItemDrul[d] = pc;
        }

        Item left = ItemDrul[Direction.Negative];
        Item right = ItemDrul[Direction.Positive];

        if (!ReferenceEquals(left, right) && left != null && right != null)
        {
            // The casts are safe: both ends were replaced by columns just above.
            SpaceableGrob.AddRod((PaperColumn)left, (PaperColumn)right, Distance);
        }
    }

    /// <summary>
    /// Returns how far the two bounding items protrude INTO the rod — the part of the
    /// distance already accounted for by their own widths.
    /// </summary>
    /// <returns>The protrusion.</returns>
    public double BoundsProtrusion()
    {
        // Return the distance that bounds protrude into rod
        double w = 0;
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            Item item = ItemDrul[d];
            if (item != null)
            {
                w += -d * item.Extent(item, Axis.X)[-d];
            }
        }

        return w;
    }
}
