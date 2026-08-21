// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.MusicView; //was previously: qpageview/link.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A clickable area on a page, in coordinates from 0.0 to 1.0.
/// </summary>
/// <remarks>
/// The area is stored as a fraction of the page rather than in pixels — the
/// same choice Poppler makes, and the reason a link keeps pointing at the
/// right note however the page is zoomed or turned.
/// </remarks>
public sealed class Link
{
    /// <summary>Creates a link.</summary>
    /// <param name="left">The left edge, 0.0 to 1.0.</param>
    /// <param name="top">The top edge, 0.0 to 1.0.</param>
    /// <param name="right">The right edge, 0.0 to 1.0.</param>
    /// <param name="bottom">The bottom edge, 0.0 to 1.0.</param>
    /// <param name="url">Where it points.</param>
    /// <param name="toolTip">What to say about it, if anything.</param>
    public Link(float left, float top, float right, float bottom, string url = null, string toolTip = null)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        Url = url ?? string.Empty;
        ToolTip = toolTip ?? string.Empty;
        IsExternal = Url.Contains("://", StringComparison.Ordinal);
    }

    /// <summary>Gets the left edge, 0.0 to 1.0.</summary>
    public float Left { get; }

    /// <summary>Gets the top edge, 0.0 to 1.0.</summary>
    public float Top { get; }

    /// <summary>Gets the right edge, 0.0 to 1.0.</summary>
    public float Right { get; }

    /// <summary>Gets the bottom edge, 0.0 to 1.0.</summary>
    public float Bottom { get; }

    /// <summary>Gets where the link points.</summary>
    public string Url { get; }

    /// <summary>Gets what to say about the link, if anything.</summary>
    public string ToolTip { get; }

    /// <summary>Gets whether the URL names a scheme, and so leaves the document.</summary>
    public bool IsExternal { get; }

    /// <summary>Gets the area as a rectangle in 0.0-to-1.0 coordinates.</summary>
    /// <returns>The area.</returns>
    public SKRect Rect() => new SKRect(Left, Top, Right, Bottom);
}

/// <summary>The links of one page, indexed so a point finds them quickly.</summary>
public sealed class LinkList : Rectangles<Link>
{
    /// <summary>Creates an empty list.</summary>
    public LinkList()
    {
    }

    /// <summary>Creates a list over the given links.</summary>
    /// <param name="links">The links.</param>
    public LinkList(IEnumerable<Link> links)
        : base(links)
    {
    }

    /// <inheritdoc/>
    protected override (float Left, float Top, float Right, float Bottom) GetCoords(Link obj)
        => (obj.Left, obj.Top, obj.Right, obj.Bottom);
}
