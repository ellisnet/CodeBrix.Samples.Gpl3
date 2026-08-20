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

namespace Lily.Docs.Tests;

/// <summary>
/// Generates the port's documentation ONCE and renders the Internals Reference to both
/// formats, so that every gate in <see cref="InternalsReferenceRenderTests"/> reads one
/// run rather than paying for its own.
/// <para>
/// Generation is an engine job of roughly forty seconds and the PDF another six, so a
/// per-test run would turn one suite into several minutes for no extra evidence — every
/// gate here is a question about the SAME run.
/// </para>
/// </summary>
public sealed class InternalsReferenceFixture : IDisposable
{
    /// <summary>Generates and renders.</summary>
    public InternalsReferenceFixture()
    {
        WorkDirectory = Path.Combine(Path.GetTempPath(),
            "lily-docs-tests-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        Directory.CreateDirectory(WorkDirectory);

        // ⚠ SHARED WITH THE NOTATION FIXTURE, AND IT HAS TO BE. Generation is a
        // once-per-process act: a second call in the same process writes nothing and
        // reports all nineteen files missing, without throwing. Both fixtures generating
        // for themselves gave whichever ran second an EMPTY directory and a manual with no
        // appendices — see GeneratedDocumentation.
        GeneratedDocumentation.EnsureGenerated();
        string generatedDirectory = GeneratedDocumentation.Directory;
        Generation = GeneratedDocumentation.Result;

        string versionDirectory = Path.Combine(WorkDirectory, "version");
        VersionItexiWriter.Write(versionDirectory);

        Manual = ManualCatalog.Find("internals");

        // The corpus is deliberately NOT on the path for the Internals Reference. Its
        // whole include closure is the three vendored assets, so leaving the corpus off
        // proves the vendoring actually carries the render (decision D49(a)) instead of
        // letting the mirror silently stand in for it.
        RenderPaths paths = new RenderPaths(
            generatedDirectory, ToolPaths.AssetsDirectory, versionDirectory, null);
        ManualRenderer renderer = new ManualRenderer(paths);

        Html = renderer.RenderHtml(Manual, Path.Combine(WorkDirectory, "html"));
        HtmlText = File.ReadAllText(Html.HtmlPath);
        Pdf = renderer.RenderPdf(Manual, Path.Combine(WorkDirectory, "pdf", "internals.pdf"));
    }

    /// <summary>The temporary directory this fixture's outputs live in.</summary>
    public string WorkDirectory { get; }

    /// <summary>The manual under test.</summary>
    public ManualDefinition Manual { get; }

    /// <summary>The generation run.</summary>
    public DocumentationGenerationResult Generation { get; }

    /// <summary>The HTML render.</summary>
    public ManualHtmlRender Html { get; }

    /// <summary>The rendered HTML, read once.</summary>
    public string HtmlText { get; }

    /// <summary>The PDF render.</summary>
    public ManualPdfRender Pdf { get; }

    /// <summary>Deletes the working directory.</summary>
    public void Dispose()
    {
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
