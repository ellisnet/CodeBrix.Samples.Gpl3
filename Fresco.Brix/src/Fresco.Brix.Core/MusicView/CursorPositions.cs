// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Lex;
using System.Collections.Generic;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;

namespace Fresco.Brix.MusicView; //was previously: frescobaldi/pointandclick.py's positions()

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Works out which stretch of source text a point-and-click link really stands
/// for, so hovering a note in the music underlines the note in the source
/// rather than a single character of it.
/// </summary>
/// <remarks>
/// The engine's anchor names ONE position — where the grob's cause began. What
/// the reader wants to see is the whole thing: a markup expression to its
/// closing brace, a string to its closing quote, a slur to its matching end.
/// The rules here are upstream's, one for one.
/// </remarks>
public static class CursorPositions
{
    /// <summary>
    /// Returns the ranges of source text the object at a position stands for.
    /// </summary>
    /// <param name="document">The tokenized document.</param>
    /// <param name="position">Where the link points.</param>
    /// <returns>
    /// Zero, one or two ranges: two when the object has separate ends, such as
    /// the two halves of a slur.
    /// </returns>
    public static IReadOnlyList<(int Start, int Length)> Positions(DocumentBase document, int position)
    {
        var result = new List<(int Start, int Length)>();
        if (document == null) { return result; }

        var cursor = new Cursor(document, position, null);
        var source = new Source(cursor, stateFromDocument: true, tokensWithPosition: true);

        Token token = (Token)source.NextToken();
        if (token == null) { return result; }

        int start = source.Position(token);
        int end = start + token.Text.Length;

        if (token is LilyPondMode.Direction)
        {
            //A _, - or ^ only says WHERE the next thing goes; the next thing is
            //what the link is really about.
            while ((token = (Token)source.NextToken()) != null)
            {
                if (token is not Space && token is not Comment) { break; }
            }

            if (token == null) { result.Add((start, end - start)); return result; }

            end = source.Position(token) + token.Text.Length;
        }

        if (token.Text == "\\markup")
        {
            int depth = source.State.Depth();
            while ((token = (Token)source.NextToken()) != null)
            {
                if (source.State.Depth() < depth)
                {
                    end = source.Position(token) + token.Text.Length;
                    break;
                }
            }
        }
        else if (token.Text == "\"")
        {
            if (token is StringEnd)
            {
                //An engine bug can point at the CLOSING quote of a string; walk
                //back to the opening one so the whole string is shown.
                end = source.Position(token) + token.Text.Length;
                Runner backward = Runner.At(cursor, false, true);
                foreach (Token previous in backward.Backward())
                {
                    if (previous is StringStart) { start = backward.Position(); break; }
                }
            }
            else
            {
                while ((token = (Token)source.NextToken()) != null)
                {
                    if (token is StringEnd)
                    {
                        end = source.Position(token) + token.Text.Length;
                        break;
                    }
                }
            }
        }
        else if (token is IMatchStart matchStart)
        {
            string name = matchStart.MatchName;
            int nest = 1;
            while ((token = (Token)source.NextToken()) != null)
            {
                if (token is IMatchEnd matchEnd && matchEnd.MatchName == name)
                {
                    nest--;
                    if (nest == 0)
                    {
                        int otherStart = source.Position(token);
                        result.Add((otherStart, token.Text.Length));
                        break;
                    }
                }
                else if (token is IMatchStart inner && inner.MatchName == name)
                {
                    nest++;
                }
            }
        }

        result.Insert(0, (start, end - start));
        return result;
    }
}
