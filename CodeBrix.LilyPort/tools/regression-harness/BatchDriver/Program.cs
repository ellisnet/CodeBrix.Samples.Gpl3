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
/// Usage: <c>BatchDriver SUITE_DIR OUT_DIR [--limit N] [--files a.ly,b.ly]</c>.
/// One SVG per input lands in <c>OUT_DIR</c>; per-file status goes to standard
/// output as a tab-separated line the harness scripts can read. The process exits
/// zero as long as the SWEEP ran; individual files failing to engrave is the
/// expected state the comparator grades, not a driver error.
/// </para>
/// </summary>
public static class Program
{
    /// <summary>Runs the sweep.</summary>
    /// <param name="args">Command line: suite directory, output directory, options.</param>
    /// <returns>0 when the sweep ran; 2 on usage errors.</returns>
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "usage: BatchDriver SUITE_DIR OUT_DIR [--limit N] [--files a.ly,b.ly]");
            return 2;
        }

        string suiteDirectory = args[0];
        string outputDirectory = args[1];
        int limit = int.MaxValue;
        HashSet<string> only = null;

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

        int produced = 0;
        int errored = 0;
        int empty = 0;
        Stopwatch clock = Stopwatch.StartNew();

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                BatchRunResult result = BatchRunner.RunFile(file, outputDirectory);
                if (result.SvgPath != null)
                {
                    produced++;
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
        return 0;
    }

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
