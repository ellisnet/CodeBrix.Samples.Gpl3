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
using Lily.Docs.Snippets;

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

    /// <summary>
    /// The engraver a manual's music snippets are handed to, or null to render with none.
    /// <para>
    /// ⚠ NULL IS NOT A NEUTRAL SETTING. With no engraver registered the package shows every
    /// snippet as source text and says so in ONE warning — which is precisely what a manual
    /// whose every engraving failed also looks like. A manual that carries music is rendered
    /// with an engraver AND gated on its invocation and failure counts;
    /// <see cref="ManualDefinition.EngravesSnippets"/> is where each manual states which of
    /// the two it is.
    /// </para>
    /// </summary>
    public ILilypondSnippetRenderer SnippetRenderer { get; set; }

    /// <summary>
    /// The page geometry a manual implies, read from its own root source file the way
    /// lilypond-book's <c>get_texinfo_width_indent</c> reads it — from the page-size command
    /// the document declares.
    /// </summary>
    /// <param name="manual">The manual.</param>
    /// <returns>The geometry its own text implies.</returns>
    /// <remarks>
    /// Read rather than assumed. All nine manuals in D48's scope declare
    /// <c>@afourpaper</c>, but a geometry taken as read is a constant that stops tracking
    /// its source: the width it yields is written into every snippet's composed source, so a
    /// manual that ever declared a different page size would engrave at the wrong width with
    /// nothing to say so.
    /// </remarks>
    public TexinfoPageGeometry GeometryOf(ManualDefinition manual)
    {
        if (manual == null)
        {
            throw new ArgumentNullException(nameof(manual));
        }

        return TexinfoPageGeometry.ForSource(File.ReadAllText(_paths.ResolveSource(manual)));
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
        ApplyOptions(renderer.Options);

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
        ApplyOptions(renderer.Options.Texinfo);

        Stopwatch clock = Stopwatch.StartNew();
        TexinfoPdfResult result = renderer.RenderFile(sourcePath, outputPdfPath);
        clock.Stop();

        return new ManualPdfRender(result, outputPdfPath, clock.Elapsed);
    }

    private void ApplyOptions(TexinfoHtmlOptions options)
    {
        // ⚠ ORDER DECIDES WHAT A MANUAL MEANS. A wrong order silently resolves an
        // include to the wrong file, which reads as content loss rather than as an
        // error. Generated output comes FIRST — the port's own bytes are the
        // specification — then the corpus, then the vendored assets, then the
        // version stand-in.
        //
        // ⚠ AND THIS LIST IS NOT THE WHOLE SEARCH PATH. The package puts the source
        // file's own directory and that directory's PARENT ahead of everything here
        // (TexinfoHtmlRenderer.BuildFileSearchPaths), which for a corpus manual means
        // Documentation/en and Documentation come before the generated files. That is
        // harmless only because the mirror holds NONE of the port's nineteen outputs —
        // they are build products there exactly as they are upstream. CorpusMirrorTests
        // asserts that, because it is the assumption "generated first" actually rests on.
        options.IncludeSearchPaths.Clear();
        foreach (string path in _paths.IncludeSearchPaths)
        {
            options.IncludeSearchPaths.Add(path);
        }

        options.SnippetRenderer = SnippetRenderer;
    }
}

/// <summary>
/// The resolved input directories one render reads from.
/// </summary>
public sealed class RenderPaths
{
    /// <summary>
    /// The directory name the port's generated files must sit in.
    /// <para>
    /// The manuals include them by LANGUAGE-QUALIFIED name —
    /// <c>@include en/markup-commands.tely</c>, eighteen times over — while the generator
    /// writes them by bare name into whatever directory it is given. The two are reconciled
    /// the way upstream reconciles them: the output directory is called <c>en</c> and its
    /// PARENT goes on the search path, which is exactly what <c>Documentation/GNUmakefile</c>
    /// does with <c>-I $(outdir)/en -I $(outdir)</c>.
    /// </para>
    /// </summary>
    public const string GeneratedDirectoryName = "en";

    /// <summary>Creates a path set.</summary>
    /// <param name="generatedDirectory">Where the port's nineteen files were written. It must
    /// be a directory named <c>en</c> — see <see cref="GeneratedDirectoryName"/>.</param>
    /// <param name="assetsDirectory">The vendored-asset ROOT — the directory CONTAINING
    /// <c>en/</c>, because manuals include <c>en/macros.itexi</c> by that path.</param>
    /// <param name="versionDirectory">Where the <c>version.itexi</c> stand-in was written.</param>
    /// <param name="corpusDirectory">The repository's Documentation mirror, or null when a
    /// manual needs no corpus text.</param>
    /// <exception cref="ArgumentException">The generated directory is not named <c>en</c>,
    /// so eighteen of the port's own files could not be included by the name the manuals
    /// use for them.</exception>
    public RenderPaths(string generatedDirectory, string assetsDirectory,
        string versionDirectory, string corpusDirectory)
    {
        GeneratedDirectory = Path.GetFullPath(generatedDirectory);
        AssetsDirectory = assetsDirectory;
        VersionDirectory = versionDirectory;
        CorpusDirectory = corpusDirectory;

        // Checked rather than documented. Getting this wrong does not fail: the eighteen
        // generated fragments simply do not resolve, the render finishes, and the manual is
        // missing its appendices behind eighteen Include warnings among however many others
        // the baseline already tolerates.
        if (!string.Equals(Path.GetFileName(GeneratedDirectory), GeneratedDirectoryName,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "the generated directory must be named '" + GeneratedDirectoryName
                + "', because the manuals include the port's own files as '"
                + GeneratedDirectoryName + "/<name>'; it is " + GeneratedDirectory,
                nameof(generatedDirectory));
        }

        GeneratedIncludeRoot = Path.GetDirectoryName(GeneratedDirectory);

        // The order below reproduces Documentation/GNUmakefile's own two lists —
        // DOCUMENTATION_INCLUDE_DIRS for @include and LILYPOND_BOOK_INCLUDE_DIRS for the
        // files @lilypondfile names, which this package resolves from the same set:
        //
        //     $(outdir)/en          the port's generated files, by bare name
        //     $(outdir)             the same files, by the en/ name the manuals use
        //     $(src-dir)            the corpus root: en/... and snippets/... resolve here
        //     $(src-dir)/en/included    the manuals' own .ly companions, by BARE name —
        //                               one @lilypondfile in chords.itely needs exactly this
        //
        // then this port's own two additions: the vendored GFDL macro root and the directory
        // the version.itexi stand-in was written into.
        List<string> paths = new List<string> { GeneratedDirectory };
        if (!string.IsNullOrEmpty(GeneratedIncludeRoot))
        {
            paths.Add(GeneratedIncludeRoot);
        }

        if (!string.IsNullOrEmpty(corpusDirectory))
        {
            paths.Add(corpusDirectory);
            paths.Add(Path.Combine(corpusDirectory, "en", "included"));
        }

        paths.Add(assetsDirectory);
        paths.Add(versionDirectory);
        IncludeSearchPaths = paths;

        // ── The ENGINE's include path, which is a different list ──────────────────
        //
        // The list above is where the TEXINFO renderer looks for @include files and for
        // the files @lilypondfile names. This one is where the ENGINE looks for the files
        // a snippet's own \include and \epsfile name, and it reproduces
        // Documentation/GNUmakefile's LILYPOND_BOOK_INCLUDE_DIRS:
        //
        //     $(outdir)                 the generated directory and its parent
        //     $(src-dir)/en             the manual's own language directory
        //     $(src-dir)/pictures       \epsfile targets
        //     $(src-dir)/en/included    the manuals' .ly companions, by BARE name
        //     $(src-dir)                paths written as en/included/x.ly or snippets/x.ly
        //
        // ⚠ MEASURED, NOT ANTICIPATED. Without it the notation manual lost 76 engravings:
        // 46 \include "neume-demo-layout.ly", 26 \include "en/included/font-table.ly",
        // and four more — and NOT as missing-file errors. An unresolved \include leaves
        // the identifiers it would have defined undefined, so what the engine reports is a
        // SYNTAX ERROR at the line that uses one. Nothing in the message says "include".
        List<string> snippetPaths = new List<string> { GeneratedDirectory };
        if (!string.IsNullOrEmpty(GeneratedIncludeRoot))
        {
            snippetPaths.Add(GeneratedIncludeRoot);
        }

        if (!string.IsNullOrEmpty(corpusDirectory))
        {
            snippetPaths.Add(Path.Combine(corpusDirectory, "en"));
            snippetPaths.Add(Path.Combine(corpusDirectory, "pictures"));
            snippetPaths.Add(Path.Combine(corpusDirectory, "en", "included"));
            snippetPaths.Add(corpusDirectory);
        }

        SnippetIncludePaths = snippetPaths;
    }

    /// <summary>Where the port's nineteen generated files live. Always named <c>en</c>.</summary>
    public string GeneratedDirectory { get; }

    /// <summary>
    /// The PARENT of <see cref="GeneratedDirectory"/> — what an
    /// <c>@include en/markup-commands.tely</c> resolves against.
    /// </summary>
    public string GeneratedIncludeRoot { get; }

    /// <summary>The vendored-asset root.</summary>
    public string AssetsDirectory { get; }

    /// <summary>Where the version.itexi stand-in was written.</summary>
    public string VersionDirectory { get; }

    /// <summary>The Documentation mirror, or null.</summary>
    public string CorpusDirectory { get; }

    /// <summary>The include search paths, in resolution order.</summary>
    public IReadOnlyList<string> IncludeSearchPaths { get; }

    /// <summary>
    /// Where the ENGINE looks for the files a snippet's own <c>\include</c> and
    /// <c>\epsfile</c> name — upstream's <c>LILYPOND_BOOK_INCLUDE_DIRS</c>, which is a
    /// different list from <see cref="IncludeSearchPaths"/> and reaches a different
    /// consumer.
    /// </summary>
    public IReadOnlyList<string> SnippetIncludePaths { get; }

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
