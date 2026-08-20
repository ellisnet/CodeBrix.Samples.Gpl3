// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using Lily.Docs;
using Lily.Docs.Generation;
using Lily.Docs.Manuals;
using Lily.Docs.Rendering;
using Lily.Docs.Snippets;

namespace Lily.Docs.Tests;

/// <summary>
/// Generates the port's documentation ONCE and renders the Notation Reference to HTML with
/// the engraving seam registered, so that every gate in
/// <see cref="NotationReferenceRenderTests"/> reads one run.
/// <para>
/// This is the wave-LD3 fixture and it is the expensive one: generation is an engine job of
/// roughly forty seconds, and the manual's music is roughly sixteen hundred distinct
/// engravings on top of that. Every gate here is a question about the SAME run, so it is
/// paid for once.
/// </para>
/// <para>
/// ⚠ BOTH FORMATS COME OUT OF ONE PASS — wave LD4. Rendering them separately would run the
/// Texinfo source twice and engrave the manual's music twice, because the package's snippet
/// coordinator dedupes only within a render: five extra minutes, and every count in
/// <c>notation-snippets.tsv</c> doubled. It is also what decision D51's ruling actually
/// says — that Lily.Docs hands THE SAME SVG to both outputs — so a fixture that engraved
/// once per format would be gating something weaker than the ruling.
/// </para>
/// </summary>
public sealed class NotationReferenceFixture : IDisposable
{
    /// <summary>Generates and renders.</summary>
    public NotationReferenceFixture()
    {
        WorkDirectory = Path.Combine(Path.GetTempPath(),
            "lily-docs-notation-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        Directory.CreateDirectory(WorkDirectory);

        // ⚠ SHARED WITH THE INTERNALS FIXTURE, AND IT HAS TO BE — generation is a
        // once-per-process act and the second caller gets an empty directory without being
        // told. See GeneratedDocumentation; the behaviour is pinned by
        // GeneratedDocumentationTests.
        GeneratedDocumentation.EnsureGenerated();
        GeneratedDirectory = GeneratedDocumentation.Directory;
        Generation = GeneratedDocumentation.Result;

        // Asserted HERE rather than only in a gate, because everything below reads these
        // files: a render out of an empty directory succeeds, and the eighteen missing
        // appendices show up as Include warnings among however many the baseline already
        // tolerates.
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

        Manual = ManualCatalog.Find("notation");

        // ⚠ THE CORPUS IS ON THE PATH HERE, AND THE GENERATED FILES ARE NOT OPTIONAL. Unlike
        // the Internals Reference, this manual is corpus prose whose appendices ARE the
        // port's own output; rendering it is the act D49(b) put the mirror in the repository
        // for. Decision D49(b) also means these gates ALWAYS RUN — the inputs are in the
        // repository, so a gate that skipped when something was absent would be a defect.
        Paths = new RenderPaths(GeneratedDirectory, ToolPaths.AssetsDirectory, versionDirectory,
            ToolPaths.CorpusDirectory);
        ManualRenderer renderer = new ManualRenderer(Paths);

        Geometry = renderer.GeometryOf(Manual);
        Snippets = new EngineSnippetRenderer(Geometry, Path.Combine(WorkDirectory, "snippets"),
            Paths.SnippetIncludePaths);
        renderer.SnippetRenderer = Snippets;

        // ⚠ The engraver hands its pictures over as FILE paths, so its scratch directory has
        // to outlive BOTH stages. It does: RenderBoth finishes the PDF before it returns, and
        // Dispose runs later.
        ManualRender render = renderer.RenderBoth(Manual, Path.Combine(WorkDirectory, "html"),
            Path.Combine(WorkDirectory, "pdf", "notation.pdf"));
        Html = render.Html;
        Pdf = render.Pdf;
        HtmlText = File.ReadAllText(Html.HtmlPath);
        PdfBytes = File.ReadAllBytes(Pdf.PdfPath);
    }

    /// <summary>The temporary directory this fixture's outputs live in.</summary>
    public string WorkDirectory { get; }

    /// <summary>Where the port's nineteen files were generated.</summary>
    public string GeneratedDirectory { get; }

    /// <summary>The manual under test.</summary>
    public ManualDefinition Manual { get; }

    /// <summary>The generation run.</summary>
    public DocumentationGenerationResult Generation { get; }

    /// <summary>The resolved input directories the render read from.</summary>
    public RenderPaths Paths { get; }

    /// <summary>The page geometry read off the manual's own source.</summary>
    public TexinfoPageGeometry Geometry { get; }

    /// <summary>The engraver, and the counts of what it was asked to do.</summary>
    public EngineSnippetRenderer Snippets { get; }

    /// <summary>The HTML render.</summary>
    public ManualHtmlRender Html { get; }

    /// <summary>The rendered HTML, read once. Roughly ten megabytes with the pictures in.</summary>
    public string HtmlText { get; }

    /// <summary>The PDF render, made from the HTML render's own markup and pictures.</summary>
    public ManualPdfRender Pdf { get; }

    /// <summary>
    /// The written PDF's own bytes, read once — so a gate can ask the FILE what page size it
    /// carries rather than asking the options object what it was told to use.
    /// </summary>
    public byte[] PdfBytes { get; }

    /// <summary>Deletes the working directory.</summary>
    public void Dispose()
    {
        // ⚠ ORDER: the engraver is disposed only after the render has copied its pictures
        // into the output directory, which RenderHtml did. Disposing it earlier would delete
        // the SVGs out from under WriteToDirectory.
        Snippets?.Dispose();

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
