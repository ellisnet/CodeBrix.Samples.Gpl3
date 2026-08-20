// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace Lily.Docs.Manuals;

/// <summary>Where a manual's top-level source file comes from.</summary>
public enum ManualSourceKind
{
    /// <summary>The port generates it — it is one of the nineteen outputs.</summary>
    Generated,

    /// <summary>It is corpus text, mirrored under the repository's Documentation tree.</summary>
    Corpus,
}

/// <summary>
/// One renderable manual: which file is its root, where that file comes from, and what
/// the render is expected to contain.
/// </summary>
public sealed class ManualDefinition
{
    /// <summary>Creates a manual definition.</summary>
    /// <param name="name">The short name used on the command line.</param>
    /// <param name="fileName">The root source file's name.</param>
    /// <param name="sourceKind">Where that file comes from.</param>
    /// <param name="title">The manual's title, for messages.</param>
    /// <param name="engravesSnippets">Whether the manual carries music that has to be
    /// engraved.</param>
    internal ManualDefinition(string name, string fileName, ManualSourceKind sourceKind,
        string title, bool engravesSnippets)
    {
        Name = name;
        FileName = fileName;
        SourceKind = sourceKind;
        Title = title;
        EngravesSnippets = engravesSnippets;
    }

    /// <summary>The short name used on the command line, e.g. <c>internals</c>.</summary>
    public string Name { get; }

    /// <summary>The root source file's name, e.g. <c>internals.texi</c>.</summary>
    public string FileName { get; }

    /// <summary>Where the root source file comes from.</summary>
    public ManualSourceKind SourceKind { get; }

    /// <summary>The manual's title.</summary>
    public string Title { get; }

    /// <summary>
    /// Whether this manual carries music snippets, so that an engraving renderer has to be
    /// registered for it.
    /// <para>
    /// Declared per manual rather than discovered, because the two answers need different
    /// evidence and both are MEASURED. The Internals Reference says of itself
    /// <c>@c @lilypond is not allowed in the IR.</c> and carries none, which is what let
    /// wave LD1 render before the seam existed; the Notation Reference carries music in
    /// its own prose AND in three of the port's own generated fragments.
    /// </para>
    /// <para>
    /// ⚠ A manual declared false here renders with NO renderer registered, and a document
    /// full of unengraved snippets is exactly what a document full of silently failed
    /// engravings looks like — so a false is a claim its own gate has to prove, never a
    /// default.
    /// </para>
    /// </summary>
    public bool EngravesSnippets { get; }
}

/// <summary>
/// The manuals Lily.Docs can render.
/// <para>
/// Scope is decision D48, RULED 2026-08-19: NINE manuals are owed in both HTML and
/// PDF — <c>internals</c>, <c>notation</c>, <c>learning</c>, <c>usage</c>,
/// <c>extending</c>, <c>essay</c>, <c>changes</c>, <c>music-glossary</c> and
/// <c>contributor</c>. <c>web.texi</c> is excluded, and <c>snippets.tely</c> is not a
/// deliverable manual but the include-warning CONTROL for the zero-include-warning
/// gates.
/// </para>
/// <para>
/// This catalogue lists the Internals Reference (wave LD1) and the Notation Reference
/// (wave LD3), because a manual is added here when its render is GATED, not when its name
/// is ruled in: the remaining seven arrive with LD5. They are deliberately absent rather
/// than listed and unsupported, so that an unknown manual name is an error rather than
/// a render that quietly produces nothing.
/// </para>
/// </summary>
public static class ManualCatalog
{
    private static readonly ManualDefinition InternalsReference = new ManualDefinition(
        "internals", "internals.texi", ManualSourceKind.Generated,
        "LilyPond Internals Reference", engravesSnippets: false);

    private static readonly ManualDefinition NotationReference = new ManualDefinition(
        "notation", "notation.tely", ManualSourceKind.Corpus,
        "LilyPond Notation Reference", engravesSnippets: true);

    /// <summary>
    /// The include-warning CONTROL — <c>snippets.tely</c>, which is NOT a deliverable manual
    /// and is deliberately absent from <see cref="All"/>.
    /// <para>
    /// Decision D48, ruled 2026-08-19, reclassified it. MEASURED: it holds two
    /// <c>@node</c>s, no chapters, no sections, and forty <c>@include</c>s — thirty-nine of
    /// them <c>snippets/*</c> files that are LSR build products upstream and exist nowhere
    /// in the checkout, plus <c>en/macros.itexi</c>, which the vendored assets resolve. As a
    /// manual it would render a title page and two empty nodes.
    /// </para>
    /// <para>
    /// ⚠ ITS VALUE IS AS A PAIRED CONTROL, AND THE NOTATION GATE NEEDS IT. That gate asserts
    /// the notation manual earns ZERO Include warnings — but a zero that passes because
    /// nothing is missing is indistinguishable from a zero that passes because the warning
    /// channel is broken. A document with exactly thirty-nine KNOWABLY absent includes,
    /// asserted at exactly thirty-nine in the same run, separates the two.
    /// </para>
    /// </summary>
    public static ManualDefinition IncludeWarningControl { get; } = new ManualDefinition(
        "snippets", "snippets.tely", ManualSourceKind.Corpus,
        "LilyPond Snippets (the include-warning control)", engravesSnippets: false);

    /// <summary>
    /// Every manual this tool can currently render. ⚠ <see cref="IncludeWarningControl"/> is
    /// NOT among them, so naming it on the command line is an error rather than a render of
    /// something that was never a deliverable.
    /// </summary>
    public static IReadOnlyList<ManualDefinition> All { get; } =
        new[] { InternalsReference, NotationReference };

    /// <summary>Looks a manual up by its command-line name.</summary>
    /// <param name="name">The manual's short name.</param>
    /// <returns>The definition, or null when the name is not one this tool knows.</returns>
    public static ManualDefinition Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (ManualDefinition manual in All)
        {
            if (string.Equals(manual.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return manual;
            }
        }

        return null;
    }
}
