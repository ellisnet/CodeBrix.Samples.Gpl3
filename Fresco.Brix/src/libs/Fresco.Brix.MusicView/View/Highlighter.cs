// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System.Collections.Generic;

namespace Fresco.Brix.MusicView; //was previously: qpageview/highlight.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One style of highlighting rectangles on a page — a rounded border drawn a
/// little outside the area itself.
/// </summary>
/// <remarks>
/// An instance IS the style, and the view keys its highlight sets by it: the
/// link the mouse is over and the objects the text cursor points at are two
/// highlighters, shown and cleared independently.
/// </remarks>
public class Highlighter
{
    /// <summary>Gets or sets the border's thickness, in pixels.</summary>
    public float LineWidth { get; set; } = 2f;

    /// <summary>Gets or sets how far outside the area the border is drawn.</summary>
    public float Radius { get; set; } = 3f;

    /// <summary>Gets or sets the border's colour.</summary>
    public SKColor Color { get; set; } = new SKColor(0x33, 0x99, 0xFF);

    /// <summary>Draws the highlighting for a set of rectangles.</summary>
    /// <param name="canvas">The canvas, in layout coordinates.</param>
    /// <param name="rects">The rectangles to highlight.</param>
    public virtual void PaintRects(SKCanvas canvas, IEnumerable<SKRect> rects)
    {
        using var paint = new SKPaint
        {
            Color = Color,
            StrokeWidth = LineWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        foreach (SKRect rect in rects)
        {
            SKRect r = new SKRect(
                rect.Left - Radius, rect.Top - Radius, rect.Right + Radius, rect.Bottom + Radius);
            canvas.DrawRoundRect(r, Radius, Radius, paint);
        }
    }
}
