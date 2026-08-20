// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Ly.Slexing;

/// <summary>
/// One token class's matching rule: what upstream keeps as CLASS ATTRIBUTES on a
/// Token subclass (<c>rx</c>, <c>test_match</c>, and the constructor).
/// <para>
/// New-in-family plumbing, not a port of a Python class: Python reads these off the
/// class object through its metaclass machinery; C# classes cannot be introspected
/// that way without reflection, so every token class declares one
/// <c>internal static readonly TokenRule Rule</c> naming its pattern, its factory,
/// and (rarely) its <c>test_match</c> predicate. A subclass that INHERITS its rx
/// upstream (e.g. <c>SequentialStart(OpenBracket)</c>) declares its own rule over
/// the same pattern constant, which is exactly the meaning inheritance had.
/// </para>
/// </summary>
public sealed class TokenRule
{
    private readonly Func<string> _pattern;

    private TokenRule(
        Func<string> pattern,
        Type tokenClass,
        Func<string, int, Token> create,
        Func<Match, bool> testMatch)
    {
        _pattern = pattern;
        TokenClass = tokenClass;
        Create = create;
        TestMatch = testMatch;
    }

    /// <summary>Gets the exact token class this rule instantiates — what Python
    /// reads as the class object itself, needed for the follow test's
    /// <c>type(token) not in self.items</c>.</summary>
    public Type TokenClass { get; }

    /// <summary>Gets the factory that builds the token from (text, pos).</summary>
    public Func<string, int, Token> Create { get; }

    /// <summary>
    /// Gets the predicate deciding whether a match really is this class, or
    /// <see langword="null"/> when any match is (upstream's default
    /// <c>test_match</c> returns True). Only consulted when several classes in one
    /// parser's items share the same rx, upstream's own rule.
    /// </summary>
    public Func<Match, bool> TestMatch { get; }

    /// <summary>
    /// Gets the regular expression fragment. Lazy because upstream's
    /// <c>patternproperty</c> rx's compose word lists that must not be touched at
    /// type-initialization time.
    /// </summary>
    public string Pattern => _pattern();

    /// <summary>Makes a rule over a fixed pattern.</summary>
    /// <typeparam name="TToken">The exact token class the factory builds.</typeparam>
    /// <param name="pattern">The regular expression fragment.</param>
    /// <param name="create">The token factory.</param>
    /// <returns>The rule.</returns>
    public static TokenRule Of<TToken>(string pattern, Func<string, int, Token> create)
        where TToken : Token
        => new TokenRule(() => pattern, typeof(TToken), create, null);

    /// <summary>
    /// Makes a pattern-less rule: a factory for a token class upstream instantiates
    /// WITHOUT an rx — a parser's <c>default</c> class, never listed in items.
    /// </summary>
    /// <typeparam name="TToken">The exact token class the factory builds.</typeparam>
    /// <param name="create">The token factory.</param>
    /// <returns>The rule.</returns>
    public static TokenRule Factory<TToken>(Func<string, int, Token> create)
        where TToken : Token
        => new TokenRule(
            () => throw new InvalidOperationException(
                typeof(TToken).Name + " has no pattern; it is a default-token factory"),
            typeof(TToken), create, null);

    /// <summary>Makes a rule over a fixed pattern with a <c>test_match</c>.</summary>
    /// <typeparam name="TToken">The exact token class the factory builds.</typeparam>
    /// <param name="pattern">The regular expression fragment.</param>
    /// <param name="create">The token factory.</param>
    /// <param name="testMatch">The predicate deciding whether a match is this class.</param>
    /// <returns>The rule.</returns>
    public static TokenRule Of<TToken>(
        string pattern, Func<string, int, Token> create, Func<Match, bool> testMatch)
        where TToken : Token
        => new TokenRule(() => pattern, typeof(TToken), create, testMatch);

    /// <summary>Makes a rule whose pattern is computed lazily, once —
    /// upstream's <c>patternproperty</c>.</summary>
    /// <typeparam name="TToken">The exact token class the factory builds.</typeparam>
    /// <param name="pattern">The pattern provider, invoked once.</param>
    /// <param name="create">The token factory.</param>
    /// <returns>The rule.</returns>
    public static TokenRule Lazy<TToken>(Func<string> pattern, Func<string, int, Token> create)
        where TToken : Token
    {
        var lazy = new Lazy<string>(pattern);
        return new TokenRule(() => lazy.Value, typeof(TToken), create, null);
    }

    /// <summary>Makes a lazily-patterned rule with a <c>test_match</c>.</summary>
    /// <typeparam name="TToken">The exact token class the factory builds.</typeparam>
    /// <param name="pattern">The pattern provider, invoked once.</param>
    /// <param name="create">The token factory.</param>
    /// <param name="testMatch">The predicate deciding whether a match is this class.</param>
    /// <returns>The rule.</returns>
    public static TokenRule Lazy<TToken>(
        Func<string> pattern, Func<string, int, Token> create, Func<Match, bool> testMatch)
        where TToken : Token
    {
        var lazy = new Lazy<string>(pattern);
        return new TokenRule(() => lazy.Value, typeof(TToken), create, testMatch);
    }
}
