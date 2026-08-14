// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// One text FACE — a single OTF file — reduced to what laying out a line of text needs:
/// which glyph a character maps to, how far the pen moves, and how tall the ink is.
/// <para>
/// New-in-family. Upstream asks Pango, which asks FreeType; the port reads the tables
/// directly and runs the charstrings (see <see cref="CffFont"/>) for the one figure no
/// table records.
/// </para>
/// </summary>
public sealed class TextFace
{
    private readonly SfntReader _reader;
    private readonly CffFont _cff;
    private readonly Dictionary<int, int> _cmap;
    private readonly double[] _advances;
    private readonly KerningTable _kerning;

    private TextFace(string fileName, SfntReader reader)
    {
        FileName = fileName;
        _reader = reader;
        UnitsPerEm = reader.UnitsPerEm;
        _cmap = reader.ReadCmap();
        _advances = reader.ReadAdvances();
        _kerning = KerningTable.Read(reader);

        byte[] cff = reader.GetTable("CFF ");
        _cff = cff == null ? null : new CffFont(cff);
    }

    /// <summary>Loads a vendored text face by file name, or returns null when absent.</summary>
    /// <param name="fileName">The file name, such as <c>C059-Roman.otf</c>.</param>
    /// <returns>The face.</returns>
    public static TextFace Load(string fileName)
    {
        byte[] bytes = FontAssets.TextFont(fileName);
        return bytes == null ? null : new TextFace(fileName, new SfntReader(bytes));
    }

    /// <summary>Gets the file the face was read from.</summary>
    public string FileName { get; }

    /// <summary>Gets the design units per em.</summary>
    public int UnitsPerEm { get; }

    /// <summary>Gets the underlying container reader.</summary>
    public SfntReader Reader => _reader;

    /// <summary>Determines whether the face can draw a code point.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns><see langword="true"/> when it maps to a real glyph.</returns>
    public bool Covers(int codePoint) => _cmap.ContainsKey(codePoint);

    /// <summary>Returns a code point's glyph index, or 0 for <c>.notdef</c>.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The glyph index.</returns>
    public int GlyphIndex(int codePoint)
        => _cmap.TryGetValue(codePoint, out int glyph) ? glyph : 0;

    /// <summary>Returns a glyph's horizontal advance, in design units.</summary>
    /// <param name="glyph">The glyph index.</param>
    /// <returns>The advance.</returns>
    public double Advance(int glyph)
        => glyph >= 0 && glyph < _advances.Length ? _advances[glyph] : 0.0;

    /// <summary>
    /// Returns the kerning advance adjustment between two adjacent glyphs of one run,
    /// in design units. Zero when the face carries no kerning or the pair none.
    /// </summary>
    /// <param name="leftGlyph">The earlier glyph's index.</param>
    /// <param name="rightGlyph">The later glyph's index.</param>
    /// <returns>The adjustment; most kern pairs are negative.</returns>
    public double Kerning(int leftGlyph, int rightGlyph)
        => _kerning == null ? 0.0 : _kerning.Adjustment(leftGlyph, rightGlyph);

    /// <summary>Returns a glyph's ink bounding box, in design units.</summary>
    /// <param name="glyph">The glyph index.</param>
    /// <returns>The box.</returns>
    public Box GlyphBox(int glyph) => _cff == null ? default : _cff.GlyphBox(glyph);

    /// <summary>Gets the face's charstring interpreter, or <see langword="null"/>.</summary>
    /// <remarks>
    /// EPG14 needs it to trace a text run's real outlines into a skyline, which is what
    /// closed EPG13's carried-forward text divergence.
    /// </remarks>
    public CffFont Cff => _cff;
}

/// <summary>
/// The ordered list of faces a text request may draw from — decision D23's fallback
/// chain, made concrete.
/// <para>
/// A chain is a family (serif, sans, typewriter) crossed with a style (bold, italic),
/// and it runs: the URW face LilyPond defaults to, then the TeX Gyre face upstream's
/// <c>00-lilypond-fonts.conf</c> names next, and then STOPS. Upstream continues into
/// DejaVu and Noto CJK, which it does not ship; the port continues into the Roboto
/// package instead and never into a system font, so what a score looks like does not
/// depend on what happens to be installed on the machine that renders it. A code point
/// no face in the chain covers deliberately draws missing-glyph tofu.
/// </para>
/// </summary>
public static class TextFontChain
{
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, TextFace> Loaded
        = new Dictionary<string, TextFace>(StringComparer.Ordinal);

    // Each family lists its fallback levels, and each level its four faces indexed by
    // (bold ? 1 : 0) + (italic ? 2 : 0). Spelled out rather than generated from a
    // template because the three collections do not agree on how to name a face: URW
    // writes "Regular" and "BoldItalic", C059 writes "Roman" and "BdIta", and TeX Gyre
    // writes everything in lower case. A template silently produces a file name that
    // does not exist, and a missing face does not fail — it just drops out of the
    // chain, leaving text measured by the FALLBACK font.
    private static readonly Dictionary<string, string[][]> Families
        = new Dictionary<string, string[][]>(StringComparer.OrdinalIgnoreCase)
        {
            ["serif"] = new[]
            {
                new[]
                {
                    "C059-Roman.otf", "C059-Bold.otf", "C059-Italic.otf", "C059-BdIta.otf",
                },
                new[]
                {
                    "texgyreschola-regular.otf", "texgyreschola-bold.otf",
                    "texgyreschola-italic.otf", "texgyreschola-bolditalic.otf",
                },
            },
            ["sans"] = new[]
            {
                new[]
                {
                    "NimbusSans-Regular.otf", "NimbusSans-Bold.otf",
                    "NimbusSans-Italic.otf", "NimbusSans-BoldItalic.otf",
                },
                new[]
                {
                    "texgyreheros-regular.otf", "texgyreheros-bold.otf",
                    "texgyreheros-italic.otf", "texgyreheros-bolditalic.otf",
                },
            },
            ["typewriter"] = new[]
            {
                new[]
                {
                    "NimbusMonoPS-Regular.otf", "NimbusMonoPS-Bold.otf",
                    "NimbusMonoPS-Italic.otf", "NimbusMonoPS-BoldItalic.otf",
                },
                new[]
                {
                    "texgyrecursor-regular.otf", "texgyrecursor-bold.otf",
                    "texgyrecursor-italic.otf", "texgyrecursor-bolditalic.otf",
                },
            },
        };

    /// <summary>
    /// Returns the faces to try, in order, for a family and style.
    /// </summary>
    /// <param name="family">The generic family: <c>serif</c>, <c>sans</c> or <c>typewriter</c>.</param>
    /// <param name="bold">Whether bold was asked for.</param>
    /// <param name="italic">Whether italic was asked for.</param>
    /// <returns>The loaded faces, in fallback order; empty when nothing resolved.</returns>
    public static IReadOnlyList<TextFace> For(string family, bool bold, bool italic)
    {
        string key = Normalize(family);
        if (!Families.TryGetValue(key, out string[][] levels))
        {
            levels = Families["serif"];
        }

        int style = (bold ? 1 : 0) + (italic ? 2 : 0);

        List<TextFace> chain = new List<TextFace>();
        foreach (string[] level in levels)
        {
            TextFace face = Face(level[style]);
            if (face != null)
            {
                chain.Add(face);
            }
        }

        return chain;
    }

    /// <summary>Loads a face by file name, caching it.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The face, or <see langword="null"/> when there is no such file.</returns>
    public static TextFace Face(string fileName)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(fileName, out TextFace cached))
            {
                return cached;
            }

            TextFace face = TextFace.Load(fileName);
            Loaded[fileName] = face;
            return face;
        }
    }

    /// <summary>Discards every loaded face.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Loaded.Clear();
        }
    }

    private static string Normalize(string family)
    {
        if (string.IsNullOrEmpty(family))
        {
            return "serif";
        }

        string lower = family.Trim().ToLowerInvariant();
        if (lower.Contains("mono") || lower.Contains("typewriter") || lower.Contains("courier"))
        {
            return "typewriter";
        }

        if (lower.Contains("sans") && !lower.Contains("sans serif"))
        {
            return "sans";
        }

        return lower.Contains("sans") ? "sans" : "serif";
    }
}
