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
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG5's dot placement arithmetic: the badness scoring, the shift walk and the
/// X offset, worked out by hand from <c>lily/dot-configuration.cc</c> — upstream ships
/// no tests for it, and the brief calls it a decades-tuned algorithm where a plausible
/// improvement is a parity bug.
/// </summary>
public class DotConfigurationTests
{
    private static DotFormattingProblem EmptyProblem()
        => new DotFormattingProblem(new List<Box>(), Interval.Empty);

    private static DotPosition At(int pos, Direction dir = default)
    {
        DotPosition dp = default;
        dp.Pos = pos;
        dp.Dir = dir;

        // A grob is only consulted by Shifted's on-line test, which these
        // arithmetic-only tests avoid by never calling Shifted with a dot that has a
        // staff symbol — StaffSymbolReferencer.OnLine answers false for a null staff.
        dp.Dot = SpacingFixtures.NewSpacingGrob();
        return dp;
    }

    [Fact]
    public void an_undisplaced_dot_still_costs_one_demerit_for_not_moving_up()
    {
        //Arrange
        DotConfiguration cfg = new DotConfiguration(EmptyProblem());
        cfg[4] = At(4);

        //Act
        int badness = cfg.Badness();

        //Assert
        // displacement 0 -> sqr * 2 = 0, dot_move_dir is CENTER which is not UP,
        // so the else-branch adds 1. Upstream's scoring is deliberately not zero.
        badness.Should().Be(1);
    }

    [Fact]
    public void moving_a_dot_against_its_own_direction_costs_two_extra()
    {
        //Arrange
        DotConfiguration withDir = new DotConfiguration(EmptyProblem());
        withDir[5] = At(4, Direction.Negative); // moved up 1, wants DOWN

        DotConfiguration withoutDir = new DotConfiguration(EmptyProblem());
        withoutDir[5] = At(4); // moved up 1, no preference

        //Act / Assert
        // With a direction and a move against it: 1*1*2 + 2 = 4.
        withDir.Badness().Should().Be(4);

        // Without a direction, moving UP is the preferred way: 1*1*2 + 0 = 2.
        withoutDir.Badness().Should().Be(2);
    }

    [Fact]
    public void displacement_costs_grow_with_the_square_of_the_distance()
    {
        //Arrange
        DotConfiguration cfg = new DotConfiguration(EmptyProblem());
        cfg[6] = At(2); // moved up 4

        //Act / Assert
        // 4*4*2 = 32, moving up so no direction penalty.
        cfg.Badness().Should().Be(32);
    }

    [Fact]
    public void remove_collision_shifts_the_cheaper_way()
    {
        //Arrange
        // One dot at 0 with no staff: Shifted moves an off-line dot by 2. Down would
        // move it to -2 (badness 2*2*2 + 1 for not moving up = 9); up to +2
        // (badness 2*2*2 = 8). Up must win.
        DotConfiguration cfg = new DotConfiguration(EmptyProblem());
        cfg[0] = At(0);

        //Act
        cfg.RemoveCollision(0);

        //Assert
        List<int> keys = new List<int>();
        foreach (KeyValuePair<int, DotPosition> ent in cfg.Entries)
        {
            keys.Add(ent.Key);
        }

        keys.Should().Equal(2);
    }

    [Fact]
    public void a_shift_carries_following_dots_along_only_while_they_collide()
    {
        //Arrange
        // Dots at 0 and 2: shifting 0 up by 2 lands on 2, and the offset walk then
        // carries the 2 up to 4 — but a dot far away (at 8) stays put because the
        // offset resets when its slot is free.
        DotConfiguration cfg = new DotConfiguration(EmptyProblem());
        cfg[0] = At(0);
        cfg[2] = At(2);
        cfg[8] = At(8);

        //Act
        DotConfiguration shifted = cfg.Shifted(0, Direction.Positive);

        //Assert
        List<int> keys = new List<int>();
        foreach (KeyValuePair<int, DotPosition> ent in shifted.Entries)
        {
            keys.Add(ent.Key);
        }

        keys.Should().Equal(2, 4, 8);
    }

    [Fact]
    public void the_x_offset_is_the_head_skyline_height_at_the_dot_positions()
    {
        //Arrange
        // One box reaching x = 1.5 over staff positions 0..2; a dot at 1 must clear
        // it, a configuration with only a dot at 10 must not.
        List<Box> boxes = new List<Box>
        {
            new Box(new Interval(0.0, 1.5), new Interval(0.0, 2.0)),
        };
        DotFormattingProblem problem = new DotFormattingProblem(
            boxes, new Interval(0.0, 0.5));

        DotConfiguration near = new DotConfiguration(problem);
        near[1] = At(1);

        DotConfiguration far = new DotConfiguration(problem);
        far[10] = At(10);

        //Act / Assert
        near.XOffset().Should().Be(1.5);

        // Away from the box the skyline is the minimum height set from the heads'
        // own extent.
        far.XOffset().Should().Be(0.5);
    }
}
