// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Lily.Docs;

namespace Lily.Docs.Tests;

/// <summary>
/// The frozen composition cases, read off <c>composed-reference/cases.tsv</c>.
/// <para>
/// The case list is READ rather than restated here for the reason rule 33's family gives:
/// a list restated in a test can silently stop covering a case the reference still holds,
/// and the failure mode is a green suite over a shrinking fence.
/// </para>
/// </summary>
public sealed class ComposedReferenceCase
{
    private ComposedReferenceCase(string name, IReadOnlyList<string> options, string code,
        int directiveLine, string referenceFile, string provenance)
    {
        Name = name;
        Options = options;
        Code = code;
        DirectiveLine = directiveLine;
        ReferenceFile = referenceFile;
        Provenance = provenance;
    }

    /// <summary>The case name, which is also its reference file's base name.</summary>
    public string Name { get; }

    /// <summary>The bracketed options, split as the package splits them.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>The snippet's music, as the probe document writes it.</summary>
    public string Code { get; }

    /// <summary>The line the <c>@lilypond</c> directive sits on in the probe document.</summary>
    public int DirectiveLine { get; }

    /// <summary>The reference file holding the oracle's composed source.</summary>
    public string ReferenceFile { get; }

    /// <summary>
    /// <c>own</c> when the oracle wrote this case its own file, or
    /// <c>deduplicated-by-oracle</c> when it composed identically to another case and the
    /// oracle wrote only one.
    /// </summary>
    public string Provenance { get; }

    /// <summary>Whether the oracle deduplicated this case against another.</summary>
    public bool WasDeduplicated =>
        !string.Equals(Provenance, "own", StringComparison.Ordinal);

    /// <summary>The oracle's composed source for this case.</summary>
    public string ReferenceSource =>
        File.ReadAllText(Path.Combine(ToolPaths.ComposedReferenceDirectory, ReferenceFile));

    /// <summary>Formats the case for a test name.</summary>
    /// <returns>The case name.</returns>
    public override string ToString() => Name;

    /// <summary>Reads every frozen case.</summary>
    /// <returns>The cases, in the order the probe document writes them.</returns>
    public static IReadOnlyList<ComposedReferenceCase> ReadAll()
    {
        string path = Path.Combine(ToolPaths.ComposedReferenceDirectory, "cases.tsv");
        List<ComposedReferenceCase> cases = new List<ComposedReferenceCase>();
        bool first = true;
        foreach (string line in File.ReadAllLines(path))
        {
            if (first)
            {
                first = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = line.Split('\t');
            cases.Add(new ComposedReferenceCase(
                fields[0],
                SplitOptions(fields[1]),
                fields[2],
                int.Parse(fields[3], CultureInfo.InvariantCulture),
                fields[4],
                fields[5]));
        }

        return cases;
    }

    /// <summary>
    /// Splits a bracket list the way the Texinfo package's own
    /// <c>snippet_option_separator</c> does — on commas, with surrounding space dropped.
    /// </summary>
    private static IReadOnlyList<string> SplitOptions(string options)
    {
        if (string.IsNullOrWhiteSpace(options))
        {
            return Array.Empty<string>();
        }

        string[] parts = options.Split(',');
        List<string> split = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            split.Add(part.Trim());
        }

        return split;
    }
}
