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

namespace Fresco.Brix.Ly; //was previously: ly/barcheck.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Bar-check editing over a selection.
/// <para>
/// Upstream's <c>insert()</c> is an UNFINISHED experiment — it builds
/// time-based event lists and then <c>print</c>s them instead of inserting
/// anything, and no Frescobaldi feature calls it — so only <c>remove()</c> is
/// ported; revisit if upstream ever finishes the other half.
/// </para>
/// </summary>
public static class Barcheck
{
    /// <summary>Removes bar checks (<c>|</c>) from the selected music,
    /// tidying adjacent whitespace the way upstream does.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Remove(Cursor cursor)
    {
        Source s = new Source(cursor, tokensWithPosition: true);
        Token prv = null;
        Token cur = null;
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (Token nxt in WithTrailingNull(s))
            {
                if (cur is LilyPondMode.PipeSymbol)
                {
                    if (prv is Space)
                    {
                        // pipesymbol and adjacent space may be deleted
                        if (nxt != null && nxt.Text == "\n")
                        {
                            d.Delete(prv.Pos, cur.End);
                        }
                        else if (nxt is Space)
                        {
                            d.Delete(cur.Pos, nxt.End);
                        }
                        else
                        {
                            d.Delete(cur.Pos, cur.End);
                        }
                    }
                    else if (nxt is Space)
                    {
                        // delete if followed by a space
                        d.Delete(cur.Pos, cur.End);
                    }
                    else
                    {
                        // replace "|" with a space
                        d.SetText(cur.Pos, cur.End, " ");
                    }
                }

                prv = cur;
                cur = nxt;
            }
        }
    }

    private static IEnumerable<Token> WithTrailingNull(Source source)
    {
        foreach (Token token in source)
        {
            yield return token;
        }

        yield return null;
    }
}
