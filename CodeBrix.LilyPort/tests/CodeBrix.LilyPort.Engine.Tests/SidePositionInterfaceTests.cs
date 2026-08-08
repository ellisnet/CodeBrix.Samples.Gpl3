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
/// The side-position workhorse over hand-built grobs: the no-direction passthrough,
/// the skyline distance with padding, the minimum-space floor, and the support-set
/// bookkeeping. The numbers are worked out by hand from
/// <c>lily/side-position-interface.cc</c>.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SidePositionInterfaceTests
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

    private static Item NewGrob(params (string Key, object Value)[] extra)
    {
        List<(string, object)> entries = new List<(string, object)>
        {
            ("meta", Alist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
        };
        entries.AddRange(extra);
        return new Item(Alist(entries.ToArray()));
    }

    private static object BoxSkylines(double left, double right, double bottom, double top)
        => new SkylinePair(
            new[] { new Box(new Interval(left, right), new Interval(bottom, top)) },
            Axis.X).ToScheme();

    /// <summary>A victim above a support, with a shared Y parent, ready to position.</summary>
    private static (Item Victim, Item Support) VictimAndSupport(
        params (string Key, object Value)[] victimExtra)
    {
        Item parent = NewGrob();
        List<(string, object)> entries = new List<(string, object)>
        {
            ("direction", 1L),
            ("vertical-skylines", BoxSkylines(0.0, 1.0, -0.2, 0.2)),
        };
        entries.AddRange(victimExtra);
        Item victim = NewGrob(entries.ToArray());
        Item support = NewGrob(("vertical-skylines", BoxSkylines(0.0, 4.0, 0.0, 1.0)));
        victim.YParent = parent;
        support.YParent = parent;
        SidePositionInterface.AddSupport(victim, support);
        return (victim, support);
    }

    [Fact]
    public void a_grob_with_no_direction_answers_the_current_offset()
    {
        //Arrange
        // This is occasionally useful, for example to place scripts in the middle of
        // two piano staves using a Dynamics context.
        Item me = NewGrob();

        //Act / Assert
        SidePositionInterface.AlignedSide(me, Axis.Y, false, 0, 0, 7.5).Should().Be(7.5);
        SidePositionInterface.AlignedSide(me, Axis.Y, false, 0, 0, null).Should().Be(0.0);
    }

    [Fact]
    public void a_victim_is_placed_clear_of_its_support_plus_padding()
    {
        //Arrange
        (Item victim, Item _) = VictimAndSupport(("padding", 0.5));

        //Act
        object offset = SidePositionInterface.AlignedSide(victim, Axis.Y, false, 0, 0, null);

        //Assert
        // The support's top is at 1, the victim's own bottom at -0.2, so the skyline
        // distance is 1.2; padding (in staff spaces, staff space 1 here) adds 0.5.
        offset.Should().BeOfType<double>();
        ((double)offset).Should().BeApproximately(1.7, 1e-12);
    }

    [Fact]
    public void minimum_space_forces_a_floor_on_the_offset()
    {
        //Arrange
        (Item victim, Item _) = VictimAndSupport(("minimum-space", 5.0));

        //Act
        object offset = SidePositionInterface.AlignedSide(victim, Axis.Y, false, 0, 0, null);

        //Assert
        ((double)offset).Should().BeApproximately(5.0, 1e-12);
    }

    [Fact]
    public void the_current_offset_is_kept_when_it_is_already_further_out()
    {
        //Arrange
        (Item victim, Item _) = VictimAndSupport();

        //Act
        object offset = SidePositionInterface.AlignedSide(victim, Axis.Y, false, 0, 0, 9.0);

        //Assert
        // dir * max (dir * computed, dir * current): the caller's 9 is further up
        // than the computed 1.2, so it wins.
        ((double)offset).Should().BeApproximately(9.0, 1e-12);
    }

    [Fact]
    public void the_support_set_keeps_first_occurrence_order_and_drops_duplicates()
    {
        //Arrange
        Item me = NewGrob();
        Item a = NewGrob();
        Item b = NewGrob();
        SidePositionInterface.AddSupport(me, a);
        SidePositionInterface.AddSupport(me, b);
        SidePositionInterface.AddSupport(me, a);

        //Act
        System.Collections.Generic.IReadOnlyList<Grob> support
            = SidePositionInterface.GetSupportSet(me);

        //Assert
        support.Should().HaveCount(2);
        support[0].Should().BeSameAs(a);
        support[1].Should().BeSameAs(b);
    }

    [Fact]
    public void directed_round_floors_downward_and_ceils_upward()
    {
        //Arrange / Act / Assert
        SidePositionInterface.DirectedRound(1.4, Direction.Negative).Should().Be(1.0);
        SidePositionInterface.DirectedRound(1.4, Direction.Positive).Should().Be(2.0);
        SidePositionInterface.DirectedRound(1.4, Direction.Center).Should().Be(2.0);
    }

    [Fact]
    public void a_cross_staff_support_makes_the_victim_cross_staff()
    {
        //Arrange
        Item me = NewGrob(("direction", 1L));
        Item support = NewGrob(("cross-staff", true));
        SidePositionInterface.AddSupport(me, support);

        //Act / Assert
        // The support is cross-staff and has no direction callback of its own, which
        // is the first of the three tests and answers immediately.
        SidePositionInterface.CalcCrossStaff(me).Should().BeTrue();
    }
}
