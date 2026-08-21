// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Lex;
using System;
using System.Collections.Generic;
using System.Linq;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.QuickInsert; //was previously: frescobaldi/quickinsert/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Which way a Quick Insert sign points.</summary>
public enum InsertDirection
{
    /// <summary>Below the staff — <c>_</c>.</summary>
    Down = -1,

    /// <summary>Wherever LilyPond puts it — no operator.</summary>
    Neutral = 0,

    /// <summary>Above the staff — <c>^</c>.</summary>
    Up = 1,
}

/// <summary>
/// Where a Quick Insert sign goes and what it says — everything about the
/// panel except its buttons.
/// </summary>
/// <remarks>
/// The buttons all work the same way: find the music item (or items) the sign
/// attaches to, and put text after it. Which items depends on whether there is
/// a selection, which is what <see cref="ArticulationPositions"/> and
/// <see cref="SpannerPositions"/> answer.
/// </remarks>
public static class QuickInsertLogic
{
    /// <summary>The articulations that have a one-character shorthand.</summary>
    public static readonly IReadOnlyDictionary<string, string> Shorthands
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["marcato"] = "^",
            ["stopped"] = "+",
            ["tenuto"] = "-",
            ["staccatissimo"] = "!",
            ["accent"] = ">",
            ["staccato"] = ".",
            ["portato"] = "_",
        };

    /// <summary>Gets the direction operator: <c>_</c>, nothing, or <c>^</c>.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The operator.</returns>
    public static string DirectionOperator(InsertDirection direction)
        => direction switch
        {
            InsertDirection.Down => "_",
            InsertDirection.Up => "^",
            _ => string.Empty,
        };

    /// <summary>
    /// Gets the operator used where a sign MUST carry one, so that the
    /// shorthand form is unambiguous.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The operator.</returns>
    /// <remarks>Upstream's <c>'_-^'[direction+1]</c>: a shorthand with no
    /// operator would be read as a duration or a fingering.</remarks>
    public static string ShorthandOperator(InsertDirection direction)
        => direction switch
        {
            InsertDirection.Down => "_",
            InsertDirection.Up => "^",
            _ => "-",
        };

    /// <summary>Gets the text an articulation button inserts.</summary>
    /// <param name="name">The articulation name.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="allowShorthands">Whether to use the short form.</param>
    /// <returns>The text.</returns>
    public static string ArticulationText(
        string name, InsertDirection direction, bool allowShorthands)
        => allowShorthands && Shorthands.TryGetValue(name, out string shorthand)
            ? ShorthandOperator(direction) + shorthand
            : DirectionOperator(direction) + "\\" + name;

    /// <summary>
    /// Finds where an articulation can be attached: after every music item in
    /// the selection, or after the first one on the caret's line.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="selectionStart">Where the selection starts.</param>
    /// <param name="selectionEnd">Where it ends, or the same as the start.</param>
    /// <returns>The offsets, in order.</returns>
    public static IReadOnlyList<int> ArticulationPositions(
        EditorDocument document, int selectionStart, int selectionEnd)
    {
        DocumentEditorState state = DocumentEditorState.For(document);
        bool hasSelection = selectionEnd > selectionStart;
        Cursor cursor = MakeCursor(
            document, state, selectionStart, selectionEnd, hasSelection);
        OverlapMode partial = hasSelection ? OverlapMode.Inside : OverlapMode.Outside;

        List<int> positions = new List<int>();
        foreach (var item in Rhythm.MusicItems(cursor, partial: partial))
        {
            //With no selection the caret's own item is wanted even if it is a
            //rest; inside a selection the rests are skipped.
            if (hasSelection && item.Tokens.Count > 0
                && item.Tokens[0] is Lily.Rest)
            {
                continue;
            }

            positions.Add(item.End);
            if (!hasSelection) { break; }
        }

        return positions;
    }

    /// <summary>
    /// Finds where a spanner starts and ends: the first and last music item of
    /// the selection, or the first two on the caret's line.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="selectionStart">Where the selection starts.</param>
    /// <param name="selectionEnd">Where it ends, or the same as the start.</param>
    /// <returns>Nought, one or two offsets.</returns>
    public static IReadOnlyList<int> SpannerPositions(
        EditorDocument document, int selectionStart, int selectionEnd)
    {
        DocumentEditorState state = DocumentEditorState.For(document);
        bool hasSelection = selectionEnd > selectionStart;
        Cursor cursor = MakeCursor(
            document, state, selectionStart, selectionEnd, hasSelection);
        OverlapMode partial = hasSelection ? OverlapMode.Inside : OverlapMode.Outside;

        List<MusicItem> items = Rhythm.MusicItems(cursor, partial: partial).ToList();
        if (items.Count == 0) { return Array.Empty<int>(); }

        List<MusicItem> wanted = hasSelection
            ? (items.Count > 1
                ? new List<MusicItem> { items[0], items[items.Count - 1] }
                : new List<MusicItem> { items[0] })
            : items.Take(2).ToList();

        return wanted.Select(i => i.End).ToList();
    }

    /// <summary>Gets the two halves of a spanner.</summary>
    /// <param name="name">The spanner name.</param>
    /// <param name="direction">The direction.</param>
    /// <returns>The starting and ending text.</returns>
    public static (string Start, string End) Spanner(
        string name, InsertDirection direction)
    {
        string d = DirectionOperator(direction);
        return name switch
        {
            "spanner_slur" => (d + "(", ")"),
            "spanner_phrasingslur" => (d + "\\(", "\\)"),
            "spanner_beam16" => (d + "[", "]"),
            "spanner_trill" => ("\\startTrillSpan", "\\stopTrillSpan"),
            "spanner_melisma" => ("\\melisma", "\\melismaEnd"),
            _ => (string.Empty, string.Empty),
        };
    }

    /// <summary>The arpeggio commands, by button name.</summary>
    public static readonly IReadOnlyDictionary<string, string> ArpeggioTypes
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["arpeggio_normal"] = "\\arpeggioNormal",
            ["arpeggio_arrow_up"] = "\\arpeggioArrowUp",
            ["arpeggio_arrow_down"] = "\\arpeggioArrowDown",
            ["arpeggio_bracket"] = "\\arpeggioBracket",
            ["arpeggio_parenthesis"] = "\\arpeggioParenthesis",
        };

    /// <summary>The glissando line styles, by button name.</summary>
    public static readonly IReadOnlyDictionary<string, string> GlissandoStyles
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["glissando_normal"] = string.Empty,
            ["glissando_dashed"] = "dashed-line",
            ["glissando_dotted"] = "dotted-line",
            ["glissando_zigzag"] = "zigzag",
            ["glissando_trill"] = "trill",
        };

    /// <summary>The dynamic marks, in upstream's order.</summary>
    public static readonly IReadOnlyList<string> DynamicMarks = new[]
    {
        "f", "ff", "fff", "ffff", "fffff",
        "p", "pp", "ppp", "pppp", "ppppp",
        "mf", "mp", "fp", "sfz", "rfz",
        "sf", "sff", "sp", "spp",
    };

    /// <summary>The dynamic spanners, by button name without its prefix.</summary>
    public static readonly IReadOnlyDictionary<string, string> DynamicSpanners
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hairpin_cresc"] = "\\<",
            ["hairpin_dim"] = "\\>",
            ["cresc"] = "\\cresc",
            ["decresc"] = "\\decresc",
            ["dim"] = "\\dim",
        };

    /// <summary>Gets the glissando text a button inserts.</summary>
    /// <param name="name">The button name.</param>
    /// <returns>The text.</returns>
    public static string GlissandoText(string name)
    {
        string style = GlissandoStyles.TryGetValue(name, out string found)
            ? found
            : string.Empty;
        return style.Length == 0
            ? "\\glissando"
            : $"-\\tweak #'style #'{style} \\glissando";
    }

    /// <summary>Gets the two halves of a grace-note wrapper.</summary>
    /// <param name="name">The button name.</param>
    /// <param name="direction">The direction.</param>
    /// <returns>The outer pair, the inner pair, and the single-note form.</returns>
    public static (string OuterStart, string OuterEnd,
        string InnerStart, string InnerEnd, string Single) Grace(
        string name, InsertDirection direction)
    {
        string d = DirectionOperator(direction);
        return name switch
        {
            "grace_grace" => ("\\grace { ", " }", string.Empty, string.Empty,
                "\\grace "),
            "grace_beam" => ("\\grace { ", " }", d + "[", "]", string.Empty),
            "grace_accia" => ("\\acciaccatura { ", " }", string.Empty, string.Empty,
                "\\acciaccatura "),
            "grace_appog" => ("\\appoggiatura { ", " }", string.Empty, string.Empty,
                "\\appoggiatura "),
            "grace_slash" => ("\\slashedGrace { ", " }", d + "[", "]", string.Empty),
            "grace_after" => ("\\afterGrace ", " }", d + "{ ", string.Empty,
                string.Empty),
            _ => (string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty),
        };
    }

    /// <summary>Gets the bar-line command a button inserts.</summary>
    /// <param name="glyph">The bar-line glyph.</param>
    /// <returns>The command.</returns>
    public static string BarLineText(string glyph) => $"\\bar \"{glyph}\"";

    /// <summary>Gets the breathing-sign text a button inserts.</summary>
    /// <param name="name">The button name.</param>
    /// <returns>The text, and whether it wants a blank line before it.</returns>
    public static (string Text, bool BlankLine) BreatheText(string name)
    {
        if (string.Equals(name, "breathe_rcomma", StringComparison.Ordinal))
        {
            return ("\\breathe", false);
        }

        string glyph = name.Substring("breathe_".Length).Replace('_', '.');
        return (
            "\\once \\override BreathingSign.text = "
            + $"#(make-musicglyph-markup \"scripts.{glyph}\")\n\\breathe",
            true);
    }

    /// <summary>
    /// Finds the arpeggio command last used above a position, so that
    /// inserting a different one writes the switch as well.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="offset">Where to look up from.</param>
    /// <returns>The command; <c>\arpeggioNormal</c> when there is none.</returns>
    public static string LastUsedArpeggioType(EditorDocument document, int offset)
    {
        DocumentEditorState state = DocumentEditorState.For(document);
        TextDocument store = document.Document;
        HashSet<string> types = new HashSet<string>(
            ArpeggioTypes.Values, StringComparer.Ordinal);
        DocumentLine line = store.GetLineByOffset(offset);

        while (line != null)
        {
            foreach (var token in TokenIter.Tokens(state.Highlighter, line.LineNumber))
            {
                if (types.Contains(token.Text)) { return token.Text; }
            }

            line = line.PreviousLine;
        }

        return "\\arpeggioNormal";
    }

    /// <summary>
    /// Gets the dynamic tokens already sitting at a position, so a second
    /// dynamic is written after them rather than through them.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The tokens.</returns>
    public static IReadOnlyList<Token> DynamicsAt(
        EditorDocument document, int offset)
    {
        DocumentEditorState state = DocumentEditorState.For(document);
        (_, _, Token[] right) = TokenIter.Partition(
            state.Highlighter, document.Document, offset);

        int count = 0;
        for (int i = 0; i < right.Length; i++)
        {
            if (right[i] is Lily.Dynamic)
            {
                count = i + 1;
            }
            else if (right[i] is not Space && right[i] is not Lily.Direction)
            {
                break;
            }
        }

        return right.Take(count).ToList();
    }

    private static Cursor MakeCursor(
        EditorDocument document,
        DocumentEditorState state,
        int selectionStart,
        int selectionEnd,
        bool hasSelection)
    {
        if (hasSelection)
        {
            return new Cursor(state.LyDocument, selectionStart, selectionEnd);
        }

        //With no selection the search runs to the end of the caret's line,
        //which is upstream's select_end_of_block().
        Cursor cursor = new Cursor(state.LyDocument, selectionStart, selectionStart);
        cursor.SelectEndOfBlock();
        return cursor;
    }
}
