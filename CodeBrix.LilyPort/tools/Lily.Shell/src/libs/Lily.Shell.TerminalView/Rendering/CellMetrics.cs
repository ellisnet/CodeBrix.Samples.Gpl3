// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.TextLayout;
using System;

namespace Lily.Shell.TerminalView.Rendering;

/// <summary>
/// The fixed cell geometry of the terminal grid, measured from a reference
/// glyph in the terminal font (the family's TextView recipe: measure "x" —
/// for a monospaced font its advance IS the cell advance). Re-measure on any
/// font family/size change.
/// </summary>
public readonly struct CellMetrics
{
    internal CellMetrics(float width, float height, float baseline)
    {
        Width = width;
        Height = height;
        Baseline = baseline;
    }

    /// <summary>The cell advance (width of one column).</summary>
    public float Width { get; }

    /// <summary>The cell height (one row).</summary>
    public float Height { get; }

    /// <summary>The text baseline offset from the top of the cell.</summary>
    public float Baseline { get; }

    /// <summary>Measures the cell geometry for a font family + size.</summary>
    public static CellMetrics Measure(string fontFamily, float fontSize)
    {
        var run = new TextRunDescriptor("x", fontFamily, fontSize);
        using var layout = TextLayoutEngine.Layout([run]);
        var metrics = layout.GetLineMetrics(0);

        return new CellMetrics(
            Math.Max(1f, layout.Size.Width),
            Math.Max(1f, metrics.Height),
            Math.Max(1f, metrics.BaselineOffset));
    }
}
