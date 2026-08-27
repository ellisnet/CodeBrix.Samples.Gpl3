// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/lydocinfo.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The library's document information, plus the two places this application
/// also looks for a <c>\version</c>: the document variables, and a
/// <c>\version</c> written in a comment of a non-LilyPond document.
/// </summary>
/// <remarks>
/// Splitting it this way is upstream's: the library half knows only about
/// tokens, so it works the same for a file on disk and for a document open in
/// the editor, and this half adds what only the application knows.
/// </remarks>
public sealed class LyDocInfo : DocInfo
{
    private static readonly Regex VersionInText = new Regex(
        @"\\version\s*""(\d+\.\d+(\.\d+)*)""", RegexOptions.Compiled);

    /// <summary>Creates the information over a document and its variables.</summary>
    /// <param name="document">The tokenized document.</param>
    /// <param name="variables">The document's <c>-*-</c> variables.</param>
    public LyDocInfo(DocumentBase document, IReadOnlyDictionary<string, string> variables)
        : base(document)
        => Variables = variables
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the document's variables.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// After the tokens, the <c>version</c> variable is consulted, and only
    /// then the whole text — which is what lets a LaTeX or HTML document that
    /// merely QUOTES a <c>\version</c> line still be engraved at the right
    /// grammar.
    /// </remarks>
    public override string VersionString()
        => Cached(nameof(LyDocInfo) + "." + nameof(VersionString), () =>
        {
            string version = base.VersionString();
            if (!string.IsNullOrEmpty(version)) { return version; }

            if (Variables.TryGetValue("version", out var declared)
                && !string.IsNullOrEmpty(declared))
            {
                return declared;
            }

            Match match = VersionInText.Match(Document.PlainText());
            return match.Success ? match.Groups[1].Value : null;
        });
}
