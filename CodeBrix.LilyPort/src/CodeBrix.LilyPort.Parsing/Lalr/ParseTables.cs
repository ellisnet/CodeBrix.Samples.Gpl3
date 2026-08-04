// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.LilyPort.Parsing.Lalr;

/// <summary>What the parser does when it sees a terminal in a state.</summary>
public enum ActionKind
{
    /// <summary>No action: a syntax error.</summary>
    Error = 0,

    /// <summary>Push the terminal and move to another state.</summary>
    Shift,

    /// <summary>Pop the right-hand side and reduce by a rule.</summary>
    Reduce,

    /// <summary>The whole input has been recognised.</summary>
    Accept,
}

/// <summary>One entry in the action table.</summary>
public readonly struct ParseAction : IEquatable<ParseAction>
{
    /// <summary>Initializes an action.</summary>
    /// <param name="kind">What to do.</param>
    /// <param name="value">The target state for a shift, or the rule for a reduce.</param>
    public ParseAction(ActionKind kind, int value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>Gets what to do.</summary>
    public ActionKind Kind { get; }

    /// <summary>Gets the target state for a shift, or the rule number for a reduce.</summary>
    public int Value { get; }

    /// <summary>Gets the error action.</summary>
    public static ParseAction Error => new ParseAction(ActionKind.Error, 0);

    /// <summary>Determines whether two actions are the same.</summary>
    /// <param name="other">The other action.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    public bool Equals(ParseAction other) => Kind == other.Kind && Value == other.Value;

    /// <summary>Determines whether an object is an equal action.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> when it is an equal action.</returns>
    public override bool Equals(object obj) => obj is ParseAction other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Kind, Value);

    /// <summary>Returns the external representation.</summary>
    /// <returns>The action.</returns>
    public override string ToString()
        => Kind switch
        {
            ActionKind.Shift => "shift " + Value.ToString(CultureInfo.InvariantCulture),
            ActionKind.Reduce => "reduce " + Value.ToString(CultureInfo.InvariantCulture),
            ActionKind.Accept => "accept",
            _ => "error",
        };
}

/// <summary>
/// A grammar conflict, and how it was resolved.
/// <para>
/// The pinned grammar has NONE — Bison reports zero shift/reduce and zero
/// reduce/reduce at v2.27.2, which is recorded in the baseline. A conflict appearing
/// here therefore means either the generator is wrong or upstream changed what
/// LilyPond accepts, and both are worth stopping for.
/// </para>
/// </summary>
public sealed class LalrConflict
{
    /// <summary>Initializes a conflict record.</summary>
    /// <param name="state">The state it occurred in.</param>
    /// <param name="terminal">The lookahead terminal.</param>
    /// <param name="description">What the conflict was and how it was resolved.</param>
    public LalrConflict(int state, string terminal, string description)
    {
        State = state;
        Terminal = terminal;
        Description = description;
    }

    /// <summary>Gets the state the conflict occurred in.</summary>
    public int State { get; }

    /// <summary>Gets the lookahead terminal.</summary>
    public string Terminal { get; }

    /// <summary>Gets what the conflict was and how it was resolved.</summary>
    public string Description { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The conflict.</returns>
    public override string ToString()
        => "state " + State.ToString(CultureInfo.InvariantCulture) + " on " + Terminal + ": " + Description;
}

/// <summary>
/// One LR(0) item: a position within a production.
/// </summary>
public readonly struct LrItem : IEquatable<LrItem>
{
    /// <summary>Initializes an item.</summary>
    /// <param name="rule">The rule number in the augmented grammar.</param>
    /// <param name="dot">How many right-hand-side symbols are behind the dot.</param>
    public LrItem(int rule, int dot)
    {
        Rule = rule;
        Dot = dot;
    }

    /// <summary>Gets the rule number in the augmented grammar.</summary>
    public int Rule { get; }

    /// <summary>Gets how many right-hand-side symbols are behind the dot.</summary>
    public int Dot { get; }

    /// <summary>Determines whether two items are the same.</summary>
    /// <param name="other">The other item.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    public bool Equals(LrItem other) => Rule == other.Rule && Dot == other.Dot;

    /// <summary>Determines whether an object is an equal item.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> when it is an equal item.</returns>
    public override bool Equals(object obj) => obj is LrItem other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => (Rule * 397) ^ Dot;

    /// <summary>Returns the external representation.</summary>
    /// <returns>The item as <c>rule.dot</c>.</returns>
    public override string ToString()
        => Rule.ToString(CultureInfo.InvariantCulture) + "." + Dot.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One state of the LALR automaton.</summary>
public sealed class LalrState
{
    /// <summary>Initializes a state.</summary>
    /// <param name="index">The state number.</param>
    /// <param name="kernel">The kernel items.</param>
    public LalrState(int index, IReadOnlyList<LrItem> kernel)
    {
        Index = index;
        Kernel = kernel;
    }

    /// <summary>Gets the state number.</summary>
    public int Index { get; }

    /// <summary>Gets the kernel items: everything not produced by closure.</summary>
    public IReadOnlyList<LrItem> Kernel { get; }

    /// <summary>Gets the full item set, kernel and closure together.</summary>
    public IReadOnlyList<LrItem> Items { get; internal set; }

    /// <summary>Gets the transitions out of this state, keyed by symbol number.</summary>
    public Dictionary<int, int> Transitions { get; } = new Dictionary<int, int>();

    /// <summary>Gets the action for each terminal, keyed by terminal number.</summary>
    public Dictionary<int, ParseAction> Actions { get; } = new Dictionary<int, ParseAction>();

    /// <summary>
    /// Gets or sets the rule this state reduces by when no explicit action matches, or
    /// -1 when it has none. This is Bison's <c>$default</c>.
    /// <para>
    /// It is not only a table compression. A state with a default reduction NEVER
    /// reports an error: it reduces instead, and the error surfaces one or more
    /// reductions later, in whatever state the reduce lands in. That changes which
    /// <c>error</c> rule can recover, so a parser without it recovers differently from
    /// Bison's — which is how this was found, with `lilypond: lilypond error` unable to
    /// fire because the error was detected before `lilypond` had been reduced at all.
    /// </para>
    /// </summary>
    public int DefaultReduction { get; set; } = -1;

    /// <summary>Returns the external representation.</summary>
    /// <returns>The state number and its size.</returns>
    public override string ToString()
        => "State " + Index.ToString(CultureInfo.InvariantCulture)
           + " (" + Kernel.Count.ToString(CultureInfo.InvariantCulture) + " kernel items)";
}

/// <summary>
/// The finished LALR(1) tables: everything the driver needs to parse.
/// </summary>
public sealed class ParseTables
{
    /// <summary>Initializes the tables.</summary>
    /// <param name="symbols">Every symbol, indexed by symbol number.</param>
    /// <param name="terminalCount">How many of the symbols are terminals.</param>
    /// <param name="rules">The augmented grammar's rules.</param>
    /// <param name="states">The automaton's states.</param>
    /// <param name="conflicts">The conflicts found, if any.</param>
    public ParseTables(
        IReadOnlyList<string> symbols,
        int terminalCount,
        IReadOnlyList<TableRule> rules,
        IReadOnlyList<LalrState> states,
        IReadOnlyList<LalrConflict> conflicts)
    {
        Symbols = symbols;
        TerminalCount = terminalCount;
        Rules = rules;
        States = states;
        Conflicts = conflicts;
    }

    /// <summary>Gets every symbol's name, indexed by symbol number.</summary>
    public IReadOnlyList<string> Symbols { get; }

    /// <summary>Gets how many of the symbols are terminals; nonterminals follow them.</summary>
    public int TerminalCount { get; }

    /// <summary>Gets the augmented grammar's rules. Rule 0 is <c>$accept</c>.</summary>
    public IReadOnlyList<TableRule> Rules { get; }

    /// <summary>Gets the automaton's states. State 0 is the start state.</summary>
    public IReadOnlyList<LalrState> States { get; }

    /// <summary>Gets the conflicts found. The pinned grammar has none.</summary>
    public IReadOnlyList<LalrConflict> Conflicts { get; }

    /// <summary>Gets the number of shift/reduce conflicts.</summary>
    public int ShiftReduceConflicts => CountConflicts("shift/reduce");

    /// <summary>Gets the number of reduce/reduce conflicts.</summary>
    public int ReduceReduceConflicts => CountConflicts("reduce/reduce");

    /// <summary>Returns the external representation.</summary>
    /// <returns>A summary of the tables' size.</returns>
    public override string ToString()
        => string.Format(
            CultureInfo.InvariantCulture,
            "#<ParseTables {0} states, {1} rules, {2} symbols, {3} conflicts>",
            States.Count,
            Rules.Count,
            Symbols.Count,
            Conflicts.Count);

    private int CountConflicts(string kind)
    {
        int count = 0;
        foreach (LalrConflict conflict in Conflicts)
        {
            if (conflict.Description.StartsWith(kind, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>One rule in numeric form, as the tables and the driver use it.</summary>
public sealed class TableRule
{
    /// <summary>Initializes a rule.</summary>
    /// <param name="index">The rule number in the augmented grammar.</param>
    /// <param name="leftHandSide">The left-hand side's symbol number.</param>
    /// <param name="rightHandSide">The right-hand side's symbol numbers.</param>
    /// <param name="source">The grammar rule this came from, or null for <c>$accept</c>.</param>
    public TableRule(int index, int leftHandSide, int[] rightHandSide, Grammar.GrammarRule source)
    {
        Index = index;
        LeftHandSide = leftHandSide;
        RightHandSide = rightHandSide;
        Source = source;
    }

    /// <summary>Gets the rule number in the augmented grammar.</summary>
    public int Index { get; }

    /// <summary>Gets the left-hand side's symbol number.</summary>
    public int LeftHandSide { get; }

    /// <summary>Gets the right-hand side's symbol numbers.</summary>
    public int[] RightHandSide { get; }

    /// <summary>Gets how many symbols the reduce pops.</summary>
    public int Length => RightHandSide.Length;

    /// <summary>Gets the grammar rule this came from, or null for <c>$accept</c>.</summary>
    public Grammar.GrammarRule Source { get; }

    /// <summary>Gets or sets the precedence level this rule resolves conflicts with.</summary>
    public int? Precedence { get; internal set; }

    /// <summary>Gets or sets how that precedence level associates.</summary>
    public Grammar.Associativity Associativity { get; internal set; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The rule number and its source.</returns>
    public override string ToString()
        => Index.ToString(CultureInfo.InvariantCulture) + " " + (Source?.ToString() ?? "$accept");
}
