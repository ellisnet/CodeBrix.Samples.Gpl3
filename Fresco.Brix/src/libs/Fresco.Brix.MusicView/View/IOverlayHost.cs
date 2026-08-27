// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;

namespace Fresco.Brix.MusicView;

/// <summary>
/// The little a drawn overlay needs to know about the view it is drawn on.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's rubberband and magnifier are QWidgets and reach their view by
/// walking <c>self.parent().parent()</c> — which is why nothing in either one
/// can be exercised without a window. Here they are drawn rather than placed,
/// so what they actually need is four things, and naming those four as an
/// interface is what lets the selection arithmetic be TESTED: where the view is
/// scrolled to, how far it is zoomed, where the pages are, and how to ask for a
/// repaint.
/// </para>
/// <para>
/// <see cref="MusicViewControl"/> is the real implementation and the only one
/// the application uses.
/// </para>
/// </remarks>
public interface IOverlayHost
{
    /// <summary>Gets where the view is scrolled to, in layout coordinates.</summary>
    SKPointI ViewOffset { get; }

    /// <summary>Gets how far the view is zoomed.</summary>
    double ZoomFactor { get; }

    /// <summary>Gets where the pages are.</summary>
    PageLayout Layout { get; }

    /// <summary>Gets the colour to paint paper that a page does not colour itself.</summary>
    SKColor PaperColor { get; }

    /// <summary>Asks for a repaint.</summary>
    void Invalidate();
}
