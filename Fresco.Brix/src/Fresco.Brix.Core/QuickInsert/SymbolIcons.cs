// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.SkiaSvg;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI;

namespace Fresco.Brix.QuickInsert; //was previously: frescobaldi/symbols/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The music glyphs the Quick Insert buttons show, drawn from SVGs that
/// LilyPort engraved.
/// </summary>
/// <remarks>
/// <para>
/// Upstream renders its SVGs and then recolours the result to the theme's text
/// colour, so a symbol follows a dark or light theme. The SVGs here carry
/// <c>fill="currentColor"</c> — the engine writes them that way — so the
/// recolouring is a fill colour rather than a composite pass.
/// </para>
/// <para>
/// The icons are engraved by <c>tools/symbolicons</c> at development time and
/// embedded in this assembly; nothing at runtime reaches an engine or a file.
/// </para>
/// </remarks>
public static class SymbolIcons
{
    private const string ResourcePrefix = "Fresco.Brix.Symbols.";
    private const int DefaultSize = 22;

    private static readonly ConcurrentDictionary<(string Name, int Size, uint Color),
        WriteableBitmap> Cache
        = new ConcurrentDictionary<(string, int, uint), WriteableBitmap>();

    /// <summary>Answers whether a symbol exists.</summary>
    /// <param name="name">The symbol name, without its extension.</param>
    /// <returns>Whether it does.</returns>
    public static bool Has(string name)
        => typeof(SymbolIcons).Assembly
            .GetManifestResourceInfo(ResourcePrefix + name + ".svg") != null;

    /// <summary>Makes an image showing a symbol.</summary>
    /// <param name="name">The symbol name.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="size">The size in pixels.</param>
    /// <returns>The image, or null when there is no such symbol.</returns>
    public static Image Icon(string name, Color color, int size = DefaultSize)
    {
        WriteableBitmap bitmap = Bitmap(name, color, size);
        return bitmap == null
            ? null
            : new Image { Source = bitmap, Width = size, Height = size };
    }

    /// <summary>Renders a symbol into a bitmap.</summary>
    /// <param name="name">The symbol name.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="size">The size in pixels.</param>
    /// <returns>The bitmap, or null.</returns>
    public static WriteableBitmap Bitmap(string name, Color color, int size = DefaultSize)
    {
        if (string.IsNullOrEmpty(name) || size <= 0) { return null; }

        uint key = ((uint)color.A << 24) | ((uint)color.R << 16)
            | ((uint)color.G << 8) | color.B;
        return Cache.GetOrAdd((name, size, key), _ => Render(name, color, size));
    }

    private static WriteableBitmap Render(string name, Color color, int size)
    {
        using Stream stream = typeof(SymbolIcons).Assembly
            .GetManifestResourceStream(ResourcePrefix + name + ".svg");
        if (stream == null) { return null; }

        SKSvg svg = new SKSvg();
        svg.Load(stream);
        SKPicture picture = svg.Picture;
        if (picture == null) { return null; }

        SKRect cull = picture.CullRect;
        if (cull.Width <= 0 || cull.Height <= 0) { return null; }

        SKImageInfo info = new SKImageInfo(
            size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        //Fit the glyph into the square and centre it, the way a 22-pixel
        //toolbar button wants it.
        float scale = Math.Min(size / cull.Width, size / cull.Height);
        canvas.Translate(
            (size - (cull.Width * scale)) / 2f,
            (size - (cull.Height * scale)) / 2f);
        canvas.Scale(scale);
        canvas.Translate(-cull.Left, -cull.Top);

        //`currentColor' in the engine's output resolves to whatever paint the
        //canvas carries, so the recolour is a colour filter over the picture.
        using SKPaint paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateBlendMode(
                new SKColor(color.R, color.G, color.B, color.A),
                SKBlendMode.SrcIn),
        };
        canvas.DrawPicture(picture, paint);
        canvas.Flush();

        using SKImage image = surface.Snapshot();
        using SKPixmap pixels = image.PeekPixels();
        WriteableBitmap bitmap = new WriteableBitmap(size, size);
        byte[] buffer = new byte[info.BytesSize];
        System.Runtime.InteropServices.Marshal.Copy(
            pixels.GetPixels(), buffer, 0, buffer.Length);
        using (Stream target = WindowsRuntimeBufferExtensions.AsStream(
            bitmap.PixelBuffer))
        {
            target.Write(buffer, 0, buffer.Length);
        }

        bitmap.Invalidate();
        return bitmap;
    }
}
