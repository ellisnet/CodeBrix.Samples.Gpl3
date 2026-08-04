// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Lexing;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The RAG12 additions: the remaining lexer-mode pushes are RECORDED into
/// <see cref="LexerModeOperations"/> like <c>PushNoteState</c> and, when
/// <see cref="ScriptedParserHost.Scanner"/> has been attached, FORWARDED onto the real
/// scanner — which is what a real host does and what any mode-sensitive text needs.
/// With no scanner attached they are recorded only, and the body after a mode keyword
/// keeps lexing in the outer mode. The chord-modifier hand-off is recorded, and the
/// lyric-state answer is scripted.
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <summary>Gets the values <see cref="SetChordModifiers"/> received, in order.</summary>
    public List<object> ChordModifierAssignments { get; } = new List<object>();

    /// <inheritdoc/>
    public void PushLyricState() => PushMode("push-lyric-state", LexerState.Lyrics);

    /// <inheritdoc/>
    // Drum mode IS the NOTES start condition upstream, with the pitch-name table
    // swapped for drumPitchNames — the table swap is the real host's, not the
    // scanner's, so only the start condition is pushed here.
    public void PushDrumState() => PushMode("push-drum-state", LexerState.Notes);

    /// <inheritdoc/>
    public void PushFiguredBassState() => PushMode("push-figuredbass-state", LexerState.Figures);

    /// <inheritdoc/>
    public void PushChordState() => PushMode("push-chord-state", LexerState.Chords);

    /// <inheritdoc/>
    public void SetChordModifiers(object modifiers) => ChordModifierAssignments.Add(modifiers);

    // IsLyricState is the settable flag on ScriptedParserHost.Rag6.cs, shared with
    // the RAG6 mode reads — the wave-2 integration kept one implementation.
}
