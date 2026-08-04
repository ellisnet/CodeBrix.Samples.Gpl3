/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                 Jan Nieuwenhuizen <janneke@gnu.org>

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

namespace CodeBrix.LilyPort.Parsing.Session; //was previously: lily/lily-lexer.cc (the_key_tab, make_keytable);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// LilyPond's keyword table: the 45 words that are grammar keywords rather than
/// identifiers, each naming the terminal it lexes as.
/// <para>
/// Upstream this is the static <c>the_key_tab</c> in <c>lily/lily-lexer.cc</c>, turned
/// into a Scheme hash table by <c>make_keytable</c> and consulted by
/// <c>Lily_lexer::lookup_keyword</c>. It is C++ DATA, not Scheme, so it is ported as
/// data — the same reasoning as <c>entry-points.tsv</c> and
/// <c>grob-interfaces.tsv</c>, except that at 45 entries it is small enough to be
/// legible in source.
/// </para>
/// <para>
/// The terminal names are the port's, and they are the same strings
/// <c>parser.yy</c>'s <c>%token</c> declarations use, because the driver resolves
/// terminals by name against the generated tables.
/// </para>
/// </summary>
public static class LilyKeywords
{
    private static readonly Dictionary<string, string> Table
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "accepts", "ACCEPTS" },
            { "addlyrics", "ADDLYRICS" },
            { "alias", "ALIAS" },
            { "alternative", "ALTERNATIVE" },
            { "book", "BOOK" },
            { "bookpart", "BOOKPART" },
            { "change", "CHANGE" },
            { "chordmode", "CHORDMODE" },
            { "chords", "CHORDS" },
            { "consists", "CONSISTS" },
            { "context", "CONTEXT" },
            { "default", "DEFAULT" },
            { "defaultchild", "DEFAULTCHILD" },
            { "denies", "DENIES" },
            { "description", "DESCRIPTION" },
            { "drummode", "DRUMMODE" },
            { "drums", "DRUMS" },
            { "etc", "ETC" },
            { "figuremode", "FIGUREMODE" },
            { "figures", "FIGURES" },
            { "header", "HEADER" },
            { "layout", "LAYOUT" },
            { "lyricmode", "LYRICMODE" },
            { "lyrics", "LYRICS" },
            { "lyricsto", "LYRICSTO" },
            { "markup", "MARKUP" },
            { "markuplist", "MARKUPLIST" },
            { "midi", "MIDI" },
            { "name", "NAME" },
            { "new", "NEWCONTEXT" },
            { "notemode", "NOTEMODE" },
            { "override", "OVERRIDE" },
            { "paper", "PAPER" },
            { "remove", "REMOVE" },
            { "repeat", "REPEAT" },
            { "rest", "REST" },
            { "revert", "REVERT" },
            { "score", "SCORE" },
            { "sequential", "SEQUENTIAL" },
            { "set", "SET" },
            { "simultaneous", "SIMULTANEOUS" },
            { "tempo", "TEMPO" },
            { "type", "TYPE" },
            { "unset", "UNSET" },
            { "with", "WITH" },
        };

    /// <summary>Gets how many keywords there are — 45, as upstream declares.</summary>
    public static int Count => Table.Count;

    /// <summary>Looks a word up as a keyword.</summary>
    /// <param name="word">The word, without its backslash.</param>
    /// <returns>The terminal's name, or <see langword="null"/> when the word is not a
    /// keyword.</returns>
    public static string Lookup(string word)
        => word != null && Table.TryGetValue(word, out string terminal) ? terminal : null;

    /// <summary>Gets the keywords, for tests and diagnostics.</summary>
    /// <returns>The word-to-terminal pairs.</returns>
    public static IReadOnlyDictionary<string, string> All() => Table;
}
