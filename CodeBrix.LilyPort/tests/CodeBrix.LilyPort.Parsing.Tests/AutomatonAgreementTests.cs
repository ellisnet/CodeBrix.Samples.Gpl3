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
using System.Text;
using CodeBrix.LilyPort.Parsing.Lalr;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The in-repo LALR generator, checked against the automaton REAL BISON built from the
/// same grammar.
/// <para>
/// This is what decision O7 rests on. The port constructs its own tables so that an
/// upstream re-sync needs no external toolchain — and the only thing that makes that
/// safe is proving, once, that the construction agrees with Bison's. Not approximately
/// and not by state count: item set by item set.
/// </para>
/// <para>
/// State NUMBERING is deliberately not compared. Bison's numbers come from its own
/// exploration order, which is an artifact rather than a fact about the grammar. What
/// is compared is the SET of item sets, which is numbering-independent and is the
/// thing that decides how the parser behaves.
/// </para>
/// </summary>
public class AutomatonAgreementTests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static readonly BisonReport Baseline = BisonReport.Read();

    [Fact]
    public void the_generator_builds_the_same_number_of_states_bison_does()
    {
        //Arrange / Act / Assert
        // LALR has exactly the states LR(0) has, so this is a real check on the item
        // set construction rather than on the lookahead pass.
        Tables.States.Should().HaveCount(Baseline.States.Count);
        Tables.States.Should().HaveCount(913);
    }

    [Fact]
    public void every_item_set_bison_built_is_one_the_generator_built()
    {
        //Arrange
        // THE CHECK THAT MATTERS. Two automata with the same number of states can still
        // disagree about what is in them; comparing the item sets as sets rules that
        // out without depending on either side's state numbering.
        HashSet<string> mine = new HashSet<string>(StringComparer.Ordinal);
        foreach (LalrState state in Tables.States)
        {
            mine.Add(Canonical(state));
        }

        HashSet<string> theirs = new HashSet<string>(Baseline.States, StringComparer.Ordinal);

        //Act
        List<string> missing = new List<string>();
        foreach (string state in theirs)
        {
            if (!mine.Contains(state))
            {
                missing.Add(state);
            }
        }

        List<string> extra = new List<string>();
        foreach (string state in mine)
        {
            if (!theirs.Contains(state))
            {
                extra.Add(state);
            }
        }

        //Assert
        missing.Should().BeEmpty();
        extra.Should().BeEmpty();
    }

    [Fact]
    public void the_grammar_generates_no_conflicts()
    {
        //Arrange / Act / Assert
        // Bison reports zero shift/reduce and zero reduce/reduce at this pin. A
        // conflict here therefore means the generator is wrong, or an upstream re-sync
        // changed what LilyPond accepts -- and both are worth stopping for.
        Tables.Conflicts.Should().BeEmpty();
        Tables.ShiftReduceConflicts.Should().Be(0);
        Tables.ReduceReduceConflicts.Should().Be(0);
    }

    [Fact]
    public void the_default_reductions_fall_exactly_where_bisons_do()
    {
        //Arrange
        // Bison's $default is a behaviour, not a compression: a state with one never
        // reports an error, it reduces and lets the error surface later. So WHERE the
        // defaults are decides where errors are detected and which error rule can
        // recover.
        //
        // Measured from the baseline report: 703 of the 913 states carry a $default,
        // and the 13 states that can shift `error` carry none -- if they did, recovery
        // could never fire in them.
        int withDefault = 0;
        int shiftsError = 0;
        int shiftsErrorAndDefaults = 0;

        int errorSymbol = -1;
        for (int i = 0; i < Tables.TerminalCount; i++)
        {
            if (string.Equals(Tables.Symbols[i], "error", StringComparison.Ordinal))
            {
                errorSymbol = i;
            }
        }

        //Act
        foreach (LalrState state in Tables.States)
        {
            bool hasDefault = state.DefaultReduction >= 0;
            bool canShiftError = errorSymbol >= 0
                && state.Actions.TryGetValue(errorSymbol, out ParseAction onError)
                && onError.Kind == ActionKind.Shift;

            if (hasDefault)
            {
                withDefault++;
            }

            if (canShiftError)
            {
                shiftsError++;
                if (hasDefault)
                {
                    shiftsErrorAndDefaults++;
                }
            }
        }

        //Assert
        withDefault.Should().Be(703);
        shiftsError.Should().Be(13);
        shiftsErrorAndDefaults.Should().Be(0);
    }

    [Fact]
    public void the_augmented_grammar_matches_bisons_numbering()
    {
        //Arrange / Act / Assert
        // Rule 0 is $accept: start_symbol $end, exactly as Bison numbers it, so every
        // other rule sits at the reader's index plus one. That alignment is what lets
        // the item sets above be compared by rule number at all.
        Tables.Rules.Should().HaveCount(617);
        Tables.Rules[0].Source.Should().BeNull();
        Tables.Rules[0].Length.Should().Be(2);
        Tables.Rules[1].Source.LeftHandSide.Should().Be("start_symbol");
    }

    [Fact]
    public void the_start_state_accepts_after_the_start_symbol()
    {
        //Arrange / Act
        // A sanity check on the table rather than the automaton: from state 0, reading
        // a start_symbol has to reach a state whose action on end-of-input is ACCEPT.
        int startSymbol = IndexOf("start_symbol");
        Tables.States[0].Transitions.Should().ContainKey(startSymbol);

        LalrState afterStart = Tables.States[Tables.States[0].Transitions[startSymbol]];

        //Assert
        afterStart.Actions.Should().ContainKey(0);
        afterStart.Actions[0].Kind.Should().Be(ActionKind.Accept);
    }

    [Fact]
    public void end_of_input_is_symbol_zero_because_the_grammar_declares_it_so()
    {
        //Arrange / Act / Assert
        // %token END_OF_FILE 0 means END_OF_FILE IS Bison's $end. Numbering it anywhere
        // else would leave the augmented rule ending on an ordinary token and the
        // parser never accepting.
        Tables.Symbols[0].Should().Be("END_OF_FILE");
    }

    private static int IndexOf(string symbol)
    {
        for (int i = 0; i < Tables.Symbols.Count; i++)
        {
            if (string.Equals(Tables.Symbols[i], symbol, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException("no symbol named " + symbol);
    }

    /// <summary>
    /// Renders a state's full item set as a canonical string: rule number and dot
    /// position for every item, sorted. Bison's rule numbering and the generator's
    /// agree because both put <c>$accept</c> at rule 0.
    /// </summary>
    private static string Canonical(LalrState state)
    {
        List<string> items = new List<string>();
        foreach (LrItem item in state.Items)
        {
            items.Add(
                item.Rule.ToString(CultureInfo.InvariantCulture)
                + "." + item.Dot.ToString(CultureInfo.InvariantCulture));
        }

        items.Sort(StringComparer.Ordinal);
        return string.Join(" ", items);
    }

    /// <summary>Reads the item sets out of the committed Bison report.</summary>
    private sealed class BisonReport
    {
        private BisonReport(List<string> states) => States = states;

        internal List<string> States { get; }

        internal static BisonReport Read()
        {
            string[] lines = File.ReadAllLines(BaselinePath("parser.output"));

            List<string> states = new List<string>();
            List<string> current = null;

            foreach (string raw in lines)
            {
                if (raw.StartsWith("State ", StringComparison.Ordinal))
                {
                    Flush();
                    current = new List<string>();
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                // An item line is "   <rule> <lhs>: <rhs with the dot>" or a
                // continuation "   <rule>      | <rhs>". Anything else -- the blank
                // lines, the action lines, the goto lines -- ends the item block.
                string line = raw.TrimEnd();
                if (line.Length == 0)
                {
                    continue;
                }

                (int rule, int dot, bool ok) = ParseItem(line);
                if (ok)
                {
                    current.Add(
                        rule.ToString(CultureInfo.InvariantCulture)
                        + "." + dot.ToString(CultureInfo.InvariantCulture));
                }
            }

            Flush();
            return new BisonReport(states);

            void Flush()
            {
                if (current != null && current.Count > 0)
                {
                    current.Sort(StringComparer.Ordinal);
                    states.Add(string.Join(" ", current));
                }
            }
        }

        /// <summary>
        /// Parses one item line into a rule number and a dot position.
        /// <para>
        /// The scanning is QUOTE-AWARE throughout, and that is not fussiness. The
        /// obvious implementation finds the lookahead list with
        /// <c>body.IndexOf('[')</c> — and this grammar has a rule
        /// <c>br_bass_figure: '[' bass_figure</c>, so that bracket lives inside a
        /// character literal. Truncating there loses the dot and silently mis-reads
        /// every state containing the rule. It cost a real debugging session, chasing
        /// a difference that was in the baseline reader rather than in the generator
        /// it was checking.
        /// </para>
        /// <para>
        /// The dot is a U+2022 BULLET in Bison 3.8's report, and the epsilon it prints
        /// for an empty right-hand side is not a symbol.
        /// </para>
        /// </summary>
        private static (int Rule, int Dot, bool Ok) ParseItem(string line)
        {
            string text = line.TrimStart();

            int space = text.IndexOf(' ');
            if (space <= 0 || !int.TryParse(
                    text.Substring(0, space),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int rule))
            {
                return (0, 0, false);
            }

            string body = text.Substring(space + 1);

            // Drop the "lhs:" or "|" that introduces the production body. Both are
            // outside any quoting, so a plain scan to the first one is safe.
            int introducer = IntroducerEnd(body);
            if (introducer > 0)
            {
                body = body.Substring(introducer);
            }

            int count = 0;
            int dot = -1;
            int i = 0;

            while (i < body.Length)
            {
                char c = body[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '[')
                {
                    // The lookahead list, and the end of the item.
                    break;
                }

                if (c == '•')
                {
                    dot = count;
                    i++;
                    continue;
                }

                if (c == 'ε')
                {
                    // Bison's epsilon for an empty right-hand side is not a symbol.
                    i++;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < body.Length && body[i] != quote)
                    {
                        if (body[i] == '\\')
                        {
                            i++;
                        }

                        i++;
                    }

                    i++;
                }
                else
                {
                    while (i < body.Length && !char.IsWhiteSpace(body[i]))
                    {
                        i++;
                    }
                }

                count++;
            }

            return dot < 0 ? (0, 0, false) : (rule, dot, true);
        }

        /// <summary>
        /// Returns where the production body starts, past the <c>lhs:</c> or <c>|</c>
        /// that introduces it, or zero when there is neither.
        /// </summary>
        private static int IntroducerEnd(string body)
        {
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];

                if (c == ':' || c == '|')
                {
                    return i + 1;
                }

                // A quote means the body has begun, so there was no introducer.
                if (c == '"' || c == '\'' || c == '•')
                {
                    return 0;
                }
            }

            return 0;
        }

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

            throw new FileNotFoundException("tools/parser-baseline/" + fileName + " was not found.");
        }
    }
}
