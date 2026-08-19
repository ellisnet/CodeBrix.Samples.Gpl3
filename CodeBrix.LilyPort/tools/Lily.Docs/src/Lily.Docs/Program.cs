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
using Lily.Docs.Generation;
using Lily.Docs.Manuals;
using Lily.Docs.Rendering;

namespace Lily.Docs;

/// <summary>
/// The Lily.Docs command line: generates the port's documentation and renders a manual
/// from it.
/// <para>
/// Usage: <c>Lily.Docs MANUAL [--html] [--pdf] [-o DIR] [--generated DIR]
/// [--baseline]</c>. With neither <c>--html</c> nor <c>--pdf</c>, both are rendered.
/// </para>
/// <para>
/// This is a REPO TOOL, not a shipped assembly (decision D52, ruled 2026-08-18) — the
/// Texinfo and Html2Pdf dependency chain deliberately stops here and does not reach
/// CodeBrix.LilyPort's own package.
/// </para>
/// </summary>
public static class Program
{
    /// <summary>Runs the tool.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>0 on success; 2 on a usage error; 3 when a render produced nothing;
    /// 4 when the run threw.</returns>
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception) when (!(exception is OutOfMemoryException))
        {
            Console.Error.WriteLine(exception);
            return 4;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            WriteUsage();
            return args.Length == 0 ? 2 : 0;
        }

        string manualName = args[0];
        ManualDefinition manual = ManualCatalog.Find(manualName);
        if (manual == null)
        {
            Console.Error.WriteLine($"unknown manual '{manualName}'");
            WriteUsage();
            return 2;
        }

        bool wantHtml = false;
        bool wantPdf = false;
        bool freezeBaseline = false;
        bool listWarnings = false;
        string outputDirectory = null;
        string generatedDirectory = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--html":
                    wantHtml = true;
                    break;
                case "--pdf":
                    wantPdf = true;
                    break;
                case "--baseline":
                    freezeBaseline = true;
                    break;
                case "--warnings":
                    listWarnings = true;
                    break;
                case "-o":
                case "--output":
                    if (++i >= args.Length)
                    {
                        Console.Error.WriteLine("-o needs a directory");
                        return 2;
                    }

                    outputDirectory = args[i];
                    break;
                case "--generated":
                    if (++i >= args.Length)
                    {
                        Console.Error.WriteLine("--generated needs a directory");
                        return 2;
                    }

                    generatedDirectory = args[i];
                    break;
                default:
                    Console.Error.WriteLine($"unknown option '{args[i]}'");
                    return 2;
            }
        }

        if (!wantHtml && !wantPdf)
        {
            wantHtml = true;
            wantPdf = true;
        }

        outputDirectory = Path.GetFullPath(outputDirectory ?? Path.Combine(".", "lilydocs-out"));
        Directory.CreateDirectory(outputDirectory);

        // Generation is skipped when the caller points at files a previous run wrote.
        // That is not a shortcut for a gate — the gates generate for themselves — it is
        // for the QA loop, where re-rendering the same bytes twenty times should not
        // re-run an eighty-second engine job twenty times.
        if (generatedDirectory == null)
        {
            generatedDirectory = Path.Combine(outputDirectory, "generated");
            Console.WriteLine($"generating the nineteen documentation files into {generatedDirectory} ...");
            DocumentationGenerationResult generation =
                new DocumentationGenerator().Generate(generatedDirectory);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} of {1} files written in {2:0.0}s",
                DocumentationGenerator.ExpectedOutputs.Count - generation.MissingFiles.Count,
                DocumentationGenerator.ExpectedOutputs.Count,
                generation.Elapsed.TotalSeconds));
            foreach (string missing in generation.MissingFiles)
            {
                Console.Error.WriteLine("  MISSING\t" + missing);
            }

            if (!generation.IsComplete)
            {
                return 3;
            }
        }
        else
        {
            generatedDirectory = Path.GetFullPath(generatedDirectory);
            Console.WriteLine($"using already-generated files in {generatedDirectory}");
        }

        string versionDirectory = Path.Combine(outputDirectory, "version");
        VersionItexiWriter.Write(versionDirectory);

        RenderPaths paths = new RenderPaths(
            generatedDirectory, ToolPaths.AssetsDirectory, versionDirectory,
            manual.SourceKind == ManualSourceKind.Corpus ? ToolPaths.CorpusDirectory : null);
        ManualRenderer renderer = new ManualRenderer(paths);

        List<string> warningsForBaseline = new List<string>();
        ManualPdfRender pdfRender = null;

        if (wantHtml)
        {
            Console.WriteLine($"rendering {manual.Title} to HTML ...");
            ManualHtmlRender html = renderer.RenderHtml(manual, outputDirectory);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}  ({1:0.0}s, {2} warnings, {3} images)",
                html.HtmlPath, html.Elapsed.TotalSeconds, html.Warnings.Count,
                html.Result.Images.Count));
            WriteCategories(html.Warnings);
            if (listWarnings)
            {
                WriteMessages(html.Warnings);
            }

            warningsForBaseline.AddRange(html.Warnings);
        }

        if (wantPdf)
        {
            Console.WriteLine($"rendering {manual.Title} to PDF ...");
            string pdfPath = Path.Combine(outputDirectory, manual.Name + ".pdf");
            ManualPdfRender pdf = renderer.RenderPdf(manual, pdfPath);
            pdfRender = pdf;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}  ({1:0.0}s, {2} pages, {3} texinfo + {4} pdf warnings)",
                pdf.PdfPath, pdf.Elapsed.TotalSeconds, pdf.PageCount,
                pdf.TexinfoWarnings.Count, pdf.PdfWarnings.Count));
            WriteCategories(pdf.PdfWarnings);
            if (listWarnings)
            {
                WriteMessages(pdf.PdfWarnings);
            }
        }

        if (freezeBaseline && wantPdf && pdfRender != null)
        {
            string pdfBaselinePath = Path.Combine(
                ToolPaths.ExpectedWarningsDirectory, manual.Name + "-pdf.tsv");
            WarningSummary.WritePdfBaseline(
                pdfBaselinePath, pdfRender.PageCount, pdfRender.PdfWarnings.Count);
            Console.WriteLine($"pdf baseline written to {pdfBaselinePath}");
        }

        if (freezeBaseline && wantHtml)
        {
            string baselinePath = Path.Combine(
                ToolPaths.ExpectedWarningsDirectory, manual.Name + ".tsv");
            WarningSummary.WriteBaseline(baselinePath, WarningSummary.Count(warningsForBaseline));
            Console.WriteLine($"baseline written to {baselinePath}");
            Console.WriteLine("  ⚠ READ IT before committing. A baseline is frozen from a run");
            Console.WriteLine("    that was reviewed, never regenerated to make a test pass.");
        }

        return 0;
    }

    private static void WriteCategories(IReadOnlyList<string> messages)
    {
        foreach (KeyValuePair<string, int> entry in WarningSummary.Count(messages))
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "    {0,-18} {1}", entry.Key, entry.Value));
        }
    }

    private static void WriteMessages(IReadOnlyList<string> messages)
    {
        foreach (string message in messages)
        {
            Console.WriteLine("      | " + message);
        }
    }

    private static void WriteUsage()
    {
        Console.WriteLine("usage: Lily.Docs MANUAL [--html] [--pdf] [-o DIR] [--generated DIR] [--baseline]");
        Console.WriteLine();
        Console.WriteLine("  MANUAL       one of: " + string.Join(", ", ManualNames()));
        Console.WriteLine("  --html       render HTML (default: both)");
        Console.WriteLine("  --pdf        render PDF  (default: both)");
        Console.WriteLine("  -o DIR       output directory (default: ./lilydocs-out)");
        Console.WriteLine("  --generated DIR");
        Console.WriteLine("               use documentation files a previous run wrote,");
        Console.WriteLine("               instead of generating them again");
        Console.WriteLine("  --baseline   freeze the expected-warnings baseline from this run");
        Console.WriteLine("  --warnings   print every warning message, not just the counts");
    }

    private static IEnumerable<string> ManualNames()
    {
        foreach (ManualDefinition manual in ManualCatalog.All)
        {
            yield return manual.Name;
        }
    }
}
