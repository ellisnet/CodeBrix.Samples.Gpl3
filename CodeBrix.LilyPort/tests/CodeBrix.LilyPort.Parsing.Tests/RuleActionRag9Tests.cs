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
/// RULE ACTION GROUP 9 — music-function arglists, the backup half. A refused token
/// is not an error here: the open optional is skipped (its default joins the
/// arglist, location-stamped) and the token is pushed back behind a synthetic
/// <c>BACKUP</c> for the next argument position to try. Real text drives the skip
/// and reparse choreography end to end; the token-order details are pinned over a
/// drainable scanner.
/// </summary>
public class RuleActionRag9Tests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static readonly IReadOnlyDictionary<int, RuleAction> Bound
        = LilyPondRuleActions.Create().Bind(Tables);

    private static readonly object MusicFunction = Symbol.Intern("test-music-function");

    private static readonly object NumberPred = Symbol.Intern("number?");

    private static readonly object StringPred = Symbol.Intern("string?");

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

    /// <summary>The two-argument test function: an optional number (default 7), then
    /// a mandatory string.</summary>
    private static object OptionalNumberThenString()
        => Pair.List(Symbol.Intern("ly:music?"), new Pair(NumberPred, 7L), StringPred);

    private static ScriptedParserHost FunctionHost(object signature)
    {
        ScriptedParserHost host = new ScriptedParserHost();
        host.Keywords["default"] = ("DEFAULT", null);
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

    private static (ParseContext Context, ModalScanner Scanner, ScriptedParserHost Host) ScannerContext()
    {
        ScriptedParserHost host = FunctionHost(OptionalNumberThenString());
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

    // ------ whole inputs through the real scanner and tables ------

    [Fact]
    public void a_skipped_optional_takes_its_default_and_the_string_lands()
    {
        //Arrange
        // \fun "hi" against (music? [number? = 7] string?): the "hi" is refused by
        // number?, so the optional is skipped — MYBACKUP pushes BACKUP and the
        // string back, the default 7 joins the arglist, and the string is then
        // accepted by the outer (mandatory) position. BACKUP never comes from raw
        // text, so a clean finish IS the choreography assertion.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(OptionalNumberThenString(), "\\fun \"hi\"");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark call = Dispatch(host, "music-function");
        call.Should().NotBeNull();

        // Reversed arglist: last argument first, then the skipped optional's default.
        Cars(call.Arguments[1]).Should().Equal("hi", 7L);
        Dispatch(host, "argument-error").Should().BeNull();
    }

    [Fact]
    public void a_written_optional_is_accepted_by_reparse_and_the_string_follows()
    {
        //Arrange
        // \fun 3 "hi": the 3 satisfies number? and reparses as REAL through the
        // backup rules' own REPARSE tail.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup(OptionalNumberThenString(), "\\fun 3 \"hi\"");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        SyntaxMark call = Dispatch(host, "music-function");
        call.Should().NotBeNull();
        Cars(call.Arguments[1]).Should().Equal("hi", 3L);
    }

    // ------ token choreography, invoked over a drainable scanner ------

    [Fact]
    public void a_refused_scheme_argument_backs_up_behind_a_backup_marker()
    {
        //Arrange
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        host.CallBehavior = (procedure, arguments) => false;
        object refused = Pair.List(Symbol.Intern("x"));

        //Act
        object result = Action(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup embedded_scm_arg")(
            context,
            new object[] { 7L, NumberPred, Nil.Instance, refused },
            new SourceSpan[4],
            default);

        //Assert
        // The default joined the arglist (LocOnCopy hands scripted values through)...
        Cars(result).Should().Equal(7L);

        // ...and BACKUP leads the pushed-back token, exactly MYBACKUP's push order.
        ParserToken backup = scanner.Next();
        backup.Symbol.Should().Be(Sym("BACKUP"));
        ParserToken token = scanner.Next();
        token.Symbol.Should().Be(Sym("SCM_ARG"));
        token.Value.Should().BeSameAs(refused);
    }

    [Fact]
    public void a_refused_negative_number_restores_the_minus_in_front_of_the_backup()
    {
        //Arrange
        // The `- UNSIGNED` body pushes '-' AFTER the MYBACKUP, and the pushback
        // queue is LIFO — so the delivered order is '-', BACKUP, UNSIGNED, exactly
        // as upstream's push_extra_token sequence produces.
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        host.CallBehavior = (procedure, arguments) => false;

        //Act
        object result = Action(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup '-' UNSIGNED")(
            context,
            new object[] { 7L, NumberPred, Nil.Instance, null, 5L },
            new SourceSpan[5],
            default);

        //Assert
        Cars(result).Should().Equal(7L);
        scanner.Next().Symbol.Should().Be(Sym("'-'"));
        scanner.Next().Symbol.Should().Be(Sym("BACKUP"));
        ParserToken number = scanner.Next();
        number.Symbol.Should().Be(Sym("UNSIGNED"));
        number.Value.Should().Be(5L);
    }

    [Fact]
    public void a_refused_negative_real_backs_up_the_negated_value()
    {
        //Arrange
        // Upstream backs up n — the NEGATED number — not the written $5.
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        host.CallBehavior = (procedure, arguments) => false;

        //Act
        Action(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup '-' REAL")(
            context,
            new object[] { 7L, NumberPred, Nil.Instance, null, 3.5 },
            new SourceSpan[5],
            default);

        //Assert
        scanner.Next().Symbol.Should().Be(Sym("BACKUP"));
        ParserToken real = scanner.Next();
        real.Symbol.Should().Be(Sym("REAL"));
        real.Value.Should().Be(-3.5);
    }

    [Fact]
    public void a_refused_symbol_backs_up_as_a_string()
    {
        //Arrange
        // Upstream writes MYBACKUP (STRING, $4, @4) for the SYMBOL alternative — the
        // refused word re-enters the stream as a STRING, not a SYMBOL.
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        host.CallBehavior = (procedure, arguments) => false;

        //Act
        object result = Action(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup SYMBOL")(
            context,
            new object[] { 7L, NumberPred, Nil.Instance, "word" },
            new SourceSpan[4],
            default);

        //Assert
        Cars(result).Should().Equal(7L);
        scanner.Next().Symbol.Should().Be(Sym("BACKUP"));
        ParserToken token = scanner.Next();
        token.Symbol.Should().Be(Sym("STRING"));
        token.Value.Should().Be("word");
    }

    [Fact]
    public void an_accepted_pitch_as_music_reparses_rather_than_consing()
    {
        //Arrange
        // The pitch body prefers the MUSIC reading: when the predicate accepts the
        // note event, the pitch reparses as PITCH_IDENTIFIER and the arglist stays
        // untouched for the REPARSE tail to extend.
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        host.IsNoteState = true;
        object accepted = null;
        host.CallBehavior = (procedure, arguments) =>
        {
            accepted = arguments[0];
            return arguments[0] is MadeMusic;
        };
        Pitch pitch = new Pitch();

        //Act
        object result = Action(
            "function_arglist_backup: EXPECT_OPTIONAL EXPECT_SCM function_arglist_backup pitch")(
            context,
            new object[] { 7L, NumberPred, Nil.Instance, pitch },
            new SourceSpan[4],
            default);

        //Assert
        result.Should().Be(Nil.Instance);
        ((MadeMusic)accepted).Name.Should().Be("NoteEvent");
        scanner.Next().Symbol.Should().Be(Sym("REPARSE"));
        ParserToken token = scanner.Next();
        token.Symbol.Should().Be(Sym("PITCH_IDENTIFIER"));
        token.Value.Should().Be(pitch);
    }
}
