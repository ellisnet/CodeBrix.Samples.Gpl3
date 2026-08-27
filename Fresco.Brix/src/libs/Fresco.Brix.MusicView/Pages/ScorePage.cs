// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.MusicView; //was previously: qpageview/page.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One page of music: a rectangle that a <see cref="PageLayout"/> positions and
/// a <see cref="MusicViewControl"/> paints.
/// </summary>
/// <remarks>
/// <para>
/// A page knows two sizes. Its NATURAL size (<see cref="PageWidth"/> and
/// <see cref="PageHeight"/>, in units of <see cref="Dpi"/> per inch) never
/// changes; its DISPLAYED size (<see cref="Width"/> and <see cref="Height"/>,
/// in pixels) is computed by the layout from the zoom, the view's resolution
/// and the rotation.
/// </para>
/// <para>
/// Everything a caller wants to say about a spot ON the page — a link's area, a
/// highlight rectangle — is said in the page's own 0-to-1 or natural
/// coordinates and mapped through <see cref="MapToPage"/>, so it survives
/// zooming and rotation without being recomputed.
/// </para>
/// </remarks>
public abstract class ScorePage
{
    /// <summary>The natural unit: points, 1/72 inch, unless a subclass says otherwise.</summary>
    public const double DefaultDpi = 72.0;

    private LinkList _links;

    /// <summary>Gets or sets the units per inch the natural size is in.</summary>
    public double Dpi { get; set; } = DefaultDpi;

    /// <summary>Gets or sets the natural width, in <see cref="Dpi"/> units.</summary>
    /// <remarks>
    /// Reading it makes sure the page KNOWS its size: a page over a file has
    /// not read that file yet, and the layout asks for the size before anything
    /// asks for the picture.
    /// </remarks>
    public double PageWidth
    {
        get
        {
            EnsureSize();
            return field;
        }

        set;
    } = 595.28;

    /// <summary>Gets or sets the natural height, in <see cref="Dpi"/> units.</summary>
    /// <remarks>See <see cref="PageWidth"/>: reading it settles the size first.</remarks>
    public double PageHeight
    {
        get
        {
            EnsureSize();
            return field;
        }

        set;
    } = 841.89;

    /// <summary>Gets or sets the horizontal scale applied to the natural size.</summary>
    public double ScaleX { get; set; } = 1.0;

    /// <summary>Gets or sets the vertical scale applied to the natural size.</summary>
    public double ScaleY { get; set; } = 1.0;

    /// <summary>Gets or sets the rotation the page itself asks for.</summary>
    public Rotation Rotation { get; set; } = Rotation.Rotate0;

    /// <summary>Gets or sets the rotation finally used — the layout sets it.</summary>
    public Rotation ComputedRotation { get; set; } = Rotation.Rotate0;

    /// <summary>Gets or sets the paper colour, or null for the renderer's own.</summary>
    public SKColor? PaperColor { get; set; }

    /// <summary>Gets or sets the x position in the layout, in pixels.</summary>
    public int X { get; set; }

    /// <summary>Gets or sets the y position in the layout, in pixels.</summary>
    public int Y { get; set; }

    /// <summary>Gets or sets the displayed width, in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the displayed height, in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Gets the page's position in the layout.</summary>
    public SKPointI Position => new SKPointI(X, Y);

    /// <summary>Gets the page's rectangle in the layout, in pixels.</summary>
    public SKRectI Geometry => new SKRectI(X, Y, X + Width, Y + Height);

    /// <summary>Gets the page's rectangle at the origin, in pixels.</summary>
    public SKRectI Rect => new SKRectI(0, 0, Width, Height);

    /// <summary>Gets the natural rectangle, at the origin.</summary>
    public SKRect PageRect => new SKRect(0f, 0f, (float)PageWidth, (float)PageHeight);

    /// <summary>
    /// Makes sure the natural size is known, reading whatever the page is over
    /// if it has not been read yet. Does nothing by default.
    /// </summary>
    protected virtual void EnsureSize()
    {
    }

    /// <summary>Sets the natural size.</summary>
    /// <param name="width">The natural width.</param>
    /// <param name="height">The natural height.</param>
    public void SetPageSize(double width, double height)
    {
        PageWidth = width;
        PageHeight = height;
    }

    /// <summary>
    /// Returns the natural size after scale and rotation, before zoom.
    /// </summary>
    /// <returns>The width and height.</returns>
    public (double Width, double Height) DefaultSize()
    {
        double w = PageWidth * ScaleX;
        double h = PageHeight * ScaleY;
        return ((int)ComputedRotation & 1) != 0 ? (h, w) : (w, h);
    }

    /// <summary>
    /// Computes <see cref="Width"/> and <see cref="Height"/> from the natural
    /// size, the view's resolution and the zoom.
    /// </summary>
    /// <param name="dpiX">The view's horizontal resolution.</param>
    /// <param name="dpiY">The view's vertical resolution.</param>
    /// <param name="zoomFactor">The zoom.</param>
    public void UpdateSize(double dpiX, double dpiY, double zoomFactor)
    {
        var (w, h) = DefaultSize();
        Width = (int)Math.Round(w * dpiX / Dpi * zoomFactor);
        Height = (int)Math.Round(h * dpiY / Dpi * zoomFactor);
    }

    /// <summary>Returns the zoom that would display this page at a width.</summary>
    /// <param name="width">The wanted width, in pixels.</param>
    /// <param name="rotation">The layout's rotation.</param>
    /// <param name="dpiX">The view's horizontal resolution.</param>
    /// <returns>The zoom factor.</returns>
    public double ZoomForWidth(double width, Rotation rotation, double dpiX)
    {
        width = Math.Max(width, 1);
        double w = (((int)Rotation + (int)rotation) & 1) != 0
            ? PageHeight / ScaleY
            : PageWidth / ScaleX;
        return width * Dpi / dpiX / w;
    }

    /// <summary>Returns the zoom that would display this page at a height.</summary>
    /// <param name="height">The wanted height, in pixels.</param>
    /// <param name="rotation">The layout's rotation.</param>
    /// <param name="dpiY">The view's vertical resolution.</param>
    /// <returns>The zoom factor.</returns>
    public double ZoomForHeight(double height, Rotation rotation, double dpiY)
    {
        height = Math.Max(height, 1);
        double h = (((int)Rotation + (int)rotation) & 1) != 0
            ? PageWidth / ScaleX
            : PageHeight / ScaleY;
        return height * Dpi / dpiY / h;
    }

    /// <summary>
    /// Returns the matrix mapping the page's own contents onto its displayed
    /// rectangle, honouring the computed rotation.
    /// </summary>
    /// <param name="width">The contents' unrotated width; the natural width by default.</param>
    /// <param name="height">The contents' unrotated height; the natural height by default.</param>
    /// <returns>The matrix.</returns>
    public SKMatrix Transform(double? width = null, double? height = null)
    {
        double w = width ?? PageWidth;
        double h = height ?? PageHeight;
        SKMatrix m = SKMatrix.CreateScale(Width, Height);
        m = m.PreConcat(SKMatrix.CreateTranslation(0.5f, 0.5f));
        m = m.PreConcat(SKMatrix.CreateRotationDegrees((int)ComputedRotation * 90f));
        m = m.PreConcat(SKMatrix.CreateTranslation(-0.5f, -0.5f));
        m = m.PreConcat(SKMatrix.CreateScale((float)(1.0 / w), (float)(1.0 / h)));
        return m;
    }

    /// <summary>
    /// Returns the matrix from the contents' coordinates to page coordinates.
    /// </summary>
    /// <param name="width">The contents' unrotated width; the natural width by default.</param>
    /// <param name="height">The contents' unrotated height; the natural height by default.</param>
    /// <returns>The matrix.</returns>
    public SKMatrix MapToPage(double? width = null, double? height = null) => Transform(width, height);

    /// <summary>
    /// Returns the matrix from page coordinates back to the contents'.
    /// </summary>
    /// <param name="width">The contents' unrotated width; the natural width by default.</param>
    /// <param name="height">The contents' unrotated height; the natural height by default.</param>
    /// <returns>The matrix.</returns>
    public SKMatrix MapFromPage(double? width = null, double? height = null)
        => Transform(width, height).TryInvert(out SKMatrix inverse) ? inverse : SKMatrix.Identity;

    /// <summary>Gets the page's links, loading them on the first request.</summary>
    /// <returns>The links.</returns>
    public LinkList Links() => _links ??= GetLinks();

    /// <summary>Loads the page's links. The base implementation finds none.</summary>
    /// <returns>The links.</returns>
    protected virtual LinkList GetLinks() => new LinkList();

    /// <summary>Forgets the loaded links, so they are read again.</summary>
    protected void InvalidateLinks() => _links = null;

    /// <summary>
    /// Returns the links a point touches, smallest first.
    /// </summary>
    /// <param name="point">The point, in page coordinates.</param>
    /// <returns>The links, smallest area first.</returns>
    public IReadOnlyList<Link> LinksAt(SKPoint point)
    {
        SKPoint p = MapFromPage(1, 1).MapPoint(point);
        LinkList links = Links();
        return links.At(p.X, p.Y).OrderBy(links.Width).ToList();
    }

    /// <summary>Returns the links wholly inside a rectangle.</summary>
    /// <param name="rect">The rectangle, in page coordinates.</param>
    /// <returns>The links, in no particular order.</returns>
    public IEnumerable<Link> LinksIn(SKRect rect)
    {
        SKRect r = MapFromPage(1, 1).MapRect(rect);
        return Links().Inside(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>Returns a link's area in page coordinates.</summary>
    /// <param name="link">The link.</param>
    /// <returns>The rectangle.</returns>
    public SKRect LinkRect(Link link) => MapToPage(1, 1).MapRect(link.Rect());

    /// <summary>
    /// Paints the page onto a canvas whose origin is the page's top-left
    /// corner and whose clip is already the wanted region.
    /// </summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="rect">The region wanted, in page coordinates.</param>
    public abstract void Paint(SKCanvas canvas, SKRect rect);

    /// <summary>
    /// Returns a page that can be given a different size and paper colour
    /// without disturbing this one.
    /// </summary>
    /// <returns>The copy.</returns>
    /// <remarks>
    /// <para>
    /// Upstream's <c>page.copy()</c>, and it exists for upstream's reason: an
    /// export renders the page at a resolution and on a paper colour that are
    /// the EXPORT's, not the view's, and the view may well be painting the
    /// same page on another thread while it happens. An exporter therefore
    /// works on a copy and sets <see cref="Width"/>, <see cref="Height"/> and
    /// <see cref="PaperColor"/> on that.
    /// </para>
    /// <para>
    /// The copy SHARES whatever the page has already read — a parsed SVG is an
    /// immutable Skia picture and drawing one from two threads is safe — so
    /// copying is a field copy and not a second parse. The subclass's job is
    /// to make sure the copy never DISPOSES what it borrowed.
    /// </para>
    /// </remarks>
    public virtual ScorePage Copy() => (ScorePage)MemberwiseClone();

    /// <summary>
    /// Renders a rectangle of the page to a picture at a resolution.
    /// </summary>
    /// <param name="rect">
    /// The region, in the page's DISPLAYED coordinates (0 to <see cref="Width"/>
    /// by 0 to <see cref="Height"/>); the whole page when null.
    /// </param>
    /// <param name="dpiX">The wanted horizontal resolution.</param>
    /// <param name="dpiY">The wanted vertical resolution; <paramref name="dpiX"/> when null.</param>
    /// <param name="paperColor">The background, or null for transparent.</param>
    /// <returns>The picture.</returns>
    /// <remarks>
    /// Upstream's <c>page.image()</c>, and the scale factors are its renderer's
    /// own arithmetic. The port has no renderer layer (board §6.3: a vector page
    /// needs no tile cache), so instead of asking a renderer for the region this
    /// resizes a COPY of the page to the output size and lets the page paint
    /// itself — which is the one drawing path every page kind already has, and
    /// therefore the one that cannot disagree with what the view shows.
    /// </remarks>
    public SKImage Image(
        SKRect? rect = null, double dpiX = 0, double dpiY = 0, SKColor? paperColor = null)
    {
        if (dpiX <= 0) { dpiX = Dpi; }

        if (dpiY <= 0) { dpiY = dpiX; }

        EnsureSize();
        SKRect region = rect ?? new SKRect(0f, 0f, Width, Height);
        var (naturalWidth, naturalHeight) = DefaultSize();
        double hscale = naturalWidth * dpiX / Dpi / Math.Max(Width, 1);
        double vscale = naturalHeight * dpiY / Dpi / Math.Max(Height, 1);

        SKMatrix scale = SKMatrix.CreateScale((float)hscale, (float)vscale);
        SKRect target = scale.MapRect(region);
        int pixelWidth = Math.Max(1, (int)Math.Round(target.Width));
        int pixelHeight = Math.Max(1, (int)Math.Round(target.Height));

        ScorePage page = Copy();
        page.Width = Math.Max(1, (int)Math.Round(Width * hscale));
        page.Height = Math.Max(1, (int)Math.Round(Height * vscale));
        page.PaperColor = paperColor;

        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        if (surface == null)
        {
            //Skia answers null rather than throwing when it cannot allocate the
            //surface, and a page asked for at an absurd size is the way to get
            //there. Answering null in turn is what lets the caller say so.
            (page as IDisposable)?.Dispose();
            return null;
        }

        SKCanvas canvas = surface.Canvas;
        canvas.Clear(paperColor ?? SKColors.Transparent);
        canvas.Translate(-target.Left, -target.Top);
        page.Paint(canvas, target);
        (page as IDisposable)?.Dispose();
        return surface.Snapshot();
    }

    /// <summary>
    /// Draws the page onto a canvas at a size, in whatever units that canvas
    /// counts — the one call an exporter to a VECTOR surface makes.
    /// </summary>
    /// <param name="canvas">The canvas, its origin at the page's top-left corner.</param>
    /// <param name="width">The width to draw at, in the canvas's units.</param>
    /// <param name="height">The height to draw at, in the canvas's units.</param>
    /// <param name="paperColor">The background, or null to draw none.</param>
    /// <remarks>
    /// <para>
    /// Upstream's <c>output()</c>/<c>print()</c> pair, as one method. There the
    /// two exist because a QPainter carries the device's resolution and the
    /// page has to be told about it; here the caller sets the canvas up and
    /// says how big to draw, and a Skia picture is resolution-free — so the
    /// same drawing code serves a bitmap, a PDF page and an SVG canvas.
    /// </para>
    /// <para>
    /// ⚠ The page is drawn to FILL the given size. A caller that wants the
    /// page's true physical size on paper computes that size itself and passes
    /// it (board trap 61): the SVG's declared millimetres are exact where its
    /// pixel viewport is rounded to whole pixels.
    /// </para>
    /// </remarks>
    public void Draw(SKCanvas canvas, double width, double height, SKColor? paperColor = null)
    {
        if (canvas == null) { throw new ArgumentNullException(nameof(canvas)); }

        EnsureSize();

        //A page paints itself in whole displayed pixels, so it is sized to the
        //nearest unit and the remainder is taken out in the canvas's own scale.
        //Nothing is lost: the page fills the target exactly either way, and the
        //content stays vector all the way down.
        int unitWidth = Math.Max(1, (int)Math.Round(width));
        int unitHeight = Math.Max(1, (int)Math.Round(height));

        ScorePage page = Copy();
        page.Width = unitWidth;
        page.Height = unitHeight;
        page.PaperColor = paperColor;

        int saved = canvas.Save();
        canvas.Scale((float)(width / unitWidth), (float)(height / unitHeight));
        page.Paint(canvas, page.Rect);
        canvas.RestoreToCount(saved);
        (page as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Returns a small picture of the page, for a drag cursor or a thumbnail.
    /// </summary>
    /// <param name="size">The longest side, in pixels.</param>
    /// <param name="paperColor">The background, or null for transparent.</param>
    /// <returns>The picture.</returns>
    public SKImage Pixmap(int size = 100, SKColor? paperColor = null)
    {
        EnsureSize();
        var (naturalWidth, naturalHeight) = DefaultSize();
        double longest = Math.Max(Math.Max(naturalWidth, naturalHeight), 1.0);
        double dpi = Dpi * size / longest;
        return Image(null, dpi, dpi, paperColor);
    }
}
