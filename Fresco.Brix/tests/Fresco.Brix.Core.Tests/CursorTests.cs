// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Editor;
using SilverAssertions;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>Replacing text without disturbing what is anchored inside it.</summary>
public class CursorDiffTests
{
    [Fact]
    public void the_replacement_ends_up_in_the_document()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "c d e f g" };

        //Act
        int end = CursorDiff.Replace(document, 2, 5, "x y z");

        //Assert
        document.Text.Should().Be("c x y z g");
        end.Should().Be(7);
    }

    [Fact]
    public void only_the_part_that_differs_is_touched()
    {
        //Arrange — an anchor inside the unchanged head must not move.
        TextDocument document = new TextDocument { Text = "\\relative c' { c d e }" };
        TextAnchor anchor = document.CreateAnchor(3);

        //Act — rewrite the whole thing, changing only the notes.
        CursorDiff.Replace(document, 0, document.TextLength,
            "\\relative c' { g a b }");

        //Assert
        document.Text.Should().Be("\\relative c' { g a b }");
        anchor.Offset.Should().Be(3);
    }

    [Fact]
    public void replacing_text_with_itself_changes_nothing()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "c d e" };
        TextAnchor anchor = document.CreateAnchor(2);

        //Act
        CursorDiff.Replace(document, 0, 5, "c d e");

        //Assert
        document.Text.Should().Be("c d e");
        anchor.Offset.Should().Be(2);
    }

    [Fact]
    public void an_empty_replacement_removes_the_range()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "c d e" };

        //Act
        CursorDiff.Replace(document, 1, 2, string.Empty);

        //Assert
        document.Text.Should().Be("c e");
    }

    [Fact]
    public void inserting_into_an_empty_range_just_inserts()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "c e" };

        //Act
        CursorDiff.Replace(document, 2, 0, "d ");

        //Assert
        document.Text.Should().Be("c d e");
    }

    [Fact]
    public void identical_text_yields_no_differences()
    {
        //Arrange, Act
        var differences = CursorDiff.Differences("same", "same").ToList();

        //Assert
        differences.Should().BeEmpty();
    }

    [Fact]
    public void the_difference_excludes_the_shared_head_and_tail()
    {
        //Arrange, Act
        var differences = CursorDiff.Differences("abcXYZdef", "abcQdef").ToList();

        //Assert
        differences.Count.Should().Be(1);
        differences[0].Start.Should().Be(3);
        differences[0].End.Should().Be(6);
        differences[0].Text.Should().Be("Q");
    }

    [Fact]
    public void a_whole_undo_step_covers_the_rewrite()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "\\relative c' { c d e }" };
        document.UndoStack.ClearAll();

        //Act
        CursorDiff.Replace(document, 0, document.TextLength,
            "\\relative c' { g a b }");
        document.UndoStack.Undo();

        //Assert
        document.Text.Should().Be("\\relative c' { c d e }");
    }
}

/// <summary>The Home key with a little intelligence.</summary>
public class CursorKeysTests
{
    [Fact]
    public void home_goes_to_the_first_real_character()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "  \\relative c' {" };

        //Act — from the end of the line.
        int home = CursorKeys.SmartHome(document, 10);

        //Assert
        home.Should().Be(2);
    }

    [Fact]
    public void home_again_goes_to_column_one()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "  \\relative c' {" };

        //Act — already at the first real character.
        int home = CursorKeys.SmartHome(document, 2);

        //Assert
        home.Should().Be(0);
    }

    [Fact]
    public void home_on_an_unindented_line_goes_to_column_one()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "c d e" };

        //Act
        int home = CursorKeys.SmartHome(document, 4);

        //Assert
        home.Should().Be(0);
    }

    [Fact]
    public void home_on_a_blank_line_stays_at_column_one()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "c\n    \nd" };

        //Act — offset 5 is inside the blank second line.
        int home = CursorKeys.SmartHome(document, 5);

        //Assert
        home.Should().Be(2);
    }

    [Fact]
    public void home_works_on_a_later_line()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "c d e\n    f g a\n" };

        //Act — offset 12 is inside "f g a".
        int home = CursorKeys.SmartHome(document, 12);

        //Assert — line two starts at 6, its first real character at 10.
        home.Should().Be(10);
    }
}
