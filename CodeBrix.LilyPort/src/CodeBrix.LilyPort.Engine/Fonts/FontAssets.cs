// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// Where font bytes come from: the assembly's own embedded copies, with an optional
/// filesystem override in front.
/// <para>
/// New-in-family. Upstream finds fonts through FontConfig seeded with LilyPond's
/// installed data directory; the port has no FontConfig dependency and ships its fonts
/// inside the assembly, so nothing about locating them depends on where a consumer's
/// output directory happens to sit.
/// </para>
/// <para>
/// That last point is not a stylistic preference. The fonts were previously found by
/// walking up six directories from the running assembly looking for
/// <c>assets/fonts/otf</c>, which reaches the repository root from a project under
/// <c>src/</c> and falls one level short from
/// <c>tools/regression-harness/BatchDriver</c>. The regression sweep therefore ran the
/// entire suite with every music glyph missing — <c>ly:font-get-glyph</c> against a
/// null font — while reporting nothing worse than a warning per file.
/// </para>
/// </summary>
public static class FontAssets
{
    private const string MusicPrefix = "CodeBrix.LilyPort.Engine.Fonts.otf.";
    private const string OutlinePrefix = "CodeBrix.LilyPort.Engine.Fonts.svg.";
    private const string TextPrefix = "CodeBrix.LilyPort.Engine.Fonts.text.";

    private static readonly object Gate = new object();
    private static readonly List<string> Overrides = new List<string>();

    /// <summary>
    /// Gets the directories consulted BEFORE the embedded copies, in order. Empty by
    /// default. A consuming application adds to it to substitute its own font files;
    /// the regression harness's one-time DejaVu validation mode (D23 phase 1) is the
    /// only in-repo user.
    /// </summary>
    public static IList<string> SearchPaths
    {
        get
        {
            lock (Gate)
            {
                return Overrides;
            }
        }
    }

    /// <summary>Returns a music font's bytes, or <see langword="null"/> when unknown.</summary>
    /// <param name="name">The font name without a suffix, such as <c>emmentaler-20</c>.</param>
    /// <returns>The OTF bytes.</returns>
    public static byte[] MusicFont(string name) => Read(name + ".otf", MusicPrefix);

    /// <summary>
    /// Returns the SVG-font text carrying a music font's glyph OUTLINES, or
    /// <see langword="null"/> when unknown.
    /// <para>
    /// The outlines the SVG backend draws come from here rather than from the OTF's
    /// CFF charstrings, because that is where upstream's own SVG backend gets them:
    /// <c>output-svg.scm</c>'s <c>music-string-to-path</c> loads
    /// <c>&lt;name-style&gt;.svg</c> and lifts the <c>d</c> attribute out verbatim. An
    /// independently rasterised outline would be the same SHAPE and a different
    /// STRING, and the parity yardstick compares strings.
    /// </para>
    /// </summary>
    /// <param name="name">The font name without a suffix, such as <c>emmentaler-20</c>.</param>
    /// <returns>The SVG font document text.</returns>
    public static string OutlineFont(string name)
    {
        byte[] raw = Read(name + ".svg", OutlinePrefix);
        return raw == null ? null : System.Text.Encoding.UTF8.GetString(raw);
    }

    /// <summary>Returns a text font's bytes, or <see langword="null"/> when unknown.</summary>
    /// <param name="fileName">The file name, such as <c>NimbusSans-Regular.otf</c>.</param>
    /// <returns>The OTF bytes.</returns>
    public static byte[] TextFont(string fileName) => Read(fileName, TextPrefix);

    /// <summary>Lists the vendored text font file names.</summary>
    /// <returns>The file names, with the <c>.otf</c> suffix.</returns>
    public static IEnumerable<string> TextFontNames()
    {
        foreach (string resource in typeof(FontAssets).Assembly.GetManifestResourceNames())
        {
            if (resource.StartsWith(TextPrefix, StringComparison.Ordinal))
            {
                yield return resource.Substring(TextPrefix.Length);
            }
        }
    }

    private static byte[] Read(string fileName, string prefix)
    {
        lock (Gate)
        {
            foreach (string directory in Overrides)
            {
                if (string.IsNullOrEmpty(directory))
                {
                    continue;
                }

                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return File.ReadAllBytes(candidate);
                }
            }
        }

        Assembly assembly = typeof(FontAssets).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(prefix + fileName);
        if (stream == null)
        {
            return null;
        }

        using MemoryStream buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
