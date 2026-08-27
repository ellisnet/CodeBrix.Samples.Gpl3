// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System;
using System.Collections.Generic;

namespace Fresco.Brix.Ly.Lex; //was previously: ly/lex/_mode.py and ly/lex/__init__.py's state()/guessState();

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The registry of tokenizer modes: mode name to initial parser, the mode
/// guesser, and the state factories.
/// </summary>
public static class Modes
{
    private static readonly Dictionary<string, Func<Slexing.Parser>> Registry
        = new Dictionary<string, Func<Slexing.Parser>>(StringComparer.Ordinal)
        {
            { "lilypond", () => new LilyPondMode.ParseGlobal() },
            { "scheme", () => new SchemeMode.ParseScheme() },
            { "docbook", () => new DocbookMode.ParseDocBook() },
            { "latex", () => new LatexMode.ParseLaTeX() },
            { "texinfo", () => new TexinfoMode.ParseTexinfo() },
            { "html", () => new HtmlMode.ParseHTML() },
            { "mup", () => new MupMode.ParseMup() },
        };

    /// <summary>
    /// The default file extension per mode — upstream's <c>extensions</c> dict.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Extensions
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "lilypond", ".ly" },
            { "html", ".html" },
            { "scheme", ".scm" },
            { "latex", ".lytex" },
            { "texinfo", ".texi" },
            { "docbook", ".docbook" },
            { "mup", ".mup" },
        };

    /// <summary>Gets the known mode names.</summary>
    public static IEnumerable<string> Names => Registry.Keys;

    /// <summary>Answers whether a mode name is known — python's <c>mode in modes</c>.</summary>
    /// <param name="mode">The mode name.</param>
    /// <returns>Whether the mode exists.</returns>
    public static bool Exists(string mode) => mode != null && Registry.ContainsKey(mode);

    /// <summary>Returns a State instance for the given mode — upstream's
    /// <c>ly.lex.state(mode)</c>.</summary>
    /// <param name="mode">The mode name.</param>
    /// <returns>The state.</returns>
    public static State CreateState(string mode) => new State(Registry[mode]());

    /// <summary>Returns a State instance, guessing the type of text —
    /// upstream's <c>ly.lex.guessState(text)</c>.</summary>
    /// <param name="text">The text to guess from.</param>
    /// <returns>The state.</returns>
    public static State GuessState(string text) => CreateState(GuessMode(text));

    /// <summary>
    /// Tries to guess the type of the input text with upstream's quick heuristic.
    /// </summary>
    /// <param name="text">The text to guess from.</param>
    /// <returns>One of the mode names.</returns>
    public static string GuessMode(string text)
    {
        text = (text ?? string.Empty).TrimStart();
        if (text.StartsWith("%", StringComparison.Ordinal)
            || text.StartsWith("\\", StringComparison.Ordinal))
        {
            if (text.Contains("\\version") || text.Contains("\\relative")
                || text.Contains("\\score"))
            {
                return "lilypond";
            }

            if (text.Contains("\\documentclass") || text.Contains("\\begin{document}"))
            {
                return "latex";
            }

            return "lilypond";
        }

        if (text.StartsWith("<<", StringComparison.Ordinal))
        {
            return "lilypond";
        }

        if (text.StartsWith("<", StringComparison.Ordinal))
        {
            return text.Contains("DOCTYPE book") || text.Contains("<programlisting")
                ? "docbook"
                : "html";
        }

        if (text.StartsWith("#!", StringComparison.Ordinal)
            || text.StartsWith(";", StringComparison.Ordinal)
            || text.StartsWith("(", StringComparison.Ordinal))
        {
            return "scheme";
        }

        if (text.StartsWith("@", StringComparison.Ordinal))
        {
            return "texinfo";
        }

        return "lilypond";
    }
}
