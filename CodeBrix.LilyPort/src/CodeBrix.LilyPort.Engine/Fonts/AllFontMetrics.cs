/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Fonts; //was previously: lily/all-font-metrics.cc, lily/include/all-font-metrics.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// The font cache: loads an Emmentaler by name and hands out the same instance every
/// time it is asked for again.
/// <para>
/// The cache matters for more than speed. A font's identity is what the layout's
/// scaled-font table is keyed on, and it is what the backend writes into a
/// <c>named-glyph</c> expression — so loading the same file twice would produce two
/// fonts that compare unequal and defeat both.
/// </para>
/// <para>
/// DIVERGENCE, recorded in PORT-COVERAGE: upstream finds font files through
/// FontConfig, seeded with LilyPond's installed data directory. The port has no
/// FontConfig dependency and ships its own fonts, so it searches
/// <see cref="SearchPaths"/> instead — seeded by probing beside the running assembly.
/// Only the MUSIC fonts are covered; text fonts are the CodeBrix.Platform TextLayout
/// add-in's job (master plan section 11).
/// </para>
/// </summary>
public static class AllFontMetrics
{
    private static readonly object Gate = new object();

    private static readonly Dictionary<string, OpenTypeFontMetric> Cache
        = new Dictionary<string, OpenTypeFontMetric>(StringComparer.Ordinal);

    private static List<string> _searchPaths;

    /// <summary>
    /// Gets the directories searched for <c>.otf</c> files, in order. Seeded by probing
    /// beside the running assembly; add to it to point the engine at another font set.
    /// </summary>
    public static IList<string> SearchPaths
    {
        get
        {
            lock (Gate)
            {
                return _searchPaths ??= ProbeDefaultPaths();
            }
        }
    }

    /// <summary>
    /// Loads a music font by name, or returns the instance already loaded.
    /// </summary>
    /// <param name="name">The font name without a suffix, such as <c>emmentaler-20</c>.</param>
    /// <returns>The font, or <see langword="null"/> when no file was found.</returns>
    public static OpenTypeFontMetric FindOtfFont(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(name, out OpenTypeFontMetric cached))
            {
                return cached;
            }

            string path = Locate(name);
            if (path == null)
            {
                Warn.Warning("cannot find font: `" + name + "'");
                return null;
            }

            OpenTypeFontMetric metric = new OpenTypeFontMetric(OpenTypeFont.Load(path), name);
            Cache[name] = metric;
            return metric;
        }
    }

    /// <summary>
    /// Discards every loaded font. This is <c>ly:reset-all-fonts</c>, which the Scheme
    /// layer calls when the global staff size changes.
    /// </summary>
    public static void ResetAllFonts()
    {
        lock (Gate)
        {
            Cache.Clear();
        }
    }

    private static string Locate(string name)
    {
        foreach (string directory in SearchPaths)
        {
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            string candidate = Path.Combine(directory, name + ".otf");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static List<string> ProbeDefaultPaths()
    {
        List<string> paths = new List<string>();

        string assemblyDirectory = Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            // Where a test project copies its font fixtures, and where a published
            // application would carry them.
            paths.Add(Path.Combine(assemblyDirectory, "TestFonts"));
            paths.Add(Path.Combine(assemblyDirectory, "fonts", "otf"));

            // Walking up to the repository's own assets/fonts/otf: bin/Release/net10.0
            // is three levels below a project, and a project is two below the repo root.
            string directory = assemblyDirectory;
            for (int level = 0; level < 6 && directory != null; level++)
            {
                paths.Add(Path.Combine(directory, "assets", "fonts", "otf"));
                directory = Path.GetDirectoryName(directory);
            }
        }

        return paths;
    }
}
