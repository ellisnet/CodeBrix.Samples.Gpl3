// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Imaging;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.PdfRasterizer;
using Fresco.Brix.MusicView;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Fresco.Brix.Documentation;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One open manual: its pages as things the paged view can draw, its table of
/// contents, and the rasteriser that turns a page into a picture.
/// </summary>
/// <remarks>
/// <para>
/// Ruling FR8: the manuals are PDFs and there is NO WebView anywhere. They are
/// shown through <c>CodeBrix.PdfRasterizer</c> in exactly the paged view the
/// Music View uses, so a reader gets the same zoom, the same fit modes, the
/// same continuous-scroll and the same paging on all six heads.
/// </para>
/// <para>
/// ⚠ A PAGE OF MUSIC AND A PAGE OF MANUAL ARE NOT THE SAME PROBLEM. An engraved
/// page is SVG: it parses once and then redraws at any zoom in a millisecond or
/// two, which is why the Music View carries no cache and no render threads
/// (board trap 13). A PDF page has to be RASTERISED, at a size, by a native
/// library — so this class is the layer the Music View deliberately does
/// without: a background renderer, a picture cache with a byte budget, and a
/// page that draws whatever it has while it waits.
/// </para>
/// <para>
/// ⚠ THE VIEW LIBRARY KNOWS NOTHING ABOUT PDF. <c>Fresco.Brix.MusicView</c>
/// takes an <see cref="IPageImageSource"/>; everything below is application
/// code. That is the same boundary that keeps LilyPort out of the view.
/// </para>
/// </remarks>
public sealed class PdfManual : IDisposable
{
    /// <summary>The most pixels the cache will hold, as bytes of picture.</summary>
    /// <remarks>An A4 page at a thousand pixels wide is about 5.7&#160;MB, so
    /// this is roughly eleven ordinary pages or two at the widest rendering —
    /// comfortably more than any layout shows at once, and bounded, which
    /// matters when the reader is in a 1,280-page manual.</remarks>
    public const long CacheBytes = 64L * 1024 * 1024;

    /// <summary>The widest a page is ever rendered, in pixels.</summary>
    /// <remarks>Beyond this the picture is SCALED UP to fill the page rather
    /// than re-rendered. A page rendered at 2,048 pixels is about 250 dpi;
    /// asking PDFium for the 4,000-pixel page a 400% zoom would want costs
    /// ninety megabytes for one sheet and buys nothing a reader can see.</remarks>
    public const int MaxRenderWidth = 2048;

    /// <summary>Render widths are rounded up to a multiple of this.</summary>
    /// <remarks>A zoom is a stream of slightly different widths, and rendering
    /// each one would keep a 1,280-page manual permanently busy. Bucketing
    /// means a drag re-renders a few times instead of sixty, and the page in
    /// between is the last rendering, scaled.</remarks>
    public const int RenderWidthStep = 256;

    private readonly PageRasterizer _rasterizer;
    private readonly List<PageSource> _pages = new List<PageSource>();
    private readonly Dictionary<int, CachedPage> _cache = new Dictionary<int, CachedPage>();
    private readonly LinkedList<int> _order = new LinkedList<int>();
    private readonly HashSet<long> _inFlight = new HashSet<long>();
    private readonly SynchronizationContext _context;

    private long _cachedBytes;
    private bool _disposed;

    private PdfManual(
        ManualDefinition definition,
        string path,
        int pageCount,
        double pageWidth,
        double pageHeight,
        IReadOnlyList<ManualOutlineEntry> outline)
    {
        Definition = definition;
        Path = path;
        PageCount = pageCount;
        PageWidthPoints = pageWidth;
        PageHeightPoints = pageHeight;
        Outline = outline;

        //Captured where the manual is opened, which is the UI thread. Every
        //picture is rasterised on a worker and handed back through this, so the
        //cache and the view are only ever touched from one thread — the same
        //rule W3 had to learn for the engine's own messages (board trap 22).
        _context = SynchronizationContext.Current;
        _rasterizer = new PageRasterizer();

        for (int number = 1; number <= pageCount; number++)
        {
            _pages.Add(new PageSource(this, number));
        }
    }

    /// <summary>Gets which manual this is.</summary>
    public ManualDefinition Definition { get; }

    /// <summary>Gets the PDF's path.</summary>
    public string Path { get; }

    /// <summary>Gets how many pages it has.</summary>
    public int PageCount { get; }

    /// <summary>Gets the page width, in points.</summary>
    public double PageWidthPoints { get; }

    /// <summary>Gets the page height, in points.</summary>
    public double PageHeightPoints { get; }

    /// <summary>Gets the table of contents, flattened into reading order.</summary>
    public IReadOnlyList<ManualOutlineEntry> Outline { get; }

    /// <summary>Gets the pages, as picture sources the paged view can draw.</summary>
    public IReadOnlyList<IPageImageSource> Pages => _pages;

    /// <summary>Opens a manual, reading its geometry and contents off the disk.</summary>
    /// <param name="definition">Which manual it is.</param>
    /// <param name="path">The PDF.</param>
    /// <returns>The open manual, or null when the file is missing or unreadable.</returns>
    /// <remarks>
    /// The whole read happens on a worker: the Notation Reference is
    /// thirty-four megabytes and takes half a second to index, which is half a
    /// second the window must not spend frozen.
    /// </remarks>
    public static async Task<PdfManual> OpenAsync(ManualDefinition definition, string path)
    {
        if (definition == null || string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        ManualStructure structure = await Task
            .Run(() => ManualOutline.ReadStructure(path))
            .ConfigureAwait(true);
        if (structure == null) { return null; }

        return new PdfManual(
            definition, path, structure.PageCount,
            structure.PageWidthPoints, structure.PageHeightPoints, structure.Outline);
    }

    /// <summary>Builds a view document over this manual's pages.</summary>
    /// <returns>The document.</returns>
    public MusicDocument ToDocument()
        => new MusicDocument(RasterPage.Load(_pages)) { FileName = Path };

    /// <summary>Releases the rasteriser and every cached picture.</summary>
    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        foreach (var cached in _cache.Values) { cached.Image?.Dispose(); }

        _cache.Clear();
        _order.Clear();
        _cachedBytes = 0;
        _rasterizer.Dispose();
    }

    /// <summary>The width a page is actually rendered at for a wanted width.</summary>
    /// <param name="wantedWidth">The width the layout asked for.</param>
    /// <returns>The render width.</returns>
    internal static int RenderWidthFor(int wantedWidth)
    {
        int width = Math.Clamp(wantedWidth, RenderWidthStep, MaxRenderWidth);
        int steps = (width + RenderWidthStep - 1) / RenderWidthStep;
        return Math.Min(steps * RenderWidthStep, MaxRenderWidth);
    }

    private SKImage ImageFor(PageSource page, int wantedWidth)
    {
        if (_disposed || wantedWidth <= 0) { return null; }

        int width = RenderWidthFor(wantedWidth);
        if (_cache.TryGetValue(page.Number, out CachedPage cached))
        {
            Touch(page.Number);

            //The right rendering, or a rendering: a page scaled from the last
            //width is what a reader sees mid-zoom instead of a blank sheet.
            if (cached.Width == width) { return cached.Image; }

            Render(page, width);
            return cached.Image;
        }

        Render(page, width);
        return null;
    }

    private void Render(PageSource page, int width)
    {
        long key = ((long)page.Number << 20) | (uint)width;
        if (!_inFlight.Add(key)) { return; }

        string path = Path;
        int number = page.Number;

        //The dpi that produces the wanted pixel width. PDFium is asked in dots
        //per inch and the page is measured in points, which is the same inch
        //counted seventy-two ways.
        int dpi = Math.Max(1, (int)Math.Round(width * 72.0 / PageWidthPoints));

        _ = Task.Run(async () =>
        {
            SKImage image = null;
            try
            {
                image = await RasterizeAsync(path, number, dpi).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                or InvalidOperationException or NotSupportedException
                or ArgumentException or ObjectDisposedException)
            {
                image = null;
            }

            Post(() => Arrived(page, width, key, image));
        });
    }

    private async Task<SKImage> RasterizeAsync(string path, int number, int dpi)
    {
        using Image raw = await _rasterizer
            .RasterizeToImage(path, pageNumber: number, dpi: dpi)
            .ConfigureAwait(false);
        if (raw == null) { return null; }

        //Straight from the rasteriser's pixels into Skia's, rather than out
        //through a PNG and back: the encode and decode would cost more than
        //the rendering did.
        using Image<Bgra32> bgra = raw.CloneAs<Bgra32>();
        byte[] pixels = new byte[(long)bgra.Width * bgra.Height * 4];
        bgra.CopyPixelDataTo(pixels);

        //Opaque: the rasteriser paints its own white background behind the
        //page, so there is no alpha to blend and saying so saves the blend.
        SKImageInfo info = new SKImageInfo(
            bgra.Width, bgra.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        return SKImage.FromPixelCopy(info, pixels);
    }

    private void Arrived(PageSource page, int width, long key, SKImage image)
    {
        _inFlight.Remove(key);

        if (_disposed || image == null)
        {
            image?.Dispose();
            return;
        }

        if (_cache.TryGetValue(page.Number, out CachedPage existing))
        {
            _cachedBytes -= existing.Bytes;
            existing.Image?.Dispose();
            _order.Remove(page.Number);
        }

        long bytes = (long)image.Width * image.Height * 4;
        _cache[page.Number] = new CachedPage(image, width, bytes);
        _order.AddFirst(page.Number);
        _cachedBytes += bytes;
        Evict();

        page.Announce();
    }

    private void Touch(int number)
    {
        _order.Remove(number);
        _order.AddFirst(number);
    }

    private void Evict()
    {
        //Never evict the newest arrival, however large: a single page wider
        //than the whole budget would otherwise be thrown away the moment it
        //arrived and asked for again forever.
        while (_cachedBytes > CacheBytes && _order.Count > 1)
        {
            int oldest = _order.Last.Value;
            _order.RemoveLast();
            if (_cache.Remove(oldest, out CachedPage cached))
            {
                _cachedBytes -= cached.Bytes;
                cached.Image?.Dispose();
            }
        }
    }

    private void Post(Action action)
    {
        if (_context != null)
        {
            _context.Post(_ => action(), null);
            return;
        }

        //No context means a host-free test, where there is no other thread to
        //get back onto.
        action();
    }

    private sealed class CachedPage
    {
        internal CachedPage(SKImage image, int width, long bytes)
        {
            Image = image;
            Width = width;
            Bytes = bytes;
        }

        internal SKImage Image { get; }

        internal int Width { get; }

        internal long Bytes { get; }
    }

    private sealed class PageSource : IPageImageSource
    {
        private readonly PdfManual _manual;

        internal PageSource(PdfManual manual, int number)
        {
            _manual = manual;
            Number = number;
        }

        public event EventHandler ImageReady;

        internal int Number { get; }

        public (double Width, double Height) NaturalSize
            => (_manual.PageWidthPoints, _manual.PageHeightPoints);

        public SKImage Image(int widthPixels, int heightPixels)
            => _manual.ImageFor(this, widthPixels);

        internal void Announce() => ImageReady?.Invoke(this, EventArgs.Empty);
    }
}
