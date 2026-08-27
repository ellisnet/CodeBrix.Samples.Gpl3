// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.MusicView;
using Fresco.Brix.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace Fresco.Brix.Export;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Writing the engraved score out: a PDF of the whole thing, or a picture of
/// one page.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ THERE IS NO UPSTREAM FOR THE PDF HALF, and the reason is worth stating.
/// Frescobaldi never exports a PDF because it never MAKES one: LilyPond writes
/// the PDF and Frescobaldi shows it, so "export" there is a file copy the
/// user does in their file manager, and the only thing on its File menu is
/// printing (which ruling FR5.5 removes here, permanently). Fresco.Brix's
/// engine writes SVG, so a PDF is something the application has to produce —
/// and producing it is what turns FR5.5 from a subtraction into a trade: no
/// print dialog, but a PDF of the score in one action.
/// </para>
/// <para>
/// Board decision <b>FD13</b>, re-measured at W11½ and ruled <b>(b)</b> under
/// FR7, is what it is drawn with: <see cref="ScorePdf"/> places the engine's
/// own SVG pages as vector content through <c>CodeBrix.PdfDocCreate.Html2Pdf</c>
/// — a fully managed chain, no Skia — with the engine's own faces registered
/// (<see cref="LilyPortScorePdfFonts"/>) and their CFF programs subset.
/// //was previously: Skia's own PDF backend, drawn from the picture the view
/// paints (W11), retired by the house's no-Skia-beyond-the-UI rule.
/// </para>
/// </remarks>
public static class ScoreExport
{
    /// <summary>Writes a whole score to a PDF file.</summary>
    /// <param name="document">The score.</param>
    /// <param name="outputPath">The file to write.</param>
    /// <param name="title">What the document should call itself, or null.</param>
    /// <param name="paperColor">The paper to paint, or null to leave it unpainted.</param>
    /// <param name="warnings">Where to put the PDF writer's warnings, or null.</param>
    /// <returns>How many pages were written.</returns>
    /// <exception cref="ArgumentNullException">No score or no output path.</exception>
    /// <remarks>
    /// The paper is NOT painted by default. A PDF page is white already, and a
    /// painted rectangle underneath everything is one more object for a reader
    /// to composite and one more thing to go wrong when the page is placed on
    /// another.
    /// </remarks>
    public static int WritePdf(
        MusicDocument document, string outputPath, string title = null,
        SKColor? paperColor = null, IList<string> warnings = null)
    {
        if (document == null) { throw new ArgumentNullException(nameof(document)); }

        if (outputPath == null) { throw new ArgumentNullException(nameof(outputPath)); }

        IReadOnlyList<ScorePage> pages = document.Pages;
        var info = new ScorePdfInfo
        {
            Title = title ?? TitleFor(document),
            Creator = AppInfo.AppName + " " + AppInfo.Version,
        };
        ScorePdf.Write(outputPath, pages, info, paperColor, 300.0, LilyPortScorePdfFonts.Get(), warnings);
        return pages.Count;
    }

    /// <summary>Writes one page of a score to a picture file.</summary>
    /// <param name="page">The page.</param>
    /// <param name="outputPath">The file to write.</param>
    /// <param name="resolution">The resolution to render at.</param>
    /// <param name="paperColor">The background, or null for transparent.</param>
    /// <exception cref="ArgumentNullException">No page or no output path.</exception>
    public static void WritePng(
        ScorePage page, string outputPath, double resolution = 300.0,
        SKColor? paperColor = null)
    {
        if (page == null) { throw new ArgumentNullException(nameof(page)); }

        if (outputPath == null) { throw new ArgumentNullException(nameof(outputPath)); }

        using var exporter = new ImageExporter(page)
        {
            Resolution = resolution,
            PaperColor = paperColor ?? SKColors.White,
        };
        exporter.Save(outputPath);
    }

    /// <summary>Writes one page of a score to an SVG file.</summary>
    /// <param name="page">The page.</param>
    /// <param name="outputPath">The file to write.</param>
    /// <exception cref="ArgumentNullException">No page or no output path.</exception>
    /// <remarks>
    /// ⚠ WHEN THE PAGE CAME FROM A FILE, THE FILE IS COPIED. The engine already
    /// wrote an SVG, and it is a better SVG than a re-recording would be: it
    /// carries the <c>textedit://</c> anchors, the engine's own element
    /// structure and its own numbers. Re-drawing it through
    /// <see cref="SvgExporter"/> would produce the same shapes and lose all
    /// three, so that path is only for a page that is not over a file.
    /// </remarks>
    public static void WriteSvg(ScorePage page, string outputPath)
    {
        if (page == null) { throw new ArgumentNullException(nameof(page)); }

        if (outputPath == null) { throw new ArgumentNullException(nameof(outputPath)); }

        if (page is SvgPage svg && File.Exists(svg.FileName))
        {
            File.Copy(svg.FileName, outputPath, overwrite: true);
            return;
        }

        var exporter = new SvgExporter(page) { Resolution = page.Dpi };
        exporter.Save(outputPath);
    }

    /// <summary>Returns the name to suggest exporting a score under.</summary>
    /// <param name="document">The score.</param>
    /// <param name="extension">The suffix wanted, such as <c>.pdf</c>.</param>
    /// <returns>The suggested name.</returns>
    public static string SuggestedName(MusicDocument document, string extension)
    {
        string baseName = document?.FileName;
        if (string.IsNullOrEmpty(baseName)) { return "score" + extension; }

        //A score's own file name is the FIRST page's, which for a multi-page
        //score ends in "-1"; the whole document should not be offered under it.
        string directory = Path.GetDirectoryName(baseName) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(baseName);
        return Path.Combine(directory, name + extension);
    }

    private static string TitleFor(MusicDocument document)
    {
        string name = document?.FileName;
        return string.IsNullOrEmpty(name) ? string.Empty : Path.GetFileNameWithoutExtension(name);
    }
}
