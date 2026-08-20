// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.TerminalView.Rendering;
using CodeBrix.Terminal.Engine;
using SilverAssertions;
using Xunit;

namespace Lily.Shell.TerminalView.Tests;

public class RunBuilderTests
{
    private static BufferLine MakeLine(int cols) => new(cols, CharData.Null);

    private static CharData Cell(char c, int attribute = CharData.DefaultAttr, int width = 1) =>
        new(attribute, c, width, c);

    [Fact]
    public void an_empty_line_yields_no_segments()
    {
        //Act
        var segments = RunBuilder.BuildRuns(MakeLine(10));

        //Assert
        segments.Should().BeEmpty();
    }

    [Fact]
    public void adjacent_cells_with_one_attribute_coalesce()
    {
        //Arrange
        var line = MakeLine(10);
        line[0] = Cell('h');
        line[1] = Cell('i');

        //Act
        var segments = RunBuilder.BuildRuns(line);

        //Assert
        segments.Should().HaveCount(1);
        segments[0].Text.Should().Be("hi");
        segments[0].StartColumn.Should().Be(0);
        segments[0].CellCount.Should().Be(2);
        segments[0].IsWide.Should().Be(false);
    }

    [Fact]
    public void an_attribute_change_splits_the_run()
    {
        //Arrange
        var red = (1 << 9) | 256;
        var line = MakeLine(10);
        line[0] = Cell('a');
        line[1] = Cell('b', red);

        //Act
        var segments = RunBuilder.BuildRuns(line);

        //Assert
        segments.Should().HaveCount(2);
        segments[0].Text.Should().Be("a");
        segments[1].Text.Should().Be("b");
        segments[1].StartColumn.Should().Be(1);
        segments[1].Attribute.Should().Be(red);
    }

    [Fact]
    public void a_wide_character_forms_its_own_two_cell_segment()
    {
        //Arrange - a CJK glyph occupies two cells; the second is a zero-width continuation
        var line = MakeLine(10);
        line[0] = Cell('a');
        line[1] = new CharData(CharData.DefaultAttr, '中', 2, 0x4e2d);
        line[2] = new CharData(CharData.DefaultAttr, ' ', 0, 0);
        line[3] = Cell('b');

        //Act
        var segments = RunBuilder.BuildRuns(line);

        //Assert
        segments.Should().HaveCount(3);
        segments[1].IsWide.Should().Be(true);
        segments[1].Text.Should().Be("中");
        segments[1].StartColumn.Should().Be(1);
        segments[1].CellCount.Should().Be(2);
        segments[2].Text.Should().Be("b");
        segments[2].StartColumn.Should().Be(3);
    }

    [Fact]
    public void null_gaps_between_characters_render_as_spaces()
    {
        //Arrange - "a", an untouched cell, "b": the gap joins the run as a space
        var line = MakeLine(10);
        line[0] = Cell('a');
        line[2] = Cell('b');

        //Act
        var segments = RunBuilder.BuildRuns(line);

        //Assert
        segments.Should().HaveCount(1);
        segments[0].Text.Should().Be("a b");
        segments[0].CellCount.Should().Be(3);
    }

    [Fact]
    public void trailing_blank_cells_are_trimmed()
    {
        //Arrange
        var line = MakeLine(10);
        line[0] = Cell('x');

        //Act
        var segments = RunBuilder.BuildRuns(line);

        //Assert
        segments.Should().HaveCount(1);
        segments[0].Text.Should().Be("x");
        segments[0].CellCount.Should().Be(1);
    }
}
