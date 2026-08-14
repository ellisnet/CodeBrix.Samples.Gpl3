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
using System.Linq;
using CodeBrix.LilyPort;

namespace CodeBrix.LilyPort.BatchDriver;

/// <summary>
/// The harness's way of running the PORT over the regression suite in one process
/// — the batch half of decision D14, on decision D20's score → SVG path.
/// <para>
/// Usage:
/// <c>BatchDriver SUITE_DIR OUT_DIR [--limit N] [--files a.ly,b.ly] [--keep-existing]</c>.
/// One SVG per input lands in <c>OUT_DIR</c>; per-file status goes to standard
/// output as a tab-separated line the harness scripts can read. The process exits
/// zero as long as the SWEEP ran AND the output directory holds exactly what the
/// sweep says it wrote; individual files failing to engrave is the expected state
/// the comparator grades, not a driver error.
/// </para>
/// <para>
/// THE OUTPUT DIRECTORY IS EMPTIED OF <c>.svg</c> FILES BEFORE THE SWEEP RUNS, and
/// that is load-bearing rather than tidiness. It once was not, and the
/// consequence was that a sweep's output sat on top of every earlier run's: a file
/// that STOPPED producing a page kept the stale one, `compare-output.py` graded the
/// stale page, and the ratchet reported no regression for precisely the failure mode
/// it exists to catch. It had accumulated 1,568 pages for a run that produced 1,470,
/// and inflated the committed floor by 97 rows over at least three sessions. The
/// self-check at the end of the sweep is the second half of the same fix: it asserts
/// the directory contents equal the set of pages this run reported writing, so the
/// mistake cannot come back silently by another route.
/// </para>
/// </summary>
public static class Program
{
    /// <summary>Runs the sweep.</summary>
    /// <param name="args">Command line: suite directory, output directory, options.</param>
    /// <returns>0 when the sweep ran and its self-check passed; 2 on usage errors;
    /// 3 when the output directory does not hold exactly what the sweep wrote.</returns>
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "usage: BatchDriver SUITE_DIR OUT_DIR [--limit N] [--files a.ly,b.ly]"
                + " [--keep-existing]");
            return 2;
        }

        string suiteDirectory = args[0];
        string outputDirectory = args[1];
        int limit = int.MaxValue;
        HashSet<string> only = null;
        bool keepExisting = false;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--limit" && i + 1 < args.Length)
            {
                limit = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--files" && i + 1 < args.Length)
            {
                only = new HashSet<string>(args[++i].Split(','), StringComparer.Ordinal);
            }
            else if (args[i] == "--keep-existing")
            {
                // For the rare partial run (--files / --limit) that is deliberately
                // ADDING pages to a directory a full sweep filled. It suppresses the
                // clean AND the self-check, because neither means anything then.
                keepExisting = true;
            }
        }

        if (!Directory.Exists(suiteDirectory))
        {
            Console.Error.WriteLine("no suite at " + suiteDirectory);
            return 2;
        }

        List<string> files = Directory.GetFiles(suiteDirectory, "*.ly")
            .OrderBy(name => name, StringComparer.Ordinal)
            .Where(path => only == null || only.Contains(Path.GetFileName(path)))
            .Take(limit)
            .ToList();

        Directory.CreateDirectory(outputDirectory);

        if (keepExisting)
        {
            Console.WriteLine("# --keep-existing: output directory NOT cleaned,"
                + " and the self-check is off. This run's verdicts are not evidence.");
        }
        else
        {
            ClearStalePages(outputDirectory);
        }

        int produced = 0;
        int errored = 0;
        int empty = 0;
        HashSet<string> written = new HashSet<string>(StringComparer.Ordinal);
        Stopwatch clock = Stopwatch.StartNew();

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                BatchRunResult result = BatchRunner.RunFile(file, outputDirectory);

                // MIDI is reported on its own line and always,
                // because the sweep log IS this project's demand list: a performance that
                // failed while the page succeeded would otherwise be invisible, which is
                // exactly how the layout side's own silent gaps survived for sessions.
                foreach (string diagnostic in result.Diagnostics)
                {
                    if (diagnostic.StartsWith("performing failed:", StringComparison.Ordinal)
                        || diagnostic.StartsWith("MIDI output failed:", StringComparison.Ordinal))
                    {
                        Console.WriteLine(name + "\tMIDI-FAIL\t" + FirstLine(diagnostic));
                    }
                }

                if (result.MidiPaths.Count > 0)
                {
                    Console.WriteLine(
                        name + "\tMIDI\t" + result.MidiPaths.Count + " file(s)");
                }

                if (result.SvgPath != null)
                {
                    produced++;

                    // EVERY page, not just the first: on the book path one input file may
                    // write several, and the self-check compares what is on disk against
                    // exactly this set.
                    foreach (string page in result.SvgPaths)
                    {
                        written.Add(Path.GetFullPath(page));
                    }
                    Console.WriteLine(name + "\tSVG\t" + result.SystemCount
                        + " system(s), " + result.ErrorCount + " parse error(s)");
                }
                else
                {
                    empty++;
                    Console.WriteLine(name + "\tNOOUT\t" + Summarise(result));
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                errored++;
                Console.WriteLine(name + "\tERROR\t" + Describe(exception));
            }
        }

        Console.WriteLine();
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "# {0} files: {1} svg, {2} no output, {3} errored, {4:0.0}s",
            files.Count,
            produced,
            empty,
            errored,
            clock.Elapsed.TotalSeconds));

        return keepExisting ? 0 : SelfCheck(outputDirectory, written);
    }

    /// <summary>
    /// Removes every <c>.svg</c> already in the output directory, so the sweep grades
    /// against ITS OWN output and nothing else.
    /// <para>
    /// Only top-level <c>.svg</c> files go: anything else in there was not written by
    /// this driver, so it is left alone and NAMED, because unexpected contents usually
    /// mean the caller pointed at the wrong directory.
    /// </para>
    /// </summary>
    /// <param name="outputDirectory">The directory the sweep writes into.</param>
    private static void ClearStalePages(string outputDirectory)
    {
        List<string> stale = PagesIn(outputDirectory);
        foreach (string page in stale)
        {
            File.Delete(page);
        }

        int foreign = Directory.GetFileSystemEntries(outputDirectory).Length;
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "# cleaned {0} stale page(s) from {1}",
            stale.Count,
            outputDirectory));

        if (foreign > 0)
        {
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "# NOTE: {0} non-.svg entr(ies) remain in the output directory and were"
                + " left alone — is this the directory you meant?",
                foreign));
        }
    }

    /// <summary>
    /// Asserts the output directory holds exactly the pages this run reported writing.
    /// <para>
    /// This is the cheap invariant that would have caught the stale-output defect the
    /// moment it appeared, in the spirit of the comparator's reference-against-itself
    /// self-check. A count alone would do for today's one-page-per-input driver; the
    /// SET is compared instead, so it keeps holding if a file ever emits several pages.
    /// </para>
    /// </summary>
    /// <param name="outputDirectory">The directory the sweep wrote into.</param>
    /// <param name="written">Full paths of the pages the sweep reported writing.</param>
    /// <returns>0 when the directory matches; 3 when it does not.</returns>
    private static int SelfCheck(string outputDirectory, HashSet<string> written)
    {
        HashSet<string> onDisk = new HashSet<string>(
            PagesIn(outputDirectory).Select(Path.GetFullPath),
            StringComparer.Ordinal);

        List<string> unexpected = onDisk.Except(written).OrderBy(p => p, StringComparer.Ordinal).ToList();
        List<string> absent = written.Except(onDisk).OrderBy(p => p, StringComparer.Ordinal).ToList();

        if (unexpected.Count == 0 && absent.Count == 0)
        {
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "# self-check: {0} page(s) on disk == {0} page(s) written",
                onDisk.Count));
            return 0;
        }

        Console.Error.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "*** SELF-CHECK FAILED: {0} page(s) on disk, {1} written by this run"
            + " ({2} unexpected, {3} missing). The comparator would grade output this"
            + " sweep did not produce; its verdicts are NOT evidence. ***",
            onDisk.Count,
            written.Count,
            unexpected.Count,
            absent.Count));

        foreach (string page in unexpected.Take(20))
        {
            Console.Error.WriteLine("  UNEXPECTED  " + Path.GetFileName(page));
        }

        foreach (string page in absent.Take(20))
        {
            Console.Error.WriteLine("  MISSING     " + Path.GetFileName(page));
        }

        return 3;
    }

    /// <summary>
    /// The pages in a directory — the ONE definition of what this driver considers
    /// its own output, shared by the clean and the self-check so they can never
    /// disagree about what a page is.
    /// <para>
    /// The extension is matched exactly rather than by a <c>"*.svg"</c> search
    /// pattern: that pattern also matches longer extensions by documented design
    /// (the rule that makes <c>"*.xls"</c> return <c>book.xlsx</c>), and one side of
    /// this pair DELETES what it is given.
    /// </para>
    /// </summary>
    /// <param name="directory">The directory to look in.</param>
    /// <returns>Full paths of its <c>.svg</c> files, top level only.</returns>
    private static List<string> PagesIn(string directory)
        => Directory.GetFiles(directory)
            .Where(path => string.Equals(
                Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Summarise(BatchRunResult result)
    {
        if (result.Diagnostics.Count == 0)
        {
            return result.BookCount + " book(s), " + result.SkippedEntries + " skipped";
        }

        return result.BookCount + " book(s); " + FirstLine(result.Diagnostics[0])
            + (result.Diagnostics.Count > 1
                ? " (+" + (result.Diagnostics.Count - 1) + " more)"
                : string.Empty);
    }

    /// <summary>
    /// Describes a failure as its TYPE and its innermost message.
    /// <para>
    /// Printing only the outermost message is what made this whole category of failure
    /// opaque: a wrapper's message says where the failure was caught, never what it was.
    /// The innermost exception is the one that knows.
    /// </para>
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>One line naming the type and the cause.</returns>
    private static string Describe(Exception exception)
    {
        Exception cause = exception;
        while (cause.InnerException != null)
        {
            cause = cause.InnerException;
        }

        return cause.GetType().Name + ": " + FirstLine(cause.Message);
    }

    private static string FirstLine(string text)
    {
        int cut = text.IndexOf('\n');
        return cut < 0 ? text : text.Substring(0, cut);
    }
}
