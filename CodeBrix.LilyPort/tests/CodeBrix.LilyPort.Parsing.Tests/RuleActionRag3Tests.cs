// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 3 — the book, bookpart and score block actions, exercised two
/// ways: whole inputs through the REAL scanner and tables with a scripted host
/// (which is what reaches the mid-rule <c>$@5</c>–<c>$@7</c> actions and their
/// stack access), and direct invocation for the branches whose surrounding grammar
/// (music, markup, output definitions) is not ported yet.
/// </summary>
public class RuleActionRag3Tests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static readonly IReadOnlyDictionary<int, RuleAction> Bound
        = LilyPondRuleActions.Create().Bind(Tables);

    private static RuleAction Action(string identity)
    {
        foreach (TableRule rule in Tables.Rules)
        {
            if (rule.Source != null
                && string.Equals(rule.Source.Identity, identity, StringComparison.Ordinal))
            {
                return Bound[rule.Index];
            }
        }

        throw new InvalidOperationException("no rule named " + identity);
    }

    private static ParseContext NewContext(ScriptedParserHost host)
        => new ParseContext(
            new LalrParser(Tables, new Dictionary<int, RuleAction>()),
            new TokenListInput())
        {
            UserState = host,
        };

    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(string input)
    {
        ScriptedParserHost host = new ScriptedParserHost();
        host.Keywords["book"] = ("BOOK", null);
        host.Keywords["bookpart"] = ("BOOKPART", null);
        host.Keywords["score"] = ("SCORE", null);
        host.Keywords["header"] = ("HEADER", null);

        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);

        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
    }

    private static OutputDef PaperDef()
    {
        OutputDef paper = new OutputDef();
        paper.SetVariable("output-def-kind", Symbol.Intern("paper"));
        return paper;
    }

    private static OutputDef LayoutDef()
    {
        OutputDef layout = new OutputDef();
        layout.SetVariable("output-def-kind", Symbol.Intern("layout"));
        return layout;
    }

    // ------ book blocks, from real text ------

    [Fact]
    public void an_empty_book_from_real_text_builds_a_book_and_reaches_the_book_handler()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("\\book { }");
        OutputDef defaultPaper = PaperDef();
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = defaultPaper;
        host.Globals.Bindings[Symbol.Intern("toplevel-book-handler")] = "book-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.AsText().Should().Be("book-proc");

        Book book = (Book)host.Calls[0].Arguments[0];
        book.Paper.Should().NotBeNull();
        book.Paper.Should().NotBeSameAs(defaultPaper);
        book.Paper.CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("paper"));
        book.Header.Should().BeOfType<FakeModule>();
        book.Origin.Should().NotBeNull();

        // pop_paper ran, and the book announced itself and then stood down.
        host.Globals.Bindings[Symbol.Intern("$papers")].Should().BeSameAs(Nil.Instance);
        host.Globals.Bindings[Symbol.Intern("$current-book")].Should().Be(false);
    }

    [Fact]
    public void a_header_inside_a_book_lands_in_the_books_own_header_module()
    {
        //Arrange
        // The $@5 mid-rule action: \header inside \book opens directly on the book's
        // header module, which is where the ParseContext stack access earns its keep.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\book { \\header { title = \"T\" } }");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = PaperDef();

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Scopes.Should().BeEmpty();

        Book book = (Book)host.Calls[0].Arguments[0];
        ((FakeModule)book.Header).Bindings[Symbol.Intern("title")].AsText().Should().Be("T");
    }

    [Fact]
    public void a_score_inside_a_book_reaches_the_book_score_handler()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\book { \\score { } }");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = PaperDef();
        host.Globals.Bindings[Symbol.Intern("book-score-handler")] = "book-score-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        // The empty score also reported its missing music; the structure still holds.
        host.Calls[0].Procedure.AsText().Should().Be("book-score-proc");
        host.Calls[0].Arguments[0].Should().BeOfType<Book>();
        host.Calls[0].Arguments[1].Should().BeOfType<Score>();
    }

    [Fact]
    public void a_bookpart_inside_a_book_reaches_the_book_bookpart_handler()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\book { \\bookpart { } }");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = PaperDef();
        host.Globals.Bindings[Symbol.Intern("book-bookpart-handler")] = "book-bookpart-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Calls[0].Procedure.AsText().Should().Be("book-bookpart-proc");
        host.Calls[0].Arguments[0].Should().BeOfType<Book>();
        host.Calls[0].Arguments[1].Should().BeOfType<Book>();
        host.Calls[0].Arguments[1].Should().NotBeSameAs(host.Calls[0].Arguments[0]);
    }

    // ------ bookpart blocks, from real text ------

    [Fact]
    public void an_empty_bookpart_from_real_text_is_a_bare_book()
    {
        //Arrange
        // No paper stack work and no header seeding — a bookpart starts genuinely
        // bare, unlike a book.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("\\bookpart { }");
        host.Globals.Bindings[Symbol.Intern("toplevel-bookpart-handler")] = "bookpart-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Calls[0].Procedure.AsText().Should().Be("bookpart-proc");

        Book part = (Book)host.Calls[0].Arguments[0];
        part.Paper.Should().BeNull();
        part.Header.Should().BeSameAs(Nil.Instance);
        host.Globals.Bindings[Symbol.Intern("$current-bookpart")].Should().Be(false);
    }

    [Fact]
    public void a_header_inside_a_bookpart_creates_the_header_module_on_demand()
    {
        //Arrange
        // The $@6 mid-rule action: the bookpart's header is the empty list until the
        // \header arrives, at which point a module is made and scoped.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\bookpart { \\header { piece = \"P\" } }");
        host.Globals.Bindings[Symbol.Intern("toplevel-bookpart-handler")] = "bookpart-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Scopes.Should().BeEmpty();

        Book part = (Book)host.Calls[0].Arguments[0];
        ((FakeModule)part.Header).Bindings[Symbol.Intern("piece")].AsText().Should().Be("P");
    }

    // ------ score blocks, from real text ------

    [Fact]
    public void an_empty_score_reports_missing_music_and_salvages_a_fresh_score()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("\\score { }");
        host.Globals.Bindings[Symbol.Intern("toplevel-score-handler")] = "score-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        host.ErrorLevel.Should().Be(1);
        parser.Diagnostics.Should().HaveCount(1);
        parser.Diagnostics[0].Should().Contain("Missing music in \\score");

        host.Calls[0].Procedure.AsText().Should().Be("score-proc");
        Score score = (Score)host.Calls[0].Arguments[0];
        score.GetMusic().Should().BeSameAs(Nil.Instance);
        score.Origin.Should().NotBeNull();
    }

    [Fact]
    public void a_header_inside_an_empty_score_reaches_the_salvaged_scores_header()
    {
        //Arrange
        // The $@7 mid-rule action's non-score path: with nothing collected yet, a
        // module is CONSED onto $1 — the assignment ParseContext.SetStackValue
        // exists for — and score_body then salvages it as the score's header.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\score { \\header { opus = \"1\" } }");
        host.Globals.Bindings[Symbol.Intern("toplevel-score-handler")] = "score-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        host.ErrorLevel.AsText().Should().Be(1, "the score still has no music");
        host.Scopes.Should().BeEmpty();

        Score score = (Score)host.Calls[0].Arguments[0];
        FakeModule header = (FakeModule)score.GetHeader();
        header.Bindings[Symbol.Intern("opus")].AsText().Should().Be("1");
    }

    // ------ score_items, invoked directly (music/output_def grammar is unported) ------

    [Fact]
    public void a_music_score_item_is_scorified_into_the_score()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject music = new MusicObject(Nil.Instance);

        //Act
        object result = Action("score_items: score_items score_item")(
            context,
            new object[] { Nil.Instance, music },
            new SourceSpan[2],
            default);

        //Assert
        host.ScorifiedMusic.Should().Equal(music);
        Score score = (Score)result;
        score.GetMusic().Should().BeSameAs(music);
    }

    [Fact]
    public void an_output_def_collected_before_music_lands_in_the_scorified_score()
    {
        //Arrange
        // Until music arrives the accumulator is a list; the first music folds the
        // collected definitions into the new score, in parse order.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        OutputDef layout = LayoutDef();
        MusicObject music = new MusicObject(Nil.Instance);
        RuleAction action = Action("score_items: score_items score_item");

        //Act
        object collected = action(
            context, new object[] { Nil.Instance, layout }, new SourceSpan[2], default);
        object result = action(
            context, new object[] { collected, music }, new SourceSpan[2], default);

        //Assert
        ((Pair)collected).Car.Should().BeSameAs(layout);
        Score score = (Score)result;
        score.Defs.Should().Equal(layout);
        score.GetMusic().Should().BeSameAs(music);
    }

    [Fact]
    public void a_layout_output_def_after_music_is_added_to_the_score()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Score score = new Score();
        OutputDef layout = LayoutDef();

        //Act
        object result = Action("score_items: score_items score_item")(
            context,
            new object[] { score, layout },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(score);
        score.Defs.Should().Equal(layout);
    }

    [Fact]
    public void paper_inside_a_score_is_refused_with_layout_advice()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Score score = new Score();

        //Act
        object result = Action("score_items: score_items score_item")(
            context,
            new object[] { score, PaperDef() },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(score);
        score.Defs.Should().BeEmpty();
        host.ErrorLevel.Should().Be(1);
        context.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void a_module_score_item_becomes_the_scores_header()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Score score = new Score();
        FakeModule supplied = new FakeModule();
        supplied.Bindings[Symbol.Intern("title")] = "From Scheme";

        //Act
        object result = Action("score_items: score_items score_item")(
            context,
            new object[] { score, supplied },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(score);
        FakeModule header = (FakeModule)score.GetHeader();
        header.Should().NotBeSameAs(supplied);
        header.Bindings[Symbol.Intern("title")].AsText().Should().Be("From Scheme");
    }

    [Fact]
    public void a_spurious_expression_in_a_score_is_an_error()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Score score = new Score();

        //Act
        Action("score_items: score_items score_item")(
            context,
            new object[] { score, 42L },
            new SourceSpan[2],
            default);

        //Assert
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_score_error_marks_the_score_as_failed()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Score score = new Score();

        //Act
        object result = Action("score_body: score_body error")(
            context,
            new object[] { score, null },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(score);
        score.ErrorFound.Should().BeTrue();
    }

    // ------ book/bookpart bodies, invoked directly ------

    [Fact]
    public void a_markup_in_a_book_body_reaches_the_text_handler_as_a_list()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        host.Globals.Bindings[Symbol.Intern("book-text-handler")] = "text-proc";
        Book book = new Book();

        //Act
        object result = Action("book_body: book_body full_markup")(
            context,
            new object[] { book, "a markup" },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(book);
        host.Calls[0].Procedure.AsText().Should().Be("text-proc");
        host.Calls[0].Arguments[0].Should().BeSameAs(book);
        Pair wrapped = (Pair)host.Calls[0].Arguments[1];
        wrapped.Car.AsText().Should().Be("a markup");
        wrapped.Cdr.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void an_active_paper_def_in_a_book_body_becomes_the_books_paper_and_the_stack_top()
    {
        //Arrange
        // book_body: book_body embedded_scm_active, the paper branch: the book takes
        // the definition AND set_paper swaps it in at the top of $papers — in place,
        // because upstream is scm_set_car_x.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Book book = new Book();
        OutputDef original = PaperDef();
        Pair papers = new Pair(original, Nil.Instance);
        host.Globals.Bindings[Symbol.Intern("$papers")] = papers;
        OutputDef supplied = PaperDef();

        //Act
        object result = Action("book_body: book_body embedded_scm_active")(
            context,
            new object[] { book, supplied },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(book);
        book.Paper.Should().BeSameAs(supplied);
        papers.Car.Should().BeSameAs(supplied);
    }

    [Fact]
    public void an_active_non_paper_def_in_a_book_body_is_an_error()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Book book = new Book();

        //Act
        Action("book_body: book_body embedded_scm_active")(
            context,
            new object[] { book, LayoutDef() },
            new SourceSpan[2],
            default);

        //Assert
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
        book.Paper.Should().BeNull();
    }

    [Fact]
    public void an_active_module_in_a_bookpart_body_creates_the_header_before_merging()
    {
        //Arrange
        // The bookpart variant makes the header module on demand; the book variant
        // relies on the header the body opened with.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Book part = new Book();
        FakeModule supplied = new FakeModule();
        supplied.Bindings[Symbol.Intern("title")] = "T";

        //Act
        Action("bookpart_body: bookpart_body embedded_scm_active")(
            context,
            new object[] { part, supplied },
            new SourceSpan[2],
            default);

        //Assert
        FakeModule header = (FakeModule)part.Header;
        header.Should().NotBeSameAs(supplied);
        header.Bindings[Symbol.Intern("title")].AsText().Should().Be("T");
    }

    [Fact]
    public void book_error_recovery_drops_paper_scores_and_bookparts()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Book book = new Book();
        book.Paper = PaperDef();
        book.AddScore(new Score());
        book.Bookparts = new Pair(new Book(), Nil.Instance);

        //Act
        object result = Action("book_body: book_body error")(
            context,
            new object[] { book, null },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(book);
        book.Paper.Should().BeNull();
        book.Scores.Should().BeSameAs(Nil.Instance);
        book.Bookparts.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void bookpart_error_recovery_keeps_its_collected_bookparts()
    {
        //Arrange
        // Upstream's bookpart recovery resets paper_ and scores_ but NOT bookparts_ —
        // preserved exactly.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Book part = new Book();
        part.Paper = PaperDef();
        part.AddScore(new Score());
        object marker = new Pair(new Book(), Nil.Instance);
        part.Bookparts = marker;

        //Act
        Action("bookpart_body: bookpart_body error")(
            context,
            new object[] { part, null },
            new SourceSpan[2],
            default);

        //Assert
        part.Paper.Should().BeNull();
        part.Scores.Should().BeSameAs(Nil.Instance);
        part.Bookparts.Should().BeSameAs(marker);
    }

    [Fact]
    public void a_scheme_token_in_a_book_body_is_evaluated_and_ignored()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Book book = new Book();

        //Act
        object result = Action("book_body: book_body SCM_TOKEN")(
            context,
            new object[] { book, "(display 1)" },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(book);
        host.EvaluatedTokens.AsText().Should().Equal("(display 1)");
    }
}
