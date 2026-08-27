// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.MusicView; //was previously: qpageview/layout.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Margins around a page or around the whole layout, in pixels.</summary>
/// <param name="Left">The left margin.</param>
/// <param name="Top">The top margin.</param>
/// <param name="Right">The right margin.</param>
/// <param name="Bottom">The bottom margin.</param>
public readonly record struct PageMargins(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Creates equal margins on all four sides.</summary>
    /// <param name="all">The margin.</param>
    public PageMargins(int all)
        : this(all, all, all, all)
    {
    }

    /// <summary>Gets the left plus the right margin.</summary>
    public int Horizontal => Left + Right;

    /// <summary>Gets the top plus the bottom margin.</summary>
    public int Vertical => Top + Bottom;
}

/// <summary>
/// Where the pages of a document sit relative to one another, and how large
/// each of them is drawn.
/// </summary>
/// <remarks>
/// The layout owns the pages and the zoom; the actual arrangement is delegated
/// to a <see cref="LayoutEngine"/>, which is what makes "two pages, first on
/// the right" a different object rather than a thicket of conditions.
/// </remarks>
public sealed class PageLayout : IEnumerable<ScorePage>
{
    private readonly List<ScorePage> _pages = new List<ScorePage>();
    private PageRects _rects;

    /// <summary>Creates a layout with the default single-row engine.</summary>
    public PageLayout() => Engine = new LayoutEngine();

    /// <summary>Gets or sets the engine that arranges the pages.</summary>
    public LayoutEngine Engine
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the margins around the whole layout.</summary>
    public PageMargins Margins { get; set; } = new PageMargins(6);

    /// <summary>Gets or sets the margins around each page.</summary>
    public PageMargins PageMargins { get; set; } = new PageMargins(0);

    /// <summary>Gets or sets the gap between pages, in pixels.</summary>
    public int Spacing { get; set; } = 8;

    /// <summary>Gets or sets the zoom.</summary>
    public double ZoomFactor { get; set; } = 1.0;

    /// <summary>Gets or sets the horizontal resolution the pages are drawn at.</summary>
    public double DpiX { get; set; } = 96.0;

    /// <summary>Gets or sets the vertical resolution the pages are drawn at.</summary>
    public double DpiY { get; set; } = 96.0;

    /// <summary>Gets or sets the rotation applied to every page.</summary>
    public Rotation Rotation { get; set; } = Rotation.Rotate0;

    /// <summary>Gets or sets which way a row of pages runs.</summary>
    public LayoutOrientation Orientation { get; set; } = LayoutOrientation.Vertical;

    /// <summary>Gets or sets where a page sits in the space given to it.</summary>
    public PageAlignment Alignment { get; set; } = PageAlignment.Center;

    /// <summary>Gets or sets whether all pages are shown at once.</summary>
    public bool ContinuousMode { get; set; } = true;

    /// <summary>Gets or sets which page set is shown when not continuous.</summary>
    public int CurrentPageSet { get; set; }

    /// <summary>Gets the layout's x position.</summary>
    public int X { get; private set; }

    /// <summary>Gets the layout's y position.</summary>
    public int Y { get; private set; }

    /// <summary>Gets the layout's total width, margins included.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the layout's total height, margins included.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the layout's rectangle.</summary>
    public SKRectI Geometry => new SKRectI(X, Y, X + Width, Y + Height);

    /// <summary>Gets how many pages the layout holds.</summary>
    public int Count => _pages.Count;

    /// <summary>Gets whether the layout holds no pages.</summary>
    public bool IsEmpty => _pages.Count == 0;

    /// <summary>Gets the page at an index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The page.</returns>
    public ScorePage this[int index] => _pages[index];

    /// <summary>Gets whether the engine changes the zoom to fit.</summary>
    public bool ZoomsToFit => Engine.ZoomToFit;

    /// <summary>Replaces the pages.</summary>
    /// <param name="pages">The new pages, in order.</param>
    public void SetPages(IEnumerable<ScorePage> pages)
    {
        _pages.Clear();
        if (pages != null) { _pages.AddRange(pages); }

        _rects = null;
    }

    /// <summary>Removes every page.</summary>
    public void Clear()
    {
        _pages.Clear();
        _rects = null;
    }

    /// <summary>Returns the index of a page, or -1.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The index.</returns>
    public int IndexOf(ScorePage page) => _pages.IndexOf(page);

    /// <summary>Returns the page a point is on, or null.</summary>
    /// <param name="point">The point, in layout coordinates.</param>
    /// <returns>The page.</returns>
    public ScorePage PageAt(SKPoint point) => Rects().At(point.X, point.Y).FirstOrDefault();

    /// <summary>Returns the pages a rectangle touches, in no particular order.</summary>
    /// <param name="rect">The rectangle, in layout coordinates.</param>
    /// <returns>The pages.</returns>
    public IEnumerable<ScorePage> PagesAt(SKRect rect)
        => Rects().Intersecting(rect.Left, rect.Top, rect.Right, rect.Bottom);

    /// <summary>
    /// Returns the page nearest a point that the point is NOT on, or null.
    /// </summary>
    /// <param name="point">The point, in layout coordinates.</param>
    /// <returns>The page.</returns>
    public ScorePage NearestPageAt(SKPoint point) => Rects().Nearest(point.X, point.Y);

    /// <summary>Returns a page's unzoomed width, honouring rotation.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The width.</returns>
    public double DefaultWidth(ScorePage page)
        => (((int)page.Rotation + (int)Rotation) & 1) != 0
            ? page.PageHeight * page.ScaleY / page.Dpi
            : page.PageWidth * page.ScaleX / page.Dpi;

    /// <summary>Returns a page's unzoomed height, honouring rotation.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The height.</returns>
    public double DefaultHeight(ScorePage page)
        => (((int)page.Rotation + (int)Rotation) & 1) != 0
            ? page.PageWidth * page.ScaleX / page.Dpi
            : page.PageHeight * page.ScaleY / page.Dpi;

    /// <summary>Returns the widest page, or null when there are none.</summary>
    /// <returns>The page.</returns>
    public ScorePage WidestPage()
    {
        ScorePage widest = null;
        foreach (ScorePage page in _pages)
        {
            if (widest == null || DefaultWidth(page) > DefaultWidth(widest)) { widest = page; }
        }

        return widest;
    }

    /// <summary>Returns the highest page, or null when there are none.</summary>
    /// <returns>The page.</returns>
    public ScorePage HighestPage()
    {
        ScorePage highest = null;
        foreach (ScorePage page in _pages)
        {
            if (highest == null || DefaultHeight(page) > DefaultHeight(highest)) { highest = page; }
        }

        return highest;
    }

    /// <summary>Sets the zoom so the layout fits a size in the given mode.</summary>
    /// <param name="size">The space available.</param>
    /// <param name="mode">How to fit.</param>
    public void Fit(SKSizeI size, ViewMode mode) => Engine.Fit(this, size, mode);

    /// <summary>
    /// Recomputes every page's size and position, then the layout's own.
    /// </summary>
    /// <returns>Whether the total geometry changed.</returns>
    public bool Update()
    {
        _rects = null;
        UpdatePageSizes();
        if (Count > 0) { Engine.UpdatePagePositions(this); }

        SKRectI geometry = ComputeGeometry();
        bool changed = geometry != Geometry;
        X = geometry.Left;
        Y = geometry.Top;
        Width = geometry.Width;
        Height = geometry.Height;
        return changed;
    }

    /// <summary>Gives every page the size the current zoom and rotation ask for.</summary>
    public void UpdatePageSizes()
    {
        foreach (ScorePage page in _pages)
        {
            page.ComputedRotation = (Rotation)(((int)page.Rotation + (int)Rotation) & 3);
            page.UpdateSize(DpiX, DpiY, ZoomFactor);
        }
    }

    /// <summary>
    /// Records a spot in the layout as a page index and a fraction of that page.
    /// </summary>
    /// <param name="point">The spot, in layout coordinates.</param>
    /// <returns>The page index (-1 for none) and the fractions.</returns>
    /// <remarks>
    /// This is how a position survives a zoom: the pixels change, the page and
    /// the fraction of it do not.
    /// </remarks>
    public (int Index, double X, double Y) PositionToOffset(SKPoint point)
    {
        ScorePage page = PageAt(point) ?? NearestPageAt(point);
        double w;
        double h;
        int index;
        if (page != null)
        {
            point = new SKPoint(point.X - page.X, point.Y - page.Y);
            w = page.Width;
            h = page.Height;
            index = IndexOf(page);
        }
        else
        {
            w = Width;
            h = Height;
            index = -1;
        }

        return (index, w > 0 ? point.X / w : 0, h > 0 ? point.Y / h : 0);
    }

    /// <summary>Turns a recorded spot back into layout coordinates.</summary>
    /// <param name="offset">The spot, as <see cref="PositionToOffset"/> returned it.</param>
    /// <returns>The point.</returns>
    public SKPointI OffsetToPosition((int Index, double X, double Y) offset)
    {
        int px;
        int py;
        double w;
        double h;
        if (offset.Index < 0 || offset.Index >= _pages.Count)
        {
            px = 0;
            py = 0;
            w = Width;
            h = Height;
        }
        else
        {
            ScorePage page = _pages[offset.Index];
            px = page.X;
            py = page.Y;
            w = page.Width;
            h = page.Height;
        }

        return new SKPointI(px + (int)Math.Round(offset.X * w), py + (int)Math.Round(offset.Y * h));
    }

    /// <summary>Gets the pages that are actually shown.</summary>
    /// <returns>The pages.</returns>
    public IReadOnlyList<ScorePage> DisplayPages()
    {
        var (start, length) = CurrentPageSetRange();
        return _pages.GetRange(start, length);
    }

    /// <summary>Gets the range of pages the current page set covers.</summary>
    /// <returns>The first index and the count.</returns>
    public (int Start, int Length) CurrentPageSetRange()
    {
        if (ContinuousMode) { return (0, Count); }

        int num = CurrentPageSet;
        int setCount = PageSetCount();
        if (num > 0 && num >= setCount) { num = CurrentPageSet = setCount - 1; }

        int p = 0;
        int s = 0;
        foreach (var (count, length) in PageSets())
        {
            if (p + count <= num)
            {
                p += count;
                s += count * length;
                continue;
            }

            s += (num - p) * length;
            return (s, Math.Min(length, Count - s));
        }

        return (0, Count);
    }

    /// <summary>Gets the page sets as (how many, how long) pairs.</summary>
    /// <returns>The page sets.</returns>
    public IReadOnlyList<(int Count, int Length)> PageSets() => Engine.PageSets(Count);

    /// <summary>Gets how many page sets there are.</summary>
    /// <returns>The count.</returns>
    public int PageSetCount() => PageSets().Sum(s => s.Count);

    /// <summary>Gets the page set a page index falls in.</summary>
    /// <param name="index">The page index.</param>
    /// <returns>The page set.</returns>
    public int PageSet(int index)
    {
        int s = 0;
        int p = 0;
        foreach (var (count, length) in PageSets())
        {
            if (s + (count * length) < index)
            {
                s += count * length;
                p += count;
                continue;
            }

            return p + ((index - s) / length);
        }

        return 0;
    }

    /// <inheritdoc/>
    public IEnumerator<ScorePage> GetEnumerator() => _pages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private SKRectI ComputeGeometry()
    {
        IReadOnlyList<ScorePage> pages = DisplayPages();
        if (pages.Count == 0)
        {
            return new SKRectI(0, 0, Margins.Horizontal + PageMargins.Horizontal,
                Margins.Vertical + PageMargins.Vertical);
        }

        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        foreach (ScorePage page in pages)
        {
            left = Math.Min(left, page.X);
            top = Math.Min(top, page.Y);
            right = Math.Max(right, page.X + page.Width);
            bottom = Math.Max(bottom, page.Y + page.Height);
        }

        return new SKRectI(
            left - Margins.Left - PageMargins.Left,
            top - Margins.Top - PageMargins.Top,
            right + Margins.Right + PageMargins.Right,
            bottom + Margins.Bottom + PageMargins.Bottom);
    }

    private PageRects Rects() => _rects ??= new PageRects(DisplayPages());

    /// <summary>The spatial index over the pages that are shown.</summary>
    private sealed class PageRects : Rectangles<ScorePage>
    {
        internal PageRects(IEnumerable<ScorePage> pages)
            : base(pages)
        {
        }

        protected override (float Left, float Top, float Right, float Bottom) GetCoords(ScorePage obj)
            => (obj.X, obj.Y, obj.X + obj.Width, obj.Y + obj.Height);
    }
}
