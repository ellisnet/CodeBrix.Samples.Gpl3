// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Completion; //was previously: frescobaldi/listmodel.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One thing the completion popup can offer.</summary>
/// <remarks>
/// Upstream's list models carry a <c>display</c> function and an <c>edit</c>
/// function over the same raw item — what the row SHOWS and what typing Enter
/// INSERTS, which are often different: a header variable shows
/// <c>title</c> and inserts <c>title = </c>. Two strings say the same thing
/// without a model class or a role table.
/// </remarks>
public readonly struct CompletionEntry : IEquatable<CompletionEntry>
{
    /// <summary>Creates an entry.</summary>
    /// <param name="insert">What is inserted.</param>
    /// <param name="display">What the row shows, or null to show what is
    /// inserted.</param>
    public CompletionEntry(string insert, string display = null)
    {
        Insert = insert ?? string.Empty;
        Display = display ?? insert ?? string.Empty;
    }

    /// <summary>Gets what is inserted.</summary>
    public string Insert { get; }

    /// <summary>Gets what the row shows.</summary>
    public string Display { get; }

    /// <inheritdoc/>
    public bool Equals(CompletionEntry other)
        => string.Equals(Insert, other.Insert, StringComparison.Ordinal)
            && string.Equals(Display, other.Display, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object obj)
        => obj is CompletionEntry other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Insert, Display);

    /// <inheritdoc/>
    public override string ToString() => Display;
}

/// <summary>A list of completions, in the order the popup shows them.</summary>
public sealed class CompletionModel
{
    /// <summary>An empty model.</summary>
    public static readonly CompletionModel Empty
        = new CompletionModel(Array.Empty<CompletionEntry>());

    /// <summary>Creates a model.</summary>
    /// <param name="entries">The entries.</param>
    public CompletionModel(IEnumerable<CompletionEntry> entries)
        => Entries = entries?.ToList() ?? new List<CompletionEntry>();

    /// <summary>Gets the entries.</summary>
    public IReadOnlyList<CompletionEntry> Entries { get; }

    /// <summary>Gets how many entries there are.</summary>
    public int Count => Entries.Count;

    /// <summary>Makes a model of plain words, shown and inserted as they are.</summary>
    /// <param name="words">The words.</param>
    /// <returns>The model.</returns>
    public static CompletionModel Of(IEnumerable<string> words)
        => new CompletionModel(words.Select(w => new CompletionEntry(w)));

    /// <summary>Makes a model of commands: each word with a backslash.</summary>
    /// <param name="words">The words, without their backslash.</param>
    /// <returns>The model.</returns>
    /// <remarks>Upstream's <c>display = util.command</c>, whose <c>edit</c>
    /// falls back to the same function, so the row shows and inserts the same
    /// text.</remarks>
    public static CompletionModel OfCommands(IEnumerable<string> words)
        => new CompletionModel(words.Select(w => new CompletionEntry("\\" + w)));

    /// <summary>
    /// Makes a model of variables: shown as the name, inserted with
    /// <c> = </c> after it.
    /// </summary>
    /// <param name="words">The names.</param>
    /// <returns>The model.</returns>
    public static CompletionModel OfVariables(IEnumerable<string> words)
        => new CompletionModel(
            words.Select(w => new CompletionEntry(w + " = ", w)));

    /// <summary>
    /// Makes a model whose commands insert as they are and whose plain names
    /// gain a <c> = </c> — upstream's <c>cmd_or_var</c>.
    /// </summary>
    /// <param name="words">The words.</param>
    /// <returns>The model.</returns>
    public static CompletionModel OfCommandsOrVariables(IEnumerable<string> words)
        => new CompletionModel(words.Select(
            w => w.StartsWith('\\')
                ? new CompletionEntry(w)
                : new CompletionEntry(w + " = ", w)));

    /// <summary>Makes a model whose rows show and insert a scheme symbol.</summary>
    /// <param name="words">The words.</param>
    /// <param name="hashQuote">Whether to prefix each with <c>#'</c>.</param>
    /// <returns>The model.</returns>
    public static CompletionModel OfSchemeSymbols(
        IEnumerable<string> words, bool hashQuote)
        => hashQuote
            ? new CompletionModel(words.Select(w => new CompletionEntry("#'" + w)))
            : Of(words);
}
