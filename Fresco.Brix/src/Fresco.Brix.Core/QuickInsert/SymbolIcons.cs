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
/// THE application's one SVG renderer: an embedded SVG in, a recoloured
/// bitmap out.
/// </summary>
/// <remarks>
/// <para>
/// It began as the music glyphs the Quick Insert buttons show, engraved by
/// <c>tools/symbolicons</c> from Frescobaldi's symbol sources; it now also
/// draws the two window toolbars' buttons from Frescobaldi's Light and Dark
/// icon sets (<see cref="Services.IconTheme"/>), which is why the resource
/// prefix is an argument rather than a constant. Board rule 6b is the reason
/// there is one of these and not two: a second renderer would be a second Skia
/// call site, and the family is trying to leave Skia, not spread it.
/// </para>
/// <para>
/// Upstream renders its SVGs and then recolours the result to the theme's text
/// colour, so an icon follows a dark or light theme. Both kinds of file here
/// are monochrome line art — the engine writes <c>fill="currentColor"</c>, and
/// the Tabler-derived icons carry <c>stroke="currentColor"</c> or a literal
/// black (Light) or white (Dark) — so the recolour is one colour filter over
/// the whole picture rather than a composite pass.
/// </para>
/// <para>
/// Nothing at runtime reaches an engine or a file: every SVG is an
/// <c>EmbeddedResource</c> of this assembly.
/// </para>
/// </remarks>
public static class SymbolIcons
{
    /// <summary>
    /// The resource prefix the Quick Insert panel's own glyphs are embedded
    /// under.
    /// </summary>
    public const string SymbolPrefix = "Fresco.Brix.Symbols.";

    private const int DefaultSize = 22;

    private static readonly ConcurrentDictionary<
        (string Prefix, string Name, int Size, uint Color), WriteableBitmap> Cache
        = new ConcurrentDictionary<(string, string, int, uint), WriteableBitmap>();

    /// <summary>Answers whether a symbol exists.</summary>
    /// <param name="name">The symbol name, without its extension.</param>
    /// <returns>Whether it does.</returns>
    public static bool Has(string name) => Has(SymbolPrefix, name);

    /// <summary>Answers whether an embedded SVG exists.</summary>
    /// <param name="resourcePrefix">The prefix its resource name starts with.</param>
    /// <param name="name">The file name, without its extension.</param>
    /// <returns>Whether it does.</returns>
    /// <remarks>
    /// //was previously: the prefix was a private constant and this class could
    /// only ever see the Quick Insert glyphs. The two window toolbars draw
    /// Frescobaldi's Light and Dark icon sets, which are embedded the same way
    /// under their own prefixes, and board rule 6b says a second Skia call site
    /// is not the answer — so the ONE renderer takes the prefix as an argument.
    /// </remarks>
    public static bool Has(string resourcePrefix, string name)
        => name != null && resourcePrefix != null
            && typeof(SymbolIcons).Assembly.GetManifestResourceInfo(
                resourcePrefix + name + ".svg") != null;

    /// <summary>Makes an image showing a symbol.</summary>
    /// <param name="name">The symbol name.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="size">The size in pixels.</param>
    /// <returns>The image, or null when there is no such symbol.</returns>
    public static Image Icon(string name, Color color, int size = DefaultSize)
        => Icon(SymbolPrefix, name, color, size);

    /// <summary>Makes an image showing an embedded SVG.</summary>
    /// <param name="resourcePrefix">The prefix its resource name starts with.</param>
    /// <param name="name">The file name, without its extension.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="size">The size in pixels.</param>
    /// <returns>The image, or null when there is no such file.</returns>
    public static Image Icon(
        string resourcePrefix, string name, Color color, int size = DefaultSize)
    {
        WriteableBitmap bitmap = Bitmap(resourcePrefix, name, color, size);
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
        => Bitmap(SymbolPrefix, name, color, size);

    /// <summary>Renders an embedded SVG into a bitmap.</summary>
    /// <param name="resourcePrefix">The prefix its resource name starts with.</param>
    /// <param name="name">The file name, without its extension.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="size">The size in pixels.</param>
    /// <returns>The bitmap, or null.</returns>
    public static WriteableBitmap Bitmap(
        string resourcePrefix, string name, Color color, int size = DefaultSize)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(resourcePrefix)
            || size <= 0)
        {
            return null;
        }

        uint key = ((uint)color.A << 24) | ((uint)color.R << 16)
            | ((uint)color.G << 8) | color.B;
        return Cache.GetOrAdd(
            (resourcePrefix, name, size, key),
            _ => Render(resourcePrefix, name, color, size));
    }

    /// <summary>Renders an embedded SVG into raw BGRA pixels.</summary>
    /// <param name="resourcePrefix">The prefix its resource name starts with.</param>
    /// <param name="name">The file name, without its extension.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="size">The square size in pixels.</param>
    /// <returns>The pixels, or null.</returns>
    internal static byte[] PixelsOfResource(
        string resourcePrefix, string name, Color color, int size)
    {
        using Stream stream = typeof(SymbolIcons).Assembly
            .GetManifestResourceStream(resourcePrefix + name + ".svg");
        return stream == null ? null : PixelsOfStream(stream, color, size);
    }

    private static WriteableBitmap Render(
        string resourcePrefix, string name, Color color, int size)
    {
        byte[] buffer = PixelsOfResource(resourcePrefix, name, color, size);
        if (buffer == null) { return null; }

        WriteableBitmap bitmap = new WriteableBitmap(size, size);
        using (Stream target = WindowsRuntimeBufferExtensions.AsStream(
            bitmap.PixelBuffer))
        {
            target.Write(buffer, 0, buffer.Length);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>Renders an SVG stream into raw BGRA pixels.</summary>
    /// <remarks>
    /// Its second caller is the test that proves ruling FR14's cleaned
    /// <c>tools-score-wizard.svg</c> draws exactly what upstream's
    /// 6.4-megabyte original draws: it renders the recorded original and the
    /// shipped file through this renderer — the one the buttons use — and
    /// compares every pixel.
    /// </remarks>
    /// <param name="stream">The SVG.</param>
    /// <param name="color">The colour to draw it in.</param>
    /// <param name="size">The square size in pixels.</param>
    /// <returns>The pixels, or null when it does not draw.</returns>
    internal static byte[] PixelsOfStream(Stream stream, Color color, int size)
    {
        if (stream == null || size <= 0) { return null; }

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

        //Fit the drawing into the square and centre it, the way a small
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
        byte[] buffer = new byte[info.BytesSize];
        System.Runtime.InteropServices.Marshal.Copy(
            pixels.GetPixels(), buffer, 0, buffer.Length);
        return buffer;
    }
}
