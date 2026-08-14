// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The members the LyricMode group (mode changes and lyric mode) added: the rest
/// of the lexer-mode pushes beside <see cref="IParserHost.PushNoteState"/>, the
/// chord-modifier table hand-off, and the lyric-state query. This group is where
/// the PARSER drives the lexer's mode switching, so a REAL host must forward every
/// one of these onto the running <see cref="Lexing.ModalScanner"/> — a host that
/// merely records them leaves the scanner lexing in the wrong mode.
/// </content>
public partial interface IParserHost
{
    /// <summary>
    /// Puts the lexer into lyric mode, stacking the current mode.
    /// <para>Upstream: <c>Lily_lexer::push_lyric_state</c>.</para>
    /// </summary>
    void PushLyricState();

    /// <summary>
    /// Puts the lexer into drum mode, stacking the current mode. Upstream this is
    /// the NOTES start condition with the pitch-name table swapped for
    /// <c>drumPitchNames</c>, which is why the real host owns the table swap.
    /// <para>Upstream: <c>Lily_lexer::push_drum_state</c>.</para>
    /// </summary>
    void PushDrumState();

    /// <summary>
    /// Puts the lexer into figured-bass mode, stacking the current mode.
    /// <para>Upstream: <c>Lily_lexer::push_figuredbass_state</c>.</para>
    /// </summary>
    void PushFiguredBassState();

    /// <summary>
    /// Puts the lexer into chord mode, stacking the current mode (and, upstream,
    /// stacking the pitch-name table like <see cref="IParserHost.PushNoteState"/>).
    /// <para>Upstream: <c>Lily_lexer::push_chord_state</c>.</para>
    /// </summary>
    void PushChordState();

    /// <summary>
    /// Installs the chord-modifier table the chord-mode lexer consults, from the
    /// <c>chordmodifiers</c> alist the action looked up.
    /// <para>Upstream: <c>parser-&gt;lexer_-&gt;chordmodifier_tab_ =
    /// Hash_table::alist_to_hashq_table (mods)</c>. The alist-to-hash conversion
    /// (the vendored <c>alist-&gt;hashq-table</c>, <c>scm/lily-library.scm</c>) is
    /// folded in because the table's representation belongs to the lexer, not to
    /// the rule action.</para>
    /// </summary>
    /// <param name="modifiers">The chord-modifier alist, as looked up — which may be
    /// <see cref="CodeBrix.LilyScheme.Values.DefaultArgument"/> when the identifier
    /// is not defined, exactly as upstream hands whatever the lookup answered.</param>
    void SetChordModifiers(object modifiers);

    // Lily_lexer::is_lyric_state — which is what decides whether a markup, string
    // or symbol may stand as a lyric element — is the IsLyricState property in
    // IParserHost.MusicAssembly.cs, declared there alongside its note/chord siblings; the
    // two groups reached the same upstream member concurrently and the wave-2
    // integration kept one declaration.
}
