// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;

namespace Fresco.Brix.MusicView; //was previously: qpageview/export.py PdfExporter and SvgExporter

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Exports a region of a page — or the whole page — to a one-page PDF.
/// </summary>
/// <remarks>
/// Upstream's <c>PdfExporter</c>. The multi-page case is
/// <see cref="ScorePdf.Write(string, System.Collections.Generic.IEnumerable{ScorePage},
/// ScorePdfInfo, System.Nullable{SKColor}, double, ScorePdfFonts, System.Collections.Generic.IList{string})"/>,
/// exactly as upstream keeps <c>export.pdf()</c> apart from the exporter class.
/// A region is written as the engine's own SVG with its viewBox narrowed to
/// the region — vectors still — so this exporter needs a page over an SVG file.
/// </remarks>
public sealed class PdfExporter : PageExporter
{
    /// <summary>Creates an exporter over a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="rect">The region, in page coordinates; null for the whole page.</param>
    public PdfExporter(ScorePage page, SKRect? rect = null)
        : base(page, rect)
    {
    }

    /// <summary>Gets or sets what the document should say about itself.</summary>
    public ScorePdfInfo Info { get; set; }

    /// <summary>Gets or sets the score's faces, or null for Html2Pdf's packaged ones.</summary>
    public ScorePdfFonts Fonts { get; set; }

    /// <inheritdoc/>
    public override string MimeType => "application/pdf";

    /// <inheritdoc/>
    public override string DefaultExtension => ".pdf";

    /// <inheritdoc/>
    public override bool SupportsGrayscale => false;

    /// <inheritdoc/>
    public override bool SupportsOversample => false;

    /// <inheritdoc/>
    protected override byte[] Export()
        => ScorePdf.ToBytes(new[] { RegionPage() }, Info, PaperColor, Resolution, Fonts);

    /// <inheritdoc/>
    public override ScorePage PreviewPage() => RegionPage();

    /// <summary>
    /// Returns the page to write: the whole page, or a page cropped to the
    /// wanted region.
    /// </summary>
    /// <returns>The page.</returns>
    private ScorePage RegionPage()
    {
        SKRect? region = AutoCroppedRect();
        return region == null ? Page : new CroppedPage(Page, region.Value);
    }
}

/// <summary>
/// Exports a region of a page — or the whole page — to a one-page SVG.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>SvgExporter</c>, over a <c>QSvgGenerator</c>. Skia's
/// equivalent is <c>SKSvgCanvas</c>, and it is the same kind of thing: a canvas
/// that records what is drawn on it as SVG elements rather than pixels.
/// </para>
/// <para>
/// ⚠ THE RESULT IS A RE-RECORDING, not the file the engine wrote. A whole
/// page's worth of engraving goes in and comes out as the same shapes with the
/// same geometry, but the engine's own element structure — the
/// <c>textedit://</c> anchors above all — does not survive, because the anchors
/// are not drawing. A caller that wants the engine's SVG should copy the file;
/// this is for the case upstream built it for, a REGION of a page.
/// </para>
/// </remarks>
public sealed class SvgExporter : PageExporter
{
    /// <summary>Creates an exporter over a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="rect">The region, in page coordinates; null for the whole page.</param>
    public SvgExporter(ScorePage page, SKRect? rect = null)
        : base(page, rect)
    {
    }

    /// <inheritdoc/>
    public override string MimeType => "image/svg+xml";

    /// <inheritdoc/>
    public override string DefaultBaseName => "image";

    /// <inheritdoc/>
    public override string DefaultExtension => ".svg";

    /// <inheritdoc/>
    public override bool SupportsGrayscale => false;

    /// <inheritdoc/>
    public override bool SupportsOversample => false;

    /// <inheritdoc/>
    protected override byte[] Export()
    {
        SKRect? region = AutoCroppedRect();
        ScorePage page = region == null ? Page : new CroppedPage(Page, region.Value);

        var (naturalWidth, naturalHeight) = page.DefaultSize();
        double width = naturalWidth * Resolution / page.Dpi;
        double height = naturalHeight * Resolution / page.Dpi;

        using var memory = new System.IO.MemoryStream();
        using (var stream = new SKManagedWStream(memory))
        {
            var bounds = new SKRect(0f, 0f, (float)width, (float)height);
            using (SKCanvas canvas = SKSvgCanvas.Create(bounds, stream))
            {
                if (canvas == null) { return null; }

                page.Draw(canvas, width, height, PaperColor);
            }

            stream.Flush();
        }

        byte[] bytes = memory.ToArray();
        return bytes.Length == 0 ? null : bytes;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The preview is the SOURCE page rather than a page over the bytes just
    /// written: reading them back would show what the re-recording lost, which
    /// is a fair thing to want but not what upstream's preview is for.
    /// </remarks>
    public override ScorePage PreviewPage()
    {
        SKRect? region = AutoCroppedRect();
        return region == null ? Page : new CroppedPage(Page, region.Value);
    }
}

/// <summary>
/// One page showing a REGION of another page, at that region's size.
/// </summary>
/// <remarks>
/// There is no such class upstream: a Qt exporter passes the rectangle down to
/// the paint device and lets the painter's clip do the work. A Skia page draws
/// itself into its own rectangle, so the region is expressed as a page instead —
/// which has the pleasant effect that every exporter, and the preview, take the
/// same thing.
/// </remarks>
internal sealed class CroppedPage : ScorePage
{
    private readonly ScorePage _page;
    private readonly SKRect _region;

    /// <summary>Creates a page over a region of another.</summary>
    /// <param name="page">The page.</param>
    /// <param name="region">The region, in that page's coordinates.</param>
    internal CroppedPage(ScorePage page, SKRect region)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _region = region;
        Dpi = page.Dpi;
        ScaleX = page.ScaleX;
        ScaleY = page.ScaleY;

        //The region is in DISPLAYED pixels; the natural size it stands for is
        //the same fraction of the natural size.
        double fractionX = page.Width > 0 ? region.Width / page.Width : 0.0;
        double fractionY = page.Height > 0 ? region.Height / page.Height : 0.0;
        SetPageSize(Math.Max(page.PageWidth * fractionX, 1.0), Math.Max(page.PageHeight * fractionY, 1.0));
        Width = Math.Max(1, (int)Math.Round(region.Width));
        Height = Math.Max(1, (int)Math.Round(region.Height));
    }

    /// <summary>Gets the page the region is of.</summary>
    internal ScorePage Source => _page;

    /// <summary>Gets the region, in the source page's DISPLAYED coordinates.</summary>
    internal SKRect Region => _region;

    /// <summary>
    /// Returns the region as fractions of the source page's UNROTATED extent —
    /// what a viewBox has to be narrowed to.
    /// </summary>
    /// <returns>Left, top, width and height, each 0..1.</returns>
    /// <remarks>
    /// The region is in the displayed page, which the view may have turned. The
    /// four cases follow <see cref="ScorePage.Transform"/>: a quarter turn
    /// clockwise maps an unrotated point (u, v) to (H − v, u), a half turn to
    /// (W − u, H − v), three quarters to (v, W − u), for the unrotated page's
    /// displayed width W and height H.
    /// </remarks>
    internal (double Left, double Top, double Width, double Height) UnrotatedFraction()
    {
        int turns = (int)_page.ComputedRotation & 3;
        double displayedWidth = Math.Max(_page.Width, 1);
        double displayedHeight = Math.Max(_page.Height, 1);
        double unrotatedWidth = (turns & 1) != 0 ? displayedHeight : displayedWidth;
        double unrotatedHeight = (turns & 1) != 0 ? displayedWidth : displayedHeight;

        double x0 = _region.Left, y0 = _region.Top, x1 = _region.Right, y1 = _region.Bottom;
        double u0, v0, u1, v1;
        switch (turns)
        {
            case 1:
                u0 = y0; u1 = y1; v0 = unrotatedHeight - x1; v1 = unrotatedHeight - x0;
                break;
            case 2:
                u0 = unrotatedWidth - x1; u1 = unrotatedWidth - x0; v0 = unrotatedHeight - y1; v1 = unrotatedHeight - y0;
                break;
            case 3:
                u0 = unrotatedWidth - y1; u1 = unrotatedWidth - y0; v0 = x0; v1 = x1;
                break;
            default:
                u0 = x0; u1 = x1; v0 = y0; v1 = y1;
                break;
        }

        return (
            Math.Clamp(u0 / unrotatedWidth, 0.0, 1.0),
            Math.Clamp(v0 / unrotatedHeight, 0.0, 1.0),
            Math.Clamp((u1 - u0) / unrotatedWidth, 0.0, 1.0),
            Math.Clamp((v1 - v0) / unrotatedHeight, 0.0, 1.0));
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas, SKRect rect)
    {
        if (PaperColor.HasValue) { canvas.DrawRect(Rect, new SKPaint { Color = PaperColor.Value }); }

        ScorePage source = _page.Copy();
        source.PaperColor = null;

        //The source is asked to draw itself at the size this page was given, so
        //a cropped export scales with the rest of the export rather than being
        //pinned to the resolution the view happened to be at.
        double scaleX = _region.Width > 0 ? Width / _region.Width : 1.0;
        double scaleY = _region.Height > 0 ? Height / _region.Height : 1.0;
        source.Width = Math.Max(1, (int)Math.Round(_page.Width * scaleX));
        source.Height = Math.Max(1, (int)Math.Round(_page.Height * scaleY));

        int saved = canvas.Save();
        canvas.ClipRect(new SKRect(0f, 0f, Width, Height));
        canvas.Translate((float)(-_region.Left * scaleX), (float)(-_region.Top * scaleY));
        source.Paint(canvas, new SKRect(
            (float)(_region.Left * scaleX), (float)(_region.Top * scaleY),
            (float)(_region.Right * scaleX), (float)(_region.Bottom * scaleY)));
        canvas.RestoreToCount(saved);
        (source as IDisposable)?.Dispose();
    }
}
