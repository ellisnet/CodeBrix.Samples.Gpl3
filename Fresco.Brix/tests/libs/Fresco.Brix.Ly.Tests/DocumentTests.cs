// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Slexing;
using SilverAssertions;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// The document machinery (Document, Cursor, Runner, Source) against
/// python-ly itself: every expectation below was READ OFF a python-ly v0.9.10
/// oracle run (ly/document.py's own docstring examples plus probes executed
/// against the checkout on 2026-08-20) — nothing is recorded from the port.
/// </summary>
public class DocumentTests
{
    private static List<string> Render(IEnumerable<Token> tokens)
        => tokens.Select(t => $"{t.GetType().Name} {t.Pos} |{t.Text}|").ToList();

    [Fact]
    public void the_docstring_edit_example_inserts_inside_a_writing_scope()
    {
        //Arrange
        //ly/document.py's own module docstring: d[5:5] = 'different '.
        Document d = new Document("some string");

        //Act
        using (d.Writing())
        {
            d.SetText(5, 5, "different ");
        }

        //Assert
        d.PlainText().Should().Be("some different string");
        d.Modified.Should().BeTrue();
    }

    [Fact]
    public void the_cursor_docstring_example_moves_its_end_with_the_insert()
    {
        //Arrange
        //Cursor's docstring: insert 'new text' at 8..8, cursor 8..8 -> 8..16.
        Document d = new Document("hi there, folks!");
        Cursor c = new Cursor(d, 8, 8);

        //Act
        using (d.Writing())
        {
            d.SetText(8, 8, "new text");
        }

        //Assert
        c.Start.Should().Be(8);
        c.End.Should().Be(16);
    }

    [Fact]
    public void an_edit_that_opens_a_block_comment_retokenizes_the_following_lines()
    {
        //Arrange
        //Oracle: "c d e\nf g a" line 2 tokenizes Name/Space/...; after
        //inserting "%{ " at 0, line 1 is BlockCommentStart+BlockComment and
        //line 2 is one BlockComment "f g a".
        Document d = new Document("c d e\nf g a", "lilypond");
        Render(d.Tokens(d[1])).Should().Equal(new List<string>
        {
            "Name 0 |f|", "Space 1 | |", "Name 2 |g|", "Space 3 | |", "Name 4 |a|",
        });

        //Act
        using (d.Writing())
        {
            d.SetText(0, 0, "%{ ");
        }

        //Assert
        Render(d.Tokens(d[0])).Should().Equal(new List<string>
        {
            "BlockCommentStart 0 |%{|", "BlockComment 2 | c d e|",
        });
        Render(d.Tokens(d[1])).Should().Equal(new List<string>
        {
            "BlockComment 0 |f g a|",
        });
    }

    [Fact]
    public void a_source_yields_only_tokens_inside_the_range_by_default()
    {
        //Arrange
        //Oracle: "c4 d8 e2\nf1 g4", cursor 3..11, INSIDE.
        Document d = new Document("c4 d8 e2\nf1 g4", "lilypond");
        Cursor cursor = new Cursor(d, 3, 11);

        //Act
        Source source = new Source(cursor);

        //Assert
        Render(source).Should().Equal(new List<string>
        {
            "Name 3 |d|", "DecimalValue 4 |8|", "Space 5 | |", "Name 6 |e|",
            "DecimalValue 7 |2|", "Newline 8 |\n|", "Name 0 |f|",
            "DecimalValue 1 |1|",
        });
    }

    [Fact]
    public void partial_and_outside_modes_treat_the_cut_token_as_the_oracle_does()
    {
        //Arrange
        //Oracle: cursor 4..11 cuts into "d8"; PARTIAL keeps the 8 but not the
        //d; OUTSIDE keeps the whole d8 token AND the trailing space at the end.
        Document d = new Document("c4 d8 e2\nf1 g4", "lilypond");
        Cursor cursor = new Cursor(d, 4, 11);

        //Act
        List<string> partial = Render(
            new Source(cursor, partial: OverlapMode.Partial));
        List<string> outside = Render(
            new Source(cursor, partial: OverlapMode.Outside));

        //Assert
        partial.Should().Equal(new List<string>
        {
            "DecimalValue 4 |8|", "Space 5 | |", "Name 6 |e|",
            "DecimalValue 7 |2|", "Newline 8 |\n|", "Name 0 |f|",
            "DecimalValue 1 |1|",
        });
        outside.Should().Equal(new List<string>
        {
            "Name 3 |d|", "DecimalValue 4 |8|", "Space 5 | |", "Name 6 |e|",
            "DecimalValue 7 |2|", "Newline 8 |\n|", "Name 0 |f|",
            "DecimalValue 1 |1|", "Space 2 | |",
        });
    }

    [Fact]
    public void a_runner_crossing_a_block_boundary_yields_a_synthetic_newline()
    {
        //Arrange
        //Oracle: "c d\ne f" forward() = c, space, d, Newline(pos 3), e, space, f.
        Document d = new Document("c d\ne f", "lilypond");
        Runner runner = new Runner(d);

        //Act + Assert
        Render(runner.Forward()).Should().Equal(new List<string>
        {
            "Name 0 |c|", "Space 1 | |", "Name 2 |d|", "Newline 3 |\n|",
            "Name 0 |e|", "Space 1 | |", "Name 2 |f|",
        });
    }

    [Fact]
    public void two_edits_in_one_batch_apply_without_disturbing_each_other()
    {
        //Arrange
        //Oracle: "aaa bbb ccc" with 0..3='xx' and 8..11='yy' -> "xx bbb yy".
        Document d = new Document("aaa bbb ccc");

        //Act
        using (d.Writing())
        {
            d.SetText(0, 3, "xx");
            d.SetText(8, 11, "yy");
        }

        //Assert
        d.PlainText().Should().Be("xx bbb yy");
    }

    [Fact]
    public void get_block_maps_positions_to_lines_and_size_counts_characters()
    {
        //Arrange
        Document d = new Document("ab\ncd\nef");

        //Act + Assert
        d.Count.Should().Be(3);
        d.Size().Should().Be(8);
        d.Index(d.GetBlock(0)).Should().Be(0);
        d.Index(d.GetBlock(2)).Should().Be(0);
        d.Index(d.GetBlock(3)).Should().Be(1);
        d.Index(d.GetBlock(7)).Should().Be(2);
        d.GetBlock(9).Should().BeNull();
        d.Text(d.GetBlock(3)).Should().Be("cd");
        d.Position(d.GetBlock(3)).Should().Be(3);
    }
}
