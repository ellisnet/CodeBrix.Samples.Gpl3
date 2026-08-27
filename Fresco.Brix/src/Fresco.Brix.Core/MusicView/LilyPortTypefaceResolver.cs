// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace Fresco.Brix.MusicView;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Answers the Music View's font questions with the very faces the ENGINE
/// measured the score's text with.
/// </summary>
/// <remarks>
/// <para>
/// Board trap 9, and it is not a matter of taste. The engine laid the title,
/// the lyrics and the dynamics out against particular faces; drawing them in
/// anything else moves the words off the places the engraver put them. Those
/// faces are vendored inside the engine as embedded resources, so this asks the
/// engine for the bytes rather than looking for a file.
/// </para>
/// <para>
/// The family table mirrors the engine's own — the CSS generics its SVG backend
/// emits (<c>serif</c>, <c>sans</c>, <c>monospace</c>) plus the three names
/// upstream's font configuration defines — and a family nothing answers falls
/// to the same last-resort face the engine uses. Nothing here ever reaches a
/// font installed on the machine: an uncovered character is meant to draw tofu.
/// </para>
/// </remarks>
public sealed class LilyPortTypefaceResolver : IScoreTypefaceResolver
{
    //Each family's faces, indexed by (bold ? 1 : 0) + (italic ? 2 : 0). Spelled
    //out because the three collections do not agree on how to name a face.
    private static readonly Dictionary<string, string[]> Families
        = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["serif"] = new[] { "C059-Roman.otf", "C059-Bold.otf", "C059-Italic.otf", "C059-BdIta.otf" },
            ["sans"] = new[]
            {
                "NimbusSans-Regular.otf", "NimbusSans-Bold.otf",
                "NimbusSans-Italic.otf", "NimbusSans-BoldItalic.otf",
            },
            ["typewriter"] = new[]
            {
                "NimbusMonoPS-Regular.otf", "NimbusMonoPS-Bold.otf",
                "NimbusMonoPS-Italic.otf", "NimbusMonoPS-BoldItalic.otf",
            },
            ["unknown"] = new[]
            {
                "texgyreschola-regular.otf", "texgyreschola-bold.otf",
                "texgyreschola-italic.otf", "texgyreschola-bolditalic.otf",
            },
        };

    //The names that resolve by CATEGORY: the CSS generics the SVG backend asks
    //for, the three virtual names upstream's own font configuration defines,
    //and — see below — the four faces this table can itself hand back.
    private static readonly Dictionary<string, string> Generics
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["serif"] = "serif",
            ["sans"] = "sans",
            ["sans-serif"] = "sans",
            ["monospace"] = "typewriter",
            ["LilyPond Serif"] = "serif",
            ["LilyPond Sans Serif"] = "sans",
            ["LilyPond Monospace"] = "typewriter",

            //THE RESOLVER HAS TO BE IDEMPOTENT, and it was not.
            ////was previously: these four rows were absent, and the whole table
            //silently drew the wrong face for every piece of text on every page.
            //The renderer does not ask once. It asks for the family the SVG
            //names, reads the FamilyName off the face it is handed, and asks
            //AGAIN under that name to build the font it actually draws with —
            //so "serif" came back as C059, "C059" matched nothing here, fell to
            //the last-resort chain, and TeX Gyre Schola did the drawing. It went
            //unseen because C059 and TeX Gyre Schola are both Century
            //Schoolbook cuts and the difference is a hair of sidebearing; the
            //PDF exporter is what exposed it, because a PDF says out loud which
            //faces it embedded (board trap 60).
            //Only the four families this table can RETURN are listed. TeX Gyre
            //Heros and TeX Gyre Cursor are the engine's own second-level
            //fallbacks and are never handed out here, so naming them would be
            //inventing a mapping rather than closing a loop.
            ["C059"] = "serif",
            ["Nimbus Sans"] = "sans",
            ["Nimbus Mono PS"] = "typewriter",
            ["TeX Gyre Schola"] = "unknown",
        };

    /// <summary>Gets every face file the four chains can hand out.</summary>
    /// <remarks>What <see cref="LilyPortScorePdfFonts"/> writes out for the PDF
    /// writer to register, so the export and the view agree on the faces.</remarks>
    public static IReadOnlyList<string> FaceFileNames
    {
        get
        {
            var names = new List<string>();
            foreach (string[] faces in Families.Values) { names.AddRange(faces); }

            return names;
        }
    }

    private readonly Dictionary<string, SKTypeface> _cache
        = new Dictionary<string, SKTypeface>(StringComparer.Ordinal);
    private readonly object _gate = new object();

    /// <inheritdoc/>
    public SKTypeface Resolve(
        string familyName, SKFontStyleWeight weight, SKFontStyleWidth width, SKFontStyleSlant slant)
    {
        bool bold = weight >= SKFontStyleWeight.SemiBold;
        bool italic = slant != SKFontStyleSlant.Upright;
        string[] faces = Families[Normalize(familyName)];
        return Load(faces[(bold ? 1 : 0) + (italic ? 2 : 0)]);
    }

    /// <summary>
    /// Reduces a family request to one of the four chains.
    /// </summary>
    /// <param name="familyName">
    /// What the SVG asked for: a generic, a comma-separated CSS list, or a name.
    /// </param>
    /// <returns>The chain's key.</returns>
    /// <remarks>
    /// A CSS list is walked left to right and the first name that resolves by
    /// category wins, which is what the engine's own reading of such a list
    /// comes to. A name nothing knows gets the last-resort chain rather than
    /// nothing at all — the engine measured it with that face, so it must be
    /// drawn with it.
    /// </remarks>
    public static string Normalize(string familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName)) { return "serif"; }

        foreach (string part in familyName.Split(','))
        {
            string name = part.Trim().Trim('\'', '"');
            if (name.Length == 0) { continue; }

            if (Generics.TryGetValue(name, out string chain)) { return chain; }
        }

        string first = familyName.Split(',')[0].Trim().Trim('\'', '"');
        if (first.Contains("mono", StringComparison.OrdinalIgnoreCase)) { return "typewriter"; }

        return first.Contains("sans", StringComparison.OrdinalIgnoreCase) ? "sans" : "unknown";
    }

    private SKTypeface Load(string fileName)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(fileName, out SKTypeface cached)) { return cached; }

            SKTypeface typeface = null;
            byte[] bytes = FontAssets.TextFont(fileName);
            if (bytes != null)
            {
                using var stream = new MemoryStream(bytes, writable: false);
                typeface = SKTypeface.FromStream(stream);
            }

            _cache[fileName] = typeface;
            return typeface;
        }
    }
}
