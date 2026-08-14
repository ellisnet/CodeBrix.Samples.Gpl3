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
using CodeBrix.LilyScheme;

namespace CodeBrix.LilyPort.DocsDriver;

/// <summary>
/// The docs-parity driver: runs the vendored <c>ly/generate-documentation.ly</c> through the
/// port and reports which of the nineteen documentation files it wrote.
/// <para>
/// Usage: <c>DocsDriver OUT_DIR</c>. Upstream's entry point is a <c>.ly</c> file that
/// loads <c>lily/documentation-generate.scm</c>, which writes its outputs through
/// <c>open-output-file</c> with RELATIVE names — so the output directory is selected
/// by the PROCESS WORKING DIRECTORY, not by an argument. The driver changes into
/// <c>OUT_DIR</c> for exactly that reason; the oracle is invoked the same
/// way (<c>cd OUT_DIR &amp;&amp; lilypond .../generate-documentation.ly</c>), so both
/// sides of the comparison are produced by the same mechanism.
/// </para>
/// <para>
/// The driver reports per-file status as tab-separated lines, and its exit code is
/// about the RUN rather than about parity: zero when the script ran to completion and
/// wrote every expected file, 1 when a file is missing, 4 when the run itself threw.
/// Whether the bytes MATCH is <c>compare-docs.py</c>'s question, never this one's.
/// </para>
/// </summary>
public static class Program
{
    /// <summary>
    /// The nineteen files <c>documentation-generate.scm</c> writes, in the order the
    /// script writes them. Read off the script itself rather than off a run, so a run
    /// that silently stops writing one is a MISSING line instead of a shorter list.
    /// </summary>
    private static readonly string[] ExpectedOutputs =
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

    /// <summary>Runs the documentation generation.</summary>
    /// <param name="args">Command line: the output directory.</param>
    /// <returns>0 when every expected file was written; 1 when one is missing;
    /// 2 on usage errors; 4 when the run threw.</returns>
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: DocsDriver OUT_DIR");
            return 2;
        }

        string outputDirectory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(outputDirectory);

        // Stale outputs would read as this run's work, which is the mistake the sweep
        // driver's own self-check exists to prevent (see BatchDriver's class remarks).
        foreach (string expected in ExpectedOutputs)
        {
            string stale = Path.Combine(outputDirectory, expected);
            if (File.Exists(stale))
            {
                File.Delete(stale);
            }
        }

        string source = LilyPondScheme.ReadInitFile("generate-documentation");
        if (source == null)
        {
            Console.Error.WriteLine("the vendored ly/generate-documentation.ly is absent");
            return 2;
        }

        string previousDirectory = Directory.GetCurrentDirectory();
        Stopwatch clock = Stopwatch.StartNew();
        List<string> diagnostics = new List<string>();
        int loadedBeforeRun = 0;
        try
        {
            // Snapshotted BEFORE the run, because the startup hook stays installed and
            // keeps appending to one report — reading the whole list afterwards would
            // report the startup layer as this run's work (the trap PORT-COVERAGE records
            // against the load-report arithmetic).
            LilyPondInit.DefaultLayout();
            loadedBeforeRun = LilyPondScheme.CurrentLoadReport == null
                ? 0
                : LilyPondScheme.CurrentLoadReport.Loaded.Count;

            Directory.SetCurrentDirectory(outputDirectory);
            BatchRunResult result = BatchRunner.RunText(
                source, "generate-documentation", null, outputDirectory);
            diagnostics.AddRange(result.Diagnostics);

            // documentation-generate.scm opens nineteen ports and closes none of them,
            // which is legal: Guile flushes every open port as the process exits
            // (libguile/init.c:332). An embedded interpreter has no exit to hang that on,
            // so the owner of the run does it — here, because the run IS the process as
            // far as those files are concerned.
            Interpreter.RunWithLargeStack(() =>
                LilyPondScheme.Current.EvalString("(flush-all-ports)", "<docs-driver>"));
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            Console.WriteLine("RUN\tTHREW\t" + exception.GetType().Name + ": "
                + FirstLine(exception.Message));
            Console.Error.WriteLine(exception);
            ReportFiles(outputDirectory);
            return 4;
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }

        foreach (string diagnostic in diagnostics)
        {
            Console.WriteLine("DIAG\t" + FirstLine(diagnostic));
        }

        // The doc pipeline is loaded ON DEMAND, by the same hook that serves the startup
        // layer — and that hook RECORDS a file's failure rather than throwing it. Reading
        // the report is the only way a failed document-*.scm is visible as itself instead
        // of as an unbound variable several files later.
        if (LilyPondScheme.CurrentLoadReport != null)
        {
            // Everything the run itself pulled in, in the order it arrived: the startup
            // layer is already loaded when the driver starts, so what is listed after
            // the snapshot IS the documentation pipeline.
            for (int i = loadedBeforeRun;
                 i < LilyPondScheme.CurrentLoadReport.Loaded.Count;
                 i++)
            {
                Console.WriteLine("LOADED\t" + LilyPondScheme.CurrentLoadReport.Loaded[i]);
            }

            foreach (KeyValuePair<string, string> failure
                in LilyPondScheme.CurrentLoadReport.Failed)
            {
                // NOT the first line: the reason is a chain, and the useful half is
                // routinely at its far end.
                Console.WriteLine("LOAD-FAILED\t" + failure.Key + "\t" + failure.Value);
            }
        }

        int missing = ReportFiles(outputDirectory);
        Console.WriteLine();
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "# {0} of {1} files written, {2:0.0}s",
            ExpectedOutputs.Length - missing,
            ExpectedOutputs.Length,
            clock.Elapsed.TotalSeconds));

        return missing == 0 ? 0 : 1;
    }

    private static int ReportFiles(string outputDirectory)
    {
        int missing = 0;
        foreach (string expected in ExpectedOutputs)
        {
            string path = Path.Combine(outputDirectory, expected);
            if (File.Exists(path))
            {
                Console.WriteLine(expected + "\tWROTE\t"
                    + new FileInfo(path).Length + " bytes");
            }
            else
            {
                missing++;
                Console.WriteLine(expected + "\tMISSING");
            }
        }

        return missing;
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int end = text.IndexOf('\n');
        return end < 0 ? text : text.Substring(0, end).TrimEnd('\r');
    }
}
