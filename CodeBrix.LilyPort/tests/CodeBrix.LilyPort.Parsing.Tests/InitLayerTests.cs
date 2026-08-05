// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Parsing.Session;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The <c>ly/</c> initialisation layer, driven end to end on a live Scheme layer.
/// <para>
/// This is the fence EPG1 exists for. Everything before it proved the grammar, the rule
/// actions and the scanner against a SCRIPTED host — which answers from tables, so nothing
/// it does can disagree with the Scheme layer. Driving <c>declarations-init.ly</c> for real
/// is what turned up six divergences that had been recorded as invisible, every one of them
/// in the "registered, but not behaving" class: output-definition scopes that were not
/// modules, identifier lookup that ignored imports, markup commands looked up without their
/// <c>-markup</c> suffix, <c>EXPECT_SCM</c> carrying a name instead of a predicate,
/// <c>MAKE_SYNTAX</c> passing the location as an argument instead of binding
/// <c>(*location*)</c>, and CLR strings standing in for Scheme strings.
/// </para>
/// <para>
/// The interpreter is process-global (plan risk 7), so these serialise with the other load
/// fences on the "LilyPondScheme" collection.
/// </para>
/// </summary>
[Collection("LilyPondScheme")]
public class InitLayerTests
{
    private static readonly object Gate = new object();
    private static Interpreter _shared;
    private static ParseOutcome _initOutcome;
    private static LilyParserSession _initialised;

    /// <summary>
    /// Builds one interpreter, loads the <c>scm/</c> layer, and runs the <c>ly/</c> init
    /// layer through it exactly once for the whole class. The init layer is the expensive
    /// part and every test here wants the same post-initialisation state.
    /// </summary>
    /// <returns>The initialised session.</returns>
    private static LilyParserSession Initialised()
    {
        lock (Gate)
        {
            if (_initialised == null)
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                LilyPondScheme.LoadViaLilyScm(interpreter);
                LilyParserSession session = new LilyParserSession(interpreter);
                _initOutcome = session.LoadInitLayer();
                _shared = interpreter;
                _initialised = session;
            }

            return _initialised;
        }
    }

    private static LilyParserSession FreshSession()
    {
        Initialised();
        return new LilyParserSession(_shared);
    }

    // ------ the headline: the layer RUNS ------

    [Fact]
    public void the_ly_init_layer_runs_to_completion()
    {
        //Arrange / Act
        Initialised();

        //Assert
        // "To completion" is the claim, and it is deliberately weaker than "cleanly":
        // the layer still reports parse errors in a handful of files, and those are
        // EPG1's remaining worklist rather than a regression. What this pins is that
        // nothing ABORTS it — before this session it threw at declarations-init.ly line
        // 31, the first \include, and everything past that was invisible.
        _initOutcome.Should().NotBeNull();
        _initOutcome.LexerErrors.Should().BeEmpty();
    }

    [Fact]
    public void the_note_name_table_is_real_after_initialisation()
    {
        //Arrange
        // declarations-init.ly calls (note-names-language default-language) at TOP LEVEL,
        // outside any note mode. ly:parser-set-note-names has to store the table anyway —
        // upstream's assignment is to Lily::pitchnames, a handle onto (lily)'s own
        // variable, and the pop/push around it only makes an already-open note mode pick
        // the new table up.
        LilyParserSession session = Initialised();

        //Act
        object names = session.LilyModule.Lookup(Symbol.Intern("pitchnames"))?.GetValue();

        //Assert
        names.Should().BeOfType<Pair>();
        session.LilyModule.Lookup(Symbol.Intern("input-language")).GetValue()
            .Should().Be(Symbol.Intern("nederlands"));
    }

    [Fact]
    public void note_names_reach_the_scanner_so_notemode_parses_a_note()
    {
        //Arrange
        // The end-to-end version of the test above: the table is only real if
        // push_note_state hands it to the scanner and a bare `c` lexes as a pitch.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText("foo = \\notemode { c }", "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        outcome.LexerErrors.Should().BeEmpty();
    }

    // ------ output definitions ------

    [Fact]
    public void an_assignment_inside_a_paper_block_lands_in_the_definition()
    {
        //Arrange
        // output_def_head pushes the definition's OWN module onto the lexer's scope
        // stack, so an assignment written in the block is a variable of the definition.
        // While the scope was a dictionary there was nothing to push and the value landed
        // in the enclosing file instead — silently.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText("\\paper { indent = #7 }", "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        OutputDef paper = session.LookupIdentifier("$defaultpaper") as OutputDef;
        paper.Should().NotBeNull();
        paper.CVariable("indent").Should().Be(7L);
    }

    [Fact]
    public void an_output_definition_leaves_the_scope_stack_as_it_found_it()
    {
        //Arrange
        // One add_scope in output_def_head, one remove_scope at the closing brace. An
        // earlier pass pushed onto a private list and popped from the module stack, so a
        // session emptied its scopes on the first \paper it read and the NEXT assignment
        // indexed past the end of the list.
        LilyParserSession session = FreshSession();

        //Act
        session.ParseText("\\paper { }\n\\layout { }\n\\paper { }", "<test>");
        ParseOutcome after = session.ParseText("afterBlocks = #42", "<test>");

        //Assert
        after.ErrorCount.Should().Be(0);
        session.LookupIdentifier("afterBlocks").Should().Be(42L);
    }

    [Fact]
    public void a_cloned_output_definition_does_not_write_through_to_its_original()
    {
        //Arrange
        // Upstream's copy constructor is `scope_ = ly_make_module (); ly_module_copy (...)`
        // — a FRESH module copied into, never a shared one.
        OutputDef original = new OutputDef();
        original.SetVariable("indent", 3L);

        //Act
        OutputDef clone = original.Clone();
        clone.SetVariable("indent", 9L);

        //Assert
        original.CVariable("indent").Should().Be(3L);
        clone.CVariable("indent").Should().Be(9L);
    }

    // ------ locations ------

    [Fact]
    public void a_location_is_a_real_input_that_can_name_its_file_and_line()
    {
        //Arrange
        LilyParserSession session = FreshSession();
        session.ParseText("foo = #1\nbar = #2", "origins.ly");

        //Act
        Input origin = session.SchemeLocation(
            new Driver.SourceSpan("origins.ly", 2, 1, 2, 4, 9, 12));

        //Assert
        // ly:input-location? has to answer on it, and it has to know where it points —
        // a boxed span of the port's own could do neither.
        origin.FileString().Should().Be("origins.ly");
        origin.LineNumber().Should().Be(2);
    }

    [Fact]
    public void a_span_with_no_offsets_reports_position_unknown_rather_than_guessing()
    {
        //Arrange
        LilyParserSession session = FreshSession();

        //Act
        Input origin = session.SchemeLocation(new Driver.SourceSpan("nowhere.ly", 1, 1, 1, 2));

        //Assert
        origin.GetSourceFile().Should().BeNull();
        origin.LocationString().Should().Contain("position unknown");
    }

    [Fact]
    public void make_syntax_binds_the_location_fluid_instead_of_passing_it()
    {
        //Arrange
        // MAKE_SYNTAX -> make_syntax -> with_location, which binds %location for the
        // dynamic extent of the call. Not one constructor in ly-syntax-constructors.scm
        // declares a location parameter.
        LilyParserSession session = FreshSession();
        List<object> seen = new List<object>();
        Interpreter interpreter = session.Interpreter;
        interpreter.DefinePrimitive("lilyport-record-location", 0, 0, a =>
        {
            seen.Add(MusicFunctionSupport.CurrentLocation());
            return Unspecified.Instance;
        });

        //Act
        object answer = session.WithLocation(
            new Driver.SourceSpan("fluid.ly", 1, 1, 1, 2, 0, 1),
            () =>
            {
                seen.Add(MusicFunctionSupport.CurrentLocation());
                return true;
            });

        //Assert
        answer.Should().Be(true);
        seen.Should().ContainSingle();
        seen[0].Should().BeOfType<Input>();

        // ...and it is restored afterwards, so a constructor cannot leak its location
        // into whatever runs next.
        MusicFunctionSupport.CurrentLocation().Should().Be(false);
    }

    // ------ the value representations the Scheme layer recognises ------

    [Fact]
    public void a_lexed_string_is_a_SCHEME_string_that_markup_accepts()
    {
        //Arrange
        // The Scheme layer's string? is `value is MutableString`. A CLR string is not a
        // Scheme string anywhere in it, so a lexer that produced one made every markup
        // built from a word or a quoted string fail markup?.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "titleMarkup = \\markup { \\italic \"cresc.\" }", "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        outcome.LexerErrors.Should().BeEmpty();
        session.LookupIdentifier("titleMarkup").Should().BeOfType<Pair>();
    }

    [Fact]
    public void a_markup_command_resolves_through_its_markup_suffix()
    {
        //Arrange
        // lookup-markup-command looks up `<word>-markup`; define-markup-command binds
        // `hspace-markup`, never `hspace`.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText("spacer = \\markup \\hspace #4", "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        outcome.LexerErrors.Should().BeEmpty();
    }

    // ------ the ly:parser-* surface ------

    [Fact]
    public void the_parser_bindings_answer_from_the_current_parser()
    {
        //Arrange
        // Every one of these starts, upstream, with scm_fluid_ref (Lily::f_parser). The
        // port publishes the session into the same %parser fluid for the duration of a
        // parse, so a binding called from an embedded #(...) finds it.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "#(ly:parser-define! 'fromScheme 11)\n"
            + "#(define seen (ly:parser-lookup 'fromScheme))\n"
            + "#(define missing (ly:parser-lookup 'noSuchName #:default 'fallback))\n"
            + "#(define clean (ly:parser-has-error?))",
            "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        session.LookupIdentifier("fromScheme").Should().Be(11L);
        session.LookupIdentifier("seen").Should().Be(11L);
        session.LookupIdentifier("missing").Should().Be(Symbol.Intern("fallback"));
        session.LookupIdentifier("clean").Should().Be(false);
    }

    [Fact]
    public void ly_parser_error_raises_the_error_level_and_has_error_reports_it()
    {
        //Arrange
        LilyParserSession session = FreshSession();

        //Act
        session.ParseText("#(ly:parser-error \"deliberate\")", "<test>");

        //Assert
        session.ErrorLevel.Should().Be(1);

        //Act — and clearing it puts both levels back
        session.ParseText("#(ly:parser-clear-error)", "<test>");

        //Assert
        session.ErrorLevel.Should().Be(0);
        session.LexerErrorLevel.Should().Be(0);
    }

    [Fact]
    public void ly_source_files_reports_every_file_the_parse_opened()
    {
        //Arrange
        // sources.cc's binding, which needs the parser's opened-file list — which is why
        // it lands with lily-parser-scheme.cc rather than with the Sources type.
        LilyParserSession session = FreshSession();

        //Act
        session.ParseText(
            "#(define files (ly:source-files))\n\\include \"toc-init.ly\"", "outer.ly");

        //Assert
        session.SourceFiles.Should().NotBeEmpty();
        List<string> names = new List<string>();
        foreach (SourceFile file in session.SourceFiles)
        {
            names.Add(file.Name);
        }

        names.Should().Contain("outer.ly");
        names.Should().Contain("toc-init.ly");
    }

    // ------ music functions ------

    [Fact]
    public void a_music_function_can_be_applied_like_a_procedure()
    {
        //Arrange
        // music-function.hh declares LY_DECLARE_SMOB_PROC, so a Music_function is
        // applicable. property-set and its neighbours are define-syntax-function results,
        // so every MAKE_SYNTAX for them applies one directly.
        Initialised();
        MusicFunction function = new MusicFunction(
            Pair.List(_shared.GuileModule.Lookup(Symbol.Intern("number?")).GetValue()),
            _shared.GuileModule.Lookup(Symbol.Intern("+")).GetValue());

        //Act
        object answer = _shared.Evaluator.Apply(function, new object[0]);

        //Assert
        // No arguments, so the signature's argument list is empty and (+) is 0.
        answer.Should().Be(0L);
    }

    [Fact]
    public void a_set_inside_music_reaches_its_syntax_function()
    {
        //Arrange
        // The end-to-end version: \set goes through MAKE_SYNTAX (property_set, ...), and
        // property-set is a music function, not a plain procedure.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "spanText = { \\set crescendoSpanner = #'text }", "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        outcome.LexerErrors.Should().BeEmpty();
        session.LookupIdentifier("spanText").Should().BeOfType<MusicObject>();
    }

    [Fact]
    public void ly_set_origin_returns_the_music_it_was_given()
    {
        //Arrange
        // Every syntax constructor ends in one of these. While it was a stub, each
        // constructor's RESULT came back as an inert placeholder and the next rule action
        // failed casting it.
        Initialised();
        MusicObject music = new MusicObject(Nil.Instance);
        object binding = _shared.GuileModule.Lookup(Symbol.Intern("ly:set-origin!")).GetValue();

        //Act
        object answer = _shared.Evaluator.Apply(binding, new object[] { music });

        //Assert
        answer.Should().BeSameAs(music);
    }
}
