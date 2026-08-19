// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CodeBrix.LilyPort;
using CodeBrix.LilyPort.Engine.Bootstrap;

namespace Lily.Docs.Generation;

/// <summary>
/// Runs the vendored <c>ly/generate-documentation.ly</c> through the port and reports
/// which of the nineteen documentation files it wrote.
/// <para>
/// This is the DocsDriver pattern (<c>tools/regression-harness/DocsDriver</c>), which
/// is the harness form of the same run; the two agree deliberately, because G8's
/// byte-parity gate is stated against DocsDriver's output and a manual rendered from
/// different bytes would not be rendered from the bytes that gate covers.
/// </para>
/// <para>
/// Upstream's entry point writes its outputs through <c>open-output-file</c> with
/// RELATIVE names, so the output directory is selected by the PROCESS WORKING
/// DIRECTORY rather than by an argument. That is why this class changes directory
/// around the run.
/// </para>
/// </summary>
public sealed class DocumentationGenerator
{
    /// <summary>
    /// The nineteen files <c>documentation-generate.scm</c> writes, in the order the
    /// script writes them. Read off the script rather than off a run, so a run that
    /// silently stops writing one is a MISSING entry instead of a shorter list.
    /// </summary>
    public static readonly IReadOnlyList<string> ExpectedOutputs = new[]
    {
        "markup-commands.tely",
        "markup-list-commands.tely",
        "type-predicates.tely",
        "identifiers.tely",
        "context-mod-identifiers.tely",
        "outside-staff-priorities.tely",
        "script-priorities.tely",
        "break-align-grobs-by-symbols.tely",
        "break-align-symbols-by-grobs.tely",
        "paper-sizes.tely",
        "paper-variables.tely",
        "standard-colors.tely",
        "x11-unnumbered-colors.tely",
        "x11-colorN.tely",
        "x11-grayN.tely",
        "css-colors.tely",
        "universal-colors.tely",
        "hyphenation.itexi",
        "internals.texi",
    };

    /// <summary>
    /// Generates the nineteen files into <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="outputDirectory">Directory to write into; created when absent.</param>
    /// <returns>The result of the run.</returns>
    /// <exception cref="InvalidOperationException">The vendored
    /// <c>generate-documentation.ly</c> could not be read out of the Engine.</exception>
    public DocumentationGenerationResult Generate(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("an output directory is required", nameof(outputDirectory));
        }

        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);

        // A stale output reads as this run's work. Deleting first is DocsDriver's own
        // rule and the reason a half-finished run cannot masquerade as a complete one.
        foreach (string expected in ExpectedOutputs)
        {
            string stale = Path.Combine(directory, expected);
            if (File.Exists(stale))
            {
                File.Delete(stale);
            }
        }

        string source = LilyPondScheme.ReadInitFile("generate-documentation");
        if (source == null)
        {
            throw new InvalidOperationException(
                "the vendored ly/generate-documentation.ly is absent from the Engine");
        }

        List<string> diagnostics = new List<string>();
        string previousDirectory = Directory.GetCurrentDirectory();
        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            LilyPondInit.DefaultLayout();
            Directory.SetCurrentDirectory(directory);
            BatchRunResult result = BatchRunner.RunText(
                source, "generate-documentation", null, directory);
            diagnostics.AddRange(result.Diagnostics);

            // documentation-generate.scm opens nineteen ports and closes none of them,
            // which is legal: Guile flushes every open port as the process exits. An
            // embedded interpreter has no exit to hang that on, so the owner of the run
            // does it — here, because the run IS the process as far as those files are
            // concerned. Without this the last writes are lost and the files are short.
            LilyPondScheme.Current.EvalString("(flush-all-ports)", "<lily-docs>");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            clock.Stop();
        }

        List<string> missing = new List<string>();
        foreach (string expected in ExpectedOutputs)
        {
            if (!File.Exists(Path.Combine(directory, expected)))
            {
                missing.Add(expected);
            }
        }

        return new DocumentationGenerationResult(directory, missing, diagnostics, clock.Elapsed);
    }
}

/// <summary>The outcome of one documentation-generation run.</summary>
public sealed class DocumentationGenerationResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="outputDirectory">Where the files were written.</param>
    /// <param name="missingFiles">Expected files that were not written.</param>
    /// <param name="diagnostics">Diagnostics the run produced.</param>
    /// <param name="elapsed">How long the run took.</param>
    internal DocumentationGenerationResult(string outputDirectory, IReadOnlyList<string> missingFiles,
        IReadOnlyList<string> diagnostics, TimeSpan elapsed)
    {
        OutputDirectory = outputDirectory;
        MissingFiles = missingFiles;
        Diagnostics = diagnostics;
        Elapsed = elapsed;
    }

    /// <summary>The directory the nineteen files were written into.</summary>
    public string OutputDirectory { get; }

    /// <summary>Expected files the run did not write. Empty on a complete run.</summary>
    public IReadOnlyList<string> MissingFiles { get; }

    /// <summary>Diagnostics the run produced.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>How long the run took.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>True when every expected file was written.</summary>
    public bool IsComplete => MissingFiles.Count == 0;
}
