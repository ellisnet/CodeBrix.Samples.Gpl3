// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;

namespace CodeBrix.LilyPort.Parsing.Lexing;

/// <summary>What a lookup answered, and with what value.</summary>
public readonly struct LexerLookup
{
    /// <summary>Initializes a successful lookup.</summary>
    /// <param name="tokenName">The terminal's name in the grammar.</param>
    /// <param name="value">The semantic value.</param>
    public LexerLookup(string tokenName, object value)
    {
        TokenName = tokenName;
        Value = value;
        FunctionSignature = null;
    }

    /// <summary>
    /// Initializes a successful lookup of a music, event or Scheme function, whose
    /// signature the scanner must announce as <c>EXPECT_*</c> tokens.
    /// </summary>
    /// <param name="tokenName">The terminal's name — <c>MUSIC_FUNCTION</c>,
    /// <c>EVENT_FUNCTION</c> or <c>SCM_FUNCTION</c>.</param>
    /// <param name="value">The semantic value: the function itself.</param>
    /// <param name="functionSignature">The function's signature, as
    /// <c>Music_function::get_signature</c> returns it — a list whose head is the
    /// return predicate and whose tail holds one entry per argument, each either the
    /// predicate or a <c>(predicate . default)</c> pair for an optional one.</param>
    public LexerLookup(string tokenName, object value, object functionSignature)
    {
        TokenName = tokenName;
        Value = value;
        FunctionSignature = functionSignature;
    }

    /// <summary>Gets the terminal's name, or null when the lookup failed.</summary>
    public string TokenName { get; }

    /// <summary>Gets the semantic value.</summary>
    public object Value { get; }

    /// <summary>
    /// Gets the function signature the scanner announces through
    /// <see cref="ModalScanner.PushFunctionSignature"/>, or null when the token is not
    /// a function.
    /// </summary>
    public object FunctionSignature { get; }

    /// <summary>Gets a value indicating whether the lookup found anything.</summary>
    public bool Found => TokenName != null;

    /// <summary>The failed lookup.</summary>
    public static LexerLookup None => default;
}

/// <summary>
/// The things the scanner cannot decide on its own.
/// <para>
/// LilyPond's lexer is not self-contained: what <c>c</c> means depends on the note-name
/// table, what <c>\foo</c> means depends on the keyword table and then on the
/// identifier the user defined, and what <c>#(...)</c> means depends on a Scheme
/// reader. Upstream reaches all of it through <c>Lily_parser</c> and Guile.
/// </para>
/// <para>
/// The port puts it behind this interface so that <c>CodeBrix.LilyPort.Parsing</c> does
/// not have to depend on whatever eventually plays the part of <c>Lily_parser</c> — the
/// same seam, and for the same reason, as <c>Context.ContextFactory</c> in the Engine.
/// </para>
/// </summary>
public interface ILexerHost
{
    /// <summary>
    /// Looks a bare word up in the tables the CURRENT MODE makes active: note names in
    /// <c>notes</c>, chord roots in <c>chords</c>, drum names in a drum context.
    /// </summary>
    /// <param name="state">The start condition the word was read in.</param>
    /// <param name="word">The word.</param>
    /// <returns>The token it names, or <see cref="LexerLookup.None"/>.</returns>
    LexerLookup ScanWord(LexerState state, string word);

    /// <summary>Looks a word up as a reserved word — <c>\score</c>, <c>\new</c> and the rest.</summary>
    /// <param name="word">The word, without its backslash.</param>
    /// <returns>The token it names, or <see cref="LexerLookup.None"/>.</returns>
    LexerLookup LookupKeyword(string word);

    /// <summary>Looks a word up as a user-defined or built-in identifier.</summary>
    /// <param name="word">The word, without its backslash.</param>
    /// <returns>The token it names, or <see cref="LexerLookup.None"/>.</returns>
    LexerLookup LookupIdentifier(string word);

    /// <summary>Looks a word up as a markup command.</summary>
    /// <param name="word">The word, without its backslash.</param>
    /// <param name="predicates">Receives the command's argument predicates.</param>
    /// <returns>The token it names, or <see cref="LexerLookup.None"/>.</returns>
    LexerLookup LookupMarkupCommand(string word, out IReadOnlyList<string> predicates);

    /// <summary>
    /// Reads one embedded Scheme expression starting at a position, for <c>#</c> and
    /// <c>$</c>.
    /// </summary>
    /// <param name="input">The whole input.</param>
    /// <param name="position">Where the expression starts, past the <c>#</c> or <c>$</c>.</param>
    /// <param name="consumed">Receives how many characters the expression occupied.</param>
    /// <returns>The value read.</returns>
    object ParseEmbeddedScheme(string input, int position, out int consumed);
}

/// <summary>
/// A host that answers "unknown" to everything needing the Scheme layer.
/// <para>
/// It lets the scanner and its rules be exercised on their own — which is where the
/// modal behaviour actually lives — without dragging in an interpreter. Every answer it
/// gives is a REFUSAL rather than a guess, so a test that needs a real table gets a
/// visible failure rather than a plausible wrong token.
/// </para>
/// </summary>
public sealed class UnresolvedLexerHost : ILexerHost
{
    /// <summary>Gets the words that were asked for and could not be answered.</summary>
    public List<string> Unresolved { get; } = new List<string>();

    /// <summary>Answers no note or chord table.</summary>
    /// <param name="state">The start condition.</param>
    /// <param name="word">The word.</param>
    /// <returns>Always <see cref="LexerLookup.None"/>.</returns>
    public LexerLookup ScanWord(LexerState state, string word)
    {
        Unresolved.Add(word);
        return LexerLookup.None;
    }

    /// <summary>Answers no keyword table.</summary>
    /// <param name="word">The word.</param>
    /// <returns>Always <see cref="LexerLookup.None"/>.</returns>
    public LexerLookup LookupKeyword(string word)
    {
        Unresolved.Add("\\" + word);
        return LexerLookup.None;
    }

    /// <summary>Answers no identifier table.</summary>
    /// <param name="word">The word.</param>
    /// <returns>Always <see cref="LexerLookup.None"/>.</returns>
    public LexerLookup LookupIdentifier(string word) => LexerLookup.None;

    /// <summary>Answers no markup command table.</summary>
    /// <param name="word">The word.</param>
    /// <param name="predicates">Receives an empty list.</param>
    /// <returns>Always <see cref="LexerLookup.None"/>.</returns>
    public LexerLookup LookupMarkupCommand(string word, out IReadOnlyList<string> predicates)
    {
        predicates = new List<string>();
        return LexerLookup.None;
    }

    /// <summary>
    /// Reads an embedded Scheme expression by BRACKET MATCHING only, and returns the
    /// text rather than a value.
    /// <para>
    /// Enough to keep the scanner in step with the input — which is all the scanner
    /// needs, since the value belongs to whoever evaluates it — and honest about not
    /// being a Scheme reader.
    /// </para>
    /// </summary>
    /// <param name="input">The whole input.</param>
    /// <param name="position">Where the expression starts.</param>
    /// <param name="consumed">Receives how many characters it occupied.</param>
    /// <returns>The expression's text.</returns>
    public object ParseEmbeddedScheme(string input, int position, out int consumed)
    {
        int i = position;
        while (i < input.Length && char.IsWhiteSpace(input[i]))
        {
            i++;
        }

        int start = i;

        if (i < input.Length && input[i] == '(')
        {
            int depth = 0;
            while (i < input.Length)
            {
                if (input[i] == '(')
                {
                    depth++;
                }
                else if (input[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        i++;
                        break;
                    }
                }
                else if (input[i] == '"')
                {
                    i++;
                    while (i < input.Length && input[i] != '"')
                    {
                        if (input[i] == '\\')
                        {
                            i++;
                        }

                        i++;
                    }
                }

                i++;
            }
        }
        else
        {
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
            {
                i++;
            }
        }

        consumed = i - position;
        return start < i ? input.Substring(start, i - start) : string.Empty;
    }
}
