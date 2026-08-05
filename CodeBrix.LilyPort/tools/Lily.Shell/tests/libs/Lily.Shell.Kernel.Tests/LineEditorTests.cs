// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel.Editing;
using SilverAssertions;
using Xunit;

namespace Lily.Shell.Kernel.Tests;

public class LineEditorTests
{
    [Fact]
    public void insert_at_end_echoes_just_the_character()
    {
        //Arrange
        var editor = new LineEditor();

        //Act
        var echo = editor.Insert('a');

        //Assert
        echo.Should().Be("a");
        editor.Text.Should().Be("a");
        editor.CursorPosition.Should().Be(1);
    }

    [Fact]
    public void insert_mid_line_rewrites_the_tail_and_moves_back()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("abc");
        editor.MoveLeft();
        editor.MoveLeft();

        //Act
        var echo = editor.Insert('X');

        //Assert
        editor.Text.Should().Be("aXbc");
        editor.CursorPosition.Should().Be(2);
        echo.Should().Be("Xbc\x1b[2D");
    }

    [Fact]
    public void backspace_at_end_erases_the_last_character()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("ab");

        //Act
        var echo = editor.Backspace();

        //Assert
        editor.Text.Should().Be("a");
        echo.Should().Be("\b \x1b[1D");
    }

    [Fact]
    public void backspace_at_start_is_a_no_op()
    {
        //Arrange
        var editor = new LineEditor();

        //Act
        var echo = editor.Backspace();

        //Assert
        echo.Should().Be("");
        editor.Text.Should().Be("");
    }

    [Fact]
    public void delete_removes_the_character_under_the_cursor()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("abc");
        editor.MoveHome();

        //Act
        var echo = editor.Delete();

        //Assert
        editor.Text.Should().Be("bc");
        editor.CursorPosition.Should().Be(0);
        echo.Should().Be("bc \x1b[3D");
    }

    [Fact]
    public void home_and_end_move_across_the_whole_line()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("hello");

        //Act
        var homeEcho = editor.MoveHome();
        var endEcho = editor.MoveEnd();

        //Assert
        homeEcho.Should().Be("\x1b[5D");
        endEcho.Should().Be("\x1b[5C");
        editor.CursorPosition.Should().Be(5);
    }

    [Fact]
    public void move_right_at_end_is_a_no_op()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("a");

        //Act
        var echo = editor.MoveRight();

        //Assert
        echo.Should().Be("");
        editor.CursorPosition.Should().Be(1);
    }

    [Fact]
    public void replace_with_erases_the_line_and_writes_the_new_text()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("old line");

        //Act
        var echo = editor.ReplaceWith("new");

        //Assert
        editor.Text.Should().Be("new");
        editor.CursorPosition.Should().Be(3);
        echo.Should().Be("\x1b[8D\x1b[Knew");
    }

    [Fact]
    public void take_line_returns_the_text_and_resets_the_editor()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("engrave demo");

        //Act
        var line = editor.TakeLine();

        //Assert
        line.Should().Be("engrave demo");
        editor.Text.Should().Be("");
        editor.CursorPosition.Should().Be(0);
    }

    [Fact]
    public void redraw_reemits_text_and_restores_the_cursor_column()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("abcd");
        editor.MoveLeft();

        //Act
        var echo = editor.Redraw();

        //Assert
        echo.Should().Be("abcd\x1b[1D");
        editor.Text.Should().Be("abcd");
        editor.CursorPosition.Should().Be(3);
    }

    [Fact]
    public void paste_insert_mid_line_keeps_the_tail()
    {
        //Arrange
        var editor = new LineEditor();
        editor.Insert("ad");
        editor.MoveLeft();

        //Act
        var echo = editor.Insert("bc");

        //Assert
        editor.Text.Should().Be("abcd");
        editor.CursorPosition.Should().Be(3);
        echo.Should().Be("bcd\x1b[1D");
    }
}
