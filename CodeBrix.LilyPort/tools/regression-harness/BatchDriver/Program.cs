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
/// <para>
/// EACH FILE RUNS FROM ITS OWN SCRATCH WORKING DIRECTORY, for the same reason the
/// docs driver changes into its output directory: a <c>.ly</c> file may WRITE, and
/// what it writes is named RELATIVE to the process working directory. Upstream gets
/// per-file isolation for free by running one process per file; a batch runner has to
/// arrange it, exactly as it already has to arrange <c>RestoreDefaults</c> for the
/// interpreter. Without it the sweep wrote into whatever directory it was launched
/// from — in practice the repo root, where <c>event-listener-output.ly</c> dropped
/// <c>-violin-1.notes</c> and, because the script opens for APPEND, every later sweep
/// added to the same file rather than replacing it (18,885 bytes after three runs).
/// Two consequences, and the second is the one that matters: the repo gets littered,
/// and one file's output becomes readable by every file engraved after it — the
/// cross-file leak class that has already produced nine separate defects here
/// (PORT-COVERAGE's per-file session leaks). The scratch directory is emptied before
/// each file, so nothing survives a run, let alone a sweep; anything a file DID write
/// is named on a <c>SIDE-FILE</c> line, because the sweep log is this project's demand
/// list and a silently discarded write would be worse than the litter it replaces.
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

        // Resolved BEFORE anything can change the working directory, and that is the
        // whole point: the sweep is invoked with RELATIVE paths, and the per-file scratch
        // directory below moves the ground they would be resolved against.
        string suiteDirectory = Path.GetFullPath(args[0]);
        string outputDirectory = Path.GetFullPath(args[1]);
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

        // The boot-expansion cache reads its override fresh on every access, so a RELATIVE
        // value stops meaning one directory the moment the sweep starts changing into
        // scratch directories — it would mean 2,146 of them, each cold, each paying the
        // ~28-second record. Pinned once here rather than left to be discovered as a
        // mysteriously slow sweep.
        string cacheOverride = Environment.GetEnvironmentVariable(CacheDirectoryVariable);
        if (!string.IsNullOrEmpty(cacheOverride) && !Path.IsPathRooted(cacheOverride))
        {
            Environment.SetEnvironmentVariable(
                CacheDirectoryVariable, Path.GetFullPath(cacheOverride));
        }

        string home = Directory.GetCurrentDirectory();
        string scratchRoot = PrepareScratchRoot();

        int produced = 0;
        int errored = 0;
        int empty = 0;
        int sideFiles = 0;
        int sideRuns = 0;
        HashSet<string> written = new HashSet<string>(StringComparer.Ordinal);
        Stopwatch clock = Stopwatch.StartNew();

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string scratch = OpenScratch(scratchRoot, name);
            try
            {
                Directory.SetCurrentDirectory(scratch);
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
            finally
            {
                // Restored before the scratch directory is read or removed, and before the
                // next file opens its own: a driver that left the process sitting in a
                // directory it then deletes would fail in a way that has nothing to do
                // with engraving.
                Directory.SetCurrentDirectory(home);
                int wrote = CloseScratch(name, scratch);
                if (wrote > 0)
                {
                    sideFiles += wrote;
                    sideRuns++;
                }
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
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "# side files: {0} written by {1} input file(s), under {2}",
            sideFiles,
            sideRuns,
            scratchRoot));

        return keepExisting ? 0 : SelfCheck(outputDirectory, written);
    }

    /// <summary>
    /// The environment variable naming the boot-expansion cache directory
    /// (<c>BootExpansionCache.DirectoryVariable</c>). Spelled here rather than
    /// referenced, because the driver has no dependency on the engine's bootstrap
    /// namespace and should not grow one to read one string.
    /// </summary>
    private const string CacheDirectoryVariable = "LILYPORT_EXPANSION_CACHE_DIR";

    /// <summary>
    /// Creates the root the per-file scratch working directories live under: one root
    /// PER PROCESS, in the system temporary directory.
    /// <para>
    /// Outside the repository, so that a file writing an unexpected name cannot litter
    /// the working tree no matter what it writes. Per process, because the alternative
    /// was tried and failed inside one session: a single shared root has to be emptied at
    /// startup to stop side files ACCUMULATING across sweeps — the defect being fixed was
    /// not only that <c>-violin-1.notes</c> appeared in the repo root but that each sweep
    /// APPENDED to it, so 6,295 bytes of one run read as 18,885 bytes of three — and that
    /// startup wipe deletes the scratch directories of any sweep already running. A probe
    /// run started alongside a sweep killed it on its first file. Naming the root after
    /// the process removes the shared resource instead of trying to schedule access to
    /// it, and each run is isolated for the same reason each FILE is.
    /// </para>
    /// <para>
    /// Nothing is deleted at startup, because a fresh process has nothing of its own to
    /// delete; the per-file clean in <see cref="OpenScratch"/> is what guarantees a file
    /// never sees an earlier run's leftovers. A crashed run leaves its root behind, which
    /// is evidence rather than litter — and in the temporary directory, which the system
    /// reclaims.
    /// </para>
    /// </summary>
    /// <returns>The scratch root, which exists when this returns.</returns>
    private static string PrepareScratchRoot()
    {
        string scratchRoot = Path.Combine(
            Path.GetTempPath(),
            "codebrix-lilyport-batch-scratch-"
                + Environment.ProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

        Directory.CreateDirectory(scratchRoot);
        Console.WriteLine("# per-file scratch working directories under " + scratchRoot);
        return scratchRoot;
    }

    /// <summary>
    /// Gives one input file an EMPTY working directory of its own.
    /// </summary>
    /// <param name="scratchRoot">The root the scratch directories live under.</param>
    /// <param name="name">The input file's base name.</param>
    /// <returns>The directory the file will run from.</returns>
    private static string OpenScratch(string scratchRoot, string name)
    {
        string scratch = Path.Combine(scratchRoot, name);
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, true);
        }

        Directory.CreateDirectory(scratch);
        return scratch;
    }

    /// <summary>
    /// Reports whatever a file wrote into its scratch directory, and removes the
    /// directory when it wrote nothing.
    /// <para>
    /// The reporting is the half that keeps this fix from being a way to LOSE
    /// information. Isolating the writes stops them polluting the tree; naming them on
    /// the sweep log keeps the fact that a file writes — and how much — in the record
    /// the project actually reads. A directory left behind is therefore evidence, not
    /// leftovers, and it is the only kind of directory left behind.
    /// </para>
    /// </summary>
    /// <param name="name">The input file's base name.</param>
    /// <param name="scratch">The directory the file ran from.</param>
    /// <returns>How many files it wrote.</returns>
    private static int CloseScratch(string name, string scratch)
    {
        List<string> artifacts = Directory
            .GetFiles(scratch, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (artifacts.Count == 0)
        {
            Directory.Delete(scratch, true);
            return 0;
        }

        foreach (string artifact in artifacts)
        {
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}\tSIDE-FILE\t{1}\t{2} bytes",
                name,
                Path.GetRelativePath(scratch, artifact),
                new FileInfo(artifact).Length));
        }

        return artifacts.Count;
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
