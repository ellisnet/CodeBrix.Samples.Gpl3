// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.TerminalView.Rendering;
using SilverAssertions;
using Xunit;

namespace Lily.Shell.TerminalView.Tests;

public class SelectionGeometryTests
{
    [Fact]
    public void pixels_map_to_the_containing_cell()
    {
        //Arrange - a 10x20 cell grid, 80x25
        var cell = TestCell(10f, 20f);

        //Act
        var origin = SelectionGeometry.ToCell(0, 0, cell, 80, 25);
        var mid = SelectionGeometry.ToCell(25, 45, cell, 80, 25);

        //Assert
        origin.Should().Be((0, 0));
        mid.Should().Be((2, 2));
    }

    [Fact]
    public void pixels_beyond_the_grid_clamp_to_the_edges()
    {
        //Arrange
        var cell = TestCell(10f, 20f);

        //Act
        var negative = SelectionGeometry.ToCell(-30, -5, cell, 80, 25);
        var beyond = SelectionGeometry.ToCell(9999, 9999, cell, 80, 25);

        //Assert
        negative.Should().Be((0, 0));
        beyond.Should().Be((79, 24));
    }

    [Fact]
    public void a_single_row_selection_ends_before_the_exclusive_end_column()
    {
        //Act
        var hit = SelectionGeometry.TryGetRowSpan(3, 10, 7, 10, 10, 80,
            out var first, out var last);

        //Assert - end column 7 is exclusive (matching GetSelectedText)
        hit.Should().Be(true);
        first.Should().Be(3);
        last.Should().Be(6);
    }

    [Fact]
    public void a_zero_width_selection_does_not_hit()
    {
        //Assert
        SelectionGeometry.TryGetRowSpan(5, 10, 5, 10, 10, 80, out _, out _)
            .Should().Be(false);
    }

    [Fact]
    public void a_backwards_drag_normalizes_its_endpoints()
    {
        //Act - selection dragged up and to the left: end before start
        var hit = SelectionGeometry.TryGetRowSpan(7, 12, 3, 10, 11, 80,
            out var first, out var last);

        //Assert - the middle row spans the full width
        hit.Should().Be(true);
        first.Should().Be(0);
        last.Should().Be(79);
    }

    [Fact]
    public void edge_rows_of_a_multi_row_selection_are_partial()
    {
        //Act
        SelectionGeometry.TryGetRowSpan(5, 10, 2, 12, 10, 80, out var firstTop, out var lastTop);
        SelectionGeometry.TryGetRowSpan(5, 10, 2, 12, 12, 80, out var firstBottom, out var lastBottom);

        //Assert - top row runs from the anchor to the right edge, bottom from
        //  the left edge to just before the exclusive end column
        firstTop.Should().Be(5);
        lastTop.Should().Be(79);
        firstBottom.Should().Be(0);
        lastBottom.Should().Be(1);
    }

    [Fact]
    public void rows_outside_the_selection_do_not_hit()
    {
        //Assert
        SelectionGeometry.TryGetRowSpan(0, 10, 5, 12, 9, 80, out _, out _).Should().Be(false);
        SelectionGeometry.TryGetRowSpan(0, 10, 5, 12, 13, 80, out _, out _).Should().Be(false);
    }

    private static CellMetrics TestCell(float width, float height) =>
        new(width, height, height * 0.8f);
}
