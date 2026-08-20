// This file is part of the Frescobaldi project, http://www.frescobaldi.org/
//
// Copyright (c) 2008 - 2014 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 2
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

using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Lex;
using System.Collections.Generic;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/matcher.py (the matches() function);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Finds the matching token pair around a caret — brackets, simultaneous
/// arrows, slurs, beams, ligatures and scheme parens, matched by the
/// tokenizer's own matchname pairs rather than by characters, so a brace
/// inside a string or comment never matches one outside it.
/// </summary>
public static class TokenMatcher
{
    /// <summary>
    /// Returns zero to two document ranges specifying matching tokens: empty
    /// when the position is not at a match token; one range when its partner
    /// could not be found; two when found — the first is the token at the
    /// position, the second its partner.
    /// </summary>
    /// <param name="document">The ly document view of the editor text.</param>
    /// <param name="position">The caret position in the document.</param>
    /// <returns>The ranges, as (start, length) pairs.</returns>
    public static List<(int Start, int Length)> Matches(
        DocumentBase document, int position)
    {
        List<(int, int)> result = new List<(int, int)>();
        DocumentBlock block = document.GetBlock(position);
        if (block == null)
        {
            return result;
        }

        int column = position - document.Position(block);
        Runner tokens = new Runner(document);
        tokens.MoveToBlock(block);

        Token found = null;
        bool forward = false;
        string matchName = null;
        foreach (Token token in tokens.ForwardLine())
        {
            if (token.Pos <= column && column <= token.End)
            {
                if (token is IMatchStart start)
                {
                    found = token;
                    forward = true;
                    matchName = start.MatchName;
                    break;
                }

                if (token is IMatchEnd end)
                {
                    found = token;
                    forward = false;
                    matchName = end.MatchName;
                    break;
                }
            }
            else if (token.Pos > column)
            {
                break;
            }
        }

        if (found == null)
        {
            return result;
        }

        result.Add((tokens.Position(), found.Length));

        int nest = 0;
        foreach (Token token2 in forward ? tokens.Forward() : tokens.Backward())
        {
            bool isOther = forward
                ? token2 is IMatchEnd otherEnd && otherEnd.MatchName == matchName
                : token2 is IMatchStart otherStart && otherStart.MatchName == matchName;
            bool isSame = forward
                ? token2 is IMatchStart sameStart && sameStart.MatchName == matchName
                : token2 is IMatchEnd sameEnd && sameEnd.MatchName == matchName;

            if (isOther)
            {
                if (nest == 0)
                {
                    // we've found the matching item!
                    result.Add((tokens.Position(), token2.Length));
                    break;
                }

                nest -= 1;
            }
            else if (isSame)
            {
                nest += 1;
            }
        }

        return result;
    }
}
