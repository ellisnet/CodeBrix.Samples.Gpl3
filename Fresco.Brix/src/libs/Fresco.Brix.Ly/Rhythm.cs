// === python-ly ly.rhythm module ===
//
// Copyright (c) 2011 - 2015 by Wilbert Berendsen
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

using Fresco.Brix.Ly.Lex;
using System;
using System.Collections.Generic;
using System.Linq;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Ly; //was previously: ly/rhythm.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A musical item that has a duration — the tokens that make it up, the
/// duration tokens among them, and where a duration could be written.
/// </summary>
public sealed class MusicItem
{
    /// <summary>Initializes the item.</summary>
    /// <param name="tokens">The item's non-duration tokens.</param>
    /// <param name="durationTokens">The item's duration tokens.</param>
    /// <param name="mayRemove">Whether the duration may be removed.</param>
    /// <param name="insertPos">Where a duration could be inserted.</param>
    /// <param name="pos">The position of the first token.</param>
    /// <param name="end">The end position of the last token.</param>
    internal MusicItem(
        IReadOnlyList<Token> tokens,
        IReadOnlyList<Token> durationTokens,
        bool mayRemove,
        int insertPos,
        int pos,
        int end)
    {
        Tokens = tokens;
        DurationTokens = durationTokens;
        MayRemove = mayRemove;
        InsertPos = insertPos;
        Pos = pos;
        End = end;
    }

    /// <summary>Gets the item's tokens, the duration tokens excepted.</summary>
    public IReadOnlyList<Token> Tokens { get; } //was previously: tokens

    /// <summary>Gets the item's duration tokens.</summary>
    public IReadOnlyList<Token> DurationTokens { get; } //was previously: dur_tokens

    /// <summary>Gets whether the duration may be removed.</summary>
    public bool MayRemove { get; } //was previously: may_remove

    /// <summary>Gets the position a duration could be inserted at.</summary>
    public int InsertPos { get; } //was previously: insert_pos

    /// <summary>Gets the position of the first token.</summary>
    public int Pos { get; }

    /// <summary>Gets the end position of the last token.</summary>
    public int End { get; }
}

/// <summary>
/// The tools that edit the durations of selected music. Durations are simply
/// lists of <see cref="LilyPondMode.Duration"/> tokens; every method takes a
/// <see cref="Cursor"/> holding the selected range.
/// </summary>
/// <remarks>Upstream's deprecated <c>music_tokens()</c> is not ported — it is
/// documented as going away and nothing consumes it.</remarks>
public static class Rhythm
{
    /// <summary>The duration values, longest first.</summary>
    public static readonly string[] Durations =
    {
        "\\maxima", "\\longa", "\\breve",
        "1", "2", "4", "8", "16", "32", "64", "128", "256", "512", "1024", "2048",
    };

    private static readonly string[] Unremovable = { "\\skip", "\\tempo", "\\tuplet", "\\partial" };

    private static readonly string[] NotImplicit = { "\\tempo", "\\tuplet", "\\partial" };

    /// <summary>
    /// Yields the items describing the rests, skips and pitches in the
    /// cursor's range.
    /// </summary>
    /// <param name="cursor">The range to read.</param>
    /// <param name="command">Whether to allow pitches inside
    /// <c>\relative</c>, <c>\transpose</c> and friends.</param>
    /// <param name="chord">Whether to allow pitches inside chords.</param>
    /// <param name="partial">How tokens overlapping the range's edges are
    /// treated.</param>
    /// <returns>The items.</returns>
    public static IEnumerable<MusicItem> MusicItems(
        Cursor cursor,
        bool command = false,
        bool chord = false,
        OverlapMode partial = OverlapMode.Inside)
    {
        var source = new Source(
            cursor, stateFromDocument: true, partial: partial, tokensWithPosition: true);

        //Python's `for token in source` leaves `token` bound to its last value
        //when the source runs out, and the code below depends on that — hence
        //the explicit `next`/`token` pair rather than a while-assignment.
        Token token = null;
        Token next;
        while ((next = source.NextToken()) != null)
        {
            token = next;
            if (SkipParser(source, command, chord)) { continue; }

            if (token.Text == "\\tuplet")
            {
                //Skip past the duration tokens of a \tuplet command.
                var tupletTokens = new List<Token> { token };
                while ((next = source.NextToken()) != null)
                {
                    token = next;
                    if (token is LilyPondMode.Duration)
                    {
                        tupletTokens.Add(token);
                        while ((next = source.NextToken()) != null)
                        {
                            token = next;
                            if (!(token is LilyPondMode.Duration)) { break; }

                            tupletTokens.Add(token);
                        }

                        break;
                    }

                    if (token is Numeric)
                    {
                        tupletTokens.Add(token);
                    }
                    else if (!(token is Space))
                    {
                        break;
                    }
                }

                yield return MakeItem(tupletTokens);
            }

            bool lengthSeen = false;
            bool sourceRanOut = false;
            while (IsStart(token))
            {
                var tokens = new List<Token> { token };
                if (token is LilyPondMode.Length) { lengthSeen = true; }

                bool broke = false;
                while ((next = source.NextToken()) != null)
                {
                    token = next;
                    if (token is LilyPondMode.Length)
                    {
                        if (lengthSeen)
                        {
                            yield return MakeItem(tokens);
                            lengthSeen = false;
                            broke = true;
                            break;
                        }

                        lengthSeen = true;
                    }
                    else if (token is Space)
                    {
                        continue;
                    }
                    else if (token is LilyPondMode.ChordSeparator)
                    {
                        //Prevent seeing the g in e.g. \chordmode { c/g }. The
                        //token this stops on is dropped, as upstream drops it.
                        while ((next = source.NextToken()) != null)
                        {
                            token = next;
                            if (!(token is Space) && !(token is LilyPondMode.Note)) { break; }
                        }

                        continue;
                    }
                    else if (!IsStay(token))
                    {
                        yield return MakeItem(tokens);
                        lengthSeen = false;
                        broke = true;
                        break;
                    }

                    tokens.Add(token);
                }

                if (!broke)
                {
                    //The source ran out inside the item (upstream's for-else,
                    //which yields what it has and leaves the outer loop).
                    yield return MakeItem(tokens);
                    sourceRanOut = true;
                    break;
                }
            }

            if (sourceRanOut) { yield break; }
        }
    }

    /// <summary>Answers the duration written before the cursor, if any.</summary>
    /// <param name="cursor">The cursor.</param>
    /// <returns>The duration tokens, empty when there is none.</returns>
    public static IReadOnlyList<Token> PrecedingDuration(Cursor cursor)
    {
        Runner runner = Runner.At(cursor);
        foreach (Token t in runner.Backward())
        {
            if (!(t is LilyPondMode.Duration)) { continue; }

            var found = new List<Token> { t };
            foreach (Token u in runner.Backward())
            {
                if (u is LilyPondMode.Duration)
                {
                    found.Add(u);
                }
                else if (!(u is Space))
                {
                    break;
                }
            }

            found.Reverse();
            return found;
        }

        return Array.Empty<Token>();
    }

    /// <summary>Doubles every duration value in the range.</summary>
    /// <param name="cursor">The range to edit.</param>
    public static void Double(Cursor cursor) => Scale(cursor, -1);

    /// <summary>Halves every duration value in the range.</summary>
    /// <param name="cursor">The range to edit.</param>
    public static void Halve(Cursor cursor) => Scale(cursor, 1);

    /// <summary>Adds a dot to every duration in the range.</summary>
    /// <param name="cursor">The range to edit.</param>
    public static void Dot(Cursor cursor)
    {
        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in MusicItems(cursor).ToList())
            {
                foreach (Token token in item.DurationTokens)
                {
                    if (token is LilyPondMode.Length)
                    {
                        cursor.Document.SetText(token.End, token.End, ".");
                        break;
                    }
                }
            }
        }
    }

    /// <summary>Removes one dot from every duration in the range.</summary>
    /// <param name="cursor">The range to edit.</param>
    public static void Undot(Cursor cursor)
    {
        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in MusicItems(cursor).ToList())
            {
                foreach (Token token in item.DurationTokens)
                {
                    if (token is LilyPondMode.Dot)
                    {
                        cursor.Document.Delete(token.Pos, token.End);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>Removes the scaling (like <c>*3</c>) from every duration.</summary>
    /// <param name="cursor">The range to edit.</param>
    public static void RemoveScaling(Cursor cursor)
    {
        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in MusicItems(cursor).ToList())
            {
                foreach (Token token in item.DurationTokens)
                {
                    if (token is LilyPondMode.Scaling)
                    {
                        cursor.Document.Delete(token.Pos, token.End);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Removes the scaling that contains a fraction (like <c>*1/3</c>) from
    /// every duration.
    /// </summary>
    /// <param name="cursor">The range to edit.</param>
    public static void RemoveFractionScaling(Cursor cursor)
    {
        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in MusicItems(cursor).ToList())
            {
                foreach (Token token in item.DurationTokens)
                {
                    if (token is LilyPondMode.Scaling && token.Text.Contains('/'))
                    {
                        cursor.Document.Delete(token.Pos, token.End);
                    }
                }
            }
        }
    }

    /// <summary>Removes every duration in the range.</summary>
    /// <param name="cursor">The range to edit.</param>
    public static void Remove(Cursor cursor)
    {
        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in MusicItems(cursor).ToList())
            {
                if (item.DurationTokens.Count > 0 && item.MayRemove)
                {
                    cursor.Document.Delete(
                        item.DurationTokens[0].Pos,
                        item.DurationTokens[item.DurationTokens.Count - 1].End);
                }
            }
        }
    }

    /// <summary>Removes durations that repeat the preceding one.</summary>
    /// <param name="cursor">The range to edit.</param>
    public static void Implicit(Cursor cursor)
    {
        List<MusicItem> items = MusicItems(cursor).ToList();
        if (items.Count == 0) { return; }

        MusicItem first = items[0];
        IReadOnlyList<Token> previous = IsNotImplicit(first)
            ? null
            : (first.DurationTokens.Count > 0
                ? first.DurationTokens
                : PrecedingDuration(cursor));

        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in items.Skip(1))
            {
                if (IsNotImplicit(item)) { continue; }

                if (item.DurationTokens.Count == 0) { continue; }

                if (item.MayRemove && SameDuration(item.DurationTokens, previous))
                {
                    cursor.Document.Delete(
                        item.DurationTokens[0].Pos,
                        item.DurationTokens[item.DurationTokens.Count - 1].End);
                }

                previous = item.DurationTokens;
            }
        }
    }

    /// <summary>
    /// Removes durations that repeat the preceding one, but always writes one
    /// at the start of a new line.
    /// </summary>
    /// <param name="cursor">The range to edit.</param>
    public static void ImplicitPerLine(Cursor cursor)
    {
        List<MusicItem> items = MusicItems(cursor).ToList();
        if (items.Count == 0) { return; }

        MusicItem first = items[0];
        IReadOnlyList<Token> previous = IsNotImplicit(first)
            ? null
            : (first.DurationTokens.Count > 0
                ? first.DurationTokens
                : PrecedingDuration(cursor));

        DocumentBlock previousBlock = previous != null && previous.Count > 0
            ? cursor.Document.GetBlock(previous[0].Pos)
            : null;

        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in items.Skip(1))
            {
                if (IsNotImplicit(item)) { continue; }

                IReadOnlyList<Token> anchor = item.DurationTokens.Count > 0
                    ? item.DurationTokens
                    : item.Tokens;
                DocumentBlock block = cursor.Document.GetBlock(anchor[0].Pos);
                if (!ReferenceEquals(block, previousBlock))
                {
                    if (item.DurationTokens.Count == 0)
                    {
                        cursor.Document.SetText(
                            item.InsertPos, item.InsertPos, Join(previous));
                    }
                    else
                    {
                        previous = item.DurationTokens;
                    }

                    previousBlock = block;
                }
                else if (item.DurationTokens.Count > 0)
                {
                    if (item.MayRemove && SameDuration(item.DurationTokens, previous))
                    {
                        cursor.Document.Delete(
                            item.DurationTokens[0].Pos,
                            item.DurationTokens[item.DurationTokens.Count - 1].End);
                    }

                    previous = item.DurationTokens;
                }
            }
        }
    }

    /// <summary>Writes out every implied duration in the range.</summary>
    /// <param name="cursor">The range to edit.</param>
    public static void Explicit(Cursor cursor)
    {
        List<MusicItem> items = MusicItems(cursor).ToList();
        if (items.Count == 0) { return; }

        IReadOnlyList<Token> previous = items[0].DurationTokens.Count > 0
            ? items[0].DurationTokens
            : PrecedingDuration(cursor);

        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in items.Skip(1))
            {
                if (IsNotImplicit(item)) { continue; }

                if (item.DurationTokens.Count > 0)
                {
                    previous = item.DurationTokens;
                }
                else
                {
                    cursor.Document.SetText(item.InsertPos, item.InsertPos, Join(previous));
                }
            }
        }
    }

    /// <summary>
    /// Applies a list of durations, e.g. <c>["4", "8", "", "16."]</c>, to the
    /// range, repeating the list as often as needed and leaving a duration out
    /// when it repeats its predecessor.
    /// </summary>
    /// <param name="cursor">The range to edit.</param>
    /// <param name="durations">The durations to apply.</param>
    public static void Overwrite(Cursor cursor, IReadOnlyList<string> durations)
    {
        if (durations == null || durations.Count == 0) { return; }

        using IEnumerator<string> source = RemoveDuplicates(Cycle(durations)).GetEnumerator();
        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in MusicItems(cursor).ToList())
            {
                int pos = item.InsertPos;
                int end = item.DurationTokens.Count > 0
                    ? item.DurationTokens[item.DurationTokens.Count - 1].End
                    : pos;
                source.MoveNext();
                cursor.Document.SetText(pos, end, source.Current);
            }
        }
    }

    /// <summary>Answers the durations written in the cursor's range.</summary>
    /// <param name="cursor">The range to read.</param>
    /// <returns>The durations, as they are written.</returns>
    public static IReadOnlyList<string> Extract(Cursor cursor)
    {
        var durations = new List<string>();
        foreach (MusicItem item in MusicItems(cursor))
        {
            durations.Add(
                Join(item.DurationTokens)
                + Join(item.Tokens.Where(t => t is LilyPondMode.Tie).ToList()));
        }

        //When the first duration was not written, look it up.
        if (durations.Count > 0 && durations[0].Length == 0)
        {
            IReadOnlyList<Token> preceding = PrecedingDuration(cursor);
            durations[0] = preceding.Count > 0 ? Join(preceding) : "4";
        }

        return durations;
    }

    private static void Scale(Cursor cursor, int step)
    {
        using (cursor.Document.Writing())
        {
            foreach (MusicItem item in MusicItems(cursor).ToList())
            {
                foreach (Token token in item.DurationTokens)
                {
                    if (!(token is LilyPondMode.Length)) { continue; }

                    int i = Array.IndexOf(Durations, token.Text);
                    if (i != -1)
                    {
                        int target = i + step;
                        if (target >= 0 && target < Durations.Length)
                        {
                            cursor.Document.SetText(token.Pos, token.End, Durations[target]);
                        }
                    }

                    break;
                }
            }
        }
    }

    private static bool SkipParser(Source source, bool command, bool chord)
    {
        Slexing.Parser parser = source.State.CurrentParser();
        if (!command && parser is LilyPondMode.ParsePitchCommand) { return true; }

        return !chord && parser is LilyPondMode.ParseChord;
    }

    private static bool IsStart(Token token)
        => token is LilyPondMode.Rest
            || token is LilyPondMode.Skip
            || token is LilyPondMode.Note
            || token is LilyPondMode.ChordEnd
            || token is LilyPondMode.Q
            || token is LilyPondMode.Octave
            || token is LilyPondMode.Accidental
            || token is LilyPondMode.OctaveCheck
            || token is LilyPondMode.Duration
            || token is LilyPondMode.Tempo
            || token is LilyPondMode.Partial;

    private static bool IsStay(Token token)
        => token is LilyPondMode.Octave
            || token is LilyPondMode.Accidental
            || token is LilyPondMode.OctaveCheck
            || token is LilyPondMode.Duration
            || token is LilyPondMode.Tie;

    private static MusicItem MakeItem(IReadOnlyList<Token> all)
    {
        var tokens = new List<Token>();
        var durationTokens = new List<Token>();
        int pos = all[0].Pos;
        int end = all[all.Count - 1].End;
        foreach (Token t in all)
        {
            if (t is LilyPondMode.Duration) { durationTokens.Add(t); } else { tokens.Add(t); }
        }

        bool mayRemove = !tokens.Any(t => Unremovable.Contains(t.Text));

        int insertPos;
        if (durationTokens.Count > 0)
        {
            insertPos = durationTokens[0].Pos;
        }
        else
        {
            //The last token that is not a tie decides where a duration goes.
            Token anchor = tokens[0];
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                anchor = tokens[i];
                if (!(anchor is LilyPondMode.Tie)) { break; }
            }

            insertPos = anchor.End;
        }

        return new MusicItem(tokens, durationTokens, mayRemove, insertPos, pos, end);
    }

    private static bool IsNotImplicit(MusicItem item)
        => item.Tokens.Any(t => NotImplicit.Contains(t.Text));

    private static bool SameDuration(IReadOnlyList<Token> a, IReadOnlyList<Token> b)
    {
        if (a == null || b == null) { return false; }

        if (a.Count != b.Count) { return false; }

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Text, b[i].Text, StringComparison.Ordinal)) { return false; }
        }

        return true;
    }

    private static string Join(IReadOnlyList<Token> tokens)
        => tokens == null ? string.Empty : string.Concat(tokens.Select(t => t.Text));

    private static IEnumerable<string> Cycle(IReadOnlyList<string> values)
    {
        while (true)
        {
            foreach (string value in values)
            {
                yield return value;
            }
        }
    }

    /// <summary>Changes a repeated string to the empty string.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The values, repeats blanked.</returns>
    private static IEnumerable<string> RemoveDuplicates(IEnumerable<string> values)
    {
        string old = null;
        foreach (string value in values)
        {
            yield return string.Equals(value, old, StringComparison.Ordinal)
                ? string.Empty
                : value;
            old = value;
        }
    }
}
