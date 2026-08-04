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

using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/item.cc, lily/include/item.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/**
   A horizontally fixed size element of the score.

   Item is the datastructure for printables whose width is known
   before the spacing is calculated
*/

/// <summary>
/// A grob whose width is known before spacing is decided: a note head, an accidental,
/// a clef, a bar line.
/// <para>
/// Items that are <c>non-musical</c> — clefs, key signatures, bar lines — get
/// PREBROKEN: a copy is made for each side of a potential line break, so the engine
/// can decide later which one to use. <see cref="BreakStatusDirection"/> tells a copy
/// which side it is.
/// </para>
/// </summary>
public class Item : Grob
{
    private static readonly Symbol ItemInterface = Symbol.Intern("item-interface");
    private static readonly Symbol NonMusicalSymbol = Symbol.Intern("non-musical");

    private DrulArray<Item> _brokenToDrul;

    /// <summary>Initializes an item from its type's basic property alist.</summary>
    /// <param name="basicProperties">The immutable alist for this grob type.</param>
    public Item(object basicProperties)
        : base(basicProperties)
    {
        _brokenToDrul = new DrulArray<Item>(null, null);
        CachedPureHeightValid = false;
        AddInterface(ItemInterface);
    }

    /**
       Item copy ctor.  Copy nothing: everything should be an elt property
       or a special purpose pointer (such as broken_to_drul_[]) */

    /// <summary>Initializes a copy of another item.</summary>
    /// <param name="source">The item to copy.</param>
    protected Item(Item source)
        : base(source)
    {
        _brokenToDrul = new DrulArray<Item>(null, null);
        CachedPureHeightValid = false;
    }

    /// <summary>Gets the C++ class name this grob corresponds to.</summary>
    public override string ClassName => "Item";

    /// <summary>Gets the item this one was broken off from.</summary>
    public new Item Original => (Item)base.Original;

    /// <summary>Gets or sets a value indicating whether the cached pure height is usable.</summary>
    protected bool CachedPureHeightValid { get; set; }

    /// <summary>Gets or sets the cached pure height.</summary>
    protected Interval CachedPureHeight { get; set; }

    /// <summary>Gets a value indicating whether this item has been prebroken.</summary>
    public bool IsBroken => _brokenToDrul.Negative != null || _brokenToDrul.Positive != null;

    /// <summary>Returns an independent copy of this item.</summary>
    /// <returns>The clone.</returns>
    public override Grob Clone() => new Item(this);

    /// <summary>
    /// Returns the prebroken copy for one side, or this item itself for the centre.
    /// </summary>
    /// <param name="direction">The side to select.</param>
    /// <returns>The piece, or <see langword="null"/> when there is none.</returns>
    public Item FindPrebrokenPiece(Direction direction)
        => !direction.IsNonZero ? this : _brokenToDrul[direction];

    /// <summary>Records the prebroken copy for one side.</summary>
    /// <param name="direction">The side.</param>
    /// <param name="piece">The copy.</param>
    public void SetPrebrokenPiece(Direction direction, Item piece)
        => _brokenToDrul[direction] = piece;

    /// <summary>
    /// Returns which side of a break this item is on: negative for the copy at the end
    /// of a line, positive for the one at the start of the next, centre for an
    /// unbroken original.
    /// </summary>
    /// <returns>The break status direction.</returns>
    public Direction BreakStatusDirection()
    {
        Item original = Original;
        if (original != null)
        {
            return ReferenceEquals(original._brokenToDrul.Negative, this)
                ? Direction.Negative
                : Direction.Positive;
        }

        return Direction.Center;
    }

    /// <summary>
    /// Determines whether a grob is non-musical, meaning it may be duplicated at a
    /// line break. The answer is inherited from the horizontal parent when there is
    /// one.
    /// </summary>
    /// <param name="grob">The grob to test.</param>
    /// <returns><see langword="true"/> when the grob is non-musical.</returns>
    public static bool IsNonMusical(Grob grob)
    {
        if (grob == null)
        {
            return false;
        }

        if (grob.GetParent(Axis.X) is Item parent)
        {
            return IsNonMusical(parent);
        }

        return SchemeUtilities.ToBool(grob.GetProperty(NonMusicalSymbol));
    }
}
