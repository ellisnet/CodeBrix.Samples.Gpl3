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

namespace Fresco.Brix.MusicView; //was previously: qpageview/layout.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Arranges a layout's pages. The default puts them in one row or one column.
/// </summary>
/// <remarks>
/// Where more than one row or column results, every row is as high as its
/// highest page and every column as wide as its widest, unless
/// <see cref="EvenWidths"/> or <see cref="EvenHeights"/> say otherwise.
/// </remarks>
public class LayoutEngine
{
    /// <summary>Gets whether this engine changes the zoom in order to fit.</summary>
    public virtual bool ZoomToFit => true;

    /// <summary>Gets the orientation this engine forces, or null for the layout's.</summary>
    public virtual LayoutOrientation? Orientation => null;

    /// <summary>Gets or sets whether every column has the same width.</summary>
    public bool EvenWidths { get; set; }

    /// <summary>Gets or sets whether every row has the same height.</summary>
    public bool EvenHeights { get; set; }

    /// <summary>Returns the grid this engine wants: columns, rows and unused leading cells.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The grid.</returns>
    public virtual (int Columns, int Rows, int Prepend) Grid(PageLayout layout)
        => layout.Orientation == LayoutOrientation.Vertical
            ? (1, layout.Count, 0)
            : (layout.Count, 1, 0);

    /// <summary>Walks the pages together with the grid cell each one occupies.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="columns">How many columns.</param>
    /// <param name="rows">How many rows.</param>
    /// <param name="prepend">How many leading cells to leave empty.</param>
    /// <returns>Each page and its cell.</returns>
    public IEnumerable<(ScorePage ScorePage, int Column, int Row)> Pages(
        PageLayout layout, int columns, int rows, int prepend = 0)
    {
        IEnumerable<(int Column, int Row)> cells;
        if ((Orientation ?? layout.Orientation) == LayoutOrientation.Vertical)
        {
            cells = from col in Enumerable.Range(0, columns)
                    from row in Enumerable.Range(0, rows)
                    select (col, row);
        }
        else
        {
            cells = from row in Enumerable.Range(0, rows)
                    from col in Enumerable.Range(0, columns)
                    select (col, row);
        }

        return layout.Zip(cells.Skip(prepend), (page, cell) => (page, cell.Column, cell.Row));
    }

    /// <summary>Returns the width of every column and the height of every row.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="columns">How many columns.</param>
    /// <param name="rows">How many rows.</param>
    /// <param name="prepend">How many leading cells are empty.</param>
    /// <returns>The column widths and row heights.</returns>
    public (int[] ColumnWidths, int[] RowHeights) Dimensions(
        PageLayout layout, int columns, int rows, int prepend = 0)
    {
        var colWidths = new int[Math.Max(columns, 1)];
        var rowHeights = new int[Math.Max(rows, 1)];
        foreach (var (page, col, row) in Pages(layout, columns, rows, prepend))
        {
            colWidths[col] = Math.Max(colWidths[col], page.Width);
            rowHeights[row] = Math.Max(rowHeights[row], page.Height);
        }

        if (EvenWidths && colWidths.Length > 0)
        {
            int max = colWidths.Max();
            for (int i = 0; i < colWidths.Length; i++) { colWidths[i] = max; }
        }

        if (EvenHeights && rowHeights.Length > 0)
        {
            int max = rowHeights.Max();
            for (int i = 0; i < rowHeights.Length; i++) { rowHeights[i] = max; }
        }

        return (colWidths, rowHeights);
    }

    /// <summary>Puts every page where it belongs. Not called on an empty layout.</summary>
    /// <param name="layout">The layout.</param>
    public virtual void UpdatePagePositions(PageLayout layout)
    {
        var (columns, rows, prepend) = Grid(layout);
        var (colWidths, rowHeights) = Dimensions(layout, columns, rows, prepend);

        PageMargins m = layout.Margins;
        PageMargins pm = layout.PageMargins;

        var xoff = new int[Math.Max(columns, 1)];
        var yoff = new int[Math.Max(rows, 1)];
        xoff[0] = m.Left + pm.Left;
        yoff[0] = m.Top + pm.Top;
        for (int i = 1; i < columns; i++)
        {
            xoff[i] = colWidths[i - 1] + xoff[i - 1] + layout.Spacing + pm.Horizontal;
        }

        for (int i = 1; i < rows; i++)
        {
            yoff[i] = rowHeights[i - 1] + yoff[i - 1] + layout.Spacing + pm.Vertical;
        }

        foreach (var (page, col, row) in Pages(layout, columns, rows, prepend))
        {
            var (x, y) = Align(page.Width, page.Height, colWidths[col], rowHeights[row], layout.Alignment);
            page.X = xoff[col] + x;
            page.Y = yoff[row] + y;
        }
    }

    /// <summary>Sets the layout's zoom so it fits the given size.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="size">The space available.</param>
    /// <param name="mode">How to fit.</param>
    public virtual void Fit(PageLayout layout, SKSizeI size, ViewMode mode)
    {
        if (mode == ViewMode.FixedScale || layout.Count == 0) { return; }

        var zooms = new List<double>();
        if ((mode & ViewMode.FitWidth) != 0) { zooms.Add(ZoomFitWidth(layout, size.Width)); }

        if ((mode & ViewMode.FitHeight) != 0) { zooms.Add(ZoomFitHeight(layout, size.Height)); }

        if (zooms.Count > 0) { layout.ZoomFactor = zooms.Min(); }
    }

    /// <summary>Returns the zoom at which the layout's width fills the given width.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="width">The space available.</param>
    /// <returns>The zoom.</returns>
    public virtual double ZoomFitWidth(PageLayout layout, double width)
    {
        width -= layout.Margins.Horizontal + layout.PageMargins.Horizontal;
        return layout.WidestPage().ZoomForWidth(width, layout.Rotation, layout.DpiX);
    }

    /// <summary>Returns the zoom at which the layout's height fills the given height.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="height">The space available.</param>
    /// <returns>The zoom.</returns>
    public virtual double ZoomFitHeight(PageLayout layout, double height)
    {
        height -= layout.Margins.Vertical + layout.PageMargins.Vertical;
        return layout.HighestPage().ZoomForHeight(height, layout.Rotation, layout.DpiY);
    }

    /// <summary>Returns the page sets this engine shows one at a time.</summary>
    /// <param name="count">How many pages there are.</param>
    /// <returns>The (how many, how long) pairs.</returns>
    public virtual IReadOnlyList<(int Count, int Length)> PageSets(int count)
        => count > 0 ? new[] { (count, 1) } : Array.Empty<(int, int)>();

    /// <summary>Places a page in the cell the grid gave it.</summary>
    /// <param name="width">The page's width.</param>
    /// <param name="height">The page's height.</param>
    /// <param name="cellWidth">The cell's width.</param>
    /// <param name="cellHeight">The cell's height.</param>
    /// <param name="alignment">Where in the cell it goes.</param>
    /// <returns>The offset within the cell.</returns>
    protected static (int X, int Y) Align(
        int width, int height, int cellWidth, int cellHeight, PageAlignment alignment)
    {
        int x = (alignment & PageAlignment.Right) != 0
            ? cellWidth - width
            : (alignment & PageAlignment.Left) != 0 ? 0 : (cellWidth - width) / 2;
        int y = (alignment & PageAlignment.Bottom) != 0
            ? cellHeight - height
            : (alignment & PageAlignment.Top) != 0 ? 0 : (cellHeight - height) / 2;
        return (x, y);
    }
}

/// <summary>
/// Puts the pages in rows of a fixed width — the "two pages side by side"
/// layouts, with the option of a single page in the first row so that page
/// numbers fall on the correct side of the spread.
/// </summary>
public sealed class RowLayoutEngine : LayoutEngine
{
    /// <summary>Gets or sets how many pages a row holds.</summary>
    public int PagesPerRow { get; set; } = 2;

    /// <summary>Gets or sets how many pages the FIRST row holds.</summary>
    public int PagesFirstRow { get; set; } = 1;

    /// <summary>Gets or sets whether fitting the width fits all the columns.</summary>
    public bool FitAllColumns { get; set; } = true;

    /// <inheritdoc/>
    public override LayoutOrientation? Orientation => LayoutOrientation.Horizontal;

    /// <inheritdoc/>
    public override IReadOnlyList<(int Count, int Length)> PageSets(int count)
    {
        var result = new List<(int Count, int Length)>();
        int left = count;
        if (left == 0) { return result; }

        if (PagesFirstRow > 0 && PagesFirstRow != PagesPerRow)
        {
            int length = Math.Min(left, PagesFirstRow);
            result.Add((1, length));
            left -= length;
        }

        if (left > 0)
        {
            int sets = left / PagesPerRow;
            int remainder = left % PagesPerRow;
            if (sets > 0) { result.Add((sets, PagesPerRow)); }

            if (remainder > 0) { result.Add((1, remainder)); }
        }

        return result;
    }

    /// <inheritdoc/>
    public override (int Columns, int Rows, int Prepend) Grid(PageLayout layout)
    {
        int columns = PagesPerRow;
        int prepend;
        if (layout.Count > columns)
        {
            prepend = (columns - PagesFirstRow) % columns;
        }
        else
        {
            columns = layout.Count;
            prepend = 0;
        }

        int rows = columns > 0 ? (int)Math.Ceiling((layout.Count + prepend) / (double)columns) : 0;
        return (columns, rows, prepend);
    }

    /// <inheritdoc/>
    public override double ZoomFitWidth(PageLayout layout, double width)
    {
        if (!FitAllColumns || PagesPerRow == 1 || layout.Count < 2)
        {
            return base.ZoomFitWidth(layout, width);
        }

        var (columns, rows, prepend) = Grid(layout);
        width -= layout.Margins.Horizontal + (layout.PageMargins.Horizontal * columns);
        width -= layout.Spacing * (columns - 1);
        if (EvenWidths) { return base.ZoomFitWidth(layout, width / columns); }

        var cols = new List<ScorePage>[columns];
        for (int i = 0; i < columns; i++) { cols[i] = new List<ScorePage>(); }

        foreach (var (page, col, _) in Pages(layout, columns, rows, prepend)) { cols[col].Add(page); }

        var widest = cols.Where(c => c.Count > 0)
            .Select(c => c.OrderByDescending(layout.DefaultWidth).First())
            .ToList();
        double total = widest.Sum(layout.DefaultWidth);
        return widest.Min(page => page.ZoomForWidth(
            width * layout.DefaultWidth(page) / total, layout.Rotation, layout.DpiX));
    }
}

/// <summary>
/// Fills the available space with as many pages as fit, in a grid.
/// </summary>
/// <remarks>
/// This engine does not zoom to fit; it changes the number of columns instead,
/// which is what makes the "raster" view feel like a contact sheet.
/// </remarks>
public sealed class RasterLayoutEngine : LayoutEngine
{
    private int _width;
    private int _height;
    private ViewMode _mode = ViewMode.FixedScale;

    /// <inheritdoc/>
    public override bool ZoomToFit => false;

    /// <inheritdoc/>
    public override void Fit(PageLayout layout, SKSizeI size, ViewMode mode)
    {
        _width = size.Width;
        _height = size.Height;
        _mode = mode;
    }

    /// <inheritdoc/>
    public override (int Columns, int Rows, int Prepend) Grid(PageLayout layout)
    {
        if (layout.Count == 0) { return (0, 0, 0); }

        PageMargins m = layout.Margins;
        PageMargins pm = layout.PageMargins;
        int width = _width - m.Horizontal;
        int height = _height - m.Vertical;

        int columns;
        if ((_mode & ViewMode.FitWidth) != 0 || _mode == ViewMode.FixedScale)
        {
            int pageWidth = layout.WidestPage().Width + pm.Horizontal;
            columns = pageWidth + layout.Spacing > 0
                ? (width + layout.Spacing) / (pageWidth + layout.Spacing)
                : 1;
        }
        else
        {
            int pageHeight = layout.HighestPage().Height + pm.Vertical;
            int rowsFit = pageHeight + layout.Spacing > 0
                ? (height + layout.Spacing) / (pageHeight + layout.Spacing)
                : 1;
            rowsFit = Math.Max(1, rowsFit);
            columns = (int)Math.Ceiling(layout.Count / (double)rowsFit);
        }

        columns = Math.Max(1, Math.Min(columns, layout.Count));
        int rows = (int)Math.Ceiling(layout.Count / (double)columns);
        return (columns, rows, 0);
    }

    /// <inheritdoc/>
    public override IReadOnlyList<(int Count, int Length)> PageSets(int count)
        => count > 0 ? new[] { (1, count) } : Array.Empty<(int, int)>();
}
