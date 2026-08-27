// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Lex;
using System;
using System.Collections.Generic;
using System.Linq;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/quickremove.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Takes one kind of LilyPond input back out of a selection: the slurs, the
/// beams, the fingerings, the dynamics, the comments — leaving everything else
/// exactly as it was.
/// </summary>
/// <remarks>
/// Every command here works the same way: walk the selection's tokens, yield
/// the ranges to remove, and delete them in one undo group. Upstream expresses
/// that with a decorator; here it is <see cref="RemoveRanges"/>, which every
/// command ends in.
/// </remarks>
public static class QuickRemove
{
    /// <summary>Answers whether a token is an articulation.</summary>
    /// <param name="token">The token.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsArticulation(Token token)
        => token is Lily.Articulation
            && Words.Articulations.Contains(TailOf(token));

    /// <summary>Answers whether a token is an ornament.</summary>
    /// <param name="token">The token.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsOrnament(Token token)
        => token is Lily.Articulation
            && Words.Ornaments.Contains(TailOf(token));

    /// <summary>Answers whether a token is an instrument script.</summary>
    /// <param name="token">The token.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsInstrumentScript(Token token)
        => token is Lily.Articulation
            && Words.InstrumentScripts.Contains(TailOf(token));

    /// <summary>Removes the comments in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void Comments(Cursor cursor)
        => RemoveRanges(cursor, CommentRanges(cursor));

    /// <summary>Removes the articulations in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void Articulations(Cursor cursor)
        => RemoveRanges(cursor, FindPositions(
            cursor,
            IsArticulation,
            t => t is Lily.ScriptAbbreviation || IsArticulation(t)));

    /// <summary>Removes the ornaments in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void Ornaments(Cursor cursor)
        => RemoveRanges(cursor, FindPositions(cursor, IsOrnament));

    /// <summary>Removes the instrument scripts in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void InstrumentScripts(Cursor cursor)
        => RemoveRanges(cursor, FindPositions(cursor, IsInstrumentScript));

    /// <summary>Removes the slurs in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void Slurs(Cursor cursor)
        => RemoveRanges(cursor, FindPositions(cursor, t => t is Lily.Slur));

    /// <summary>Removes the beams in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void Beams(Cursor cursor)
        => RemoveRanges(cursor, FindPositions(cursor, t => t is Lily.Beam));

    /// <summary>Removes the ligatures in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void Ligatures(Cursor cursor)
        => RemoveRanges(cursor, FindPositions(cursor, t => t is Lily.Ligature));

    /// <summary>Removes the dynamics in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void Dynamics(Cursor cursor)
        => RemoveRanges(cursor, FindPositions(cursor, t => t is Lily.Dynamic));

    /// <summary>Removes the fingerings in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void Fingerings(Cursor cursor)
        => RemoveRanges(cursor, FindPositions(cursor, t => t is Lily.Fingering));

    /// <summary>Removes the postfix markup texts in a range.</summary>
    /// <param name="cursor">The range.</param>
    public static void Markup(Cursor cursor)
        => RemoveRanges(cursor, MarkupRanges(cursor));

    /// <summary>
    /// Makes every direction in a range the same: up, down or neutral.
    /// </summary>
    /// <param name="cursor">The range.</param>
    /// <param name="direction">One of <c>up</c>, <c>down</c>, <c>neutral</c>.</param>
    /// <remarks>Both halves of upstream's function are here: the
    /// <c>^ _ -</c> operators are replaced with the wanted one, and a command
    /// ending in <c>Up</c>/<c>Down</c>/<c>Neutral</c> (such as
    /// <c>\slurUp</c>) has its suffix swapped.</remarks>
    public static void ForceDirections(Cursor cursor, string direction)
    {
        if (cursor == null) { return; }

        if (!DirectionOperators.TryGetValue(direction, out string operatorText)) { return; }

        string suffix = DirectionCommands[direction];
        var edits = new List<(int Start, int End, string Text)>();
        var source = new Source(
            cursor, null, false, OverlapMode.Partial, tokensWithPosition: true);

        foreach (var token in source)
        {
            if (token is Lily.Direction)
            {
                edits.Add((token.Pos, token.End, operatorText));
            }
            else if (token is Lily.Command)
            {
                string text = token.Text;
                foreach (var known in DirectionCommands.Values)
                {
                    if (!text.EndsWith(known, StringComparison.Ordinal)
                        || text.Length == known.Length)
                    {
                        continue;
                    }

                    edits.Add((
                        token.Pos,
                        token.End,
                        text.Substring(0, text.Length - known.Length) + suffix));
                    break;
                }
            }
        }

        ApplyEdits(cursor, edits);
    }

    /// <summary>
    /// Yields the ranges of the tokens a predicate accepts, taking a preceding
    /// direction operator with them.
    /// </summary>
    /// <param name="cursor">The range to look in.</param>
    /// <param name="predicate">What to remove.</param>
    /// <param name="predicateAfterDirection">What to remove when it follows a
    /// direction operator, or null to use <paramref name="predicate"/>.</param>
    /// <returns>The ranges.</returns>
    public static IEnumerable<(int Start, int End)> FindPositions(
        Cursor cursor,
        Func<Token, bool> predicate,
        Func<Token, bool> predicateAfterDirection = null)
    {
        predicateAfterDirection ??= predicate;
        var source = new Source(
            cursor, null, false, OverlapMode.Partial, tokensWithPosition: true);
        IEnumerator<Token> stream = source.GetEnumerator();

        while (stream.MoveNext())
        {
            Token token = stream.Current;
            if (token is Lily.Direction)
            {
                int start = token.Pos;

                //Upstream reads on from source.tokens — the REMAINDER of the
                //current line — so a direction at the end of a line takes
                //nothing with it. The shared enumerator gives the same.
                while (source.Tokens.MoveNext())
                {
                    Token next = source.Tokens.Current;
                    if (next is Space) { continue; }

                    if (predicateAfterDirection(next))
                    {
                        yield return (start, next.End);
                    }

                    break;
                }
            }
            else if (predicate(token))
            {
                yield return (token.Pos, token.End);
            }
        }
    }

    /// <summary>Deletes ranges from the cursor's document in one undo group.</summary>
    /// <param name="cursor">The document to edit.</param>
    /// <param name="ranges">The ranges.</param>
    public static void RemoveRanges(
        Cursor cursor, IEnumerable<(int Start, int End)> ranges)
    {
        if (cursor == null) { return; }

        //Materialised BEFORE the write scope opens: the ranges come out of a
        //lazy walk over the very text the scope is about to change.
        var list = ranges.ToList();
        if (list.Count == 0) { return; }

        using (cursor.Document.Writing())
        {
            foreach (var (start, end) in list)
            {
                cursor.Document.Delete(start, end);
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, string> DirectionOperators
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["up"] = "^", ["neutral"] = "-", ["down"] = "_",
        };

    private static readonly IReadOnlyDictionary<string, string> DirectionCommands
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["up"] = "Up", ["neutral"] = "Neutral", ["down"] = "Down",
        };

    private static string TailOf(Token token)
        => token.Text.Length > 1 ? token.Text.Substring(1) : string.Empty;

    private static IEnumerable<(int Start, int End)> CommentRanges(Cursor cursor)
    {
        var source = new Source(
            cursor, null, stateFromDocument: true, tokensWithPosition: true);
        foreach (var token in source)
        {
            if (token is Comment)
            {
                yield return (token.Pos, token.End);
            }
        }
    }

    private static IEnumerable<(int Start, int End)> MarkupRanges(Cursor cursor)
    {
        var source = new Source(
            cursor, null, stateFromDocument: true, tokensWithPosition: true);
        IEnumerator<Token> stream = source.GetEnumerator();

        while (stream.MoveNext())
        {
            if (stream.Current is not Lily.Direction) { continue; }

            int start = stream.Current.Pos;
            while (stream.MoveNext())
            {
                Token token = stream.Current;
                if (token.Text == "\\markup")
                {
                    //Read to where the markup expression closes, which the
                    //parser depth says better than counting braces would.
                    int depth = source.State.Depth();
                    while (stream.MoveNext())
                    {
                        if (source.State.Depth() >= depth) { continue; }

                        yield return (start, stream.Current.End);
                        break;
                    }
                }
                else if (token.Text == "\"")
                {
                    while (stream.MoveNext())
                    {
                        if (stream.Current is not StringEnd) { continue; }

                        yield return (start, stream.Current.End);
                        break;
                    }
                }
                else if (token.Text.Length > 0 && token.Text.All(char.IsLetter))
                {
                    yield return (start, token.End);
                }
                else if (token is Space)
                {
                    continue;
                }

                break;
            }
        }
    }

    private static void ApplyEdits(
        Cursor cursor, IReadOnlyList<(int Start, int End, string Text)> edits)
    {
        if (edits.Count == 0) { return; }

        using (cursor.Document.Writing())
        {
            foreach (var (start, end, text) in edits)
            {
                cursor.Document.SetText(start, end, text);
            }
        }
    }
}
