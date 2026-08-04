// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// RULE ACTION GROUP 2 — embedded Scheme and embedded LilyPond, exercised three
/// ways: real text through the scanner and tables where the surrounding grammar is
/// ported (a <c>#(...)</c> assignment), token streams through the
/// <c>EMBEDDED_LILY</c> start that RAG1 opened, and direct invocation for the rules
/// whose neighbours (post events, music lists, function arglists) are not ported yet.
/// </summary>
public class RuleActionRag2Tests
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

    private static ScriptedParserHost ParseText(string input, out LalrParser parser)
    {
        ScriptedParserHost host = new ScriptedParserHost();
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        parser = new LalrParser(Tables, Bound);
        parser.Parse(scanner, host);
        return host;
    }

    private static object ParseTokens(
        ScriptedParserHost host, params (string Symbol, object Value)[] tokens)
    {
        List<ParserToken> list = new List<ParserToken>();
        for (int i = 0; i < tokens.Length; i++)
        {
            list.Add(new ParserToken(
                Sym(tokens[i].Symbol),
                tokens[i].Value,
                new SourceSpan("<test>", 1, i + 1, 1, i + 2)));
        }

        return new LalrParser(Tables, Bound).Parse(new TokenListInput(list), host);
    }

    /// <summary>Makes a real music object carrying the given <c>types</c> tags.</summary>
    private static MusicObject NewMusic(params string[] types)
    {
        object typeList = Nil.Instance;
        for (int i = types.Length - 1; i >= 0; i--)
        {
            typeList = new Pair(Symbol.Intern(types[i]), typeList);
        }

        return new MusicObject(
            new Pair(new Pair(Symbol.Intern("types"), typeList), Nil.Instance));
    }

    // ------ embedded_scm_bare / embedded_scm_bare_arg ------

    [Fact]
    public void an_embedded_scheme_assignment_from_real_text_binds_the_evaluated_value()
    {
        //Arrange
        // x = #(+ 1 2) — embedded_scm_bare: SCM_TOKEN evaluates and its RESULT is
        // the value, unlike toplevel SCM_TOKEN which evaluates and ignores; the
        // value then rides the actionless embedded_scm / identifier_init_nonumber
        // pass-throughs into RAG1's assignment.
        ScriptedParserHost host = new ScriptedParserHost();
        host.EvalResults["(+ 1 2)"] = 3L;
        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), "x = #(+ 1 2)", "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        LalrParser parser = new LalrParser(Tables, Bound);

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Globals.Bindings[Symbol.Intern("x")].Should().Be(3L);
        host.EvaluatedTokens.Should().HaveCount(1);
        host.EvaluatedTokens[0].Should().Be("(+ 1 2)");
    }

    [Fact]
    public void an_embedded_scheme_bare_arg_token_evaluates_to_its_result()
    {
        //Arrange
        // embedded_scm_bare_arg: SCM_TOKEN — invoked directly because the arg
        // grammar around it is the RAG8-10 function-arglist territory.
        ScriptedParserHost host = new ScriptedParserHost();
        host.EvalResults["(x)"] = 99L;
        ParseContext context = NewContext(host);

        //Act
        object result = Action("embedded_scm_bare_arg: SCM_TOKEN")(
            context, new object[] { "(x)" }, new SourceSpan[1], default);

        //Assert
        result.Should().Be(99L);
        host.EvaluatedTokens.Should().HaveCount(1);
        host.EvaluatedTokens[0].Should().Be("(x)");
    }

    // ------ scm_function_call ------

    [Fact]
    public void a_scheme_function_call_dispatches_to_the_music_function_constructor()
    {
        //Arrange
        // scm_function_call: SCM_FUNCTION function_arglist — invoked directly
        // because the arglist rules are RAG8-10.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object function = new object();
        object arguments = Pair.List("arg");

        //Act
        object result = Action("scm_function_call: SCM_FUNCTION function_arglist")(
            context, new object[] { function, arguments }, new SourceSpan[2], default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("music-function");
        mark.Arguments.Should().HaveCount(2);
        mark.Arguments[0].Should().BeSameAs(function);
        mark.Arguments[1].Should().BeSameAs(arguments);
    }

    // ------ embedded_lilypond_number ------

    [Fact]
    public void an_embedded_lilypond_number_multiplies_and_returns_through_the_start_symbol()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();

        //Act
        object result = ParseTokens(
            host,
            ("EMBEDDED_LILY", null),
            ("UNSIGNED", 3L),
            ("NUMBER_IDENTIFIER", 4L));

        //Assert
        result.Should().Be(12L);
        host.LexerModeOperations.Should().Equal("push-note-state", "pop-state");
    }

    [Fact]
    public void a_negated_embedded_lilypond_number_negates()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();

        //Act
        object result = ParseTokens(
            host,
            ("EMBEDDED_LILY", null),
            ("'-'", '-'),
            ("UNSIGNED", 3L),
            ("NUMBER_IDENTIFIER", 4L));

        //Assert
        result.Should().Be(-12L);
    }

    // ------ embedded_lilypond ------

    [Fact]
    public void an_empty_embedded_lilypond_becomes_unspecified_music()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();

        //Act
        object result = ParseTokens(host, ("EMBEDDED_LILY", null));

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("unspecified-music");
        mark.Arguments.Should().BeEmpty();
        host.LexerModeOperations.Should().Equal("push-note-state", "pop-state");
    }

    [Fact]
    public void an_invalid_embedded_lilypond_raises_the_error_level_and_passes_the_rest_through()
    {
        //Arrange
        // embedded_lilypond: INVALID embedded_lilypond — the inner embedded_lilypond
        // reduces empty, so the whole start returns its unspecified-music.
        ScriptedParserHost host = new ScriptedParserHost();

        //Act
        object result = ParseTokens(host, ("EMBEDDED_LILY", null), ("INVALID", null));

        //Assert
        ((SyntaxMark)result).Name.Should().Be("unspecified-music");
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void an_embedded_lilypond_error_recovers_as_unspecified()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("embedded_lilypond: error")(
            context, new object[] { null }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(Unspecified.Instance);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void an_embedded_post_event_that_is_music_passes_through()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject postEvent = NewMusic("post-event");

        //Act
        object result = Action("embedded_lilypond: post_event")(
            context, new object[] { postEvent }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(postEvent);
    }

    [Fact]
    public void an_embedded_post_event_that_is_not_music_becomes_an_empty_post_events()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("embedded_lilypond: post_event")(
            context, new object[] { Unspecified.Instance }, new SourceSpan[1], default);

        //Assert
        MadeMusic made = (MadeMusic)result;
        made.Name.Should().Be("PostEvents");
        made.Properties.Should().BeEmpty();
    }

    [Fact]
    public void an_embedded_bare_duration_stays_a_duration_and_leaves_the_default_alone()
    {
        //Arrange
        // duration post_events with NO post events: upstream's body does nothing,
        // so the implicit $$ = $1 keeps the Duration — and default_duration_ is
        // NOT assigned.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object boxed = new Duration(3, 1);

        //Act
        object result = Action("embedded_lilypond: duration post_events %prec ':'")(
            context, new object[] { boxed, Nil.Instance }, new SourceSpan[2], default);

        //Assert
        result.Should().BeSameAs(boxed);
        host.DefaultDuration.Should().Be(new Duration(2, 0));
    }

    [Fact]
    public void an_embedded_duration_with_post_events_makes_a_note_event_and_sets_the_default_duration()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object boxed = new Duration(3, 1);
        MusicObject first = NewMusic("post-event");
        MusicObject second = NewMusic("post-event");

        // post_events accumulates front-to-back, so $2 arrives reversed.
        object reversedEvents = Pair.List(second, first);

        //Act
        object result = Action("embedded_lilypond: duration post_events %prec ':'")(
            context, new object[] { boxed, reversedEvents }, new SourceSpan[2], default);

        //Assert
        host.DefaultDuration.Should().Be(new Duration(3, 1));

        MadeMusic note = (MadeMusic)result;
        note.Name.Should().Be("NoteEvent");
        note.Properties.Should().HaveCount(2);
        note.Properties[0].Name.Should().Be("duration");
        note.Properties[0].Value.Should().BeSameAs(boxed);
        note.Properties[1].Name.Should().Be("articulations");
        Pair articulations = (Pair)note.Properties[1].Value;
        articulations.Car.Should().BeSameAs(first);
        ((Pair)articulations.Cdr).Car.Should().BeSameAs(second);
    }

    [Fact]
    public void two_embedded_musics_become_sequential_music_in_document_order()
    {
        //Arrange
        // embedded_lilypond: music_embedded music_embedded music_list — invoked
        // directly because music_embedded and music_list are RAG6.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject firstMusic = NewMusic();
        MusicObject secondMusic = NewMusic();

        //Act
        object result = Action("embedded_lilypond: music_embedded music_embedded music_list")(
            context,
            new object[] { firstMusic, secondMusic, Nil.Instance },
            new SourceSpan[3],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("sequential-music");
        Pair list = (Pair)mark.Arguments[0];
        list.Car.Should().BeSameAs(firstMusic);
        ((Pair)list.Cdr).Car.Should().BeSameAs(secondMusic);
        ((Pair)list.Cdr).Cdr.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void one_embedded_music_beside_a_non_music_is_returned_by_itself()
    {
        //Arrange
        // music_list ignores non-music, so a lone survivor is the single
        // expression, not a sequence.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject music = NewMusic();

        //Act
        object result = Action("embedded_lilypond: music_embedded music_embedded music_list")(
            context,
            new object[] { music, Unspecified.Instance, Nil.Instance },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().BeSameAs(music);
    }

    [Fact]
    public void post_events_after_a_note_attach_to_its_articulations_in_document_order()
    {
        //Arrange
        // Document order: note post1 post2. The action receives $1 = note,
        // $2 = post1, $3 = (post2) — music_list accumulates reversed.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject note = NewMusic("rhythmic-event");
        MusicObject post1 = NewMusic("post-event");
        MusicObject post2 = NewMusic("post-event");

        //Act
        object result = Action("embedded_lilypond: music_embedded music_embedded music_list")(
            context,
            new object[] { note, post1, Pair.List(post2) },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().BeSameAs(note);
        Pair articulations = (Pair)note.GetProperty("articulations");
        articulations.Car.Should().BeSameAs(post1);
        ((Pair)articulations.Cdr).Car.Should().BeSameAs(post2);
        ((Pair)articulations.Cdr).Cdr.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void a_single_embedded_post_event_sequence_compresses_to_the_post_event_itself()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject postEvent = NewMusic("post-event");

        //Act
        object result = Action("embedded_lilypond: music_embedded music_embedded music_list")(
            context,
            new object[] { postEvent, Unspecified.Instance, Nil.Instance },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().BeSameAs(postEvent);
    }

    [Fact]
    public void a_pure_post_event_sequence_compresses_to_one_post_events_music()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject post1 = NewMusic("post-event");
        MusicObject post2 = NewMusic("post-event");

        //Act
        object result = Action("embedded_lilypond: music_embedded music_embedded music_list")(
            context,
            new object[] { post1, post2, Nil.Instance },
            new SourceSpan[3],
            default);

        //Assert
        MadeMusic made = (MadeMusic)result;
        made.Name.Should().Be("PostEvents");
        made.Properties.Should().HaveCount(1);
        made.Properties[0].Name.Should().Be("elements");
        Pair elements = (Pair)made.Properties[0].Value;
        elements.Car.Should().BeSameAs(post1);
        ((Pair)elements.Cdr).Car.Should().BeSameAs(post2);
    }

    [Fact]
    public void an_unattached_post_event_is_preserved_on_an_empty_chord_and_warned_about()
    {
        //Arrange
        // Document order: post1 plain — a post event with NOTHING before it to
        // attach to. reverse_music_list runs with preserve, so it survives on an
        // event chord of its own, and the music is warned at.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject post1 = NewMusic("post-event");
        MusicObject plain = NewMusic();

        TextWriter savedOutput = Warn.Output;
        Warn.Output = TextWriter.Null;
        Warn.RecordMessages = true;
        Warn.ClearMessages();

        try
        {
            //Act
            object result = Action("embedded_lilypond: music_embedded music_embedded music_list")(
                context,
                new object[] { post1, plain, Nil.Instance },
                new SourceSpan[3],
                default);

            //Assert
            SyntaxMark sequence = (SyntaxMark)result;
            sequence.Name.Should().Be("sequential-music");
            Pair list = (Pair)sequence.Arguments[0];

            SyntaxMark chord = (SyntaxMark)list.Car;
            chord.Name.Should().Be("event-chord");
            ((Pair)chord.Arguments[0]).Car.Should().BeSameAs(post1);

            ((Pair)list.Cdr).Car.Should().BeSameAs(plain);

            Warn.Messages.Any(m => m.Contains("Unattached Music")).Should().BeTrue();
        }
        finally
        {
            Warn.RecordMessages = false;
            Warn.ClearMessages();
            Warn.Output = savedOutput;
        }
    }
}
