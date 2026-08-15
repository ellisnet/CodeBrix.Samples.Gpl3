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
/// RULE ACTION GROUP 12 — mode changes and lyric mode, exercised two ways: whole
/// inputs through the REAL scanner and tables with a scripted host (every mode
/// keyword parses end-to-end because the RAG1 spine and the RAG5
/// <c>optional_context_mods</c> are ported; the surrounding
/// <c>grouped_music_list</c> machinery is RAG6 and reduces by default), and direct
/// invocation for the value shapes and for <c>lyric_element_music</c>, whose
/// neighbours (<c>optional_notemode_duration</c>, <c>post_events</c>) are RAG15/16
/// and not ported yet. NOTE the scripted host only RECORDS mode pushes — the
/// scanner keeps lexing in the outer mode here, so these parses never depend on
/// the pushed mode's lexing.
/// </summary>
public class RuleActionRag12Tests
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

    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(string input)
    {
        ScriptedParserHost host = new ScriptedParserHost();
        host.Keywords["addlyrics"] = ("ADDLYRICS", null);
        host.Keywords["chordmode"] = ("CHORDMODE", null);
        host.Keywords["chords"] = ("CHORDS", null);
        host.Keywords["drummode"] = ("DRUMMODE", null);
        host.Keywords["drums"] = ("DRUMS", null);
        host.Keywords["figuremode"] = ("FIGUREMODE", null);
        host.Keywords["figures"] = ("FIGURES", null);
        host.Keywords["lyricmode"] = ("LYRICMODE", null);
        host.Keywords["lyrics"] = ("LYRICS", null);
        host.Keywords["new"] = ("NEWCONTEXT", null);
        host.Keywords["notemode"] = ("NOTEMODE", null);
        host.Globals.Bindings[Symbol.Intern("toplevel-music-handler")] = "music-proc";

        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);

        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
    }

    private static object HandledMusic(ScriptedParserHost host)
    {
        (object procedure, object[] arguments) = host.Calls[host.Calls.Count - 1];
        procedure.AsText().Should().Be("music-proc");
        return arguments[0];
    }

    // ------ real text: the mode_changing_head family ------

    [Fact]
    public void notemode_from_real_text_pushes_and_pops_note_state()
    {
        //Arrange
        // \notemode { } — mode_changing_head pushes, mode_changed_music pops, and
        // the music (not "chords") passes through unwrapped.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\notemode { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.LexerModeOperations.AsText().Should().Equal("push-note-state", "pop-state");
        host.Calls.Should().NotBeEmpty();
    }

    [Fact]
    public void each_plain_mode_keyword_pushes_its_own_state()
    {
        //Arrange
        (string Input, string Operation)[] cases =
        {
            ("\\drummode { }", "push-drum-state"),
            ("\\figuremode { }", "push-figuredbass-state"),
            ("\\lyricmode { }", "push-lyric-state"),
        };

        foreach ((string input, string operation) in cases)
        {
            (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup(input);

            //Act
            parser.Parse(scanner, host);

            //Assert
            parser.ErrorCount.Should().Be(0);
            host.LexerModeOperations.AsText().Should().Equal(operation, "pop-state");
        }
    }

    [Fact]
    public void chordmode_from_real_text_installs_the_modifier_table_and_wraps_unrelativable()
    {
        //Arrange
        // \chordmode { } — the chordmodifiers identifier is looked up, handed to the
        // lexer, chord state is pushed, and the "chords" tag makes the result an
        // unrelativable-music wrap.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\chordmode { }");
        object modifiers = Pair.List(Symbol.Intern("maj"));
        host.Globals.Bindings[Symbol.Intern("chordmodifiers")] = modifiers;

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ChordModifierAssignments.Should().Equal(modifiers);
        host.LexerModeOperations.AsText().Should().Equal("push-chord-state", "pop-state");

        SyntaxMark wrapped = (SyntaxMark)HandledMusic(host);
        wrapped.Name.Should().Be("unrelativable-music");
    }

    // ------ real text: the mode_changing_head_with_context family ------

    [Fact]
    public void drums_from_real_text_creates_a_drumstaff_context()
    {
        //Arrange
        // \drums { } — the head names DrumStaff, optional_context_mods (RAG5) folds
        // the empty mods list, and mode_changed_music dispatches context-create with
        // SCM_EOL for the id.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\drums { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.LexerModeOperations.AsText().Should().Equal("push-drum-state", "pop-state");

        SyntaxMark created = (SyntaxMark)HandledMusic(host);
        created.Name.Should().Be("context-create");
        created.Arguments[0].Should().BeSameAs(Symbol.Intern("DrumStaff"));
        created.Arguments[1].Should().BeSameAs(Nil.Instance);
        created.Arguments[2].Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void figures_and_lyrics_from_real_text_create_their_contexts()
    {
        //Arrange
        (string Input, string Operation, string Context)[] cases =
        {
            ("\\figures { }", "push-figuredbass-state", "FiguredBass"),
            ("\\lyrics { }", "push-lyric-state", "Lyrics"),
        };

        foreach ((string input, string operation, string contextName) in cases)
        {
            (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup(input);

            //Act
            parser.Parse(scanner, host);

            //Assert
            parser.ErrorCount.Should().Be(0);
            host.LexerModeOperations.AsText().Should().Equal(operation, "pop-state");

            SyntaxMark created = (SyntaxMark)HandledMusic(host);
            created.Name.Should().Be("context-create");
            created.Arguments[0].Should().BeSameAs(Symbol.Intern(contextName));
        }
    }

    [Fact]
    public void chords_from_real_text_wraps_the_chordnames_context_unrelativable()
    {
        //Arrange
        // \chords { } — ChordNames earns the extra unrelativable-music wrap, and the
        // chordmodifiers lookup happens even when the identifier is undefined: the
        // lexer receives exactly what the lookup answered (SCM_UNDEFINED here).
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\chords { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ChordModifierAssignments.Should().Equal(DefaultArgument.Instance);
        host.LexerModeOperations.AsText().Should().Equal("push-chord-state", "pop-state");

        SyntaxMark wrapped = (SyntaxMark)HandledMusic(host);
        wrapped.Name.Should().Be("unrelativable-music");

        SyntaxMark created = (SyntaxMark)wrapped.Arguments[0];
        created.Name.Should().Be("context-create");
        created.Arguments[0].Should().BeSameAs(Symbol.Intern("ChordNames"));
    }

    // ------ real text: lyric_mode_music and optional_id ------

    [Fact]
    public void addlyrics_from_real_text_drives_the_lyric_mode_mid_rule()
    {
        //Arrange
        // { } \addlyrics { } — the $@10 mid-rule pushes lyric state BEFORE the
        // grouped_music_list and lyric_mode_music pops it after. Upstream reads the
        // lookahead before this mid-rule too (the state can shift MUSIC_IDENTIFIER),
        // so eager lexing matches upstream at this site.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ } \\addlyrics { }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.LexerModeOperations.AsText().Should().Equal("push-lyric-state", "pop-state");
    }

    [Fact]
    public void a_written_context_id_lands_in_the_prefix_and_an_absent_one_is_nil()
    {
        //Arrange
        // \new Staff = "up" { } — optional_id: '=' simple_string hands the string
        // through into RAG5's context_prefix cons; without the =, the empty
        // alternative answers SCM_EOL. Since RAG6 landed, contexted_basic_music
        // FINISHES the prefix (FINISH_MAKE_SYNTAX), so the id is read out of the
        // APPLIED constructor's argument list rather than the raw prefix.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\new Staff = \"up\" { }");
        (LalrParser bareParser, ModalScanner bareScanner, ScriptedParserHost bareHost)
            = Setup("\\new Staff { }");

        //Act
        parser.Parse(scanner, host);
        bareParser.Parse(bareScanner, bareHost);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark applied = (SyntaxMark)HandledMusic(host);
        applied.Name.Should().Be("constructor:context-create");
        applied.Arguments[0].Should().BeSameAs(Symbol.Intern("Staff"));
        applied.Arguments[1].AsText().Should().Be("up");

        bareParser.ErrorCount.Should().Be(0);
        SyntaxMark bareApplied = (SyntaxMark)HandledMusic(bareHost);
        bareApplied.Arguments[1].Should().BeSameAs(Nil.Instance);
    }

    // ------ real text: lyric elements outside lyric mode ------

    [Fact]
    public void a_string_in_music_outside_lyric_mode_is_an_error()
    {
        //Arrange
        // { "hello" } — the string reduces through lyric_element: STRING, and the
        // lexer (scripted to answer NOT lyric state) makes that a parser_error that
        // raises the error level without stopping the parse.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \"hello\" }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(1);
        parser.Diagnostics[0].Should().Contain("string outside of text script or \\lyricmode");
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_symbol_in_music_outside_lyric_mode_names_the_bad_note()
    {
        //Arrange
        // { la } — an unknown bare word lexes as SYMBOL, and lyric_element: SYMBOL
        // reports it with upstream's formatted "not a note name: %s" message.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ la }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(1);
        parser.Diagnostics[0].Should().Contain("not a note name: la");
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_string_in_lyric_state_is_a_quiet_lyric_event()
    {
        //Arrange
        // The same { "hello" } with the lexer scripted to BE in lyric state: no
        // diagnostic, and lyric_element_music dispatches the lyric-event.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \"hello\" }");
        host.IsLyricState = true;

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
    }

    // ------ direct invocation: the heads' value shapes ------

    [Fact]
    public void every_mode_changing_head_pushes_its_state_and_interns_its_tag()
    {
        //Arrange
        (string Identity, string Operation, string Tag)[] heads =
        {
            ("mode_changing_head: NOTEMODE", "push-note-state", "notes"),
            ("mode_changing_head: DRUMMODE", "push-drum-state", "drums"),
            ("mode_changing_head: FIGUREMODE", "push-figuredbass-state", "figures"),
            ("mode_changing_head: LYRICMODE", "push-lyric-state", "lyrics"),
            ("mode_changing_head_with_context: DRUMS", "push-drum-state", "DrumStaff"),
            ("mode_changing_head_with_context: FIGURES", "push-figuredbass-state", "FiguredBass"),
            ("mode_changing_head_with_context: LYRICS", "push-lyric-state", "Lyrics"),
        };

        foreach ((string identity, string operation, string tag) in heads)
        {
            ScriptedParserHost host = new ScriptedParserHost();
            ParseContext context = NewContext(host);

            //Act
            object result = Action(identity)(
                context, new object[] { "KEYWORD" }, new SourceSpan[1], default);

            //Assert
            result.Should().BeSameAs(Symbol.Intern(tag));
            host.LexerModeOperations.Should().Equal(operation);
        }
    }

    [Fact]
    public void the_chord_heads_install_the_looked_up_modifiers_before_pushing()
    {
        //Arrange
        (string Identity, string Tag)[] heads =
        {
            ("mode_changing_head: CHORDMODE", "chords"),
            ("mode_changing_head_with_context: CHORDS", "ChordNames"),
        };

        foreach ((string identity, string tag) in heads)
        {
            ScriptedParserHost host = new ScriptedParserHost();
            object modifiers = Pair.List(Symbol.Intern("dim"));
            host.Globals.Bindings[Symbol.Intern("chordmodifiers")] = modifiers;
            ParseContext context = NewContext(host);

            //Act
            object result = Action(identity)(
                context, new object[] { "KEYWORD" }, new SourceSpan[1], default);

            //Assert
            result.Should().BeSameAs(Symbol.Intern(tag));
            host.ChordModifierAssignments.Should().Equal(modifiers);
            host.LexerModeOperations.AsText().Should().Equal("push-chord-state");
        }
    }

    // ------ direct invocation: optional_id, lyric_mode_music, mode_changed_music ------

    [Fact]
    public void an_optional_id_is_the_string_or_the_empty_list()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object empty = Action("optional_id: /* empty */")(
            context, new object[0], new SourceSpan[0], default);
        object given = Action("optional_id: '=' simple_string")(
            context, new object[] { '=', "up" }, new SourceSpan[2], default);

        //Assert
        empty.Should().BeSameAs(Nil.Instance);
        given.AsText().Should().Be("up");
    }

    [Fact]
    public void lyric_mode_music_wraps_its_music_in_lyric_state()
    {
        //Arrange
        // $@10 pushes lyric state before grouped_music_list; the outer rule pops it
        // and passes the music through.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object music = new object();

        //Act
        object pushed = Action("$@10: /* empty */")(
            context, new object[0], new SourceSpan[0], default);
        object result = Action("lyric_mode_music: $@10 grouped_music_list")(
            context, new object[] { pushed, music }, new SourceSpan[2], default);

        //Assert
        pushed.Should().BeSameAs(Unspecified.Instance);
        result.Should().BeSameAs(music);
        host.LexerModeOperations.AsText().Should().Equal("push-lyric-state", "pop-state");
    }

    [Fact]
    public void mode_changed_music_wraps_only_the_chords_tag()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("mode_changed_music: mode_changing_head grouped_music_list");
        object music = new object();

        //Act
        object passed = action(
            context,
            new object[] { Symbol.Intern("notes"), music },
            new SourceSpan[2],
            default);
        object wrapped = action(
            context,
            new object[] { Symbol.Intern("chords"), music },
            new SourceSpan[2],
            default);

        //Assert
        passed.Should().BeSameAs(music);
        ((SyntaxMark)wrapped).Name.Should().Be("unrelativable-music");
        ((SyntaxMark)wrapped).Arguments[0].Should().BeSameAs(music);
        host.LexerModeOperations.AsText().Should().Equal("pop-state", "pop-state");
    }

    [Fact]
    public void mode_changed_music_with_context_creates_and_wraps_only_chordnames()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action(
            "mode_changed_music: mode_changing_head_with_context optional_context_mods grouped_music_list");
        object mods = Pair.List(Pair.List(Symbol.Intern("consists"), "X"));
        object music = new object();

        //Act
        object created = action(
            context,
            new object[] { Symbol.Intern("DrumStaff"), mods, music },
            new SourceSpan[3],
            default);
        object wrapped = action(
            context,
            new object[] { Symbol.Intern("ChordNames"), Nil.Instance, music },
            new SourceSpan[3],
            default);

        //Assert
        SyntaxMark drums = (SyntaxMark)created;
        drums.Name.Should().Be("context-create");
        drums.Arguments[0].Should().BeSameAs(Symbol.Intern("DrumStaff"));
        drums.Arguments[1].Should().BeSameAs(Nil.Instance);
        drums.Arguments[2].Should().BeSameAs(mods);
        drums.Arguments[3].Should().BeSameAs(music);

        SyntaxMark chords = (SyntaxMark)wrapped;
        chords.Name.Should().Be("unrelativable-music");
        ((SyntaxMark)chords.Arguments[0]).Name.Should().Be("context-create");

        host.LexerModeOperations.AsText().Should().Equal("pop-state", "pop-state");
    }

    // ------ direct invocation: lyric_element and lyric_element_music ------

    [Fact]
    public void a_markup_lyric_element_needs_lyric_state()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("lyric_element: full_markup");
        object markup = "the-markup";

        //Act
        host.IsLyricState = true;
        object accepted = action(
            context, new object[] { markup }, new SourceSpan[1], default);

        host.IsLyricState = false;
        object refused = action(
            context, new object[] { markup }, new SourceSpan[1], default);

        //Assert
        accepted.Should().BeSameAs(markup);
        refused.Should().BeSameAs(markup); // $$ = $1 even after the error
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_lyric_event_without_post_events_sets_no_articulations()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action(
            "lyric_element_music: lyric_element optional_notemode_duration post_events %prec ':'")(
            context,
            new object[] { "la", "the-duration", Nil.Instance },
            new SourceSpan[3],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("lyric-event");
        mark.Arguments.AsText().Should().Equal("la", "the-duration");
    }

    [Fact]
    public void a_lyric_events_post_events_become_reversed_articulations()
    {
        //Arrange
        // The accumulated post_events list is NEWEST FIRST; the action must reverse
        // it destructively onto the made music's articulations. The host here
        // answers MakeSyntax with a REAL music object, because the upstream body
        // sets the property on unsmob<Music> of the constructor's result.
        MusicSyntaxHost host = new MusicSyntaxHost();
        ParseContext context = NewContext(host);
        object first = "event-1";
        object second = "event-2";
        object accumulated = Pair.List(second, first); // (e2 e1), newest first

        //Act
        object result = Action(
            "lyric_element_music: lyric_element optional_notemode_duration post_events %prec ':'")(
            context,
            new object[] { "la", "the-duration", accumulated },
            new SourceSpan[3],
            default);

        //Assert
        host.SyntaxDispatches.Should().HaveCount(1);
        host.SyntaxDispatches[0].Name.Should().Be("lyric-event");
        host.SyntaxDispatches[0].Arguments.AsText().Should().Equal("la", "the-duration");

        MusicObject music = (MusicObject)result;
        Pair.ToList(music.GetProperty("articulations")).Should().Equal(first, second);
    }

    /// <summary>
    /// A host for the one action that must SET A PROPERTY on a
    /// <c>MAKE_SYNTAX</c> result: <see cref="MakeSyntax"/> answers a real
    /// <see cref="MusicObject"/> (recording the dispatch), because upstream's
    /// <c>lyric_event</c> constructor always makes music. Everything the action
    /// does not touch refuses loudly.
    /// </summary>
    private sealed class MusicSyntaxHost : IParserHost
    {
        public List<(string Name, object[] Arguments)> SyntaxDispatches { get; }
            = new List<(string, object[])>();

        public int ErrorLevel { get; set; }

        public Duration DefaultDuration { get; set; }

        // RAG16 additions; this host reaches none of them.
        public int DefaultTremoloType
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public object MakeSyntax(string constructor, SourceSpan location, params object[] arguments)
        {
            SyntaxDispatches.Add((constructor, arguments));
            return new MusicObject(Nil.Instance);
        }

        public object LookupIdentifier(string name) => throw new NotSupportedException();

        public void SetIdentifier(object key, object value) => throw new NotSupportedException();

        public object EvalSchemeToken(object token, SourceSpan location) => throw new NotSupportedException();

        public void PushNoteState() => throw new NotSupportedException();

        public void PopLexerState() => throw new NotSupportedException();

        // RAG18 additions; this host reaches neither.
        public void PushMarkupState() => throw new NotSupportedException();

        public object LilyImport(string name) => throw new NotSupportedException();

        public void AddScope(object module) => throw new NotSupportedException();

        public object RemoveScope() => throw new NotSupportedException();

        public object CurrentModule() => throw new NotSupportedException();

        public bool IsModule(object value) => throw new NotSupportedException();

        public object MakeModule() => throw new NotSupportedException();

        public void ModuleCopy(object destination, object source) => throw new NotSupportedException();

        public bool TryModuleVariable(object module, object name, out object value)
            => throw new NotSupportedException();

        public object Call(object procedure, params object[] arguments) => throw new NotSupportedException();

        public object LocOnCopy(object value, SourceSpan location) => throw new NotSupportedException();

        public object MakeMusic(string name, SourceSpan location) => throw new NotSupportedException();

        public void SetMusicProperty(object music, string name, object value)
            => throw new NotSupportedException();

        public bool IsMarkup(object value) => throw new NotSupportedException();

        public bool IsMarkupList(object value) => throw new NotSupportedException();

        public bool IsMarkupFunction(object value) => throw new NotSupportedException();

        public void DefineMarkupCommand(object name, object function) => throw new NotSupportedException();

        public bool IsScore(object value) => throw new NotSupportedException();

        public bool BookHasPaper(object book) => throw new NotSupportedException();

        public bool IsKey(object value) => throw new NotSupportedException();

        public object ScorifyMusic(object music) => throw new NotSupportedException();

        public object SyntaxConstructor(string constructor) => throw new NotSupportedException();

        public bool IsGrobSymbol(object value) => throw new NotSupportedException();

        public bool IsKeyList(object value) => throw new NotSupportedException();

        public void Warning(SourceSpan location, string message) => throw new NotSupportedException();

        public void PushLyricState() => throw new NotSupportedException();

        public void PushDrumState() => throw new NotSupportedException();

        public void PushFiguredBassState() => throw new NotSupportedException();

        public void PushChordState() => throw new NotSupportedException();

        public void SetChordModifiers(object modifiers) => throw new NotSupportedException();

        public bool IsLyricState => throw new NotSupportedException();

        public void PushInitialState() => throw new NotSupportedException();

        public void AddOutputDefScope(OutputDef definition) => throw new NotSupportedException();

        public bool IsNoteState => throw new NotSupportedException();

        public bool IsChordState => throw new NotSupportedException();

        public LexerLookup ScanWord(object word) => throw new NotSupportedException();

        public object ApplySyntax(object constructor, SourceSpan location, object arguments)
            => throw new NotSupportedException();

        public object ConstructChordElements(object pitch, object duration, object modifications)
            => throw new NotSupportedException();

        public object GetMusicProperty(object music, string name) => throw new NotSupportedException();

        public void MusicWarning(object music, string message) => throw new NotSupportedException();

        public bool IsScale(object value) => throw new NotSupportedException();

        public object ScaleToFactor(object value) => throw new NotSupportedException();

        // RAG15 additions; this host reaches none of them.
        public bool IsMusic(object value) => throw new NotSupportedException();

        public bool IsMusicType(object music, string type) => throw new NotSupportedException();

        public object CloneMusic(object music) => throw new NotSupportedException();

        public void SetMusicSpot(object music, SourceSpan location)
            => throw new NotSupportedException();

        public CodeBrix.LilyPort.Engine.Origins.Input SchemeLocation(SourceSpan location)
            => new CodeBrix.LilyPort.Engine.Origins.Input();
    }
}
