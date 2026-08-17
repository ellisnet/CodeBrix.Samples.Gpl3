// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <c>Page_layout_problem::get_fixed_spacing</c> reads <c>alignment-distances</c> off the
/// system's bound — and which BOUND it reads is decided by whether the lookup is pure.
/// <para>
/// A system's real bounds are whatever the last line breaking left behind, so in a pure
/// lookup — one asking about a line the breaker has not chosen yet — reading them answers
/// about a different line. That is why upstream has <c>System::get_maybe_pure_bound</c>
/// at all, and this call site is the port's only consumer of it.
/// </para>
/// </summary>
public class FixedSpacingPureBoundTests
{
    private static readonly Symbol DetailsSymbol = Symbol.Intern("line-break-system-details");
    private static readonly Symbol AlignmentDistancesSymbol
        = Symbol.Intern("alignment-distances");

    /// <summary>
    /// A system whose LEFT bound forces one alignment distance. The fixture's system has
    /// no paper score, which is what a pure bound lookup needs in order to be
    /// distinguishable here: it answers "no bound for that line" while the ordinary
    /// accessor answers the bound the system is carrying.
    /// </summary>
    private static (Grob Before, Grob After) Fixture(bool forceADistance)
    {
        (PaperColumn left, PaperColumn _, Item leftItem, Item rightItem)
            = SpacingFixtures.TwoColumnsWithItems();

        SystemGrob system = Grob.SystemOf(leftItem);
        system.SetBound(Direction.Negative, left);

        if (forceADistance)
        {
            left.SetProperty(
                DetailsSymbol,
                Pair.List(new Pair(AlignmentDistancesSymbol, Pair.List(7.0))));
        }

        return (leftItem, rightItem);
    }

    [Fact]
    public void the_two_bound_accessors_answer_differently_on_this_system()
    {
        //Arrange
        // This is what makes the test below mean anything: if the ordinary and the pure
        // bound answered the same thing here, a lookup reading the wrong one would be
        // indistinguishable. The fixture's system carries a real LEFT bound and no paper
        // score, so the pure accessor has no line to answer about.
        (Grob before, Grob _) = Fixture(forceADistance: true);
        SystemGrob system = Grob.SystemOf(before);

        //Act
        Grob ordinary = system.GetMaybePureBound(Direction.Negative, false, 0, 0);
        Grob pure = system.GetMaybePureBound(Direction.Negative, true, 0, 0);

        //Assert
        ordinary.Should().NotBeNull();
        pure.Should().BeNull();
    }

    [Fact]
    public void the_pure_lookup_does_not_read_the_ordinary_bound()
    {
        //Arrange
        (Grob before, Grob after) = Fixture(forceADistance: true);

        //Act
        double forced = PageLayoutSpacing.GetFixedSpacing(before, after, 1, true, 0, 0);

        //Assert
        // THE PAIR THAT CARRIES THE CLAIM: same system, same bound, same forced distance —
        // and the pure lookup must NOT find it, because it asked for the bound of the line
        // under consideration rather than for the system's current one. A port that reads
        // the ordinary bound on both branches answers 7.0 here.
        double.IsNegativeInfinity(forced).Should().BeTrue();
    }

    [Fact]
    public void neither_lookup_forces_anything_when_no_distance_is_set()
    {
        //Arrange
        // THE CONTROL. With nothing forced, both branches answer "no forced distance", so
        // the test above is reading the alignment-distances entry and not merely the
        // absence of a bound.
        (Grob before, Grob after) = Fixture(forceADistance: false);

        //Act
        double unpure = PageLayoutSpacing.GetFixedSpacing(before, after, 1, false, 0, 0);
        double pure = PageLayoutSpacing.GetFixedSpacing(before, after, 1, true, 0, 0);

        //Assert
        double.IsNegativeInfinity(unpure).Should().BeTrue();
        double.IsNegativeInfinity(pure).Should().BeTrue();
    }
}
