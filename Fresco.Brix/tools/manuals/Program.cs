// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.PdfRasterizer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Fresco.Brix.Tools.Manuals;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Renders the nine manuals with LilyPort's own Lily.Docs and installs them as
/// the application's documentation assets.
/// </summary>
/// <remarks>
/// <para>
/// Board wave W10, and decision D52 stays intact both ways: Lily.Docs ships
/// nothing and CodeBrix.LilyPort never grows a documentation dependency, while
/// Fresco.Brix bundles the PDFs those renders produced. Nothing at APPLICATION
/// runtime renders a manual; the app opens files that were made here.
/// </para>
/// <para>
/// ⚠ THE CodeBrix.LilyPort REPOSITORY IS AN ARGUMENT, NOT A GUESS. Lily.Docs
/// lives in that repository's <c>tools</c> folder and is driven by its command
/// line, so a person running this tool has to say where that checkout is:
/// <c>--lilyport-root &lt;dir&gt;</c>, required, no default. Fresco.Brix itself
/// consumes the engine as the published nuget package
/// (<c>CodeBrix.LilyPort.GplLicenseForever</c>, board row W13b) and holds no
/// path into that repository anywhere else; the two are unrelated checkouts
/// that need not be siblings, or both present.
/// //was previously: the root was assumed to be
/// <c>&lt;Fresco.Brix&gt;/../CodeBrix.LilyPort</c>, from when the two were
/// siblings inside CodeBrix.Samples.Gpl3. LilyPort moved to its own repository
/// on 2026-08-27 and that path stopped existing.
/// </para>
/// <para>
/// ⚠ GENERATION IS ONCE PER PROCESS (board trap 15, and Lily.Docs' own README
/// says so first). The nineteen generated documentation files are an engine job
/// of roughly forty seconds, and a second generation in the same process does
/// not throw — it reports every file missing and renders out of an empty
/// directory. This tool therefore renders the FIRST manual in a process of its
/// own, letting it generate, and hands every later render
/// <c>--generated &lt;dir&gt;/generated/en</c>.
/// </para>
/// <para>
/// ⚠ THE MANUALS ARE GFDL, NOT GPL. They are documentation, aggregated with the
/// application and never intermixed with its source; <c>COPYING.FDL</c> is
/// copied in beside them, which is what the licence itself requires, and
/// THIRD-PARTY-NOTICES.txt section 9 records the position. Do not move a
/// manual out of the <c>docs</c> folder and do not copy text out of one.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>The manuals, in the order the documentation panel lists them.</summary>
    /// <remarks>
    /// Board decision D48's nine, in READING order rather than Lily.Docs'
    /// command-line order: a person new to the language opens the Learning
    /// Manual, and a person looking something up opens the Notation Reference.
    /// The order here is the order <c>ManualCatalog</c> declares, and the two
    /// are checked against each other by <c>ManualCatalogTests</c>.
    /// </remarks>
    private static readonly string[] Manuals =
    {
        "learning", "notation", "usage", "extending", "internals",
        "essay", "music-glossary", "changes", "contributor",
    };

    /// <summary>Runs the tool.</summary>
    /// <param name="args">See <c>--help</c>.</param>
    /// <returns>Zero on success.</returns>
    public static async Task<int> Main(string[] args)
    {
        string renderDirectory = null;
        string assetsDirectory = null;
        string lilyPortRoot = null;
        bool skipRender = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    WriteUsage();
                    return 0;
                case "--skip-render":
                    skipRender = true;
                    break;
                case "-o":
                case "--output":
                    if (++i >= args.Length) { Console.Error.WriteLine("-o needs a directory"); return 2; }

                    assetsDirectory = args[i];
                    break;
                case "--render-dir":
                    if (++i >= args.Length) { Console.Error.WriteLine("--render-dir needs a directory"); return 2; }

                    renderDirectory = args[i];
                    break;
                case "--lilyport-root":
                    if (++i >= args.Length)
                    {
                        Console.Error.WriteLine("--lilyport-root needs a directory");
                        return 2;
                    }

                    lilyPortRoot = args[i];
                    break;
                default:
                    Console.Error.WriteLine($"unknown option '{args[i]}'");
                    WriteUsage();
                    return 2;
            }
        }

        string repositoryRoot = FindFrescoBrixRoot();
        if (repositoryRoot == null)
        {
            Console.Error.WriteLine("could not find Fresco.Brix.slnx above " + AppContext.BaseDirectory);
            return 2;
        }

        //REQUIRED, with no default. The CodeBrix.LilyPort checkout that holds
        //Lily.Docs is a separate repository with no fixed relationship to this
        //one, so the only honest answer is the one the caller gives.
        if (string.IsNullOrWhiteSpace(lilyPortRoot))
        {
            Console.Error.WriteLine(
                "--lilyport-root is required: pass the CodeBrix.LilyPort repository root "
                + "(the folder holding CodeBrix.LilyPort.slnx), which is where Lily.Docs lives");
            WriteUsage();
            return 2;
        }

        lilyPortRoot = Path.GetFullPath(lilyPortRoot);
        if (!File.Exists(Path.Combine(lilyPortRoot, "CodeBrix.LilyPort.slnx")))
        {
            Console.Error.WriteLine(
                "--lilyport-root does not name a CodeBrix.LilyPort repository: no "
                + "CodeBrix.LilyPort.slnx in " + lilyPortRoot);
            return 2;
        }

        assetsDirectory = Path.GetFullPath(assetsDirectory ?? Path.Combine(
            repositoryRoot, "src", "Fresco.Brix.Core", "assets", "docs"));
        renderDirectory = Path.GetFullPath(renderDirectory ?? Path.Combine(
            Path.GetTempPath(), "frescobrix-manuals"));

        Directory.CreateDirectory(assetsDirectory);
        Directory.CreateDirectory(renderDirectory);

        Console.WriteLine("Fresco.Brix manuals pipeline");
        Console.WriteLine("  LilyPort      " + lilyPortRoot);
        Console.WriteLine("  rendering to  " + renderDirectory);
        Console.WriteLine("  installing to " + assetsDirectory);
        Console.WriteLine();

        if (!skipRender && !Render(lilyPortRoot, renderDirectory)) { return 3; }

        return await Install(lilyPortRoot, renderDirectory, assetsDirectory).ConfigureAwait(false)
            ? 0
            : 3;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("usage: Manuals --lilyport-root DIR [--render-dir DIR] [-o ASSETS_DIR]");
        Console.WriteLine("               [--skip-render]");
        Console.WriteLine();
        Console.WriteLine("  --lilyport-root DIR  REQUIRED. The CodeBrix.LilyPort repository root:");
        Console.WriteLine("                       the folder holding CodeBrix.LilyPort.slnx, whose");
        Console.WriteLine("                       tools/Lily.Docs renders the manuals and whose");
        Console.WriteLine("                       COPYING.FDL ships beside them. There is no");
        Console.WriteLine("                       default: that repository is a separate checkout");
        Console.WriteLine("                       and Fresco.Brix uses the engine as a nuget");
        Console.WriteLine("                       package, so it need not be anywhere in");
        Console.WriteLine("                       particular.");
        Console.WriteLine("  --render-dir DIR     where Lily.Docs renders (default: a temp directory)");
        Console.WriteLine("  -o ASSETS_DIR        where the PDFs are installed");
        Console.WriteLine("                       (default: src/Fresco.Brix.Core/assets/docs)");
        Console.WriteLine("  --skip-render        install from an existing render directory");
        Console.WriteLine();
        Console.WriteLine("Renders the nine manuals (about ten minutes; the Notation Reference");
        Console.WriteLine("is five of them and 2,555 engravings), then writes the PDFs,");
        Console.WriteLine("COPYING.FDL and MANIFEST.txt into the assets directory.");
        Console.WriteLine();
        Console.WriteLine("example:");
        Console.WriteLine("  Manuals --lilyport-root ~/GitHome/CodeBrix.LilyPort");
    }

    /// <summary>Renders the nine manuals to PDF with Lily.Docs.</summary>
    /// <param name="lilyPortRoot">
    /// The CodeBrix.LilyPort repository root, as given by <c>--lilyport-root</c>.
    /// </param>
    /// <param name="renderDirectory">Where the PDFs are written.</param>
    /// <returns>True when all nine rendered.</returns>
    private static bool Render(string lilyPortRoot, string renderDirectory)
    {
        string project = Path.Combine(
            lilyPortRoot, "tools", "Lily.Docs", "src", "Lily.Docs", "Lily.Docs.csproj");
        string generated = Path.Combine(renderDirectory, "generated", "en");

        Stopwatch total = Stopwatch.StartNew();
        for (int i = 0; i < Manuals.Length; i++)
        {
            string manual = Manuals[i];
            List<string> arguments = new List<string>
            {
                "run", "--project", project, "-c", "Release",
            };

            //Built once, then reused: nine `dotnet run` calls that each restore
            //and build would spend longer deciding nothing had changed than
            //the first eight renders take.
            if (i > 0) { arguments.Add("--no-build"); }

            arguments.Add("--");
            arguments.Add(manual);
            arguments.Add("--pdf");
            arguments.Add("-o");
            arguments.Add(renderDirectory);

            //The first render GENERATES the nineteen documentation files; every
            //later one reuses them, because generation is once per process
            //(board trap 15).
            if (i > 0)
            {
                arguments.Add("--generated");
                arguments.Add(generated);
            }

            Console.WriteLine(Stamp() + " rendering " + manual + " ...");
            Stopwatch one = Stopwatch.StartNew();
            int exit = RunDotnet(arguments, lilyPortRoot);
            one.Stop();
            if (exit != 0)
            {
                Console.Error.WriteLine($"  {manual}: Lily.Docs exited {exit}");
                return false;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} in {1:0.0}s", manual, one.Elapsed.TotalSeconds));
        }

        total.Stop();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0} nine manuals rendered in {1:0} min {2:00} s",
            Stamp(), Math.Floor(total.Elapsed.TotalMinutes), total.Elapsed.Seconds));
        return true;
    }

    /// <summary>
    /// Copies the rendered PDFs and the licence into the assets directory and
    /// writes the manifest.
    /// </summary>
    /// <param name="lilyPortRoot">
    /// The CodeBrix.LilyPort repository root, as given by <c>--lilyport-root</c>;
    /// its <c>COPYING.FDL</c> is the copy installed beside the manuals.
    /// </param>
    /// <param name="renderDirectory">Where Lily.Docs wrote the PDFs.</param>
    /// <param name="assetsDirectory">Where the application reads them.</param>
    /// <returns>True when every manual was installed.</returns>
    private static async Task<bool> Install(
        string lilyPortRoot, string renderDirectory, string assetsDirectory)
    {
        //The licence travels WITH the documents: the GNU FDL requires a copy of
        //itself to accompany the work, and this folder is what a packager can
        //point at. Verbatim from LilyPort's own repository root, which is in
        //turn verbatim from upstream lilypond/COPYING.FDL.
        string license = Path.Combine(lilyPortRoot, "COPYING.FDL");
        if (!File.Exists(license))
        {
            Console.Error.WriteLine("COPYING.FDL is not at " + license);
            return false;
        }

        File.Copy(license, Path.Combine(assetsDirectory, "COPYING.FDL"), overwrite: true);
        Console.WriteLine("  COPYING.FDL");

        using PageRasterizer rasterizer = new PageRasterizer();
        StringBuilder manifest = new StringBuilder();
        manifest.AppendLine("# Fresco.Brix bundled manuals — MANIFEST");
        manifest.AppendLine("#");
        manifest.AppendLine("# Written by tools/manuals. Every row is one PDF this folder ships:");
        manifest.AppendLine("#     <name>  <pages>  <bytes>  <sha256>");
        manifest.AppendLine("# The page counts are what ManualCatalog declares and what");
        manifest.AppendLine("# ManualCatalogTests reads back out of the shipped file.");
        manifest.AppendLine();

        bool ok = true;
        long totalBytes = 0;
        int totalPages = 0;
        foreach (var manual in Manuals)
        {
            string source = Path.Combine(renderDirectory, manual + ".pdf");
            if (!File.Exists(source))
            {
                Console.Error.WriteLine("  MISSING\t" + source);
                ok = false;
                continue;
            }

            string target = Path.Combine(assetsDirectory, manual + ".pdf");
            File.Copy(source, target, overwrite: true);

            long bytes = new FileInfo(target).Length;
            int pages = await rasterizer.GetPageCount(target).ConfigureAwait(false);
            string hash = Sha256(target);

            totalBytes += bytes;
            totalPages += pages;
            manifest.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-15}{1,6}{2,12}  {3}", manual, pages, bytes, hash));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-15}{1,6} pages{2,12} bytes", manual, pages, bytes));
        }

        manifest.AppendLine();
        manifest.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-15}{1,6}{2,12}", "TOTAL", totalPages, totalBytes));
        File.WriteAllText(Path.Combine(assetsDirectory, "MANIFEST.txt"), manifest.ToString());

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  TOTAL          {0,6} pages{1,12} bytes ({2:0.0} MB)",
            totalPages, totalBytes, totalBytes / 1048576.0));
        return ok;
    }

    private static int RunDotnet(IEnumerable<string> arguments, string workingDirectory)
    {
        ProcessStartInfo start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }

        using Process process = Process.Start(start);
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Stamp()
        => "[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "]";

    private static string FindFrescoBrixRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Fresco.Brix.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
