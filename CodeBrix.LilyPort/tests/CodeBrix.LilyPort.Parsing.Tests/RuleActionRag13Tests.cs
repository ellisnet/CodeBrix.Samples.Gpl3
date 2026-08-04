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
/// RULE ACTION GROUP 13 — strings, scalars and numbers. The arithmetic runs whole
/// inputs through the real scanner and tables; the rules whose surrounding grammar is
/// not ported yet are invoked directly.
/// </summary>
public class RuleActionRag13Tests
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

    private static ScriptedParserHost ParseTokens(params (string Symbol, object Value)[] tokens)
    {
        ScriptedParserHost host = new ScriptedParserHost();
        List<ParserToken> list = new List<ParserToken>();
        for (int i = 0; i < tokens.Length; i++)
        {
            list.Add(new ParserToken(
                Sym(tokens[i].Symbol),
                tokens[i].Value,
                new SourceSpan("<test>", 1, i + 1, 1, i + 2)));
        }

        new LalrParser(Tables, Bound).Parse(new TokenListInput(list), host);
        return host;
    }

    [Fact]
    public void number_arithmetic_from_real_text_respects_precedence()
    {
        //Arrange / Act
        ScriptedParserHost host = ParseText("x = 3 + 4 * 2", out LalrParser parser);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Globals.Bindings[Symbol.Intern("x")].Should().Be(11L);
    }

    [Fact]
    public void subtraction_from_real_text_associates_left()
    {
        //Arrange / Act
        ScriptedParserHost host = ParseText("x = 10 - 3 - 2", out LalrParser parser);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Globals.Bindings[Symbol.Intern("x")].Should().Be(5L);
    }

    [Fact]
    public void unary_minus_from_real_text_negates()
    {
        //Arrange / Act
        ScriptedParserHost host = ParseText("x = - 5", out LalrParser parser);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Globals.Bindings[Symbol.Intern("x")].Should().Be(-5L);
    }

    [Fact]
    public void exact_division_makes_a_ratio_not_a_real()
    {
        //Arrange / Act
        // scm_divide over exact integers is exact — 3/4 stays a ratio, exactly as
        // Guile answers, because the value may go on to a duration.
        ScriptedParserHost host = ParseTokens(
            ("SYMBOL", "x"), ("'='", '='), ("UNSIGNED", 3L), ("'/'", '/'), ("UNSIGNED", 4L));

        //Assert
        object value = host.Globals.Bindings[Symbol.Intern("x")];
        SchemeNumber.IsExact(value).Should().BeTrue();
        SchemeNumber.NumericEquals(value, SchemeNumber.MakeRatio(3L, 4L)).Should().BeTrue();
    }

    [Fact]
    public void a_real_times_a_number_identifier_multiplies()
    {
        //Arrange / Act
        // bare_number_common: REAL NUMBER_IDENTIFIER — `2.5\cm` with \cm holding 4.
        ScriptedParserHost host = ParseTokens(
            ("SYMBOL", "x"), ("'='", '='), ("REAL", 2.5), ("NUMBER_IDENTIFIER", 4L));

        //Assert
        host.Globals.Bindings[Symbol.Intern("x")].Should().Be(10.0);
    }

    [Fact]
    public void an_unsigned_times_a_number_identifier_multiplies()
    {
        //Arrange / Act
        ScriptedParserHost host = ParseTokens(
            ("SYMBOL", "x"), ("'='", '='), ("UNSIGNED", 2L), ("NUMBER_IDENTIFIER", 3L));

        //Assert
        host.Globals.Bindings[Symbol.Intern("x")].Should().Be(6L);
    }

    [Fact]
    public void an_exact_unsigned_number_accepts_an_exact_and_refuses_the_rest()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("exact_unsigned_number: NUMBER_IDENTIFIER");

        //Act / Assert — an exact non-negative passes through untouched.
        action(context, new object[] { 5L }, new SourceSpan[1], default).Should().Be(5L);
        context.ErrorCount.Should().Be(0);

        // An inexact value is refused and recovers as 0.
        action(context, new object[] { 2.5 }, new SourceSpan[1], default).Should().Be(0L);
        context.ErrorCount.Should().Be(1);

        // A negative exact is refused too.
        action(context, new object[] { -3L }, new SourceSpan[1], default).Should().Be(0L);
        context.ErrorCount.Should().Be(2);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void an_embedded_scheme_exact_unsigned_number_must_be_a_number_at_all()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("exact_unsigned_number: embedded_scm")(
            context, new object[] { "not a number" }, new SourceSpan[1], default);

        //Assert
        result.Should().Be(0L);
        context.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void an_unsigned_integer_accepts_an_integral_real_as_guile_does()
    {
        //Arrange
        // scm_is_integer (3.0) is true in Guile, so upstream accepts it here.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("unsigned_integer: embedded_scm");

        //Act / Assert
        action(context, new object[] { 3.0 }, new SourceSpan[1], default).Should().Be(3.0);
        context.ErrorCount.Should().Be(0);

        action(context, new object[] { -2L }, new SourceSpan[1], default).Should().Be(0L);
        context.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void a_string_becomes_its_interned_symbol()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("symbol: STRING")(
            context, new object[] { "Voice" }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(Symbol.Intern("Voice"));
    }

    [Fact]
    public void an_irregular_symbol_bareword_is_an_error_but_still_interns()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("symbol: SYMBOL");

        //Act / Assert — a regular identifier passes silently.
        action(context, new object[] { "foo-bar" }, new SourceSpan[1], default)
            .Should().BeSameAs(Symbol.Intern("foo-bar"));
        context.ErrorCount.Should().Be(0);

        // One that starts with a digit is reported, yet still produces the symbol so
        // the parse can continue.
        action(context, new object[] { "9bad" }, new SourceSpan[1], default)
            .Should().BeSameAs(Symbol.Intern("9bad"));
        context.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void an_embedded_scheme_symbol_passes_through_and_a_string_is_interpreted()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("symbol: embedded_scm_bare");

        //Act / Assert — a symbol is already what the rule wants.
        action(context, new object[] { Symbol.Intern("direct") }, new SourceSpan[1], default)
            .Should().BeSameAs(Symbol.Intern("direct"));

        // A string is tried as a symbol via try_string_variants.
        action(context, new object[] { "as-string" }, new SourceSpan[1], default)
            .Should().BeSameAs(Symbol.Intern("as-string"));
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void an_embedded_scheme_non_symbol_is_an_error_and_generates_a_fresh_symbol()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("symbol: embedded_scm_bare")(
            context, new object[] { 42L }, new SourceSpan[1], default);

        //Assert
        context.ErrorCount.Should().Be(1);
        Symbol generated = (Symbol)result;
        generated.Should().NotBeSameAs(Symbol.Intern(generated.Name));
    }

    [Fact]
    public void text_accepts_a_markup_and_refuses_the_rest_with_an_empty_string()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("text: embedded_scm_bare");

        //Act / Assert — the scripted host counts strings as markups.
        action(context, new object[] { "a markup" }, new SourceSpan[1], default)
            .Should().Be("a markup");
        context.ErrorCount.Should().Be(0);

        action(context, new object[] { 42L }, new SourceSpan[1], default)
            .Should().Be(string.Empty);
        context.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void simple_string_accepts_both_string_shapes_and_refuses_the_rest()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction action = Action("simple_string: embedded_scm_bare");
        MutableString mutableString = new MutableString("mutable");

        //Act / Assert
        action(context, new object[] { "plain" }, new SourceSpan[1], default)
            .Should().Be("plain");
        action(context, new object[] { mutableString }, new SourceSpan[1], default)
            .Should().BeSameAs(mutableString);
        context.ErrorCount.Should().Be(0);

        action(context, new object[] { Symbol.Intern("nope") }, new SourceSpan[1], default)
            .Should().Be(string.Empty);
        context.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void a_negative_scalar_negates_the_bare_number()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("scalar: '-' bare_number")(
            context, new object[] { '-', 3L }, new SourceSpan[2], default);

        //Assert
        result.Should().Be(-3L);
    }

    [Fact]
    public void a_scalar_property_path_reverses_the_symbol_list_onto_the_path()
    {
        //Arrange
        // scalar: symbol_list_part_bare '.' property_path — $1 accumulated reversed,
        // so scm_reverse_x puts it right way round in front of the path.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object reversedHead = Pair.List(Symbol.Intern("b"), Symbol.Intern("a"));
        object path = Pair.List(Symbol.Intern("c"));

        //Act
        object result = Action("scalar: symbol_list_part_bare '.' property_path")(
            context, new object[] { reversedHead, '.', path }, new SourceSpan[3], default);

        //Assert
        Pair list = (Pair)result;
        list.Car.Should().BeSameAs(Symbol.Intern("a"));
        ((Pair)list.Cdr).Car.Should().BeSameAs(Symbol.Intern("b"));
        ((Pair)((Pair)list.Cdr).Cdr).Car.Should().BeSameAs(Symbol.Intern("c"));
    }

    [Fact]
    public void exclamations_toggle_from_undefined_through_true_and_false()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction empty = Action("exclamations: /* empty */");
        RuleAction mark = Action("exclamations: exclamations '!'");

        //Act
        object none = empty(context, new object[0], new SourceSpan[0], default);
        object one = mark(context, new object[] { none, '!' }, new SourceSpan[2], default);
        object two = mark(context, new object[] { one, '!' }, new SourceSpan[2], default);

        //Assert
        none.Should().BeSameAs(DefaultArgument.Instance);
        one.Should().Be(true);
        two.Should().Be(false);
    }

    [Fact]
    public void questions_toggle_the_same_way()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        RuleAction empty = Action("questions: /* empty */ %prec ':'");
        RuleAction mark = Action("questions: questions '?'");

        //Act
        object none = empty(context, new object[0], new SourceSpan[0], default);
        object one = mark(context, new object[] { none, '?' }, new SourceSpan[2], default);
        object two = mark(context, new object[] { one, '?' }, new SourceSpan[2], default);

        //Assert
        none.Should().BeSameAs(DefaultArgument.Instance);
        one.Should().Be(true);
        two.Should().Be(false);
    }
}
