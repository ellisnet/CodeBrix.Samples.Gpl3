// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Fresco.Brix.Shell;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A grid that asks for whatever room it is offered, rather than only as much
/// as its children happen to want.
/// </summary>
/// <remarks>
/// The dock's tabs hand their content the DESIRED size, so a panel whose
/// contents have no natural height — a list in a scroll viewer, a drawing
/// surface, a tabbed page — comes out a few pixels tall. The Music View hit
/// this first and answered it with the same measure; this is that answer made
/// reusable, so every panel built on it fills its tab.
/// </remarks>
public class FillGrid : Grid
{
    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        Size desired = base.MeasureOverride(availableSize);
        double width = double.IsInfinity(availableSize.Width)
            ? desired.Width
            : Math.Max(desired.Width, availableSize.Width);
        double height = double.IsInfinity(availableSize.Height)
            ? desired.Height
            : Math.Max(desired.Height, availableSize.Height);
        return new Size(width, height);
    }
}
