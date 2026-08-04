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

namespace CodeBrix.LilyPort.Parsing.Grammar;

/// <summary>What a grammar symbol is.</summary>
public enum SymbolKind
{
    /// <summary>A terminal declared with <c>%token</c>.</summary>
    Token,

    /// <summary>A terminal written as a character literal, such as <c>'{'</c>.</summary>
    CharacterLiteral,

    /// <summary>A nonterminal: something with rules of its own.</summary>
    Nonterminal,
}

/// <summary>How a precedence level associates.</summary>
public enum Associativity
{
    /// <summary>Declared with <c>%nonassoc</c>: repeating the operator is an error.</summary>
    None,

    /// <summary>Declared with <c>%left</c>.</summary>
    Left,

    /// <summary>Declared with <c>%right</c>.</summary>
    Right,
}

/// <summary>One symbol in the grammar.</summary>
public sealed class GrammarSymbol
{
    /// <summary>Initializes a symbol.</summary>
    /// <param name="name">The name as it appears in the grammar.</param>
    /// <param name="kind">What kind of symbol it is.</param>
    public GrammarSymbol(string name, SymbolKind kind)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind;
    }

    /// <summary>Gets the name as written in the grammar.</summary>
    public string Name { get; }

    /// <summary>Gets what kind of symbol this is.</summary>
    public SymbolKind Kind { get; internal set; }

    /// <summary>
    /// Gets the display alias from <c>%token NAME "alias"</c>, or null.
    /// <para>
    /// Upstream's comment calls this "an alias that will be used to display the syntax
    /// error", and it is why LilyPond's parse errors say <c>\accepts</c> rather than
    /// <c>ACCEPTS</c>. It carries no grammatical meaning.
    /// </para>
    /// </summary>
    public string Alias { get; internal set; }

    /// <summary>
    /// Gets the explicit token number from <c>%token NAME 0 "alias"</c>, or null.
    /// Only <c>END_OF_FILE</c> uses one, and it must be zero.
    /// </summary>
    public int? DeclaredNumber { get; internal set; }

    /// <summary>Gets the precedence level, or null when the symbol has none.</summary>
    public int? Precedence { get; internal set; }

    /// <summary>Gets how the precedence level associates.</summary>
    public Associativity Associativity { get; internal set; }

    /// <summary>Gets a value indicating whether this symbol is a terminal.</summary>
    public bool IsTerminal => Kind != SymbolKind.Nonterminal;

    /// <summary>Returns the external representation.</summary>
    /// <returns>The symbol's name and kind.</returns>
    public override string ToString() => Name + " (" + Kind + ")";
}

/// <summary>
/// One production: a left-hand side, a sequence of right-hand-side symbols, and the
/// action body that runs when it reduces.
/// </summary>
public sealed class GrammarRule
{
    /// <summary>Initializes a rule.</summary>
    /// <param name="index">The rule's position in the grammar, counting from zero.</param>
    /// <param name="leftHandSide">The nonterminal being defined.</param>
    /// <param name="rightHandSide">The symbols on the right, in order.</param>
    public GrammarRule(int index, string leftHandSide, IReadOnlyList<string> rightHandSide)
    {
        Index = index;
        LeftHandSide = leftHandSide ?? throw new ArgumentNullException(nameof(leftHandSide));
        RightHandSide = rightHandSide ?? throw new ArgumentNullException(nameof(rightHandSide));
    }

    /// <summary>Gets the rule's position in the grammar, counting from zero.</summary>
    public int Index { get; }

    /// <summary>Gets the nonterminal this rule defines.</summary>
    public string LeftHandSide { get; }

    /// <summary>Gets the symbols on the right-hand side, in order.</summary>
    public IReadOnlyList<string> RightHandSide { get; }

    /// <summary>
    /// Gets the action body as OPAQUE TEXT, exactly as written, or null when the rule
    /// has none.
    /// <para>
    /// Deliberately not parsed. The action is C++ and is hand-ported; the reader's job
    /// is to know that a rule HAS one and to key it so the port can be matched against
    /// it, not to understand it.
    /// </para>
    /// </summary>
    public string ActionText { get; internal set; }

    /// <summary>Gets the line in <c>parser.yy</c> the rule starts on.</summary>
    public int Line { get; internal set; }

    /// <summary>
    /// Gets the symbol whose precedence this rule takes, from <c>%prec</c>, or null to
    /// use the rule's last terminal.
    /// </summary>
    public string PrecedenceSymbol { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether this rule was SYNTHESIZED for a mid-rule action.
    /// <para>
    /// Bison rewrites <c>a: B { action } C</c> into <c>a: B $@1 C</c> plus an empty
    /// rule <c>$@1: /*empty*/ { action }</c>, which is what makes the action run before
    /// <c>C</c> is read. The rewrite changes the grammar — the synthesized empty rule
    /// is a real reduction point and can create conflicts — so the reader reproduces
    /// it rather than hiding it.
    /// </para>
    /// </summary>
    public bool IsMidRuleAction { get; internal set; }

    /// <summary>
    /// Gets a stable identity for this rule, independent of its index.
    /// <para>
    /// Rule indices shift the moment anything is inserted above them, so the hand-ported
    /// actions are keyed on THIS instead: the production written out, plus an ordinal
    /// when the same production appears more than once.
    /// </para>
    /// </summary>
    public string Identity { get; internal set; }

    /// <summary>Returns the production in Bison-like notation.</summary>
    /// <returns>The production.</returns>
    public override string ToString()
    {
        StringBuilder text = new StringBuilder();
        text.Append(LeftHandSide);
        text.Append(':');

        if (RightHandSide.Count == 0)
        {
            text.Append(" /* empty */");
        }
        else
        {
            foreach (string symbol in RightHandSide)
            {
                text.Append(' ');
                text.Append(symbol);
            }
        }

        if (PrecedenceSymbol != null)
        {
            text.Append(" %prec ");
            text.Append(PrecedenceSymbol);
        }

        return text.ToString();
    }
}

/// <summary>
/// A whole Bison grammar as the generator needs it: the symbols, their precedence,
/// and the productions with their action bodies.
/// </summary>
public sealed class BisonGrammar
{
    private readonly Dictionary<string, GrammarSymbol> _symbols
        = new Dictionary<string, GrammarSymbol>(StringComparer.Ordinal);

    private readonly List<GrammarSymbol> _order = new List<GrammarSymbol>();
    private readonly List<GrammarRule> _rules = new List<GrammarRule>();

    /// <summary>Gets the symbols, in declaration order.</summary>
    public IReadOnlyList<GrammarSymbol> Symbols => _order;

    /// <summary>Gets the productions, in the order they appear.</summary>
    public IReadOnlyList<GrammarRule> Rules => _rules;

    /// <summary>Gets the start symbol: the left-hand side of the first rule.</summary>
    public string StartSymbol => _rules.Count > 0 ? _rules[0].LeftHandSide : null;

    /// <summary>Gets the <c>%define</c> settings, keyed by variable.</summary>
    public Dictionary<string, string> Defines { get; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the <c>%parse-param</c> declarations, verbatim.</summary>
    public List<string> ParseParameters { get; } = new List<string>();

    /// <summary>Gets the <c>%lex-param</c> declarations, verbatim.</summary>
    public List<string> LexParameters { get; } = new List<string>();

    /// <summary>Gets a value indicating whether <c>%locations</c> was declared.</summary>
    public bool HasLocations { get; internal set; }

    /// <summary>Gets a value indicating whether <c>%debug</c> was declared.</summary>
    public bool HasDebug { get; internal set; }

    /// <summary>Gets how many distinct precedence levels were declared.</summary>
    public int PrecedenceLevelCount { get; internal set; }

    /// <summary>Gets the number of terminals.</summary>
    public int TerminalCount
    {
        get
        {
            int count = 0;
            foreach (GrammarSymbol symbol in _order)
            {
                if (symbol.IsTerminal)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets the number of nonterminals.</summary>
    public int NonterminalCount => _order.Count - TerminalCount;

    /// <summary>Returns a symbol by name, or null.</summary>
    /// <param name="name">The symbol name.</param>
    /// <returns>The symbol.</returns>
    public GrammarSymbol Find(string name)
        => name != null && _symbols.TryGetValue(name, out GrammarSymbol symbol) ? symbol : null;

    /// <summary>Returns the rules that define a nonterminal.</summary>
    /// <param name="nonterminal">The nonterminal name.</param>
    /// <returns>The rules.</returns>
    public IReadOnlyList<GrammarRule> RulesFor(string nonterminal)
    {
        List<GrammarRule> rules = new List<GrammarRule>();
        foreach (GrammarRule rule in _rules)
        {
            if (string.Equals(rule.LeftHandSide, nonterminal, StringComparison.Ordinal))
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    /// <summary>Returns a one-line summary of the grammar's size.</summary>
    /// <returns>The summary.</returns>
    public override string ToString()
        => string.Format(
            CultureInfo.InvariantCulture,
            "#<BisonGrammar {0} rules, {1} terminals, {2} nonterminals, {3} precedence levels>",
            _rules.Count,
            TerminalCount,
            NonterminalCount,
            PrecedenceLevelCount);

    internal GrammarSymbol Declare(string name, SymbolKind kind)
    {
        if (_symbols.TryGetValue(name, out GrammarSymbol existing))
        {
            // A name first seen on a right-hand side is assumed to be a nonterminal and
            // is corrected the moment a %token declaration or a rule proves otherwise.
            if (existing.Kind == SymbolKind.Nonterminal && kind != SymbolKind.Nonterminal)
            {
                existing.Kind = kind;
            }

            return existing;
        }

        GrammarSymbol symbol = new GrammarSymbol(name, kind);
        _symbols[name] = symbol;
        _order.Add(symbol);
        return symbol;
    }

    internal void AddRule(GrammarRule rule) => _rules.Add(rule);
}

/// <summary>
/// Raised when the grammar uses a Bison feature the in-repo generator does not
/// support.
/// <para>
/// This is deliberately FATAL rather than a warning. The whole point of generating the
/// tables in-repo is that an upstream re-sync fails loudly at sync time instead of
/// mis-parsing quietly afterwards, and that only works if an unknown declaration stops
/// the build.
/// </para>
/// </summary>
public sealed class UnsupportedBisonFeatureException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="feature">The declaration or construct that is not supported.</param>
    /// <param name="line">The line in the grammar it appeared on.</param>
    public UnsupportedBisonFeatureException(string feature, int line)
        : base(BuildMessage(feature, line))
    {
        Feature = feature;
        Line = line;
    }

    /// <summary>Gets the unsupported declaration or construct.</summary>
    public string Feature { get; }

    /// <summary>Gets the line it appeared on.</summary>
    public int Line { get; }

    private static string BuildMessage(string feature, int line)
        => "parser.yy uses a Bison feature this generator does not support: '"
           + feature + "' at line " + line.ToString(CultureInfo.InvariantCulture)
           + ". Add support for it in BisonGrammarReader rather than ignoring it —"
           + " a silently skipped declaration changes the language the parser accepts.";
}
