// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

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
/// <c>parse-scm.cc</c> end to end: <c>#</c>, <c>$</c>, <c>#@</c> and the two
/// <c>scm_c_catch</c> handlers, driven through a live Scheme layer.
/// <para>
/// The catch is the load-bearing one. Without it a single unreadable <c>#(...)</c>
/// anywhere in a file takes the whole run down with a CLR exception, so the demand loop —
/// which works by running a file and reading what it complains about — gets one complaint
/// per run and no location. With it, every failure names a file and a line and the parse
/// keeps going, which is what makes a file's SECOND error visible at all.
/// </para>
/// </summary>
[Collection("LilyPondScheme")]
public class EmbeddedSchemeTests
{
    private static readonly object Gate = new object();
    private static Interpreter _shared;

    private static LilyParserSession FreshSession()
    {
        lock (Gate)
        {
            if (_shared == null)
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                LilyPondScheme.LoadViaLilyScm(interpreter);
                new LilyParserSession(interpreter).LoadInitLayer();
                _shared = interpreter;
            }
        }

        return new LilyParserSession(_shared);
    }

    // ------ the catch ------

    [Fact]
    public void an_unreadable_embedded_expression_is_a_located_error_rather_than_an_abort()
    {
        //Arrange
        // An unterminated list: the reader runs off the end of the input. Upstream's
        // pre-unwind handler reports at the START of the expression, not where the reader
        // gave up, because where it gave up is meaningless to whoever wrote the file.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText("broken = #(list 1 2", "bad.ly");

        //Assert
        outcome.ErrorCount.Should().BeGreaterThan(0);
        string joined = string.Join(" || ", outcome.AllDiagnostics());
        joined.Should().Contain("bad.ly:");
    }

    [Fact]
    public void a_bad_expression_does_not_stop_the_rest_of_the_file_being_read()
    {
        //Arrange
        // This is the property the demand loop actually needs. Before the catch, the
        // first bad expression threw and everything after it was invisible.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "first = #(list 1 2\nafterwards = #42", "two.ly");

        //Assert
        session.LookupIdentifier("afterwards").Should().Be(42L);
    }

    [Fact]
    public void an_expression_that_raises_while_being_evaluated_is_also_located()
    {
        //Arrange
        // The OTHER catch: the reader is happy, the evaluator is not.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "boom = #(no-such-procedure-at-all 1)\nlater = #7", "raise.ly");

        //Assert
        outcome.ErrorCount.Should().BeGreaterThan(0);
        string.Join(" || ", outcome.AllDiagnostics()).Should().Contain("raise.ly:");
        session.LookupIdentifier("later").Should().Be(7L);
    }

    [Fact]
    public void a_bad_embedded_expression_raises_the_error_level()
    {
        //Arrange
        // lexer.ll 412: `if (SCM_UNBNDP (sval)) error_level_ = 1;`. The error level is
        // what tells the toplevel handlers to discard the music rather than engrave it.
        LilyParserSession session = FreshSession();

        //Act
        session.ParseText("broken = #(list 1 2", "level.ly");

        //Assert
        session.ErrorLevel.Should().Be(1);
    }

    // ------ $ : immediate Scheme ------

    [Fact]
    public void immediate_scheme_lexes_as_the_token_its_value_calls_for()
    {
        //Arrange
        // `$' is evaluated BY THE LEXER and the token comes from the value's type, which
        // is the whole difference from `#'. A number has to arrive as NUMBER_IDENTIFIER,
        // because that is the terminal the grammar accepts where a number is legal.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "#(define seven 7)\nindented = $seven", "immediate.ly");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        session.LookupIdentifier("indented").Should().Be(7L);
    }

    [Fact]
    public void immediate_scheme_delivers_music_as_a_music_identifier()
    {
        //Arrange
        // The type-directed choice again, on the case that matters most: a music value
        // has to lex as MUSIC_IDENTIFIER so that `$mus' is usable anywhere `\mus' is.
        LilyParserSession session = FreshSession();
        session.ParseText("source = { c4 }", "immediate.ly");

        //Act
        ParseOutcome outcome = session.ParseText(
            "#(define fromScheme (ly:parser-lookup 'source))\ncopy = { $fromScheme }",
            "immediate.ly");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        session.LookupIdentifier("copy").Should().BeOfType<MusicObject>();
    }

    [Fact]
    public void an_immediate_expression_that_fails_reports_the_scheme_error_once()
    {
        //Arrange
        // lexer.ll 442: the rule returns a token only `if (!scm_is_eq (yylval,
        // SCM_UNSPECIFIED))`, and otherwise falls off the end of the action — which in
        // flex means the text is consumed and scanning continues WITHOUT a token. What
        // that buys is the absence of a second, invented diagnostic: the Scheme failure
        // is reported where it happened, and the grammar is not handed a placeholder
        // token that would fail again somewhere downstream and blame the wrong line.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText("bad = $no-such-variable-anywhere", "novalue.ly");

        //Assert
        List<string> scheme = new List<string>();
        foreach (string message in outcome.AllDiagnostics())
        {
            if (message.Contains("no-such-variable-anywhere"))
            {
                scheme.Add(message);
            }
        }

        scheme.Should().ContainSingle();
        scheme[0].Should().Contain("novalue.ly:");
        session.ErrorLevel.Should().Be(1);
    }

    // ------ #@ / $@ : the multiple-values prefix ------

    [Fact]
    public void the_multiple_values_prefix_wraps_the_form_in_apply_values()
    {
        //Arrange
        // internal_parse_embedded_scheme: `if (multiple) form = ly_list (apply, values,
        // form)`. Checked at the reader, because that is where the '@' is consumed and
        // the wrapping is the only trace it leaves.
        LilyParserSession session = FreshSession();
        session.ParseText(string.Empty, "values.ly");

        //Act
        object form = ((Lexing.ILexerHost)session).ParseEmbeddedScheme(
            "@(values 1 2)", 0, new Driver.SourceSpan("values.ly", 1, 1, 1, 1, 0, 0),
            out int consumed);

        //Assert
        consumed.Should().Be("@(values 1 2)".Length);
        Pair wrapped = form.Should().BeOfType<Pair>().Subject;
        wrapped.Car.Should().Be(Symbol.Intern("apply"));
        ((Pair)wrapped.Cdr).Car.Should().Be(Symbol.Intern("values"));
    }

    [Fact]
    public void extra_values_are_delivered_as_further_tokens()
    {
        //Arrange
        // eval_scm's second half. A form yielding three values delivers the FIRST as its
        // own token and pushes the other two back, so `#@(values a b c)' fills three
        // argument slots of the construct it sits in.
        LilyParserSession session = FreshSession();
        ParseOutcome outcome = session.ParseText(
            "spread = \\markup \\concat { #@(values \"a\" \"b\") }", "spread.ly");

        //Assert
        outcome.ErrorCount.Should().Be(0);
        outcome.LexerErrors.Should().BeEmpty();
        session.LookupIdentifier("spread").Should().NotBeNull();
    }

    // ------ the closures lookup ------

    [Fact]
    public void an_embedded_block_evaluates_its_scheme_in_the_enclosing_lexical_scope()
    {
        //Arrange
        // The reason the closures alist exists. Inside `#{ ... #}` the `$p` is NOT read
        // back from the reconstructed text — it is looked up by the byte offset the
        // reader recorded and the stored THUNK is called, so it sees `p`, a let-bound
        // Scheme variable that no parser scope has ever heard of. Read from the text
        // instead, `p` is simply unbound.
        //
        // Written `$p` and not `$p4`: the Scheme reader takes the longest symbol it can,
        // and `p4` is a perfectly good one — upstream's reader does the same, so a
        // duration has to be separated from the identifier it follows.
        LilyParserSession session = FreshSession();

        //Act
        ParseOutcome outcome = session.ParseText(
            "#(define lexical (let ((p (ly:make-pitch 0 1 0))) #{ $p #}))",
            "closure.ly");

        //Assert
        outcome.ErrorCount.Should().Be(0,
            "closure.ly reported: " + string.Join(" || ", outcome.AllDiagnostics()));
        session.LookupIdentifier("lexical").Should().BeOfType<Pitch>();
    }
}
