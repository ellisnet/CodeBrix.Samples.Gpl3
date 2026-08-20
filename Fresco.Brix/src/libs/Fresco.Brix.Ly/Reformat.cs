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

using Fresco.Brix.Ly.Lex;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using SchemeMode = Fresco.Brix.Ly.Lex.SchemeMode;
using Fresco.Brix.Ly.Slexing;
using Token = Fresco.Brix.Ly.Slexing.Token;
using System.Collections.Generic;

namespace Fresco.Brix.Ly; //was previously: ly/reformat.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Formatting tools improving readability without changing the semantic
/// meaning of the source: only whitespace moves. See also
/// <see cref="Indenter"/>.
/// </summary>
public static class Reformatter
{
    /// <summary>
    /// Adds newlines around indent and dedent tokens where needed: stuff after
    /// an unclosed <c>{</c> or <c>&lt;&lt;</c> goes on a new line, and a
    /// <c>}</c> or <c>&gt;&gt;</c> with stuff before it goes on a new line.
    /// Run the indenter again afterwards.
    /// </summary>
    /// <param name="cursor">The range to treat.</param>
    public static void BreakIndenters(Cursor cursor)
    {
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (DocumentBlock b in cursor.Blocks())
            {
                List<int> denters = new List<int>();
                Token[] tokens = d.Tokens(b);
                int nonspaceIndex = -1;
                for (int i = 0; i < tokens.Length; i++)
                {
                    Token t = tokens[i];
                    if (t is IIndent && (t.Text == "{" || t.Text == "<<"))
                    {
                        denters.Add(i);
                    }
                    else if (t is IDedent && (t.Text == "}" || t.Text == ">>"))
                    {
                        if (denters.Count > 0)
                        {
                            denters.RemoveAt(denters.Count - 1);
                        }
                        else if (nonspaceIndex != -1)
                        {
                            // add newline before t
                            int pos = d.Position(b) + t.Pos;
                            d.SetText(pos, pos, "\n");
                        }
                    }

                    if (!(t is Space))
                    {
                        nonspaceIndex = i;
                    }
                }

                foreach (int i in denters)
                {
                    if (i < nonspaceIndex)
                    {
                        // add newline after tokens[i]
                        int pos = d.Position(b) + tokens[i].End;
                        d.SetText(pos, pos, "\n");
                    }
                }
            }
        }
    }

    /// <summary>Moves line comments with more than 2 comment characters to
    /// column 0.</summary>
    /// <param name="cursor">The range to treat.</param>
    public static void MoveLongComments(Cursor cursor)
    {
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (DocumentBlock b in cursor.Blocks())
            {
                Token[] tokens = d.Tokens(b);
                if (tokens.Length == 2
                    && tokens[0] is Space
                    && (tokens[1] is LilyPondMode.LineComment
                        || tokens[1] is SchemeMode.LineComment)
                    && tokens[1].Text.Length >= 3
                    && (tokens[1].Text.Substring(0, 3) == "%%%"
                        || tokens[1].Text.Substring(0, 3) == ";;;"))
                {
                    d.Delete(d.Position(b), d.Position(b) + tokens[1].Pos);
                }
            }
        }
    }

    /// <summary>Removes trailing whitespace from all lines in the range.</summary>
    /// <param name="cursor">The range to treat.</param>
    public static void RemoveTrailingWhitespace(Cursor cursor)
    {
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (DocumentBlock b in cursor.Blocks())
            {
                Token[] tokens = d.Tokens(b);
                if (tokens.Length == 0)
                {
                    continue;
                }

                Token t = tokens[tokens.Length - 1];
                int end = d.Position(b) + t.End;
                if (t is Space)
                {
                    d.Delete(end - t.Length, end);
                }
                else if (!(t is StringBase))
                {
                    int offset = t.Length - t.Text.TrimEnd().Length;
                    if (offset != 0)
                    {
                        d.Delete(end - offset, end);
                    }
                }
            }
        }
    }

    /// <summary>The do-it-all formatter: break indenters, indent, move long
    /// comments, strip trailing whitespace.</summary>
    /// <param name="cursor">The range to treat.</param>
    /// <param name="indenter">The indenter to run.</param>
    public static void Reformat(Cursor cursor, Indenter indenter)
    {
        BreakIndenters(cursor);
        indenter.Indent(cursor);
        MoveLongComments(cursor);
        RemoveTrailingWhitespace(cursor);
    }
}
