// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;

namespace Fresco.Brix.MusicView; //was previously: qpageview/constants.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>How far a page is turned before it is displayed.</summary>
public enum Rotation
{
    /// <summary>Not rotated.</summary>
    Rotate0 = 0,

    /// <summary>90° clockwise.</summary>
    Rotate90 = 1,

    /// <summary>180°.</summary>
    Rotate180 = 2,

    /// <summary>270° clockwise (90° counter-clockwise).</summary>
    Rotate270 = 3,
}

/// <summary>
/// How the zoom follows the size of the view.
/// </summary>
/// <remarks>
/// The values are upstream's, and <see cref="FitBoth"/> really is the bitwise
/// union of the other two — the layout takes the smaller of the two zooms it
/// computes, which is what fitting a whole page means.
/// </remarks>
[Flags]
public enum ViewMode
{
    /// <summary>The zoom is whatever the user set; resizing does not change it.</summary>
    FixedScale = 0,

    /// <summary>The page's width fills the view.</summary>
    FitWidth = 1,

    /// <summary>The page's height fills the view.</summary>
    FitHeight = 2,

    /// <summary>The whole page fits in the view.</summary>
    FitBoth = FitWidth | FitHeight,
}

/// <summary>Which way a row of pages runs.</summary>
public enum LayoutOrientation
{
    /// <summary>Pages side by side.</summary>
    Horizontal = 1,

    /// <summary>Pages one below the other.</summary>
    Vertical = 2,
}

/// <summary>Where a page sits in the space a layout gives it.</summary>
[Flags]
public enum PageAlignment
{
    /// <summary>Against the left edge.</summary>
    Left = 1,

    /// <summary>Against the right edge.</summary>
    Right = 2,

    /// <summary>Centred horizontally.</summary>
    HorizontalCenter = 4,

    /// <summary>Against the top edge.</summary>
    Top = 8,

    /// <summary>Against the bottom edge.</summary>
    Bottom = 16,

    /// <summary>Centred vertically.</summary>
    VerticalCenter = 32,

    /// <summary>Centred both ways — the default.</summary>
    Center = HorizontalCenter | VerticalCenter,
}
