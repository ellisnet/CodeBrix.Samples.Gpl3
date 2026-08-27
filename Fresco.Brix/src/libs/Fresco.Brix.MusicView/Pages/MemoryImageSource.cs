// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;

namespace Fresco.Brix.MusicView; //was previously: qpageview/image.py ImageContainer

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A picture that is already in memory, shown as a page.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>ImageContainer</c>, the half of <c>image.py</c> that holds a
/// <c>QImage</c> rather than loading one. W10 ported the loading half —
/// <see cref="IPageImageSource"/> over a rasterised PDF page — and left this
/// one until something needed it. The Copy to Image dialog is that something:
/// its preview shows the very picture that would be written to the file, and
/// nothing has to be read to get it.
/// </para>
/// <para>
/// The natural size is the picture's PIXELS. A page over one of these sets its
/// <see cref="ScorePage.Dpi"/> to the resolution the picture was rendered at,
/// which is what makes the view's "natural size" the size the saved file is.
/// </para>
/// </remarks>
public sealed class MemoryImageSource : IPageImageSource
{
    private readonly SKImage _image;

    /// <summary>Creates a source over a picture.</summary>
    /// <param name="image">The picture. Not owned: the caller disposes it.</param>
    public MemoryImageSource(SKImage image)
        => _image = image ?? throw new ArgumentNullException(nameof(image));

    /// <inheritdoc/>
    public (double Width, double Height) NaturalSize => (_image.Width, _image.Height);

    /// <inheritdoc/>
    public SKImage Image(int widthPixels, int heightPixels) => _image;

    /// <inheritdoc/>
    /// <remarks>Never raised: the picture was there before the page was.</remarks>
    public event EventHandler ImageReady
    {
        add { }
        remove { }
    }
}
