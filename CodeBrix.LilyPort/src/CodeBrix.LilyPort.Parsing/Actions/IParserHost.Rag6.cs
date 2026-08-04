// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The members RULE ACTION GROUP 6 (core music assembly) added to the seam: the
/// lexer's mode predicates and word scan, the applying half of the
/// <c>START_MAKE_SYNTAX</c>/<c>FINISH_MAKE_SYNTAX</c> pair, and the chord-element
/// constructor.
/// </content>
public partial interface IParserHost
{
    /// <summary>
    /// Gets a value indicating whether the lexer is in note mode.
    /// <para>Upstream: <c>Lily_lexer::is_note_state</c> — the CURRENT start
    /// condition is <c>notes</c>, not merely somewhere on the stack.</para>
    /// </summary>
    bool IsNoteState { get; }

    /// <summary>
    /// Gets a value indicating whether the lexer is in lyric mode — which is what
    /// decides whether a non-music embedded Scheme markup is backed up as a
    /// <c>LYRIC_ELEMENT</c>.
    /// <para>Upstream: <c>Lily_lexer::is_lyric_state</c>.</para>
    /// </summary>
    bool IsLyricState { get; }

    /// <summary>
    /// Gets a value indicating whether the lexer is in chord mode.
    /// <para>Upstream: <c>Lily_lexer::is_chord_state</c>.</para>
    /// </summary>
    bool IsChordState { get; }

    /// <summary>
    /// Scans a symbol against the tables the current mode makes active: note names,
    /// drum names, chord modifiers.
    /// <para>Upstream: <c>Lily_lexer::scan_word (SCM &amp;output, SCM sym)</c>, whose
    /// token-number result and out-parameter fold into one
    /// <see cref="LexerLookup"/> — the same shape as
    /// <see cref="Lexing.ILexerHost.ScanWord"/>, which is the same table lookup
    /// reached from the scanner side of the seam.</para>
    /// </summary>
    /// <param name="word">The interned symbol to scan.</param>
    /// <returns>The token it names, or <see cref="LexerLookup.None"/>.</returns>
    LexerLookup ScanWord(object word);

    /// <summary>
    /// Applies a syntax constructor VALUE (the head of a list
    /// <see cref="SyntaxConstructor"/> was consed into) to an argument list, under a
    /// location.
    /// <para>Upstream: the application inside <c>FINISH_MAKE_SYNTAX</c> —
    /// <c>make_syntax (parser, Guile_user::apply, location, scm_car (start),
    /// args)</c>, Guile's <c>apply</c> spreading the argument list over the
    /// constructor with the location in effect. <see cref="MakeSyntax"/> covers the
    /// by-name calls; this member covers the by-value one.</para>
    /// </summary>
    /// <param name="constructor">The constructor procedure.</param>
    /// <param name="location">The span the constructed music is located at.</param>
    /// <param name="arguments">The argument list, a proper list.</param>
    /// <returns>The constructed value.</returns>
    object ApplySyntax(object constructor, SourceSpan location, object arguments);

    /// <summary>
    /// Builds the note events of a chord from its root, duration and modifications.
    /// <para>Upstream: <c>Lily::construct_chord_elements</c>
    /// (<c>lily/lily-imports.cc</c>), bound to <c>construct-chord-elements</c> in the
    /// vendored <c>scm/chord-entry.scm</c> — Scheme-side chord logic, so only the
    /// host can answer.</para>
    /// </summary>
    /// <param name="pitch">The chord's root pitch.</param>
    /// <param name="duration">The chord's duration.</param>
    /// <param name="modifications">The modification list.</param>
    /// <returns>The list of chord elements.</returns>
    object ConstructChordElements(object pitch, object duration, object modifications);
}
