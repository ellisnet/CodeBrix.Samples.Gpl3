// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The self-alignment linear combinations: -1 is the left/bottom edge, 0 the centre,
/// 1 the right/top edge, and the OFFSET is what moves the chosen point of the grob's
/// extent onto the reference point.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SelfAlignmentInterfaceTests
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

    private static object Extent(double left, double right) => new Pair(left, right);

    [Fact]
    public void aligning_on_self_moves_the_chosen_edge_to_the_reference_point()
    {
        //Arrange
        Item centered = NewGrob(
            ("X-extent", Extent(-2.0, 4.0)), ("self-alignment-X", 0L));
        Item leftEdge = NewGrob(
            ("X-extent", Extent(-2.0, 4.0)), ("self-alignment-X", -1L));
        Item rightEdge = NewGrob(
            ("X-extent", Extent(-2.0, 4.0)), ("self-alignment-X", 1L));

        //Act / Assert
        // linear_combination over (-2, 4): centre 1, left -2, right 4 — and the
        // offset is its NEGATION, which is what puts that point at the origin.
        SelfAlignmentInterface.XAlignedOnSelf(centered).Should().BeApproximately(-1.0, 1e-12);
        SelfAlignmentInterface.XAlignedOnSelf(leftEdge).Should().BeApproximately(2.0, 1e-12);
        SelfAlignmentInterface.XAlignedOnSelf(rightEdge).Should().BeApproximately(-4.0, 1e-12);
    }

    [Fact]
    public void an_empty_extent_is_not_an_error_and_is_not_aligned()
    {
        //Arrange
        // Empty extent doesn't mean an error - we simply don't align such grobs.
        Item me = NewGrob(("self-alignment-X", 0L));

        //Act / Assert
        SelfAlignmentInterface.XAlignedOnSelf(me).Should().Be(0.0);
    }

    [Fact]
    public void aligning_on_the_parent_combines_both_alignments()
    {
        //Arrange
        Item parent = NewGrob(("X-extent", Extent(2.0, 10.0)));
        Item me = NewGrob(
            ("X-extent", Extent(-1.0, 1.0)),
            ("self-alignment-X", 0L),
            ("parent-alignment-X", -1L));
        me.XParent = parent;

        //Act
        double offset = SelfAlignmentInterface.AlignedOnParent(me, Axis.X);

        //Assert
        // Own centre (0) is subtracted, the parent's LEFT edge (2) added.
        offset.Should().BeApproximately(2.0, 1e-12);
    }

    [Fact]
    public void parent_alignment_falls_back_on_self_alignment_when_unset()
    {
        //Arrange
        Item parent = NewGrob(("X-extent", Extent(2.0, 10.0)));
        Item me = NewGrob(
            ("X-extent", Extent(-1.0, 1.0)),
            ("self-alignment-X", 0L));
        me.XParent = parent;

        //Act
        double offset = SelfAlignmentInterface.AlignedOnParent(me, Axis.X);

        //Assert
        // par_align defaults to self_align: centre on the parent's centre (6).
        offset.Should().BeApproximately(6.0, 1e-12);
    }

    [Fact]
    public void centering_on_the_parent_answers_the_parent_extent_centre()
    {
        //Arrange
        Item parent = NewGrob(("X-extent", Extent(2.0, 10.0)));
        Item me = NewGrob();
        me.XParent = parent;

        //Act / Assert
        SelfAlignmentInterface.CenteredOnXParent(me).Should().BeApproximately(6.0, 1e-12);
    }

    [Fact]
    public void a_parentless_extent_centres_via_the_robust_fallback()
    {
        //Arrange
        // robust_relative_extent of an extent-less grob answers a point at its own
        // coordinate, so the centre is 0 rather than NaN.
        Item parent = NewGrob();
        Item me = NewGrob();
        me.XParent = parent;

        //Act / Assert
        SelfAlignmentInterface.CenteredOnXParent(me).Should().Be(0.0);
    }
}
