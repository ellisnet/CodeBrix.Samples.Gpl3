// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
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
/// RULE ACTION GROUP 6 — core music assembly, exercised two ways: whole inputs
/// through the REAL scanner and tables (braced and double-angle music lists over
/// embedded Scheme, <c>\repeat</c>/<c>\alternative</c>, <c>\new</c>-prefixed music
/// finished by <c>FINISH_MAKE_SYNTAX</c>, <c>\addlyrics</c> and <c>\lyricsto</c>,
/// and the <c>music_embedded_backup</c> <c>BACKUP</c> dance — the synthetic token
/// can only come from <c>MYBACKUP</c>, so a clean finish IS the token-flow
/// assertion), and direct invocation for the rules whose neighbours
/// (<c>pitch_or_music</c>, <c>duration</c>, <c>lyric_mode_music</c>) are RAG12/RAG16
/// and not ported yet.
/// </summary>
public class RuleActionRag6Tests
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

    private static ScriptedParserHost NewHost()
    {
        ScriptedParserHost host = new ScriptedParserHost();
        host.Keywords["addlyrics"] = ("ADDLYRICS", null);
        host.Keywords["alternative"] = ("ALTERNATIVE", null);
        host.Keywords["context"] = ("CONTEXT", null);
        host.Keywords["lyricsto"] = ("LYRICSTO", null);
        host.Keywords["new"] = ("NEWCONTEXT", null);
        host.Keywords["repeat"] = ("REPEAT", null);
        host.Keywords["sequential"] = ("SEQUENTIAL", null);
        host.Keywords["simultaneous"] = ("SIMULTANEOUS", null);
        host.Globals.Bindings[Symbol.Intern("toplevel-music-handler")] = "music-proc";
        return host;
    }

    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(string input)
    {
        ScriptedParserHost host = NewHost();
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
    }

    private static (ParseContext Context, ModalScanner Scanner, ScriptedParserHost Host) ScannerContext()
    {
        // A context whose token source is a REAL scanner over empty input, so an
        // action that pushes tokens by name can resolve them and a test can drain
        // what was pushed.
        ScriptedParserHost host = NewHost();
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), string.Empty, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        ParseContext context = new ParseContext(
            new LalrParser(Tables, new Dictionary<int, RuleAction>()), scanner)
        {
            UserState = host,
        };
        return (context, scanner, host);
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

    private static List<object> Cars(object list)
    {
        List<object> cars = new List<object>();
        for (object p = list; p is Pair pair; p = pair.Cdr)
        {
            cars.Add(pair.Car);
        }

        return cars;
    }

    /// <summary>The music the toplevel handler received in the last call.</summary>
    private static object ToplevelMusic(ScriptedParserHost host)
    {
        (object procedure, object[] arguments) = host.Calls[host.Calls.Count - 1];
        procedure.AsText().Should().Be("music-proc");
        return arguments[0];
    }

    // ------ whole inputs through the real scanner and tables ------

    [Fact]
    public void empty_braces_at_toplevel_become_sequential_music_over_the_empty_list()
    {
        //Arrange
        // { } — music_list: /* empty */, braced_music_list, sequential_music:
        // braced_music_list and grouped_music_list: sequential_music, end to end.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("{ }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("sequential-music");
        mark.Arguments[0].Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void embedded_scheme_music_in_braces_arrives_in_document_order()
    {
        //Arrange
        // music_list conses in REVERSE for efficient append; braced_music_list's
        // reverse_music_list restores document order.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ #(one) #(two) }");
        MusicObject first = NewMusic();
        MusicObject second = NewMusic();
        host.EvalResults["(one)"] = first;
        host.EvalResults["(two)"] = second;

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("sequential-music");
        Cars(mark.Arguments[0]).Should().Equal(first, second);
    }

    [Fact]
    public void double_angles_make_simultaneous_music_in_document_order()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("<< #(one) #(two) >>");
        MusicObject first = NewMusic();
        MusicObject second = NewMusic();
        host.EvalResults["(one)"] = first;
        host.EvalResults["(two)"] = second;

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("simultaneous-music");
        Cars(mark.Arguments[0]).Should().Equal(first, second);
    }

    [Fact]
    public void the_simultaneous_keyword_makes_simultaneous_music_over_a_braced_list()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\simultaneous { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("simultaneous-music");
        mark.Arguments[0].Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void the_sequential_keyword_makes_sequential_music_over_a_braced_list()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\sequential { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("sequential-music");
        mark.Arguments[0].Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void a_repeat_from_real_text_dispatches_to_the_repeat_constructor()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\repeat \"volta\" 2 { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("repeat");
        mark.Arguments[0].AsText().Should().Be("volta");
        mark.Arguments[1].Should().Be(2L);
        ((SyntaxMark)mark.Arguments[2]).Name.Should().Be("sequential-music");
    }

    [Fact]
    public void a_repeat_with_an_alternative_dispatches_to_repeat_alt()
    {
        //Arrange
        // \alternative basic_music becomes the alternative constructor, and the
        // five-symbol repeated_music alternative carries it as its fifth argument.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\repeat \"volta\" 2 { } \\alternative { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("repeat-alt");
        mark.Arguments[0].AsText().Should().Be("volta");
        mark.Arguments[1].Should().Be(2L);
        ((SyntaxMark)mark.Arguments[2]).Name.Should().Be("sequential-music");
        SyntaxMark alternative = (SyntaxMark)mark.Arguments[3];
        alternative.Name.Should().Be("alternative");
        ((SyntaxMark)alternative.Arguments[0]).Name.Should().Be("sequential-music");
    }

    [Fact]
    public void a_new_context_from_real_text_finishes_the_prefix_with_its_music()
    {
        //Arrange
        // \new Staff { } — RAG5's context_prefix built (constructor Staff id mods)
        // WITHOUT calling it; contexted_basic_music now applies it to the braced
        // music (FINISH_MAKE_SYNTAX).
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\new Staff { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.AppliedSyntax.Should().HaveCount(1);
        SyntaxMark applied = (SyntaxMark)ToplevelMusic(host);
        applied.Name.Should().Be("constructor:context-create");
        applied.Arguments.Should().HaveCount(4);
        applied.Arguments[0].Should().BeSameAs(Symbol.Intern("Staff"));
        ((SyntaxMark)applied.Arguments[3]).Name.Should().Be("sequential-music");
    }

    [Fact]
    public void nested_new_contexts_finish_one_prefix_layer_at_a_time()
    {
        //Arrange
        // \new Staff \new Voice { } — contexted_basic_music: context_prefix
        // contexted_basic_music: the Voice prefix is finished first, and the Staff
        // prefix is applied to the result.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\new Staff \\new Voice { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.AppliedSyntax.Should().HaveCount(2);
        SyntaxMark outer = (SyntaxMark)ToplevelMusic(host);
        outer.Arguments[0].Should().BeSameAs(Symbol.Intern("Staff"));
        SyntaxMark inner = (SyntaxMark)outer.Arguments[3];
        inner.Name.Should().Be("constructor:context-create");
        inner.Arguments[0].Should().BeSameAs(Symbol.Intern("Voice"));
    }

    [Fact]
    public void addlyrics_after_music_wraps_it_in_add_lyrics_in_document_order()
    {
        //Arrange
        // { } \addlyrics { } \addlyrics { } — new_lyrics accumulates its alist in
        // reverse and composite_music: basic_music new_lyrics restores order. The
        // lyric music values themselves are RAG12's lyric_mode_music and are not
        // pinned here; the mods entries (RAG5, ported) are.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ } \\addlyrics { } \\addlyrics { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("add-lyrics");
        ((SyntaxMark)mark.Arguments[0]).Name.Should().Be("sequential-music");
        List<object> entries = Cars(mark.Arguments[1]);
        entries.Should().HaveCount(2);
        ((Pair)entries[0]).Cdr.Should().BeSameAs(Nil.Instance);
        ((Pair)entries[1]).Cdr.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void addlyrics_after_a_new_context_goes_with_the_context_prefix_rule()
    {
        //Arrange
        // \new Staff { } \addlyrics { } — the three-symbol contexted_basic_music
        // alternative: the prefix is finished over @1-@2 and the whole is wrapped
        // in add_lyrics.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\new Staff { } \\addlyrics { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("add-lyrics");
        SyntaxMark applied = (SyntaxMark)mark.Arguments[0];
        applied.Name.Should().Be("constructor:context-create");
        applied.Arguments[0].Should().BeSameAs(Symbol.Intern("Staff"));
        Cars(mark.Arguments[1]).Should().HaveCount(1);
    }

    [Fact]
    public void lyricsto_with_a_voice_name_dispatches_to_lyric_combine()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\lyricsto \"sop\" { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("lyric-combine");
        mark.Arguments[0].AsText().Should().Be("sop");
        mark.Arguments[1].Should().BeSameAs(Nil.Instance);

        // Arguments[2] is lyric_mode_music, whose action is RAG12 and not pinned.
    }

    [Fact]
    public void lyricsto_with_a_context_type_passes_the_symbol_as_sync_type()
    {
        //Arrange
        // \lyricsto NullVoice = "sop" ... — upstream's argument order is $4, $2,
        // $5: the voice NAME first, the context-type SYMBOL second.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\lyricsto NullVoice = \"sop\" { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("lyric-combine");
        mark.Arguments[0].AsText().Should().Be("sop");
        mark.Arguments[1].Should().BeSameAs(Symbol.Intern("NullVoice"));
    }

    [Fact]
    public void an_embedded_markup_in_lyric_state_runs_the_backup_token_dance()
    {
        //Arrange
        // { #(m) } with the lexer in lyric state and the expression evaluating to a
        // markup: music_embedded_backup MYBACKUPs it as LYRIC_ELEMENT, and the parse
        // can ONLY finish through `music_embedded: music_embedded_backup BACKUP
        // lyric_element_music` — BACKUP never comes from raw text, so a clean finish
        // IS the token-flow assertion.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ #(m) }");
        host.IsLyricState = true;
        host.EvalResults["(m)"] = "la";

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
        host.Warnings.Should().BeEmpty();
        ((SyntaxMark)ToplevelMusic(host)).Name.Should().Be("sequential-music");
    }

    [Fact]
    public void an_embedded_non_music_value_outside_lyric_state_is_ignored_with_a_warning()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ #(m) }");
        host.EvalResults["(m)"] = "la";

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
        host.Warnings.Should().HaveCount(1);
        host.Warnings[0].Message.Should().Contain("Ignoring non-music expression");
        SyntaxMark mark = (SyntaxMark)ToplevelMusic(host);
        mark.Name.Should().Be("sequential-music");
        mark.Arguments[0].Should().BeSameAs(Nil.Instance);
    }

    // ------ the MYBACKUP site, invoked over a drainable scanner ------

    [Fact]
    public void music_embedded_backup_backs_a_lyric_markup_up_as_lyric_element()
    {
        //Arrange
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        host.IsLyricState = true;

        //Act
        object result = Action("music_embedded_backup: embedded_scm")(
            context, new object[] { "tra" }, new SourceSpan[1], default);

        //Assert
        // MYBACKUP pushes token-then-BACKUP, so BACKUP comes back FIRST; $$ stays
        // the pre-set $1, which both consumers ignore.
        result.AsText().Should().Be("tra");
        ParserToken backup = scanner.Next();
        backup.Symbol.AsText().Should().Be(Sym("BACKUP"));
        backup.Value.Should().BeSameAs(Unspecified.Instance);
        ParserToken element = scanner.Next();
        element.Symbol.AsText().Should().Be(Sym("LYRIC_ELEMENT"));
        element.Value.AsText().Should().Be("tra");
    }

    [Fact]
    public void music_embedded_backup_passes_music_and_unspecified_through_untouched()
    {
        //Arrange
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        host.IsLyricState = true;
        MusicObject music = NewMusic();

        //Act
        object musicResult = Action("music_embedded_backup: embedded_scm")(
            context, new object[] { music }, new SourceSpan[1], default);
        object unspecifiedResult = Action("music_embedded_backup: embedded_scm")(
            context, new object[] { Unspecified.Instance }, new SourceSpan[1], default);

        //Assert
        musicResult.Should().BeSameAs(music);
        unspecifiedResult.Should().BeSameAs(Unspecified.Instance);
        host.Warnings.Should().BeEmpty();
        scanner.Next().Symbol.Should().Be(0); // nothing was pushed back
    }

    [Fact]
    public void music_embedded_backup_warns_and_drops_a_non_music_value()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("music_embedded_backup: embedded_scm")(
            context, new object[] { 7L }, new SourceSpan[1], default);

        //Assert
        // @$.warning — a WARNING at the rule's own span, not a parser error.
        result.Should().BeSameAs(Unspecified.Instance);
        host.Warnings.Should().HaveCount(1);
        host.Warnings[0].Message.Should().Contain("Ignoring non-music expression");
        context.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
    }

    // ------ value-shaping rules, invoked directly ------

    [Fact]
    public void music_list_skips_a_non_music_element()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object list = Pair.List(NewMusic());

        //Act
        object result = Action("music_list: music_list music_embedded")(
            context, new object[] { list, Unspecified.Instance }, new SourceSpan[2], default);

        //Assert
        result.Should().BeSameAs(list);
    }

    [Fact]
    public void music_list_error_recovery_conses_an_error_marked_music()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("music_list: music_list error")(
            context, new object[] { Nil.Instance, null }, new SourceSpan[2], default);

        //Assert
        Pair pair = (Pair)result;
        pair.Cdr.Should().BeSameAs(Nil.Instance);
        MadeMusic made = (MadeMusic)pair.Car;
        made.Name.Should().Be("Music");
        made.Properties.Should().HaveCount(1);
        made.Properties[0].Name.Should().Be("error-found");
        made.Properties[0].Value.Should().Be(true);
    }

    [Fact]
    public void music_embedded_hands_the_backed_up_value_and_the_lyric_music_on()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object backedUp = new object();
        object lyricMusic = new object();

        //Act
        object unitResult = Action("music_embedded: music_embedded_backup")(
            context, new object[] { backedUp }, new SourceSpan[1], default);
        object danceResult = Action(
            "music_embedded: music_embedded_backup BACKUP lyric_element_music")(
            context,
            new object[] { backedUp, null, lyricMusic },
            new SourceSpan[3],
            default);

        //Assert
        unitResult.Should().BeSameAs(backedUp);
        danceResult.Should().BeSameAs(lyricMusic);
    }

    [Fact]
    public void an_embedded_bare_duration_always_becomes_a_note_event_here()
    {
        //Arrange
        // THE TWIN TRAP: unlike RAG2's `embedded_lilypond: duration post_events`,
        // where a bare duration stays a Duration and the default is left alone,
        // music_embedded ALWAYS makes the NoteEvent and ALWAYS assigns the
        // parser's default duration — only the articulations are conditional.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object boxed = new Duration(3, 1);

        //Act
        object result = Action("music_embedded: duration post_events %prec ':'")(
            context, new object[] { boxed, Nil.Instance }, new SourceSpan[2], default);

        //Assert
        host.DefaultDuration.Should().Be(new Duration(3, 1));
        MadeMusic note = (MadeMusic)result;
        note.Name.Should().Be("NoteEvent");
        note.Properties.Should().HaveCount(1);
        note.Properties[0].Name.Should().Be("duration");
        note.Properties[0].Value.Should().BeSameAs(boxed);
    }

    [Fact]
    public void an_embedded_duration_with_post_events_attaches_them_in_document_order()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object boxed = new Duration(1, 0);
        MusicObject first = NewMusic("post-event");
        MusicObject second = NewMusic("post-event");

        //Act
        // post_events accumulates front-to-back, so $2 arrives reversed.
        object result = Action("music_embedded: duration post_events %prec ':'")(
            context,
            new object[] { boxed, Pair.List(second, first) },
            new SourceSpan[2],
            default);

        //Assert
        host.DefaultDuration.Should().Be(new Duration(1, 0));
        MadeMusic note = (MadeMusic)result;
        note.Name.Should().Be("NoteEvent");
        note.Properties.Should().HaveCount(2);
        note.Properties[1].Name.Should().Be("articulations");
        Cars(note.Properties[1].Value).Should().Equal(first, second);
    }

    [Fact]
    public void pitch_as_music_passes_real_music_through()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        MusicObject music = NewMusic();

        //Act
        object result = Action("pitch_as_music: pitch_or_music")(
            context, new object[] { music }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(music);
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void pitch_as_music_reports_music_expected_for_a_non_music_value()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("pitch_as_music: pitch_or_music")(
            context, new object[] { 3.5 }, new SourceSpan[1], default);

        //Assert
        ((SyntaxMark)result).Name.Should().Be("unspecified-music");
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }

    // ------ make_music_from_simple, the epilogue helper behind pitch_as_music ------

    [Fact]
    public void a_drum_name_becomes_a_note_event_with_the_default_duration()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        host.DefaultDuration = new Duration(3, 1);
        host.WordScans[Symbol.Intern("bd")] = ("DRUM_PITCH", Symbol.Intern("bassdrum"));

        //Act
        object result = ParserActionHelpers.MakeMusicFromSimple(
            host, default, Symbol.Intern("bd"));

        //Assert
        MadeMusic note = (MadeMusic)result;
        note.Name.Should().Be("NoteEvent");
        note.Properties[0].Name.Should().Be("duration");
        note.Properties[0].Value.Should().Be(new Duration(3, 1));
        note.Properties[1].Name.Should().Be("drum-type");
        note.Properties[1].Value.Should().BeSameAs(Symbol.Intern("bassdrum"));
    }

    [Fact]
    public void a_note_name_in_note_state_becomes_a_note_event_with_its_pitch()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        host.IsNoteState = true;
        host.DefaultDuration = new Duration(3, 0);
        Pitch pitch = new Pitch(0, 2, CodeBrix.LilyPort.Flower.Rational.Zero);
        host.WordScans[Symbol.Intern("mi")] = ("NOTENAME_PITCH", pitch);

        //Act
        object result = ParserActionHelpers.MakeMusicFromSimple(
            host, default, Symbol.Intern("mi"));

        //Assert
        MadeMusic note = (MadeMusic)result;
        note.Name.Should().Be("NoteEvent");
        note.Properties[0].Name.Should().Be("duration");
        note.Properties[0].Value.Should().Be(new Duration(3, 0));
        note.Properties[1].Name.Should().Be("pitch");
        note.Properties[1].Value.Should().BeSameAs(pitch);
    }

    [Fact]
    public void a_power_of_two_integer_in_note_state_becomes_a_duration_note_event()
    {
        //Arrange
        // make_duration (4) is a quarter note — Duration (intlog2 (4), 0) — which is
        // distinguishable from the default duration set to something else.
        ScriptedParserHost host = NewHost();
        host.IsNoteState = true;
        host.DefaultDuration = new Duration(3, 1);

        //Act
        object result = ParserActionHelpers.MakeMusicFromSimple(host, default, 4L);

        //Assert
        MadeMusic note = (MadeMusic)result;
        note.Name.Should().Be("NoteEvent");
        note.Properties.Should().HaveCount(1);
        note.Properties[0].Name.Should().Be("duration");
        note.Properties[0].Value.Should().Be(new Duration(2, 0));
    }

    [Fact]
    public void a_non_power_of_two_integer_in_note_state_comes_back_unchanged()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        host.IsNoteState = true;

        //Act
        object result = ParserActionHelpers.MakeMusicFromSimple(host, default, 6L);

        //Assert
        result.Should().Be(6L);
    }

    [Fact]
    public void a_markup_in_lyric_state_becomes_a_lyric_event()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        host.IsLyricState = true;
        host.DefaultDuration = new Duration(2, 1);

        //Act
        object result = ParserActionHelpers.MakeMusicFromSimple(host, default, "doo");

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("lyric-event");
        mark.Arguments[0].AsText().Should().Be("doo");
        mark.Arguments[1].Should().Be(new Duration(2, 1));
    }

    [Fact]
    public void a_pitch_in_chord_state_becomes_an_event_chord_with_located_elements()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        host.IsChordState = true;
        host.DefaultDuration = new Duration(2, 0);
        Pitch pitch = new Pitch(0, 0, CodeBrix.LilyPort.Flower.Rational.Zero);
        MusicObject element = NewMusic();
        host.ChordElementsResult = Pair.List(element);
        SourceSpan where = new SourceSpan("<test>", 3, 5, 3, 7);

        //Act
        object result = ParserActionHelpers.MakeMusicFromSimple(host, where, pitch);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("event-chord");
        Cars(mark.Arguments[0]).Should().Equal(element);

        host.ChordElementCalls.Should().HaveCount(1);
        host.ChordElementCalls[0].Pitch.Should().BeSameAs(pitch);
        host.ChordElementCalls[0].Duration.Should().Be(new Duration(2, 0));
        host.ChordElementCalls[0].Modifications.Should().BeSameAs(Nil.Instance);

        // make_chord_elements stamps every element with the location — and an origin is
        // an Input, the type ly:input-location? answers on, converted from the parser's
        // own span. Asserting the IDENTITY of the origin the conversion returned is a
        // stronger fence than a column number: it fails if the stamp is dropped, if it
        // is taken from a different location, or if the raw span is stamped instead.
        host.SchemeLocations.Should().HaveCount(1);
        host.SchemeLocations[0].Span.StartColumn.Should().Be(5);
        element.Origin.Should().BeSameAs(host.SchemeLocations[0].Origin);
    }

    [Fact]
    public void a_value_no_mode_can_interpret_comes_back_unchanged()
    {
        //Arrange
        // A symbol the word tables do not know, with no mode active, is handed back
        // for the caller (pitch_as_music) to reject.
        ScriptedParserHost host = NewHost();

        //Act
        object result = ParserActionHelpers.MakeMusicFromSimple(
            host, default, Symbol.Intern("unknown"));

        //Assert
        result.Should().BeSameAs(Symbol.Intern("unknown"));
    }

    // ------ the FINISH_MAKE_SYNTAX rules, invoked directly ------

    [Fact]
    public void finishing_a_context_prefix_appends_the_music_to_the_constructor_arguments()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object constructor = new object();
        object start = Pair.List(constructor, Symbol.Intern("Staff"), Nil.Instance);
        object music = new object();
        SourceSpan whole = new SourceSpan("<test>", 1, 1, 1, 30);

        //Act
        object result = Action(
            "contexted_basic_music: context_prefix contextable_music %prec COMPOSITE")(
            context, new object[] { start, music }, new SourceSpan[2], whole);

        //Assert
        SyntaxMark applied = (SyntaxMark)result;
        applied.Arguments.Should().HaveCount(3);
        applied.Arguments[0].Should().BeSameAs(Symbol.Intern("Staff"));
        applied.Arguments[1].Should().BeSameAs(Nil.Instance);
        applied.Arguments[2].Should().BeSameAs(music);

        host.AppliedSyntax.Should().HaveCount(1);
        host.AppliedSyntax[0].Constructor.Should().BeSameAs(constructor);
        host.AppliedSyntax[0].Location.EndColumn.Should().Be(30);

        // scm_append_x: the start list's OWN pairs were extended in place.
        Cars(((Pair)start).Cdr).Should().Equal(
            Symbol.Intern("Staff"), Nil.Instance, music);
    }

    [Fact]
    public void finishing_a_prefix_with_lyrics_locates_the_context_at_the_prefix_and_music_only()
    {
        //Arrange
        // Input i; i.set_location (@1, @2); — the finished context music spans the
        // prefix and the music, NOT the trailing lyrics, while add_lyrics gets @$.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object start = Pair.List(new object(), Symbol.Intern("Staff"));
        object music = new object();
        object lyricA = new Pair(new object(), Nil.Instance);
        object lyricB = new Pair(new object(), Nil.Instance);
        object lyrics = Pair.List(lyricB, lyricA); // accumulated in reverse

        SourceSpan[] spans =
        {
            new SourceSpan("<test>", 1, 1, 1, 10),
            new SourceSpan("<test>", 1, 11, 1, 15),
            new SourceSpan("<test>", 1, 16, 1, 40),
        };

        //Act
        object result = Action(
            "contexted_basic_music: context_prefix contextable_music new_lyrics %prec COMPOSITE")(
            context,
            new object[] { start, music, lyrics },
            spans,
            new SourceSpan("<test>", 1, 1, 1, 40));

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("add-lyrics");
        mark.Arguments[0].Should().BeOfType<SyntaxMark>();

        // The alist ENTRIES, restored to document order.
        Cars(mark.Arguments[1]).Should().Equal(lyricA, lyricB);

        host.AppliedSyntax.Should().HaveCount(1);
        host.AppliedSyntax[0].Location.StartColumn.Should().Be(1);
        host.AppliedSyntax[0].Location.EndColumn.Should().Be(15);
    }

    // ------ the remaining value-shaping rules ------

    [Fact]
    public void new_lyrics_builds_and_extends_the_reversed_alist()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object firstMusic = new object();
        object firstMods = Nil.Instance;
        object secondMusic = new object();
        object secondMods = Pair.List("mod");

        //Act
        object one = Action("new_lyrics: ADDLYRICS optional_context_mods lyric_mode_music")(
            context,
            new object[] { null, firstMods, firstMusic },
            new SourceSpan[3],
            default);
        object two = Action(
            "new_lyrics: new_lyrics ADDLYRICS optional_context_mods lyric_mode_music")(
            context,
            new object[] { one, null, secondMods, secondMusic },
            new SourceSpan[4],
            default);

        //Assert
        // scm_acons conses in FRONT, so the newest entry leads.
        Pair first = (Pair)((Pair)two).Car;
        first.Car.Should().BeSameAs(secondMusic);
        first.Cdr.Should().BeSameAs(secondMods);
        ((Pair)two).Cdr.Should().BeSameAs(one);
        Pair second = (Pair)((Pair)one).Car;
        second.Car.Should().BeSameAs(firstMusic);
        second.Cdr.Should().BeSameAs(firstMods);
    }

    [Fact]
    public void composite_music_with_lyrics_restores_document_order()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object music = new object();
        object entryA = new Pair(new object(), Nil.Instance);
        object entryB = new Pair(new object(), Nil.Instance);

        //Act
        object result = Action("composite_music: basic_music new_lyrics %prec COMPOSITE")(
            context,
            new object[] { music, Pair.List(entryB, entryA) },
            new SourceSpan[2],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("add-lyrics");
        mark.Arguments[0].Should().BeSameAs(music);
        Cars(mark.Arguments[1]).Should().Equal(entryA, entryB);
    }

    [Fact]
    public void lyricsto_actions_place_their_arguments_in_upstream_order()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object lyricMusic = new object();

        //Act
        object plain = Action("basic_music: LYRICSTO simple_string lyric_mode_music")(
            context, new object[] { null, "sop", lyricMusic }, new SourceSpan[3], default);
        object typed = Action(
            "basic_music: LYRICSTO symbol '=' simple_string lyric_mode_music")(
            context,
            new object[] { null, Symbol.Intern("NullVoice"), '=', "sop", lyricMusic },
            new SourceSpan[5],
            default);

        //Assert
        SyntaxMark plainMark = (SyntaxMark)plain;
        plainMark.Name.Should().Be("lyric-combine");
        plainMark.Arguments.AsText().Should().Equal("sop", Nil.Instance, lyricMusic);

        SyntaxMark typedMark = (SyntaxMark)typed;
        typedMark.Name.Should().Be("lyric-combine");
        typedMark.Arguments.AsText().Should().Equal("sop", Symbol.Intern("NullVoice"), lyricMusic);
    }

    [Fact]
    public void grouped_music_list_hands_either_grouping_through()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object simultaneous = new object();
        object sequential = new object();

        //Act
        object fromSimultaneous = Action("grouped_music_list: simultaneous_music")(
            context, new object[] { simultaneous }, new SourceSpan[1], default);
        object fromSequential = Action("grouped_music_list: sequential_music")(
            context, new object[] { sequential }, new SourceSpan[1], default);

        //Assert
        fromSimultaneous.Should().BeSameAs(simultaneous);
        fromSequential.Should().BeSameAs(sequential);
    }
}
