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

using System.Collections.Generic;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/spanner.cc, lily/include/spanner.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/** A symbol which is attached between two columns. A spanner is a
    symbol which spans across several columns, so its final appearance
    can only be calculated after the breaking problem is solved.

    Examples

    * (de)crescendo
    * slur
    * beam
    * bracket

    Spanner should know about the items which it should consider:
    e.g. slurs should be steep enough to "enclose" all those items. This
    is absolutely necessary for beams, since they have to adjust the
    length of stems of notes they encompass.
*/

/// <summary>
/// A grob attached between two points: a slur, a beam, a hairpin, a staff symbol.
/// <para>
/// Unlike an <see cref="Item"/>, a spanner's final appearance cannot be known until
/// line breaking is solved, because it may be split across systems. Its two ends are
/// held as <see cref="GetBound"/> items, and the pieces it is broken into are kept in
/// <see cref="BrokenIntos"/>.
/// </para>
/// </summary>
public class Spanner : Grob
{
    private static readonly Symbol SpannerInterface = Symbol.Intern("spanner-interface");

    private DrulArray<Item> _spannedDrul;

    /// <summary>Initializes a spanner from its type's basic property alist.</summary>
    /// <param name="basicProperties">The immutable alist for this grob type.</param>
    public Spanner(object basicProperties)
        : base(basicProperties)
    {
        _spannedDrul = new DrulArray<Item>(null, null);
        BreakIndex = 0;
        AddInterface(SpannerInterface);
    }

    /// <summary>Initializes a copy of another spanner.</summary>
    /// <param name="source">The spanner to copy.</param>
    protected Spanner(Spanner source)
        : base(source)
    {
        _spannedDrul = new DrulArray<Item>(null, null);
        BreakIndex = 0;
    }

    /// <summary>Gets the C++ class name this grob corresponds to.</summary>
    public override string ClassName => "Spanner";

    /// <summary>Gets the spanner this one was broken off from.</summary>
    public new Spanner Original => (Spanner)base.Original;

    /// <summary>Gets the pieces this spanner was broken into, one per system.</summary>
    public List<Spanner> BrokenIntos { get; } = new List<Spanner>();

    /// <summary>Gets or sets which broken piece this is, counting from the first.</summary>
    public int BreakIndex { get; set; }

    /// <summary>Gets a value indicating whether this spanner has been broken.</summary>
    public bool IsBroken => BrokenIntos.Count > 0;

    /// <summary>Returns an independent copy of this spanner.</summary>
    /// <returns>The clone.</returns>
    public override Grob Clone() => new Spanner(this);

    /// <summary>Returns the item at one end of the spanner.</summary>
    /// <param name="direction">Which end.</param>
    /// <returns>The bound item, or <see langword="null"/> when unset.</returns>
    public Item GetBound(Direction direction) => _spannedDrul[direction];

    /// <summary>Gets both bounds at once.</summary>
    /// <returns>The two bounds.</returns>
    public DrulArray<Item> GetBounds() => _spannedDrul;

    /// <summary>
    /// Returns the system this spanner lies on: the one BOTH its bounds are on.
    /// <para>
    /// A spanner whose ends landed on different lines has no single system — that is
    /// the state a spanner is in until it has been broken into its pieces — and the
    /// answer is <see langword="null"/> rather than either end's.
    /// </para>
    /// </summary>
    /// <returns>The system, or <see langword="null"/>.</returns>
    public override SystemGrob GetSystem()
    {
        Item left = GetBound(Direction.Negative);
        Item right = GetBound(Direction.Positive);
        if (left == null || right == null)
        {
            return null;
        }

        SystemGrob system = left.GetSystem();
        return system != null && ReferenceEquals(system, right.GetSystem()) ? system : null;
    }

    /// <summary>
    /// Attaches one end of the spanner to an item, and normally makes the LEFT bound
    /// the spanner's horizontal reference point.
    /// <para>
    /// Two exceptions, and both are load bearing. A <see cref="SystemGrob"/> never
    /// takes its bound as parent — a system's left bound is one of its own columns, and
    /// a column's horizontal parent is the system, so taking it would close a cycle
    /// that hangs the first walk up the parent chain. And a spanner whose horizontal
    /// parent is already another SPANNER keeps it: that parent is split at a line break
    /// too, and the original is what later alignment measures against.
    /// </para>
    /// </summary>
    /// <param name="direction">Which end.</param>
    /// <param name="grob">The item to attach to.</param>
    public virtual void SetBound(Direction direction, Grob grob)
    {
        if (!(grob is Item item))
        {
            Warn.ProgrammingError("must have Item for spanner bound of " + Name);
            return;
        }

        _spannedDrul[direction] = item;

        /*
          We check for System to prevent the column -> line_of_score
          -> column -> line_of_score -> etc situation
        */
        if (direction == Direction.Negative && !(this is SystemGrob))
        {
            /*
              If the X-parent is a spanner, it will be split across linebreaks, too,
              so we shouldn't have to overwrite it with the bound. Also, we need
              original parent for alignment.
              This happens e.g. for MultiMeasureRestNumbers and PercentRepeatCounters.
            */
            if (!(GetParent(Axis.X) is Spanner))
            {
                SetParent(item, Axis.X);
            }
        }
    }

    /// <summary>
    /// Returns the neighbouring broken piece on one side, or <see langword="null"/>
    /// when this piece is at the end.
    /// </summary>
    /// <param name="direction">Which side.</param>
    /// <returns>The neighbouring piece.</returns>
    public Spanner BrokenNeighbor(Direction direction)
    {
        Spanner original = Original;
        if (original == null)
        {
            return null;
        }

        int index = BreakIndex + direction.Value;
        if (index < 0 || index >= original.BrokenIntos.Count)
        {
            return null;
        }

        return original.BrokenIntos[index];
    }

    /// <summary>
    /// Returns the spanner's horizontal length: the distance between its two bounds.
    /// </summary>
    /// <returns>The length.</returns>
    public double SpannerLength()
    {
        Interval bounds = Interval.Empty;
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            Item bound = GetBound(d);
            if (bound != null)
            {
                bounds[d] = bound.RelativeCoordinate(bound.GetParent(Axis.X), Axis.X);
            }
        }

        if (bounds.IsEmpty)
        {
            Warn.ProgrammingError("spanner with no bounds");
            return 0.0;
        }

        return bounds.Length;
    }
}
