// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Tools.SymbolIcons;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Engraves Frescobaldi's symbol sources with LilyPort and writes the SVGs the
/// Quick Insert panel's buttons show.
/// </summary>
/// <remarks>
/// <para>
/// Upstream ships the generated SVGs beside the sources and regenerates them
/// with a Makefile that calls an external <c>lilypond</c> binary. Here the
/// SOURCES are what is read and the engine is in-process, which is the whole
/// point of the exercise: the icons on the buttons are drawn by the same
/// engine that will engrave the user's score.
/// </para>
/// <para>
/// The reference checkout is READ ONLY (standing rule 3): the sources are
/// copied into a scratch directory and engraved there.
/// </para>
/// </remarks>
public static class Program
{
    private const string DefaultSource = "~/GitHome/frescobaldi/frescobaldi/symbols";

    /// <summary>Matches the pre-2.21 <c>\note #"4."</c> markup form.</summary>
    private static readonly System.Text.RegularExpressions.Regex NoteMarkup
        = new System.Text.RegularExpressions.Regex(
            "\\\\note\\s+#\"([0-9]+\\.*)\"");

    /// <summary>Glyph names the sources use that the engine's font renamed.</summary>
    private static readonly Dictionary<string, string> RenamedGlyphs
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["scripts.upbow"] = "scripts.uupbow",
            ["scripts.downbow"] = "scripts.udownbow",
        };

    /// <summary>Runs the tool.</summary>
    /// <param name="args">The symbol source directory, then the output
    /// directory; both optional.</param>
    /// <returns>Zero on success.</returns>
    public static int Main(string[] args)
    {
        string source = Expand(args.Length > 0 ? args[0] : DefaultSource);
        string output = args.Length > 1
            ? args[1]
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "Fresco.Brix.Core", "assets", "symbols"));

        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine($"symbol sources not found: {source}");
            return 1;
        }

        Directory.CreateDirectory(output);

        //Everything is engraved in a scratch copy so the checkout stays
        //untouched and the .ily includes resolve beside the .ly files.
        string scratch = Path.Combine(
            Path.GetTempPath(), "frescobrix-symbolicons");
        if (Directory.Exists(scratch)) { Directory.Delete(scratch, true); }

        Directory.CreateDirectory(scratch);
        foreach (var file in Directory.EnumerateFiles(source)
            .Where(f => f.EndsWith(".ly", StringComparison.Ordinal)
                || f.EndsWith(".ily", StringComparison.Ordinal)))
        {
            File.Copy(file, Path.Combine(scratch, Path.GetFileName(file)));
        }

        //The sources declare \version "2.18.0" and one construct really has
        //changed since: LilyPond 2.21.0 made \markup \note take a DURATION
        //rather than a string, and the engine's 2.27.2 grammar rejects the old
        //form. This is the very rewrite convert-ly's own 2.21.0 rule performs;
        //W8 brings the whole rules engine, and this local fix-up goes when it
        //arrives.
        //was previously: nothing - the 11 note_*.ly symbols simply failed to
        //engrave, and their buttons had no icon.
        foreach (var file in Directory.EnumerateFiles(scratch, "*.ly"))
        {
            string text = File.ReadAllText(file);
            string updated = NoteMarkup.Replace(
                text, m => "\\note {" + m.Groups[1].Value + "}");

            //Two glyph names in the sources no longer exist: the bowing marks
            //gained the up/down-position prefix every other script already had
            //(scripts.ufermata, scripts.umarcato, ...) and the bare names went.
            //The "u" (above-the-staff) form is the one a toolbar button wants.
            //was previously: nothing - articulation_upbow and
            //articulation_downbow engraved to an empty page and their buttons
            //showed no glyph.
            foreach (var pair in RenamedGlyphs)
            {
                updated = updated.Replace(pair.Key, pair.Value);
            }

            if (text != updated) { File.WriteAllText(file, updated); }
        }

        List<string> sources = Directory.EnumerateFiles(scratch, "*.ly")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine($"engraving {sources.Count} symbols into {output}");
        Stopwatch clock = Stopwatch.StartNew();
        int written = 0;
        int failed = 0;

        foreach (var file in sources)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                BatchRunResult result = BatchRunner.RunFile(
                    file,
                    scratch,
                    null,
                    new BatchRunOptions { PointAndClick = false });

                string page = result.SvgPaths.FirstOrDefault();
                if (page == null)
                {
                    Console.Error.WriteLine($"  {name}: no page written");
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        Console.Error.WriteLine($"      {diagnostic}");
                    }

                    failed++;
                    continue;
                }

                File.Copy(page, Path.Combine(output, name + ".svg"), true);
                written++;
                if (written % 20 == 0)
                {
                    Console.WriteLine(
                        $"  [{clock.Elapsed:mm\\:ss}] {written}/{sources.Count}");
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"  {name}: {error.Message}");
                failed++;
            }
        }

        Console.WriteLine(
            $"done in {clock.Elapsed:mm\\:ss}: {written} written, {failed} failed");
        return failed == 0 ? 0 : 2;
    }

    private static string Expand(string path)
        => path.StartsWith('~')
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path.TrimStart('~', '/'))
            : path;
}
