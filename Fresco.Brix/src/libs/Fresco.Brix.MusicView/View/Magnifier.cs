// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;
using System.Linq;

namespace Fresco.Brix.MusicView; //was previously: qpageview/magnifier.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A magnifying glass over the pages of a <see cref="MusicViewControl"/>.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>magnifier.Magnifier</c>: a round window that follows the
/// pointer while a modifier is held and draws the pages under it at a larger
/// scale. There it is a QWidget with a circular mask; here it is state the view
/// draws inside a circular clip, for the reason every other overlay in this
/// control is drawn rather than placed.
/// </para>
/// <para>
/// ⚠ What upstream's LONG drag is for does not arise here. There the glass can
/// be shown programmatically and then picked up and carried; the only way it is
/// ever shown in Fresco.Brix is by holding the modifier, so it lives exactly as
/// long as the button is down — upstream's DRAG_SHORT, and nothing else.
/// </para>
/// </remarks>
public sealed class Magnifier
{
    /// <summary>The smallest the glass may be made, in pixels.</summary>
    public const int MinimumSize = 50;

    /// <summary>The largest the glass may be made, in pixels.</summary>
    public const int MaximumSize = 640;

    /// <summary>How far past the view's own maximum the glass may zoom.</summary>
    public const double MaxExtraZoom = 1.25;

    private readonly IOverlayHost _view;

    private SKPointI _center;
    private int _size = 350;
    private double _scale = 3.0;
    private bool _visible;
    private SKPointI? _resizeFrom;
    private int _resizeWidth;

    /// <summary>Creates a magnifier over a view.</summary>
    /// <param name="view">The view.</param>
    public Magnifier(IOverlayHost view)
        => _view = view ?? throw new ArgumentNullException(nameof(view));

    /// <summary>Gets whether the glass is being shown.</summary>
    public bool IsVisible => _visible;

    /// <summary>Gets or sets how wide the glass is, in pixels.</summary>
    public int Size
    {
        get => _size;
        set
        {
            _size = Math.Min(Math.Max(value, MinimumSize), MaximumSize);
            _view.Invalidate();
        }
    }

    /// <summary>Gets or sets how much larger the glass draws, relative to the view.</summary>
    public double Scale
    {
        get => _scale;
        set
        {
            _scale = value;
            _view.Invalidate();
        }
    }

    /// <summary>Gets where the glass is centred, in canvas pixels.</summary>
    public SKPointI Center => _center;

    /// <summary>Shows the glass, centred on a point.</summary>
    /// <param name="viewPoint">The point, in canvas pixels.</param>
    public void Show(SKPointI viewPoint)
    {
        _visible = true;
        _center = viewPoint;
        _resizeFrom = null;
        _view.Invalidate();
    }

    /// <summary>Moves the glass.</summary>
    /// <param name="viewPoint">The new centre, in canvas pixels.</param>
    public void MoveCenter(SKPointI viewPoint)
    {
        if (!_visible) { return; }

        _center = viewPoint;
        _view.Invalidate();
    }

    /// <summary>
    /// Resizes the glass by dragging, keeping its centre where it is.
    /// </summary>
    /// <param name="viewPoint">Where the pointer is, in canvas pixels.</param>
    /// <remarks>
    /// Upstream's second-button drag, arithmetic included: the width follows
    /// twice the VERTICAL movement since the resize began, so the glass grows
    /// as the pointer is pulled down.
    /// </remarks>
    public void Resize(SKPointI viewPoint)
    {
        if (!_visible) { return; }

        if (_resizeFrom == null)
        {
            _resizeFrom = viewPoint;
            _resizeWidth = _size;
            return;
        }

        int dy = viewPoint.Y - _resizeFrom.Value.Y;
        Size = _resizeWidth + (2 * dy);
    }

    /// <summary>Ends a resize, so the next one measures afresh.</summary>
    public void EndResize() => _resizeFrom = null;

    /// <summary>Hides the glass.</summary>
    public void Hide()
    {
        if (!_visible) { return; }

        _visible = false;
        _resizeFrom = null;
        _view.Invalidate();
    }

    /// <summary>Zooms the glass by a wheel notch.</summary>
    /// <param name="notches">How many notches, positive to zoom in.</param>
    public void ZoomBy(double notches) => Scale = ClampScale(_scale * Math.Pow(1.1, notches));

    /// <summary>Resizes the glass by a wheel notch.</summary>
    /// <param name="notches">How many notches, positive to grow.</param>
    public void ResizeBy(double notches)
        => Size = (int)Math.Round(_size * Math.Pow(1.1, notches));

    /// <summary>Draws the glass. The canvas's origin is the canvas's own.</summary>
    /// <param name="canvas">The canvas.</param>
    public void Paint(SKCanvas canvas)
    {
        if (!_visible) { return; }

        double scale = ClampScale(_scale);
        int half = _size / 2;
        var bounds = new SKRect(_center.X - half, _center.Y - half, _center.X + half, _center.Y + half);

        //Where the glass's centre sits on the layout, and therefore what to
        //show: the region the glass covers, shrunk back to layout scale.
        SKPointI offset = _view.ViewOffset;
        var layoutCenter = new SKPoint(_center.X + offset.X, _center.Y + offset.Y);
        float visibleHalfWidth = (float)(half / scale);
        var region = new SKRect(
            layoutCenter.X - visibleHalfWidth, layoutCenter.Y - visibleHalfWidth,
            layoutCenter.X + visibleHalfWidth, layoutCenter.Y + visibleHalfWidth);

        int saved = canvas.Save();
        using (var builder = new SKPathBuilder())
        {
            builder.AddOval(bounds);
            using SKPath clip = builder.Detach();
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        }

        using (var backing = new SKPaint { Color = new SKColor(0x50, 0x50, 0x50) })
        {
            canvas.DrawRect(bounds, backing);
        }

        //Everything under the glass is drawn at `scale` about the glass's own
        //centre, which is the one transform that makes the magnified music line
        //up with the music around it.
        canvas.Translate(_center.X, _center.Y);
        canvas.Scale((float)scale);
        canvas.Translate(-layoutCenter.X, -layoutCenter.Y);

        foreach (ScorePage page in _view.Layout.PagesAt(region).OrderBy(_view.Layout.IndexOf))
        {
            int pageSaved = canvas.Save();
            canvas.Translate(page.X, page.Y);
            canvas.ClipRect(new SKRect(0f, 0f, page.Width, page.Height));
            using (var paper = new SKPaint { Color = page.PaperColor ?? _view.PaperColor })
            {
                canvas.DrawRect(new SKRect(0f, 0f, page.Width, page.Height), paper);
            }

            page.Paint(canvas, new SKRect(0f, 0f, page.Width, page.Height));
            canvas.RestoreToCount(pageSaved);
        }

        canvas.RestoreToCount(saved);
        DrawBorder(canvas, bounds);
    }

    /// <summary>Draws the glass's rim.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="bounds">Where the glass is.</param>
    private static void DrawBorder(SKCanvas canvas, SKRect bounds)
    {
        using var pen = new SKPaint
        {
            Color = new SKColor(192, 192, 192, 128),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 6,
            IsAntialias = true,
        };
        canvas.DrawOval(
            new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Right - 2, bounds.Bottom - 2), pen);
    }

    /// <summary>
    /// Keeps the glass's scale inside what the view itself would allow.
    /// </summary>
    /// <param name="scale">The wanted scale.</param>
    /// <returns>The scale to use.</returns>
    private double ClampScale(double scale)
    {
        double zoom = Math.Max(_view.ZoomFactor, 0.0001);
        double most = MusicViewControl.MaxZoom * MaxExtraZoom / zoom;
        double least = MusicViewControl.MinZoom / zoom;
        return Math.Max(Math.Min(scale, most), least);
    }
}
