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
    internal ManualDefinition(string name, string fileName, ManualSourceKind sourceKind, string title)
    {
        Name = name;
        FileName = fileName;
        SourceKind = sourceKind;
        Title = title;
    }

    /// <summary>The short name used on the command line, e.g. <c>internals</c>.</summary>
    public string Name { get; }

    /// <summary>The root source file's name, e.g. <c>internals.texi</c>.</summary>
    public string FileName { get; }

    /// <summary>Where the root source file comes from.</summary>
    public ManualSourceKind SourceKind { get; }

    /// <summary>The manual's title.</summary>
    public string Title { get; }
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
/// This catalogue still lists only the Internals Reference, because a manual is added
/// here when its render is GATED, not when its name is ruled in: the notation manual
/// arrives with LD3 and the other seven with LD5. They are deliberately absent rather
/// than listed and unsupported, so that an unknown manual name is an error rather than
/// a render that quietly produces nothing.
/// </para>
/// </summary>
public static class ManualCatalog
{
    private static readonly ManualDefinition InternalsReference = new ManualDefinition(
        "internals", "internals.texi", ManualSourceKind.Generated,
        "LilyPond Internals Reference");

    /// <summary>Every manual this tool can currently render.</summary>
    public static IReadOnlyList<ManualDefinition> All { get; } = new[] { InternalsReference };

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
