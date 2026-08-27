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

namespace Fresco.Brix.MusicView; //was previously: qpageview/rubberband.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Which part of the rubberband a point touches.</summary>
[Flags]
public enum RubberBandEdge
{
    /// <summary>Nowhere near it.</summary>
    Outside = 0,

    /// <summary>Its left edge.</summary>
    Left = 1,

    /// <summary>Its top edge.</summary>
    Top = 2,

    /// <summary>Its right edge.</summary>
    Right = 4,

    /// <summary>Its bottom edge.</summary>
    Bottom = 8,

    /// <summary>Its middle — all four edges at once, as upstream spells it.</summary>
    Inside = 15,
}

/// <summary>A rubberband selection over the pages of a <see cref="MusicViewControl"/>.</summary>
/// <remarks>
/// <para>
/// Upstream's <c>rubberband.Rubberband</c>. There it is a QWidget laid over the
/// viewport with its own paint and mouse events; here it is state the view
/// DRAWS and feeds pointer events to — the same answer the scroll bars, the
/// splitter dividers and the transport slider all came to on the Skia heads
/// (board traps 2, 20, 40, 53), and the same answer this control already gives
/// its drop shadows and its highlights.
/// </para>
/// <para>
/// The selection is kept in LAYOUT coordinates, as upstream keeps it, so it
/// stays over the same music while the view is scrolled; a zoom scales it about
/// the layout position it was anchored to.
/// </para>
/// </remarks>
public sealed class RubberBand
{
    /// <summary>How close to an edge counts as being on it, in pixels.</summary>
    public const int EdgeWidth = 8;

    /// <summary>A drag smaller than this in both directions selects nothing.</summary>
    public const int MinimumDrag = 8;

    private readonly IOverlayHost _view;

    private SKRectI _selection;
    //What was last ANNOUNCED, which is not the same thing as what is selected:
    //an untracked drag moves the selection on every mouse move and says nothing
    //until it ends, so the comparison that decides whether to raise the event
    //has to be against the last thing said, not against the live rectangle.
    private SKRectI _announced;
    private bool _hasSelection;
    private bool _dragging;
    private RubberBandEdge _dragEdge;
    private SKPointI _dragPosition;
    private SKRectI _dragGeometry;
    private double _oldZoom = 1.0;

    /// <summary>Creates a rubberband over a view.</summary>
    /// <param name="view">The view.</param>
    public RubberBand(IOverlayHost view)
        => _view = view ?? throw new ArgumentNullException(nameof(view));

    /// <summary>Raised when the selection has changed.</summary>
    /// <remarks>
    /// Carries the selection in layout coordinates, empty when there is none.
    /// Raised on every change only when <see cref="TrackSelection"/> is set;
    /// otherwise once, when the drag ends — which is upstream's default and the
    /// reason a Copy to Image dialog does not re-render on every mouse move.
    /// </remarks>
    public event EventHandler<SKRectI> SelectionChanged;

    /// <summary>Gets or sets whether every change is announced, not just the last.</summary>
    public bool TrackSelection { get; set; }

    /// <summary>Gets whether anything is selected.</summary>
    public bool HasSelection => _hasSelection && _selection.Width > 0 && _selection.Height > 0;

    /// <summary>Gets whether the user is dragging the band right now.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Gets the selection, in layout coordinates.</summary>
    public SKRectI Selection => _selection;

    /// <summary>Sets the selection, in layout coordinates.</summary>
    /// <param name="rect">The rectangle; an empty one clears the selection.</param>
    public void SetSelection(SKRectI rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            ClearSelection();
            return;
        }

        _hasSelection = true;
        _oldZoom = _view.ZoomFactor;
        Announce(rect);
        _view.Invalidate();
    }

    /// <summary>Forgets the selection.</summary>
    public void ClearSelection()
    {
        _dragging = false;
        _hasSelection = false;
        Announce(SKRectI.Empty);
        _view.Invalidate();
    }

    /// <summary>Returns which part of the band a view point touches.</summary>
    /// <param name="viewPoint">The point, in the canvas's own pixels.</param>
    /// <returns>The edge, or <see cref="RubberBandEdge.Outside"/>.</returns>
    public RubberBandEdge EdgeAt(SKPointI viewPoint)
    {
        if (!HasSelection) { return RubberBandEdge.Outside; }

        SKRectI rect = ViewRect();
        if (!rect.Contains(viewPoint.X, viewPoint.Y)) { return RubberBandEdge.Outside; }

        RubberBandEdge edge = 0;
        if (viewPoint.X <= rect.Left + EdgeWidth) { edge |= RubberBandEdge.Left; }
        else if (viewPoint.X >= rect.Right - EdgeWidth) { edge |= RubberBandEdge.Right; }

        if (viewPoint.Y <= rect.Top + EdgeWidth) { edge |= RubberBandEdge.Top; }
        else if (viewPoint.Y >= rect.Bottom - EdgeWidth) { edge |= RubberBandEdge.Bottom; }

        return edge == 0 ? RubberBandEdge.Inside : edge;
    }

    /// <summary>Gets the selection where it currently sits on the canvas.</summary>
    /// <returns>The rectangle, in the canvas's own pixels.</returns>
    public SKRectI ViewRect()
    {
        SKPointI offset = _view.ViewOffset;
        return new SKRectI(
            _selection.Left - offset.X, _selection.Top - offset.Y,
            _selection.Right - offset.X, _selection.Bottom - offset.Y);
    }

    /// <summary>
    /// Returns every page the selection touches, with the part of it selected.
    /// </summary>
    /// <returns>The pages and their regions, each in that page's coordinates.</returns>
    public IEnumerable<(ScorePage Page, SKRect Rect)> SelectedPages()
    {
        if (!HasSelection) { yield break; }

        var selection = new SKRect(_selection.Left, _selection.Top, _selection.Right, _selection.Bottom);
        foreach (ScorePage page in _view.Layout.PagesAt(selection))
        {
            var geometry = new SKRect(page.X, page.Y, page.X + page.Width, page.Y + page.Height);
            SKRect part = selection;
            if (!part.IntersectsWith(geometry)) { continue; }

            part.Intersect(geometry);
            part.Offset(-page.X, -page.Y);
            if (part.Width > 0 && part.Height > 0) { yield return (page, part); }
        }
    }

    /// <summary>
    /// Returns the page with the biggest share of the selection, and that share.
    /// </summary>
    /// <returns>The page and its region; a null page when nothing is selected.</returns>
    /// <remarks>
    /// Upstream's <c>selectedPage()</c>, and it sorts on width PLUS height
    /// rather than on area — kept, because a selection reaching a sliver of the
    /// next page should not win it just by being tall.
    /// </remarks>
    public (ScorePage Page, SKRect Rect) SelectedPage()
    {
        List<(ScorePage Page, SKRect Rect)> pages = SelectedPages()
            .OrderBy(pair => pair.Rect.Width + pair.Rect.Height).ToList();
        return pages.Count == 0 ? (null, SKRect.Empty) : pages[pages.Count - 1];
    }

    /// <summary>Renders the selected part of a page to a picture.</summary>
    /// <param name="resolution">The wanted resolution; the displayed one when null.</param>
    /// <param name="paperColor">The background, or null for transparent.</param>
    /// <returns>The picture, or null when nothing is selected.</returns>
    public SKImage SelectedImage(double? resolution = null, SKColor? paperColor = null)
    {
        var (page, rect) = SelectedPage();
        if (page == null) { return null; }

        double dpi = resolution ?? page.Dpi * _view.ZoomFactor;
        return page.Image(rect, dpi, dpi, paperColor);
    }

    /// <summary>Returns every link the selection wholly contains, page by page.</summary>
    /// <returns>The pages and their links; pages with no links are skipped.</returns>
    public IEnumerable<(ScorePage Page, IReadOnlyList<Link> Links)> SelectedLinks()
    {
        foreach (var (page, rect) in SelectedPages())
        {
            List<Link> links = page.LinksIn(rect).ToList();
            if (links.Count > 0) { yield return (page, links); }
        }
    }

    /// <summary>Begins a new band at a point, dragging its bottom-right corner.</summary>
    /// <param name="viewPoint">Where the pointer went down, in canvas pixels.</param>
    public void BeginNew(SKPointI viewPoint)
    {
        SKPointI offset = _view.ViewOffset;
        var start = new SKRectI(
            viewPoint.X + offset.X, viewPoint.Y + offset.Y,
            viewPoint.X + offset.X, viewPoint.Y + offset.Y);

        _hasSelection = true;
        _selection = start;
        _oldZoom = _view.ZoomFactor;
        _dragging = true;
        _dragPosition = viewPoint;
        _dragGeometry = start;
        _dragEdge = RubberBandEdge.Right | RubberBandEdge.Bottom;
        _view.Invalidate();
    }

    /// <summary>Begins moving or resizing the band that is already there.</summary>
    /// <param name="viewPoint">Where the pointer went down, in canvas pixels.</param>
    /// <returns>True when the point was on the band and a drag started.</returns>
    public bool BeginDrag(SKPointI viewPoint)
    {
        RubberBandEdge edge = EdgeAt(viewPoint);
        if (edge == RubberBandEdge.Outside) { return false; }

        _dragging = true;
        _dragPosition = viewPoint;
        _dragEdge = edge;
        _dragGeometry = _selection;
        return true;
    }

    /// <summary>Continues a drag.</summary>
    /// <param name="viewPoint">Where the pointer is now, in canvas pixels.</param>
    public void Drag(SKPointI viewPoint)
    {
        if (!_dragging) { return; }

        var diff = new SKPointI(viewPoint.X - _dragPosition.X, viewPoint.Y - _dragPosition.Y);
        _dragPosition = viewPoint;
        DragBy(diff);
    }

    /// <summary>Moves or resizes the band by a delta, according to the edge held.</summary>
    /// <param name="diff">The movement, in pixels.</param>
    public void DragBy(SKPointI diff)
    {
        RubberBandEdge edge = _dragEdge;
        SKRectI g = _dragGeometry;
        g.Left += edge.HasFlag(RubberBandEdge.Left) ? diff.X : 0;
        g.Top += edge.HasFlag(RubberBandEdge.Top) ? diff.Y : 0;
        g.Right += edge.HasFlag(RubberBandEdge.Right) ? diff.X : 0;
        g.Bottom += edge.HasFlag(RubberBandEdge.Bottom) ? diff.Y : 0;
        _dragGeometry = g;

        SKRectI normalized = Normalized(g);
        if (normalized.Width <= 0 && normalized.Height <= 0) { return; }

        _selection = normalized;
        if (TrackSelection) { Announce(normalized); }

        _view.Invalidate();
    }

    /// <summary>Ends a drag, announcing what was selected.</summary>
    public void EndDrag()
    {
        if (!_dragging) { return; }

        _dragging = false;

        //Upstream's own threshold: a band under 8 pixels each way is a click
        //that missed, not a selection.
        if (_selection.Width < MinimumDrag && _selection.Height < MinimumDrag)
        {
            _hasSelection = false;
            Announce(SKRectI.Empty);
        }
        else
        {
            Announce(_selection);
        }

        _view.Invalidate();
    }

    /// <summary>Follows a scroll, so the band stays over the same music.</summary>
    /// <param name="diff">How far the view scrolled, in pixels.</param>
    /// <remarks>
    /// The selection is already in LAYOUT coordinates, so scrolling moves
    /// nothing — except mid-drag, where the anchor the drag is measured from
    /// has to move with the view.
    /// </remarks>
    public void ScrollBy(SKPointI diff)
    {
        if (!_dragging) { return; }

        _dragPosition = new SKPointI(_dragPosition.X - diff.X, _dragPosition.Y - diff.Y);
    }

    /// <summary>Rescales the band when the view's zoom changes.</summary>
    /// <param name="zoom">The new zoom factor.</param>
    public void ZoomChanged(double zoom)
    {
        if (!HasSelection || _oldZoom <= 0.0) { return; }

        double factor = zoom / _oldZoom;
        _oldZoom = zoom;
        var scaled = new SKRectI(
            (int)Math.Round(_selection.Left * factor),
            (int)Math.Round(_selection.Top * factor),
            (int)Math.Round(_selection.Right * factor),
            (int)Math.Round(_selection.Bottom * factor));
        Announce(scaled);
    }

    /// <summary>Draws the band. The canvas's origin is the canvas's own.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="highlight">The theme's selection colour.</param>
    /// <remarks>
    /// Upstream's paint code, contributed by Richard Cognot in 2012 and kept
    /// step for step: a translucent fill inset by two pixels, a thin outline,
    /// and then a thick outline clipped to the four corners and the middle of
    /// each side, which is what makes the handles look like handles without
    /// any handle being drawn.
    /// </remarks>
    public void Paint(SKCanvas canvas, SKColor highlight)
    {
        if (!HasSelection) { return; }

        SKRectI rect = ViewRect();
        if (rect.Width <= 0 || rect.Height <= 0) { return; }

        var bounds = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);

        using (var fill = new SKPaint
               {
                   Color = highlight.WithAlpha(50), Style = SKPaintStyle.Fill,
               })
        {
            canvas.DrawRect(
                new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Right - 2, bounds.Bottom - 2), fill);
        }

        using (var outline = new SKPaint
               {
                   Color = highlight.WithAlpha(150), Style = SKPaintStyle.Stroke, StrokeWidth = 1,
               })
        {
            canvas.DrawRect(
                new SKRect(bounds.Left, bounds.Top, bounds.Right - 1, bounds.Bottom - 1), outline);
        }

        int saved = canvas.Save();
        using (var handles = new SKPaint
               {
                   Color = highlight.WithAlpha(100), Style = SKPaintStyle.Stroke, StrokeWidth = 8,
               })
        {
            float width = bounds.Width;
            float height = bounds.Height;
            using var builder = new SKPathBuilder();
            builder.AddRect(new SKRect(bounds.Left, bounds.Top, bounds.Left + 20, bounds.Top + 20));
            builder.AddRect(new SKRect(bounds.Right - 20, bounds.Top, bounds.Right, bounds.Top + 20));
            builder.AddRect(new SKRect(bounds.Right - 20, bounds.Bottom - 20, bounds.Right, bounds.Bottom));
            builder.AddRect(new SKRect(bounds.Left, bounds.Bottom - 20, bounds.Left + 20, bounds.Bottom));
            builder.AddRect(new SKRect(
                bounds.Left, bounds.Top + (height / 2) - 10, bounds.Right, bounds.Top + (height / 2) + 10));
            builder.AddRect(new SKRect(
                bounds.Left + (width / 2) - 10, bounds.Top, bounds.Left + (width / 2) + 10, bounds.Bottom));

            using SKPath region = builder.Detach();
            canvas.ClipPath(region);
            canvas.DrawRect(bounds, handles);
        }

        canvas.RestoreToCount(saved);
    }

    private static SKRectI Normalized(SKRectI rect)
        => new SKRectI(
            Math.Min(rect.Left, rect.Right), Math.Min(rect.Top, rect.Bottom),
            Math.Max(rect.Left, rect.Right), Math.Max(rect.Top, rect.Bottom));

    private void Announce(SKRectI rect)
    {
        _selection = rect;
        _hasSelection = rect.Width > 0 && rect.Height > 0;
        if (rect == _announced) { return; }

        _announced = rect;
        SelectionChanged?.Invoke(this, rect);
    }
}
