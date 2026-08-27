// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2013 - 2015 by Wilbert Berendsen
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
using Fresco.Brix.Ly.Slexing;
using Token = Fresco.Brix.Ly.Slexing.Token;
using System.Collections.Generic;

namespace Fresco.Brix.Ly; //was previously: ly/cursortools.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Routines manipulating <see cref="Cursor"/> instances.</summary>
public static class CursorTools
{
    /// <summary>
    /// Yields (token, isIndent, nest) for every indent/dedent token occurring
    /// in the iterable.
    /// </summary>
    /// <param name="iterable">The tokens to scan.</param>
    /// <returns>The indent events.</returns>
    public static IEnumerable<(Token Token, bool IsIndent, int Nest)> FindIndent(
        IEnumerable<Token> iterable)
    {
        int nest = 0;
        foreach (Token token in iterable)
        {
            if (token is IIndent)
            {
                nest += 1;
                yield return (token, true, nest);
            }
            else if (token is IDedent)
            {
                nest -= 1;
                yield return (token, false, nest);
            }
        }
    }

    /// <summary>
    /// Tries to select a meaningful block: searches backwards for an indenting
    /// token, then selects up to the corresponding dedenting token, searching
    /// an extra level back if needed so the selection always extends.
    /// </summary>
    /// <param name="cursor">The cursor whose selection to grow.</param>
    /// <returns>Whether the cursor's selection has changed.</returns>
    public static bool SelectBlock(Cursor cursor)
    {
        int end = cursor.End ?? cursor.Document.Size();
        Runner tokens = Runner.At(cursor, afterToken: true);

        // search backwards to the first indenting token
        foreach ((Token _, bool isIndent, int nest) in FindIndent(tokens.Backward()))
        {
            if (isIndent && nest == 1)
            {
                int pos1 = tokens.Position();
                Runner startPoint = tokens.Copy();

                // found, now look forward
                foreach ((Token token, bool isIndent2, int nest2)
                    in FindIndent(tokens.Forward()))
                {
                    if (!isIndent2 && nest2 < 0 && tokens.Position() + token.Length >= end)
                    {
                        // we found the endpoint
                        int pos2 = tokens.Position() + token.Length;
                        if (nest2 < -1)
                        {
                            int threshold = 1 - nest2;
                            foreach ((Token _, bool isIndent3, int nest3)
                                in FindIndent(startPoint.Backward()))
                            {
                                if (isIndent3 && nest3 == threshold)
                                {
                                    pos1 = tokens.Position();
                                    break;
                                }
                            }
                        }

                        cursor.Start = pos1;
                        cursor.End = pos2;
                        return true;
                    }
                }

                return false;
            }
        }

        return false;
    }
}
