// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Parsing.Session;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The REAL parser host, running real <c>.ly</c> text on a live Scheme layer.
/// <para>
/// This is Phase 3's first fence. Everything before it drove the grammar from a
/// scripted host, which is honest but cannot answer from the vendored Scheme; these
/// tests build the interpreter, load the <c>scm/</c> layer, and then parse LilyPond
/// SOURCE — which is what a regression input is.
/// </para>
/// <para>
/// The interpreter is process-global (plan risk 7), so these tests serialise on the
/// same lock the load fences use, and one interpreter is shared across the class.
/// </para>
/// </summary>
[Collection("LilyPondScheme")]
public class LilyParserSessionTests
{
    private static readonly object Gate = new object();
    private static Interpreter _shared;

    private static Interpreter Shared()
    {
        lock (Gate)
        {
            if (_shared == null)
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                LilyPondScheme.LoadViaLilyScm(interpreter);
                _shared = interpreter;
            }

            return _shared;
        }
    }

    private static LilyParserSession NewSession() => new LilyParserSession(Shared());

    // ------ the keyword table ------

    [Fact]
    public void the_keyword_table_carries_upstreams_forty_five_words()
    {
        //Arrange / Act / Assert
        // lily/lily-lexer.cc's the_key_tab, ported as data. The count is asserted so a
        // re-sync that adds or removes a keyword is visible here rather than as a
        // mysterious syntax error in one construct.
        LilyKeywords.Count.Should().Be(45);
        LilyKeywords.Lookup("markup").Should().Be("MARKUP");
        LilyKeywords.Lookup("new").Should().Be("NEWCONTEXT");
        LilyKeywords.Lookup("notaword").Should().BeNull();
    }

    // ------ assignments, which is what the init layer is made of ------

    [Fact]
    public void an_assignment_of_embedded_scheme_binds_the_evaluated_value()
    {
        //Arrange
        LilyParserSession session = NewSession();

        //Act
        ParseOutcome outcome = session.ParseText("foo = #(+ 1 2)", "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        session.LookupIdentifier("foo").Should().Be(3L);
    }

    [Fact]
    public void embedded_scheme_is_evaluated_THROUGH_THE_EXPANDER()
    {
        //Arrange
        // Almost everything a .ly file embeds is a MACRO USE —
        // define-music-function and define-markup-command are the whole of
        // ly/music-functions-init.ly. Evaluated without expansion they read as
        // procedure calls and die on an unbound variable named after their first
        // parameter, which is what the first run of the init layer did, once per
        // definition. A macro that expands to a plain value is enough to pin it.
        LilyParserSession session = NewSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "#(define-syntax-rule (twice x) (+ x x))\nfoo = #(twice 21)", "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        session.Diagnostics.Should().BeEmpty();
        session.LookupIdentifier("foo").Should().Be(42L);
    }

    [Fact]
    public void a_music_function_definition_expands_rather_than_being_applied()
    {
        //Arrange
        // The specific shape the whole init layer is built from, and the one that
        // exposed the expander's named-module requirement: a music function defined in
        // a parser scope must EXPAND. If the scope module cannot be named, the macro
        // reads as a variable and `(music)` is evaluated as a call.
        LilyParserSession session = NewSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "myFunc = #(define-music-function (music) (ly:music?) music)", "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        session.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void a_header_block_opens_a_scope_and_closes_it_again()
    {
        //Arrange
        LilyParserSession session = NewSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "\\header { title = \"Adagio\" }", "<test>");

        //Assert
        outcome.ErrorCount.Should().Be(0);

        object header = session.LookupIdentifier("$defaultheader");
        SchemeModule module = header as SchemeModule;
        module.Should().NotBeNull();
        module.LookupLocal(Symbol.Intern("title")).GetValue().Should().Be("Adagio");
    }

    // ------ the lexer's mode stack, driven by the real host ------

    [Fact]
    public void note_mode_is_entered_and_left_and_note_names_come_from_the_init_layer()
    {
        //Arrange
        // What this pins is the SEAM, and one honest limitation. \notemode really
        // switches the scanner — the mode stack is driven by the rule action through
        // the real host — but the note-name table it consults is EMPTY until the ly/
        // init layer runs `(note-names-language default-language)`. So `c` is reported
        // as not a note name, and that is correct for a session that has not been
        // initialised: note names are init-layer data, not built in.
        LilyParserSession session = NewSession();

        //Act
        ParseOutcome outcome = session.ParseText("foo = \\notemode { c }", "<test>");

        //Assert
        string.Join("; ", outcome.AllDiagnostics()).Should().Contain("not a note name");
    }

    // ------ \include, which the init layer is threaded on ------

    [Fact]
    public void an_include_switches_input_and_comes_back()
    {
        //Arrange
        // The scanner's include stack — upstream's Includable_lexer. The included
        // file is one of the vendored ly/ ones, resolved by name rather than by path.
        LilyParserSession session = NewSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "beforeInclude = #1\n\\include \"chord-modifiers-init.ly\"\nafterInclude = #2",
            "<test>");

        //Assert
        // Both assignments AROUND the include took effect, which is what proves the
        // input was switched and then restored. The included file's own content is
        // NOT asserted clean: chord-modifiers-init.ly needs the rest of the init layer
        // (this session has none), so it reports plenty — and the parse continuing
        // through it to `afterInclude` is precisely the error recovery working.
        session.LookupIdentifier("beforeInclude").Should().Be(1L);
        session.LookupIdentifier("afterInclude").Should().Be(2L);
        outcome.Should().NotBeNull();
    }

    [Fact]
    public void an_include_of_a_file_that_does_not_exist_is_reported()
    {
        //Arrange
        LilyParserSession session = NewSession();

        //Act
        ParseOutcome outcome = session.ParseText("\\include \"no-such-file.ly\"", "<test>");

        //Assert
        // Loud, not silent: a skipped include produces a file that parses and means
        // something else. The message is the SCANNER's, so it arrives in the outcome's
        // lexer errors rather than in the session's own diagnostics.
        string all = string.Join("; ", outcome.LexerErrors);
        all.Should().Contain("no-such-file");
    }

    private static bool AnyDiagnosticMentions(LilyParserSession session, string text)
    {
        foreach (string diagnostic in session.Diagnostics)
        {
            if (diagnostic.IndexOf(text, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // ------ the ly/ init layer itself ------

    [Fact]
    public void the_ly_init_layer_is_vendored_whole()
    {
        //Arrange / Act
        List<string> names = new List<string>(LilyPondScheme.InitFileNames());

        //Assert
        // 62 files, byte-identical with lily/ly at the v2.27.2 pin. They are read by
        // the PARSER, which is why they could only arrive once Track P was finished.
        names.Should().HaveCount(62);
        names.Should().Contain("declarations-init.ly");
        names.Should().Contain("engraver-init.ly");
        names.Should().Contain("music-functions-init.ly");
        LilyPondScheme.ReadInitFile("declarations-init.ly").Should().NotBeNull();
    }

    [Fact]
    public void a_lily_submodule_autoloads_from_the_vendored_mirror()
    {
        //Arrange
        // (lily ly-syntax-constructors) is not in the startup load order — upstream
        // reaches it lazily too — so the syntax constructors every MAKE_SYNTAX action
        // needs arrive by autoload or not at all.
        Interpreter interpreter = Shared();

        //Act
        SchemeModule module = interpreter.Modules.Resolve(
            Pair.List(Symbol.Intern("lily"), Symbol.Intern("ly-syntax-constructors")));

        //Assert
        module.LookupLocal(Symbol.Intern("lyric-event")).Should().NotBeNull();
        module.LookupLocal(Symbol.Intern("composed-markup-list")).Should().NotBeNull();
    }
}
