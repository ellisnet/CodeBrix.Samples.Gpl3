// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The half of the spacing layer that reads the score: collecting each column's
/// spring and rods and handing them to the solver.
/// <para>
/// Fixtures come from <see cref="SpacingFixtures"/>: anything that reaches the
/// solvers uses <see cref="SpacingFixtures.PrebrokenChain"/>, because the solvers
/// read a line's first and last springs off PREBROKEN pieces — see the fixture-trap
/// note in PORT-COVERAGE.txt.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class LineSpacingTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    [Fact]
    public void a_spring_can_be_read_back_from_the_column_it_was_recorded_on()
    {
        //Arrange
        List<PaperColumn> columns = SpacingFixtures.PlainChain(2, 5.0, 2.0);

        //Act
        Spring spring = SpaceableGrob.GetSpring(columns[0], columns[1]);

        //Assert
        spring.IdealDistance.Should().Be(5.0);
        spring.MinDistance.Should().Be(2.0);
    }

    [Fact]
    public void a_rod_between_the_same_pair_is_raised_not_replaced()
    {
        //Arrange
        // Two independent reasons for a minimum distance both have to be satisfied.
        List<PaperColumn> columns = SpacingFixtures.PlainChain(2, 5.0, 2.0);

        //Act
        SpaceableGrob.AddRod(columns[0], columns[1], 3.0);
        SpaceableGrob.AddRod(columns[0], columns[1], 7.0);
        SpaceableGrob.AddRod(columns[0], columns[1], 4.0);

        //Assert
        object minimums = SpaceableGrob.GetMinimumDistances(columns[0]);
        Pair entry = (Pair)((Pair)minimums).Car;
        entry.Cdr.Should().Be(7.0);
        Pair.Length(minimums).Should().Be(1);
    }

    [Fact]
    public void a_negative_rod_is_ignored()
    {
        //Arrange
        List<PaperColumn> columns = SpacingFixtures.PlainChain(2, 5.0, 2.0);

        //Act
        SpaceableGrob.AddRod(columns[0], columns[1], -1.0);

        //Assert
        SpaceableGrob.GetMinimumDistances(columns[0]).Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void a_line_configuration_places_every_column_boundary()
    {
        //Arrange
        // Three columns, two springs, each ideal 5. Natural length 10.
        List<PaperColumn> columns = SpacingFixtures.PrebrokenChain(3, 5.0, 2.0);

        //Act
        ColumnXPositions positions = LineSpacing.GetLineConfiguration(columns, 10.0, 0.0, false);

        //Assert
        positions.Columns.Count.Should().Be(3);
        positions.Configuration.Count.Should().Be(3);
        positions.Configuration[0].Should().Be(0.0);
        positions.Configuration[1].Should().Be(5.0);
        positions.Configuration[2].Should().Be(10.0);
        positions.SatisfiesConstraints.Should().BeTrue();
    }

    [Fact]
    public void an_indent_shifts_every_position()
    {
        //Arrange
        List<PaperColumn> columns = SpacingFixtures.PrebrokenChain(3, 5.0, 2.0);

        //Act
        ColumnXPositions positions = LineSpacing.GetLineConfiguration(columns, 10.0, 3.0, false);

        //Assert
        positions.Configuration[0].Should().Be(3.0);
        positions.Configuration[2].Should().Be(13.0);
    }

    [Fact]
    public void a_wider_line_stretches_the_columns_apart()
    {
        //Arrange
        List<PaperColumn> columns = SpacingFixtures.PrebrokenChain(3, 5.0, 2.0);

        //Act
        ColumnXPositions positions = LineSpacing.GetLineConfiguration(columns, 20.0, 0.0, false);

        //Assert
        positions.Configuration[2].Should().Be(20.0);
        positions.Configuration[1].Should().Be(10.0);
    }

    [Fact]
    public void a_loose_column_is_kept_out_of_the_spacing_problem()
    {
        //Arrange
        List<PaperColumn> columns = SpacingFixtures.PrebrokenChain(3, 5.0, 2.0);

        // Mark the middle one loose; the solver then springs straight from the first
        // column to the last, which is the bypass spring registered here.
        columns[1].SetObject("between-cols", new Pair(columns[0], columns[2]));
        SpacingFixtures.RegisterSpring(columns[0], columns[2], new Spring(5.0, 2.0));

        //Act
        ColumnXPositions positions = LineSpacing.GetLineConfiguration(columns, 5.0, 0.0, false);

        //Assert
        positions.Columns.Count.Should().Be(2);
        positions.LooseColumns.Should().Equal(new List<PaperColumn> { columns[1] });
    }

    [Fact]
    public void a_forced_break_in_the_middle_means_the_line_does_not_satisfy_its_constraints()
    {
        //Arrange
        List<PaperColumn> columns = SpacingFixtures.PrebrokenChain(3, 5.0, 2.0);
        columns[1].SetProperty("line-break-permission", Sym("force"));

        //Act
        ColumnXPositions positions = LineSpacing.GetLineConfiguration(columns, 10.0, 0.0, false);

        //Assert
        positions.SatisfiesConstraints.Should().BeFalse();
    }

    [Fact]
    public void the_force_matrix_is_square_in_the_number_of_break_points()
    {
        //Arrange
        List<PaperColumn> columns = SpacingFixtures.PrebrokenChain(4, 5.0, 2.0);

        //Act
        List<double> forces = LineSpacing.GetLineForces(columns, 10.0, 0.0, false);

        //Assert
        // Break points: column 0, the two interior breakables, and the end.
        forces.Count.Should().Be(4 * 4);
    }

    [Fact]
    public void a_line_that_fits_exactly_costs_nothing()
    {
        //Arrange
        List<PaperColumn> columns = SpacingFixtures.PrebrokenChain(3, 5.0, 2.0);

        //Act
        List<double> forces = LineSpacing.GetLineForces(columns, 5.0, 0.0, false);

        //Assert
        // Breaks are at 0, 1 and 2 -> a 3x3 matrix. The span from break 0 to break 1
        // is one spring of ideal 5 in a 5-wide line: no force needed.
        forces.Count.Should().Be(9);
        forces[1].Should().Be(0.0);
    }

    [Fact]
    public void an_impossible_single_span_is_scored_rather_than_ruled_out()
    {
        //Arrange
        // A line far too narrow for one spring's minimum. Upstream scores an
        // unbreakable-but-unfitting span -200000 instead of infinity, so the breaker
        // still has something to choose.
        List<PaperColumn> columns = SpacingFixtures.PrebrokenChain(2, 50.0, 40.0);

        //Act
        List<double> forces = LineSpacing.GetLineForces(columns, 1.0, 0.0, false);

        //Assert
        forces[1].Should().Be(-200000.0);
    }

    [Fact]
    public void get_line_forces_reports_unprebroken_break_columns_instead_of_staying_silent()
    {
        //Arrange
        // The fixture trap this pins: un-prebroken columns make every candidate
        // line's end spring a silent default, and the wrong forces look exactly like
        // a solver bug. The port names the actual mistake instead.
        List<PaperColumn> columns = SpacingFixtures.PlainChain(3, 5.0, 2.0);
        TextWriter savedOutput = Warn.Output;
        Warn.Output = TextWriter.Null;
        Warn.RecordMessages = true;
        Warn.ClearMessages();

        try
        {
            //Act
            LineSpacing.GetLineForces(columns, 10.0, 0.0, false);

            //Assert
            Warn.Messages
                .Any(m => m.Contains("get_line_forces") && m.Contains("prebroken"))
                .Should().BeTrue();
        }
        finally
        {
            Warn.RecordMessages = false;
            Warn.ClearMessages();
            Warn.Output = savedOutput;
        }
    }

    [Fact]
    public void get_line_configuration_reports_unprebroken_boundary_columns_instead_of_staying_silent()
    {
        //Arrange
        List<PaperColumn> columns = SpacingFixtures.PlainChain(3, 5.0, 2.0);
        TextWriter savedOutput = Warn.Output;
        Warn.Output = TextWriter.Null;
        Warn.RecordMessages = true;
        Warn.ClearMessages();

        try
        {
            //Act
            LineSpacing.GetLineConfiguration(columns, 10.0, 0.0, false);

            //Assert
            Warn.Messages
                .Any(m => m.Contains("get_line_configuration") && m.Contains("prebroken"))
                .Should().BeTrue();
        }
        finally
        {
            Warn.RecordMessages = false;
            Warn.ClearMessages();
            Warn.Output = savedOutput;
        }
    }

    [Fact]
    public void a_prebroken_chain_runs_both_solvers_without_spacing_diagnostics()
    {
        //Arrange
        // The other half of the fence: the canonical fixture is fully clean, so any
        // diagnostic appearing here means a fixture or solver regression.
        List<PaperColumn> columns = SpacingFixtures.PrebrokenChain(3, 5.0, 2.0);
        TextWriter savedOutput = Warn.Output;
        Warn.Output = TextWriter.Null;
        Warn.RecordMessages = true;
        Warn.ClearMessages();

        try
        {
            //Act
            LineSpacing.GetLineForces(columns, 10.0, 0.0, false);
            LineSpacing.GetLineConfiguration(columns, 10.0, 0.0, false);

            //Assert
            Warn.Messages.Any(m => m.Contains("prebroken")).Should().BeFalse();
            Warn.Messages.Any(m => m.Contains("No spring")).Should().BeFalse();
        }
        finally
        {
            Warn.RecordMessages = false;
            Warn.ClearMessages();
            Warn.Output = savedOutput;
        }
    }
}
