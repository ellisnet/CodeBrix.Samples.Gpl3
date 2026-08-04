// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The RAG6 additions: the lexer-mode predicates are scripted flags (the REAL host
/// answers from the running scanner's start condition), word scans come from a
/// scripted table like the keywords, <c>ApplySyntax</c> records the application and
/// hands back a <see cref="SyntaxMark"/> whose name is the constructor value's
/// string form, and <c>ConstructChordElements</c> records the call and returns
/// whatever list the test scripted — <see cref="Nil.Instance"/> by default, honestly
/// admitting no chord logic lives here.
/// </content>
internal sealed partial class ScriptedParserHost
{
    private bool _isNoteState;

    /// <inheritdoc/>
    /// <remarks>
    /// A SCRIPTED flag until a <see cref="Scanner"/> is attached, and the scanner's
    /// real start condition once one is — because a mode can change DURING a parse and
    /// a flag cannot follow it. <c>\markup \score { c }</c> is markup, then notes, then
    /// markup again in one expression, and the note inside it only converts if this
    /// answers for the moment it is asked rather than for the run.
    /// </remarks>
    public bool IsNoteState
    {
        get => Scanner != null ? Scanner.State == LexerState.Notes : _isNoteState;
        set => _isNoteState = value;
    }

    /// <inheritdoc/>
    public bool IsLyricState { get; set; }

    /// <inheritdoc/>
    public bool IsChordState { get; set; }

    /// <summary>Gets the word-scan table: symbol to token name and value.</summary>
    public Dictionary<Symbol, (string TokenName, object Value)> WordScans { get; }
        = new Dictionary<Symbol, (string, object)>();

    /// <summary>Gets the syntax applications, as (constructor, location, arguments).</summary>
    public List<(object Constructor, SourceSpan Location, object Arguments)> AppliedSyntax { get; }
        = new List<(object, SourceSpan, object)>();

    /// <summary>Gets the chord-element calls, as (pitch, duration, modifications).</summary>
    public List<(object Pitch, object Duration, object Modifications)> ChordElementCalls { get; }
        = new List<(object, object, object)>();

    /// <summary>Gets or sets the list <see cref="ConstructChordElements"/> answers.</summary>
    public object ChordElementsResult { get; set; } = Nil.Instance;

    /// <inheritdoc/>
    public LexerLookup ScanWord(object word)
        => word is Symbol symbol
           && WordScans.TryGetValue(symbol, out (string TokenName, object Value) entry)
            ? new LexerLookup(entry.TokenName, entry.Value)
            : LexerLookup.None;

    /// <inheritdoc/>
    public object ApplySyntax(object constructor, SourceSpan location, object arguments)
    {
        AppliedSyntax.Add((constructor, location, arguments));
        return new SyntaxMark
        {
            Name = constructor?.ToString(),
            Arguments = Pair.ToList(arguments).ToArray(),
        };
    }

    /// <inheritdoc/>
    public object ConstructChordElements(object pitch, object duration, object modifications)
    {
        ChordElementCalls.Add((pitch, duration, modifications));
        return ChordElementsResult;
    }
}
