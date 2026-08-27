// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using System;
using System.Collections.Generic;
using System.IO;

namespace Fresco.Brix.MusicView;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The engine's own text faces, as FILES the PDF writer can register, with
/// the mapping from what the engine's SVG asks for to the family each file
/// declares.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LilyPortTypefaceResolver"/> answers the VIEW with the bytes of
/// the faces the engine measured the score's text with; the PDF writer
/// registers files. The faces are embedded resources of the engine, so they
/// are written out once, to the application's own data folder beside its
/// settings, and registered from there. The mapping is the resolver's own
/// table (<see cref="LilyPortTypefaceResolver.Normalize"/>), so the PDF is set
/// in exactly the face the view draws with — <c>serif</c> is C059, not a
/// packaged serif — which is board trap 9 carried into the export.
/// </para>
/// <para>
/// The family names are the ones the files' own name tables declare, which
/// the resolver already lists: C059, Nimbus Sans, Nimbus Mono PS, TeX Gyre
/// Schola.
/// </para>
/// </remarks>
public static class LilyPortScorePdfFonts
{
    private static readonly Dictionary<string, string> ChainFamilies
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["serif"] = "C059",
            ["sans"] = "Nimbus Sans",
            ["typewriter"] = "Nimbus Mono PS",
            ["unknown"] = "TeX Gyre Schola",
        };

    private static readonly object Gate = new object();
    private static ScorePdfFonts _fonts;

    /// <summary>Gets the folder the faces are written to.</summary>
    /// <returns><c>&lt;ApplicationData&gt;/Fresco.Brix/fonts/text</c>.</returns>
    public static string DefaultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Fresco.Brix", "fonts", "text");

    /// <summary>
    /// Returns the engine's faces as registrable files, writing them out on the
    /// first call.
    /// </summary>
    /// <returns>The description the PDF writer takes.</returns>
    public static ScorePdfFonts Get()
    {
        lock (Gate)
        {
            return _fonts ??= new ScorePdfFonts(Extract(DefaultDirectory()), MapFamily);
        }
    }

    /// <summary>Maps an SVG <c>font-family</c> value to the engine face's family.</summary>
    /// <param name="familyName">What the SVG asked for.</param>
    /// <returns>The declared family of the face the engine measured with.</returns>
    public static string MapFamily(string familyName)
        => ChainFamilies[LilyPortTypefaceResolver.Normalize(familyName)];

    /// <summary>
    /// Writes the engine's text faces into a folder, skipping any already there
    /// at the right size, and returns their paths.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The files written or found.</returns>
    public static IReadOnlyList<string> Extract(string directory)
    {
        Directory.CreateDirectory(directory);
        var files = new List<string>();
        foreach (string fileName in LilyPortTypefaceResolver.FaceFileNames)
        {
            byte[] bytes = FontAssets.TextFont(fileName);
            if (bytes == null) { continue; }

            string path = Path.Combine(directory, fileName);
            var existing = new FileInfo(path);
            if (!existing.Exists || existing.Length != bytes.Length)
            {
                File.WriteAllBytes(path, bytes);
            }

            files.Add(path);
        }

        return files;
    }
}
