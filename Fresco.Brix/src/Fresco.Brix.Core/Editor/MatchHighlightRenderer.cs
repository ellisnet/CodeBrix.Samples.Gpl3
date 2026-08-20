// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using CodeBrix.Platform.UI.AdvancedTextEdit.Rendering;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.Editor;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Draws the matching-token highlight boxes the <see cref="TokenMatcher"/>
/// finds — Frescobaldi highlights the pair the caret sits on; this renderer is
/// the editor-side half, fed by the page on caret movement.
/// </summary>
public sealed class MatchHighlightRenderer : IBackgroundRenderer
{
    private static readonly SKColor MatchFill = new SKColor(0x99, 0xdd, 0x77, 0x60);
    private static readonly SKColor MatchBorder = new SKColor(0x44, 0x88, 0x22, 0xa0);

    private readonly TextView _textView;
    private List<(int Start, int Length)> _ranges = new List<(int, int)>();

    /// <summary>Initializes the renderer and registers it with the view.</summary>
    /// <param name="textView">The editor's text view.</param>
    public MatchHighlightRenderer(TextView textView)
    {
        _textView = textView ?? throw new ArgumentNullException(nameof(textView));
        _textView.BackgroundRenderers.Add(this);
    }

    /// <summary>Gets the layer: below the selection, above the text
    /// background.</summary>
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Sets the ranges to highlight (empty clears) and repaints.</summary>
    /// <param name="ranges">The (start, length) document ranges.</param>
    public void SetRanges(List<(int Start, int Length)> ranges)
    {
        _ranges = ranges ?? new List<(int, int)>();
        _textView.InvalidateLayer(Layer);
    }

    /// <summary>Draws the highlight boxes.</summary>
    /// <param name="textView">The view being painted.</param>
    /// <param name="canvas">The paint pass's canvas.</param>
    public void Draw(TextView textView, SKCanvas canvas)
    {
        if (_ranges.Count == 0)
        {
            return;
        }

        using SKPaint fill = new SKPaint { Color = MatchFill, Style = SKPaintStyle.Fill };
        using SKPaint border = new SKPaint
        {
            Color = MatchBorder,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
        };

        foreach ((int start, int length) in _ranges)
        {
            //TextSegment (SimpleSegment is internal to the add-in)
            ISegment segment = new TextSegment { StartOffset = start, Length = length };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(
                textView, segment))
            {
                SKRect box = new SKRect(
                    (float)rect.Left, (float)rect.Top,
                    (float)rect.Right, (float)rect.Bottom);
                canvas.DrawRect(box, fill);
                canvas.DrawRect(box, border);
            }
        }
    }
}
