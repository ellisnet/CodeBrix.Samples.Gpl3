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
/// RULE ACTION GROUP 7 — symbol lists, property paths and overrides. The
/// lookahead-manipulating rules (the <c>MYBACKUP</c> sites and
/// <c>simple_revert_context</c>) are driven through REAL parses over the scanner —
/// their token choreography is the point, and the grammar only accepts a
/// <c>\revert</c> at all when the synthetic <c>BACKUP</c>/<c>SCM_ARG</c>/
/// <c>SYMBOL_LIST</c> tokens arrive in the right order. Value-shaping rules are
/// invoked directly where their surrounding grammar is not ported yet.
/// </summary>
public class RuleActionRag7Tests
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

    private static ScriptedParserHost NewHost()
    {
        ScriptedParserHost host = new ScriptedParserHost();
        host.Keywords["override"] = ("OVERRIDE", null);
        host.Keywords["revert"] = ("REVERT", null);
        host.Keywords["set"] = ("SET", null);
        host.Keywords["unset"] = ("UNSET", null);
        host.GrobSymbols.Add(Symbol.Intern("NoteHead"));
        host.GrobSymbols.Add(Symbol.Intern("TextSpanner"));
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

    private static (ParseContext Context, ModalScanner Scanner, ScriptedParserHost Host) ScannerContext()
    {
        // A context whose token source is a REAL scanner over empty input, so an
        // action that pushes tokens by name can resolve them and a test can drain
        // what was pushed.
        ScriptedParserHost host = NewHost();
        ModalScanner scanner = new ModalScanner(LilyPondLexerRules.Create(host), string.Empty, "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        ParseContext context = new ParseContext(
            new LalrParser(Tables, new Dictionary<int, RuleAction>()), scanner)
        {
            UserState = host,
        };
        return (context, scanner, host);
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
    public void a_dotted_path_assignment_from_real_text_builds_the_full_key()
    {
        //Arrange
        // RAG1's `assignment: assignment_id '.' property_path '=' identifier_init`
        // consumes THIS group's property_path/symbol_list values.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("foo.bar.baz = 42");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.PathAssignments.Should().HaveCount(1);
        Pair key = (Pair)host.PathAssignments[0].Key;
        Cars(key).Should().Equal(
            Symbol.Intern("foo"), Symbol.Intern("bar"), Symbol.Intern("baz"));
        host.PathAssignments[0].Value.Should().Be(42L);
    }

    [Fact]
    public void an_override_from_real_text_consults_the_grob_table_and_parses_clean()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\override NoteHead.color = 4 }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
        host.GrobQueries.Should().Contain(Symbol.Intern("NoteHead"));
        host.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void an_override_with_a_context_prefix_parses_clean()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\override Voice.NoteHead.color = 4 }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.GrobQueries.Should().Contain(Symbol.Intern("Voice"));
        host.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void an_override_of_a_short_path_reports_bad_grob_property_path()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\override color = 4 }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(1);
        parser.Diagnostics[0].Should().Contain("bad grob property path");
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_set_of_a_bare_property_parses_clean()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\set autoBeaming = 4 }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
    }

    [Fact]
    public void a_set_of_a_three_part_path_reports_bad_context_property_path()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\set a.b.c = 4 }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(1);
        parser.Diagnostics[0].Should().Contain("bad context property path");
    }

    [Fact]
    public void an_unset_from_real_text_parses_clean()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\unset autoBeaming }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
    }

    [Fact]
    public void a_revert_of_a_grob_path_runs_the_backup_token_dance()
    {
        //Arrange
        // \revert NoteHead.color can ONLY parse if simple_revert_context pushes the
        // SCM_IDENTIFIER remainder and revert_arg_backup's MYBACKUP feeds BACKUP,
        // SCM_ARG and finally SYMBOL_LIST back in front of the pending lookahead —
        // BACKUP never comes from raw text, so a clean finish IS the token-flow
        // assertion.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\revert NoteHead.color }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
        host.GrobQueries.Should().Contain(Symbol.Intern("NoteHead"));
        host.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void a_revert_with_a_context_prefix_runs_the_dance_from_the_context()
    {
        //Arrange
        // The first component is not a grob, so it becomes the context and an EMPTY
        // list is pushed back; the whole grob path then arrives through the
        // SCM_ARG '.' route, twice.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\revert Staff.NoteHead.color }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.GrobQueries[0].Should().BeSameAs(Symbol.Intern("Staff"));
        host.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void a_revert_without_a_dot_warns_deprecated()
    {
        //Arrange
        // The backward-compatible `\revert NoteHead color` goes through
        // revert_arg_part's dotless alternative, which warns.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\revert NoteHead color }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.Warnings.Should().HaveCount(1);
        host.Warnings[0].Message.Should()
            .Contain("missing `.' in property path NoteHead.color");
    }

    [Fact]
    public void a_revert_of_a_long_path_takes_the_symbol_list_backup_route()
    {
        //Arrange
        // Once two components are in hand, MYBACKUP switches from SCM_ARG to
        // SYMBOL_LIST and symbol_list_arg's '.' rule collects the remainder.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("{ \\revert TextSpanner.bound-details.left.text }");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().Be(0);
        host.ErrorLevel.Should().Be(0);
        host.Warnings.Should().BeEmpty();
    }

    // ------ lookahead-manipulating rules, invoked over a drainable scanner ------

    [Fact]
    public void simple_revert_context_for_a_grob_supplies_bottom_and_pushes_the_whole_list()
    {
        //Arrange
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        object part = Pair.List(Symbol.Intern("NoteHead"));

        //Act
        object result = Action("simple_revert_context: symbol_list_part")(
            context, new object[] { part }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(Symbol.Intern("Bottom"));
        ParserToken pushed = scanner.Next();
        pushed.Symbol.Should().Be(Sym("SCM_IDENTIFIER"));
        Cars(pushed.Value).Should().Equal(Symbol.Intern("NoteHead"));
        host.GrobQueries.Should().Equal(Symbol.Intern("NoteHead"));
    }

    [Fact]
    public void simple_revert_context_for_a_context_pushes_the_remainder()
    {
        //Arrange
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        object part = Pair.List(Symbol.Intern("Staff"));

        //Act
        object result = Action("simple_revert_context: symbol_list_part")(
            context, new object[] { part }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(Symbol.Intern("Staff"));
        ParserToken pushed = scanner.Next();
        pushed.Symbol.Should().Be(Sym("SCM_IDENTIFIER"));
        pushed.Value.Should().BeSameAs(Nil.Instance);
        host.GrobQueries.Should().Equal(Symbol.Intern("Staff"));
    }

    [Fact]
    public void revert_arg_backup_backs_up_a_single_part_as_scm_arg()
    {
        //Arrange
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        object part = Pair.List(Symbol.Intern("NoteHead"));

        //Act
        object result = Action("revert_arg_backup: revert_arg_part")(
            context, new object[] { part }, new SourceSpan[1], default);

        //Assert
        // MYBACKUP pushes token-then-BACKUP, so BACKUP comes back FIRST.
        result.Should().BeSameAs(part);
        ParserToken backup = scanner.Next();
        backup.Symbol.Should().Be(Sym("BACKUP"));
        backup.Value.Should().BeSameAs(Unspecified.Instance);
        ParserToken argument = scanner.Next();
        argument.Symbol.Should().Be(Sym("SCM_ARG"));
        argument.Value.Should().BeSameAs(part);
    }

    [Fact]
    public void revert_arg_backup_backs_up_a_longer_list_reversed_as_symbol_list()
    {
        //Arrange
        // revert_arg_part delivers in reverse; the SYMBOL_LIST it backs up with is
        // restored to written order.
        (ParseContext context, ModalScanner scanner, ScriptedParserHost host) = ScannerContext();
        object part = Pair.List(Symbol.Intern("color"), Symbol.Intern("NoteHead"));

        //Act
        Action("revert_arg_backup: revert_arg_part")(
            context, new object[] { part }, new SourceSpan[1], default);

        //Assert
        ParserToken backup = scanner.Next();
        backup.Symbol.Should().Be(Sym("BACKUP"));
        ParserToken list = scanner.Next();
        list.Symbol.Should().Be(Sym("SYMBOL_LIST"));
        Cars(list.Value).Should().Equal(Symbol.Intern("NoteHead"), Symbol.Intern("color"));
    }

    // ------ value-shaping rules, invoked directly ------

    [Fact]
    public void property_path_restores_written_order()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object reversed = Pair.List(Symbol.Intern("baz"), Symbol.Intern("bar"));

        //Act
        object result = Action("property_path: symbol_list_rev")(
            context, new object[] { reversed }, new SourceSpan[1], default);

        //Assert
        Cars(result).Should().Equal(Symbol.Intern("bar"), Symbol.Intern("baz"));
    }

    [Fact]
    public void a_property_assignment_operation_builds_the_assign_triple()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("property_operation: symbol '=' scalar")(
            context,
            new object[] { Symbol.Intern("fontSize"), '=', 42L },
            new SourceSpan[3],
            default);

        //Assert
        Cars(result).Should().Equal(Symbol.Intern("assign"), Symbol.Intern("fontSize"), 42L);
    }

    [Fact]
    public void an_unset_operation_builds_the_unset_pair()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("property_operation: UNSET symbol")(
            context,
            new object[] { null, Symbol.Intern("fontSize") },
            new SourceSpan[2],
            default);

        //Assert
        Cars(result).Should().Equal(Symbol.Intern("unset"), Symbol.Intern("fontSize"));
    }

    [Fact]
    public void an_override_operation_builds_the_push_entry()
    {
        //Arrange
        // (push grob-symbol scalar . property-path) — scm_cons2 puts the scalar
        // BETWEEN the grob and the rest of the path.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object path = Pair.List(Symbol.Intern("NoteHead"), Symbol.Intern("color"));

        //Act
        object result = Action("property_operation: OVERRIDE revert_arg '=' scalar")(
            context,
            new object[] { null, path, '=', 4L },
            new SourceSpan[4],
            default);

        //Assert
        Cars(result).Should().Equal(
            Symbol.Intern("push"), Symbol.Intern("NoteHead"), 4L, Symbol.Intern("color"));
    }

    [Fact]
    public void an_override_operation_with_a_short_path_is_an_error()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("property_operation: OVERRIDE revert_arg '=' scalar")(
            context,
            new object[] { null, Pair.List(Symbol.Intern("NoteHead")), '=', 4L },
            new SourceSpan[4],
            default);

        //Assert
        result.Should().BeSameAs(DefaultArgument.Instance);
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_revert_operation_conses_pop_onto_the_path()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object path = Pair.List(Symbol.Intern("NoteHead"), Symbol.Intern("color"));

        //Act
        object result = Action("property_operation: REVERT revert_arg")(
            context, new object[] { null, path }, new SourceSpan[2], default);

        //Assert
        Pair pair = (Pair)result;
        pair.Car.Should().BeSameAs(Symbol.Intern("pop"));
        pair.Cdr.Should().BeSameAs(path);
    }

    [Fact]
    public void revert_arg_hands_back_the_symbol_list()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object list = Pair.List(Symbol.Intern("NoteHead"), Symbol.Intern("color"));

        //Act
        object result = Action("revert_arg: revert_arg_backup BACKUP symbol_list_arg")(
            context, new object[] { Nil.Instance, null, list }, new SourceSpan[3], default);

        //Assert
        result.Should().BeSameAs(list);
    }

    [Fact]
    public void revert_arg_part_with_a_dot_joins_part_and_backed_up_argument()
    {
        //Arrange
        // Both sides are in reverse, so the NEW part goes in front.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action(
            "revert_arg_part: revert_arg_backup BACKUP SCM_ARG '.' symbol_list_part")(
            context,
            new object[]
            {
                Nil.Instance, null,
                Pair.List(Symbol.Intern("NoteHead")), '.',
                Pair.List(Symbol.Intern("color")),
            },
            new SourceSpan[5],
            default);

        //Assert
        Cars(result).Should().Equal(Symbol.Intern("color"), Symbol.Intern("NoteHead"));
        host.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void revert_arg_part_without_a_dot_joins_and_warns()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action(
            "revert_arg_part: revert_arg_backup BACKUP SCM_ARG symbol_list_part")(
            context,
            new object[]
            {
                Nil.Instance, null,
                Pair.List(Symbol.Intern("NoteHead")),
                Pair.List(Symbol.Intern("color")),
            },
            new SourceSpan[4],
            default);

        //Assert
        Cars(result).Should().Equal(Symbol.Intern("color"), Symbol.Intern("NoteHead"));
        host.Warnings.Should().HaveCount(1);
        host.Warnings[0].Message.Should()
            .Contain("missing `.' in property path NoteHead.color");
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void symbol_list_arg_with_a_dot_appends_the_reversed_tail()
    {
        //Arrange
        // ly_append COPIES the SYMBOL_LIST, so the token's own list is unharmed.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object head = Pair.List(Symbol.Intern("a"), Symbol.Intern("b"));
        object reversedTail = Pair.List(Symbol.Intern("d"), Symbol.Intern("c"));

        //Act
        object result = Action("symbol_list_arg: SYMBOL_LIST '.' symbol_list_rev")(
            context,
            new object[] { head, '.', reversedTail },
            new SourceSpan[3],
            default);

        //Assert
        Cars(result).Should().Equal(
            Symbol.Intern("a"), Symbol.Intern("b"), Symbol.Intern("c"), Symbol.Intern("d"));
        Cars(head).Should().Equal(Symbol.Intern("a"), Symbol.Intern("b"));
    }

    [Fact]
    public void symbol_list_rev_prepends_the_new_part_destructively()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object accumulated = Pair.List(Symbol.Intern("b"), Symbol.Intern("a"));
        object part = Pair.List(Symbol.Intern("c"));

        //Act
        object result = Action("symbol_list_rev: symbol_list_rev '.' symbol_list_part")(
            context,
            new object[] { accumulated, '.', part },
            new SourceSpan[3],
            default);

        //Assert
        result.Should().BeSameAs(part);
        Cars(result).Should().Equal(Symbol.Intern("c"), Symbol.Intern("b"), Symbol.Intern("a"));
    }

    [Fact]
    public void a_scheme_value_symbol_list_part_reverses_keys_and_interns_strings()
    {
        //Arrange
        // Keys are symbols and non-negative exact integers; strings are accepted
        // and interned. The result is in reverse, as the rule's name promises.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object value = Pair.List(Symbol.Intern("a"), "b", 3L);

        //Act
        object result = Action("symbol_list_part: embedded_scm_bare")(
            context, new object[] { value }, new SourceSpan[1], default);

        //Assert
        Cars(result).Should().Equal(3L, Symbol.Intern("b"), Symbol.Intern("a"));
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void a_scheme_value_that_is_not_key_material_is_not_a_key()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("symbol_list_part: embedded_scm_bare")(
            context, new object[] { 3.5 }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(Nil.Instance);
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_string_symbol_list_element_becomes_a_symbol()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("symbol_list_element: STRING")(
            context, new object[] { "foo" }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(Symbol.Intern("foo"));
    }

    [Fact]
    public void a_symbol_word_becomes_a_single_key_list()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("symbol_list_part_bare: SYMBOL")(
            context, new object[] { "NoteHead" }, new SourceSpan[1], default);

        //Assert
        Cars(result).Should().Equal(Symbol.Intern("NoteHead"));
    }

    [Fact]
    public void a_dotted_symbol_word_splits_and_delivers_in_reverse()
    {
        //Arrange
        // try_word_variants splits the word on '.' and ',' into a symbol list, and
        // the rule reverses it into part order.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("symbol_list_part_bare: SYMBOL")(
            context, new object[] { "foo.bar" }, new SourceSpan[1], default);

        //Assert
        Cars(result).Should().Equal(Symbol.Intern("bar"), Symbol.Intern("foo"));
    }

    [Fact]
    public void a_word_that_cannot_be_a_key_is_an_error()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("symbol_list_part_bare: SYMBOL")(
            context, new object[] { "3x" }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(Nil.Instance);
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_symbol_list_element_is_wrapped_as_a_one_element_part()
    {
        //Arrange
        // The UNSIGNED alternative of symbol_list_element passes a number through,
        // so integer keys arrive here too.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("symbol_list_part_bare: symbol_list_element")(
            context, new object[] { 3L }, new SourceSpan[1], default);

        //Assert
        Cars(result).Should().Equal(3L);
    }

    [Fact]
    public void grob_prop_path_prepends_bottom_for_a_grob_head()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object spec = Pair.List(Symbol.Intern("NoteHead"), Symbol.Intern("color"));

        //Act
        object result = Action("grob_prop_path: grob_prop_spec")(
            context, new object[] { spec }, new SourceSpan[1], default);

        //Assert
        Cars(result).Should().Equal(
            Symbol.Intern("Bottom"), Symbol.Intern("NoteHead"), Symbol.Intern("color"));
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void grob_prop_path_of_three_parts_without_a_grob_passes_through()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object spec = Pair.List(
            Symbol.Intern("Voice"), Symbol.Intern("NoteHead"), Symbol.Intern("color"));

        //Act
        object result = Action("grob_prop_path: grob_prop_spec")(
            context, new object[] { spec }, new SourceSpan[1], default);

        //Assert
        result.Should().BeSameAs(spec);
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void grob_prop_path_of_too_few_parts_is_bad()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("grob_prop_path: grob_prop_spec")(
            context,
            new object[] { Pair.List(Symbol.Intern("color")) },
            new SourceSpan[1],
            default);

        //Assert
        result.Should().BeSameAs(DefaultArgument.Instance);
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void grob_prop_path_with_a_separate_property_path_joins_and_warns()
    {
        //Arrange
        // The two-part alternative is the traditional split form —
        // \override NoteHead #'color — and always earns the missing-dot warning.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object spec = Pair.List(Symbol.Intern("NoteHead"));
        object path = Pair.List(Symbol.Intern("color"));

        //Act
        object result = Action("grob_prop_path: grob_prop_spec property_path")(
            context, new object[] { spec, path }, new SourceSpan[2], default);

        //Assert
        Cars(result).Should().Equal(
            Symbol.Intern("Bottom"), Symbol.Intern("NoteHead"), Symbol.Intern("color"));
        host.Warnings.Should().HaveCount(1);
        host.Warnings[0].Message.Should()
            .Contain("missing `.' in property path NoteHead.color");
    }

    [Fact]
    public void grob_prop_path_with_a_property_path_rejects_an_overlong_spec()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object spec = Pair.List(
            Symbol.Intern("Voice"), Symbol.Intern("NoteHead"), Symbol.Intern("color"));

        //Act
        object result = Action("grob_prop_path: grob_prop_spec property_path")(
            context,
            new object[] { spec, Pair.List(Symbol.Intern("thickness")) },
            new SourceSpan[2],
            default);

        //Assert
        result.Should().BeSameAs(DefaultArgument.Instance);
        context.ErrorCount.Should().Be(1);
        host.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void context_prop_spec_of_one_part_gets_the_bottom_context()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("context_prop_spec: symbol_list_rev")(
            context,
            new object[] { Pair.List(Symbol.Intern("fontSize")) },
            new SourceSpan[1],
            default);

        //Assert
        Cars(result).Should().Equal(Symbol.Intern("Bottom"), Symbol.Intern("fontSize"));
    }

    [Fact]
    public void context_prop_spec_of_two_parts_is_kept()
    {
        //Arrange
        // The symbol_list_rev arrives REVERSED.
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("context_prop_spec: symbol_list_rev")(
            context,
            new object[] { Pair.List(Symbol.Intern("fontSize"), Symbol.Intern("Staff")) },
            new SourceSpan[1],
            default);

        //Assert
        Cars(result).Should().Equal(Symbol.Intern("Staff"), Symbol.Intern("fontSize"));
        context.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void context_prop_spec_of_three_parts_is_bad()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("context_prop_spec: symbol_list_rev")(
            context,
            new object[]
            {
                Pair.List(Symbol.Intern("c"), Symbol.Intern("b"), Symbol.Intern("a")),
            },
            new SourceSpan[1],
            default);

        //Assert
        result.Should().BeSameAs(DefaultArgument.Instance);
        context.ErrorCount.Should().Be(1);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void an_override_def_dispatches_to_the_property_override_constructor()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object path = Pair.List(
            Symbol.Intern("Bottom"), Symbol.Intern("NoteHead"), Symbol.Intern("color"));

        //Act
        object result = Action("music_property_def: OVERRIDE grob_prop_path '=' scalar")(
            context, new object[] { null, path, '=', 4L }, new SourceSpan[4], default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("property-override");
        mark.Arguments[0].Should().BeSameAs(Symbol.Intern("Bottom"));
        Cars(mark.Arguments[1]).Should().Equal(Symbol.Intern("NoteHead"), Symbol.Intern("color"));
        mark.Arguments[2].Should().Be(4L);
    }

    [Fact]
    public void an_override_def_of_an_undefined_path_is_unspecified_music()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("music_property_def: OVERRIDE grob_prop_path '=' scalar")(
            context,
            new object[] { null, DefaultArgument.Instance, '=', 4L },
            new SourceSpan[4],
            default);

        //Assert
        ((SyntaxMark)result).Name.Should().Be("unspecified-music");
    }

    [Fact]
    public void a_revert_def_dispatches_to_the_property_revert_constructor()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object path = Pair.List(Symbol.Intern("NoteHead"), Symbol.Intern("color"));

        //Act
        object result = Action("music_property_def: REVERT simple_revert_context revert_arg")(
            context,
            new object[] { null, Symbol.Intern("Bottom"), path },
            new SourceSpan[3],
            default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("property-revert");
        mark.Arguments[0].Should().BeSameAs(Symbol.Intern("Bottom"));
        mark.Arguments[1].Should().BeSameAs(path);
    }

    [Fact]
    public void a_set_def_dispatches_to_the_property_set_constructor()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object spec = Pair.List(Symbol.Intern("Staff"), Symbol.Intern("fontSize"));

        //Act
        object result = Action("music_property_def: SET context_prop_spec '=' scalar")(
            context, new object[] { null, spec, '=', 4L }, new SourceSpan[4], default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("property-set");
        mark.Arguments[0].Should().BeSameAs(Symbol.Intern("Staff"));
        mark.Arguments[1].Should().BeSameAs(Symbol.Intern("fontSize"));
        mark.Arguments[2].Should().Be(4L);
    }

    [Fact]
    public void an_unset_def_of_an_undefined_spec_is_unspecified_music()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("music_property_def: UNSET context_prop_spec")(
            context,
            new object[] { null, DefaultArgument.Instance },
            new SourceSpan[2],
            default);

        //Assert
        ((SyntaxMark)result).Name.Should().Be("unspecified-music");
    }

    [Fact]
    public void an_unset_def_dispatches_to_the_property_unset_constructor()
    {
        //Arrange
        ScriptedParserHost host = NewHost();
        ParseContext context = NewContext(host);
        object spec = Pair.List(Symbol.Intern("Staff"), Symbol.Intern("fontSize"));

        //Act
        object result = Action("music_property_def: UNSET context_prop_spec")(
            context, new object[] { null, spec }, new SourceSpan[2], default);

        //Assert
        SyntaxMark mark = (SyntaxMark)result;
        mark.Name.Should().Be("property-unset");
        mark.Arguments[0].Should().BeSameAs(Symbol.Intern("Staff"));
        mark.Arguments[1].Should().BeSameAs(Symbol.Intern("fontSize"));
    }
}
