// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.MusicView; //was previously: qpageview/util.py autoCropRect()

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Finding the ink in a picture, so an export can be trimmed to it.
/// </summary>
public static class AutoCropping
{
    /// <summary>
    /// Returns the smallest rectangle holding everything that is not the
    /// background, or null when the whole picture is one colour.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <returns>The rectangle, in the picture's own pixels.</returns>
    /// <remarks>
    /// <para>
    /// Upstream's <c>util.autoCropRect</c>, decision for decision: the
    /// background is the colour that holds the MOST of the four corners — not
    /// the top-left corner, so a page with one dark corner still crops — and
    /// everything else is ink. Upstream then hands the masking to Qt
    /// (<c>createMaskFromColor</c> into a <c>QRegion</c>'s bounding rect);
    /// there is no such call in Skia, so the pixels are walked, which for the
    /// sizes involved is a few milliseconds and needs no mask bitmap at all.
    /// </para>
    /// <para>
    /// The comparison is EXACT, as upstream's is. Antialiasing means a scanned
    /// or resampled margin will not crop, and that is upstream's behaviour too.
    /// </para>
    /// </remarks>
    public static SKRectI? InkRect(SKImage image)
    {
        if (image == null) { return null; }

        using SKBitmap bitmap = SKBitmap.FromImage(image);
        return bitmap == null ? null : InkRect(bitmap);
    }

    /// <summary>
    /// Returns the smallest rectangle holding everything that is not the
    /// background, or null when the whole picture is one colour.
    /// </summary>
    /// <param name="bitmap">The picture.</param>
    /// <returns>The rectangle, in the picture's own pixels.</returns>
    public static SKRectI? InkRect(SKBitmap bitmap)
    {
        if (bitmap == null) { return null; }

        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width <= 0 || height <= 0) { return null; }

        SKColor background = MostCommonCorner(bitmap, width, height);

        int left = width;
        int top = height;
        int right = -1;
        int bottom = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (bitmap.GetPixel(x, y) == background) { continue; }

                if (x < left) { left = x; }

                if (x > right) { right = x; }

                if (y < top) { top = y; }

                bottom = y;
            }
        }

        return right < 0 ? null : new SKRectI(left, top, right + 1, bottom + 1);
    }

    private static SKColor MostCommonCorner(SKBitmap bitmap, int width, int height)
    {
        var counts = new Dictionary<SKColor, int>();
        foreach ((int x, int y) in new[]
                 {
                     (0, 0), (width - 1, 0), (width - 1, height - 1), (0, height - 1),
                 })
        {
            SKColor color = bitmap.GetPixel(x, y);
            counts[color] = counts.TryGetValue(color, out int count) ? count + 1 : 1;
        }

        SKColor most = default;
        int best = -1;
        foreach (KeyValuePair<SKColor, int> pair in counts)
        {
            if (pair.Value <= best) { continue; }

            best = pair.Value;
            most = pair.Key;
        }

        return most;
    }

    /// <summary>Reduces a picture to grey, keeping its transparency.</summary>
    /// <param name="image">The picture.</param>
    /// <returns>The grey picture.</returns>
    /// <remarks>
    /// Upstream converts to <c>Format_Grayscale8</c> and then, when there is no
    /// paper colour, rebuilds the alpha channel out of the image's own negative
    /// "to save memory". Skia has a colour filter for the first half and keeps
    /// the alpha channel through it, so the second half is unnecessary — and the
    /// result is the one upstream was reconstructing rather than a reading of it.
    /// The weights are Qt's own (the Rec. 601 luma coefficients).
    /// </remarks>
    public static SKImage ToGrayscale(SKImage image)
    {
        if (image == null) { return null; }

        float[] matrix =
        {
            0.299f, 0.587f, 0.114f, 0f, 0f,
            0.299f, 0.587f, 0.114f, 0f, 0f,
            0.299f, 0.587f, 0.114f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f,
        };

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(matrix) };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(image, 0f, 0f, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        return surface.Snapshot();
    }

    /// <summary>Scales a picture down by a whole factor, smoothly.</summary>
    /// <param name="image">The picture.</param>
    /// <param name="factor">How many times over it was rendered.</param>
    /// <returns>The scaled picture, or the original when the factor is one.</returns>
    public static SKImage Downsample(SKImage image, int factor)
    {
        if (image == null || factor <= 1) { return image; }

        int width = Math.Max(1, image.Width / factor);
        int height = Math.Max(1, image.Height / factor);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(
            image, new SKRect(0f, 0f, width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        return surface.Snapshot();
    }
}
