// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using CodeBrix.Platform.UI.AdvancedTextEdit.Folding;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly.Colorizing;
using Fresco.Brix.Ly.Lex;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Token = Fresco.Brix.Ly.Slexing.Token;
using CodeBrix.LilyPort;

namespace Fresco.Brix.Core.Tests;

/// <summary>Reaching the parsed tokens of an open document.</summary>
public class TokenIterTests
{
    private static (TextDocument Document, LyHighlighter Highlighter) Open(string text)
    {
        TextDocument document = new TextDocument { Text = text };
        return (document, new LyHighlighter(document));
    }

    [Fact]
    public void a_lines_tokens_come_back_in_order()
    {
        //Arrange
        var (document, highlighter) = Open("\\relative c' { c d e }\n");

        //Act
        Token[] tokens = TokenIter.Tokens(highlighter, 1);

        //Assert
        tokens.Length.Should().BeGreaterThan(0);
        tokens[0].Text.Should().Be("\\relative");
    }

    [Fact]
    public void a_line_out_of_range_has_no_tokens()
    {
        //Arrange
        var (document, highlighter) = Open("c d e\n");

        //Act, Assert
        TokenIter.Tokens(highlighter, 0).Should().BeEmpty();
        TokenIter.Tokens(highlighter, 99).Should().BeEmpty();
    }

    [Fact]
    public void the_index_names_the_token_the_offset_is_in()
    {
        //Arrange
        var (document, highlighter) = Open("\\relative c' { c d e }");

        //Act — offset 3 is inside "\relative", the first token.
        int index = TokenIter.Index(highlighter, document, 3);

        //Assert
        index.Should().Be(0);
    }

    [Fact]
    public void the_index_at_the_end_of_a_line_is_the_token_count()
    {
        //Arrange
        var (document, highlighter) = Open("c d e");
        int count = TokenIter.Tokens(highlighter, 1).Length;

        //Act
        int index = TokenIter.Index(highlighter, document, 5);

        //Assert
        index.Should().Be(count);
    }

    [Fact]
    public void partitioning_splits_the_line_around_the_offset()
    {
        //Arrange
        var (document, highlighter) = Open("\\relative c' { c d e }");

        //Act — inside the first token.
        var (left, middle, right) = TokenIter.Partition(highlighter, document, 3);

        //Assert
        left.Should().BeEmpty();
        middle.Should().NotBeNull();
        middle.Text.Should().Be("\\relative");
        right.Should().NotBeEmpty();
    }

    [Fact]
    public void partitioning_at_a_token_boundary_has_no_middle()
    {
        //Arrange
        var (document, highlighter) = Open("\\relative c' { c d e }");

        //Act — offset 0 is before the first token, not inside it.
        var (left, middle, right) = TokenIter.Partition(highlighter, document, 0);

        //Assert
        left.Should().BeEmpty();
        middle.Should().BeNull();
        right.Should().NotBeEmpty();
    }

    [Fact]
    public void a_token_can_be_found_by_its_text()
    {
        //Arrange
        var (document, highlighter) = Open("\\relative c' { c d e }");

        //Act
        Token found = TokenIter.Find("{", TokenIter.Tokens(highlighter, 1));

        //Assert
        found.Should().NotBeNull();
    }

    [Fact]
    public void every_token_of_a_document_can_be_walked()
    {
        //Arrange
        var (document, highlighter) = Open("\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"\n\\relative c' { c }\n");

        //Act
        List<Token> tokens = TokenIter.AllTokens(highlighter).ToList();

        //Assert
        tokens.Select(t => t.Text).Should().Contain("\\version");
        tokens.Select(t => t.Text).Should().Contain("\\relative");
    }

    [Fact]
    public void a_tokens_document_offset_accounts_for_its_line()
    {
        //Arrange
        var (document, highlighter) = Open("c d e\n\\relative c' { c }\n");
        Token relative = TokenIter.Tokens(highlighter, 2)[0];

        //Act
        int offset = TokenIter.OffsetOf(document, 2, relative);

        //Assert
        offset.Should().Be(6);
        document.GetText(offset, 9).Should().Be("\\relative");
    }
}

/// <summary>What counts as a word when moving through LilyPond source.</summary>
public class WordBoundaryTests
{
    [Fact]
    public void a_lilypond_command_is_one_word()
    {
        //Arrange, Act
        IReadOnlyList<(int Start, int End)> words
            = WordBoundary.Boundaries("\\relative c'");

        //Assert — the first real word spans the whole command.
        words.Should().Contain((0, 9));
    }

    [Fact]
    public void a_directed_command_keeps_its_direction_character()
    {
        //Arrange, Act
        IReadOnlyList<(int Start, int End)> words
            = WordBoundary.Boundaries("c-\\markup { x }");

        //Assert
        words.Should().Contain((1, 9));
    }

    [Fact]
    public void a_hyphenated_word_holds_together()
    {
        //Arrange, Act
        IReadOnlyList<(int Start, int End)> words
            = WordBoundary.Boundaries("page-breaking");

        //Assert
        words.Should().Contain((0, 13));
    }

    [Fact]
    public void moving_right_lands_on_the_next_word()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "\\relative c' { c d e }" };

        //Act
        int next = WordBoundary.NextWord(document, 0);

        //Assert
        document.GetText(next, 1).Should().Be("c");
        next.Should().Be(10);
    }

    [Fact]
    public void moving_left_lands_on_the_previous_word()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "\\relative c' { c d e }" };

        //Act
        int previous = WordBoundary.PreviousWord(document, 10);

        //Assert
        previous.Should().Be(0);
    }

    [Fact]
    public void moving_right_crosses_into_the_next_line()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "c\nd\n" };

        //Act
        int next = WordBoundary.NextWord(document, 1);

        //Assert
        document.GetLineByOffset(next).LineNumber.Should().Be(2);
    }

    [Fact]
    public void moving_left_from_the_start_stays_at_the_start()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "c d e\n" };

        //Act
        int previous = WordBoundary.PreviousWord(document, 0);

        //Assert
        previous.Should().Be(0);
    }

    [Fact]
    public void the_start_and_end_of_the_word_under_the_cursor_are_found()
    {
        //Arrange
        TextDocument document = new TextDocument { Text = "\\relative c'" };

        //Act
        int start = WordBoundary.StartOfWord(document, 4);
        int end = WordBoundary.EndOfWord(document, 4);

        //Assert
        start.Should().Be(0);
        end.Should().Be(9);
    }
}

/// <summary>Working out what can be folded.</summary>
public class FoldingTests
{
    private static (TextDocument Document, LyFoldingStrategy Strategy) Open(string text)
    {
        TextDocument document = new TextDocument { Text = text };
        return (document, new LyFoldingStrategy(new LyHighlighter(document)));
    }

    [Fact]
    public void a_multi_line_block_can_be_folded()
    {
        //Arrange
        var (document, strategy) = Open("\\score {\n  \\relative c' {\n    c d e\n  }\n}\n");

        //Act
        List<NewFolding> foldings = strategy.CreateFoldings(document).ToList();

        //Assert
        foldings.Count.Should().Be(2);
    }

    [Fact]
    public void a_block_that_stays_on_one_line_is_not_foldable()
    {
        //Arrange
        var (document, strategy) = Open("\\relative c' { c d e }\n");

        //Act
        List<NewFolding> foldings = strategy.CreateFoldings(document).ToList();

        //Assert
        foldings.Should().BeEmpty();
    }

    [Fact]
    public void a_brace_inside_a_string_does_not_open_a_fold()
    {
        //Arrange — deciding from the tokens rather than the characters is the
        //whole reason this reads the tokenization.
        var (document, strategy) = Open("\\header {\n  title = \"a { brace\"\n}\n");

        //Act
        List<NewFolding> foldings = strategy.CreateFoldings(document).ToList();

        //Assert
        foldings.Count.Should().Be(1);
    }

    [Fact]
    public void a_brace_inside_a_comment_does_not_open_a_fold()
    {
        //Arrange
        var (document, strategy) = Open("\\header {\n  % a { brace\n}\n");

        //Act
        List<NewFolding> foldings = strategy.CreateFoldings(document).ToList();

        //Assert
        foldings.Count.Should().Be(1);
    }

    [Fact]
    public void a_block_comment_folds_as_one()
    {
        //Arrange
        var (document, strategy) = Open("%{\nsome notes\nover lines\n%}\nc d e\n");

        //Act
        List<NewFolding> foldings = strategy.CreateFoldings(document).ToList();

        //Assert
        foldings.Count.Should().Be(1);
    }

    [Fact]
    public void an_unclosed_block_is_simply_not_foldable()
    {
        //Arrange
        var (document, strategy) = Open("\\score {\n  c d e\n");

        //Act
        List<NewFolding> foldings = strategy.CreateFoldings(document).ToList();

        //Assert
        foldings.Should().BeEmpty();
    }

    [Fact]
    public void foldings_come_back_in_start_order()
    {
        //Arrange
        var (document, strategy) = Open(
            "\\header {\n  x = 1\n}\n\\score {\n  \\relative c' {\n c\n }\n}\n");

        //Act
        List<NewFolding> foldings = strategy.CreateFoldings(document).ToList();

        //Assert
        foldings.Select(f => f.StartOffset).Should().BeInAscendingOrder();
    }
}

/// <summary>Saying in plain words where in a document the cursor is.</summary>
public class SimpleStateTests
{
    private static IReadOnlyList<string> Describe(string text, int lineNumber)
    {
        TextDocument document = new TextDocument { Text = text };
        LyHighlighter highlighter = new LyHighlighter(document);
        return SimpleState.Describe(highlighter.StateAtLineEnd(lineNumber));
    }

    [Fact]
    public void the_first_word_is_always_the_mode()
    {
        //Arrange, Act
        IReadOnlyList<string> names = Describe("c d e\n", 1);

        //Assert
        names.First().Should().Be("lilypond");
    }

    [Fact]
    public void a_nested_position_is_described_outermost_first()
    {
        //Arrange
        string text = "\\book {\n  \\header {\n    title = \\markup { #\"";

        //Act
        IReadOnlyList<string> names = Describe(text, 3);

        //Assert
        names.Should().BeEquivalentTo(
            new[] { "lilypond", "book", "header", "markup", "scheme", "string" });
    }

    [Fact]
    public void a_repeated_name_is_collapsed()
    {
        //Arrange
        string text = "\\score {\n  \\relative c' {\n    c d e";

        //Act
        IReadOnlyList<string> names = Describe(text, 3);

        //Assert — "music" appears once however many music lists are nested.
        names.Count(n => n == "music").Should().Be(1);
    }

    [Fact]
    public void a_null_state_describes_nothing()
    {
        //Arrange, Act
        IReadOnlyList<string> names = SimpleState.Describe(null);

        //Assert
        names.Should().BeEmpty();
    }
}

/// <summary>The Fonts and Colors model.</summary>
public class TextFormatsTests
{
    [Fact]
    public void a_style_inherits_its_default_styles_look()
    {
        //Arrange
        TextFormatData scheme = new TextFormatData();

        //Act — lilypond "command" inherits "function" and adds nothing.
        TextFormat format = scheme.TextFormatFor("lilypond", "command");

        //Assert
        format.IsBold.Should().BeTrue();
        format.Foreground.Should().NotBeNull();
        TextFormat.FormatColor(format.Foreground.Value).Should().Be("#0000c0");
    }

    [Fact]
    public void a_mode_style_overrides_what_it_inherits()
    {
        //Arrange
        TextFormatData scheme = new TextFormatData();

        //Act — lilypond "markup" inherits bold blue and overrides both.
        TextFormat format = scheme.TextFormatFor("lilypond", "markup");

        //Assert
        format.IsBold.Should().BeFalse();
        TextFormat.FormatColor(format.Foreground.Value).Should().Be("#008000");
    }

    [Fact]
    public void a_style_with_no_default_and_no_override_looks_like_nothing()
    {
        //Arrange
        TextFormatData scheme = new TextFormatData();

        //Act — lilypond "octave" has neither.
        TextFormat format = scheme.TextFormatFor("lilypond", "octave");

        //Assert
        format.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void a_colorize_style_resolves_the_same_way()
    {
        //Arrange
        TextFormatData scheme = new TextFormatData();
        TokenMapper<CssClass> mapper = Colorize.CssMapper();

        //Act
        CssClass style = mapper.ValueForClass(typeof(Lily.MarkupStart));
        TextFormat format = scheme.TextFormatFor(style);

        //Assert
        style.Name.Should().Be("markup");
        TextFormat.FormatColor(format.Foreground.Value).Should().Be("#008000");
    }

    [Theory]
    [InlineData("#ff0000", 0xff, 0x00, 0x00)]
    [InlineData("#0000c0", 0x00, 0x00, 0xc0)]
    [InlineData("#abc", 0xaa, 0xbb, 0xcc)]
    public void a_colour_reads_from_its_css_form(string text, int r, int g, int b)
    {
        //Arrange, Act
        var color = TextFormat.ParseColor(text);

        //Assert
        color.Should().NotBeNull();
        color.Value.R.Should().Be((byte)r);
        color.Value.G.Should().Be((byte)g);
        color.Value.B.Should().Be((byte)b);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("")]
    [InlineData(null)]
    public void a_colour_that_is_not_a_hex_triple_reads_as_nothing(string text)
    {
        //Arrange, Act, Assert
        TextFormat.ParseColor(text).Should().BeNull();
    }

    [Fact]
    public void a_format_survives_a_css_round_trip()
    {
        //Arrange
        TextFormat format = new TextFormat
        {
            IsBold = true,
            IsItalic = false,
            Foreground = TextFormat.ParseColor("#123456"),
        };

        //Act
        TextFormat again = TextFormat.FromCss(format.ToCss());

        //Assert
        again.IsBold.Should().BeTrue();
        again.IsItalic.Should().BeFalse();
        TextFormat.FormatColor(again.Foreground.Value).Should().Be("#123456");
    }

    [Fact]
    public void the_scheme_converts_back_to_a_colorize_scheme()
    {
        //Arrange
        TextFormatData scheme = new TextFormatData();

        //Act
        CssScheme css = scheme.ToCssScheme();

        //Assert
        css.BaseStyle("keyword")["font-weight"].Should().Be("bold");
        css.ModeStyle("lilypond", "markup")["color"].Should().Be("#008000");
    }

    [Fact]
    public void every_style_the_mapping_names_has_a_format()
    {
        //Arrange
        TextFormatData scheme = new TextFormatData();

        //Act
        List<string> missing = Colorize.DefaultMapping()
            .SelectMany(g => g.Styles.Select(s => new { g.Mode, s.Name }))
            .Where(s => scheme.ModeStyle(s.Mode, s.Name) == null)
            .Select(s => s.Mode + "/" + s.Name)
            .ToList();

        //Assert
        missing.Should().BeEmpty();
    }
}
