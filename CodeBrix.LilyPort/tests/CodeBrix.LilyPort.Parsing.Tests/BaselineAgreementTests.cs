// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CodeBrix.LilyPort.Parsing.Grammar;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The reader, checked against what REAL BISON makes of the same grammar.
/// <para>
/// This is the fidelity anchor decision O7 rests on, and it is not decorative: it
/// caught the reader being wrong by exactly 17 on three separate counts the first time
/// it ran. Seventeen rules are written <c>... { action } %prec X</c>, and the reader
/// was treating those actions as MID-RULE actions because something followed them —
/// inventing 17 productions and 17 nonterminals that Bison does not have, each of them
/// a reduction point the real parser lacks.
/// </para>
/// <para>
/// Nothing else would have found it. The reader's own tests were green, the grammar
/// was structurally consistent, and every invented rule was individually plausible. It
/// took the real automaton to say the number was wrong.
/// </para>
/// <para>
/// The baseline is committed data under <c>tools/parser-baseline/</c>, generated once
/// by GNU Bison 3.8.2 at the v2.27.2 pin. Bison is not needed to build or test the
/// port.
/// </para>
/// </summary>
public class BaselineAgreementTests
{
    private static readonly BisonGrammar Grammar = BisonGrammarReader.ReadMirroredGrammar();

    private static readonly Dictionary<string, string> Facts = ReadFacts();

    private static int Fact(string name)
    {
        Facts.Should().ContainKey(name);
        return int.Parse(Facts[name], CultureInfo.InvariantCulture);
    }

    [Fact]
    public void the_reader_finds_the_productions_bison_finds()
    {
        //Arrange / Act / Assert
        // Bison counts its own rule 0 -- `$accept: start_symbol $end` -- which it adds
        // and the reader does not, so the comparison is against the excluding-accept
        // figure.
        Grammar.Rules.Count.Should().Be(Fact("productions-excluding-accept"));
    }

    [Fact]
    public void the_reader_finds_the_symbols_bison_finds()
    {
        //Arrange / Act / Assert
        // Terminals include Bison's built-in `error` and END_OF_FILE, which is its
        // $end. Nonterminals exclude $accept, which only Bison adds.
        Grammar.TerminalCount.Should().Be(Fact("terminals-including-end-and-error"));
        Grammar.NonterminalCount.Should().Be(Fact("nonterminals-excluding-accept"));
    }

    [Fact]
    public void the_reader_synthesizes_the_mid_rule_nonterminals_bison_does()
    {
        //Arrange
        // THE ONE THAT CAUGHT THE DEFECT. A mid-rule action is an action followed by
        // another SYMBOL; %prec is an annotation and does not count.
        int midRule = 0;
        int empty = 0;

        //Act
        foreach (GrammarRule rule in Grammar.Rules)
        {
            if (rule.IsMidRuleAction)
            {
                midRule++;
            }

            if (rule.RightHandSide.Count == 0)
            {
                empty++;
            }
        }

        //Assert
        midRule.Should().Be(Fact("mid-rule-nonterminals"));
        empty.Should().Be(Fact("empty-productions"));
    }

    [Fact]
    public void the_grammar_has_no_conflicts_and_the_generator_must_keep_it_that_way()
    {
        //Arrange / Act / Assert
        // Zero shift/reduce and zero reduce/reduce at this pin. When the in-repo table
        // construction lands, any conflict it reports is a defect in the generator, not
        // in the grammar -- and if a future upstream re-sync introduces a real one,
        // that is a change in what LilyPond accepts and has to be understood before
        // anything is ported.
        Fact("shift-reduce-conflicts").Should().Be(0);
        Fact("reduce-reduce-conflicts").Should().Be(0);

        // Recorded for the table construction to aim at.
        Fact("states").Should().Be(913);
    }

    [Fact]
    public void every_production_bison_numbered_is_one_the_reader_read()
    {
        //Arrange
        // The strongest available check short of building the tables: not just the
        // COUNT but the actual left-hand side of every production, in Bison's order.
        // A reader that mis-split one alternative would keep the count and change the
        // sequence.
        List<string> baseline = ReadBaselineLeftHandSides();

        //Act
        List<string> mine = new List<string>();
        foreach (GrammarRule rule in Grammar.Rules)
        {
            mine.Add(rule.LeftHandSide);
        }

        //Assert
        mine.Should().HaveCount(baseline.Count);

        List<string> mismatches = new List<string>();
        for (int i = 0; i < baseline.Count; i++)
        {
            if (!string.Equals(NormaliseAnonymous(mine[i]), NormaliseAnonymous(baseline[i]), StringComparison.Ordinal))
            {
                mismatches.Add(
                    "rule " + i.ToString(CultureInfo.InvariantCulture)
                    + ": reader '" + mine[i] + "' vs bison '" + baseline[i] + "'");
            }
        }

        mismatches.Should().BeEmpty();
    }

    /// <summary>
    /// Mid-rule nonterminals are numbered independently by each side — Bison counts
    /// them per enclosing rule, the reader counts them overall — so only the fact that
    /// BOTH are anonymous is compared, not the number.
    /// </summary>
    private static string NormaliseAnonymous(string name)
        => name.StartsWith("$@", StringComparison.Ordinal) ? "$@" : name;

    private static Dictionary<string, string> ReadFacts()
    {
        Dictionary<string, string> facts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string line in File.ReadAllLines(BaselinePath("automaton-facts.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                facts[parts[0]] = parts[1];
            }
        }

        return facts;
    }

    private static List<string> ReadBaselineLeftHandSides()
    {
        List<string> sides = new List<string>();

        foreach (string line in File.ReadAllLines(BaselinePath("productions.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }

            // Rule 0 is Bison's own $accept production, which the reader does not add.
            if (string.Equals(parts[0], "0", StringComparison.Ordinal))
            {
                continue;
            }

            sides.Add(parts[1]);
        }

        return sides;
    }

    /// <summary>
    /// Finds <c>tools/parser-baseline/</c> by walking up from the test assembly.
    /// <para>
    /// The baseline is committed REPOSITORY data, not a shipped resource: it exists so
    /// the generator can be checked against Bison, and nothing at runtime reads it.
    /// Embedding it would put a megabyte of report into every consumer's package.
    /// </para>
    /// </summary>
    private static string BaselinePath(string fileName)
    {
        string directory = AppContext.BaseDirectory;

        for (int level = 0; level < 8 && directory != null; level++)
        {
            string candidate = Path.Combine(directory, "tools", "parser-baseline", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new FileNotFoundException(
            "tools/parser-baseline/" + fileName + " was not found above " + AppContext.BaseDirectory);
    }
}
