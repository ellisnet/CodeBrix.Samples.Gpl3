// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Docs;
using Lily.Docs.Generation;
using Lily.Docs.Manuals;
using Lily.Docs.Rendering;
using Lily.Docs.Snippets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Lily.Shell.Services;

/// <summary>
/// Drives the Lily.Docs vehicle from inside the shell's own process: generates the
/// port's nineteen documentation files once, then renders one manual from them.
/// </summary>
/// <remarks>
/// <para>
/// The shell renders; it does not FREEZE. Lily.Docs' <c>--baseline</c> switch is
/// deliberately absent here — an expected-warnings baseline is frozen from a run that
/// was read and reviewed, in the repository, by the tool that owns those files. A shell
/// session is where a manual is looked at, not where a gate's expectation is rewritten.
/// </para>
/// <para>
/// ⚠ GENERATION IS A ONCE-PER-PROCESS ACT, AND THE SECOND CALL LIES QUIETLY. The first
/// run of <c>ly/generate-documentation.ly</c> writes all nineteen files in about forty
/// seconds; every later run in the same process returns in a tenth of a second having
/// written NOTHING, reports all nineteen missing, and does not throw. A long-lived shell
/// is exactly where that bites — <c>docs internals</c> followed by <c>docs notation</c>
/// is two calls in one process — so this class generates ONCE and every later render
/// reuses those bytes. Upstream never meets the problem because it gets a process per
/// run.
/// </para>
/// </remarks>
public sealed class DocsRunner
{
    private readonly object _gate = new();

    private string _generatedDirectory;
    private EngineSnippetRenderer _activeSnippets;

    /// <summary>
    /// The generation step, injectable so the once-per-process contract can be gated
    /// without paying for a forty-second engine run.
    /// </summary>
    /// <remarks>
    /// Returns the expected files it did NOT write, so an empty list means a complete
    /// generation. The real implementation is <see cref="DocumentationGenerator"/>.
    /// </remarks>
    internal Func<string, IReadOnlyList<string>> Generator { get; set; }

    /// <summary>
    /// Where the nineteen generated files live once they have been written, or null
    /// before the first successful generation.
    /// </summary>
    public string GeneratedDirectory
    {
        get { lock (_gate) { return _generatedDirectory; } }
    }

    /// <summary>
    /// How many snippets the engraver has been asked for so far in the render that is
    /// running now, or 0 when none is.
    /// </summary>
    /// <remarks>
    /// Read from the shell's own thread while the render runs on another, which is why
    /// this is only ever used for a progress line: an occasionally stale count costs a
    /// reader nothing, and the counts that MATTER are read from the finished result.
    /// </remarks>
    public int SnippetsAsked
    {
        get
        {
            EngineSnippetRenderer snippets = _activeSnippets;
            return snippets == null ? 0 : snippets.InvocationCount;
        }
    }

    /// <summary>The scratch root the shell renders into when no output directory is given.</summary>
    /// <remarks>
    /// Not the working directory, deliberately. A GUI shell's working directory is its own
    /// output folder, and writing a thousand-page manual into <c>bin/Release</c> would be a
    /// surprise; the manuals also outlive the session, so they go somewhere a user can find
    /// them by name.
    /// </remarks>
    public static string ScratchRoot => Path.Combine(Path.GetTempPath(), "lily-shell-docs");

    /// <summary>Renders one manual.</summary>
    /// <param name="request">What to render.</param>
    /// <param name="report">Called with progress lines as the run proceeds.</param>
    /// <returns>What the run produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Generation did not write every file.</exception>
    public DocsRunResult Render(DocsRunRequest request, Action<string> report)
    {
        if (request == null) { throw new ArgumentNullException(nameof(request)); }

        report ??= _ => { };

        string outputDirectory = Path.GetFullPath(
            request.OutputDirectory ?? Path.Combine(ScratchRoot, request.Manual.Name));
        Directory.CreateDirectory(outputDirectory);

        string generated = EnsureGenerated(report);

        // The version stand-in is written per render rather than cached with the generated
        // files: it states the version of the ENGINE that produced the manual, and it is
        // cheap. Lily.Docs' own reasoning for never vendoring it applies unchanged here.
        string versionDirectory = Path.Combine(outputDirectory, "version");
        VersionItexiWriter.Write(versionDirectory);

        RenderPaths paths = new RenderPaths(
            generated, ToolPaths.AssetsDirectory, versionDirectory,
            request.Manual.SourceKind == ManualSourceKind.Corpus ? ToolPaths.CorpusDirectory : null);
        ManualRenderer renderer = new ManualRenderer(paths);

        // A manual that carries music is rendered WITH an engraver and one that carries none
        // without — because a manual rendered with no engraver looks exactly like a manual
        // whose every engraving failed, so `engravesSnippets: false' is a claim rather than a
        // default. The counts reported afterwards are what tell the two apart.
        using EngineSnippetRenderer snippets =
            request.Manual.EngravesSnippets && request.EngraveSnippets
                ? new EngineSnippetRenderer(renderer.GeometryOf(request.Manual),
                    Path.Combine(outputDirectory, "snippets"), paths.SnippetIncludePaths)
                : null;
        renderer.SnippetRenderer = snippets;
        _activeSnippets = snippets;

        try
        {
            if (snippets != null)
            {
                report(string.Format(CultureInfo.InvariantCulture,
                    "engraving snippets at line-width {0} into {1}",
                    renderer.GeometryOf(request.Manual).LineWidth, snippets.ScratchRoot));
            }

            string pdfPath = Path.Combine(outputDirectory, request.Manual.Name + ".pdf");
            ManualHtmlRender html = null;
            ManualPdfRender pdf = null;

            // ⚠ BOTH FORMATS ARE ONE RENDER, NOT TWO. RenderHtml followed by RenderPdf runs
            // the Texinfo source twice, and the package's snippet coordinator dedupes only
            // WITHIN a render — so a manual carrying music would be engraved once per FORMAT,
            // at two and a half thousand engravings and five minutes a time. It is also what
            // makes decision D51 true as stated: the same SVG reaches both outputs rather
            // than each output receiving its own separately engraved copy of the same music.
            if (request.WantHtml && request.WantPdf)
            {
                report("rendering " + request.Manual.Title + " to HTML and PDF (one Texinfo pass)...");
                ManualRender render = renderer.RenderBoth(request.Manual, outputDirectory, pdfPath);
                html = render.Html;
                pdf = render.Pdf;
            }
            else if (request.WantHtml)
            {
                report("rendering " + request.Manual.Title + " to HTML...");
                html = renderer.RenderHtml(request.Manual, outputDirectory);
            }
            else
            {
                report("rendering " + request.Manual.Title + " to PDF...");
                pdf = renderer.RenderPdf(request.Manual, pdfPath);
            }

            return new DocsRunResult(request.Manual, outputDirectory, html, pdf, snippets);
        }
        finally
        {
            _activeSnippets = null;
        }
    }

    /// <summary>
    /// Generates the nineteen files, or returns the directory an earlier call in this
    /// process already wrote them into.
    /// </summary>
    /// <param name="report">Called with progress lines.</param>
    /// <returns>The directory holding the generated files.</returns>
    /// <exception cref="InvalidOperationException">The run did not write every file.</exception>
    internal string EnsureGenerated(Action<string> report)
    {
        report ??= _ => { };

        lock (_gate)
        {
            if (_generatedDirectory != null)
            {
                report("using the nineteen documentation files generated earlier this session");
                return _generatedDirectory;
            }

            // Named `en' because the manuals include the port's own files as `en/<name>' —
            // eighteen times, in the notation manual alone. RenderPaths refuses any other
            // name, because getting it wrong does not fail: it silently resolves nothing.
            string directory = Path.Combine(
                ScratchRoot, "generated", RenderPaths.GeneratedDirectoryName);

            report("generating the nineteen documentation files into " + directory
                + " (about 40 s)...");

            // ⚠ This changes the PROCESS working directory for the duration of the run.
            // Upstream's entry point writes its outputs through open-output-file with
            // RELATIVE names, so the output directory is chosen that way rather than by an
            // argument; DocumentationGenerator restores the previous directory in a finally.
            IReadOnlyList<string> missing = (Generator ?? GenerateForReal)(directory);
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "documentation generation wrote only "
                    + (DocumentationGenerator.ExpectedOutputs.Count - missing.Count) + " of "
                    + DocumentationGenerator.ExpectedOutputs.Count + " files; missing: "
                    + string.Join(", ", missing));
            }

            _generatedDirectory = directory;
            return directory;
        }
    }

    // ⚠ AN INCOMPLETE GENERATION IS NOT REMEMBERED, AND THE RETRY IT ALLOWS WILL NOT
    // HELP — deliberately. Generation works once per PROCESS, so a second attempt in
    // this one writes nothing and reports all nineteen files missing, which throws the
    // same way. That is the point: the alternative to failing twice, loudly, is caching
    // a half-written directory and rendering manuals out of it, successfully, with
    // their appendices simply absent. Restarting the shell is the fix.

    private static IReadOnlyList<string> GenerateForReal(string directory) =>
        new DocumentationGenerator().Generate(directory).MissingFiles;
}

/// <summary>What one <c>docs</c> run was asked for.</summary>
public sealed class DocsRunRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="manual">The manual to render.</param>
    /// <exception cref="ArgumentNullException"><paramref name="manual"/> is null.</exception>
    public DocsRunRequest(ManualDefinition manual)
    {
        Manual = manual ?? throw new ArgumentNullException(nameof(manual));
    }

    /// <summary>The manual to render.</summary>
    public ManualDefinition Manual { get; }

    /// <summary>Whether to write HTML. Both formats when neither is asked for.</summary>
    public bool WantHtml { get; set; } = true;

    /// <summary>Whether to write PDF. Both formats when neither is asked for.</summary>
    public bool WantPdf { get; set; } = true;

    /// <summary>
    /// Whether to register the engraver at all. False is the CONTROL run: every snippet
    /// becomes source text, in seconds instead of minutes, which separates "did the
    /// includes resolve?" from "did the music engrave?".
    /// </summary>
    public bool EngraveSnippets { get; set; } = true;

    /// <summary>Where to write, or null for a directory under the scratch root.</summary>
    public string OutputDirectory { get; set; }
}

/// <summary>What one <c>docs</c> run produced.</summary>
public sealed class DocsRunResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="manual">The manual rendered.</param>
    /// <param name="outputDirectory">Where the outputs were written.</param>
    /// <param name="html">The HTML render, or null when none was asked for.</param>
    /// <param name="pdf">The PDF render, or null when none was asked for.</param>
    /// <param name="snippets">The engraver, or null when none was registered.</param>
    internal DocsRunResult(ManualDefinition manual, string outputDirectory,
        ManualHtmlRender html, ManualPdfRender pdf, EngineSnippetRenderer snippets)
    {
        Manual = manual;
        OutputDirectory = outputDirectory;
        Html = html;
        Pdf = pdf;

        // Read off the engraver HERE rather than holding a reference to it, because the
        // engraver is disposed with the render that owned it and anything read afterwards
        // would be read from a disposed object.
        SnippetsAsked = snippets == null ? 0 : snippets.InvocationCount;
        SnippetsEngraved = snippets == null ? 0 : snippets.EngravedCount;
        Pictures = snippets == null ? 0 : snippets.PageCount;
        SnippetFailures = snippets == null ? 0 : snippets.FailureCount;
        SnippetDeclines = snippets == null ? 0 : snippets.DeclineCount;
        Failures = snippets == null
            ? Array.Empty<SnippetFailure>()
            : new List<SnippetFailure>(snippets.Failures);
    }

    /// <summary>The manual rendered.</summary>
    public ManualDefinition Manual { get; }

    /// <summary>Where the outputs were written.</summary>
    public string OutputDirectory { get; }

    /// <summary>The HTML render, or null when none was asked for.</summary>
    public ManualHtmlRender Html { get; }

    /// <summary>The PDF render, or null when none was asked for.</summary>
    public ManualPdfRender Pdf { get; }

    /// <summary>How many snippets the engraver was ASKED for.</summary>
    /// <remarks>
    /// ⚠ INVOCATIONS AND FAILURES, NEVER COMPLETION. The Texinfo package CATCHES a snippet
    /// renderer that throws and falls back to printing the snippet's source, so a render
    /// that finished is entirely compatible with every engraving in it having failed. These
    /// five counts are the only thing that can tell those apart.
    /// </remarks>
    public int SnippetsAsked { get; }

    /// <summary>How many snippets came back as pictures.</summary>
    public int SnippetsEngraved { get; }

    /// <summary>How many picture files those engravings produced (a snippet may run to several pages).</summary>
    public int Pictures { get; }

    /// <summary>How many engravings FAILED.</summary>
    public int SnippetFailures { get; }

    /// <summary>How many snippets the engraver DECLINED (returning "not rendered").</summary>
    public int SnippetDeclines { get; }

    /// <summary>The failures, each with the location and the engine's message.</summary>
    public IReadOnlyList<SnippetFailure> Failures { get; }
}
