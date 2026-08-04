// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Grammar;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// The measured inventory of the vendored grammar, asserted with EQUALITY.
/// <para>
/// These are the numbers Track P's remaining work is scoped against — how many rule
/// actions have to be hand-ported, how many mid-rule actions the generator has to
/// synthesize, how many symbols the tables will be built over. They are asserted
/// rather than merely printed so that a re-sync cannot change the size of the job
/// without saying so.
/// </para>
/// </summary>
public class GrammarInventory
{
    private static readonly BisonGrammar Grammar = BisonGrammarReader.ReadMirroredGrammar();

    [Fact]
    public void the_measured_size_of_the_job()
    {
        //Arrange
        int withAction = 0;
        int midRule = 0;
        int empty = 0;

        //Act
        foreach (GrammarRule rule in Grammar.Rules)
        {
            if (rule.ActionText != null)
            {
                withAction++;
            }

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
        Grammar.Rules.Count.Should().Be(GrammarFigures.RuleCount);
        withAction.Should().Be(GrammarFigures.RulesWithActions);
        midRule.Should().Be(GrammarFigures.MidRuleActions);
        empty.Should().Be(GrammarFigures.EmptyRules);
        Grammar.TerminalCount.Should().Be(GrammarFigures.TerminalCount);
        Grammar.NonterminalCount.Should().Be(GrammarFigures.NonterminalCount);
    }
}

/// <summary>
/// The measured figures for the pinned v2.27.2 grammar. Kept in one place so a
/// deliberate re-sync updates them once.
/// </summary>
public static class GrammarFigures
{
    /// <summary>
    /// Productions, including the ones synthesized for mid-rule actions.
    /// <para>
    /// VERIFIED AGAINST REAL BISON at the v2.27.2 pin — see
    /// <c>tools/parser-baseline/</c>. Bison reports 616 productions plus its own
    /// rule 0 (<c>$accept: start_symbol $end</c>), which it adds and the reader does
    /// not; 616 is therefore the number to match.
    /// </para>
    /// <para>
    /// CORRECTION to a figure the O7 decision record carries. Master plan section 13
    /// says "the ~187 rules". That is the count of NONTERMINALS WITH RULES, not of
    /// productions: each <c>|</c> alternative is its own production. The larger figure
    /// is the one the generator works over and the one the hand-porting effort is
    /// scoped against.
    /// </para>
    /// </summary>
    public const int RuleCount = 616;

    /// <summary>
    /// Productions carrying an action body to hand-port.
    /// <para>
    /// Unchanged by the <c>%prec</c> correction, and that is the point: the seventeen
    /// actions still exist, they simply belong to the rule they were written in rather
    /// than to a synthesized mid-rule one. The correction moved them; it did not remove
    /// any.
    /// </para>
    /// </summary>
    public const int RulesWithActions = 479;

    /// <summary>
    /// Mid-rule actions, each of which becomes an anonymous empty rule.
    /// <para>
    /// Bison's automaton names exactly fifteen <c>$@n</c> nonterminals. The reader
    /// first reported 32, because seventeen rules are written
    /// <c>... { action } %prec X</c> and <c>%prec</c> is an ANNOTATION, not a symbol —
    /// so the action is a final action, not a mid-rule one. Getting that wrong invents
    /// seventeen productions and seventeen nonterminals that Bison does not have.
    /// </para>
    /// </summary>
    public const int MidRuleActions = 15;

    /// <summary>
    /// Productions with an empty right-hand side — the ones Bison prints as
    /// <c>ε</c>. Thirty-nine, of which 15 are the synthesized mid-rule rules and 24
    /// are genuine empty alternatives written in the grammar. Verified against the
    /// baseline automaton.
    /// </summary>
    public const int EmptyRules = 39;

    /// <summary>
    /// Terminals: declared tokens, inferred character literals, Bison's built-in
    /// <c>error</c>, and <c>END_OF_FILE</c> (which is Bison's <c>$end</c>). Matches
    /// the baseline automaton exactly.
    /// </summary>
    public const int TerminalCount = 130;

    /// <summary>
    /// Nonterminals, including the 15 synthesized mid-rule ones. Bison reports 205
    /// including its own <c>$accept</c>, which the reader does not add.
    /// </summary>
    public const int NonterminalCount = 204;
}
