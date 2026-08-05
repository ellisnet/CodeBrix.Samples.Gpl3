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
/// RULE ACTION GROUP 17 — figured bass, exercised two ways: real FIGURES-mode text
/// through the scanner and tables (the scanner already implements figures mode, and
/// the surrounding chord grammar reduces by defaults, so <c>&lt;6 4&gt;</c> chords
/// reach every figure rule from a plain <c>{ ... }</c> once the scanner is pushed
/// into figures state the way <c>push_figuredbass_state</c> would), and direct
/// invocation for each action's individual branches.
/// </summary>
public class RuleActionRag17Tests
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

    /// <summary>
    /// Parses real text with the scanner pushed into figures state first — the state
    /// <c>Lily_lexer::push_figuredbass_state</c> would enter; the FIGUREMODE
    /// mode-head action that calls it is another group's rule.
    /// </summary>
    private static ScriptedParserHost ParseFiguresText(string input, out LalrParser parser)
    {
        ScriptedParserHost host = new ScriptedParserHost();
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        scanner.PushState(LexerState.Figures);
        parser = new LalrParser(Tables, Bound);
        parser.Parse(scanner, host);
        return host;
    }

    private static MadeMusic NewFigure() => new MadeMusic { Name = "BassFigureEvent" };

    private static bool IsRatio(object value, long numerator, long denominator)
        => SchemeNumber.NumericEquals(value, SchemeNumber.MakeRatio(numerator, denominator));

    // ------ bass_number ------

    [Fact]
    public void a_non_negative_integer_bass_number_passes_through()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("bass_number: embedded_scm_bare")(
            context, new object[] { 6L }, new SourceSpan[1], default);

        //Assert
        result.Should().Be(6L);
        host.ErrorLevel.Should().Be(0);
    }

    [Fact]
    public void a_negative_integer_bass_number_errors_to_zero()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("bass_number: embedded_scm_bare")(
            context, new object[] { -3L }, new SourceSpan[1], default);

        //Assert
        result.Should().Be(0L);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_markup_bass_number_passes_through()
    {
        //Arrange
        // Not an integer, so the check falls to Text_interface::is_markup — the
        // scripted host answers true for strings.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("bass_number: embedded_scm_bare")(
            context, new object[] { "6+" }, new SourceSpan[1], default);

        //Assert
        result.AsText().Should().Be("6+");
        host.ErrorLevel.Should().Be(0);
    }

    [Fact]
    public void a_bass_number_that_is_neither_integer_nor_markup_errors_to_zero()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("bass_number: embedded_scm_bare")(
            context, new object[] { Unspecified.Instance }, new SourceSpan[1], default);

        //Assert
        result.Should().Be(0L);
        host.ErrorLevel.Should().Be(1);
    }

    // ------ bass_figure ------

    [Fact]
    public void a_figure_space_makes_an_empty_bass_figure_event()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("bass_figure: FIGURE_SPACE")(
            context, new object[] { Unspecified.Instance }, new SourceSpan[1], default);

        //Assert
        MadeMusic made = (MadeMusic)result;
        made.Name.Should().Be("BassFigureEvent");
        made.Properties.Should().BeEmpty();
    }

    [Fact]
    public void a_numeric_bass_number_lands_in_the_figure_property()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("bass_figure: bass_number")(
            context, new object[] { 6L }, new SourceSpan[1], default);

        //Assert
        MadeMusic made = (MadeMusic)result;
        made.Name.Should().Be("BassFigureEvent");
        made.Properties.Should().HaveCount(1);
        made.Properties[0].Name.Should().Be("figure");
        made.Properties[0].Value.Should().Be(6L);
    }

    [Fact]
    public void a_markup_bass_number_lands_in_the_text_property()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("bass_figure: bass_number")(
            context, new object[] { "markup" }, new SourceSpan[1], default);

        //Assert
        MadeMusic made = (MadeMusic)result;
        made.Properties.Should().HaveCount(1);
        made.Properties[0].Name.Should().Be("text");
        made.Properties[0].Value.AsText().Should().Be("markup");
    }

    [Fact]
    public void a_bass_number_that_is_neither_number_nor_markup_sets_no_property()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("bass_figure: bass_number")(
            context, new object[] { Unspecified.Instance }, new SourceSpan[1], default);

        //Assert
        ((MadeMusic)result).Properties.Should().BeEmpty();
    }

    [Fact]
    public void a_closing_bracket_sets_bracket_stop_on_the_figure()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();

        //Act
        object result = Action("bass_figure: bass_figure ']'")(
            context, new object[] { figure, ']' }, new SourceSpan[2], default);

        //Assert
        result.Should().BeSameAs(figure);
        figure.Properties.Should().HaveCount(1);
        figure.Properties[0].Name.Should().Be("bracket-stop");
        figure.Properties[0].Value.Should().Be(true);
    }

    // ------ bass_figure FIGURE_ALTERATION_EXPR ------

    [Fact]
    public void a_sharp_alteration_expression_sets_alteration_one_half()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();

        //Act
        object result = Action("bass_figure: bass_figure FIGURE_ALTERATION_EXPR")(
            context, new object[] { figure, "+" }, new SourceSpan[2], default);

        //Assert
        result.Should().BeSameAs(figure);
        figure.Properties.Should().HaveCount(1);
        figure.Properties[0].Name.Should().Be("alteration");
        IsRatio(figure.Properties[0].Value, 1L, 2L).Should().BeTrue();
    }

    [Fact]
    public void a_flat_alteration_expression_sets_alteration_minus_one_half()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();

        //Act
        Action("bass_figure: bass_figure FIGURE_ALTERATION_EXPR")(
            context, new object[] { figure, "-" }, new SourceSpan[2], default);

        //Assert
        IsRatio(figure.Properties[0].Value, -1L, 2L).Should().BeTrue();
    }

    [Fact]
    public void alteration_symbols_accumulate_and_whitespace_is_ignored()
    {
        //Arrange
        // Two sharps make a whole sharp — SHARP_ALTERATION twice — and the WHITE
        // the lexer's FIG_ALT_EXPR pattern admits between symbols contributes
        // nothing.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();

        //Act
        Action("bass_figure: bass_figure FIGURE_ALTERATION_EXPR")(
            context, new object[] { figure, " + +" }, new SourceSpan[2], default);

        //Assert
        figure.Properties.Should().HaveCount(1);
        figure.Properties[0].Value.Should().Be(1L);
    }

    [Fact]
    public void an_exclamation_resets_the_accumulated_alteration()
    {
        //Arrange
        // "!" resets the counter — the traditional pre-2.23.4 behavior the
        // upstream body mimics: ++!- is just a flat.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();

        //Act
        Action("bass_figure: bass_figure FIGURE_ALTERATION_EXPR")(
            context, new object[] { figure, "++!-" }, new SourceSpan[2], default);

        //Assert
        IsRatio(figure.Properties[0].Value, -1L, 2L).Should().BeTrue();
    }

    [Fact]
    public void a_bracketed_alteration_expression_also_sets_the_alteration_bracket()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();

        //Act
        Action("bass_figure: bass_figure FIGURE_ALTERATION_EXPR")(
            context, new object[] { figure, "[-]" }, new SourceSpan[2], default);

        //Assert
        figure.Properties.Should().HaveCount(2);
        figure.Properties[0].Name.Should().Be("alteration");
        IsRatio(figure.Properties[0].Value, -1L, 2L).Should().BeTrue();
        figure.Properties[1].Name.Should().Be("alteration-bracket");
        figure.Properties[1].Value.Should().Be(true);
    }

    [Fact]
    public void a_second_alteration_expression_warns_and_changes_nothing()
    {
        //Arrange
        // The alteration is already a number, so the surplus symbols are dropped
        // with a warning at the music — Music::warning, which does not raise the
        // error level.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();
        figure.Properties.Add(("alteration", SchemeNumber.MakeRatio(1L, 2L)));

        //Act
        object result = Action("bass_figure: bass_figure FIGURE_ALTERATION_EXPR")(
            context, new object[] { figure, "-" }, new SourceSpan[2], default);

        //Assert
        result.Should().BeSameAs(figure);
        figure.Properties.Should().HaveCount(1);
        IsRatio(figure.Properties[0].Value, 1L, 2L).Should().BeTrue();
        host.MusicWarnings.Should().HaveCount(1);
        host.MusicWarnings[0].Music.Should().BeSameAs(figure);
        host.MusicWarnings[0].Message.Should().Be(
            "Dropping surplus alteration symbols for bass figure.");
        host.ErrorLevel.Should().Be(0);
    }

    // ------ figured_bass_modification ------

    [Fact]
    public void each_figured_bass_modification_becomes_its_property_symbol()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object[] one = new object[1];
        SourceSpan[] spans = new SourceSpan[1];

        //Act / Assert
        Action("figured_bass_modification: E_PLUS")(context, one, spans, default)
            .Should().BeSameAs(Symbol.Intern("augmented"));
        Action("figured_bass_modification: E_EXCLAMATION")(context, one, spans, default)
            .Should().BeSameAs(Symbol.Intern("no-continuation"));
        Action("figured_bass_modification: '/'")(context, one, spans, default)
            .Should().BeSameAs(Symbol.Intern("diminished"));
        Action("figured_bass_modification: E_BACKSLASH")(context, one, spans, default)
            .Should().BeSameAs(Symbol.Intern("augmented-slash"));
    }

    [Fact]
    public void a_figured_bass_modification_is_set_true_on_the_figure()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();

        //Act
        object result = Action("bass_figure: bass_figure figured_bass_modification")(
            context,
            new object[] { figure, Symbol.Intern("augmented") },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(figure);
        figure.Properties.Should().HaveCount(1);
        figure.Properties[0].Name.Should().Be("augmented");
        figure.Properties[0].Value.Should().Be(true);
    }

    // ------ br_bass_figure ------

    [Fact]
    public void a_bare_bass_figure_passes_through_br_bass_figure()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();

        //Act
        object result = Action("br_bass_figure: bass_figure")(
            context, new object[] { figure }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(figure);
        figure.Properties.Should().BeEmpty();
    }

    [Fact]
    public void an_opening_bracket_sets_bracket_start_and_returns_the_figure()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic figure = NewFigure();

        //Act
        object result = Action("br_bass_figure: '[' bass_figure")(
            context, new object[] { '[', figure }, new SourceSpan[2], default);

        //Assert
        result.Should().BeSameAs(figure);
        figure.Properties.Should().HaveCount(1);
        figure.Properties[0].Name.Should().Be("bracket-start");
        figure.Properties[0].Value.Should().Be(true);
    }

    // ------ figure_list ------

    [Fact]
    public void an_empty_figure_list_is_nil()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("figure_list: /* empty */")(
            context, new object[0], new SourceSpan[0], default);

        //Assert
        result.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void a_figure_list_conses_the_new_figure_in_reverse()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MadeMusic first = NewFigure();
        MadeMusic second = NewFigure();
        object listSoFar = new Pair(first, Nil.Instance);

        //Act
        object result = Action("figure_list: figure_list br_bass_figure")(
            context, new object[] { listSoFar, second }, new SourceSpan[2], default);

        //Assert
        Pair pair = (Pair)result;
        pair.Car.Should().BeSameAs(second);
        pair.Cdr.Should().BeSameAs(listSoFar);
    }

    // ------ real FIGURES-mode text ------

    [Fact]
    public void figures_mode_real_text_lexes_the_figure_token_stream()
    {
        //Arrange
        // The scanner's figures mode is already ported, so the token inputs the
        // figure rules consume are reachable from real text once the figures state
        // is pushed. Written without spaces so each token's extent is unambiguous —
        // and with '/' never followed by a digit, which would lex as FRACTION
        // exactly as upstream's {FRACTION} rule would.
        ScriptedParserHost host = new ScriptedParserHost();
        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), @"<6-_4\+[5]2/\\3\!7>", "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        scanner.PushState(LexerState.Figures);

        //Act
        List<string> names = new List<string>();
        List<object> values = new List<object>();
        for (ParserToken token = scanner.Next(); token.Symbol != 0; token = scanner.Next())
        {
            names.Add(Tables.Symbols[token.Symbol]);
            values.Add(token.Value);
        }

        //Assert
        names.Should().Equal(
            "FIGURE_OPEN", "UNSIGNED", "FIGURE_ALTERATION_EXPR", "FIGURE_SPACE",
            "UNSIGNED", "E_PLUS", "'['", "UNSIGNED", "']'", "UNSIGNED", "'/'",
            "E_BACKSLASH", "UNSIGNED", "E_EXCLAMATION", "UNSIGNED", "FIGURE_CLOSE");
        values[1].Should().Be(6L);
        values[2].AsText().Should().Be("-");
        values[4].Should().Be(4L);
        values[7].Should().Be(5L);
        values[9].Should().Be(2L);
        values[12].Should().Be(3L);
        values[14].Should().Be(7L);
    }

    [Fact]
    public void a_real_text_figure_chord_parses_clean_through_every_figure_rule()
    {
        //Arrange
        // One chord exercising FIGURE_SPACE, a [4-] bracketed figure (bracket-start,
        // alteration, bracket-stop), all four modifications and an alteration reset:
        // the surrounding chord grammar (chord_body, note_chord_element) is another
        // group's and reduces by defaults, which is enough to drive these rules.
        //Act
        ScriptedParserHost host = ParseFiguresText(
            @"{ <_ 6 [4-] 5\+ 2\\ 3/ 7\! 8!> }", out LalrParser parser);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
        host.MusicWarnings.Should().BeEmpty();
    }

    [Fact]
    public void a_real_text_surplus_alteration_warns_at_the_built_figure_event()
    {
        //Arrange
        // 6- [-]: the first alteration lands, the second finds a number already
        // there and is dropped with the warning — carrying the actual
        // BassFigureEvent the parse built, whose recorded properties show the
        // whole real-text path: figure 6, alteration -1/2, and no bracket flag
        // from the dropped [-].
        //Act
        ScriptedParserHost host = ParseFiguresText("{ <6- [-]> }", out LalrParser parser);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.MusicWarnings.Should().HaveCount(1);
        host.MusicWarnings[0].Message.Should().Be(
            "Dropping surplus alteration symbols for bass figure.");

        MadeMusic figure = (MadeMusic)host.MusicWarnings[0].Music;
        figure.Name.Should().Be("BassFigureEvent");
        figure.Properties[0].Name.Should().Be("figure");
        figure.Properties[0].Value.Should().Be(6L);
        figure.Properties[1].Name.Should().Be("alteration");
        IsRatio(figure.Properties[1].Value, -1L, 2L).Should().BeTrue();

        // Since RAG14 landed, the path does not stop at the figure: chord_body wraps
        // the figure list as an event-chord and note_chord_element gives every element
        // the chord's duration — here the default one, since none was written. The
        // count moved from 2 to 3 when that rule became reachable, which is the whole
        // point of asserting on the real-text path.
        figure.Properties.Should().HaveCount(3);
        figure.Properties[2].Name.Should().Be("duration");
        figure.Properties[2].Value.Should().BeOfType<Duration>();
    }

    [Fact]
    public void a_real_text_negative_scheme_bass_number_reports_bass_number_expected()
    {
        //Arrange
        // #(bad) evaluates (scripted) to -3 — an integer that is negative, so
        // bass_number: embedded_scm_bare reports and recovers to zero.
        ScriptedParserHost host = new ScriptedParserHost();
        host.EvalResults["(bad)"] = -3L;
        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), "{ <#(bad)> }", "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        scanner.PushState(LexerState.Figures);
        LalrParser parser = new LalrParser(Tables, Bound);

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }
}
