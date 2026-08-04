// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The horizontal spacing model: springs, and the line spacer that solves chains of
/// them.
/// <para>
/// Every number here was worked out by hand from <c>lily/spring.cc</c> and
/// <c>lily/simple-spacer.cc</c>, since upstream ships no tests for either. The
/// blocking-force arithmetic is the part worth pinning: the spacer's compression loop
/// depends on the blocking force being the exact boundary below which a spring stops
/// changing length.
/// </para>
/// </summary>
public class SpacingTests
{
    [Fact]
    public void a_default_spring_is_one_unit_long_and_unit_stiff()
    {
        //Arrange
        Spring spring = Spring.Default;

        //Act
        double ideal = spring.IdealDistance;

        //Assert
        ideal.Should().Be(1.0);
        spring.MinDistance.Should().Be(1.0);
        spring.InverseStretchStrength.Should().Be(1.0);
        spring.InverseCompressStrength.Should().Be(1.0);
        spring.BlockingForce.Should().Be(0.0);
    }

    [Fact]
    public void a_zeroed_spring_reads_back_as_the_default_spring()
    {
        //Arrange
        // C# cannot intercept `default(Spring)`, and upstream's defaults are all 1.0
        // rather than 0.0 — so a zeroed spring must not read as zero-length.
        Spring spring = default;

        //Act
        double ideal = spring.IdealDistance;

        //Assert
        ideal.Should().Be(1.0);
        spring.Length(0.0).Should().Be(1.0);
    }

    [Fact]
    public void a_constructed_spring_takes_its_strengths_from_its_distances()
    {
        //Arrange
        Spring spring = new Spring(2.0, 1.0);

        //Act
        double stretch = spring.InverseStretchStrength;

        //Assert
        stretch.Should().Be(2.0);
        spring.InverseCompressStrength.Should().Be(1.0);
        spring.BlockingForce.Should().Be(-1.0);
    }

    [Fact]
    public void spring_length_follows_the_force_until_it_blocks()
    {
        //Arrange
        Spring spring = new Spring(2.0, 1.0);

        //Act
        double neutral = spring.Length(0.0);

        //Assert
        neutral.Should().Be(2.0);
        spring.Length(1.0).Should().Be(4.0);
        spring.Length(-1.0).Should().Be(1.0);

        // Below the blocking force nothing more happens.
        spring.Length(-5.0).Should().Be(1.0);
    }

    [Fact]
    public void a_spring_whose_minimum_exceeds_its_ideal_blocks_at_a_positive_force()
    {
        //Arrange
        Spring spring = new Spring(2.0, 5.0);

        //Act
        double blocking = spring.BlockingForce;

        //Assert
        // Stretch strength is the ideal distance; compress strength clamps to zero
        // because there is no room below the minimum.
        spring.InverseStretchStrength.Should().Be(2.0);
        spring.InverseCompressStrength.Should().Be(0.0);
        blocking.Should().Be(1.5);
        spring.Length(0.0).Should().Be(5.0);
    }

    [Fact]
    public void setting_a_blocking_force_pins_the_spring_at_that_length()
    {
        //Arrange
        Spring spring = new Spring(2.0, 1.0);

        //Act
        spring.SetBlockingForce(0.25);

        //Assert
        spring.MinDistance.Should().Be(2.5);
        spring.BlockingForce.Should().Be(0.25);
        spring.Length(0.0).Should().Be(2.5);
        spring.Length(0.25).Should().Be(2.5);
    }

    [Fact]
    public void scaling_a_spring_never_takes_it_below_its_minimum()
    {
        //Arrange
        Spring spring = new Spring(2.0, 1.0);

        //Act
        spring.ScaleBy(0.1);

        //Assert
        spring.IdealDistance.Should().Be(1.0);
        spring.InverseCompressStrength.Should().Be(0.0);
    }

    [Fact]
    public void scaling_a_spring_up_scales_its_ideal_and_stretch_together()
    {
        //Arrange
        Spring spring = new Spring(2.0, 1.0);

        //Act
        Spring scaled = spring * 2.0;

        //Assert
        scaled.IdealDistance.Should().Be(4.0);
        scaled.InverseStretchStrength.Should().Be(4.0);
        scaled.InverseCompressStrength.Should().Be(3.0);
    }

    [Fact]
    public void an_insane_distance_is_reported_and_ignored()
    {
        //Arrange
        // Upstream keeps the old value and reports a programming error rather than
        // aborting, and the spacing engine relies on that.
        Spring spring = new Spring(2.0, 1.0);

        //Act
        spring.SetIdealDistance(double.NaN);

        //Assert
        spring.IdealDistance.Should().Be(2.0);
    }

    [Fact]
    public void ensuring_a_minimum_distance_only_ever_raises_it()
    {
        //Arrange
        Spring spring = new Spring(4.0, 2.0);

        //Act
        spring.EnsureMinDistance(1.0);

        //Assert
        spring.MinDistance.Should().Be(2.0);

        //Act
        spring.EnsureMinDistance(3.0);

        //Assert
        spring.MinDistance.Should().Be(3.0);
    }

    [Fact]
    public void merging_springs_averages_them_with_headroom_above_the_largest_minimum()
    {
        //Arrange
        List<Spring> springs = new List<Spring>
        {
            new Spring(2.0, 1.0),
            new Spring(4.0, 2.0),
        };

        //Act
        Spring merged = Spring.Merge(springs);

        //Assert
        merged.IdealDistance.Should().Be(3.0);
        merged.MinDistance.Should().Be(2.0);
        merged.InverseStretchStrength.Should().Be(3.0);

        // The compress strengths are averaged harmonically: 2 / (1/1 + 1/2).
        merged.InverseCompressStrength.Should().BeApproximately(4.0 / 3.0, 1e-12);
    }

    [Fact]
    public void merging_leaves_at_least_three_tenths_above_the_largest_minimum()
    {
        //Arrange
        List<Spring> springs = new List<Spring>
        {
            new Spring(1.0, 1.0),
            new Spring(1.0, 5.0),
        };

        //Act
        Spring merged = Spring.Merge(springs);

        //Assert
        merged.MinDistance.Should().Be(5.0);
        merged.IdealDistance.Should().BeApproximately(5.3, 1e-12);
    }

    [Fact]
    public void a_chain_at_its_natural_length_needs_no_force()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        for (int i = 0; i < 3; i++)
        {
            spacer.AddSpring(new Spring(2.0, 1.0));
        }

        //Act
        SpacerSolution solution = spacer.Solve(6.0, false);

        //Assert
        solution.Force.Should().Be(0.0);
        solution.Fits.Should().BeTrue();
    }

    [Fact]
    public void stretching_a_chain_spreads_the_force_over_the_flexibilities()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        for (int i = 0; i < 3; i++)
        {
            spacer.AddSpring(new Spring(2.0, 1.0));
        }

        //Act
        SpacerSolution solution = spacer.Solve(9.0, false);

        //Assert
        solution.Force.Should().Be(0.5);
        solution.Fits.Should().BeTrue();
        spacer.ConfigurationLength(solution.Force).Should().Be(9.0);
    }

    [Fact]
    public void compressing_a_chain_produces_a_negative_force()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        for (int i = 0; i < 3; i++)
        {
            spacer.AddSpring(new Spring(2.0, 1.0));
        }

        //Act
        SpacerSolution solution = spacer.Solve(4.5, false);

        //Assert
        solution.Force.Should().Be(-0.5);
        solution.Fits.Should().BeTrue();
        spacer.ConfigurationLength(solution.Force).Should().Be(4.5);
    }

    [Fact]
    public void a_chain_that_cannot_be_squeezed_far_enough_does_not_fit()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        for (int i = 0; i < 3; i++)
        {
            spacer.AddSpring(new Spring(2.0, 1.0));
        }

        //Act
        SpacerSolution solution = spacer.Solve(1.0, false);

        //Assert
        solution.Fits.Should().BeFalse();
    }

    [Fact]
    public void a_ragged_line_never_accepts_a_compressing_solution()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        for (int i = 0; i < 3; i++)
        {
            spacer.AddSpring(new Spring(2.0, 1.0));
        }

        //Act
        SpacerSolution solution = spacer.Solve(4.5, true);

        //Assert
        solution.Force.Should().Be(-0.5);
        solution.Fits.Should().BeFalse();
    }

    [Fact]
    public void spring_positions_accumulate_from_zero()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        for (int i = 0; i < 3; i++)
        {
            spacer.AddSpring(new Spring(2.0, 1.0));
        }

        //Act
        List<double> positions = spacer.SpringPositions(0.5, false);

        //Assert
        positions.Count.Should().Be(4);
        positions[0].Should().Be(0.0);
        positions[1].Should().Be(3.0);
        positions[2].Should().Be(6.0);
        positions[3].Should().Be(9.0);
    }

    [Fact]
    public void a_ragged_line_is_never_stretched_when_laying_out_positions()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        for (int i = 0; i < 3; i++)
        {
            spacer.AddSpring(new Spring(2.0, 1.0));
        }

        //Act
        List<double> positions = spacer.SpringPositions(0.5, true);

        //Assert
        positions[3].Should().Be(6.0);
    }

    [Fact]
    public void a_rod_raises_the_blocking_force_of_every_spring_it_spans()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        spacer.AddSpring(new Spring(2.0, 1.0));
        spacer.AddSpring(new Spring(2.0, 1.0));

        //Act
        spacer.AddRod(0, 2, 5.0);

        //Assert
        spacer.Springs[0].BlockingForce.Should().Be(0.25);
        spacer.Springs[0].MinDistance.Should().Be(2.5);
        spacer.Springs[1].BlockingForce.Should().Be(0.25);
    }

    [Fact]
    public void a_rod_stops_the_line_compressing_past_it()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        spacer.AddSpring(new Spring(2.0, 1.0));
        spacer.AddSpring(new Spring(2.0, 1.0));
        spacer.AddRod(0, 2, 5.0);

        //Act
        SpacerSolution solution = spacer.Solve(3.0, false);

        //Assert
        solution.Fits.Should().BeFalse();
        spacer.ConfigurationLength(solution.Force).Should().Be(5.0);
    }

    [Fact]
    public void a_rod_that_is_already_satisfied_changes_nothing()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        spacer.AddSpring(new Spring(2.0, 1.0));
        spacer.AddSpring(new Spring(2.0, 1.0));

        //Act
        // The chain is 2 long when fully blocked, so a rod of 1 demands nothing.
        spacer.AddRod(0, 2, 1.0);

        //Assert
        spacer.Springs[0].BlockingForce.Should().Be(-1.0);
        spacer.Springs[1].BlockingForce.Should().Be(-1.0);
    }

    [Fact]
    public void a_rod_across_infinitely_stiff_springs_widens_them_directly()
    {
        //Arrange
        // With zero flexibility no force can satisfy the rod, so upstream falls back
        // to sharing the distance out over the springs' ideal distances instead.
        SimpleSpacer spacer = new SimpleSpacer();
        spacer.AddSpring(new Spring(0.0, 0.0));
        spacer.AddSpring(new Spring(0.0, 0.0));

        //Act
        spacer.AddRod(0, 2, 4.0);

        //Assert
        spacer.Springs[0].IdealDistance.Should().Be(2.0);
        spacer.Springs[1].IdealDistance.Should().Be(2.0);
    }

    [Fact]
    public void an_infinite_rod_distance_is_reported_and_ignored()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        spacer.AddSpring(new Spring(2.0, 1.0));

        //Act
        spacer.AddRod(0, 1, double.PositiveInfinity);

        //Assert
        spacer.Springs[0].BlockingForce.Should().Be(-1.0);
    }

    [Fact]
    public void the_force_penalty_is_convex_under_compression()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        spacer.AddSpring(new Spring(2.0, 1.0));

        //Act
        double stretched = spacer.ForcePenalty(10.0, 0.5, false);
        double compressed = spacer.ForcePenalty(10.0, -0.5, false);

        //Assert
        stretched.Should().Be(0.5);

        // -0.5 - (0.5^4 * 2 * -1) worked through: f - 2f^4 with f negative.
        compressed.Should().BeApproximately(-0.625, 1e-12);
    }

    [Fact]
    public void a_ragged_line_is_penalised_on_leftover_whitespace_instead_of_force()
    {
        //Arrange
        SimpleSpacer spacer = new SimpleSpacer();
        spacer.AddSpring(new Spring(2.0, 1.0));
        spacer.AddSpring(new Spring(2.0, 1.0));

        //Act
        double penalty = spacer.ForcePenalty(10.0, 0.5, true);

        //Assert
        penalty.Should().Be(6.0);
    }
}
