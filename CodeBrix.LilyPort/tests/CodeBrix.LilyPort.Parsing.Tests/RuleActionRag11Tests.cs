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
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 11 — partial functions, <c>\etc</c>. The
/// <c>\override</c>/<c>\set</c>/<c>\repeat</c> shorthand alternatives ride entirely
/// on machinery RAG7 and RAG13 already ported and feed RAG1's
/// <c>identifier_init_nonumber: partial_function ETC</c>, so they are driven through
/// REAL parses of <c>name = ... \etc</c> assignments. The
/// <c>partial_function_scriptable</c> family needs <c>MUSIC_FUNCTION</c>-style tokens
/// whose <c>EXPECT_*</c> choreography belongs to the unported arglist groups, and the
/// markup/script alternatives need unported <c>script_dir</c>/<c>markup_mode</c>
/// values — those are invoked directly by identity.
/// </summary>
public class RuleActionRag11Tests
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

    private static ScriptedParserHost NewHost()
    {
        ScriptedParserHost host = new ScriptedParserHost();
        host.Keywords["override"] = ("OVERRIDE", null);
        host.Keywords["set"] = ("SET", null);
        host.Keywords["repeat"] = ("REPEAT", null);
        host.Keywords["etc"] = ("ETC", null);
        host.GrobSymbols.Add(Symbol.Intern("NoteHead"));
        return host;
    }

    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(string input)
    {
        ScriptedParserHost host = NewHost();
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), input, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
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

    /// <summary>Parses an assignment and returns the partial-music-function's call list.</summary>
    private static (object CallList, ScriptedParserHost Host, LalrParser Parser) ParsePartial(string input)
    {
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup(input);
        parser.Parse(scanner, host);

        object bound = host.Globals.Bindings[Symbol.Intern("fun")];
        SyntaxMark mark = (SyntaxMark)bound;
        mark.Name.Should().Be("partial-music-function");
        return (mark.Arguments[0], host, parser);
    }

    // ------ whole inputs through the real scanner and tables ------
    //
    // The rules cons entries in WRITTEN order as the parse unwinds; RAG1's
    // `identifier_init_nonumber: partial_function ETC` then REVERSES them, so the
    // call list reads INNERMOST FIRST — the entry that directly receives the
    // supplied argument leads, which is the order partial-music-function's fold
    // applies them in.

    [Fact]
    public void an_override_partial_from_real_text_builds_the_property_override_entry()
    {
        //Arrange / Act
        (object callList, ScriptedParserHost host, LalrParser parser)
            = ParsePartial("fun = \\override NoteHead.color = \\etc");

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
        List<object> entries = Cars(callList);
        entries.Should().HaveCount(1);

        // (property-override (NoteHead color) Bottom) — the path's REST before its
        // HEAD, the reverse of music_property_def's argument order.
        List<object> entry = Cars(entries[0]);
        entry[0].AsText().Should().Be("constructor:property-override");
        Cars(entry[1]).AsText().Should().Equal(Symbol.Intern("NoteHead"), Symbol.Intern("color"));
        entry[2].Should().BeSameAs(Symbol.Intern("Bottom"));
    }

    [Fact]
    public void a_set_partial_from_real_text_supplies_the_bottom_context()
    {
        //Arrange / Act
        (object callList, ScriptedParserHost host, LalrParser parser)
            = ParsePartial("fun = \\set autoBeaming = \\etc");

        //Assert
        parser.ErrorCount.Should().Be(0);
        List<object> entries = Cars(callList);
        entries.Should().HaveCount(1);

        // (property-set fontSize-symbol context) — scm_cadr before scm_car.
        List<object> entry = Cars(entries[0]);
        entry[0].AsText().Should().Be("constructor:property-set");
        entry[1].Should().BeSameAs(Symbol.Intern("autoBeaming"));
        entry[2].Should().BeSameAs(Symbol.Intern("Bottom"));
    }

    [Fact]
    public void a_chained_override_then_set_partial_lists_the_inner_entry_first()
    {
        //Arrange
        // The OVERRIDE alternative with a partial_function tail (parser.yy 884)
        // conses onto the inner SET entry; RAG1's reversal puts the inner SET first.
        //Act
        (object callList, ScriptedParserHost host, LalrParser parser)
            = ParsePartial("fun = \\override NoteHead.color = \\set autoBeaming = \\etc");

        //Assert
        parser.ErrorCount.Should().Be(0);
        List<object> entries = Cars(callList);
        entries.Should().HaveCount(2);
        Cars(entries[0])[0].AsText().Should().Be("constructor:property-set");
        Cars(entries[1])[0].AsText().Should().Be("constructor:property-override");
    }

    [Fact]
    public void a_chained_set_then_override_partial_takes_the_set_tail_rule()
    {
        //Arrange
        // The SET alternative with a partial_function tail (parser.yy 894), the
        // inner entry coming from the terminal OVERRIDE alternative.
        //Act
        (object callList, ScriptedParserHost host, LalrParser parser)
            = ParsePartial("fun = \\set autoBeaming = \\override NoteHead.color = \\etc");

        //Assert
        parser.ErrorCount.Should().Be(0);
        List<object> entries = Cars(callList);
        entries.Should().HaveCount(2);
        Cars(entries[0])[0].AsText().Should().Be("constructor:property-override");
        Cars(entries[1])[0].AsText().Should().Be("constructor:property-set");
    }

    [Fact]
    public void a_repeat_partial_with_a_count_puts_the_count_before_the_type()
    {
        //Arrange / Act
        (object callList, ScriptedParserHost host, LalrParser parser)
            = ParsePartial("fun = \\repeat volta 2 \\etc");

        //Assert
        parser.ErrorCount.Should().Be(0);
        List<object> entries = Cars(callList);
        entries.Should().HaveCount(1);

        // (repeat count type) — ly_list (Syntax::repeat, $3, $2).
        Cars(entries[0]).AsText().Should().Equal("constructor:repeat", 2L, "volta");
    }

    [Fact]
    public void a_repeat_partial_chained_onto_an_override_lists_the_override_first()
    {
        //Arrange / Act
        (object callList, ScriptedParserHost host, LalrParser parser)
            = ParsePartial("fun = \\repeat volta 2 \\override NoteHead.color = \\etc");

        //Assert
        parser.ErrorCount.Should().Be(0);
        List<object> entries = Cars(callList);
        entries.Should().HaveCount(2);
        Cars(entries[0])[0].AsText().Should().Be("constructor:property-override");
        Cars(entries[1]).AsText().Should().Equal("constructor:repeat", 2L, "volta");
    }

    [Fact]
    public void a_repeat_partial_without_a_count_leaves_the_count_slot_open()
    {
        //Arrange / Act
        (object callList, ScriptedParserHost host, LalrParser parser)
            = ParsePartial("fun = \\repeat volta \\etc");

        //Assert
        parser.ErrorCount.Should().Be(0);
        List<object> entries = Cars(callList);
        entries.Should().HaveCount(1);
        Cars(entries[0]).AsText().Should().Equal("constructor:repeat", "volta");
    }

    [Fact]
    public void a_countless_repeat_partial_chains_onto_a_set()
    {
        //Arrange / Act
        (object callList, ScriptedParserHost host, LalrParser parser)
            = ParsePartial("fun = \\repeat volta \\set autoBeaming = \\etc");

        //Assert
        parser.ErrorCount.Should().Be(0);
        List<object> entries = Cars(callList);
        entries.Should().HaveCount(2);
        Cars(entries[0])[0].AsText().Should().Be("constructor:property-set");
        Cars(entries[1]).AsText().Should().Equal("constructor:repeat", "volta");
    }

    [Fact]
    public void a_bad_override_path_in_a_partial_becomes_the_false_entry()
    {
        //Arrange
        // grob_prop_path (RAG7) reports "bad grob property path" and hands back
        // SCM_UNDEFINED; this group's action turns that into the (#f) entry.
        //Act
        (object callList, ScriptedParserHost host, LalrParser parser)
            = ParsePartial("fun = \\override color = \\etc");

        //Assert
        parser.ErrorCount.Should().Be(1);
        parser.Diagnostics[0].Should().Contain("bad grob property path");
        host.ErrorLevel.Should().Be(1);
        Cars(callList).Should().Equal(false);
    }

    // ------ partial_function_scriptable, invoked directly ------
    //
    // The MUSIC/EVENT/SCM_FUNCTION tokens and their EXPECT_* choreography belong to
    // the unported arglist groups, so no real-text path reaches these yet.

    [Theory]
    [InlineData("partial_function_scriptable: MUSIC_FUNCTION function_arglist_partial")]
    [InlineData("partial_function_scriptable: EVENT_FUNCTION function_arglist_partial")]
    [InlineData("partial_function_scriptable: SCM_FUNCTION function_arglist_partial")]
    public void a_partial_arglist_function_is_aconsed_onto_the_empty_chain(string identity)
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object function = new object();
        object arglist = Pair.List(1L);

        //Act
        object result = Action(identity)(
            context, new object[] { function, arglist }, new SourceSpan[2], default);

        //Assert
        Pair entry = (Pair)((Pair)result).Car;
        entry.Car.Should().BeSameAs(function);
        entry.Cdr.Should().BeSameAs(arglist);
        ((Pair)result).Cdr.Should().BeSameAs(Nil.Instance);
    }

    [Theory]
    [InlineData("partial_function_scriptable: MUSIC_FUNCTION EXPECT_SCM function_arglist_optional partial_function")]
    [InlineData("partial_function_scriptable: EVENT_FUNCTION EXPECT_SCM function_arglist_optional partial_function")]
    [InlineData("partial_function_scriptable: SCM_FUNCTION EXPECT_SCM function_arglist_optional partial_function")]
    public void a_function_over_an_inner_partial_is_aconsed_onto_its_chain(string identity)
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object function = new object();
        object arglist = Pair.List(1L);
        object chain = Pair.List(Pair.List(false));

        //Act
        object result = Action(identity)(
            context,
            new object[] { function, null, arglist, chain },
            new SourceSpan[4],
            default);

        //Assert
        Pair entry = (Pair)((Pair)result).Car;
        entry.Car.Should().BeSameAs(function);
        entry.Cdr.Should().BeSameAs(arglist);
        ((Pair)result).Cdr.Should().BeSameAs(chain);
    }

    [Theory]
    [InlineData("partial_function_scriptable: MUSIC_FUNCTION EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup partial_function")]
    [InlineData("partial_function_scriptable: EVENT_FUNCTION EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup partial_function")]
    [InlineData("partial_function_scriptable: SCM_FUNCTION EXPECT_OPTIONAL EXPECT_SCM function_arglist_nonbackup partial_function")]
    public void a_function_with_an_optional_argument_supplied_is_aconsed_the_same_way(string identity)
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object function = new object();
        object arglist = Pair.List(1L);
        object chain = Pair.List(Pair.List(false));

        //Act
        object result = Action(identity)(
            context,
            new object[] { function, null, null, arglist, chain },
            new SourceSpan[5],
            default);

        //Assert
        Pair entry = (Pair)((Pair)result).Car;
        entry.Car.Should().BeSameAs(function);
        entry.Cdr.Should().BeSameAs(arglist);
        ((Pair)result).Cdr.Should().BeSameAs(chain);
    }

    // ------ the undefined-path branches, invoked directly ------

    [Fact]
    public void a_set_partial_of_an_undefined_spec_is_the_false_entry()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("partial_function: SET context_prop_spec '='")(
            context,
            new object[] { null, DefaultArgument.Instance, '=' },
            new SourceSpan[3],
            default);

        //Assert
        Cars(result).Should().Equal(false);
    }

    [Fact]
    public void an_undefined_override_path_with_a_tail_drops_the_inner_chain()
    {
        //Arrange
        // Upstream's error value is the bare ly_list (SCM_BOOL_F) — NOT consed onto
        // $4 — so the entries collected after the bad path vanish with it.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object chain = Pair.List(Pair.List(false));

        //Act
        object result = Action("partial_function: OVERRIDE grob_prop_path '=' partial_function")(
            context,
            new object[] { null, DefaultArgument.Instance, '=', chain },
            new SourceSpan[4],
            default);

        //Assert
        Cars(result).Should().Equal(false);
    }

    [Fact]
    public void an_undefined_set_spec_with_a_tail_drops_the_inner_chain()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object chain = Pair.List(Pair.List(false));

        //Act
        object result = Action("partial_function: SET context_prop_spec '=' partial_function")(
            context,
            new object[] { null, DefaultArgument.Instance, '=', chain },
            new SourceSpan[4],
            default);

        //Assert
        Cars(result).Should().Equal(false);
    }

    // ------ the markup and script alternatives, invoked directly ------
    //
    // script_dir, markup_mode and markup_partial_function are later groups'
    // grammar, so no real-text path reaches these yet.

    [Fact]
    public void a_markup_partial_wraps_the_markup_and_pops_the_lexer_state()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object markupPartial = new object();

        //Act
        object result = Action("partial_function: script_dir markup_mode markup_partial_function")(
            context,
            new object[] { 1L, null, markupPartial },
            new SourceSpan[3],
            default);

        //Assert
        // ly_list (ly_list (partial-text-script-dispatch, wrapped-markup, dir)).
        List<object> outer = Cars(result);
        outer.Should().HaveCount(1);
        List<object> entry = Cars(outer[0]);
        entry.Should().HaveCount(3);

        SyntaxMark textScript = (SyntaxMark)entry[0];
        textScript.Name.Should().Be("partial-text-script");
        SyntaxMark wrapped = (SyntaxMark)entry[1];
        wrapped.Name.Should().Be("partial-markup");
        wrapped.Arguments[0].Should().BeSameAs(markupPartial);
        textScript.Arguments[0].Should().BeSameAs(wrapped);
        entry[2].Should().Be(1L);

        // parser->lexer_->pop_state () — markup_mode pushed it, this action pops it.
        host.LexerModeOperations.AsText().Should().Equal("pop-state");
    }

    [Fact]
    public void a_markup_partial_with_no_direction_uses_exact_zero()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("partial_function: script_dir markup_mode markup_partial_function")(
            context,
            new object[] { DefaultArgument.Instance, null, new object() },
            new SourceSpan[3],
            default);

        //Assert
        Cars(Cars(result)[0])[2].Should().Be(0L);
    }

    [Fact]
    public void a_directed_scriptable_partial_aconses_the_create_script_function()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object chain = Pair.List(Pair.List(false));

        //Act
        object result = Action("partial_function: script_dir partial_function_scriptable")(
            context, new object[] { -1L, chain }, new SourceSpan[2], default);

        //Assert
        // scm_acons (Syntax::create_script_function, ly_list ($1), $2).
        Pair entry = (Pair)((Pair)result).Car;
        entry.Car.AsText().Should().Be("constructor:create-script-function");
        Cars(entry.Cdr).Should().Equal(-1L);
        ((Pair)result).Cdr.Should().BeSameAs(chain);
    }

    [Fact]
    public void a_neutral_scriptable_partial_gets_direction_zero()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("partial_function: script_dir partial_function_scriptable")(
            context,
            new object[] { DefaultArgument.Instance, Nil.Instance },
            new SourceSpan[2],
            default);

        //Assert
        Pair entry = (Pair)((Pair)result).Car;
        Cars(entry.Cdr).Should().Equal(0L);
    }

    [Fact]
    public void a_bare_script_dir_partial_is_the_one_entry_chain()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("partial_function: script_dir")(
            context, new object[] { 1L }, new SourceSpan[1], default);

        //Assert
        Pair entry = (Pair)((Pair)result).Car;
        entry.Car.AsText().Should().Be("constructor:create-script-function");
        Cars(entry.Cdr).Should().Equal(1L);
        ((Pair)result).Cdr.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void a_bare_script_dir_partial_with_no_direction_uses_exact_zero()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("partial_function: script_dir")(
            context,
            new object[] { DefaultArgument.Instance },
            new SourceSpan[1],
            default);

        //Assert
        Cars(((Pair)((Pair)result).Car).Cdr).Should().Equal(0L);
    }
}
