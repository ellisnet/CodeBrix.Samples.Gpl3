// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;

namespace Fresco.Brix.MusicView; //was previously: qpageview/export.py ImageExporter

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Exports a region of a page — or the whole page — to a raster picture.
/// </summary>
/// <remarks>
/// Upstream's <c>ImageExporter</c>, in upstream's order: render at the
/// resolution (times the oversample), scale back down, reduce to grey, then
/// crop. The one step that is not here is upstream's dots-per-metre stamping,
/// which is done where the bytes are encoded instead.
/// </remarks>
public sealed class ImageExporter : PageExporter, IDisposable
{
    private SKImage _image;
    private ScorePage _preview;

    /// <summary>Creates an exporter over a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="rect">The region, in page coordinates; null for the whole page.</param>
    public ImageExporter(ScorePage page, SKRect? rect = null)
        : base(page, rect)
    {
    }

    /// <inheritdoc/>
    public override bool WantsVector => false;

    /// <inheritdoc/>
    public override string MimeType => "image/png";

    /// <inheritdoc/>
    public override string DefaultBaseName => "image";

    /// <inheritdoc/>
    public override string DefaultExtension => ".png";

    /// <summary>Gets the picture that would be saved, rendering it once.</summary>
    /// <returns>The picture.</returns>
    public SKImage Image()
    {
        if (_image != null) { return _image; }

        double resolution = EffectiveResolution();

        //Grey is computed from a picture drawn on a solid background: a
        //half-transparent grey pixel is not the same thing as a grey pixel, and
        //upstream avoids the question the same way.
        SKColor? paper = Grayscale ? PaperColor ?? SKColors.White : PaperColor;

        SKImage image = Page.Image(AutoCroppedRect(), resolution, resolution, paper);

        if (Oversample > 1)
        {
            SKImage scaled = AutoCropping.Downsample(image, Oversample);
            if (!ReferenceEquals(scaled, image)) { image.Dispose(); }

            image = scaled;
        }

        if (Grayscale)
        {
            SKImage grey = AutoCropping.ToGrayscale(image);
            if (!ReferenceEquals(grey, image)) { image.Dispose(); }

            image = grey;
        }

        return _image = image;
    }

    /// <inheritdoc/>
    protected override byte[] Export()
    {
        SKImage image = Image();
        if (image == null) { return null; }

        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

    /// <inheritdoc/>
    public override ScorePage PreviewPage()
    {
        SKImage image = Image();
        if (image == null) { return null; }

        //The picture is already at the export's resolution, so the page it goes
        //on counts pixels: one unit per pixel, which is what makes the preview's
        //"natural size" the size the file will be.
        double dpi = EffectiveResolution() / Math.Max(Oversample, 1);
        return _preview ??= new RasterPage(new MemoryImageSource(image)) { Dpi = dpi };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _image?.Dispose();
        _image = null;
        _preview = null;
    }
}
