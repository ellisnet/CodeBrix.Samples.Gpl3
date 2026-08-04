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
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 1 — the top-level, header and assignment actions, exercised two
/// ways: whole inputs through the REAL scanner and tables with a scripted host, and
/// direct invocation for the rules whose surrounding grammar is not ported yet.
/// </summary>
public class RuleActionRag1Tests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static readonly IReadOnlyDictionary<int, RuleAction> Bound
        = LilyPondRuleActions.Create().Bind(Tables);

    private static int Sym(string name)
    {
        for (int i = 0; i < Tables.Symbols.Count; i++)
        {
            if (string.Equals(Tables.Symbols[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException("no symbol named " + name);
    }

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
        host.Keywords["header"] = ("HEADER", null);

        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);

        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
    }

    private static LalrParser ParseTokens(ScriptedParserHost host, params (string Symbol, object Value)[] tokens)
    {
        List<ParserToken> list = new List<ParserToken>();
        for (int i = 0; i < tokens.Length; i++)
        {
            list.Add(new ParserToken(
                Sym(tokens[i].Symbol),
                tokens[i].Value,
                new SourceSpan("<test>", 1, i + 1, 1, i + 2)));
        }

        LalrParser parser = new LalrParser(Tables, Bound);
        parser.Parse(new TokenListInput(list), host);
        return parser;
    }

    [Fact]
    public void an_assignment_from_real_text_lands_in_the_identifier_table()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("title = \"Adagio\"");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Globals.Bindings[Symbol.Intern("title")].Should().Be("Adagio");
    }

    [Fact]
    public void a_header_block_from_real_text_becomes_the_default_header()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\header { title = \"Adagio\" composer = \"Someone\" }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Scopes.Should().BeEmpty();

        FakeModule header = (FakeModule)host.Globals.Bindings[Symbol.Intern("$defaultheader")];
        header.Bindings[Symbol.Intern("title")].Should().Be("Adagio");
        header.Bindings[Symbol.Intern("composer")].Should().Be("Someone");
    }

    [Fact]
    public void a_header_block_retains_values_an_earlier_header_set()
    {
        //Arrange
        // header_block opens on a COPY of $defaultheader (get_header), which is how a
        // later \header keeps the earlier one's fields it does not overwrite.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\header { title = \"New\" }");

        FakeModule earlier = new FakeModule();
        earlier.Bindings[Symbol.Intern("composer")] = "Kept";
        earlier.Bindings[Symbol.Intern("title")] = "Old";
        host.Globals.Bindings[Symbol.Intern("$defaultheader")] = earlier;

        //Act
        parser.Parse(scanner, host);

        //Assert
        FakeModule header = (FakeModule)host.Globals.Bindings[Symbol.Intern("$defaultheader")];
        header.Should().NotBeSameAs(earlier);
        header.Bindings[Symbol.Intern("composer")].Should().Be("Kept");
        header.Bindings[Symbol.Intern("title")].Should().Be("New");
        earlier.Bindings[Symbol.Intern("title")].Should().Be("Old");
    }

    [Fact]
    public void a_header_assigned_to_an_identifier_starts_clean_and_does_not_become_the_default()
    {
        //Arrange
        // foo = \header { ... } goes through header_modification, which opens on a
        // FRESH module rather than a copy of $defaultheader.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("foo = \\header { title = \"T\" }");

        FakeModule earlier = new FakeModule();
        earlier.Bindings[Symbol.Intern("composer")] = "Kept";
        host.Globals.Bindings[Symbol.Intern("$defaultheader")] = earlier;

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);

        FakeModule assigned = (FakeModule)host.Globals.Bindings[Symbol.Intern("foo")];
        assigned.Bindings[Symbol.Intern("title")].Should().Be("T");
        assigned.Bindings.ContainsKey(Symbol.Intern("composer")).Should().BeFalse();
        host.Globals.Bindings[Symbol.Intern("$defaultheader")].Should().BeSameAs(earlier);
    }

    [Fact]
    public void a_scheme_token_at_top_level_is_evaluated_and_ignored()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("#(display \"hi\")");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.EvaluatedTokens.Should().HaveCount(1);
        host.EvaluatedTokens[0].Should().Be("(display \"hi\")");
    }

    [Fact]
    public void a_book_identifier_with_paper_dispatches_to_the_book_handler()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        object book = new object();
        host.BooksWithPaper.Add(book);
        host.Globals.Bindings[Symbol.Intern("toplevel-book-handler")] = "book-proc";
        host.Globals.Bindings[Symbol.Intern("toplevel-bookpart-handler")] = "bookpart-proc";

        //Act
        ParseTokens(host, ("BOOK_IDENTIFIER", book));

        //Assert
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.Should().Be("book-proc");
        host.Calls[0].Arguments[0].Should().BeSameAs(book);
    }

    [Fact]
    public void a_book_identifier_without_paper_dispatches_to_the_bookpart_handler()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        object book = new object();
        host.Globals.Bindings[Symbol.Intern("toplevel-bookpart-handler")] = "bookpart-proc";

        //Act
        ParseTokens(host, ("BOOK_IDENTIFIER", book));

        //Assert
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.Should().Be("bookpart-proc");
    }

    [Fact]
    public void an_active_scheme_value_that_is_a_markup_reaches_the_text_handler_as_a_list()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        host.Globals.Bindings[Symbol.Intern("toplevel-text-handler")] = "text-proc";

        //Act
        ParseTokens(host, ("SCM_IDENTIFIER", "a markup"));

        //Assert
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.Should().Be("text-proc");
        Pair wrapped = (Pair)host.Calls[0].Arguments[0];
        wrapped.Car.Should().Be("a markup");
        wrapped.Cdr.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void an_active_scheme_value_that_is_an_output_def_becomes_the_matching_default()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        OutputDef outputDef = new OutputDef();
        outputDef.SetVariable("output-def-kind", Symbol.Intern("paper"));

        //Act
        ParseTokens(host, ("SCM_IDENTIFIER", outputDef));

        //Assert
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")].Should().BeSameAs(outputDef);
    }

    [Fact]
    public void an_active_scheme_value_that_is_a_module_is_copied_into_the_default_header()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        FakeModule supplied = new FakeModule();
        supplied.Bindings[Symbol.Intern("title")] = "From Scheme";

        //Act
        ParseTokens(host, ("SCM_IDENTIFIER", supplied));

        //Assert
        FakeModule header = (FakeModule)host.Globals.Bindings[Symbol.Intern("$defaultheader")];
        header.Should().NotBeSameAs(supplied);
        header.Bindings[Symbol.Intern("title")].Should().Be("From Scheme");
    }

    [Fact]
    public void an_active_scheme_value_of_no_recognized_kind_is_a_bad_expression_type()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();

        //Act
        LalrParser parser = ParseTokens(host, ("SCM_IDENTIFIER", 42L));

        //Assert
        host.ErrorLevel.Should().Be(1);
        parser.Diagnostics.Should().HaveCount(1);
        parser.Diagnostics[0].Should().Contain("bad expression type");
    }

    [Fact]
    public void a_score_at_top_level_reaches_the_score_handler()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        object score = new object();
        host.Scores.Add(score);
        host.Globals.Bindings[Symbol.Intern("toplevel-score-handler")] = "score-proc";

        //Act
        ParseTokens(host, ("SCM_IDENTIFIER", score));

        //Assert
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.Should().Be("score-proc");
        host.Calls[0].Arguments[0].Should().BeSameAs(score);
    }

    [Fact]
    public void a_path_assignment_conses_the_base_symbol_onto_the_path()
    {
        //Arrange
        // assignment: assignment_id '.' property_path '=' identifier_init — invoked
        // directly because property_path's own actions are RAG7.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object path = Pair.List(Symbol.Intern("size"));

        //Act
        object result = Action("assignment: assignment_id '.' property_path '=' identifier_init")(
            context,
            new object[] { Symbol.Intern("foo"), '.', path, '=', 42L },
            new SourceSpan[5],
            default);

        //Assert
        result.Should().BeSameAs(Unspecified.Instance);
        host.PathAssignments.Should().HaveCount(1);
        Pair key = (Pair)host.PathAssignments[0].Key;
        key.Car.Should().BeSameAs(Symbol.Intern("foo"));
        key.Cdr.Should().BeSameAs(path);
        host.PathAssignments[0].Value.Should().Be(42L);
    }

    [Fact]
    public void a_markup_word_assignment_defines_a_markup_command()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object function = new object();
        host.MarkupFunctions.Add(function);

        //Act
        object result = Action("assignment: markup_mode_word '=' identifier_init")(
            context,
            new object[] { "myCommand", '=', function },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().BeSameAs(Unspecified.Instance);
        host.MarkupCommandsDefined.Should().HaveCount(1);
        host.MarkupCommandsDefined[0].Name.Should().BeSameAs(Symbol.Intern("myCommand"));
        host.MarkupCommandsDefined[0].Function.Should().BeSameAs(function);
    }

    [Fact]
    public void a_markup_word_assignment_of_a_non_function_is_an_error()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        Action("assignment: markup_mode_word '=' identifier_init")(
            context,
            new object[] { "myCommand", '=', "not a function" },
            new SourceSpan[3],
            default);

        //Assert
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
        host.MarkupCommandsDefined.Should().BeEmpty();
    }

    [Fact]
    public void a_module_lookup_walks_the_path_through_nested_modules()
    {
        //Arrange
        // lookup: MODULE_IDENTIFIER '.' symbol_list_rev — invoked directly because
        // symbol_list_rev's own actions are RAG7. The path arrives REVERSED, as the
        // rule name says.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        FakeModule inner = new FakeModule();
        inner.Bindings[Symbol.Intern("value")] = 7L;
        FakeModule outer = new FakeModule();
        outer.Bindings[Symbol.Intern("inner")] = inner;

        object reversedPath = Pair.List(Symbol.Intern("value"), Symbol.Intern("inner"));

        //Act
        object result = Action("lookup: MODULE_IDENTIFIER '.' symbol_list_rev")(
            context,
            new object[] { outer, '.', reversedPath },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().Be(7L);
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void a_module_lookup_that_misses_reports_not_found()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        FakeModule module = new FakeModule();

        //Act
        object result = Action("lookup: MODULE_IDENTIFIER '.' symbol_list_rev")(
            context,
            new object[] { module, '.', Pair.List(Symbol.Intern("missing")) },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().BeSameAs(DefaultArgument.Instance);
        context.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void a_single_post_event_is_assigned_as_itself()
    {
        //Arrange
        // identifier_init: post_event_nofinger post_events — one event needs no
        // PostEvents wrapper.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject postEvent = new MusicObject(Nil.Instance);

        //Act
        object result = Action("identifier_init: post_event_nofinger post_events")(
            context,
            new object[] { postEvent, Nil.Instance },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(postEvent);
    }

    [Fact]
    public void several_post_events_are_wrapped_in_a_post_events_music()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject first = new MusicObject(Nil.Instance);
        MusicObject second = new MusicObject(Nil.Instance);

        //Act
        // post_events accumulates front-to-back, so $2 arrives reversed; here it
        // holds the one further event.
        object result = Action("identifier_init: post_event_nofinger post_events")(
            context,
            new object[] { first, new Pair(second, Nil.Instance) },
            new SourceSpan[2],
            default);

        //Assert
        MadeMusic wrapped = (MadeMusic)result;
        wrapped.Name.Should().Be("PostEvents");
        wrapped.Properties.Should().HaveCount(1);
        wrapped.Properties[0].Name.Should().Be("elements");
        Pair elements = (Pair)wrapped.Properties[0].Value;
        elements.Car.Should().BeSameAs(first);
        ((Pair)elements.Cdr).Car.Should().BeSameAs(second);
    }

    [Fact]
    public void an_embedded_lilypond_start_returns_the_embedded_music_and_restores_the_lexer()
    {
        //Arrange
        // start_symbol: EMBEDDED_LILY $@1 embedded_lilypond — upstream stores $3
        // through *retval; the port returns it from Parse.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object music = new object();

        //Act
        object pushed = Action("$@1: /* empty */")(
            context, new object[0], new SourceSpan[0], default);
        object result = Action("start_symbol: EMBEDDED_LILY $@1 embedded_lilypond")(
            context,
            new object[] { "EMBEDDED_LILY", pushed, music },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().BeSameAs(music);
        host.LexerModeOperations.Should().Equal("push-note-state", "pop-state");
    }

    [Fact]
    public void a_partial_function_assignment_dispatches_to_the_syntax_constructor()
    {
        //Arrange
        // identifier_init_nonumber: partial_function ETC — the group's one
        // MAKE_SYNTAX site. The accumulated calls arrive reversed.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object calls = Pair.List("second-call", "first-call");

        //Act
        object result = Action("identifier_init_nonumber: partial_function ETC")(
            context,
            new object[] { calls, "ETC" },
            new SourceSpan[2],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("partial-music-function");
        Pair reversed = (Pair)mark.Arguments[0];
        reversed.Car.Should().Be("first-call");
        ((Pair)reversed.Cdr).Car.Should().Be("second-call");
    }
}
