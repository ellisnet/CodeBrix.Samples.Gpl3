// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CodeBrix.PdfDocCreate.Html2Pdf;
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

    /// <summary>
    /// The page size every manual in decision D48's scope is set on.
    /// <para>
    /// ⚠ READ OFF THE MANUALS, NOT CHOSEN. All nine declare <c>@afourpaper</c>, and that
    /// same declaration is what <see cref="TexinfoPageGeometry"/> turns into the 160&#160;mm
    /// line width written into every snippet's composed source. A US-Letter page carrying
    /// music engraved to an A4 measure is not wrong in any way a count can see — which is
    /// exactly why wave LD1's Internals Reference was 612&#215;792 for a day without
    /// anything going red.
    /// </para>
    /// <para>
    /// ⚠ The package's own named helper is used rather than the two point values, so the
    /// size cannot be mistyped into a plausible wrong number. MEASURED 2026-08-19: it
    /// yields 595&#215;842&#160;pt, where true A4 (210&#215;297&#160;mm) is
    /// 595.276&#215;841.890. The 0.28&#160;pt difference is a tenth of a millimetre; it is
    /// recorded in the baseline as the page size actually used, so a package that ever
    /// changed the constant would show up as a baseline that moved.
    /// </para>
    /// </summary>
    public const string PageSizeName = "A4";

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
        PdfRenderSettings settings = ApplyPdfOptions(renderer.Options.Html);

        Stopwatch clock = Stopwatch.StartNew();
        TexinfoPdfResult result = renderer.RenderFile(sourcePath, outputPdfPath);
        clock.Stop();

        return new ManualPdfRender(result, outputPdfPath, clock.Elapsed, settings);
    }

    /// <summary>
    /// Renders a manual to BOTH formats from ONE pass over the Texinfo source.
    /// </summary>
    /// <param name="manual">The manual to render.</param>
    /// <param name="htmlDirectory">Directory to write the HTML and CSS into.</param>
    /// <param name="outputPdfPath">The PDF file to write.</param>
    /// <returns>Both renders, made from one resolution of the source.</returns>
    /// <remarks>
    /// <para>
    /// ⚠ THIS IS NOT AN OPTIMISATION, AND CALLING <see cref="RenderHtml"/> AND
    /// <see cref="RenderPdf"/> IN TURN IS NOT THE SAME ACT. Each of those runs the Texinfo
    /// source from the beginning, and the package's snippet coordinator dedupes only WITHIN
    /// one render — so a manual that carries music is engraved once per format. For the
    /// Notation Reference that is two and a half thousand extra engravings, five extra
    /// minutes, and every engraving count in the baseline doubled.
    /// </para>
    /// <para>
    /// It is also what makes decision D51's premise literally true. The ruling is that
    /// Lily.Docs hands THE SAME SVG to both outputs; two renders would hand each output its
    /// own separately engraved copy, which is a different and weaker claim — and one that
    /// would go unnoticed, because two engravings of the same snippet look alike.
    /// </para>
    /// <para>
    /// The fence is <c>NotationReferenceRenderTests</c>' engraving baseline: it asserts the
    /// ASKED count of a fixture that produced BOTH formats, so a return to two passes shows
    /// up as that number doubling rather than as a slow suite.
    /// </para>
    /// </remarks>
    public ManualRender RenderBoth(ManualDefinition manual, string htmlDirectory,
        string outputPdfPath)
    {
        if (manual == null)
        {
            throw new ArgumentNullException(nameof(manual));
        }

        string sourcePath = _paths.ResolveSource(manual);
        TexinfoPdfRenderer renderer = new TexinfoPdfRenderer();

        // Options.Texinfo is the LIVE options object of the HTML renderer underneath, not a
        // copy — the package's own API note — so this one call configures the single Texinfo
        // pass that both outputs are made from.
        ApplyOptions(renderer.Options.Texinfo);
        PdfRenderSettings settings = ApplyPdfOptions(renderer.Options.Html);

        Stopwatch clock = Stopwatch.StartNew();
        TexinfoHtmlResult htmlResult = renderer.GenerateHtmlFromFile(sourcePath);
        string htmlPath = htmlResult.WriteToDirectory(htmlDirectory, manual.Name);
        TimeSpan htmlElapsed = clock.Elapsed;

        // The pictures need no handling: RenderHtml stages them itself for the length of the
        // render and sweeps up afterwards (the package's own note on workflow two). Passing
        // null for the stylesheet keeps the one the Texinfo stage produced — Lily.Docs adds
        // no styling of its own, so the two outputs are styled identically by construction.
        clock.Restart();
        TexinfoPdfResult pdfResult = renderer.RenderHtml(htmlResult, outputPdfPath, null);
        clock.Stop();

        return new ManualRender(
            new ManualHtmlRender(htmlResult, htmlPath, htmlElapsed),
            new ManualPdfRender(pdfResult, outputPdfPath, clock.Elapsed, settings));
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

    /// <summary>
    /// The PDF stage's own options — the ones that have no meaning for an HTML render.
    /// </summary>
    /// <param name="options">The live Html2Pdf options of the PDF renderer.</param>
    /// <remarks>
    /// Everything else is left at the package's defaults DELIBERATELY, and one of those
    /// defaults is load-bearing enough to be recorded in the baseline rather than trusted:
    /// <c>SvgRasterScale</c> (2.0 as shipped), which decides how sharp two and a half
    /// thousand engravings are and how large the file is.
    /// </remarks>
    private static PdfRenderSettings ApplyPdfOptions(HtmlRenderOptions options)
    {
        options.SetPageSize(PageSizeName);

        // ── Decision D56, RULED at wave LD5: a coverage gap is VISIBLE ────────────
        //
        // The shipped default is false, which REMOVES a character no registered font
        // covers and warns about it. True draws the face's own missing-glyph box instead
        // — a tofu — and warns under its own separate code (font.uncovered.kept), so the
        // drop baselines stay exact either way.
        //
        // ⚠ MEASURED FIRST, AND THE PREMISE THIS DECISION WAS PARKED ON IS GONE. Wave LD4
        // left the ruling to LD5 because music-glossary was expected to be where the switch
        // finally decided something: it is the one manual carrying music symbols in PROSE
        // rather than inside an engraved snippet — a flat four times and a sharp once, in a
        // chord-name multitable. It drops NEITHER. The NotoMusic package on Html2Pdf's
        // fallback chain covers them, and music-glossary's PDF earns zero PDF-stage
        // warnings of any kind. Across all nine manuals in scope PDF_ITEMS is either zero
        // or — for the Notation Reference — entirely SVG text, which this switch does not
        // govern. So it changes nothing measurable today, in either position.
        //
        // It is therefore ruled on what it does NEXT rather than on what it does now. The
        // family's standing rule is that a font chain ends at the package fonts and a gap
        // it cannot fill must be SEEN; a character that silently disappears from a manual
        // is invisible to the visual QA that is this phase's acceptance, while a row of
        // boxes is not. It also makes the two text paths agree: SVG text already draws
        // notdef for an uncovered glyph, so leaving this false meant the same character
        // vanished in prose and appeared as a box in an engraved lyric.
        options.KeepUncoveredCharacters = true;

        // ⚠ NOT AN OPTION — process-global font-registry state, and the one thing wave LD4
        // had to ADD rather than merely measure. The SVG text path's per-glyph fallback
        // chain contains only Noto Music unless a consumer says otherwise, so an engraved
        // lyric asking for `serif' loses its Greek while the identical run in HTML keeps it.
        // See PdfTextFallback for what each family is there to cover.
        PdfTextFallback.EnsureRegistered();

        // READ BACK off the live options object rather than restated from what was just
        // asked for. The baseline then records what the render actually ran with, so a
        // package that changed a default or reinterpreted a page-size name shows up as a
        // baseline that moved rather than as a constant in this file that still agrees
        // with itself.
        return new PdfRenderSettings(options.PageWidthPoints, options.PageHeightPoints,
            options.SvgRasterScale, options.KeepUncoveredCharacters);
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
    /// <c>en/</c>, because manuals include <c>en/macros.itexi</c> by that path. Its
    /// <c>bib/</c> and <c>staged/</c> subdirectories go on the search path in their own
    /// right, because the files in them are named by BARE name.</param>
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

        // ── The two vendored-asset subdirectories, decision D57 (2026-08-19) ──────
        //
        // Both hold files a manual names by BARE NAME, so neither is reachable through the
        // assets root above and each needs its own entry.
        //
        //   bib/     the five bibliographies, TRANSLATED ONCE by the BibTeX oracle. The
        //            essay manual's literature list @includes three of them. Upstream
        //            generates them at build time from Documentation/bib/*.bib; the
        //            generator is thirty lines of Python around the bibtex BINARY and an
        //            8.5 KB .bst style program, so the alternative to vendoring was writing
        //            a BibTeX style-language interpreter.
        //
        //   staged/  ROADMAP and code-review-checklist.md, which the Contributor's Guide
        //            prints with @verbatiminclude. ⚠ NEITHER IS IN Documentation/ UPSTREAM
        //            — one is at the source-tree root and one under .agents/ — and the doc
        //            build COPIES them into its output directory before rendering. This
        //            reproduces that staging, which is why the directory has that name.
        //
        // ⚠ Both are LAST-RESORT by position, after the corpus. That is deliberate and it
        // matters for bib/: if a future corpus mirror ever carried a real colorado.itexi,
        // the corpus copy would win, which is the right precedence for a build product we
        // are standing in for.
        paths.Add(Path.Combine(assetsDirectory, "bib"));
        paths.Add(Path.Combine(assetsDirectory, "staged"));

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

/// <summary>
/// Both of a manual's outputs, made from ONE pass over its Texinfo source.
/// </summary>
public sealed class ManualRender
{
    internal ManualRender(ManualHtmlRender html, ManualPdfRender pdf)
    {
        Html = html;
        Pdf = pdf;
    }

    /// <summary>The HTML render.</summary>
    public ManualHtmlRender Html { get; }

    /// <summary>The PDF render, made from the HTML render's own markup and pictures.</summary>
    public ManualPdfRender Pdf { get; }
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

/// <summary>
/// The PDF-stage settings a render actually ran with, read back off the live options
/// object after they were applied.
/// </summary>
public sealed class PdfRenderSettings
{
    internal PdfRenderSettings(double pageWidthPoints, double pageHeightPoints,
        double svgRasterScale, bool keepUncoveredCharacters)
    {
        PageWidthPoints = pageWidthPoints;
        PageHeightPoints = pageHeightPoints;
        SvgRasterScale = svgRasterScale;
        KeepUncoveredCharacters = keepUncoveredCharacters;
    }

    /// <summary>The page width in points.</summary>
    public double PageWidthPoints { get; }

    /// <summary>The page height in points.</summary>
    public double PageHeightPoints { get; }

    /// <summary>
    /// How much bigger than its placed size an SVG is rasterized, which is the whole of
    /// this pipeline's picture-quality decision.
    /// </summary>
    /// <remarks>
    /// ⚠ NOT A DPI, AND NOT OURS TO SET WELL OR BADLY. Decision D51 dissolved into
    /// "Html2Pdf places the SVG itself", and the raster route it took put the resolution
    /// choice inside the package. The engraved SVG carries its own physical size in
    /// millimetres, so the placed dimensions come from the picture; this scales only the
    /// pixels behind them. Recorded rather than chosen, because the Notation Reference
    /// places two and a half thousand pictures and this number multiplies all of them.
    /// </remarks>
    public double SvgRasterScale { get; }

    /// <summary>
    /// Whether a character no registered font covers is drawn as a visible missing-glyph
    /// box rather than removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decision D56, RULED at wave LD5: TRUE. Wave LD4 left it at the package's shipped
    /// false, having MEASURED that it governs HTML text while every character the Notation
    /// Reference drops is inside an engraved SVG — where <c>font.svg-text.notdef</c> already
    /// draws the face's own notdef and warns — and deferred the ruling to the manual that
    /// was expected to make it matter.
    /// </para>
    /// <para>
    /// ⚠ THAT MANUAL DOES NOT MAKE IT MATTER EITHER. <c>music-glossary</c> carries the only
    /// music symbols in scope that are PROSE rather than engraved music, and MEASURED at
    /// wave LD5 it drops none of them: the NotoMusic package on Html2Pdf's fallback chain
    /// covers them, and its PDF earns zero PDF-stage warnings. The switch changes nothing
    /// measurable in any of the nine manuals, so it is ruled on what it does to the NEXT
    /// uncovered character rather than on evidence from this corpus — visible, because a
    /// character that silently disappears is invisible to the visual QA this phase is
    /// accepted on.
    /// </para>
    /// </remarks>
    public bool KeepUncoveredCharacters { get; }
}

/// <summary>What a PDF render produced.</summary>
public sealed class ManualPdfRender
{
    internal ManualPdfRender(TexinfoPdfResult result, string pdfPath, TimeSpan elapsed,
        PdfRenderSettings settings)
    {
        Result = result;
        PdfPath = pdfPath;
        Elapsed = elapsed;
        Settings = settings;
    }

    /// <summary>The PDF-stage settings this render ran with.</summary>
    public PdfRenderSettings Settings { get; }

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

    /// <summary>
    /// The PDF stage's warnings in STRUCTURED form — category, a stable code, the code
    /// point involved and an exact occurrence count.
    /// </summary>
    /// <remarks>
    /// ⚠ THIS IS WHAT MAKES A DROP BASELINE AN ASSERTION RATHER THAN A STRING MATCH. The
    /// prose form of these warnings names only the FIRST code point seen and carries no
    /// count, so a baseline built on it could not tell one dropped character from twenty,
    /// nor notice that a different character started dropping.
    /// </remarks>
    public IReadOnlyList<RenderWarning> PdfItems => Result.Warnings.PdfItems;

    /// <summary>The scalar facts this render's baseline freezes.</summary>
    /// <returns>Key to value, formatted exactly as the baseline file carries them.</returns>
    /// <remarks>
    /// Produced HERE rather than in the freezer and again in the gate. A baseline written by
    /// one computation and asserted by another agrees with itself for as long as the two
    /// stay in step, and stops meaning anything the moment they do not.
    /// </remarks>
    public SortedDictionary<string, string> BaselineValues()
    {
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            { "PAGES", Text(PageCount) },
            { "PDF_WARNINGS", Text(PdfWarnings.Count) },
            { "PDF_ITEMS", Text(PdfItems.Count) },
            { "PAGE_WIDTH_PT", Text(Settings.PageWidthPoints) },
            { "PAGE_HEIGHT_PT", Text(Settings.PageHeightPoints) },
            { "SVG_RASTER_SCALE", Text(Settings.SvgRasterScale) },
            { "KEEP_UNCOVERED", Settings.KeepUncoveredCharacters ? "true" : "false" },
        };
    }

    /// <summary>One row per distinct dropped code point, sorted.</summary>
    /// <returns>The rows.</returns>
    public List<string> DropRows()
    {
        List<string> rows = new List<string>();
        foreach (RenderWarning item in PdfItems)
        {
            rows.Add(WarningSummary.FormatDropRow(item.Code, item.CodePoint, item.Occurrences));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private static string Text(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
