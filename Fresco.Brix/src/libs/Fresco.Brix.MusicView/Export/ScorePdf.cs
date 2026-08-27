// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.PdfDocCreate.Html2Pdf;
using CodeBrix.PdfDocCreate.Html2Pdf.Fonts;
using CodeBrix.PdfDocuments.Pdf;
using CodeBrix.PdfDocuments.Pdf.IO;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Fresco.Brix.MusicView; //was previously: qpageview/export.py pdf()

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What a PDF says about itself.
/// </summary>
public sealed class ScorePdfInfo
{
    /// <summary>Gets or sets the document's title.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets who wrote it.</summary>
    public string Author { get; set; }

    /// <summary>Gets or sets what it is about.</summary>
    public string Subject { get; set; }

    /// <summary>Gets or sets its keywords.</summary>
    public string Keywords { get; set; }

    /// <summary>Gets or sets the application that composed it.</summary>
    public string Creator { get; set; }
}

/// <summary>
/// The faces a score's text is set in, for the PDF to embed.
/// </summary>
/// <remarks>
/// <para>
/// The view answers a font request through <see cref="IScoreTypefaceResolver"/>
/// with the bytes of the face the ENGINE measured the text with (board trap 9).
/// The PDF writer cannot take bytes: it registers font FILES with Html2Pdf and
/// then matches the family names an SVG asks for against the families those
/// files declare. So the host hands over two things — the files, and a mapping
/// from what the SVG says (<c>serif</c>, <c>LilyPond Sans Serif</c>, a CSS
/// list) to the family name a registered file declares.
/// </para>
/// <para>
/// With no fonts given, Html2Pdf answers the generics with its own packaged
/// faces (Merriweather for <c>serif</c>, and so on): a correct PDF, in faces
/// the engine did not lay the text out against. The application always gives
/// the engine's own.
/// </para>
/// </remarks>
public sealed class ScorePdfFonts
{
    /// <summary>Creates the description.</summary>
    /// <param name="fontFiles">The <c>.otf</c>/<c>.ttf</c> files to register.</param>
    /// <param name="familyMapper">
    /// Maps an SVG <c>font-family</c> value to the family name one of the files
    /// declares; null leaves the value as it is.
    /// </param>
    public ScorePdfFonts(IEnumerable<string> fontFiles, Func<string, string> familyMapper)
    {
        FontFiles = new List<string>(fontFiles ?? throw new ArgumentNullException(nameof(fontFiles)));
        FamilyMapper = familyMapper ?? throw new ArgumentNullException(nameof(familyMapper));
    }

    /// <summary>Gets the files to register.</summary>
    public IReadOnlyList<string> FontFiles { get; }

    /// <summary>Gets the mapping from SVG family text to a registered family.</summary>
    public Func<string, string> FamilyMapper { get; }
}

/// <summary>
/// Writes pages of engraved music to a PDF, as VECTORS.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's module-level <c>export.pdf()</c>, which drives a
/// <c>QPdfWriter</c> and asks each page to <c>output()</c> onto it. The device
/// here is <c>CodeBrix.PdfDocCreate.Html2Pdf</c> placing the ENGINE'S OWN SVG
/// FILES as vector content — paths, strokes, dashes, and the score's text as
/// real embedded-font text — through a fully managed chain: board decision
/// FD13 as re-measured at W11½ and ruled (b) under FR7. Nothing here draws
/// through Skia.
/// //was previously: <c>SKDocument.CreatePdf</c>, drawn from the picture the
/// view paints (W11). Measured at W11½ the Html2Pdf route is vector too, embeds
/// real font programs rather than Type 3 glyph procedures, writes the exact
/// paper box, and — with <see cref="PdfCffSubsetMode.Sparse"/> — subsets the
/// engine's CFF faces; and the house has no Skia beyond the UI foundation.
/// </para>
/// <para>
/// What a page is to this writer: an <see cref="SvgPage"/>, whose FILE is
/// placed; or a region of one (the <see cref="PdfExporter"/> case), which is
/// the same file with its root <c>viewBox</c> narrowed to the region. A page of
/// any other kind cannot be placed as vectors and is refused rather than
/// rasterised — the application never asks for one.
/// </para>
/// <para>
/// Pages that share one paper box are placed in one pass, so a face embedded
/// once serves them all; pages of differing boxes are rendered one by one and
/// merged. A rotated page keeps its content and gets a <c>/Rotate</c> entry,
/// which is what a PDF reader expects rotation to be.
/// </para>
/// </remarks>
public static class ScorePdf
{
    /// <summary>How many points there are to an inch.</summary>
    public const double PointsPerInch = 72.0;

    private static readonly Regex FontFamilyAttribute = new Regex(
        "font-family=(\"[^\"]*\"|'[^']*')", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RootStart = new Regex(
        "<svg\\b[^>]*>", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>Writes pages to a PDF file.</summary>
    /// <param name="fileName">The file to write.</param>
    /// <param name="pages">The pages, in order.</param>
    /// <param name="info">What the document should say about itself.</param>
    /// <param name="paperColor">The background to paint, or null to paint none.</param>
    /// <param name="rasterResolution">
    /// The resolution for anything that cannot be placed as vectors (Html2Pdf's
    /// per-part raster fallback; nothing in an engraving needs it).
    /// </param>
    /// <param name="fonts">The score's faces, or null for Html2Pdf's packaged ones.</param>
    /// <param name="warnings">Where to put Html2Pdf's warnings, or null.</param>
    /// <exception cref="ArgumentNullException">No file name or no pages.</exception>
    public static void Write(
        string fileName,
        IEnumerable<ScorePage> pages,
        ScorePdfInfo info = null,
        SKColor? paperColor = null,
        double rasterResolution = 300.0,
        ScorePdfFonts fonts = null,
        IList<string> warnings = null)
    {
        if (fileName == null) { throw new ArgumentNullException(nameof(fileName)); }

        byte[] bytes = ToBytes(pages, info, paperColor, rasterResolution, fonts, warnings)
            ?? throw new InvalidOperationException("There were no pages to write.");
        File.WriteAllBytes(fileName, bytes);
    }

    /// <summary>Writes pages to a PDF and returns its bytes.</summary>
    /// <param name="pages">The pages, in order.</param>
    /// <param name="info">What the document should say about itself.</param>
    /// <param name="paperColor">The background to paint, or null to paint none.</param>
    /// <param name="rasterResolution">
    /// The resolution for anything that cannot be placed as vectors.
    /// </param>
    /// <param name="fonts">The score's faces, or null for Html2Pdf's packaged ones.</param>
    /// <param name="warnings">Where to put Html2Pdf's warnings, or null.</param>
    /// <returns>The bytes, or null when there were no pages.</returns>
    /// <exception cref="ArgumentNullException">No pages.</exception>
    /// <exception cref="NotSupportedException">A page is not over an SVG file.</exception>
    public static byte[] ToBytes(
        IEnumerable<ScorePage> pages,
        ScorePdfInfo info = null,
        SKColor? paperColor = null,
        double rasterResolution = 300.0,
        ScorePdfFonts fonts = null,
        IList<string> warnings = null)
    {
        if (pages == null) { throw new ArgumentNullException(nameof(pages)); }

        var sources = new List<PageSource>();
        foreach (ScorePage page in pages)
        {
            if (page != null) { sources.Add(PageSource.For(page)); }
        }

        if (sources.Count == 0) { return null; }

        if (fonts != null) { Html2PdfFonts.AddFontFiles(fonts.FontFiles, false); }

        string directory = Path.Combine(
            Path.GetTempPath(), "fresco-brix-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].WriteSvg(Path.Combine(directory, "page-" + (i + 1) + ".svg"), fonts?.FamilyMapper);
            }

            byte[] rendered = SameBox(sources)
                ? Render(sources, directory, sources[0].Width, sources[0].Height, info, paperColor, rasterResolution, warnings)
                : Merge(sources, directory, info, paperColor, rasterResolution, warnings);

            return Finish(rendered, sources, info);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Returns the size to give a page's PDF page box, in points.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The width and height, in points.</returns>
    /// <remarks>
    /// ⚠ Board trap 61. A page's natural size is what the VIEW lays out in, and
    /// for an SVG that is the renderer's pixel viewport — 794 by 1123 whole CSS
    /// pixels for A4, which is 0.04% too big. The engine also wrote the size
    /// exactly, in millimetres, on the file's root element; when it did, that
    /// is what the page box says, so a printed export measures 210 millimetres
    /// across. The drawing is scaled to fill whichever box is used, so the two
    /// never disagree about where the music sits on the paper.
    /// </remarks>
    public static (double Width, double Height) PageSizePoints(ScorePage page)
    {
        if (page == null) { throw new ArgumentNullException(nameof(page)); }

        var (naturalWidth, naturalHeight) = page.DefaultSize();
        double width = naturalWidth * PointsPerInch / page.Dpi;
        double height = naturalHeight * PointsPerInch / page.Dpi;

        if (page is SvgPage svg
            && Math.Abs(page.ScaleX - 1.0) < double.Epsilon
            && Math.Abs(page.ScaleY - 1.0) < double.Epsilon)
        {
            (double Width, double Height)? declared = svg.PaperSizePoints;
            if (declared != null)
            {
                width = declared.Value.Width;
                height = declared.Value.Height;
                if (((int)page.ComputedRotation & 1) != 0) { (width, height) = (height, width); }
            }
        }

        return (Math.Max(width, 1.0), Math.Max(height, 1.0));
    }

    // ----- the pipeline -----

    private static bool SameBox(List<PageSource> sources)
    {
        for (int i = 1; i < sources.Count; i++)
        {
            if (Math.Abs(sources[i].Width - sources[0].Width) > 0.01
                || Math.Abs(sources[i].Height - sources[0].Height) > 0.01)
            {
                return false;
            }
        }

        return true;
    }

    private static HtmlPdfRenderer Renderer(
        double width, double height, ScorePdfInfo info, double rasterResolution)
    {
        var renderer = new HtmlPdfRenderer();
        HtmlRenderOptions options = renderer.Options;
        options.PageWidthPoints = width;
        options.PageHeightPoints = height;
        options.Landscape = false;
        options.MarginTopPoints = 0;
        options.MarginRightPoints = 0;
        options.MarginBottomPoints = 0;
        options.MarginLeftPoints = 0;
        options.HeaderText = null;
        options.FooterText = null;
        options.GenerateOutline = false;
        options.DocumentTitle = string.IsNullOrEmpty(info?.Title) ? null : info.Title;
        options.DocumentAuthor = string.IsNullOrEmpty(info?.Author) ? null : info.Author;
        options.SvgPlacement = SvgPlacementMode.Vector;
        options.SvgRasterScale = Math.Clamp(rasterResolution / SvgPage.SvgDpi, 0.25, 8.0);

        //The house rule: a character no face covers draws tofu rather than
        //vanishing, so a gap is seen (feedback: never fall back to system fonts).
        options.KeepUncoveredCharacters = true;

        //The engine's faces have CFF outlines. Without this they would go into
        //the file whole — about 60 KB a face — under a subset-style name; with
        //it only the glyphs the score uses are kept (CodeBrix.PdfDocuments
        //1.0.238.1192, built for this export).
        options.CffSubsetMode = PdfCffSubsetMode.Sparse;
        return renderer;
    }

    private static string Html(
        IEnumerable<(string File, double Width, double Height)> images, SKColor? paperColor)
    {
        var html = new StringBuilder();
        html.Append("<html><head><style>body{margin:0;padding:0}img{display:block;margin:0;padding:0}</style></head><body>");
        foreach (var (file, width, height) in images)
        {
            string size = "width:" + Points(width) + "pt;height:" + Points(height) + "pt";
            if (paperColor != null)
            {
                html.Append("<div style=\"").Append(size).Append(";background-color:")
                    .Append(Css(paperColor.Value)).Append("\">");
            }

            html.Append("<img src=\"").Append(file).Append("\" style=\"").Append(size).Append("\">");
            if (paperColor != null) { html.Append("</div>"); }
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    private static byte[] Render(
        List<PageSource> sources, string directory, double width, double height,
        ScorePdfInfo info, SKColor? paperColor, double rasterResolution, IList<string> warnings)
    {
        var images = new List<(string, double, double)>();
        for (int i = 0; i < sources.Count; i++)
        {
            images.Add(("page-" + (i + 1) + ".svg", sources[i].Width, sources[i].Height));
        }

        HtmlPdfRenderer renderer = Renderer(width, height, info, rasterResolution);
        HtmlRenderResult result = renderer.RenderHtmlToBytes(Html(images, paperColor), directory);
        Collect(result, warnings);
        return result.PdfBytes;
    }

    private static byte[] Merge(
        List<PageSource> sources, string directory,
        ScorePdfInfo info, SKColor? paperColor, double rasterResolution, IList<string> warnings)
    {
        using var output = new PdfDocument();
        for (int i = 0; i < sources.Count; i++)
        {
            PageSource source = sources[i];
            HtmlPdfRenderer renderer = Renderer(source.Width, source.Height, info, rasterResolution);
            var images = new[] { ("page-" + (i + 1) + ".svg", source.Width, source.Height) };
            HtmlRenderResult result = renderer.RenderHtmlToBytes(Html(images, paperColor), directory);
            Collect(result, warnings);

            using var stream = new MemoryStream(result.PdfBytes);
            PdfDocument one = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            for (int p = 0; p < one.PageCount; p++) { output.AddPage(one.Pages[p]); }
        }

        using var memory = new MemoryStream();
        output.Save(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// The pass that sets what Html2Pdf's options cannot: the rest of the
    /// document information, and each rotated page's <c>/Rotate</c>.
    /// </summary>
    private static byte[] Finish(byte[] rendered, List<PageSource> sources, ScorePdfInfo info)
    {
        bool rotated = false;
        foreach (PageSource source in sources) { rotated |= source.QuarterTurns != 0; }

        bool moreInfo = !string.IsNullOrEmpty(info?.Subject)
            || !string.IsNullOrEmpty(info?.Keywords)
            || !string.IsNullOrEmpty(info?.Creator);
        if (!rotated && !moreInfo) { return rendered; }

        using var input = new MemoryStream(rendered);
        using PdfDocument document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        if (!string.IsNullOrEmpty(info?.Subject)) { document.Info.Subject = info.Subject; }

        if (!string.IsNullOrEmpty(info?.Keywords)) { document.Info.Keywords = info.Keywords; }

        if (!string.IsNullOrEmpty(info?.Creator)) { document.Info.Creator = info.Creator; }

        if (rotated)
        {
            for (int i = 0; i < sources.Count && i < document.PageCount; i++)
            {
                if (sources[i].QuarterTurns != 0)
                {
                    document.Pages[i].Rotate = sources[i].QuarterTurns * 90;
                }
            }
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static void Collect(HtmlRenderResult result, IList<string> warnings)
    {
        if (warnings == null || result?.Warnings == null) { return; }

        foreach (RenderWarning warning in result.Warnings.Items)
        {
            warnings.Add(warning.Code + ": " + warning.Message
                + (warning.Occurrences > 1 ? " (x" + warning.Occurrences + ")" : string.Empty));
        }
    }

    private static string Points(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Css(SKColor color)
        => "#" + color.Red.ToString("x2") + color.Green.ToString("x2") + color.Blue.ToString("x2");

    /// <summary>
    /// Where a page's vectors come from: an SVG file, the part of it to show,
    /// the box to show it in, and how the view had it turned.
    /// </summary>
    private sealed class PageSource
    {
        private PageSource() { }

        public string FileName { get; private set; }
        public double Width { get; private set; }
        public double Height { get; private set; }
        public int QuarterTurns { get; private set; }

        //The region, as fractions of the UNROTATED page, when only part is wanted.
        public (double Left, double Top, double Width, double Height)? Region { get; private set; }

        public static PageSource For(ScorePage page)
        {
            if (page is SvgPage svg)
            {
                var (width, height) = PageSizePoints(page);
                int turns = (int)page.ComputedRotation & 3;
                //The box is the UNROTATED paper; the turn is applied by /Rotate.
                if ((turns & 1) != 0) { (width, height) = (height, width); }

                return new PageSource
                {
                    FileName = svg.FileName, Width = width, Height = height, QuarterTurns = turns,
                };
            }

            if (page is CroppedPage cropped && cropped.Source is SvgPage source)
            {
                PageSource whole = For(source);
                var region = cropped.UnrotatedFraction();
                whole.Region = region;
                whole.Width = Math.Max(whole.Width * region.Width, 1.0);
                whole.Height = Math.Max(whole.Height * region.Height, 1.0);
                return whole;
            }

            throw new NotSupportedException(
                "A PDF is written from the engine's own SVG pages; this page is not over an SVG file.");
        }

        /// <summary>
        /// Writes the page's SVG for Html2Pdf: the engine's file, with the
        /// families the host maps re-named, and — for a region — the root
        /// element's viewBox narrowed to it.
        /// </summary>
        public void WriteSvg(string path, Func<string, string> familyMapper)
        {
            string text = File.ReadAllText(FileName);
            if (familyMapper != null)
            {
                text = FontFamilyAttribute.Replace(text, match =>
                {
                    string quoted = match.Groups[1].Value;
                    string value = quoted.Substring(1, quoted.Length - 2);
                    string mapped = familyMapper(value);
                    return mapped == null ? match.Value : "font-family=\"" + mapped + "\"";
                });
            }

            if (Region != null) { text = Narrow(text, Region.Value); }

            File.WriteAllText(path, text);
        }

        /// <summary>Rewrites the root element so its viewBox is the region.</summary>
        private static string Narrow(string text, (double Left, double Top, double Width, double Height) region)
        {
            Match root = RootStart.Match(text);
            if (!root.Success) { return text; }

            string element = root.Value;
            Match viewBox = Regex.Match(element, "viewBox=\"([^\"]*)\"");
            Match width = Regex.Match(element, "\\bwidth=\"([^\"]*)\"");
            Match height = Regex.Match(element, "\\bheight=\"([^\"]*)\"");
            if (!viewBox.Success || !width.Success || !height.Success) { return text; }

            string[] parts = viewBox.Groups[1].Value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) { return text; }

            double x = Parse(parts[0]), y = Parse(parts[1]), w = Parse(parts[2]), h = Parse(parts[3]);
            var (wValue, wUnit) = SplitLength(width.Groups[1].Value);
            var (hValue, hUnit) = SplitLength(height.Groups[1].Value);

            string newViewBox = "viewBox=\"" + Points(x + w * region.Left) + " " + Points(y + h * region.Top)
                + " " + Points(w * region.Width) + " " + Points(h * region.Height) + "\"";
            string newWidth = "width=\"" + Points(wValue * region.Width) + wUnit + "\"";
            string newHeight = "height=\"" + Points(hValue * region.Height) + hUnit + "\"";

            string rebuilt = element
                .Replace(viewBox.Value, newViewBox)
                .Replace(width.Value, newWidth)
                .Replace(height.Value, newHeight);
            return text.Substring(0, root.Index) + rebuilt + text.Substring(root.Index + root.Length);
        }

        private static (double Value, string Unit) SplitLength(string text)
        {
            string value = text.Trim();
            int end = value.Length;
            while (end > 0 && !(char.IsDigit(value[end - 1]) || value[end - 1] == '.')) { end--; }

            return (Parse(value.Substring(0, end)), value.Substring(end));
        }

        private static double Parse(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0.0;
    }
}
