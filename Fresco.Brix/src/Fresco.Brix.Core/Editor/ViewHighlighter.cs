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
using System.Linq;
using Windows.UI;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/viewhighlighter.py and gadgets/arbitraryhighlighter.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Paints the coloured backgrounds that mark places in the text rather than
/// kinds of text: the line the caret is on, bookmarked lines, error lines,
/// search hits, the matching bracket, the music the cursor points at.
/// <para>
/// Highlights are kept in named groups, so a caller replaces its own group
/// without disturbing anybody else's. Groups draw lowest priority first, so a
/// later, more specific highlight covers a broader one.
/// </para>
/// </summary>
public sealed class ViewHighlighter : IBackgroundRenderer
{
    private readonly TextView _textView;
    private readonly Dictionary<string, HighlightGroup> _groups
        = new Dictionary<string, HighlightGroup>(StringComparer.Ordinal);

    /// <summary>Creates the highlighter and attaches it to a view.</summary>
    /// <param name="textView">The editor's text view.</param>
    public ViewHighlighter(TextView textView)
    {
        _textView = textView ?? throw new ArgumentNullException(nameof(textView));
        _textView.BackgroundRenderers.Add(this);
    }

    /// <summary>Gets the layer drawn on: under the selection.</summary>
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>
    /// Sets a named group's highlights, replacing whatever it held.
    /// </summary>
    /// <param name="name">The group name, e.g. <c>current</c> or <c>match</c>.</param>
    /// <param name="ranges">The document ranges; empty clears the group.</param>
    /// <param name="color">The background colour.</param>
    /// <param name="priority">Higher draws later, so over lower ones.</param>
    /// <param name="fullWidth">Whether the highlight spans the whole line
    /// width, as the current-line and bookmark highlights do.</param>
    /// <param name="borderColor">An outline colour, or null for none.</param>
    public void Highlight(
        string name,
        IEnumerable<(int Start, int Length)> ranges,
        Color color,
        int priority = 0,
        bool fullWidth = false,
        Color? borderColor = null)
    {
        List<(int Start, int Length)> list = ranges?.ToList()
            ?? new List<(int, int)>();
        if (list.Count == 0)
        {
            _groups.Remove(name);
        }
        else
        {
            _groups[name] = new HighlightGroup
            {
                Ranges = list,
                Color = color,
                Priority = priority,
                FullWidth = fullWidth,
                BorderColor = borderColor,
            };
        }

        _textView.InvalidateLayer(Layer);
    }

    /// <summary>Clears a named group.</summary>
    /// <param name="name">The group name.</param>
    public void Clear(string name)
    {
        if (_groups.Remove(name))
        {
            _textView.InvalidateLayer(Layer);
        }
    }

    /// <summary>Clears every group.</summary>
    public void ClearAll()
    {
        if (_groups.Count == 0) { return; }

        _groups.Clear();
        _textView.InvalidateLayer(Layer);
    }

    /// <summary>Gets whether a group currently holds anything.</summary>
    /// <param name="name">The group name.</param>
    /// <returns>Whether it does.</returns>
    public bool Has(string name) => _groups.ContainsKey(name);

    /// <inheritdoc/>
    public void Draw(TextView textView, SKCanvas canvas)
    {
        foreach (var group in _groups.Values.OrderBy(g => g.Priority))
        {
            using SKPaint fill = new SKPaint
            {
                Color = ToSkia(group.Color),
                Style = SKPaintStyle.Fill,
            };
            using SKPaint border = group.BorderColor == null
                ? null
                : new SKPaint
                {
                    Color = ToSkia(group.BorderColor.Value),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                };

            foreach (var (start, length) in group.Ranges)
            {
                ISegment segment = new TextSegment
                {
                    StartOffset = start,
                    Length = length,
                };

                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(
                    textView, segment))
                {
                    SKRect box = new SKRect(
                        (float)rect.Left,
                        (float)rect.Top,
                        group.FullWidth
                            ? (float)Math.Max(rect.Right, textView.ActualWidth)
                            : (float)rect.Right,
                        (float)rect.Bottom);
                    canvas.DrawRect(box, fill);
                    if (border != null)
                    {
                        canvas.DrawRect(box, border);
                    }
                }
            }
        }
    }

    private static SKColor ToSkia(Color color)
        => new SKColor(color.R, color.G, color.B, color.A);

    private sealed class HighlightGroup
    {
        public List<(int Start, int Length)> Ranges { get; set; }

        public Color Color { get; set; }

        public int Priority { get; set; }

        public bool FullWidth { get; set; }

        public Color? BorderColor { get; set; }
    }
}

/// <summary>
/// The names the <see cref="ViewHighlighter"/> groups are kept under, and the
/// order they draw in.
/// </summary>
/// <remarks>Upstream keys the same groups by the base-colour name they use, so
/// these match <see cref="TextFormatData.BaseColorNames"/>.</remarks>
public static class HighlightGroups
{
    /// <summary>The line the caret is on. Drawn under everything.</summary>
    public const string CurrentLine = "current";

    /// <summary>Bookmarked lines.</summary>
    public const string Mark = "mark";

    /// <summary>Lines an engrave run reported an error on.</summary>
    public const string Error = "error";

    /// <summary>Search hits.</summary>
    public const string Search = "search";

    /// <summary>The bracket pair the caret sits on.</summary>
    public const string Match = "match";

    /// <summary>The source of the music object the cursor points at.</summary>
    public const string MusicHighlight = "musichighlight";

    /// <summary>The drawing order, lowest first.</summary>
    public static int PriorityOf(string name)
        => name switch
        {
            CurrentLine => 0,
            Mark => 10,
            Error => 20,
            Search => 30,
            MusicHighlight => 40,
            Match => 50,
            _ => 5,
        };
}
