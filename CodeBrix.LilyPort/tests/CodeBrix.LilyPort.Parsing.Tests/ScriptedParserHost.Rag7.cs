// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The RAG7 additions: grob-symbol answers are scripted and their queries recorded,
/// warnings are recorded, and the key-list test is real (it is pure, like
/// <c>IsKey</c>).
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <summary>Gets the symbols <see cref="IsGrobSymbol"/> answers true for.</summary>
    public HashSet<object> GrobSymbols { get; } = new HashSet<object>();

    /// <summary>Gets the values <see cref="IsGrobSymbol"/> was asked about, in order.</summary>
    public List<object> GrobQueries { get; } = new List<object>();

    /// <summary>Gets the warnings received, in order.</summary>
    public List<(SourceSpan Location, string Message)> Warnings { get; }
        = new List<(SourceSpan, string)>();

    /// <inheritdoc/>
    public bool IsGrobSymbol(object value)
    {
        GrobQueries.Add(value);
        return GrobSymbols.Contains(value);
    }

    /// <inheritdoc/>
    public bool IsKeyList(object value)
    {
        // (and (list? x) (every key? x)) -- scm/c++.scm
        object cursor = value;
        while (cursor is Pair pair)
        {
            if (!IsKey(pair.Car))
            {
                return false;
            }

            cursor = pair.Cdr;
        }

        return cursor is Nil;
    }

    /// <inheritdoc/>
    public void Warning(SourceSpan location, string message)
        => Warnings.Add((location, message));
}
