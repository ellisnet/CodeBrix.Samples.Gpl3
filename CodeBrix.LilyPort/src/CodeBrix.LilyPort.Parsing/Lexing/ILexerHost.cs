// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Driver;

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
/// One entry of a markup command's signature, as the scanner needs it.
/// <para>
/// Upstream's <c>Lily_lexer::push_markup_predicates</c> compares each predicate against
/// <c>Lily::markup_p</c> and <c>Lily::markup_list_p</c> BY IDENTITY to choose the token,
/// and puts THE PREDICATE ITSELF on an <c>EXPECT_SCM</c>, because the grammar's arglist
/// rules call it to decide whether the argument they just read is acceptable. Both halves
/// matter: an earlier pass carried only the name the identity comparison produced, so
/// every <c>EXPECT_SCM</c> arrived holding a string, and the first arglist rule to test an
/// argument tried to call it.
/// </para>
/// </summary>
public readonly struct MarkupPredicate
{
    /// <summary>Initializes a signature entry.</summary>
    /// <param name="name">The name the token choice is made from: <c>markup?</c>,
    /// <c>markup-list?</c>, or <c>scm?</c> for everything else.</param>
    /// <param name="value">The predicate procedure an <c>EXPECT_SCM</c> carries.</param>
    public MarkupPredicate(string name, object value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Gets the name the token choice is made from.</summary>
    public string Name { get; }

    /// <summary>Gets the predicate procedure the token carries.</summary>
    public object Value { get; }
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

    /// <summary>
    /// Stamps a music identifier's value with WHERE THE <c>\word</c> WAS WRITTEN.
    /// <para>
    /// Upstream does this in the lexer, not the grammar:
    /// <c>Lily_lexer::scan_escaped_word</c> and <c>::scan_shorthand</c> both open with
    /// <c>if (Music *m = unsmob&lt;Music&gt; (sid)) m-&gt;set_spot (override_input (here_input ()))</c>,
    /// and it is the ONLY thing that gives a music identifier a use-site origin — a
    /// bare <c>\glide</c> comes from <c>(make-music 'FingerGlideEvent)</c>, which sets
    /// none. Without it every use of one identifier carries the same (absent) origin,
    /// and code that tells two post-events apart by origin — <c>finger-key-glide</c>
    /// partitions the glide stream events that way — takes them all for the first one.
    /// Only MUSIC is stamped: upstream's test is <c>unsmob&lt;Music&gt;</c>, so a score,
    /// book or output definition bound to an identifier keeps the origin it was
    /// defined with.
    /// </para>
    /// </summary>
    /// <param name="value">The identifier's value; anything but music is left alone.</param>
    /// <param name="location">Where the word was written.</param>
    void SetMusicIdentifierSpot(object value, SourceSpan location);

    /// <summary>Looks a word up as a markup command.</summary>
    /// <param name="word">The word, without its backslash.</param>
    /// <param name="predicates">Receives the command's argument predicates.</param>
    /// <returns>The token it names, or <see cref="LexerLookup.None"/>.</returns>
    LexerLookup LookupMarkupCommand(string word, out IReadOnlyList<MarkupPredicate> predicates);

    /// <summary>
    /// Reads one embedded Scheme expression starting at a position, for <c>#</c> and
    /// <c>$</c>.
    /// <para>Upstream: <c>parse_embedded_scheme</c>, including its <c>scm_c_catch</c> —
    /// an expression the reader cannot make sense of is reported AT
    /// <paramref name="start"/> and answered with
    /// <see cref="CodeBrix.LilyScheme.Values.DefaultArgument"/>, which is upstream's
    /// <c>SCM_UNDEFINED</c>.</para>
    /// </summary>
    /// <param name="input">The whole input.</param>
    /// <param name="position">Where the expression starts, past the <c>#</c> or <c>$</c>.</param>
    /// <param name="start">That same position as a span, for diagnostics.</param>
    /// <param name="consumed">Receives how many characters the expression occupied.</param>
    /// <returns>The value read, or <c>DefaultArgument</c> when it could not be read.</returns>
    object ParseEmbeddedScheme(string input, int position, SourceSpan start, out int consumed);

    /// <summary>
    /// Evaluates what <see cref="ParseEmbeddedScheme"/> produced.
    /// <para>Upstream: <c>Lily_lexer::eval_scm</c>. The discriminator says how the extra
    /// values of a <c>#@</c> / <c>$@</c> form become extra tokens.</para>
    /// </summary>
    /// <param name="token">The datum the reader produced.</param>
    /// <param name="location">Where the expression began.</param>
    /// <param name="extraToken"><c>'#'</c> or <c>'$'</c>.</param>
    /// <returns>The value, or <see cref="CodeBrix.LilyScheme.Values.Unspecified"/> on failure.</returns>
    object EvalScheme(object token, SourceSpan location, char extraToken);

    /// <summary>
    /// Decides which token a VALUE lexes as, and hands back the value the token carries.
    /// <para>Upstream: <c>Lily_lexer::scan_scm_id</c>, reached from the <c>$</c> rule.
    /// This is the same classification <see cref="LookupIdentifier"/> applies to
    /// <c>\foo</c>'s value, which is why <c>$foo</c> and <c>\foo</c> are interchangeable
    /// wherever both are legal.</para>
    /// </summary>
    /// <param name="value">The evaluated value.</param>
    /// <returns>The token it names, or <see cref="LexerLookup.None"/>.</returns>
    LexerLookup ScanSchemeValue(object value);

    /// <summary>
    /// Answers whether a VALUE is a markup command, for <c>$</c> read in markup mode.
    /// <para>Upstream: the <c>YYSTATE == markup &amp;&amp; ly_is_procedure (sval)</c>
    /// branch of the <c>$</c> rule, which asks
    /// <c>Lily::markup_command_signature</c>.</para>
    /// </summary>
    /// <param name="value">The evaluated value.</param>
    /// <param name="predicates">Receives the command's argument predicates.</param>
    /// <returns>The token it names, or <see cref="LexerLookup.None"/>.</returns>
    LexerLookup MarkupFunctionToken(object value, out IReadOnlyList<MarkupPredicate> predicates);

    /// <summary>
    /// Gets or sets the lexer's error level, which a <c>#</c> the reader could not read
    /// raises (lexer.ll 412).
    /// </summary>
    int ErrorLevel { get; set; }
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

    /// <summary>Answers no identifier table, so there is never a value to stamp.</summary>
    /// <param name="value">The identifier's value.</param>
    /// <param name="location">Where the word was written.</param>
    public void SetMusicIdentifierSpot(object value, SourceSpan location)
    {
    }

    /// <summary>Answers no markup command table.</summary>
    /// <param name="word">The word.</param>
    /// <param name="predicates">Receives an empty list.</param>
    /// <returns>Always <see cref="LexerLookup.None"/>.</returns>
    public LexerLookup LookupMarkupCommand(string word, out IReadOnlyList<MarkupPredicate> predicates)
    {
        predicates = new List<MarkupPredicate>();
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
    /// <param name="start">That same position as a span; unused, since nothing here can fail.</param>
    /// <param name="consumed">Receives how many characters it occupied.</param>
    /// <returns>The expression's text.</returns>
    public object ParseEmbeddedScheme(string input, int position, SourceSpan start, out int consumed)
    {
        int i = position;
        while (i < input.Length && char.IsWhiteSpace(input[i]))
        {
            i++;
        }

        int begin = i;

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
        return begin < i ? input.Substring(begin, i - begin) : string.Empty;
    }

    /// <summary>Answers with the datum unchanged — there is nothing here to evaluate in.</summary>
    /// <param name="token">The datum.</param>
    /// <param name="location">Ignored.</param>
    /// <param name="extraToken">Ignored.</param>
    /// <returns>The datum.</returns>
    public object EvalScheme(object token, SourceSpan location, char extraToken) => token;

    /// <summary>Answers no classification, since that needs the type table.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Always <see cref="LexerLookup.None"/>.</returns>
    public LexerLookup ScanSchemeValue(object value) => LexerLookup.None;

    /// <summary>Answers no markup command table.</summary>
    /// <param name="value">The value.</param>
    /// <param name="predicates">Receives an empty list.</param>
    /// <returns>Always <see cref="LexerLookup.None"/>.</returns>
    public LexerLookup MarkupFunctionToken(object value, out IReadOnlyList<MarkupPredicate> predicates)
    {
        predicates = new List<MarkupPredicate>();
        return LexerLookup.None;
    }

    /// <summary>Gets or sets the error level a bad embedded expression would raise.</summary>
    public int ErrorLevel { get; set; }
}
