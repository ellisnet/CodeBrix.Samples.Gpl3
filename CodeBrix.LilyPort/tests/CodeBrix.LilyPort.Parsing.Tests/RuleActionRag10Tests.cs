// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
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
/// RULE ACTION GROUP 10 — music-function arglists: the common half, the skip and
/// partial plumbing, and <c>music_function_call</c> itself. Real text drives whole
/// calls — mandatory arguments, written and defaulted optionals, negative numbers —
/// and the <c>\etc</c> partial-application path that RAG11 was waiting on.
/// </summary>
public class RuleActionRag10Tests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static readonly IReadOnlyDictionary<int, RuleAction> Bound
        = LilyPondRuleActions.Create().Bind(Tables);

    private static readonly object MusicFunction = Symbol.Intern("test-music-function");

    private static readonly object NumberPred = Symbol.Intern("number?");

    private static readonly object StringPred = Symbol.Intern("string?");

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

    private static object Signature(params object[] arguments)
    {
        object list = Nil.Instance;
        for (int i = arguments.Length - 1; i >= 0; i--)
        {
            list = new Pair(arguments[i], list);
        }

        return new Pair(Symbol.Intern("ly:music?"), list);
    }

    private static ScriptedParserHost FunctionHost(object signature)
    {
        ScriptedParserHost host = new ScriptedParserHost();
        host.Keywords["default"] = ("DEFAULT", null);
        host.Keywords["etc"] = ("ETC", null);
        host.Identifiers["fun"] = new LexerLookup("MUSIC_FUNCTION", MusicFunction, signature);
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

    private static ParseContext NewContext(ScriptedParserHost host)
        => new ParseContext(
            new LalrParser(Tables, new Dictionary<int, RuleAction>()),
            new TokenListInput())
        {
            UserState = host,
        };

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

    // ------ whole inputs through the real scanner and tables ------

    [Fact]
    public void a_mandatory_number_argument_parses_from_real_text()
    {
        //Arrange
        // \fun 3 with signature (music? number?): the UNSIGNED goes through
        // function_arglist_common_reparse's ladder, reparses as REAL, and lands
        // through the REPARSE tail.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(Signature(NumberPred), "\\fun 3");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark call = Dispatch(host, "music-function");
        call.Should().NotBeNull();
        call.Arguments[0].Should().BeSameAs(MusicFunction);
        Cars(call.Arguments[1]).Should().Equal(3L);
    }

    [Fact]
    public void a_real_number_argument_takes_the_direct_check_path()
    {
        //Arrange
        // 3.5 lexes as REAL and reduces through bare_number_common — no reparse —
        // into function_arglist_common's direct check_scheme_arg alternative.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(Signature(NumberPred), "\\fun 3.5");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        Cars(Dispatch(host, "music-function").Arguments[1]).Should().Equal(3.5);
        Dispatch(host, "argument-error").Should().BeNull();
    }

    [Fact]
    public void a_negative_number_argument_negates_and_reparses_as_real()
    {
        //Arrange
        // \fun -3: the '-' UNSIGNED alternative of function_arglist_common_reparse
        // negates and reparses as REAL.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(Signature(NumberPred), "\\fun -3");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        Cars(Dispatch(host, "music-function").Arguments[1]).Should().Equal(-3L);
    }

    [Fact]
    public void two_mandatory_arguments_arrive_reversed()
    {
        //Arrange
        // \fun 3 "hi" with (music? number? string?): the arglist is built by consing
        // inner-to-outer, so it arrives last-argument-first — the shape the vendored
        // music-function constructor expects.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(Signature(NumberPred, StringPred), "\\fun 3 \"hi\"");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        Cars(Dispatch(host, "music-function").Arguments[1]).AsText().Should().Equal("hi", 3L);
    }

    [Fact]
    public void a_written_default_takes_the_optional_place()
    {
        //Arrange
        // \fun \default "hi" with (music? [number? = 7] string?): the DEFAULT token
        // routes through function_arglist_optional's DEFAULT alternative, and the
        // optional's default value joins the arglist.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(
                Signature(new Pair(NumberPred, 7L), StringPred),
                "\\fun \\default \"hi\"");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        Cars(Dispatch(host, "music-function").Arguments[1]).AsText().Should().Equal("hi", 7L);
    }

    [Fact]
    public void a_trailing_default_routes_through_the_nonbackup_skip()
    {
        //Arrange
        // \fun \default with (music? [number? = 7]): the FINAL optional's DEFAULT
        // goes through function_arglist's own DEFAULT alternative over
        // function_arglist_skip_nonbackup.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(Signature(new Pair(NumberPred, 7L)), "\\fun \\default");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        Cars(Dispatch(host, "music-function").Arguments[1]).Should().Equal(7L);
    }

    [Fact]
    public void an_etc_call_returns_the_partial_arglist_for_later_completion()
    {
        //Arrange
        // fun = \fun 3 \etc with (music? number? string?): the written 3 is skimmed
        // into the partial arglist, ETC stops the reading, and RAG11's
        // partial_function machinery hands RAG1's assignment a partial function —
        // the flow that could not run from real text before this group landed.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(Signature(NumberPred, StringPred), "foo = \\fun 3 \\etc");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Globals.Bindings.Should().ContainKey(Symbol.Intern("foo"));
    }

    // ------ direct invocations ------

    [Fact]
    public void the_arglist_floor_is_the_empty_list()
    {
        //Arrange
        ScriptedParserHost host = FunctionHost(Signature(NumberPred));

        //Act
        object result = Action("function_arglist_common: EXPECT_NO_MORE_ARGS")(
            NewContext(host), new object[] { null }, new SourceSpan[1], default);

        //Assert
        result.Should().Be(Nil.Instance);
    }

    [Fact]
    public void a_skipped_optional_in_nonbackup_position_conses_its_default()
    {
        //Arrange
        ScriptedParserHost host = FunctionHost(Signature(NumberPred));

        //Act
        object result = Action(
            "function_arglist_skip_nonbackup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_skip_nonbackup")(
            NewContext(host),
            new object[] { 7L, NumberPred, Pair.List("inner") },
            new SourceSpan[3],
            default);

        //Assert
        Cars(result).AsText().Should().Equal(7L, "inner");
    }

    [Fact]
    public void a_partial_arglist_passes_through_unchanged()
    {
        //Arrange
        ScriptedParserHost host = FunctionHost(Signature(NumberPred));
        object arglist = Pair.List("kept");

        //Act
        object viaOptional = Action(
            "function_arglist_partial: EXPECT_SCM function_arglist_optional")(
            NewContext(host), new object[] { NumberPred, arglist }, new SourceSpan[2], default);
        object viaNonbackup = Action(
            "function_arglist_partial: EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup")(
            NewContext(host),
            new object[] { 7L, NumberPred, arglist },
            new SourceSpan[3],
            default);

        //Assert
        viaOptional.Should().BeSameAs(arglist);
        viaNonbackup.Should().BeSameAs(arglist);
    }

    [Fact]
    public void the_music_function_call_dispatches_with_the_function_and_its_arglist()
    {
        //Arrange
        ScriptedParserHost host = FunctionHost(Signature(NumberPred));
        object arglist = Pair.List(3L);

        //Act
        object result = Action("music_function_call: MUSIC_FUNCTION function_arglist")(
            NewContext(host),
            new object[] { MusicFunction, arglist },
            new SourceSpan[2],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("music-function");
        mark.Arguments[0].Should().BeSameAs(MusicFunction);
        mark.Arguments[1].Should().BeSameAs(arglist);
    }
}
