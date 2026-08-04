// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// Asserts that every markup rule action (RAG18 and RAG19 — the last 51 of the 479)
/// actually RUNS on real LilyPond text.
/// <para>
/// The rule-action fence proves an action is REGISTERED, and the per-group tests prove
/// the ones they exercise behave. Neither catches the third failure: an action that is
/// registered, correct, and never reached — because its production is unreachable in
/// the tables, or because the LEXER never produces the tokens it needs. The markup
/// layer is where that matters most, since half of it is driven by <c>EXPECT_*</c>
/// tokens the scanner invents from a command's signature rather than by anything
/// written in the file.
/// </para>
/// <para>
/// So this runs a corpus through the real scanner and the real tables with every
/// action wrapped in a recorder, and insists all 51 fired and nothing errored. It
/// found four rules the per-group tests had missed when it was first run.
/// </para>
/// </summary>
public class MarkupReachabilityTests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    /// <summary>The nonterminals RAG18 and RAG19 own, including their mid-rule actions.</summary>
    private static readonly HashSet<string> MarkupNonterminals = new HashSet<string>(
        new[]
        {
            "full_markup", "full_markup_list", "markup", "markup_braced_list",
            "markup_braced_list_body", "markup_composed_list", "markup_list",
            "markup_mode", "markup_mode_word", "markup_scm", "markup_top",
            "markup_uncomposed_list", "markup_word", "partial_markup", "simple_markup",
            "simple_markup_noword", "markup_arglist_partial",
            "markup_command_basic_arguments", "markup_command_list",
            "markup_command_list_arguments", "markup_head_1_item", "markup_head_1_list",
            "markup_partial_function", "$@11", "$@12", "$@13", "$@14", "$@15",
        },
        StringComparer.Ordinal);

    /// <summary>
    /// The corpus. Each line is chosen to reach a rule the others do not, and the set
    /// is what makes the count below come out at 51 — adding a markup production
    /// upstream will fail this test until something here exercises it.
    /// </summary>
    private static readonly string[] Corpus =
    {
        // words, braced lists, and the splice
        "\\markup hello",
        "\\markup \"two words\"",
        "\\markup { }",
        "\\markup { a b }",
        "\\markup { a { b c } d }",
        "\\markuplist { a b }",

        // command chains, at the top and inside a list
        "\\markup \\bold x",
        "\\markup \\bold \\italic x",
        "\\markup { \\bold a b }",
        "\\markuplist \\bold { a b }",

        // commands and their argument shapes
        "\\markup \\raise #2 x",
        "\\markup \\combine a b",
        "\\markup \\column { a b }",
        "\\markup \\fromproperty \"t\"",
        "\\markup \\raise { 4 } x",
        "\\markup \\test \\notemode { c } x",
        "\\markup \\test \\mus x",
        "\\markuplist \\table-of-contents",

        // scores in markup
        "\\markup \\score { c }",
        "\\markuplist \\score-lines { c }",

        // embedded Scheme, classified by markup_scm
        "\\markup #(m)",
        "\\markuplist #(ml)",

        // \etc, and each way an expectation is discarded or matched
        "foo = \\markup \\bold \\etc",
        "foo = \\markup \\raise #2 \\etc",
        "foo = \\markup \\raise \\etc",
        "foo = \\markup \\bold \\raise \\etc",
        "foo = \\markup \\column \\etc",
        "foo = \\markup \\padlist \\etc",
        "foo = \\markup \\numafter \\etc",
    };

    private static ScriptedParserHost NewHost()
    {
        ScriptedParserHost host = new ScriptedParserHost { MakeRealMusic = true };

        host.Keywords["markup"] = ("MARKUP", null);
        host.Keywords["markuplist"] = ("MARKUPLIST", null);
        host.Keywords["etc"] = ("ETC", null);
        host.Keywords["notemode"] = ("NOTEMODE", null);

        host.Globals.Bindings[Symbol.Intern("toplevel-text-handler")] = "text-proc";
        host.WordScans[Symbol.Intern("c")] = ("NOTENAME_PITCH", new Pitch(0, 0, Rational.Zero));
        host.Identifiers["mus"] = new LexerLookup("MUSIC_IDENTIFIER", new MusicObject(Nil.Instance));

        host.EvalResults["2"] = 2L;
        host.EvalResults["(m)"] = "a-markup";
        host.EvalResults["(ml)"] = Pair.List("a", "b");

        // Every declared argument accepted: this test is about REACHING the rules, and
        // the per-group tests own what a refused predicate does.
        host.CallBehavior = (procedure, arguments) => true;

        host.MarkupCommands["bold"] = ("MARKUP_FUNCTION", "bold-proc", new[] { "markup?" });
        host.MarkupCommands["italic"] = ("MARKUP_FUNCTION", "italic-proc", new[] { "markup?" });
        host.MarkupCommands["raise"]
            = ("MARKUP_FUNCTION", "raise-proc", new[] { "number?", "markup?" });
        host.MarkupCommands["combine"]
            = ("MARKUP_FUNCTION", "combine-proc", new[] { "markup?", "markup?" });
        host.MarkupCommands["column"]
            = ("MARKUP_FUNCTION", "column-proc", new[] { "markup-list?" });
        host.MarkupCommands["fromproperty"]
            = ("MARKUP_FUNCTION", "fromproperty-proc", new[] { "symbol?" });
        host.MarkupCommands["test"]
            = ("MARKUP_FUNCTION", "test-proc", new[] { "ly:music?", "markup?" });
        host.MarkupCommands["padlist"]
            = ("MARKUP_FUNCTION", "padlist-proc", new[] { "number?", "markup-list?" });
        host.MarkupCommands["numafter"]
            = ("MARKUP_FUNCTION", "numafter-proc", new[] { "markup?", "number?" });
        host.MarkupCommands["table-of-contents"]
            = ("MARKUP_LIST_FUNCTION", "toc-proc", new string[0]);

        return host;
    }

    [Fact]
    public void every_markup_rule_action_runs_on_real_text()
    {
        //Arrange
        RuleActionTable table = LilyPondRuleActions.Create();
        HashSet<string> fired = new HashSet<string>(StringComparer.Ordinal);

        Dictionary<int, RuleAction> recording = new Dictionary<int, RuleAction>();
        foreach (KeyValuePair<int, RuleAction> entry in table.Bind(Tables))
        {
            string identity = Tables.Rules[entry.Key].Source.Identity;
            RuleAction action = entry.Value;
            recording[entry.Key] = (context, values, locations, location) =>
            {
                fired.Add(identity);
                return action(context, values, locations, location);
            };
        }

        //Act
        List<string> failures = new List<string>();
        foreach (string input in Corpus)
        {
            ScriptedParserHost host = NewHost();
            ModalScanner scanner = new ModalScanner(
                LilyPondLexerRules.Create(host), input, "<corpus>");
            scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
            host.Scanner = scanner;

            LalrParser parser = new LalrParser(Tables, recording);
            parser.Parse(scanner, host);

            if (parser.ErrorCount > 0)
            {
                failures.Add(input + " => " + string.Join("; ", parser.Diagnostics));
            }
        }

        //Assert
        // Every corpus line parses cleanly...
        failures.Should().BeEmpty();

        // ...and between them they run all 51 markup actions.
        List<string> unreached = new List<string>();
        int total = 0;
        foreach (ManifestEntry entry in RuleManifest.Entries)
        {
            if (!entry.HasAction)
            {
                continue;
            }

            string leftHandSide = entry.Identity.Substring(0, entry.Identity.IndexOf(':'));
            if (!MarkupNonterminals.Contains(leftHandSide))
            {
                continue;
            }

            total++;
            if (!fired.Contains(entry.Identity))
            {
                unreached.Add(entry.Identity);
            }
        }

        total.Should().Be(51);
        unreached.Should().BeEmpty();
    }
}
