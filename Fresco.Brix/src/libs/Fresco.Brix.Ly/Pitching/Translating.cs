// === python-ly ly.pitch.translate module ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using PitchTable = Fresco.Brix.Ly.Pitching.Pitches;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Ly.Pitching; //was previously: ly/pitch/translate.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Translates the language pitch names are written in.</summary>
public static class Translating
{
    /// <summary>
    /// Changes the language of the pitch names in the cursor's range.
    /// </summary>
    /// <param name="cursor">The range to translate.</param>
    /// <param name="language">The language to write.</param>
    /// <param name="defaultLanguage">The language to start reading in.</param>
    /// <returns>Whether a <c>\language</c> or <c>\include</c> language command
    /// was changed too. When it answers false and the cursor covered only part
    /// of the document, the caller may want to warn the user, or call
    /// <see cref="InsertLanguage"/>.</returns>
    /// <exception cref="PitchNameNotAvailableException">When the current pitch
    /// language has no quarter tones.</exception>
    public static bool Translate(
        Cursor cursor, string language, string defaultLanguage = "nederlands")
    {
        int start = cursor.Start;
        cursor.Start = 0;

        var source = new Source(cursor, tokensWithPosition: true);
        var pitches = new PitchIterator(source, defaultLanguage);
        IEnumerable<Token> tokens = pitches.Tokens();
        PitchWriter writer = PitchTable.PitchWriterFor(language);

        if (start > 0)
        {
            //Consume the tokens before the selection, following the language.
            source.Consume(tokens, start);
            cursor.Start = start;
        }

        bool changed = false; //whether a \language or \include command changed
        using (cursor.Document.Writing())
        {
            foreach (Token t in tokens)
            {
                if (t is LilyPondMode.Note)
                {
                    //Translate the pitch name.
                    if (pitches.Read(t, out int note, out Fraction alter))
                    {
                        string name = writer.Write(note, alter);
                        if (name != t.Text)
                        {
                            cursor.Document.SetText(t.Pos, t.End, name);
                        }
                    }
                }
                else if (t is LanguageName)
                {
                    if (t.Text != language)
                    {
                        //Change the language name in the command.
                        cursor.Document.SetText(t.Pos, t.End, language);
                    }

                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Inserts a language command at the top of the document, just below the
    /// version line when there is one.
    /// </summary>
    /// <param name="document">The document to change.</param>
    /// <param name="language">The language to select.</param>
    /// <param name="version">The document's LilyPond version, or
    /// <see langword="null"/> when unknown; before 2.13.38 the older
    /// <c>\include</c> form is written instead of <c>\language</c>.</param>
    public static void InsertLanguage(
        DocumentBase document, string language, IReadOnlyList<int> version = null)
    {
        string text = IsBefore(version, 2, 13, 38)
            ? string.Format(CultureInfo.InvariantCulture, "\\include \"{0}.ly\"\n", language)
            : string.Format(CultureInfo.InvariantCulture, "\\language \"{0}\"\n", language);

        using (document.Writing())
        {
            foreach (DocumentBlock block in document.Blocks())
            {
                if (document.Tokens(block).Any(t => t.Text == "\\version")) { continue; }

                int pos = document.Position(block);
                document.SetText(pos, pos, text);
                return;
            }

            int end = document.Size();
            document.SetText(end, end, "\n\n" + text);
        }
    }

    private static bool IsBefore(IReadOnlyList<int> version, params int[] other)
    {
        if (version == null || version.Count == 0) { return false; }

        for (int i = 0; i < other.Length; i++)
        {
            int part = i < version.Count ? version[i] : 0;
            if (part != other[i]) { return part < other[i]; }
        }

        return version.Count < other.Length;
    }
}
