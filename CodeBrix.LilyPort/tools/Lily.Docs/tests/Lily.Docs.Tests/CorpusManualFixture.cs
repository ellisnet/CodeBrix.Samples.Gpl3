// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using Lily.Docs;
using Lily.Docs.Generation;
using Lily.Docs.Manuals;
using Lily.Docs.Rendering;
using Lily.Docs.Snippets;

namespace Lily.Docs.Tests;

/// <summary>
/// Renders the SEVEN corpus manuals wave LD5 added — learning, usage, extending, essay,
/// changes, music-glossary and contributor — to both formats, once, so that every gate in
/// <see cref="CorpusManualRenderTests"/> reads the same runs.
/// <para>
/// One fixture for seven manuals rather than seven fixtures, because the expensive thing is
/// shared and the cheap thing is not: documentation generation is a once-per-process act
/// costing about forty seconds (see <see cref="GeneratedDocumentation"/>), while the renders
/// themselves are between a second and a minute apiece. Seven fixtures would each pay for
/// their own class-fixture lifetime and their own engraver scratch tree for no extra
/// evidence.
/// </para>
/// <para>
/// ⚠ NONE of these seven consumes any of the port's nineteen generated files. That is a fact
/// about the mission's ORIGIN — Phase 5 began as "render what the port generates", and the
/// Internals Reference IS one of the nineteen while the Notation Reference includes the other
/// eighteen. Decision D48 ruled all nine owed in both formats anyway. The generated directory
/// is still on their search path, and <c>CorpusMirrorTests</c> proves the mirror holds none of
/// the nineteen, so "these seven read only corpus text" is a checked claim rather than an
/// assumption.
/// </para>
/// </summary>
public sealed class CorpusManualFixture : IDisposable
{
    /// <summary>
    /// The manuals this fixture renders — every manual in decision D48's scope EXCEPT the
    /// two that already have their own waves and their own gates.
    /// </summary>
    public static readonly string[] ManualNames =
    {
        "learning", "usage", "extending", "essay", "changes", "music-glossary", "contributor",
    };

    private readonly List<IDisposable> _disposables = new List<IDisposable>();

    /// <summary>Generates the documentation once, then renders all seven manuals.</summary>
    public CorpusManualFixture()
    {
        WorkDirectory = Path.Combine(Path.GetTempPath(),
            "lily-docs-corpus-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        Directory.CreateDirectory(WorkDirectory);

        // ⚠ SHARED WITH THE OTHER TWO FIXTURES, AND IT HAS TO BE — generation is a
        // once-per-process act and the second caller gets an empty directory without being
        // told. See GeneratedDocumentation; the behaviour is pinned by
        // GeneratedDocumentationTests.
        GeneratedDocumentation.EnsureGenerated();
        Generation = GeneratedDocumentation.Result;
        if (!Generation.IsComplete)
        {
            throw new InvalidOperationException(
                "documentation generation wrote only "
                + (DocumentationGenerator.ExpectedOutputs.Count - Generation.MissingFiles.Count)
                + " of " + DocumentationGenerator.ExpectedOutputs.Count
                + " files; missing: " + string.Join(", ", Generation.MissingFiles));
        }

        string versionDirectory = Path.Combine(WorkDirectory, "version");
        VersionItexiWriter.Write(versionDirectory);

        Paths = new RenderPaths(GeneratedDocumentation.Directory, ToolPaths.AssetsDirectory,
            versionDirectory, ToolPaths.CorpusDirectory);

        foreach (string name in ManualNames)
        {
            Renders[name] = Render(ManualCatalog.Find(name), versionDirectory);
        }

        // ⚠ THE ONE EXTRA RENDER, AND IT IS THE ONLY WAY TO CHECK A `false'. The Contributor's
        // Guide declares engravesSnippets: false, so the render above gave it NO engraver —
        // and a manual rendered without one is indistinguishable from a manual whose every
        // engraving failed. This renders it AGAIN with an engraver registered and nothing else
        // changed, so the claim becomes "the engraver was there and was never called" rather
        // than "we did not ask". It costs a fraction of a second: the manual carries no music,
        // which is the thing being proved.
        ManualDefinition contributor = ManualCatalog.Find("contributor");
        ManualRenderer probe = new ManualRenderer(Paths);
        ContributorEngraverProbe = new EngineSnippetRenderer(probe.GeometryOf(contributor),
            Path.Combine(WorkDirectory, "contributor-probe-snippets"), Paths.SnippetIncludePaths);
        _disposables.Add(ContributorEngraverProbe);
        probe.SnippetRenderer = ContributorEngraverProbe;
        ContributorProbeHtml = probe.RenderHtml(contributor,
            Path.Combine(WorkDirectory, "contributor-probe"));
    }

    /// <summary>The temporary directory this fixture's outputs live in.</summary>
    public string WorkDirectory { get; }

    /// <summary>The generation run every manual here shares.</summary>
    public DocumentationGenerationResult Generation { get; }

    /// <summary>The resolved input directories every render read from.</summary>
    public RenderPaths Paths { get; }

    /// <summary>Each manual's render, by manual name.</summary>
    public Dictionary<string, CorpusManualRender> Renders { get; } =
        new Dictionary<string, CorpusManualRender>(StringComparer.Ordinal);

    /// <summary>
    /// The engraver registered for the Contributor's Guide's second render — the one that
    /// exists so its zero can be asserted.
    /// </summary>
    public EngineSnippetRenderer ContributorEngraverProbe { get; }

    /// <summary>The Contributor's Guide rendered WITH an engraver registered.</summary>
    public ManualHtmlRender ContributorProbeHtml { get; }

    /// <summary>Looks a render up by manual name.</summary>
    /// <param name="name">The manual's short name.</param>
    /// <returns>Its render.</returns>
    public CorpusManualRender this[string name] => Renders[name];

    private CorpusManualRender Render(ManualDefinition manual, string versionDirectory)
    {
        ManualRenderer renderer = new ManualRenderer(Paths);
        TexinfoPageGeometry geometry = renderer.GeometryOf(manual);

        // Faithful to what the command line does, deliberately: a manual that carries music
        // gets an engraver and one that does not gets none, so the artefact these gates read
        // is the artefact the tool produces. The Contributor's Guide's `false' is checked
        // separately, by the probe render in the constructor.
        EngineSnippetRenderer snippets = null;
        if (manual.EngravesSnippets)
        {
            snippets = new EngineSnippetRenderer(geometry,
                Path.Combine(WorkDirectory, manual.Name + "-snippets"), Paths.SnippetIncludePaths);
            _disposables.Add(snippets);
            renderer.SnippetRenderer = snippets;
        }

        // ⚠ ONE PASS FOR BOTH FORMATS. RenderHtml followed by RenderPdf runs the Texinfo
        // source twice and engraves the music twice, because the package's coordinator
        // dedupes only WITHIN a render — and two engravings of one snippet look exactly like
        // one. It is also what makes decision D51's ruling literally true: the SAME SVG
        // reaches both outputs.
        ManualRender render = renderer.RenderBoth(manual,
            Path.Combine(WorkDirectory, manual.Name),
            Path.Combine(WorkDirectory, manual.Name, manual.Name + ".pdf"));

        return new CorpusManualRender(manual, geometry, snippets, render.Html, render.Pdf);
    }

    /// <summary>Disposes every engraver, then deletes the working directory.</summary>
    public void Dispose()
    {
        // ⚠ ORDER: the engravers hand their pictures over as FILE paths and are disposed only
        // after every render has copied them into its output directory. Disposing one earlier
        // would delete the SVGs out from under WriteToDirectory.
        foreach (IDisposable disposable in _disposables)
        {
            disposable.Dispose();
        }

        try
        {
            if (Directory.Exists(WorkDirectory))
            {
                Directory.Delete(WorkDirectory, true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green suite over.
        }
    }
}

/// <summary>One corpus manual's render, and everything its gates ask about.</summary>
public sealed class CorpusManualRender
{
    internal CorpusManualRender(ManualDefinition manual, TexinfoPageGeometry geometry,
        EngineSnippetRenderer snippets, ManualHtmlRender html, ManualPdfRender pdf)
    {
        Manual = manual;
        Geometry = geometry;
        Snippets = snippets;
        Html = html;
        Pdf = pdf;
        HtmlText = File.ReadAllText(html.HtmlPath);
        PdfBytes = File.ReadAllBytes(pdf.PdfPath);
    }

    /// <summary>The manual rendered.</summary>
    public ManualDefinition Manual { get; }

    /// <summary>The page geometry read off the manual's own source.</summary>
    public TexinfoPageGeometry Geometry { get; }

    /// <summary>The engraver, or null for a manual that declares it carries no music.</summary>
    public EngineSnippetRenderer Snippets { get; }

    /// <summary>The HTML render.</summary>
    public ManualHtmlRender Html { get; }

    /// <summary>The rendered HTML, read once.</summary>
    public string HtmlText { get; }

    /// <summary>The PDF render, made from the HTML render's own markup and pictures.</summary>
    public ManualPdfRender Pdf { get; }

    /// <summary>The written PDF's own bytes, so a gate can ask the FILE about its pages.</summary>
    public byte[] PdfBytes { get; }
}
