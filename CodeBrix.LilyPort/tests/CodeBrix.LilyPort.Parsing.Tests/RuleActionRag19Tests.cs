// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 19 — markup commands and their argument lists, the last of the
/// 479 action bodies.
/// <para>
/// These rules are driven by TOKENS THE LEXER INVENTS. On reading <c>\bold</c> the
/// scanner looks the command up, finds its signature, and pushes one <c>EXPECT_*</c>
/// token per declared argument plus a terminating <c>EXPECT_NO_MORE_ARGS</c> — in
/// reverse, so the LAST declared argument is announced FIRST. Every test here scripts
/// a command's signature and then writes the command in real text, because the
/// interplay between the announcement order and the rules that consume it is the
/// entire mechanism and is not visible in any single body.
/// </para>
/// </summary>
public class RuleActionRag19Tests
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

    private static ParseContext NewContext(object host)
        => new ParseContext(
            new LalrParser(Tables, new Dictionary<int, RuleAction>()),
            new TokenListInput())
        {
            UserState = host,
        };

    private static void NoErrors(LalrParser parser)
        => string.Join("; ", parser.Diagnostics).Should().BeEmpty();

    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(
        string input)
    {
        ScriptedParserHost host = new ScriptedParserHost { MakeRealMusic = true };

        host.Keywords["markup"] = ("MARKUP", null);
        host.Keywords["markuplist"] = ("MARKUPLIST", null);
        host.Keywords["etc"] = ("ETC", null);

        host.Globals.Bindings[Symbol.Intern("toplevel-text-handler")] = "text-proc";
        host.WordScans[Symbol.Intern("c")] = ("NOTENAME_PITCH", new Pitch(0, 0, Rational.Zero));

        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        host.Scanner = scanner;

        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
    }

    // The toplevel handler is not the only thing that goes through IParserHost.Call —
    // every argument PREDICATE does too — so the handler call is found by name rather
    // than by position.
    private static object HandledMarkup(ScriptedParserHost host)
    {
        List<(object Procedure, object[] Arguments)> handler
            = host.Calls.FindAll(c => Equals(c.Procedure, "text-proc"));
        handler.Should().HaveCount(1);

        List<object> handed = Pair.ToList(handler[0].Arguments[0]);
        handed.Should().HaveCount(1);
        return handed[0];
    }

    // ------ the announcement itself ------

    [Fact]
    public void a_commands_signature_is_announced_last_argument_first()
    {
        //Arrange
        // The premise everything else here rests on, asserted directly at the scanner
        // so that a later surprise in a parse can be told apart from a surprise in the
        // announcement. Upstream's comment: "(number? number? markup?) [gives] tokens
        // EXPECT_MARKUP EXPECT_SCM EXPECT_SCM EXPECT_NO_MORE_ARGS".
        ScriptedParserHost host = new ScriptedParserHost();
        host.MarkupCommands["pad"]
            = ("MARKUP_FUNCTION", "pad-proc", new[] { "number?", "number?", "markup?" });

        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), "\\pad", "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        scanner.PushState(LexerState.Markup);

        //Act
        List<string> tokens = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            ParserToken token = scanner.Next();
            if (token.Symbol == 0)
            {
                break;
            }

            tokens.Add(Tables.Symbols[token.Symbol]);
        }

        //Assert
        tokens.Should().Equal(
            "MARKUP_FUNCTION", "EXPECT_MARKUP", "EXPECT_SCM", "EXPECT_SCM",
            "EXPECT_NO_MORE_ARGS");
    }

    // ------ markup_head_1_item + markup_command_list_arguments ------

    [Fact]
    public void a_command_carries_its_scheme_argument_and_takes_its_markup_from_composition()
    {
        //Arrange
        // \markup \raise #2 x, with \raise declared (number? markup?). The number is
        // read here, into the head item; the MARKUP is not — markup_head_1_item exists
        // precisely to leave the last argument outstanding, and RAG18's
        // composed-markup-list supplies it.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\raise #2 x");
        host.MarkupCommands["raise"]
            = ("MARKUP_FUNCTION", "raise-proc", new[] { "number?", "markup?" });
        host.EvalResults["2"] = 2L;

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        // The head item as the constructor received it: the command and its number.
        List<object> commands = Pair.ToList(host.SyntaxDispatches[0].Arguments[0]);
        Pair.ToList(commands[0]).Should().Equal("raise-proc", 2L);

        // And the finished markup, with the composed markup last.
        Pair.ToList(HandledMarkup(host)).Should().Equal("raise-proc", 2L, "x");
    }

    [Fact]
    public void a_two_markup_command_reads_the_first_and_composes_the_second()
    {
        //Arrange
        // \markup \combine a b, declared (markup? markup?). The FIRST markup is read
        // by markup_command_list_arguments' EXPECT_MARKUP alternative; the second is
        // the outstanding one. Getting these two the wrong way round would silently
        // swap the arguments of every two-markup command.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\combine a b");
        host.MarkupCommands["combine"]
            = ("MARKUP_FUNCTION", "combine-proc", new[] { "markup?", "markup?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        Pair.ToList(HandledMarkup(host)).Should().Equal("combine-proc", "a", "b");
    }

    [Fact]
    public void a_command_whose_last_argument_is_not_a_markup_finishes_on_its_own()
    {
        //Arrange
        // \markup \fromproperty "header:title", declared (symbol?). With no markup
        // argument outstanding there is no composition at all: the command reduces
        // through simple_markup_noword, which conses the procedure onto the reversed
        // arguments itself. This is RAG18's rule, exercised here because RAG19's
        // EXPECT_SCM ... STRING alternative is what feeds it.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\fromproperty \"header:title\"");
        host.MarkupCommands["fromproperty"]
            = ("MARKUP_FUNCTION", "fromproperty-proc", new[] { "symbol?" });
        host.CallBehavior = (procedure, arguments) => true;

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        Pair.ToList(HandledMarkup(host)).Should().Equal("fromproperty-proc", "header:title");
        host.SyntaxDispatches.Should().BeEmpty();
    }

    [Fact]
    public void a_markup_list_argument_is_taken_whole()
    {
        //Arrange
        // \markup \column { a b }, declared (markup-list?). The braced list reduces as
        // a markup_list and goes in as ONE argument — not spliced, which is the
        // distinction markup_braced_list_body draws in RAG18.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\column { a b }");
        host.MarkupCommands["column"]
            = ("MARKUP_FUNCTION", "column-proc", new[] { "markup-list?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> markup = Pair.ToList(HandledMarkup(host));
        markup.Should().HaveCount(2);
        markup[0].Should().Be("column-proc");
        Pair.ToList(markup[1]).Should().Equal("a", "b");
    }

    // ------ the predicate, and what a failed one does ------

    [Fact]
    public void an_argument_that_fails_its_predicate_is_reported_and_the_list_is_marked()
    {
        //Arrange
        // check_scheme_arg — RAG8-RAG10's shared helper — reached here through
        // EXPECT_SCM. The argument is kept so the command's arity still reads right,
        // the error is dispatched to argument-error, and the list is terminated with
        // #f, which is what marks the whole call as uncallable.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\raise #2 x");
        host.MarkupCommands["raise"]
            = ("MARKUP_FUNCTION", "raise-proc", new[] { "number?", "markup?" });
        host.EvalResults["2"] = 2L;
        host.CallBehavior = (procedure, arguments) =>
            string.Equals(procedure as string, "number?", StringComparison.Ordinal)
                ? (object)false
                : true;

        //Act
        parser.Parse(scanner, host);

        //Assert
        SyntaxMark error = host.SyntaxDispatches.Find(m => m.Name == "argument-error");
        error.Should().NotBeNull();
        error.Arguments[0].Should().Be(1L);
        error.Arguments[1].Should().Be("number?");
        error.Arguments[2].Should().Be(2L);
    }

    // ------ the braced-argument rule ($@14) ------

    [Fact]
    public void a_braced_argument_that_satisfies_the_predicate_is_taken_as_written()
    {
        //Arrange
        // \markup \raise { 4 } x. The braces hold LilyPond, so the lexer drops into
        // note state for them and returns afterwards. Upstream tries the WRITTEN value
        // first: here the predicate accepts the duration, so no note-event
        // reinterpretation is attempted at all.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\raise { 4 } x");
        host.MarkupCommands["raise"]
            = ("MARKUP_FUNCTION", "raise-proc", new[] { "number?", "markup?" });
        host.CallBehavior = (procedure, arguments) => true;

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.LexerModeOperations.Should().Equal(
            "push-markup-state", "push-note-state", "pop-state", "pop-state");

        List<object> commands = Pair.ToList(host.SyntaxDispatches[0].Arguments[0]);
        List<object> raise = Pair.ToList(commands[0]);
        raise[0].Should().Be("raise-proc");
        raise[1].Should().BeOfType<Duration>();
    }

    [Fact]
    public void a_braced_argument_the_predicate_refuses_is_retried_as_a_note_event()
    {
        //Arrange
        // The other half of upstream's comment: "{ 4 } and { cis } can be interpreted
        // both as a duration or pitch, respectively, or as a note event. Therefore, we
        // try both variants". The predicate refuses the bare duration and accepts
        // music, so make_music_from_simple's reading is what goes in — and it is made
        // WHILE THE LEXER IS STILL IN NOTE STATE, which is why the pop is last.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\test { 4 } x");
        host.MarkupCommands["test"]
            = ("MARKUP_FUNCTION", "test-proc", new[] { "ly:music?", "markup?" });
        host.CallBehavior = (procedure, arguments) => arguments[0] is MusicObject;

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> commands = Pair.ToList(host.SyntaxDispatches[0].Arguments[0]);
        Pair.ToList(commands[0])[1].Should().BeOfType<MusicObject>();
    }

    [Fact]
    public void a_braced_duration_taken_as_music_becomes_the_sticky_default_duration()
    {
        //Arrange
        // `if (Duration *dur = unsmob<Duration> ($5)) parser->default_duration_ = *dur;`
        // — and it is inside the SUCCEEDED-AS-MUSIC branch only. A written { 4 } that
        // became a note event changes what a later bare note means, exactly as `c4 d`
        // does in ordinary music (RAG16's sticky duration).
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\test { 2 } x");
        host.MarkupCommands["test"]
            = ("MARKUP_FUNCTION", "test-proc", new[] { "ly:music?", "markup?" });
        host.CallBehavior = (procedure, arguments) => arguments[0] is MusicObject;

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.DefaultDuration.DurationLog.Should().Be(1);
    }

    [Fact]
    public void a_braced_argument_refused_both_ways_is_reported_and_kept_as_written()
    {
        //Arrange
        // Neither the written value nor the note-event reading satisfies the
        // predicate, so the fallback runs check_scheme_arg on the WRITTEN value — the
        // error names what the reader actually typed rather than the interpretation
        // the parser tried on its behalf.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\test { 4 } x");
        host.MarkupCommands["test"]
            = ("MARKUP_FUNCTION", "test-proc", new[] { "string?", "markup?" });
        host.CallBehavior = (procedure, arguments) => false;

        //Act
        parser.Parse(scanner, host);

        //Assert
        SyntaxMark error = host.SyntaxDispatches.Find(m => m.Name == "argument-error");
        error.Should().NotBeNull();
        error.Arguments[2].Should().BeOfType<Duration>();

        // And the default duration was NOT touched: it is still the quarter note a
        // parser starts with (upstream lily-parser.cc:42, Duration (2, 0)), not the
        // written 4. Only the branch that succeeded AS MUSIC makes a written duration
        // sticky — the test above shows it moving to 1 for `{ 2 }`.
        host.DefaultDuration.DurationLog.Should().Be(2);
    }

    // ------ markup_partial_function and markup_arglist_partial (\etc) ------

    [Fact]
    public void a_markup_command_with_etc_becomes_a_partial_markup()
    {
        //Arrange
        // foo = \markup \bold \etc — the markup half of RAG11's partial-function
        // mechanism. markup_arglist_partial matches the outstanding markup argument
        // and the command is handed to the partial-markup constructor as a
        // ONE-ELEMENT command chain.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("foo = \\markup \\bold \\etc");
        host.MarkupCommands["bold"] = ("MARKUP_FUNCTION", "bold-proc", new[] { "markup?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.SyntaxDispatches.Should().HaveCount(1);
        SyntaxMark partial = host.SyntaxDispatches[0];
        partial.Name.Should().Be("partial-markup");

        List<object> commands = Pair.ToList(partial.Arguments[0]);
        commands.Should().HaveCount(1);
        Pair.ToList(commands[0]).Should().Equal("bold-proc");

        host.Globals.Bindings[Symbol.Intern("foo")].Should().BeSameAs(partial);
        host.LexerModeOperations.Should().Equal("push-markup-state", "pop-state");
    }

    [Fact]
    public void a_partial_markup_keeps_the_arguments_written_before_the_missing_one()
    {
        //Arrange
        // foo = \markup \raise #2 \etc. The number is written and kept; the markup is
        // the missing argument. This is the rule pair that decides which announced
        // expectations are DISCARDED and which one MATCHES.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("foo = \\markup \\raise #2 \\etc");
        host.MarkupCommands["raise"]
            = ("MARKUP_FUNCTION", "raise-proc", new[] { "number?", "markup?" });
        host.EvalResults["2"] = 2L;
        host.CallBehavior = (procedure, arguments) => true;

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> commands = Pair.ToList(host.SyntaxDispatches[0].Arguments[0]);
        Pair.ToList(commands[0]).Should().Equal("raise-proc", 2L);
    }

    [Fact]
    public void a_partial_markup_with_nothing_written_keeps_no_arguments()
    {
        //Arrange
        // foo = \markup \raise \etc — BOTH declared arguments are outstanding. The
        // first announced expectation is discarded by the recursive rules and the
        // command keeps nothing.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("foo = \\markup \\raise \\etc");
        host.MarkupCommands["raise"]
            = ("MARKUP_FUNCTION", "raise-proc", new[] { "number?", "markup?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> commands = Pair.ToList(host.SyntaxDispatches[0].Arguments[0]);
        Pair.ToList(commands[0]).Should().Equal("raise-proc");
    }

    [Fact]
    public void a_partial_markup_behind_a_command_chain_keeps_the_incomplete_call_innermost()
    {
        //Arrange
        // foo = \markup \bold \raise \etc. The vendored partial-markup takes
        // `(car commands)` as the call the eventual argument is appended to, so the
        // INCOMPLETE one has to be the head of the list. Consing it onto the chain is
        // what puts it there — the reverse would apply \bold to the missing argument
        // and \raise to the result.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("foo = \\markup \\bold \\raise \\etc");
        host.MarkupCommands["bold"] = ("MARKUP_FUNCTION", "bold-proc", new[] { "markup?" });
        host.MarkupCommands["raise"]
            = ("MARKUP_FUNCTION", "raise-proc", new[] { "number?", "markup?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> commands = Pair.ToList(host.SyntaxDispatches[0].Arguments[0]);
        commands.Should().HaveCount(2);
        Pair.ToList(commands[0]).Should().Equal("raise-proc");
        Pair.ToList(commands[1]).Should().Equal("bold-proc");
    }

    [Fact]
    public void a_partial_markup_discards_a_trailing_markup_list_expectation()
    {
        //Arrange
        // foo = \markup \padlist \etc, declared (number? markup-list?). The markup-LIST
        // expectation is announced first (it is the last argument) and is DISCARDED by
        // markup_arglist_partial's recursive alternative; the number is then the first
        // missing argument and matches. Its sibling below does the same for a scheme
        // expectation — three near-identical rules that differ only in which
        // announcement they throw away.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("foo = \\markup \\padlist \\etc");
        host.MarkupCommands["padlist"]
            = ("MARKUP_FUNCTION", "padlist-proc", new[] { "number?", "markup-list?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> commands = Pair.ToList(host.SyntaxDispatches[0].Arguments[0]);
        Pair.ToList(commands[0]).Should().Equal("padlist-proc");
    }

    [Fact]
    public void a_partial_markup_discards_a_trailing_scheme_expectation()
    {
        //Arrange
        // foo = \markup \numafter \etc, declared (markup? number?) — a command whose
        // LAST argument is not a markup. The scheme expectation is announced first and
        // discarded; the markup is the first missing argument.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("foo = \\markup \\numafter \\etc");
        host.MarkupCommands["numafter"]
            = ("MARKUP_FUNCTION", "numafter-proc", new[] { "markup?", "number?" });

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> commands = Pair.ToList(host.SyntaxDispatches[0].Arguments[0]);
        Pair.ToList(commands[0]).Should().Equal("numafter-proc");
    }

    // ------ music as a markup command's argument ------

    [Fact]
    public void music_written_in_a_mode_block_is_checked_like_any_other_argument()
    {
        //Arrange
        // \markup \test \notemode { c } x — the mode_changed_music alternative. The
        // music is checked against the declared predicate exactly as an embedded
        // Scheme value would be; nothing about it being music is special here.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\test \\notemode { c } x");
        host.Keywords["notemode"] = ("NOTEMODE", null);
        host.MarkupCommands["test"]
            = ("MARKUP_FUNCTION", "test-proc", new[] { "ly:music?", "markup?" });
        host.CallBehavior = (procedure, arguments) => true;

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        // \notemode dispatches a constructor of its own (RAG12), so the composition is
        // found by name rather than by position — and the argument the command carries
        // is exactly what that constructor answered, travelling through unchanged.
        SyntaxMark modeChange = host.SyntaxDispatches[0];
        SyntaxMark composed = host.SyntaxDispatches.Find(m => m.Name == "composed-markup-list");

        List<object> command = Pair.ToList(Pair.ToList(composed.Arguments[0])[0]);
        command[0].Should().Be("test-proc");
        command[1].Should().BeSameAs(modeChange);
    }

    [Fact]
    public void a_music_identifier_is_checked_like_any_other_argument()
    {
        //Arrange
        // The MUSIC_IDENTIFIER alternative: \mus, previously bound to music, used as a
        // markup command's argument. Same body as the three around it — the grammar
        // needs the separate productions because the TOKENS differ, not the handling.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\markup \\test \\mus x");
        MusicObject music = new MusicObject(Nil.Instance);
        host.Identifiers["mus"] = new LexerLookup("MUSIC_IDENTIFIER", music);
        host.MarkupCommands["test"]
            = ("MARKUP_FUNCTION", "test-proc", new[] { "ly:music?", "markup?" });
        host.CallBehavior = (procedure, arguments) => true;

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        List<object> command = Pair.ToList(
            Pair.ToList(host.SyntaxDispatches[0].Arguments[0])[0]);
        Pair.ToList(HandledMarkup(host)).Should().Equal("test-proc", music, "x");
        command[1].Should().BeSameAs(music);
    }
}
