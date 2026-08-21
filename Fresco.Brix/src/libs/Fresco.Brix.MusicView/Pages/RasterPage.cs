// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.MusicView; //was previously: qpageview/image.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Where a <see cref="RasterPage"/> gets its picture from.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>ImageContainer</c>/<c>ImageLoader</c> pair, as one interface.
/// It is an interface rather than a class for the reason the split existed
/// there: a page may be over a picture already in memory, or over something
/// that has to be READ — and reading may be slow enough that it cannot happen
/// while the view is painting.
/// </para>
/// <para>
/// ⚠ THIS IS WHY THE VIEW STAYS FREE OF ANY PDF LIBRARY. The documentation
/// panel's pages are rasterised out of PDFs by CodeBrix.PdfRasterizer, which
/// lives in the application; the view knows only that something can hand it an
/// <see cref="SKImage"/> at a size, and answer how big the page is before it
/// has drawn anything.
/// </para>
/// </remarks>
public interface IPageImageSource
{
    /// <summary>
    /// Gets the page's natural size, in the units the page's
    /// <see cref="ScorePage.Dpi"/> counts.
    /// </summary>
    /// <remarks>Asked before anything is drawn: a layout settles its geometry
    /// first, and a page over a file has not read that file yet (board trap
    /// 33).</remarks>
    (double Width, double Height) NaturalSize { get; }

    /// <summary>
    /// Returns the picture at a size in pixels, or null when it is not ready.
    /// </summary>
    /// <param name="widthPixels">The wanted width.</param>
    /// <param name="heightPixels">The wanted height.</param>
    /// <returns>The picture, or null.</returns>
    /// <remarks>
    /// ⚠ CALLED WHILE PAINTING, ON THE UI THREAD, so it must return whatever it
    /// has and NEVER wait. A source that has to produce the picture starts
    /// doing so and raises <see cref="ImageReady"/> when it can answer; the
    /// view repaints then. Returning a picture at some OTHER size is not only
    /// allowed but wanted — a page scaled up from the last rendering is what a
    /// reader sees during a zoom, instead of a blank rectangle.
    /// </remarks>
    SKImage Image(int widthPixels, int heightPixels);

    /// <summary>Raised when a picture asked for earlier has arrived.</summary>
    event EventHandler ImageReady;
}

/// <summary>
/// One page that is a PICTURE — a rasterised PDF page, or any other bitmap the
/// host can produce.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>ImagePage</c>. The Music View's own pages are
/// <see cref="SvgPage"/>s and stay vector, because a page of engraved music
/// parses once and then redraws at any zoom in milliseconds (board trap 13).
/// A PDF manual cannot do that: its pages have to be rasterised, at a size, by
/// something that takes real time. So this page draws whatever its source has
/// and asks for what it wants; the tile cache upstream needs for its Poppler
/// pages is still not ported, because the unit of work here is a whole PAGE
/// and the source caches those.
/// </para>
/// <para>
/// A raster page carries no links. Upstream's ImagePage has none either, and
/// the point-and-click machinery has nothing to say about a manual.
/// </para>
/// </remarks>
public sealed class RasterPage : ScorePage
{
    /// <summary>The unit a PDF's coordinates are in: points, 1/72 inch.</summary>
    public const double PdfDpi = 72.0;

    private readonly IPageImageSource _source;
    private bool _sized;

    /// <summary>Creates a page over a picture source.</summary>
    /// <param name="source">Who produces the picture.</param>
    /// <param name="number">The page's 1-based number in its document.</param>
    public RasterPage(IPageImageSource source, int number = 1)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Number = number;
        Dpi = PdfDpi;
    }

    /// <summary>Gets the page's 1-based number in its document.</summary>
    public int Number { get; }

    /// <summary>Gets where the picture comes from.</summary>
    public IPageImageSource Source => _source;

    /// <summary>Creates one page per page of a source set.</summary>
    /// <param name="sources">The sources, in page order.</param>
    /// <returns>The pages.</returns>
    public static IReadOnlyList<RasterPage> Load(IEnumerable<IPageImageSource> sources)
    {
        List<RasterPage> pages = new List<RasterPage>();
        if (sources == null) { return pages; }

        int number = 1;
        foreach (IPageImageSource source in sources)
        {
            if (source != null) { pages.Add(new RasterPage(source, number)); }

            number++;
        }

        return pages;
    }

    /// <inheritdoc/>
    protected override void EnsureSize()
    {
        if (_sized) { return; }

        //Set before asking, so a source that answers by calling back into the
        //page cannot start this again.
        _sized = true;
        var (width, height) = _source.NaturalSize;
        if (width > 0 && height > 0) { SetPageSize(width, height); }
    }

    /// <inheritdoc/>
    public override void Paint(SKCanvas canvas, SKRect rect)
    {
        EnsureSize();

        //The paper goes down first and always. A page whose picture has not
        //arrived is a sheet of paper, not a hole in the view — and a picture
        //that turns out to be narrower than the page (a rounding pixel at the
        //right-hand edge) leaves paper showing rather than the view behind it.
        canvas.DrawRect(Rect, new SKPaint { Color = PaperColor ?? SKColors.White });

        SKImage image = _source.Image(Width, Height);
        if (image == null) { return; }

        //Drawn into the page's whole rectangle whatever size came back, so a
        //picture rendered for a different zoom is SCALED rather than dropped:
        //during a zoom that is the difference between reading a slightly soft
        //page and staring at a blank one.
        using SKPaint paint = new SKPaint { IsAntialias = true };
        SKSamplingOptions sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        canvas.DrawImage(image, new SKRect(0f, 0f, Width, Height), sampling, paint);
    }
}
