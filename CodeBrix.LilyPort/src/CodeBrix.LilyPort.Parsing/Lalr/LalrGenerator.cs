// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CodeBrix.LilyPort.Parsing.Grammar;

namespace CodeBrix.LilyPort.Parsing.Lalr;

/// <summary>
/// Builds the LALR(1) parse tables from the vendored grammar.
/// <para>
/// This is the second half of decision O7: the grammar source is mirrored, and the
/// tables are constructed HERE rather than by an external Bison run. That is what
/// makes an upstream re-sync a copy-and-diff instead of a toolchain dependency.
/// </para>
/// <para>
/// The construction is the textbook one, and deliberately so — it has to agree with
/// Bison, not improve on it. LR(0) item sets for the states, then LALR lookaheads by
/// spontaneous generation and propagation over the KERNEL items only (Aho/Sethi/Ullman
/// algorithm 4.63), then conflict resolution by the same precedence rules Bison uses.
/// The committed baseline under <c>tools/parser-baseline/</c> is what proves the
/// agreement, item set by item set.
/// </para>
/// </summary>
public sealed class LalrGenerator
{
    /// <summary>The dummy lookahead used to detect propagation, written <c>#</c> upstream.</summary>
    private const int PropagationMarker = -1;

    private readonly BisonGrammar _grammar;

    private readonly List<string> _symbolNames = new List<string>();
    private readonly Dictionary<string, int> _symbolNumbers = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly List<TableRule> _rules = new List<TableRule>();
    private readonly List<LalrState> _states = new List<LalrState>();
    private readonly List<LalrConflict> _conflicts = new List<LalrConflict>();

    private int _terminalCount;
    private int _endSymbol;
    private int _acceptSymbol;

    private bool[] _nullable;
    private HashSet<int>[] _first;
    private List<int>[] _rulesByLeftHandSide;

    // Lookahead sets, indexed [state][kernel item index].
    private HashSet<int>[][] _lookaheads;

    /// <summary>Initializes a generator over a grammar.</summary>
    /// <param name="grammar">The grammar to build tables for.</param>
    public LalrGenerator(BisonGrammar grammar)
        => _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));

    /// <summary>Builds the tables for the grammar mirrored into this assembly.</summary>
    /// <returns>The tables.</returns>
    public static ParseTables GenerateFromMirror()
        => new LalrGenerator(BisonGrammarReader.ReadMirroredGrammar()).Generate();

    /// <summary>Builds the LALR(1) tables.</summary>
    /// <returns>The tables.</returns>
    public ParseTables Generate()
    {
        NumberSymbols();
        BuildRules();
        ComputeNullableAndFirst();
        BuildLr0Automaton();
        ComputeLookaheads();
        BuildActions();
        PruneUnreachableStates();
        ChooseDefaultReductions();

        return new ParseTables(_symbolNames, _terminalCount, _rules, _states, _conflicts);
    }

    /// <summary>
    /// Numbers the symbols: terminals first, then nonterminals.
    /// <para>
    /// <c>END_OF_FILE</c> is Bison's <c>$end</c> — that is what
    /// <c>%token END_OF_FILE 0</c> means — so it takes terminal number 0 and the
    /// augmented rule ends with it, exactly as Bison's rule 0 does.
    /// </para>
    /// </summary>
    private void NumberSymbols()
    {
        List<GrammarSymbol> terminals = new List<GrammarSymbol>();
        List<GrammarSymbol> nonterminals = new List<GrammarSymbol>();
        GrammarSymbol end = null;

        foreach (GrammarSymbol symbol in _grammar.Symbols)
        {
            if (!symbol.IsTerminal)
            {
                nonterminals.Add(symbol);
                continue;
            }

            if (symbol.DeclaredNumber == 0)
            {
                end = symbol;
            }
            else
            {
                terminals.Add(symbol);
            }
        }

        if (end == null)
        {
            throw new InvalidOperationException(
                "The grammar declares no end-of-input token (%token NAME 0), so the augmented"
                + " rule cannot be built.");
        }

        Add(end.Name);
        _endSymbol = 0;

        foreach (GrammarSymbol symbol in terminals)
        {
            Add(symbol.Name);
        }

        _terminalCount = _symbolNames.Count;

        _acceptSymbol = Add("$accept");
        foreach (GrammarSymbol symbol in nonterminals)
        {
            Add(symbol.Name);
        }

        int Add(string name)
        {
            int number = _symbolNames.Count;
            _symbolNames.Add(name);
            _symbolNumbers[name] = number;
            return number;
        }
    }

    /// <summary>
    /// Builds the augmented rule list. Rule 0 is <c>$accept: start_symbol $end</c>, which
    /// is Bison's rule 0 and the reason every other rule here is one higher than the
    /// reader's index.
    /// </summary>
    private void BuildRules()
    {
        string start = _grammar.StartSymbol
            ?? throw new InvalidOperationException("The grammar has no rules, so it has no start symbol.");

        _rules.Add(new TableRule(
            0,
            _acceptSymbol,
            new[] { _symbolNumbers[start], _endSymbol },
            null));

        foreach (GrammarRule rule in _grammar.Rules)
        {
            int[] rightHandSide = new int[rule.RightHandSide.Count];
            for (int i = 0; i < rightHandSide.Length; i++)
            {
                rightHandSide[i] = _symbolNumbers[rule.RightHandSide[i]];
            }

            TableRule tableRule = new TableRule(
                _rules.Count,
                _symbolNumbers[rule.LeftHandSide],
                rightHandSide,
                rule);

            AssignRulePrecedence(tableRule, rule);
            _rules.Add(tableRule);
        }

        _rulesByLeftHandSide = new List<int>[_symbolNames.Count];
        foreach (TableRule rule in _rules)
        {
            (_rulesByLeftHandSide[rule.LeftHandSide] ??= new List<int>()).Add(rule.Index);
        }
    }

    /// <summary>
    /// Gives a rule the precedence it resolves conflicts with: the <c>%prec</c> symbol's
    /// when there is one, otherwise its LAST terminal's. That fallback is Bison's, and
    /// it is why <c>%prec</c> exists at all — a rule whose last terminal is the wrong
    /// one needs to say so.
    /// </summary>
    private void AssignRulePrecedence(TableRule tableRule, GrammarRule rule)
    {
        GrammarSymbol source = null;

        if (rule.PrecedenceSymbol != null)
        {
            source = _grammar.Find(rule.PrecedenceSymbol);
        }
        else
        {
            for (int i = tableRule.RightHandSide.Length - 1; i >= 0; i--)
            {
                int symbol = tableRule.RightHandSide[i];
                if (symbol < _terminalCount)
                {
                    source = _grammar.Find(_symbolNames[symbol]);
                    break;
                }
            }
        }

        if (source?.Precedence != null)
        {
            tableRule.Precedence = source.Precedence;
            tableRule.Associativity = source.Associativity;
        }
    }

    private void ComputeNullableAndFirst()
    {
        int count = _symbolNames.Count;
        _nullable = new bool[count];
        _first = new HashSet<int>[count];

        for (int i = 0; i < count; i++)
        {
            _first[i] = new HashSet<int>();
            if (i < _terminalCount)
            {
                _first[i].Add(i);
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;

            foreach (TableRule rule in _rules)
            {
                int lhs = rule.LeftHandSide;

                bool allNullable = true;
                foreach (int symbol in rule.RightHandSide)
                {
                    foreach (int terminal in _first[symbol])
                    {
                        changed |= _first[lhs].Add(terminal);
                    }

                    if (!_nullable[symbol])
                    {
                        allNullable = false;
                        break;
                    }
                }

                if (allNullable && !_nullable[lhs])
                {
                    _nullable[lhs] = true;
                    changed = true;
                }
            }
        }
    }

    /// <summary>
    /// Returns FIRST of a right-hand-side suffix followed by a lookahead, which is what
    /// the LR(1) closure needs.
    /// </summary>
    private HashSet<int> FirstOfSuffix(int[] symbols, int start, int lookahead)
    {
        HashSet<int> result = new HashSet<int>();

        for (int i = start; i < symbols.Length; i++)
        {
            foreach (int terminal in _first[symbols[i]])
            {
                result.Add(terminal);
            }

            if (!_nullable[symbols[i]])
            {
                return result;
            }
        }

        result.Add(lookahead);
        return result;
    }

    /// <summary>Returns the closure of an LR(0) item set.</summary>
    private List<LrItem> Closure(IReadOnlyList<LrItem> kernel)
    {
        List<LrItem> items = new List<LrItem>(kernel);
        HashSet<LrItem> seen = new HashSet<LrItem>(kernel);
        HashSet<int> expanded = new HashSet<int>();

        for (int i = 0; i < items.Count; i++)
        {
            LrItem item = items[i];
            TableRule rule = _rules[item.Rule];
            if (item.Dot >= rule.RightHandSide.Length)
            {
                continue;
            }

            int next = rule.RightHandSide[item.Dot];
            if (next < _terminalCount || !expanded.Add(next))
            {
                continue;
            }

            List<int> production = _rulesByLeftHandSide[next];
            if (production == null)
            {
                continue;
            }

            foreach (int number in production)
            {
                LrItem candidate = new LrItem(number, 0);
                if (seen.Add(candidate))
                {
                    items.Add(candidate);
                }
            }
        }

        return items;
    }

    /// <summary>
    /// Builds the LR(0) automaton by breadth-first exploration from the start state.
    /// <para>
    /// LALR has exactly the same states as LR(0) — that is the whole point of it — so
    /// this is also the state count the baseline is checked against.
    /// </para>
    /// </summary>
    private void BuildLr0Automaton()
    {
        Dictionary<string, int> byKernel = new Dictionary<string, int>(StringComparer.Ordinal);

        int start = AddState(new List<LrItem> { new LrItem(0, 0) });

        for (int index = start; index < _states.Count; index++)
        {
            LalrState state = _states[index];

            // Group items by the symbol after the dot, in first-seen order so that the
            // automaton is built deterministically.
            List<int> order = new List<int>();
            Dictionary<int, List<LrItem>> moved = new Dictionary<int, List<LrItem>>();

            foreach (LrItem item in state.Items)
            {
                TableRule rule = _rules[item.Rule];
                if (item.Dot >= rule.RightHandSide.Length)
                {
                    continue;
                }

                int symbol = rule.RightHandSide[item.Dot];
                if (!moved.TryGetValue(symbol, out List<LrItem> target))
                {
                    target = new List<LrItem>();
                    moved[symbol] = target;
                    order.Add(symbol);
                }

                target.Add(new LrItem(item.Rule, item.Dot + 1));
            }

            foreach (int symbol in order)
            {
                state.Transitions[symbol] = AddState(moved[symbol]);
            }
        }

        int AddState(List<LrItem> kernel)
        {
            kernel.Sort(CompareItems);

            string key = KernelKey(kernel);
            if (byKernel.TryGetValue(key, out int existing))
            {
                return existing;
            }

            LalrState state = new LalrState(_states.Count, kernel);
            state.Items = Closure(kernel);
            _states.Add(state);
            byKernel[key] = state.Index;
            return state.Index;
        }
    }

    private static int CompareItems(LrItem a, LrItem b)
        => a.Rule != b.Rule ? a.Rule.CompareTo(b.Rule) : a.Dot.CompareTo(b.Dot);

    private static string KernelKey(IReadOnlyList<LrItem> kernel)
    {
        StringBuilder key = new StringBuilder();
        foreach (LrItem item in kernel)
        {
            key.Append(item.Rule);
            key.Append('.');
            key.Append(item.Dot);
            key.Append(';');
        }

        return key.ToString();
    }

    /// <summary>
    /// Computes the LALR(1) lookahead sets.
    /// <para>
    /// The standard two-phase construction: for each kernel item, run an LR(1) closure
    /// with a dummy lookahead. A real terminal arriving on a successor item was
    /// generated SPONTANEOUSLY; the dummy arriving means the successor's lookaheads
    /// PROPAGATE from this item. Then push the spontaneous sets along the propagation
    /// links until nothing changes.
    /// </para>
    /// <para>
    /// Doing it over kernels only is what keeps LALR the size of LR(0) rather than the
    /// size of canonical LR(1) — for this grammar, 913 states instead of many thousands.
    /// </para>
    /// </summary>
    private void ComputeLookaheads()
    {
        _lookaheads = new HashSet<int>[_states.Count][];
        for (int i = 0; i < _states.Count; i++)
        {
            _lookaheads[i] = new HashSet<int>[_states[i].Kernel.Count];
            for (int k = 0; k < _lookaheads[i].Length; k++)
            {
                _lookaheads[i][k] = new HashSet<int>();
            }
        }

        // $accept: . start_symbol $end sees end-of-input, and nothing else does by fiat.
        _lookaheads[0][0].Add(_endSymbol);

        List<(int FromState, int FromItem, int ToState, int ToItem)> propagation
            = new List<(int, int, int, int)>();

        for (int stateIndex = 0; stateIndex < _states.Count; stateIndex++)
        {
            LalrState state = _states[stateIndex];

            for (int kernelIndex = 0; kernelIndex < state.Kernel.Count; kernelIndex++)
            {
                LrItem kernelItem = state.Kernel[kernelIndex];

                foreach ((LrItem item, int lookahead) in Lr1Closure(kernelItem, PropagationMarker))
                {
                    TableRule rule = _rules[item.Rule];
                    if (item.Dot >= rule.RightHandSide.Length)
                    {
                        continue;
                    }

                    int symbol = rule.RightHandSide[item.Dot];
                    if (!state.Transitions.TryGetValue(symbol, out int target))
                    {
                        continue;
                    }

                    int targetItem = IndexOfKernel(target, new LrItem(item.Rule, item.Dot + 1));
                    if (targetItem < 0)
                    {
                        continue;
                    }

                    if (lookahead == PropagationMarker)
                    {
                        propagation.Add((stateIndex, kernelIndex, target, targetItem));
                    }
                    else
                    {
                        _lookaheads[target][targetItem].Add(lookahead);
                    }
                }
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach ((int fromState, int fromItem, int toState, int toItem) in propagation)
            {
                foreach (int lookahead in _lookaheads[fromState][fromItem])
                {
                    changed |= _lookaheads[toState][toItem].Add(lookahead);
                }
            }
        }
    }

    private int IndexOfKernel(int state, LrItem item)
    {
        IReadOnlyList<LrItem> kernel = _states[state].Kernel;
        for (int i = 0; i < kernel.Count; i++)
        {
            if (kernel[i].Equals(item))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns the LR(1) closure of a single item with a single lookahead.</summary>
    private List<(LrItem Item, int Lookahead)> Lr1Closure(LrItem seed, int lookahead)
    {
        List<(LrItem, int)> items = new List<(LrItem, int)> { (seed, lookahead) };
        HashSet<(int, int, int)> seen = new HashSet<(int, int, int)> { (seed.Rule, seed.Dot, lookahead) };

        for (int i = 0; i < items.Count; i++)
        {
            (LrItem item, int look) = items[i];
            TableRule rule = _rules[item.Rule];
            if (item.Dot >= rule.RightHandSide.Length)
            {
                continue;
            }

            int next = rule.RightHandSide[item.Dot];
            if (next < _terminalCount)
            {
                continue;
            }

            List<int> production = _rulesByLeftHandSide[next];
            if (production == null)
            {
                continue;
            }

            HashSet<int> lookaheads = FirstOfSuffix(rule.RightHandSide, item.Dot + 1, look);

            foreach (int number in production)
            {
                foreach (int candidate in lookaheads)
                {
                    if (seen.Add((number, 0, candidate)))
                    {
                        items.Add((new LrItem(number, 0), candidate));
                    }
                }
            }
        }

        return items;
    }

    /// <summary>
    /// Fills the action table, resolving conflicts by Bison's rules.
    /// </summary>
    private void BuildActions()
    {
        for (int stateIndex = 0; stateIndex < _states.Count; stateIndex++)
        {
            LalrState state = _states[stateIndex];

            // Shifts first, so that a later reduce meets an existing entry and the
            // conflict is resolved once, in one place.
            foreach (KeyValuePair<int, int> transition in state.Transitions)
            {
                if (transition.Key < _terminalCount)
                {
                    state.Actions[transition.Key] = new ParseAction(ActionKind.Shift, transition.Value);
                }
            }

            for (int kernelIndex = 0; kernelIndex < state.Kernel.Count; kernelIndex++)
            {
                LrItem item = state.Kernel[kernelIndex];
                TableRule rule = _rules[item.Rule];

                if (item.Dot < rule.RightHandSide.Length)
                {
                    continue;
                }

                foreach (int terminal in _lookaheads[stateIndex][kernelIndex])
                {
                    AddReduce(state, terminal, rule);
                }
            }

            // An empty production reduces without ever being a kernel item anywhere it
            // is used, so its reduce comes from the closure instead.
            foreach (LrItem item in state.Items)
            {
                TableRule rule = _rules[item.Rule];
                if (item.Dot != 0 || rule.RightHandSide.Length != 0)
                {
                    continue;
                }

                foreach (int terminal in EmptyRuleLookaheads(state, rule))
                {
                    AddReduce(state, terminal, rule);
                }
            }

            // Accepting is the shift of $end in the state that has seen the start
            // symbol -- Bison's rule 0 made $end part of the grammar, so this is a
            // rewrite of that transition rather than a special case in the driver.
            if (state.Actions.TryGetValue(_endSymbol, out ParseAction action)
                && action.Kind == ActionKind.Shift
                && HasAcceptItem(state))
            {
                state.Actions[_endSymbol] = new ParseAction(ActionKind.Accept, 0);
            }
        }
    }

    /// <summary>
    /// Drops the states nothing can reach any more, and renumbers what is left.
    /// <para>
    /// This is not tidying — it is required to agree with Bison, and the reason is
    /// worth keeping. Precedence resolution DELETES shift actions: when
    /// <c>%left '-'</c> makes <c>script_dir: '-' •</c> reduce rather than shift,
    /// the <c>'-'</c> transition out of that state goes with it. Bison builds its
    /// automaton, resolves, and then removes whatever became unreachable. A generator
    /// that resolves conflicts but keeps the states produces the same PARSER — the
    /// extra states cannot be entered — with a different state count, and then nothing
    /// it says about itself can be checked against the baseline.
    /// </para>
    /// <para>
    /// For the pinned grammar this is worth exactly two states, both downstream of the
    /// one <c>'-'</c> resolution above, plus the two more they alone led to.
    /// </para>
    /// </summary>
    private void PruneUnreachableStates()
    {
        // A terminal transition whose action is no longer a shift is gone: the action
        // table is the authority, and the transition table has to say the same thing.
        foreach (LalrState state in _states)
        {
            List<int> dead = new List<int>();
            foreach (KeyValuePair<int, int> transition in state.Transitions)
            {
                if (transition.Key >= _terminalCount)
                {
                    continue;
                }

                if (!state.Actions.TryGetValue(transition.Key, out ParseAction action)
                    || (action.Kind != ActionKind.Shift && action.Kind != ActionKind.Accept))
                {
                    dead.Add(transition.Key);
                }
            }

            foreach (int symbol in dead)
            {
                state.Transitions.Remove(symbol);
            }
        }

        bool[] reachable = new bool[_states.Count];
        Queue<int> pending = new Queue<int>();
        reachable[0] = true;
        pending.Enqueue(0);

        while (pending.Count > 0)
        {
            foreach (int target in _states[pending.Dequeue()].Transitions.Values)
            {
                if (!reachable[target])
                {
                    reachable[target] = true;
                    pending.Enqueue(target);
                }
            }
        }

        int[] renumbered = new int[_states.Count];
        List<LalrState> kept = new List<LalrState>();

        for (int i = 0; i < _states.Count; i++)
        {
            renumbered[i] = reachable[i] ? kept.Count : -1;
            if (reachable[i])
            {
                kept.Add(_states[i]);
            }
        }

        if (kept.Count == _states.Count)
        {
            return;
        }

        List<LalrState> rebuilt = new List<LalrState>(kept.Count);
        foreach (LalrState state in kept)
        {
            LalrState moved = new LalrState(renumbered[state.Index], state.Kernel)
            {
                Items = state.Items,
            };

            foreach (KeyValuePair<int, int> transition in state.Transitions)
            {
                moved.Transitions[transition.Key] = renumbered[transition.Value];
            }

            foreach (KeyValuePair<int, ParseAction> action in state.Actions)
            {
                moved.Actions[action.Key] = action.Value.Kind == ActionKind.Shift
                    ? new ParseAction(ActionKind.Shift, renumbered[action.Value.Value])
                    : action.Value;
            }

            rebuilt.Add(moved);
        }

        _states.Clear();
        _states.AddRange(rebuilt);
    }

    /// <summary>
    /// Gives each state its <c>$default</c> reduction: the rule it reduces by when no
    /// explicit action matches.
    /// <para>
    /// Bison picks the reduction covering the most lookaheads, so this does too. Ties
    /// go to the earlier rule, matching the same preference reduce/reduce resolution
    /// uses. States with no reduction keep none, and an ACCEPT is never defaulted over.
    /// </para>
    /// </summary>
    private void ChooseDefaultReductions()
    {
        int errorSymbol = ErrorSymbolNumber();

        foreach (LalrState state in _states)
        {
            // A state that can shift `error` gets NO default reduction, and this is not
            // an optimisation -- it is what makes recovery possible. A default reduction
            // never reports an error, so a state that defaulted could never reach its
            // own error rule. Bison does exactly this: of its 913 states, the 13 that
            // shift `error` are precisely the ones with no $default.
            if (errorSymbol >= 0
                && state.Actions.TryGetValue(errorSymbol, out ParseAction onError)
                && onError.Kind == ActionKind.Shift)
            {
                continue;
            }

            Dictionary<int, int> counts = new Dictionary<int, int>();

            foreach (KeyValuePair<int, ParseAction> entry in state.Actions)
            {
                if (entry.Value.Kind != ActionKind.Reduce)
                {
                    continue;
                }

                counts.TryGetValue(entry.Value.Value, out int count);
                counts[entry.Value.Value] = count + 1;
            }

            if (counts.Count == 0)
            {
                continue;
            }

            int best = -1;
            int bestCount = -1;
            foreach (KeyValuePair<int, int> entry in counts)
            {
                if (entry.Value > bestCount || (entry.Value == bestCount && entry.Key < best))
                {
                    best = entry.Key;
                    bestCount = entry.Value;
                }
            }

            state.DefaultReduction = best;

            List<int> redundant = new List<int>();
            foreach (KeyValuePair<int, ParseAction> entry in state.Actions)
            {
                if (entry.Value.Kind == ActionKind.Reduce && entry.Value.Value == best)
                {
                    redundant.Add(entry.Key);
                }
            }

            foreach (int terminal in redundant)
            {
                state.Actions.Remove(terminal);
            }
        }
    }

    private int ErrorSymbolNumber()
    {
        for (int i = 0; i < _terminalCount; i++)
        {
            if (string.Equals(_symbolNames[i], "error", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private bool HasAcceptItem(LalrState state)
    {
        foreach (LrItem item in state.Items)
        {
            if (item.Rule == 0 && item.Dot == 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the lookaheads an empty production reduces on in a state.
    /// <para>
    /// An empty rule's item is <c>A: . </c> with the dot already at the end, so it is
    /// never a kernel item and the propagation pass above never sees it. Its lookaheads
    /// are those of whatever asked for an <c>A</c> here: FIRST of what follows the
    /// <c>A</c> in every item that has the dot before one.
    /// </para>
    /// </summary>
    private HashSet<int> EmptyRuleLookaheads(LalrState state, TableRule empty)
    {
        HashSet<int> result = new HashSet<int>();

        for (int kernelIndex = 0; kernelIndex < state.Kernel.Count; kernelIndex++)
        {
            SeedFrom(state.Kernel[kernelIndex], _lookaheads[state.Index][kernelIndex]);
        }

        // State 0 has no kernel beyond the augmented item, so seed from the closure too.
        if (state.Index == 0)
        {
            SeedFrom(new LrItem(0, 0), _lookaheads[0][0]);
        }

        return result;

        void SeedFrom(LrItem seed, HashSet<int> seedLookaheads)
        {
            foreach (int seedLookahead in seedLookaheads)
            {
                foreach ((LrItem item, int lookahead) in Lr1Closure(seed, seedLookahead))
                {
                    if (item.Rule == empty.Index && item.Dot == 0)
                    {
                        result.Add(lookahead);
                    }
                }
            }
        }
    }

    private void AddReduce(LalrState state, int terminal, TableRule rule)
    {
        ParseAction reduce = new ParseAction(ActionKind.Reduce, rule.Index);

        if (!state.Actions.TryGetValue(terminal, out ParseAction existing))
        {
            state.Actions[terminal] = reduce;
            return;
        }

        if (existing.Equals(reduce))
        {
            return;
        }

        if (existing.Kind == ActionKind.Shift)
        {
            ResolveShiftReduce(state, terminal, rule, existing);
            return;
        }

        if (existing.Kind == ActionKind.Reduce)
        {
            // Bison keeps the EARLIER rule and reports the conflict. Rule order in the
            // file is therefore load bearing, which is why the reader preserves it.
            int keep = Math.Min(existing.Value, rule.Index);
            state.Actions[terminal] = new ParseAction(ActionKind.Reduce, keep);

            _conflicts.Add(new LalrConflict(
                state.Index,
                _symbolNames[terminal],
                "reduce/reduce between rules "
                + existing.Value.ToString(CultureInfo.InvariantCulture) + " and "
                + rule.Index.ToString(CultureInfo.InvariantCulture)
                + "; kept " + keep.ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>
    /// Resolves a shift/reduce conflict exactly as Bison does.
    /// <para>
    /// Both sides need a precedence for the resolution to be silent. With both: the
    /// higher level wins, and on a tie the associativity decides — left reduces, right
    /// shifts, and <c>%nonassoc</c> makes the input an ERROR, which is how
    /// <c>a &lt; b &lt; c</c> gets rejected rather than parsed. Without both, the shift
    /// wins and the conflict is REPORTED, because that default is a guess.
    /// </para>
    /// </summary>
    private void ResolveShiftReduce(LalrState state, int terminal, TableRule rule, ParseAction shift)
    {
        GrammarSymbol lookahead = _grammar.Find(_symbolNames[terminal]);

        int? terminalPrecedence = lookahead?.Precedence;
        int? rulePrecedence = rule.Precedence;

        if (terminalPrecedence == null || rulePrecedence == null)
        {
            _conflicts.Add(new LalrConflict(
                state.Index,
                _symbolNames[terminal],
                "shift/reduce with rule " + rule.Index.ToString(CultureInfo.InvariantCulture)
                + "; no precedence on both sides, so the shift was kept"));
            return;
        }

        if (rulePrecedence > terminalPrecedence)
        {
            state.Actions[terminal] = new ParseAction(ActionKind.Reduce, rule.Index);
            return;
        }

        if (rulePrecedence < terminalPrecedence)
        {
            state.Actions[terminal] = shift;
            return;
        }

        switch (lookahead.Associativity)
        {
            case Associativity.Left:
                state.Actions[terminal] = new ParseAction(ActionKind.Reduce, rule.Index);
                break;

            case Associativity.Right:
                state.Actions[terminal] = shift;
                break;

            default:
                // %nonassoc: neither, and that is the point of declaring it. The entry
                // becomes an EXPLICIT error rather than an absent one, so that the
                // state's default reduction cannot quietly take it over -- `a < b < c`
                // has to be rejected, which is the only reason %nonassoc was written.
                state.Actions[terminal] = ParseAction.Error;
                break;
        }
    }
}
