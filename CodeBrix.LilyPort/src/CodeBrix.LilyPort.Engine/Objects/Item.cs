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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/item.cc, lily/include/item.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - spanned_time_interval added; it is upstream's own free function in this file and
//     had never been carried.

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
    private static readonly Symbol BreakVisibilitySymbol = Symbol.Intern("break-visibility");
    private static readonly Symbol WhenSymbol = Symbol.Intern("when");

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

    /// <summary>
    /// The item's pure vertical extent, CACHED after the first ask.
    /// </summary>
    /// <remarks>
    /// <para>
    /// //was previously: inherited from <see cref="Grob"/>, so every pure enquiry
    /// recomputed. Upstream overrides it on Item (<c>item.cc:242</c>) and stores the
    /// answer in <c>cached_pure_height_</c>; the port carried the two cache fields and
    /// invalidated them in both constructors, but nothing ever read them — the write
    /// side of the handshake without its reader (trap 17a).
    /// </para>
    /// <para>
    /// THE CACHE IS PART OF THE SPECIFICATION, NOT AN OPTIMISATION (trap 1c). The FIRST
    /// reader freezes the value, so a property the item's extent depends on that is
    /// rewritten LATER cannot change what an earlier reader saw. That is load-bearing:
    /// <c>Note_collision</c>'s <c>fa</c> merge rewrites both heads'
    /// <c>stem-attachment</c> during <c>positioning-done</c>, and horizontal spacing —
    /// which asks for the stem's pure height through
    /// <c>Note_spacing::stem_dir_correction</c> — must go on seeing the value it read
    /// before that. Without the cache the port's spacing followed
    /// <c>fa-merge-direction</c> and the oracle's did not.
    /// </para>
    /// <para>
    /// Upstream's own comment on the range: "cached_pure_height_ does not notice if
    /// start changes, implicitly assuming that Items' pure_heights do not depend on
    /// 'start' or 'end'." That assumption is reproduced here rather than corrected.
    /// </para>
    /// </remarks>
    /// <param name="refp">The reference grob.</param>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The pure extent, in <paramref name="refp"/>'s frame.</returns>
    public override Interval PureYExtent(Grob refp, int start, int end)
    {
        if (!CachedPureHeightValid)
        {
            // Measured against THIS item, so the stored interval carries no offset and
            // can be re-based for any later reference point.
            CachePureHeight(base.PureYExtent(this, start, end));
        }

        Interval result = CachedPureHeight;
        result.Translate(PureRelativeYCoordinate(refp, start, end));
        return result;
    }

    /// <summary>Stores an item's pure height, from now on answered without recomputing.</summary>
    /// <param name="height">The extent, in the item's own frame.</param>
    public virtual void CachePureHeight(Interval height)
    {
        CachedPureHeight = height;
        CachedPureHeightValid = true;
    }

    /// <summary>Gets a value indicating whether this item has been prebroken.</summary>
    public bool IsBroken => _brokenToDrul.Negative != null || _brokenToDrul.Positive != null;

    /// <summary>Returns an independent copy of this item.</summary>
    /// <returns>The clone.</returns>
    public override Grob Clone() => new Item(this);

    /// <summary>
    /// Takes the object links belonging to this side of the break, then kills this piece
    /// when <c>break-visibility</c> says it should not be seen on this side.
    /// </summary>
    public override void HandlePrebrokenDependencies()
    {
        base.HandlePrebrokenDependencies();
        if (!BreakVisible())
        {
            Suicide();
        }
    }

    /// <summary>
    /// Determines whether this item is visible on its side of the break, from the
    /// <c>break-visibility</c> vector.
    /// </summary>
    /// <returns><see langword="true"/> when visible, and when nothing said otherwise.</returns>
    public bool BreakVisible()
    {
        if (GetProperty(BreakVisibilitySymbol) is object[] vis)
        {
            int index = BreakStatusDirection().ToIndex;
            return index >= 0 && index < vis.Length && SchemeUtilities.ToBool(vis[index]);
        }

        return true;
    }

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
    /// Accepts being made a bound of <paramref name="spanner"/> — an ITEM asks the
    /// spanner's item overload.
    /// </summary>
    /// <param name="spanner">The spanner asking.</param>
    /// <param name="direction">Which end of the spanner.</param>
    /// <returns><see langword="true"/> when the spanner takes items as bounds.</returns>
    public override bool InternalSetAsBoundOfSpanner(Spanner spanner, Direction direction)
        => spanner.AcceptsAsBoundItem(this);

    /// <summary>
    /// Finds the piece of this item that lives on a given system —
    /// <c>Item::find_broken_piece</c>.
    /// </summary>
    /// <param name="system">The system to look on.</param>
    /// <returns>The piece, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// An item has at most the two prebroken copies to offer, so the search is this item
    /// and then its drul pair. Added with scripts/dynamics — see <see cref="Grob"/>.
    /// </remarks>
    public override Grob FindBrokenPiece(SystemGrob system)
    {
        if (ReferenceEquals(GetSystem(), system))
        {
            return this;
        }

        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            Item s = _brokenToDrul[d];
            if (s != null && ReferenceEquals(s.GetSystem(), system))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the paper column this item hangs off, by walking the horizontal parent
    /// chain. A column answers itself.
    /// </summary>
    /// <returns>The column, or <see langword="null"/> when the item has no column.</returns>
    public virtual PaperColumn GetColumn()
        => GetParent(Axis.X) is Item parent ? parent.GetColumn() : null;

    /// <summary>
    /// The single paper-column rank this item sits at —
    /// <c>Item::spanned_column_rank_interval</c>.
    /// </summary>
    /// <returns>The rank range, empty when the item has no column.</returns>
    public override Slice SpannedColumnRankInterval()
    {
        // An Item "always" has a column, but it is possible for a grob to be killed
        // before its parents are set, so we have to be careful.
        PaperColumn col = GetColumn();
        if (col != null)
        {
            int c = col.Rank;
            return new Slice(c, c);
        }

        return Slice.Empty;
    }

    /// <summary>
    /// The range of SYSTEM ranks this item is alive on —
    /// <c>Item::spanned_system_rank_interval</c>.
    /// <para>
    /// An item that has landed on a system answers that system's rank twice. One that has
    /// not — which is every breakable item before line breaking, since the prebreak pass
    /// runs first — answers the range its two PREBROKEN PIECES landed on, because those
    /// are the copies that will actually appear.
    /// </para>
    /// </summary>
    /// <returns>The system-rank range, empty when nothing has been placed.</returns>
    public override Slice SpannedSystemRankInterval()
    {
        SystemGrob st = GetSystem();
        if (st != null)
        {
            return new Slice(st.Rank, st.Rank);
        }

        Slice sr = Slice.Empty;
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            Item bi = FindPrebrokenPiece(d);
            if (bi != null && bi.GetSystem() != null)
            {
                sr.AddPoint(bi.GetSystem().Rank);
            }
        }

        return sr;
    }

    /// <summary>
    /// Gets the piece of this item relevant to a line running from
    /// <paramref name="start"/> to <paramref name="end"/> —
    /// <c>Item::pure_find_visible_prebroken_piece</c>.
    /// <para>
    /// An item at the line's left edge answers its RIGHT prebroken piece and one at the
    /// right edge its LEFT piece — the piece facing into the line, which is the copy that
    /// would actually be drawn there. An item anywhere else answers itself. In every case
    /// the answer is dropped if it is not break-visible.
    /// </para>
    /// </summary>
    /// <param name="start">The starting column rank.</param>
    /// <param name="end">The ending column rank.</param>
    /// <returns>The relevant piece, or <see langword="null"/>.</returns>
    public override Grob PureFindVisiblePrebrokenPiece(int start, int end)
    {
        Item it = this;

        // An Item "always" has a column, but it is possible for a grob to be killed
        // before its parents are set, so we have to be careful.
        PaperColumn col = GetColumn();
        if (col == null)
        {
            return null;
        }

        int rank = col.Rank;
        if (rank == start)
        {
            it = FindPrebrokenPiece(Direction.Positive);
        }
        else if (rank == end)
        {
            it = FindPrebrokenPiece(Direction.Negative);
        }

        return it != null && it.BreakVisible() ? it : null;
    }

    /// <summary>
    /// Returns the system this item ended up on, which is the answer its column gives
    /// and is therefore <see langword="null"/> until line breaking has run.
    /// </summary>
    /// <returns>The system, or <see langword="null"/>.</returns>
    public override SystemGrob GetSystem()
    {
        Grob parent = GetParent(Axis.X);
        return parent != null ? parent.GetSystem() : null;
    }

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

    /// <summary>
    /// Returns the span of musical time between two items, read off the <c>when</c> of
    /// each one's paper column.
    /// <para>
    /// An end with no item, or an item with no column, collapses onto the other end
    /// rather than leaving the interval empty — so the answer is always a real span, and
    /// a zero-length one means the two ends sit at the same moment.
    /// </para>
    /// <para>Upstream: the free function <c>spanned_time_interval</c> in
    /// <c>lily/item.cc</c>. It was never carried when that file was ported; the lyrics group's
    /// <c>Vowel_transition</c> is its first caller.</para>
    /// </summary>
    /// <param name="left">The earlier item, which may be null.</param>
    /// <param name="right">The later item, which may be null.</param>
    /// <returns>The spanned time.</returns>
    public static MomentInterval SpannedTimeInterval(Item left, Item right)
    {
        // A default-constructed interval reads back as [+infinity, -infinity], which is
        // the sentinel each end below falls back to when there is no `when' to read.
        MomentInterval iv = new MomentInterval();

        if (left != null && left.GetColumn() != null)
        {
            iv.Left = left.GetColumn().GetProperty(WhenSymbol) is Moment when ? when : iv.Left;
        }

        if (right != null && right.GetColumn() != null)
        {
            iv.Right = right.GetColumn().GetProperty(WhenSymbol) is Moment when ? when : iv.Right;
        }

        // An end that contributed nothing collapses onto the other one. Upstream runs
        // this as a second LEFT-then-RIGHT loop over the interval it is updating, so when
        // BOTH ends are missing the right end collapses onto the already-collapsed left
        // one; the two statements below are in that same order and do the same thing.
        if (left == null || left.GetColumn() == null)
        {
            iv.Left = iv.Right;
        }

        if (right == null || right.GetColumn() == null)
        {
            iv.Right = iv.Left;
        }

        return iv;
    }
}
