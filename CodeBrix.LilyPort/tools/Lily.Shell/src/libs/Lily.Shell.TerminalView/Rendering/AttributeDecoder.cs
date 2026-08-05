// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Terminal.Engine;
using SkiaSharp;

namespace Lily.Shell.TerminalView.Rendering;

/// <summary>
/// Unpacks a CodeBrix.Terminal packed cell attribute into a drawable
/// <see cref="CellStyle"/>. The packed layout (from CharData/CharacterAttribute):
/// bits 0-8 background color index, bits 9-17 foreground color index,
/// bits 18+ the FLAGS enum. Index 256 is the default color, 257 the inverted
/// default; 0-255 index the ANSI palette.
/// </summary>
public static class AttributeDecoder
{
    private const int DefaultColorIndex = 256;   //Renderer.DefaultColor
    private const int InvertedColorIndex = 257;  //Renderer.InvertedDefaultColor

    /// <summary>
    /// Decodes a packed attribute against the view's default colors.
    /// </summary>
    public static CellStyle Decode(int attribute, SKColor defaultForeground, SKColor defaultBackground)
    {
        var flags = (FLAGS)(attribute >> 18);
        var fgIndex = (attribute >> 9) & 0x1ff;
        var bgIndex = attribute & 0x1ff;

        //Classic bold-as-bright: BOLD promotes the dark palette (0-7) to bright (8-15)
        if (flags.HasFlag(FLAGS.BOLD) && fgIndex < 8)
        {
            fgIndex += 8;
        }

        var foreground = Resolve(fgIndex, defaultForeground, defaultBackground);
        var background = Resolve(bgIndex, defaultBackground, defaultForeground);

        if (flags.HasFlag(FLAGS.INVERSE))
        {
            (foreground, background) = (background, foreground);
        }

        if (flags.HasFlag(FLAGS.DIM))
        {
            foreground = Dim(foreground);
        }

        if (flags.HasFlag(FLAGS.INVISIBLE))
        {
            foreground = background;
        }

        return new CellStyle(
            foreground,
            background,
            flags.HasFlag(FLAGS.BOLD),
            flags.HasFlag(FLAGS.ITALIC),
            flags.HasFlag(FLAGS.UNDERLINE),
            flags.HasFlag(FLAGS.CrossedOut));
    }

    private static SKColor Resolve(int index, SKColor defaultColor, SKColor invertedDefault)
    {
        if (index == DefaultColorIndex) { return defaultColor; }
        if (index == InvertedColorIndex) { return invertedDefault; }

        if (index >= 0 && index < Color.DefaultAnsiColors.Count)
        {
            var c = Color.DefaultAnsiColors[index];
            return new SKColor(c.Red, c.Green, c.Blue);
        }

        return defaultColor;
    }

    private static SKColor Dim(SKColor color) =>
        new((byte)(color.Red * 0.6), (byte)(color.Green * 0.6), (byte)(color.Blue * 0.6));
}
