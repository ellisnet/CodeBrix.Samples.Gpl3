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
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 8 — music-function arglists, the non-backup half. Real text
/// exercises the whole machine: the scanner's signature announcement
/// (<c>scan_scm_id</c>) turns <c>\fun</c> into <c>MUSIC_FUNCTION</c> plus its
/// <c>EXPECT_*</c> tokens, and the arglist rules then accept, reinterpret
/// (<c>MYREPARSE</c>) or reject each argument by predicate. Bodies whose surrounding
/// grammar is not ported yet (the rhythm reparse, the fingering fallback) are
/// invoked directly.
/// </summary>
public class RuleActionRag8Tests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static readonly IReadOnlyDictionary<int, RuleAction> Bound
        = LilyPondRuleActions.Create().Bind(Tables);

    private static readonly object MusicFunction = Symbol.Intern("test-music-function");

    private static readonly object NumberPred = Symbol.Intern("number?");

    private static readonly object StringPred = Symbol.Intern("string?");

    private static readonly object MusicHandler = Symbol.Intern("the-music-handler");

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

    /// <summary>Builds a signature list: head the return predicate, then one entry
    /// per argument — the predicate itself, or a (predicate . default) pair.</summary>
    private static object Signature(params object[] arguments)
    {
        object list = Nil.Instance;
        for (int i = arguments.Length - 1; i >= 0; i--)
        {
            list = new Pair(arguments[i], list);
        }

        return new Pair(Symbol.Intern("ly:music?"), list);
    }

    /// <summary>A host whose <c>\fun</c> is a music function with the given
    /// signature, whose number?/string? predicates really discriminate, and whose
    /// toplevel-music-handler is bound so the finished call is observable.</summary>
    private static ScriptedParserHost FunctionHost(object signature)
    {
        ScriptedParserHost host = new ScriptedParserHost();
        host.Keywords["default"] = ("DEFAULT", null);
        host.Keywords["etc"] = ("ETC", null);
        host.Identifiers["fun"] = new LexerLookup("MUSIC_FUNCTION", MusicFunction, signature);
        host.Globals.Bindings[Symbol.Intern("toplevel-music-handler")] = MusicHandler;
        host.CallBehavior = (procedure, arguments) =>
        {
            if (ReferenceEquals(procedure, NumberPred))
            {
                return SchemeNumber.IsNumber(arguments[0]);
            }

            if (ReferenceEquals(procedure, StringPred))
            {
                return arguments[0] is string || arguments[0] is MutableString;
            }

            return Unspecified.Instance;
        };
        return host;
    }

    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(
        object signature, string input)
    {
        ScriptedParserHost host = FunctionHost(signature);
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
    }

    private static (ParseContext Context, ModalScanner Scanner, ScriptedParserHost Host) ScannerContext()
    {
        ScriptedParserHost host = FunctionHost(Signature(NumberPred));
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), string.Empty, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        ParseContext context = new ParseContext(
            new LalrParser(Tables, new Dictionary<int, RuleAction>()), scanner)
        {
            UserState = host,
        };
        return (context, scanner, host);
    }

    private static SyntaxMark Dispatch(ScriptedParserHost host, string name)
    {
        foreach (SyntaxMark mark in host.SyntaxDispatches)
        {
            if (string.Equals(mark.Name, name, StringComparison.Ordinal))
            {
                return mark;
            }
        }

        return null;
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

    // ------ the scanner's signature announcement (scan_scm_id) ------

    [Fact]
    public void a_music_function_announces_its_signature_as_expect_tokens()
    {
        //Arrange
        // scan_scm_id pushes EXPECT_NO_MORE_ARGS first and each argument's tokens
        // after it, so delivery runs LAST ARGUMENT FIRST and the floor arrives last —
        // with EXPECT_OPTIONAL delivered BEFORE its EXPECT_SCM, the order the
        // grammar's `EXPECT_OPTIONAL EXPECT_SCM ...` rules spell out.
        object signature = Signature(new Pair(NumberPred, 7L), StringPred);
        ScriptedParserHost host = FunctionHost(signature);
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), "\\fun", "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);

        //Act
        ParserToken function = scanner.Next();
        ParserToken first = scanner.Next();
        ParserToken second = scanner.Next();
        ParserToken third = scanner.Next();
        ParserToken floor = scanner.Next();

        //Assert
        function.Symbol.Should().Be(Sym("MUSIC_FUNCTION"));
        function.Value.Should().BeSameAs(MusicFunction);

        // The LAST argument (mandatory string) leads...
        first.Symbol.Should().Be(Sym("EXPECT_SCM"));
        first.Value.Should().BeSameAs(StringPred);

        // ...then the optional number, EXPECT_OPTIONAL carrying the DEFAULT before
        // EXPECT_SCM carrying the predicate...
        second.Symbol.Should().Be(Sym("EXPECT_OPTIONAL"));
        second.Value.Should().Be(7L);
        third.Symbol.Should().Be(Sym("EXPECT_SCM"));
        third.Value.Should().BeSameAs(NumberPred);

        // ...and the floor arrives last.
        floor.Symbol.Should().Be(Sym("EXPECT_NO_MORE_ARGS"));
    }

    // ------ whole inputs through the real scanner and tables ------

    [Fact]
    public void a_trailing_optional_number_accepts_a_number_from_real_text()
    {
        //Arrange
        // \fun 3 with signature (music? [number? = 7]): the 3 satisfies the optional
        // in nonbackup (final) position and joins the arglist.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(Signature(new Pair(NumberPred, 7L)), "\\fun 3");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark call = Dispatch(host, "music-function");
        call.Should().NotBeNull();
        call.Arguments[0].Should().BeSameAs(MusicFunction);
        Cars(call.Arguments[1]).Should().Equal(3L);

        // And the finished music went to the toplevel handler.
        host.Calls.Should().Contain(
            entry => ReferenceEquals(entry.Procedure, MusicHandler));
    }

    [Fact]
    public void a_refused_final_argument_reports_argument_error_and_marks_the_arglist()
    {
        //Arrange
        // \fun "x" against (music? [number? = 7]): nothing makes "x" a number, so the
        // reparse settles on SCM_ARG knowing the predicate is false, and
        // check_scheme_arg reports argument-error while terminating the arglist with
        // #f — uncallable, but still the right length.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(Signature(new Pair(NumberPred, 7L)), "\\fun \"x\"");

        //Act
        parser.Parse(scanner, host);

        //Assert
        SyntaxMark error = Dispatch(host, "argument-error");
        error.Should().NotBeNull();
        error.Arguments[0].Should().Be(1L);
        error.Arguments[1].Should().BeSameAs(NumberPred);
        error.Arguments[2].Should().Be("x");

        SyntaxMark call = Dispatch(host, "music-function");
        call.Should().NotBeNull();
        Pair arglist = (Pair)call.Arguments[1];
        arglist.Car.Should().Be("x");
        arglist.Cdr.Should().Be(false);
    }

    // ------ MYREPARSE, invoked over a drainable scanner ------

    [Fact]
    public void a_reparse_delivers_the_reparse_marker_before_the_reinterpreted_token()
    {
        //Arrange
        // MYREPARSE pushes the token then REPARSE, so REPARSE — carrying the
        // predicate — is read FIRST, exactly the order the grammar's
        // `..._reparse REPARSE <token>` tail spells.
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();

        //Act
        // "3" satisfies no string variant and no music reading, so the action
        // settles on SCM_ARG.
        object result = Action(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup SCM_IDENTIFIER")(
            context,
            new object[] { 7L, NumberPred, Nil.Instance, "3" },
            new SourceSpan[4],
            default);

        //Assert
        result.Should().Be(Nil.Instance);
        ParserToken reparse = scanner.Next();
        reparse.Symbol.Should().Be(Sym("REPARSE"));
        reparse.Value.Should().BeSameAs(NumberPred);
        ParserToken token = scanner.Next();
        token.Symbol.Should().Be(Sym("SCM_ARG"));
        token.Value.Should().Be("3");
    }

    [Fact]
    public void an_unsigned_the_predicate_accepts_reparses_as_real()
    {
        //Arrange
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();

        //Act
        Action(
            "function_arglist_nonbackup_reparse: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup UNSIGNED")(
            context,
            new object[] { 7L, NumberPred, Nil.Instance, 3L },
            new SourceSpan[4],
            default);

        //Assert
        scanner.Next().Symbol.Should().Be(Sym("REPARSE"));
        ParserToken token = scanner.Next();
        token.Symbol.Should().Be(Sym("REAL"));
        token.Value.Should().Be(3L);
    }

    // ------ bodies whose surrounding grammar is not ported yet, invoked directly ------

    [Fact]
    public void reparsed_rhythm_sets_the_default_duration_and_makes_the_note()
    {
        //Arrange
        // A DURATION_ARG only ever arrives via MYREPARSE. The body updates the
        // parser's default duration, makes music from it (a NoteEvent in note mode),
        // and attaches reversed articulations when there are post events.
        (ParseContext context, _, ScriptedParserHost host) = ScannerContext();
        host.IsNoteState = true;
        Duration quarter = new Duration(2, 0);
        object postEvents = Pair.List("second", "first");

        //Act
        object result = Action("reparsed_rhythm: DURATION_ARG dots multipliers post_events %prec ':'")(
            context,
            new object[] { quarter, 1L, DefaultArgument.Instance, postEvents },
            new SourceSpan[4],
            default);

        //Assert
        // One dot was added to the quarter.
        host.DefaultDuration.DurationLog.Should().Be(2);
        host.DefaultDuration.DotCount.Should().Be(1);

        MadeMusic note = (MadeMusic)result;
        note.Name.Should().Be("NoteEvent");
        note.Properties.Should().Contain(p => p.Name == "duration");
        (string _, object articulations) = note.Properties.Find(p => p.Name == "articulations");
        Cars(articulations).Should().Equal("first", "second");
    }

    [Fact]
    public void a_refused_negative_number_falls_back_to_a_fingering_event()
    {
        //Arrange
        // `- 5` against a predicate that accepts neither the number nor the event:
        // the FingeringEvent is still made (digit 5), check_scheme_arg reports with
        // the NEGATED number as the display value, and the arglist is terminated
        // with #f.
        (ParseContext context, _, ScriptedParserHost host) = ScannerContext();
        host.CallBehavior = (procedure, arguments) => false;

        //Act
        object result = Action(
            "function_arglist_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup '-' UNSIGNED")(
            context,
            new object[] { 7L, NumberPred, Nil.Instance, null, 5L },
            new SourceSpan[5],
            default);

        //Assert
        Pair arglist = (Pair)result;
        MadeMusic fingering = (MadeMusic)arglist.Car;
        fingering.Name.Should().Be("FingeringEvent");
        fingering.Properties.Should().Contain(p => p.Name == "digit" && 5L.Equals(p.Value));
        arglist.Cdr.Should().Be(false);

        SyntaxMark error = Dispatch(host, "argument-error");
        error.Should().NotBeNull();
        error.Arguments[2].Should().Be(-5L);
    }

    [Fact]
    public void an_accepted_argument_is_consed_without_an_error()
    {
        //Arrange
        (ParseContext context, _, ScriptedParserHost host) = ScannerContext();

        //Act
        object result = Action(
            "function_arglist_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup bare_number_common")(
            context,
            new object[] { 7L, NumberPred, Pair.List("earlier"), 3L },
            new SourceSpan[4],
            default);

        //Assert
        Cars(result).Should().Equal(3L, "earlier");
        host.SyntaxDispatches.Should().BeEmpty();
    }
}
