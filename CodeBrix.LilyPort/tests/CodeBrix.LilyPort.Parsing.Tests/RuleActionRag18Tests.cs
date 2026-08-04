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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 18 — markup modes, lists and structure, from REAL TEXT wherever
/// the grammar allows it, which here is nearly everywhere: <c>\markup</c> is reachable
/// at top level, so a whole markup can be lexed, parsed, reduced and then read back out
/// of the <c>toplevel-text-handler</c> call it ends in.
/// <para>
/// Every test attaches the scanner to the host, and that is not optional here. Markup
/// mode is a real start condition: a bare word in it is a <c>SYMBOL</c> rather than a
/// note name or a keyword, and <c>\command</c> is looked up as a markup command before
/// anything else. Without the attachment the scanner would never leave INITIAL and
/// these tests would be reading a quietly different token stream — the trap RAG16
/// recorded.
/// </para>
/// </summary>
public class RuleActionRag18Tests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static readonly IReadOnlyDictionary<int, RuleAction> Bound
        = LilyPondRuleActions.Create().Bind(Tables);

    // Asserts a clean parse, and says WHAT went wrong when it was not.
    private static void NoErrors(LalrParser parser)
        => string.Join("; ", parser.Diagnostics).Should().BeEmpty();

    /// <summary>
    /// Sets a run up over markup text at top level, with the scanner attached so the
    /// mode pushes really happen.
    /// </summary>
    /// <param name="input">The text to parse.</param>
    /// <returns>The parser, the scanner and the host.</returns>
    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(
        string input)
    {
        ScriptedParserHost host = new ScriptedParserHost { MakeRealMusic = true };

        host.Keywords["markup"] = ("MARKUP", null);
        host.Keywords["markuplist"] = ("MARKUPLIST", null);
        host.Keywords["etc"] = ("ETC", null);
        host.Keywords["new"] = ("NEWCONTEXT", null);

        host.Globals.Bindings[Symbol.Intern("toplevel-text-handler")] = "text-proc";
        host.Globals.Bindings[Symbol.Intern("toplevel-score-handler")] = "score-proc";
        host.Globals.Bindings[Symbol.Intern("toplevel-music-handler")] = "music-proc";

        // The note table the NOTES mode consults inside a \score.
        host.WordScans[Symbol.Intern("c")] = ("NOTENAME_PITCH", new Pitch(0, 0, Rational.Zero));

        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        host.Scanner = scanner;

        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
    }

    /// <summary>Returns the one markup the toplevel text handler was given.</summary>
    /// <param name="host">The host that recorded the call.</param>
    /// <returns>The markup.</returns>
    private static object HandledMarkup(ScriptedParserHost host)
    {
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.Should().Be("text-proc");
        List<object> handed = Pair.ToList(host.Calls[0].Arguments[0]);
        handed.Should().HaveCount(1);
        return handed[0];
    }

    /// <summary>Returns the markup LIST the toplevel text handler was given.</summary>
    /// <param name="host">The host that recorded the call.</param>
    /// <returns>The list's elements.</returns>
    private static List<object> HandledMarkupList(ScriptedParserHost host)
    {
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.Should().Be("text-proc");
        return Pair.ToList(host.Calls[0].Arguments[0]);
    }

    // ------ markup_mode, markup_mode_word, full_markup: the word cases ------

    [Fact]
    public void a_markup_of_one_bare_word_is_that_word()
    {
        //Arrange
        // \markup hello — markup_mode pushes the mode, markup_mode_word pops it and
        // takes the word, and full_markup: markup_mode_word makes it a markup, which
        // for a string is the identity (make_simple_markup).
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("\\markup hello");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        HandledMarkup(host).Should().Be("hello");
    }

    [Fact]
    public void a_markup_of_one_quoted_string_is_that_string()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \"two words\"");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        HandledMarkup(host).Should().Be("two words");
    }

    [Fact]
    public void the_lexer_enters_markup_mode_and_leaves_it_again()
    {
        //Arrange
        // The mode is pushed by markup_mode and popped by whichever rule finishes the
        // markup — here markup_mode_word. A markup that did not pop would leave the
        // REST OF THE FILE lexing as markup, which is why this is asserted rather
        // than assumed.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("\\markup hello");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.LexerModeOperations.Should().Equal("push-markup-state", "pop-state");
        scanner.State.Should().Be(LexerState.Initial);
    }

    [Fact]
    public void markup_mode_really_changes_what_a_word_means()
    {
        //Arrange
        // `new` is a KEYWORD in INITIAL (NEWCONTEXT) and `c4` would be a note in
        // NOTES. In markup mode both are just words. This is the test that would fail
        // if the host recorded the mode push without performing it — the whole markup
        // would then lex as ordinary music and never reduce.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup { new c4 }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        object markup = HandledMarkup(host);
        Pair.ToList(((Pair)markup).Cdr).Should().HaveCount(1);
        Pair.ToList(Pair.ToList(((Pair)markup).Cdr)[0]).Should().Equal("new", "c4");
    }

    // ------ markup_braced_list, markup_braced_list_body, markup_top ------

    [Fact]
    public void a_braced_markup_list_at_the_top_becomes_a_line_markup_in_written_order()
    {
        //Arrange
        // \markup { a b } — markup_top wraps a markup LIST in line-markup, which is
        // what puts the words side by side. The body accumulated in reverse and
        // markup_braced_list reversed it back, so the order here is the written one.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup { a b c }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        Pair markup = (Pair)HandledMarkup(host);
        markup.Car.Should().Be("lily:line-markup");
        List<object> arguments = Pair.ToList(markup.Cdr);
        arguments.Should().HaveCount(1);
        Pair.ToList(arguments[0]).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void an_empty_braced_markup_list_is_an_empty_line()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("\\markup { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        Pair markup = (Pair)HandledMarkup(host);
        markup.Car.Should().Be("lily:line-markup");
        Pair.ToList(markup.Cdr)[0].Should().Be(Nil.Instance);
    }

    [Fact]
    public void a_nested_braced_list_SPLICES_its_elements_rather_than_nesting_them()
    {
        //Arrange
        // THE ONE THAT MATTERS in markup_braced_list_body. Its two recursive
        // alternatives look alike and do not behave alike: a `markup` CONSES on, a
        // `markup_list` SPLICES (Srfi_1::append_reverse). Consing the list instead
        // would produce { a (b c) d } — the same text, a different markup, and
        // nothing would report it.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup { a { b c } d }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        Pair markup = (Pair)HandledMarkup(host);
        markup.Car.Should().Be("lily:line-markup");
        Pair.ToList(Pair.ToList(markup.Cdr)[0]).Should().Equal("a", "b", "c", "d");
    }

    // ------ full_markup_list ------

    [Fact]
    public void markuplist_hands_the_list_over_as_it_is_with_no_line_wrapper()
    {
        //Arrange
        // The contrast with \markup { a b } above, and the reason both rules exist:
        // \markuplist produces a markup LIST, so the toplevel handler receives its
        // elements directly rather than one line markup holding them.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markuplist { a b }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        HandledMarkupList(host).Should().Equal("a", "b");
        host.LexerModeOperations.Should().Equal("push-markup-state", "pop-state");
    }

    // ------ markup_head_1_list + simple_markup: composition ------

    [Fact]
    public void a_markup_command_composes_over_the_markup_that_follows_it()
    {
        //Arrange
        // \markup \bold "x" — markup_top's second alternative: the command chain and
        // the markup are handed to composed-markup-list, whose result is a LIST of
        // composed markups, and the single markup is its car.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("\\markup \\bold x");
        host.MarkupCommands["bold"] = ("MARKUP_FUNCTION", "bold-proc", new[] { "markup?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.SyntaxDispatches.Should().HaveCount(1);
        host.SyntaxDispatches[0].Name.Should().Be("composed-markup-list");

        // The markup itself: the command applied to the word.
        Pair.ToList(HandledMarkup(host)).Should().Equal("bold-proc", "x");
    }

    [Fact]
    public void chained_commands_are_accumulated_with_the_outermost_LAST()
    {
        //Arrange
        // \markup \bold \italic x. The vendored composed-markup-list documents the
        // order it wants: "`commands` a list of commands with their scheme arguments,
        // IN REVERSE ORDER, eg: ((italic) (raise 4) (bold))" — it folds them, so the
        // list head is applied innermost. Written outermost-first, accumulated
        // outermost-last. Reversing this would bold the text and then italicise the
        // result, which for these two commands is invisible and for \raise / \rotate
        // is not.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\bold \\italic x");
        host.MarkupCommands["bold"] = ("MARKUP_FUNCTION", "bold-proc", new[] { "markup?" });
        host.MarkupCommands["italic"] = ("MARKUP_FUNCTION", "italic-proc", new[] { "markup?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        // The commands list, as the constructor receives it: outermost last.
        List<object> commands = Pair.ToList(host.SyntaxDispatches[0].Arguments[0]);
        commands.Should().HaveCount(2);
        Pair.ToList(commands[0]).Should().Equal("italic-proc");
        Pair.ToList(commands[1]).Should().Equal("bold-proc");

        // And what that MEANS once composed: bold on the outside, wrapping italic.
        List<object> markup = Pair.ToList(HandledMarkup(host));
        markup[0].Should().Be("bold-proc");
        Pair.ToList(markup[1]).Should().Equal("italic-proc", "x");
    }

    [Fact]
    public void a_command_INSIDE_a_braced_list_composes_over_the_next_markup_only()
    {
        //Arrange
        // \markup { \bold a b } — this is the `markup` rule rather than `markup_top`,
        // and it is the reason both exist: inside a braced list a command binds to the
        // ONE markup after it, so `b` stays unbolded. The two bodies are identical, so
        // only a test at this position tells them apart.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup { \\bold a b }");
        host.MarkupCommands["bold"] = ("MARKUP_FUNCTION", "bold-proc", new[] { "markup?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> line = Pair.ToList(Pair.ToList(((Pair)HandledMarkup(host)).Cdr)[0]);
        line.Should().HaveCount(2);
        Pair.ToList(line[0]).Should().Equal("bold-proc", "a");
        line[1].Should().Be("b");
    }

    [Fact]
    public void a_command_chain_over_a_markuplist_distributes_and_stays_a_list()
    {
        //Arrange
        // markup_composed_list: the same constructor, but its result is used WHOLE
        // rather than car'd, because \markuplist yields a list.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markuplist \\bold { a b }");
        host.MarkupCommands["bold"] = ("MARKUP_FUNCTION", "bold-proc", new[] { "markup?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.SyntaxDispatches[0].Name.Should().Be("composed-markup-list");
        Pair.ToList(host.SyntaxDispatches[0].Arguments[1]).Should().Equal("a", "b");

        // The handler received the whole composed LIST — the command distributed over
        // BOTH markups — rather than its car, which is what makes this rule the
        // markup-list twin of `markup: markup_head_1_list simple_markup`.
        List<object> handed = HandledMarkupList(host);
        handed.Should().HaveCount(2);
        Pair.ToList(handed[0]).Should().Equal("bold-proc", "a");
        Pair.ToList(handed[1]).Should().Equal("bold-proc", "b");
    }

    [Fact]
    public void a_markup_list_command_on_its_own_is_a_one_element_markup_list()
    {
        //Arrange
        // markup_uncomposed_list: markup_command_list — a MARKUP_LIST_FUNCTION
        // produces one expression, which has to be wrapped to stand where a list is
        // expected.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markuplist \\table-of-contents");
        host.MarkupCommands["table-of-contents"]
            = ("MARKUP_LIST_FUNCTION", "toc-proc", new string[0]);

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> handed = HandledMarkupList(host);
        handed.Should().HaveCount(1);
        Pair.ToList(handed[0]).Should().Equal("toc-proc");
    }

    // ------ markup_scm: the embedded-Scheme classifier ($@12) ------

    [Fact]
    public void an_embedded_expression_that_is_a_markup_comes_back_as_a_markup_identifier()
    {
        //Arrange
        // markup_scm's mid-rule classifies the evaluated expression and hands it back
        // to the token stream as the identifier token it turned out to be, so that
        // `markup_scm MARKUP_IDENTIFIER` can pick it up. A string is a markup.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup #(get-title)");
        host.EvalResults["(get-title)"] = "Sonata";

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        HandledMarkup(host).Should().Be("Sonata");
    }

    [Fact]
    public void an_embedded_expression_that_is_a_markup_list_comes_back_as_a_markuplist_identifier()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markuplist #(get-lines)");
        host.EvalResults["(get-lines)"] = Pair.List("one", "two");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        HandledMarkupList(host).Should().Equal("one", "two");
    }

    [Fact]
    public void an_embedded_expression_evaluated_for_effect_reads_as_the_empty_markup_list()
    {
        //Arrange
        // SCM_UNSPECIFIED is what an expression evaluated for its side effect answers.
        // Upstream reads it as the EMPTY markup list rather than as an error, so
        // `#(do-something)` in markup position contributes nothing and does not
        // derail the parse.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markuplist #(side-effect)");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.EvaluatedTokens.Should().Equal("(side-effect)");
        HandledMarkupList(host).Should().BeEmpty();
    }

    [Fact]
    public void an_embedded_expression_that_is_not_a_markup_is_reported_and_yields_an_empty_markup()
    {
        //Arrange
        // The error branch: the file is known to have failed, but the parse continues
        // with an empty markup rather than stopping — the same posture as every other
        // parser_error site.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup #(a-number)");
        host.EvalResults["(a-number)"] = 42L;

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.Diagnostics.Should().HaveCount(1);
        parser.Diagnostics[0].Should().Contain("not a markup");
        host.ErrorLevel.Should().Be(1);
        HandledMarkup(host).Should().Be(string.Empty);
    }

    // ------ \score inside markup ------

    [Fact]
    public void a_score_written_inside_markup_becomes_a_score_markup_and_gains_a_layout()
    {
        //Arrange
        // simple_markup_noword: SCORE $@15 '{' score_body '}'. Three things happen at
        // once: the lexer leaves markup mode for the score's MUSIC and returns to it
        // afterwards, the score is given a layout definition because it brought none,
        // and the markup expression is built around Lily::score_markup.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\score { c }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.LexerModeOperations.Should().Equal(
            "push-markup-state", "push-note-state", "pop-state", "pop-state");

        Pair markup = (Pair)HandledMarkup(host);
        markup.Car.Should().Be("lily:score-markup");

        Score score = (Score)Pair.ToList(markup.Cdr)[0];
        score.Defs.Should().HaveCount(1);
        score.Defs[0].LookupVariable(Symbol.Intern("output-def-kind"))
            .Should().Be(Symbol.Intern("layout"));
        score.Origin.Should().NotBeNull();
    }

    [Fact]
    public void a_score_that_brought_its_own_layout_is_left_alone()
    {
        //Arrange
        // The `if (sc->defs_.empty ())` guard. A \score written with its own \layout
        // must not be given a second one.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\score { c \\layout { } }");
        host.Keywords["layout"] = ("LAYOUT", null);

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        Score score = (Score)Pair.ToList(((Pair)HandledMarkup(host)).Cdr)[0];
        score.Defs.Should().HaveCount(1);
    }

    [Fact]
    public void score_lines_inside_a_markuplist_is_wrapped_twice()
    {
        //Arrange
        // \score-lines produces a markup LIST — one line per system — so the
        // expression is wrapped once as the command call and again to make it a
        // one-element markup list. The single-markup twin above wraps once.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markuplist \\score-lines { c }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> handed = HandledMarkupList(host);
        handed.Should().HaveCount(1);

        List<object> call = Pair.ToList(handed[0]);
        call.Should().HaveCount(2);
        call[0].Should().Be("lily:score-lines-markup-list");
        ((Score)call[1]).Defs.Should().HaveCount(1);
    }

    // ------ the layout a markup score is given ------

    [Fact]
    public void the_layout_a_markup_score_is_given_is_a_clone_of_defaultlayout()
    {
        //Arrange
        // PrepareMarkupScore reaches RAG4's get_layout, which clones $defaultlayout
        // when one is in scope. The clone matters: a markup score that mutated the
        // session's own layout would change every later score in the file.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\score { c }");

        OutputDef defaults = new OutputDef();
        defaults.SetVariable(Symbol.Intern("indent"), 7L);
        host.Globals.Bindings[Symbol.Intern("$defaultlayout")] = defaults;

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        Score score = (Score)Pair.ToList(((Pair)HandledMarkup(host)).Cdr)[0];
        score.Defs[0].LookupVariable(Symbol.Intern("indent")).Should().Be(7L);
        score.Defs[0].Should().NotBeSameAs(defaults);
    }
}
