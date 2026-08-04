// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;

namespace CodeBrix.LilyPort.Parsing.Lexing;

/// <summary>
/// Which of <c>lexer.ll</c>'s start conditions the port has rules for, and which it
/// does not.
/// <para>
/// The same fence pattern as <c>Bootstrap/TypePredicates.cs</c> in the Engine and
/// <c>EngraveResult.MissingTranslators</c> in the facade, and for the same reason: a
/// scanner that runs without producing the tokens a mode needs looks like a working
/// scanner. Naming the gap is what stops "the lexer exists" being read as "the lexer is
/// done".
/// </para>
/// <para>
/// A state on <see cref="Ported"/> means its rules are ported — not that every token it
/// can produce is, which is finer-grained work tracked by the tests for each mode as
/// they land.
/// </para>
/// </summary>
public static class LexerCoverage
{
    /// <summary>
    /// The start conditions whose rules are ported.
    /// <para>
    /// The mode machinery: what opens and closes a comment, what pushes and pops the
    /// three string-reading conditions, and the whitespace handling shared across
    /// modes. Everything else depends on the scanner being in the right state first,
    /// which is why this came before the token rules.
    /// </para>
    /// </summary>
    public static IReadOnlyList<LexerState> Ported { get; } = new[]
    {
        LexerState.Initial,
        LexerState.Chords,
        LexerState.Figures,
        LexerState.Include,
        LexerState.Lyrics,
        LexerState.LongComment,
        LexerState.MainInput,
        LexerState.Markup,
        LexerState.Notes,
        LexerState.Quote,
        LexerState.CommandQuote,
        LexerState.SourceFileLine,
        LexerState.SourceFileName,
        LexerState.Version,
    };

    /// <summary>
    /// The start conditions whose token-producing rules are NOT ported yet.
    /// <para>
    /// These are the modes that make LilyPond's syntax what it is — the same characters
    /// meaning different things in each — so each is its own piece of work rather than
    /// a variation on the last.
    /// </para>
    /// </summary>
    public static IReadOnlyList<LexerState> NotYetPorted { get; } = new LexerState[0];

    /// <summary>
    /// What the scanner still delegates rather than deciding, and to whom.
    /// <para>
    /// Every start condition's RULES are ported. What is not in this assembly is the
    /// DATA those rules consult — the note-name and drum tables, the keyword and
    /// identifier tables, the markup-command signatures, and a Scheme reader. Upstream
    /// reaches all of it through <c>Lily_parser</c> and Guile; the port reaches it
    /// through <see cref="ILexerHost"/>, so that the parsing assembly does not have to
    /// depend on whatever plays that part.
    /// </para>
    /// <para>
    /// Naming them keeps "every mode is ported" from being read as "the lexer is
    /// finished": with <see cref="UnresolvedLexerHost"/> the modes run and produce
    /// tokens, but a note name comes out as a SYMBOL rather than as a pitch.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> DelegatedToHost { get; } = new[]
    {
        "note-name, drum-name and chord-root tables (ScanWord)",
        "the reserved-word table (LookupKeyword)",
        "the user identifier table (LookupIdentifier)",
        "markup command signatures (LookupMarkupCommand)",
        "the embedded Scheme reader (ParseEmbeddedScheme)",
    };
}
