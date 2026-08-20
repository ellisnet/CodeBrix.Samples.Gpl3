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
using System;
using System.Collections.Generic;

namespace Fresco.Brix.Ly; //was previously: ly/rests.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Tools to edit rests of selected music; every function takes a
/// <see cref="Cursor"/> with the selected range.</summary>
public static class Rests
{
    /// <summary>Replaces full rests (<c>r</c>) with the given token.</summary>
    /// <param name="cursor">The selection.</param>
    /// <param name="replaceToken">The replacement text.</param>
    public static void ReplaceRest(Cursor cursor, string replaceToken)
        => ReplaceRestKind(cursor, "r", replaceToken);

    /// <summary>Replaces full measure rests (<c>R</c>) with the given token.</summary>
    /// <param name="cursor">The selection.</param>
    /// <param name="replaceToken">The replacement text.</param>
    public static void ReplaceFmRest(Cursor cursor, string replaceToken)
        => ReplaceRestKind(cursor, "R", replaceToken);

    private static void ReplaceRestKind(Cursor cursor, string kind, string replaceToken)
    {
        Source source = new Source(
            cursor, stateFromDocument: true, tokensWithPosition: true);
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (Token token in source)
            {
                if (token is LilyPondMode.Rest && token.Text == kind)
                {
                    d.SetText(token.Pos, token.End, replaceToken);
                }
            }
        }
    }

    /// <summary>Replaces spacer rests (<c>s</c>) with the given token.</summary>
    /// <param name="cursor">The selection.</param>
    /// <param name="replaceToken">The replacement text.</param>
    public static void ReplaceSpacer(Cursor cursor, string replaceToken)
    {
        Source source = new Source(
            cursor, stateFromDocument: true, tokensWithPosition: true);
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (Token token in source)
            {
                if (token is LilyPondMode.Spacer)
                {
                    d.SetText(token.Pos, token.End, replaceToken);
                }
            }
        }
    }

    /// <summary>Replaces pitched rests (<c>c\rest</c>) with the given
    /// token, deleting the space and the <c>\rest</c> command.</summary>
    /// <param name="cursor">The selection.</param>
    /// <param name="replaceToken">The replacement text.</param>
    public static void ReplaceRestComm(Cursor cursor, string replaceToken)
    {
        static IEnumerable<List<Token>> GetCommRests(Source source)
        {
            List<Token> restTokens = null;
            foreach (Token token in source)
            {
                if (token is LilyPondMode.Note)
                {
                    restTokens = new List<Token> { token };
                    continue;
                }

                if (restTokens != null && token is Space)
                {
                    restTokens.Add(token);
                    continue;
                }

                if (restTokens != null && token is LilyPondMode.Command
                    && token.Text == "\\rest")
                {
                    restTokens.Add(token);
                    yield return restTokens;
                    restTokens = null;
                }
            }
        }

        Source source = new Source(
            cursor, stateFromDocument: true, tokensWithPosition: true);
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (List<Token> rt in GetCommRests(source))
            {
                Token note = rt[0];
                Token space = rt[rt.Count - 2];
                Token comm = rt[rt.Count - 1];
                d.SetText(note.Pos, note.End, replaceToken);
                d.Delete(space.Pos, space.End);
                d.Delete(comm.Pos, comm.End);
            }
        }
    }
}
