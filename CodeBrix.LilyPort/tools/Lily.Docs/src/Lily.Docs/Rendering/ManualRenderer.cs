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
using CodeBrix.Texinfo2Html;
using CodeBrix.Texinfo2Pdf;
using Lily.Docs.Manuals;

namespace Lily.Docs.Rendering;

/// <summary>
/// Renders one manual to HTML and to PDF through the published CodeBrix.Texinfo
/// packages.
/// <para>
/// Decision D28 governs what this class may do: it CONSUMES
/// <c>CodeBrix.Texinfo2Html</c> and <c>CodeBrix.Texinfo2Pdf</c> and never converts
/// anything itself. A rendering defect is fixed in the rendering layer here or in the
/// package family's own repository — never by editing generated output, the
/// generator, or a vendored source file.
/// </para>
/// </summary>
public sealed class ManualRenderer
{
    private readonly RenderPaths _paths;

    /// <summary>Creates a renderer over a set of resolved input directories.</summary>
    /// <param name="paths">Where the inputs live.</param>
    public ManualRenderer(RenderPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>Renders a manual to HTML.</summary>
    /// <param name="manual">The manual to render.</param>
    /// <param name="outputDirectory">Directory to write the HTML and CSS into.</param>
    /// <returns>What the render produced.</returns>
    public ManualHtmlRender RenderHtml(ManualDefinition manual, string outputDirectory)
    {
        if (manual == null)
        {
            throw new ArgumentNullException(nameof(manual));
        }

        string sourcePath = _paths.ResolveSource(manual);
        TexinfoHtmlRenderer renderer = new TexinfoHtmlRenderer();
        ApplyIncludePaths(renderer.Options);

        Stopwatch clock = Stopwatch.StartNew();
        TexinfoHtmlResult result = renderer.GenerateFromFile(sourcePath);
        string htmlPath = result.WriteToDirectory(outputDirectory, manual.Name);
        clock.Stop();

        return new ManualHtmlRender(result, htmlPath, clock.Elapsed);
    }

    /// <summary>Renders a manual to PDF.</summary>
    /// <param name="manual">The manual to render.</param>
    /// <param name="outputPdfPath">The PDF file to write.</param>
    /// <returns>What the render produced.</returns>
    public ManualPdfRender RenderPdf(ManualDefinition manual, string outputPdfPath)
    {
        if (manual == null)
        {
            throw new ArgumentNullException(nameof(manual));
        }

        string sourcePath = _paths.ResolveSource(manual);
        TexinfoPdfRenderer renderer = new TexinfoPdfRenderer();

        // Options.Texinfo is the LIVE options object of the HTML renderer underneath,
        // not a copy — the package's own API note. Setting include paths on it is
        // therefore the same act as setting them for the HTML render above, which is
        // what keeps the two outputs made from the same resolution of the same
        // includes.
        ApplyIncludePaths(renderer.Options.Texinfo);

        Stopwatch clock = Stopwatch.StartNew();
        TexinfoPdfResult result = renderer.RenderFile(sourcePath, outputPdfPath);
        clock.Stop();

        return new ManualPdfRender(result, outputPdfPath, clock.Elapsed);
    }

    private void ApplyIncludePaths(TexinfoHtmlOptions options)
    {
        // ⚠ ORDER DECIDES WHAT A MANUAL MEANS. A wrong order silently resolves an
        // include to the wrong file, which reads as content loss rather than as an
        // error. Generated output comes FIRST — the port's own bytes are the
        // specification — then the corpus, then the vendored assets, then the
        // version stand-in.
        options.IncludeSearchPaths.Clear();
        foreach (string path in _paths.IncludeSearchPaths)
        {
            options.IncludeSearchPaths.Add(path);
        }
    }
}

/// <summary>
/// The resolved input directories one render reads from.
/// </summary>
public sealed class RenderPaths
{
    /// <summary>Creates a path set.</summary>
    /// <param name="generatedDirectory">Where the port's nineteen files were written.</param>
    /// <param name="assetsDirectory">The vendored-asset ROOT — the directory CONTAINING
    /// <c>en/</c>, because manuals include <c>en/macros.itexi</c> by that path.</param>
    /// <param name="versionDirectory">Where the <c>version.itexi</c> stand-in was written.</param>
    /// <param name="corpusDirectory">The repository's Documentation mirror, or null when a
    /// manual needs no corpus text.</param>
    public RenderPaths(string generatedDirectory, string assetsDirectory,
        string versionDirectory, string corpusDirectory)
    {
        GeneratedDirectory = generatedDirectory;
        AssetsDirectory = assetsDirectory;
        VersionDirectory = versionDirectory;
        CorpusDirectory = corpusDirectory;

        List<string> paths = new List<string> { generatedDirectory };
        if (!string.IsNullOrEmpty(corpusDirectory))
        {
            paths.Add(corpusDirectory);
        }

        paths.Add(assetsDirectory);
        paths.Add(versionDirectory);
        IncludeSearchPaths = paths;
    }

    /// <summary>Where the port's nineteen generated files live.</summary>
    public string GeneratedDirectory { get; }

    /// <summary>The vendored-asset root.</summary>
    public string AssetsDirectory { get; }

    /// <summary>Where the version.itexi stand-in was written.</summary>
    public string VersionDirectory { get; }

    /// <summary>The Documentation mirror, or null.</summary>
    public string CorpusDirectory { get; }

    /// <summary>The include search paths, in resolution order.</summary>
    public IReadOnlyList<string> IncludeSearchPaths { get; }

    /// <summary>Resolves a manual's root source file.</summary>
    /// <param name="manual">The manual.</param>
    /// <returns>The full path of its root source file.</returns>
    /// <exception cref="FileNotFoundException">The file is not where its kind says it is.</exception>
    public string ResolveSource(ManualDefinition manual)
    {
        string directory = manual.SourceKind == ManualSourceKind.Generated
            ? GeneratedDirectory
            : Path.Combine(CorpusDirectory ?? string.Empty, "en");
        string path = Path.Combine(directory, manual.FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"the source for manual '{manual.Name}' is not at {path}", path);
        }

        return path;
    }
}

/// <summary>What an HTML render produced.</summary>
public sealed class ManualHtmlRender
{
    internal ManualHtmlRender(TexinfoHtmlResult result, string htmlPath, TimeSpan elapsed)
    {
        Result = result;
        HtmlPath = htmlPath;
        Elapsed = elapsed;
    }

    /// <summary>The package's own result — markup, stylesheet, images and warnings.</summary>
    public TexinfoHtmlResult Result { get; }

    /// <summary>The full path of the written HTML file.</summary>
    public string HtmlPath { get; }

    /// <summary>How long the render took.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>The render's warning messages.</summary>
    public IReadOnlyList<string> Warnings => Result.Warnings.Messages;
}

/// <summary>What a PDF render produced.</summary>
public sealed class ManualPdfRender
{
    internal ManualPdfRender(TexinfoPdfResult result, string pdfPath, TimeSpan elapsed)
    {
        Result = result;
        PdfPath = pdfPath;
        Elapsed = elapsed;
    }

    /// <summary>The package's own result.</summary>
    public TexinfoPdfResult Result { get; }

    /// <summary>The full path of the written PDF.</summary>
    public string PdfPath { get; }

    /// <summary>How long the render took.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>The page count the renderer reported.</summary>
    public int PageCount => Result.PageCount;

    /// <summary>Warnings from the Texinfo stage, untagged.</summary>
    public IReadOnlyList<string> TexinfoWarnings => Result.Warnings.TexinfoMessages;

    /// <summary>Warnings from the PDF stage, untagged.</summary>
    public IReadOnlyList<string> PdfWarnings => Result.Warnings.PdfMessages;
}
