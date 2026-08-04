// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Lexing;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The additions the music-function arglist groups (RAG8–RAG10) needed — all on the
/// SCRIPTING side, because those groups added no <c>IParserHost</c> members at all:
/// an identifier table for the lexer seam (so <c>\foo</c> can resolve to a
/// <c>MUSIC_FUNCTION</c> whose signature the scanner announces), scriptable results
/// for <see cref="ScriptedParserHost.Call"/> (argument predicates must be able to
/// say no), and a record of every <c>MakeSyntax</c> dispatch (so a test can see the
/// <c>argument-error</c> that <c>check_scheme_arg</c> raises).
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <summary>Gets the identifier table the lexer seam resolves <c>\word</c> through.</summary>
    public Dictionary<string, LexerLookup> Identifiers { get; }
        = new Dictionary<string, LexerLookup>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets what <see cref="ScriptedParserHost.Call"/> answers; null keeps
    /// the recording-only default of <c>Unspecified</c>.
    /// </summary>
    public Func<object, object[], object> CallBehavior { get; set; }

    /// <summary>Gets every <c>MakeSyntax</c> dispatch, in order.</summary>
    public List<SyntaxMark> SyntaxDispatches { get; } = new List<SyntaxMark>();
}
