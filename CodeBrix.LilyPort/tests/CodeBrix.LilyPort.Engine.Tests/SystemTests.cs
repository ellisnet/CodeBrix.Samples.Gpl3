// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Paper columns, systems, grob arrays and axis groups: the layer that fixes where
/// grobs sit horizontally and owns them once they are made.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SystemTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static object Alist(params (string Key, object Value)[] entries)
    {
        object result = Nil.Instance;
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            result = new Pair(new Pair(Sym(entries[i].Key), entries[i].Value), result);
        }

        return result;
    }

    private static object GrobBasics(params (string Key, object Value)[] extra)
    {
        List<(string, object)> entries = new List<(string, object)>
        {
            ("meta", Alist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
        };
        entries.AddRange(extra);
        return Alist(entries.ToArray());
    }

    /// <summary>A system spanning both axes, which is what upstream's System does.</summary>
    private static SystemGrob MakeSystem()
        => new SystemGrob(GrobBasics(("axes", Pair.List(0L, 1L))));

    private static PaperColumn MakeColumn(params (string Key, object Value)[] extra)
        => new PaperColumn(GrobBasics(extra));

    [Fact]
    public void a_grob_array_starts_empty_and_ordered()
    {
        //Arrange
        GrobArray array = new GrobArray();

        //Act
        bool ordered = array.IsOrdered;

        //Assert
        ordered.Should().BeTrue();
        array.IsEmpty.Should().BeTrue();
        array.Count.Should().Be(0);
    }

    [Fact]
    public void removing_duplicates_keeps_the_first_of_each()
    {
        //Arrange
        GrobArray array = new GrobArray();
        Item first = new Item(GrobBasics());
        Item second = new Item(GrobBasics());
        array.Add(first);
        array.Add(second);
        array.Add(first);

        //Act
        array.RemoveDuplicates();

        //Assert
        array.Count.Should().Be(2);
        array[0].Should().BeSameAs(first);
        array[1].Should().BeSameAs(second);
    }

    [Fact]
    public void reading_a_grob_link_does_not_create_it()
    {
        //Arrange
        // extract_grob_set must not have the side effect of installing an empty array,
        // because most grobs never have most links.
        Item grob = new Item(GrobBasics());

        //Act
        IReadOnlyList<Grob> elements = PointerGroupInterface.ExtractGrobSet(grob, "elements");

        //Assert
        elements.Should().BeEmpty();
        grob.GetObject("elements").Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void adding_to_a_grob_link_creates_it_on_demand()
    {
        //Arrange
        Item host = new Item(GrobBasics());
        Item child = new Item(GrobBasics());

        //Act
        PointerGroupInterface.AddGrob(host, Sym("elements"), child);

        //Assert
        PointerGroupInterface.Count(host, Sym("elements")).Should().Be(1);
        PointerGroupInterface.ExtractGrobSet(host, "elements")[0].Should().BeSameAs(child);
    }

    [Fact]
    public void adding_an_unordered_grob_marks_the_link_unordered()
    {
        //Arrange
        Item host = new Item(GrobBasics());

        //Act
        PointerGroupInterface.AddUnorderedGrob(host, Sym("all-elements"), new Item(GrobBasics()));

        //Assert
        PointerGroupInterface.GetGrobArray(host, Sym("all-elements")).IsOrdered.Should().BeFalse();
    }

    [Fact]
    public void an_axis_group_becomes_the_parent_on_each_of_its_axes()
    {
        //Arrange
        Item group = new Item(GrobBasics(("axes", Pair.List(0L, 1L))));
        Item element = new Item(GrobBasics());

        //Act
        AxisGroupInterface.AddElement(group, element);

        //Assert
        element.GetParent(Axis.X).Should().BeSameAs(group);
        element.GetParent(Axis.Y).Should().BeSameAs(group);
        AxisGroupInterface.Elements(group)[0].Should().BeSameAs(element);
    }

    [Fact]
    public void an_axis_group_leaves_an_existing_parent_alone()
    {
        //Arrange
        // This is what lets a note head take its horizontal reference from a paper
        // column and its vertical one from a staff.
        Item column = new Item(GrobBasics());
        Item staff = new Item(GrobBasics(("axes", Pair.List(0L, 1L))));
        Item head = new Item(GrobBasics());
        head.SetParent(column, Axis.X);

        //Act
        AxisGroupInterface.AddElement(staff, head);

        //Assert
        head.GetParent(Axis.X).Should().BeSameAs(column);
        head.GetParent(Axis.Y).Should().BeSameAs(staff);
    }

    [Fact]
    public void an_axis_group_extent_is_the_union_of_its_elements()
    {
        //Arrange
        Item group = new Item(GrobBasics(("axes", Pair.List(0L, 1L))));

        Item left = new Item(GrobBasics());
        left.SetProperty("X-extent", new Pair(-1.0, 1.0));
        Item right = new Item(GrobBasics());
        right.SetProperty("X-extent", new Pair(-1.0, 1.0));

        AxisGroupInterface.AddElement(group, left);
        AxisGroupInterface.AddElement(group, right);
        right.TranslateAxis(10.0, Axis.X);

        //Act
        Interval extent = AxisGroupInterface.GroupExtent(group, Axis.X);

        //Assert
        extent.Should().Be(new Interval(-1.0, 11.0));
    }

    [Fact]
    public void a_fresh_paper_column_has_no_rank_and_no_system()
    {
        //Arrange
        PaperColumn column = MakeColumn();

        //Act
        int rank = column.Rank;

        //Assert
        rank.Should().Be(PaperColumn.InvalidRank);
        column.System.Should().BeNull();
        column.GetColumn().Should().BeSameAs(column);
        column.HasInterface("paper-column-interface").Should().BeTrue();
    }

    [Fact]
    public void adding_columns_to_a_system_ranks_them_in_order()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        PaperColumn first = MakeColumn();
        PaperColumn second = MakeColumn();

        //Act
        system.AddColumn(first);
        system.AddColumn(second);

        //Assert
        first.Rank.Should().Be(0);
        second.Rank.Should().Be(1);
        system.Column(1).Should().BeSameAs(second);
        system.Column(5).Should().BeNull();
    }

    [Fact]
    public void a_column_added_to_a_system_takes_it_as_its_axis_group_parent()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        PaperColumn column = MakeColumn();

        //Act
        system.AddColumn(column);

        //Assert
        column.GetParent(Axis.X).Should().BeSameAs(system);
        column.GetParent(Axis.Y).Should().BeSameAs(system);
    }

    [Fact]
    public void a_column_is_breakable_only_when_it_carries_a_break_permission()
    {
        //Arrange
        PaperColumn plain = MakeColumn();
        PaperColumn breakable = MakeColumn(("line-break-permission", Sym("allow")));

        //Act
        bool plainBreakable = PaperColumn.IsBreakable(plain);

        //Assert
        plainBreakable.Should().BeFalse();
        PaperColumn.IsBreakable(breakable).Should().BeTrue();
    }

    [Fact]
    public void a_column_is_musical_when_something_starts_there()
    {
        //Arrange
        PaperColumn silent = MakeColumn();
        PaperColumn musical = MakeColumn(("shortest-starter-duration", new Rational(1, 4)));

        //Act
        bool silentIsMusical = PaperColumn.IsMusical(silent);

        //Assert
        silentIsMusical.Should().BeFalse();
        PaperColumn.IsMusical(musical).Should().BeTrue();
    }

    [Fact]
    public void a_column_with_nothing_on_it_is_unused()
    {
        //Arrange
        PaperColumn empty = MakeColumn();

        //Act
        bool used = PaperColumn.IsUsed(empty);

        //Assert
        used.Should().BeFalse();
    }

    [Fact]
    public void a_column_counts_as_used_for_any_of_the_five_reasons()
    {
        //Arrange
        PaperColumn withElement = MakeColumn();
        PointerGroupInterface.AddGrob(withElement, Sym("elements"), new Item(GrobBasics()));

        PaperColumn bounded = MakeColumn();
        PointerGroupInterface.AddGrob(bounded, Sym("bounded-by-me"), new Item(GrobBasics()));

        PaperColumn breakable = MakeColumn(("line-break-permission", Sym("allow")));
        PaperColumn flagged = MakeColumn(("used", true));
        PaperColumn labelled = MakeColumn(("labels", Pair.List(Sym("a"))));

        //Act
        bool elementUsed = PaperColumn.IsUsed(withElement);

        //Assert
        elementUsed.Should().BeTrue();
        PaperColumn.IsUsed(bounded).Should().BeTrue();
        PaperColumn.IsUsed(breakable).Should().BeTrue();
        PaperColumn.IsUsed(flagged).Should().BeTrue();
        PaperColumn.IsUsed(labelled).Should().BeTrue();
    }

    [Fact]
    public void used_columns_drops_the_empty_ones()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        PaperColumn start = MakeColumn(("line-break-permission", Sym("allow")));
        PaperColumn empty = MakeColumn();
        PaperColumn note = MakeColumn(("used", true));
        PaperColumn end = MakeColumn(("line-break-permission", Sym("allow")));

        system.AddColumn(start);
        system.AddColumn(empty);
        system.AddColumn(note);
        system.AddColumn(end);

        //Act
        List<PaperColumn> used = system.UsedColumns();

        //Assert
        used.Should().Equal(new List<PaperColumn> { start, note, end });
    }

    [Fact]
    public void used_columns_stops_at_the_last_breakable_column()
    {
        //Arrange
        // Anything after the last breakable column cannot start a line, so including
        // it would only enlarge the spacing problem.
        SystemGrob system = MakeSystem();
        PaperColumn start = MakeColumn(("line-break-permission", Sym("allow")));
        PaperColumn end = MakeColumn(("line-break-permission", Sym("allow")));
        PaperColumn trailing = MakeColumn(("used", true));

        system.AddColumn(start);
        system.AddColumn(end);
        system.AddColumn(trailing);

        //Act
        List<PaperColumn> used = system.UsedColumns();

        //Assert
        used.Should().Equal(new List<PaperColumn> { start, end });
    }

    [Fact]
    public void broken_column_range_lists_the_candidate_break_points_between_two_items()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        PaperColumn left = MakeColumn(("line-break-permission", Sym("allow")));
        PaperColumn middle = MakeColumn(("line-break-permission", Sym("allow")));
        PaperColumn plain = MakeColumn();
        PaperColumn right = MakeColumn(("line-break-permission", Sym("allow")));

        system.AddColumn(left);
        system.AddColumn(middle);
        system.AddColumn(plain);
        system.AddColumn(right);

        //Act
        List<PaperColumn> candidates = system.BrokenColumnRange(left, right);

        //Assert
        candidates.Should().Equal(new List<PaperColumn> { middle });
    }

    [Fact]
    public void a_column_already_assigned_to_a_system_is_not_a_break_candidate()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        PaperColumn left = MakeColumn(("line-break-permission", Sym("allow")));
        PaperColumn middle = MakeColumn(("line-break-permission", Sym("allow")));
        PaperColumn right = MakeColumn(("line-break-permission", Sym("allow")));

        system.AddColumn(left);
        system.AddColumn(middle);
        system.AddColumn(right);
        middle.System = system;

        //Act
        List<PaperColumn> candidates = system.BrokenColumnRange(left, right);

        //Assert
        candidates.Should().BeEmpty();
    }

    [Fact]
    public void a_system_owns_the_grobs_it_typesets()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        system.Layout = new OutputDef();
        Item grob = new Item(GrobBasics());

        //Act
        system.TypesetGrob(grob);

        //Assert
        system.ElementCount.Should().Be(1);
        grob.Layout.Should().BeSameAs(system.Layout);
    }

    [Fact]
    public void typesetting_the_same_grob_twice_is_reported_and_ignored()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        system.Layout = new OutputDef();
        Item grob = new Item(GrobBasics());
        system.TypesetGrob(grob);

        //Act
        system.TypesetGrob(grob);

        //Assert
        system.ElementCount.Should().Be(1);
    }

    [Fact]
    public void a_system_counts_its_spanners_separately_from_its_items()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        system.Layout = new OutputDef();
        system.TypesetGrob(new Item(GrobBasics()));
        system.TypesetGrob(new Spanner(GrobBasics()));
        system.TypesetGrob(new Spanner(GrobBasics()));

        //Act
        int spanners = system.SpannerCount;

        //Assert
        spanners.Should().Be(2);
        system.ElementCount.Should().Be(3);
    }

    [Fact]
    public void the_all_elements_link_is_unordered()
    {
        //Arrange
        SystemGrob system = MakeSystem();

        //Act
        bool ordered = system.AllElements.IsOrdered;

        //Assert
        ordered.Should().BeFalse();
    }

    [Fact]
    public void a_system_bound_must_be_a_paper_column()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        PaperColumn column = MakeColumn();
        Item notAColumn = new Item(GrobBasics());

        //Act
        system.SetBound(Direction.Negative, column);
        system.SetBound(Direction.Positive, notAColumn);

        //Assert
        system.GetBound(Direction.Negative).Should().BeSameAs(column);
        system.GetBound(Direction.Positive).Should().BeNull();
    }

    [Fact]
    public void pre_processing_prebreaks_every_non_musical_item_once()
    {
        //Arrange
        // Breaking appends the clones to the same list; iterating over the ORIGINAL
        // count is what stops the clones being broken in turn.
        SystemGrob system = MakeSystem();
        system.Layout = new OutputDef();

        Item breakable = new Item(GrobBasics(("non-musical", true)));
        Item musical = new Item(GrobBasics());
        system.TypesetGrob(breakable);
        system.TypesetGrob(musical);

        //Act
        system.PreProcessing();

        //Assert
        breakable.IsBroken.Should().BeTrue();
        musical.IsBroken.Should().BeFalse();

        // Two originals plus two clones of the breakable one.
        system.ElementCount.Should().Be(4);
        breakable.FindPrebrokenPiece(Direction.Negative).IsBroken.Should().BeFalse();
    }

    [Fact]
    public void post_processing_moves_the_system_so_its_top_sits_at_the_origin()
    {
        //Arrange
        SystemGrob system = MakeSystem();
        system.SetProperty("Y-extent", new Pair(-3.0, 5.0));

        //Act
        system.PostProcessing();

        //Assert
        system.GetOffset(Axis.Y).Should().Be(-5.0);
    }

    [Fact]
    public void the_minimum_distance_between_columns_comes_from_their_facing_skylines()
    {
        //Arrange
        // The LEFT column presents its right-facing outline and the RIGHT column its
        // left-facing one. Reading the same side from both would compare an outline
        // with itself.
        PaperColumn left = MakeColumn();
        PaperColumn right = MakeColumn();

        left.SetProperty(
            "horizontal-skylines",
            new SkylinePair(new Box(new Interval(0, 2), new Interval(0, 1)), Axis.Y));
        right.SetProperty(
            "horizontal-skylines",
            new SkylinePair(new Box(new Interval(-1, 0), new Interval(0, 1)), Axis.Y));

        //Act
        double distance = PaperColumn.MinimumDistance(left, right);

        //Assert
        // The left column reaches x = 2 and the right one back to x = -1, so they must
        // be at least 3 apart.
        distance.Should().Be(3.0);
    }

    [Fact]
    public void columns_with_no_skylines_may_touch()
    {
        //Arrange
        PaperColumn left = MakeColumn();
        PaperColumn right = MakeColumn();

        //Act
        double distance = PaperColumn.MinimumDistance(left, right);

        //Assert
        distance.Should().Be(0.0);
    }

    [Fact]
    public void a_column_records_the_moment_it_sits_at()
    {
        //Arrange
        PaperColumn column = MakeColumn(("when", new Moment(new Rational(1, 2))));

        //Act
        Moment when = PaperColumn.WhenMoment(column);

        //Assert
        when.Should().Be(new Moment(new Rational(1, 2)));
        PaperColumn.WhenMoment(MakeColumn()).Should().Be(new Moment(0));
    }

    [Fact]
    public void a_cloned_column_keeps_its_rank_but_not_its_system()
    {
        //Arrange
        // A prebroken piece belongs at the same horizontal position as its original,
        // but has not been assigned to a line yet.
        SystemGrob system = MakeSystem();
        PaperColumn column = MakeColumn();
        system.AddColumn(column);
        column.System = system;

        //Act
        PaperColumn clone = (PaperColumn)column.Clone();

        //Assert
        clone.Rank.Should().Be(column.Rank);
        clone.System.Should().BeNull();
        clone.Original.Should().BeSameAs(column);
    }
}
