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
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 4 — the output definition, paper and tempo actions, exercised
/// two ways: whole inputs through the REAL scanner and tables with a scripted host
/// (\paper at top level and inside \book, \layout and \midi inside \score, and a
/// \context definition inside \layout, which is what reaches the mid-rule <c>$@8</c>),
/// and direct invocation for the branches whose surrounding grammar (music, tempo's
/// event_chord) is not ported yet. The <c>get_paper</c>/<c>get_midi</c>/
/// <c>get_layout</c> helpers are pinned directly too, because <c>get_paper</c> is
/// the read side of the <c>$papers</c> stack wave 1 left to this group.
/// </summary>
public class RuleActionRag4Tests
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
        host.Keywords["context"] = ("CONTEXT", null);
        host.Keywords["layout"] = ("LAYOUT", null);
        host.Keywords["midi"] = ("MIDI", null);
        host.Keywords["name"] = ("NAME", null);
        host.Keywords["paper"] = ("PAPER", null);
        host.Keywords["score"] = ("SCORE", null);

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

    // ------ paper/layout/midi blocks, from real text ------

    [Fact]
    public void a_toplevel_paper_block_becomes_the_default_paper()
    {
        //Arrange
        // \paper { } — the head clones $defaultpaper (get_paper's fallback when the
        // $papers stack is empty), the body opens its scope in the INITIAL lexer
        // mode, and RAG1's toplevel_expression: output_def stores the result back
        // as the session default.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("\\paper { }");
        OutputDef defaultPaper = PaperDef();
        defaultPaper.SetVariable("size", "a4");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = defaultPaper;

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);

        OutputDef parsed = (OutputDef)host.Globals.Bindings[Symbol.Intern("$defaultpaper")];
        parsed.Should().NotBeSameAs(defaultPaper);
        parsed.CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("paper"));
        parsed.CVariable("size").AsText().Should().Be("a4");
        parsed.InputOrigin.Should().BeOfType<SourceSpan>();

        host.LexerModeOperations.AsText().Should().Equal("push-initial-state", "pop-state");
        host.Scopes.Should().BeEmpty();
        host.OutputDefScopes.Should().HaveCount(1);
        host.OutputDefScopes[0].Definition.Should().BeSameAs(parsed);
    }

    [Fact]
    public void an_assignment_in_a_paper_body_lands_in_the_definitions_scope()
    {
        //Arrange
        // foo = "bar" inside \paper goes through RAG1's assignment action while the
        // definition's scope is on top of the stack — the scripted host records the
        // scope as a stand-in module, and the assignment must be in it.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\paper { foo = \"bar\" }");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = PaperDef();

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.OutputDefScopes.Should().HaveCount(1);
        host.OutputDefScopes[0].Module.Bindings[Symbol.Intern("foo")].AsText().Should().Be("bar");

        // The assignment also unwrapped the marker list, so the stored default is
        // the definition itself.
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")]
            .Should().BeSameAs(host.OutputDefScopes[0].Definition);
    }

    [Fact]
    public void a_scheme_token_in_a_paper_body_is_evaluated_and_ignored()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\paper { #(tweak) }");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = PaperDef();

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.EvaluatedTokens.AsText().Should().Equal("(tweak)");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")].Should().BeOfType<OutputDef>();
    }

    [Fact]
    public void a_paper_block_in_a_book_replaces_the_books_paper()
    {
        //Arrange
        // \book opens on a clone of $defaultpaper (RAG3); the \paper block clones
        // the top of $papers (get_paper's primary source) and book_body swaps it in.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\book { \\paper { } }");
        OutputDef defaultPaper = PaperDef();
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = defaultPaper;
        host.Globals.Bindings[Symbol.Intern("toplevel-book-handler")] = "book-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Calls[0].Procedure.AsText().Should().Be("book-proc");

        Book book = (Book)host.Calls[0].Arguments[0];
        book.Paper.Should().NotBeSameAs(defaultPaper);
        book.Paper.CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("paper"));
        book.Paper.Should().BeSameAs(host.OutputDefScopes[0].Definition);

        // The book closed cleanly behind it.
        host.Globals.Bindings[Symbol.Intern("$papers")].Should().BeSameAs(Nil.Instance);
        host.Scopes.Should().BeEmpty();
    }

    [Fact]
    public void a_layout_where_a_paper_belongs_is_refused_and_replaced()
    {
        //Arrange
        // \book { \layout { } } — paper_block reports "need \paper for paper block"
        // and substitutes a fresh get_paper result, so the book still ends up with
        // a genuine paper definition.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\book { \\layout { } }");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = PaperDef();

        //Act
        parser.Parse(scanner, host);

        //Assert
        host.ErrorLevel.Should().Be(1);
        parser.Diagnostics.Should().HaveCount(1);
        parser.Diagnostics[0].Should().Contain("need \\paper for paper block");

        Book built = (Book)host.Calls[0].Arguments[0];
        built.Paper.CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("paper"));
        built.Paper.Should().NotBeSameAs(host.OutputDefScopes[0].Definition);
    }

    [Fact]
    public void a_layout_and_a_midi_in_a_score_collect_into_the_score()
    {
        //Arrange
        // score_item: output_def is pass-through into RAG3's accumulator; with no
        // music the salvage path still folds both definitions in, in parse order.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\score { \\layout { } \\midi { } }");
        host.Globals.Bindings[Symbol.Intern("toplevel-score-handler")] = "score-proc";

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.Diagnostics.Should().HaveCount(1);
        parser.Diagnostics[0].Should().Contain("Missing music in \\score");

        Score score = (Score)host.Calls[0].Arguments[0];
        score.Defs.Should().HaveCount(2);
        score.Defs[0].CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("layout"));
        score.Defs[1].CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("midi"));
        score.Defs[0].InputOrigin.Should().NotBeNull();
        score.Defs[1].InputOrigin.Should().NotBeNull();

        host.LexerModeOperations.Should().Equal(
            "push-initial-state", "pop-state", "push-initial-state", "pop-state");
    }

    [Fact]
    public void a_context_definition_in_a_layout_block_is_assigned_under_its_name()
    {
        //Arrange
        // \layout { \context { \name "MyStaff" } } — the $@8 mid-rule unwraps the
        // marker list and pushes note mode, RAG5 builds the ContextDef, and the
        // outer rule assigns it into the layout under the context's own name.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\layout { \\context { \\name \"MyStaff\" } }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);

        OutputDef layout = (OutputDef)host.Globals.Bindings[Symbol.Intern("$defaultlayout")];
        object stored = layout.LookupVariable(Symbol.Intern("MyStaff"));
        stored.Should().BeOfType<ContextDef>();
        ((ContextDef)stored).ContextName.Should().BeSameAs(Symbol.Intern("MyStaff"));

        host.LexerModeOperations.Should().Equal(
            "push-initial-state", "push-note-state", "pop-state", "pop-state");
        host.Scopes.Should().BeEmpty();
    }

    [Fact]
    public void an_active_output_definition_replaces_the_definition_being_built()
    {
        //Arrange
        // \paper { \mylayout } with \mylayout carrying an output definition — the
        // "stupid trick" branch: while the marker list is still in place, the
        // active value replaces the definition wholesale, scope and all, and the
        // block finishes as the REPLACEMENT's kind.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("\\paper { \\mylayout }");
        OutputDef supplied = LayoutDef();
        host.Keywords["mylayout"] = ("SCM_IDENTIFIER", supplied);

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Globals.Bindings[Symbol.Intern("$defaultlayout")].Should().BeSameAs(supplied);
        host.Globals.Bindings.ContainsKey(Symbol.Intern("$defaultpaper")).Should().BeFalse();

        supplied.InputOrigin.Should().BeOfType<SourceSpan>();
        host.OutputDefScopes.Should().HaveCount(2);
        host.OutputDefScopes[1].Definition.Should().BeSameAs(supplied);
        host.Scopes.Should().BeEmpty();
    }

    // ------ paper_block and output_def_body branches, invoked directly ------

    [Fact]
    public void a_paper_output_def_passes_through_a_paper_block()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        OutputDef paper = PaperDef();

        //Act
        object result = Action("paper_block: output_def")(
            context,
            new object[] { paper },
            new SourceSpan[1],
            default);

        //Assert
        result.Should().BeSameAs(paper);
        context.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
    }

    [Fact]
    public void a_context_def_in_an_output_def_body_is_assigned_and_note_mode_closed()
    {
        //Arrange
        // The outer half of the $@8 pair: pop the note mode the mid-rule pushed,
        // then assign_context_def stores the definition under its name — the empty
        // symbol, for a bare `new ContextDef ()`.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        OutputDef layout = LayoutDef();
        ContextDef contextDef = new ContextDef();

        //Act
        object result = Action("output_def_body: output_def_body $@8 music_or_context_def")(
            context,
            new object[] { layout, Unspecified.Instance, contextDef },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().BeSameAs(layout);
        layout.LookupVariable(Symbol.Intern("")).Should().BeSameAs(contextDef);
        host.LexerModeOperations.AsText().Should().Equal("pop-state");
        host.Calls.Should().BeEmpty();
    }

    [Fact]
    public void music_in_an_output_def_body_reaches_the_output_def_music_handler()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        host.Globals.Bindings[Symbol.Intern("output-def-music-handler")] = "odm-proc";
        OutputDef midi = new OutputDef();
        MusicObject music = new MusicObject(Nil.Instance);

        //Act
        object result = Action("output_def_body: output_def_body $@8 music_or_context_def")(
            context,
            new object[] { midi, Unspecified.Instance, music },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().BeSameAs(midi);
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.AsText().Should().Be("odm-proc");
        host.Calls[0].Arguments[0].Should().BeSameAs(midi);
        host.Calls[0].Arguments[1].Should().BeSameAs(music);
    }

    [Fact]
    public void an_active_music_value_reaches_the_output_def_music_handler()
    {
        //Arrange
        // embedded_scm_active's "Seems unlikely, but let's be complete" branch, with
        // the marker list already unwrapped by an earlier body item.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        host.Globals.Bindings[Symbol.Intern("output-def-music-handler")] = "odm-proc";
        OutputDef layout = LayoutDef();
        MusicObject music = new MusicObject(Nil.Instance);

        //Act
        object result = Action("output_def_body: output_def_body embedded_scm_active")(
            context,
            new object[] { layout, music },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(layout);
        host.Calls.Should().HaveCount(1);
        host.Calls[0].Procedure.AsText().Should().Be("odm-proc");
        host.Calls[0].Arguments[1].Should().BeSameAs(music);
    }

    [Fact]
    public void an_active_value_of_bad_type_is_an_error()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        OutputDef paper = PaperDef();

        //Act
        object result = Action("output_def_body: output_def_body embedded_scm_active")(
            context,
            new object[] { new Pair(paper, Nil.Instance), 42L },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(paper);
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void an_unspecified_active_value_is_ignored()
    {
        //Arrange
        // SCM_UNSPECIFIED (an evaluated-for-effect Scheme call) is not an error; the
        // marker list is still unwrapped in passing.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        OutputDef paper = PaperDef();

        //Act
        object result = Action("output_def_body: output_def_body embedded_scm_active")(
            context,
            new object[] { new Pair(paper, Nil.Instance), Unspecified.Instance },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(paper);
        context.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
    }

    [Fact]
    public void error_recovery_keeps_the_body()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        OutputDef layout = LayoutDef();

        //Act
        object result = Action("output_def_body: output_def_body error")(
            context,
            new object[] { layout, null },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(layout);
    }

    // ------ tempo_event / tempo_range, invoked directly (event_chord is unported) ------

    [Fact]
    public void a_tempo_with_duration_and_count_dispatches_the_tempo_constructor()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object duration = new object();

        //Act
        object result = Action("tempo_event: TEMPO steno_duration '=' tempo_range")(
            context,
            new object[] { null, duration, null, 120L },
            new SourceSpan[4],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("tempo");
        mark.Arguments.Should().Equal(Nil.Instance, duration, 120L);
    }

    [Fact]
    public void a_tempo_with_text_duration_and_range_passes_all_three()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object duration = new object();
        object range = new Pair(96L, 120L);

        //Act
        object result = Action("tempo_event: TEMPO text steno_duration '=' tempo_range")(
            context,
            new object[] { null, "Allegro", duration, null, range },
            new SourceSpan[5],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("tempo");
        mark.Arguments.AsText().Should().Equal("Allegro", duration, range);
    }

    [Fact]
    public void a_bare_tempo_text_dispatches_text_only()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("tempo_event: TEMPO text %prec ':'")(
            context,
            new object[] { null, "Adagio" },
            new SourceSpan[2],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("tempo");
        mark.Arguments.AsText().Should().Equal("Adagio");
    }

    [Fact]
    public void a_tempo_range_number_passes_through()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("tempo_range: exact_unsigned_number %prec ':'")(
            context,
            new object[] { 96L },
            new SourceSpan[1],
            default);

        //Assert
        result.Should().Be(96L);
    }

    [Fact]
    public void a_tempo_range_pair_conses_its_bounds()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("tempo_range: exact_unsigned_number '-' exact_unsigned_number")(
            context,
            new object[] { 96L, null, 120L },
            new SourceSpan[3],
            default);

        //Assert
        Pair pair = (Pair)result;
        pair.Car.Should().Be(96L);
        pair.Cdr.Should().Be(120L);
    }

    // ------ the get_paper/get_midi/get_layout helpers ------

    [Fact]
    public void get_paper_clones_the_top_of_the_papers_stack()
    {
        //Arrange
        // "Return a copy of the top of $papers stack, or $defaultpaper if the stack
        // is empty" — with a stack, the default must NOT be consulted.
        ScriptedParserHost host = new ScriptedParserHost();
        OutputDef stacked = PaperDef();
        stacked.SetVariable("from", "stack");
        host.Globals.Bindings[Symbol.Intern("$papers")] = new Pair(stacked, Nil.Instance);
        OutputDef fallback = PaperDef();
        fallback.SetVariable("from", "default");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = fallback;

        //Act
        OutputDef result = ParserActionHelpers.GetPaper(host);

        //Assert
        result.Should().NotBeSameAs(stacked);
        result.CVariable("from").AsText().Should().Be("stack");
        result.CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("paper"));
    }

    [Fact]
    public void get_paper_falls_back_to_the_default_paper_then_to_a_fresh_definition()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        host.Globals.Bindings[Symbol.Intern("$papers")] = Nil.Instance;
        OutputDef fallback = PaperDef();
        fallback.SetVariable("from", "default");
        host.Globals.Bindings[Symbol.Intern("$defaultpaper")] = fallback;

        //Act
        OutputDef fromDefault = ParserActionHelpers.GetPaper(host);
        OutputDef fresh = ParserActionHelpers.GetPaper(new ScriptedParserHost());

        //Assert
        fromDefault.Should().NotBeSameAs(fallback);
        fromDefault.CVariable("from").AsText().Should().Be("default");

        fresh.CVariable("from").Should().BeNull();
        fresh.CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("paper"));
    }

    [Fact]
    public void get_midi_and_get_layout_clone_their_session_defaults()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        OutputDef defaultMidi = new OutputDef();
        defaultMidi.SetVariable("from", "midi-default");
        host.Globals.Bindings[Symbol.Intern("$defaultmidi")] = defaultMidi;
        OutputDef defaultLayout = new OutputDef();
        defaultLayout.SetVariable("from", "layout-default");
        host.Globals.Bindings[Symbol.Intern("$defaultlayout")] = defaultLayout;

        //Act
        OutputDef midi = ParserActionHelpers.GetMidi(host);
        OutputDef layout = ParserActionHelpers.GetLayout(host);

        //Assert
        midi.Should().NotBeSameAs(defaultMidi);
        midi.CVariable("from").AsText().Should().Be("midi-default");
        midi.CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("midi"));

        layout.Should().NotBeSameAs(defaultLayout);
        layout.CVariable("from").AsText().Should().Be("layout-default");
        layout.CVariable("output-def-kind").Should().BeSameAs(Symbol.Intern("layout"));
    }

    [Fact]
    public void cloning_an_output_def_carries_its_input_origin()
    {
        //Arrange
        // Upstream's Output_def copy constructor does `input_origin_ =
        // s.input_origin_`, which is what lets a \paper cloned from the $papers
        // stack keep saying where its ancestor was written.
        OutputDef original = new OutputDef();
        object origin = default(SourceSpan);
        original.SetSpot(origin);

        //Act
        OutputDef clone = original.Clone();

        //Assert
        clone.InputOrigin.Should().BeSameAs(origin);
    }
}
