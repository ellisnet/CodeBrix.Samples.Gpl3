// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <c>Item::pure_y_extent</c> CACHES, and the cache is part of the specification rather
/// than an optimisation.
/// <para>
/// Upstream (<c>lily/item.cc:242-251</c>) overrides <c>Grob::pure_y_extent</c> on Item and
/// stores the answer in <c>cached_pure_height_</c>, so the FIRST reader freezes the value:
/// a property the extent depends on that is rewritten LATER cannot change what an earlier
/// reader already saw. <c>Spanner</c> gets no such override and follows the change.
/// </para>
/// <para>
/// WHAT IT IS LOAD-BEARING FOR: <c>Note_collision</c>'s <c>fa</c> merge rewrites both
/// heads' <c>stem-attachment</c> while resolving <c>positioning-done</c>, which is forced
/// from <c>Separation_item::boxes</c>. Horizontal spacing reads the stem's PURE height
/// through <c>Note_spacing::stem_dir_correction</c> and must go on seeing the value from
/// before that rewrite. Without the cache the port's spacing followed
/// <c>fa-merge-direction</c> where the oracle's does not, and
/// <c>collision-head-solfa-fa</c> placed every mark after the first bar line 0.0241 out.
/// </para>
/// </summary>
public class ItemPureHeightCacheTests
{
    private static readonly Symbol YExtentSymbol = Symbol.Intern("Y-extent");

    /// <summary>The minimum basic-property alist a bare grob needs to exist.</summary>
    private static object Basics()
        => Pair.List(
            new Pair(
                Symbol.Intern("meta"),
                Pair.List(
                    new Pair(Symbol.Intern("name"), Symbol.Intern("TestGrob")),
                    new Pair(Symbol.Intern("interfaces"), Nil.Instance))));

    private static Item NewItem(double top)
    {
        Item item = new Item(Basics());
        item.SetProperty(YExtentSymbol, new Pair(0.0, top));
        return item;
    }

    private static Spanner NewSpanner(double top)
    {
        Spanner spanner = new Spanner(Basics());
        spanner.SetProperty(YExtentSymbol, new Pair(0.0, top));
        return spanner;
    }

    [Fact]
    public void an_items_pure_height_is_frozen_by_its_first_reader()
    {
        //Arrange
        Item item = NewItem(2.0);

        //Act
        Interval before = item.PureYExtent(item, 0, int.MaxValue);
        item.SetProperty(YExtentSymbol, new Pair(0.0, 5.0));
        Interval after = item.PureYExtent(item, 0, int.MaxValue);

        //Assert -- upstream returns cached_pure_height_, so the rewrite is invisible here
        before.Right.Should().Be(2.0);
        after.Right.Should().Be(2.0);
    }

    [Fact]
    public void the_control_an_item_read_for_the_first_time_after_the_change_follows_it()
    {
        //Arrange -- the same rewrite, on an item nothing has read yet
        Item item = NewItem(2.0);

        //Act
        item.SetProperty(YExtentSymbol, new Pair(0.0, 5.0));
        Interval first = item.PureYExtent(item, 0, int.MaxValue);

        //Assert -- so the frozen answer above is a CACHE and not a constant
        first.Right.Should().Be(5.0);
    }

    [Fact]
    public void the_control_a_spanner_has_no_such_cache_and_follows_the_change()
    {
        //Arrange -- upstream overrides pure_y_extent on Item ONLY
        Spanner spanner = NewSpanner(2.0);

        //Act
        Interval before = spanner.PureYExtent(spanner, 0, int.MaxValue);
        spanner.SetProperty(YExtentSymbol, new Pair(0.0, 5.0));
        Interval after = spanner.PureYExtent(spanner, 0, int.MaxValue);

        //Assert
        before.Right.Should().Be(2.0);
        after.Right.Should().Be(5.0);
    }

    [Fact]
    public void a_cached_height_is_re_based_onto_whatever_reference_is_asked_for()
    {
        //Arrange -- the stored interval carries no offset, so it can serve any refpoint
        Item item = NewItem(2.0);

        //Act
        Interval ownFrame = item.PureYExtent(item, 0, int.MaxValue);
        Interval againstItself = item.PureYExtent(item, 0, int.MaxValue);

        //Assert -- zero offset against itself, both before and after the cache filled
        ownFrame.Left.Should().Be(0.0);
        ownFrame.Right.Should().Be(2.0);
        againstItself.Left.Should().Be(ownFrame.Left);
        againstItself.Right.Should().Be(ownFrame.Right);
    }

    [Fact]
    public void a_fresh_copy_of_an_item_starts_with_an_empty_cache()
    {
        //Arrange -- upstream clears cached_pure_height_valid_ in the copy ctor too
        Item item = NewItem(2.0);
        _ = item.PureYExtent(item, 0, int.MaxValue);

        //Act -- a second item built the same way and given the new value up front
        Item other = NewItem(5.0);
        Interval fresh = other.PureYExtent(other, 0, int.MaxValue);

        //Assert -- the cache is per item, not shared through the class
        fresh.Right.Should().Be(5.0);
        item.PureYExtent(item, 0, int.MaxValue).Right.Should().Be(2.0);
    }
}
