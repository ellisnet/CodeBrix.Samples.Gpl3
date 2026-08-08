// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG7's stacking arithmetic: <c>Align_interface</c>'s minimum-distance
/// translations over hand-built grobs with box skylines. The numbers are worked out
/// by hand from <c>lily/align-interface.cc</c>, since upstream ships no tests for it.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class AlignInterfaceTests
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

    /// <summary>A box skyline pair spanning x in [0, 4] and y in [-height, height].</summary>
    private static object BoxSkylines(double height)
        => new SkylinePair(
            new[] { new Box(new Interval(0, 4), new Interval(-height, height)) },
            Axis.X).ToScheme();

    private static Item Alignment(long stackingDir)
        => new Item(GrobBasics(
            ("axes", Pair.List(1L)),
            ("stacking-dir", stackingDir)));

    private static Item Staffish(params (string Key, object Value)[] extra)
        => new Item(GrobBasics(extra));

    [Fact]
    public void two_staves_stack_downward_at_their_minimum_distance()
    {
        //Arrange
        Item me = Alignment(-1);
        Item first = Staffish(("vertical-skylines", BoxSkylines(1.0)));
        Item second = Staffish(("vertical-skylines", BoxSkylines(1.0)));

        //Act
        List<double> translates = AlignInterface.GetMinimumTranslations(
            me, new Grob[] { first, second }, Axis.Y);

        //Assert
        // The first staff is pushed down so its top touches the alignment's origin;
        // the second so that its top (at translate + 1) touches the first's bottom
        // (at -2). That contact-not-overlap answer is the whole point of skyline
        // stacking.
        translates.Should().HaveCount(2);
        translates[0].Should().BeApproximately(-1.0, 1e-12);
        translates[1].Should().BeApproximately(-3.0, 1e-12);
    }

    [Fact]
    public void an_element_with_no_skyline_keeps_the_running_position()
    {
        //Arrange
        Item me = Alignment(-1);
        Item first = Staffish(("vertical-skylines", BoxSkylines(1.0)));
        Item empty = Staffish();
        Item last = Staffish(("vertical-skylines", BoxSkylines(1.0)));

        //Act
        List<double> translates = AlignInterface.GetMinimumTranslations(
            me, new Grob[] { first, empty, last }, Axis.Y);

        //Assert
        // A skyline-less element travels WITH the running position rather than
        // claiming space of its own, and the stack continues over it unbroken.
        translates.Should().HaveCount(3);
        translates[0].Should().BeApproximately(-1.0, 1e-12);
        translates[1].Should().BeApproximately(-1.0, 1e-12);
        translates[2].Should().BeApproximately(-3.0, 1e-12);
    }

    [Fact]
    public void minimum_distance_in_the_spacing_spec_wins_over_the_skyline_gap()
    {
        //Arrange
        // Both elements are spaceable (no staff-affinity), so the spec between them
        // is the FIRST one's staff-staff-spacing.
        Item me = Alignment(-1);
        Item first = Staffish(
            ("vertical-skylines", BoxSkylines(1.0)),
            ("staff-staff-spacing", Alist(("minimum-distance", 10.0))));
        Item second = Staffish(("vertical-skylines", BoxSkylines(1.0)));

        //Act
        List<double> translates = AlignInterface.GetMinimumTranslations(
            me, new Grob[] { first, second }, Axis.Y);

        //Assert
        // The skyline gap alone would give -3; minimum-distance 10 forces the
        // second reference point 10 below the first.
        translates[1].Should().BeApproximately(-11.0, 1e-12);
    }

    [Fact]
    public void stacking_dir_up_stacks_upward()
    {
        //Arrange
        Item me = Alignment(1);
        Item first = Staffish(("vertical-skylines", BoxSkylines(1.0)));
        Item second = Staffish(("vertical-skylines", BoxSkylines(1.0)));

        //Act
        List<double> translates = AlignInterface.GetMinimumTranslations(
            me, new Grob[] { first, second }, Axis.Y);

        //Assert
        // The first element's move compensates its own overhang PAST the origin in
        // the stacking direction: its bottom edge (the max_height of its DOWN
        // skyline, which reads back SIGNED, here -1) gives dy = -1, clamped to 0.
        // The second then clears the first's top (+1) against its own bottom (-1).
        translates[0].Should().BeApproximately(0.0, 1e-12);
        translates[1].Should().BeApproximately(2.0, 1e-12);
    }

    [Fact]
    public void align_elements_to_minimum_distances_moves_the_elements()
    {
        //Arrange
        Item me = Alignment(-1);
        Item first = Staffish(("vertical-skylines", BoxSkylines(1.0)));
        Item second = Staffish(("vertical-skylines", BoxSkylines(1.0)));
        first.YParent = me;
        second.YParent = me;
        PointerGroupInterface.AddGrob(me, Sym("elements"), first);
        PointerGroupInterface.AddGrob(me, Sym("elements"), second);

        //Act
        AlignInterface.AlignElementsToMinimumDistances(me, Axis.Y);

        //Assert
        first.RelativeCoordinate(me, Axis.Y).Should().BeApproximately(-1.0, 1e-12);
        second.RelativeCoordinate(me, Axis.Y).Should().BeApproximately(-3.0, 1e-12);
    }

    [Fact]
    public void get_axis_reads_the_first_axes_entry()
    {
        //Arrange
        Item vertical = new Item(GrobBasics(("axes", Pair.List(1L))));
        Item horizontal = new Item(GrobBasics(("axes", Pair.List(0L))));

        //Act / Assert
        AlignInterface.GetAxis(vertical).Should().Be(Axis.Y);
        AlignInterface.GetAxis(horizontal).Should().Be(Axis.X);
    }

    [Fact]
    public void padding_widens_every_gap()
    {
        //Arrange
        Item me = new Item(GrobBasics(
            ("axes", Pair.List(1L)),
            ("stacking-dir", -1L),
            ("padding", 0.5)));
        Item first = Staffish(("vertical-skylines", BoxSkylines(1.0)));
        Item second = Staffish(("vertical-skylines", BoxSkylines(1.0)));

        //Act
        List<double> translates = AlignInterface.GetMinimumTranslations(
            me, new Grob[] { first, second }, Axis.Y);

        //Assert
        // Padding is added at the top of the stack AND between the staves.
        translates[0].Should().BeApproximately(-1.5, 1e-12);
        translates[1].Should().BeApproximately(-4.0, 1e-12);
    }
}
