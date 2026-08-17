// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <c>Staff_grouper_interface::maybe_pure_within_group</c>, whose two branches ask
/// DIFFERENT questions about the staff below.
/// <para>
/// A staff is "within the group" when the next spaceable staff below it belongs to the
/// same grouper — that is what decides whether the gap under it is priced as
/// <c>staff-staff-spacing</c> or as <c>staffgroup-staff-spacing</c>. The UNPURE branch
/// asks whether that staff is live; the PURE branch asks whether it would SUICIDE on the
/// line under consideration, because before line breaking nothing has suicided yet and
/// every removable staff still reads live.
/// </para>
/// </summary>
public class StaffGrouperWithinGroupTests
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol StaffGrouperSymbol = Symbol.Intern("staff-grouper");

    /// <summary>
    /// A grouper over two staves. The lower one is <c>remove-empty</c> with nothing worth
    /// living for, so it is LIVE now and will not survive a line that starts after the
    /// first column — which is precisely the disagreement the two branches exist to have.
    /// </summary>
    private static (Grob Grouper, Grob Upper) Fixture(bool lowerIsRemovable)
    {
        Grob grouper = SpacingFixtures.NewSpacingGrob();
        Grob upper = SpacingFixtures.NewSpacingGrob();
        Grob lower = lowerIsRemovable
            ? SpacingFixtures.NewSpacingGrob(("remove-empty", true))
            : SpacingFixtures.NewSpacingGrob();

        upper.SetObject(StaffGrouperSymbol, grouper);
        lower.SetObject(StaffGrouperSymbol, grouper);
        PointerGroupInterface.AddGrob(grouper, ElementsSymbol, upper);
        PointerGroupInterface.AddGrob(grouper, ElementsSymbol, lower);
        return (grouper, upper);
    }

    [Fact]
    public void the_pure_branch_skips_a_staff_that_will_suicide_on_the_line()
    {
        //Arrange
        // The range starts after column 0 because remove-first is unset: upstream never
        // removes a staff on the FIRST line, so a range starting at zero could not tell
        // the two branches apart at all.
        (Grob grouper, Grob upper) = Fixture(lowerIsRemovable: true);

        //Act
        bool within = StaffGrouperInterface.MaybePureWithinGroup(grouper, upper, true, 1, 5);

        //Assert
        // Nothing spaceable SURVIVES below the upper staff, so it is the group's last
        // staff and the gap under it is a staffgroup gap.
        within.Should().BeFalse();
    }

    [Fact]
    public void the_unpure_branch_keeps_the_same_staff_because_it_is_still_live()
    {
        //Arrange
        (Grob grouper, Grob upper) = Fixture(lowerIsRemovable: true);

        //Act
        bool within = StaffGrouperInterface.MaybePureWithinGroup(grouper, upper, false, 1, 5);

        //Assert
        // THE PAIR THAT CARRIES THE CLAIM: the same grouper, the same staff, the same
        // range — and the opposite answer, because liveness and survival are not the
        // same question. A port that answers liveness on both branches cannot produce
        // this pair at all.
        within.Should().BeTrue();
    }

    [Fact]
    public void a_staff_that_never_suicides_is_within_the_group_on_both_branches()
    {
        //Arrange
        // THE CONTROL. With nothing removable below, the two branches must agree — which
        // is why the divergence was invisible on every score without a removable staff.
        (Grob grouper, Grob upper) = Fixture(lowerIsRemovable: false);

        //Act
        bool pure = StaffGrouperInterface.MaybePureWithinGroup(grouper, upper, true, 1, 5);
        bool unpure = StaffGrouperInterface.MaybePureWithinGroup(grouper, upper, false, 1, 5);

        //Assert
        pure.Should().BeTrue();
        unpure.Should().BeTrue();
    }

    [Fact]
    public void the_last_staff_of_a_grouper_is_never_within_the_group()
    {
        //Arrange
        // A second control, for the branch below the loop: with no staff at all after it,
        // the answer is false however the question is asked.
        Grob grouper = SpacingFixtures.NewSpacingGrob();
        Grob only = SpacingFixtures.NewSpacingGrob();
        only.SetObject(StaffGrouperSymbol, grouper);
        PointerGroupInterface.AddGrob(grouper, ElementsSymbol, only);

        //Act
        bool pure = StaffGrouperInterface.MaybePureWithinGroup(grouper, only, true, 1, 5);
        bool unpure = StaffGrouperInterface.MaybePureWithinGroup(grouper, only, false, 1, 5);

        //Assert
        pure.Should().BeFalse();
        unpure.Should().BeFalse();
    }
}
