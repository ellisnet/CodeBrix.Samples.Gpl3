// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using CodeBrix.Platform.UI.AdvancedTextEdit.Editing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/wordboundary.py and gadgets/wordboundary.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Word-at-a-time cursor movement that knows what a word is in LilyPond
/// source: <c>\relative</c> is one word, not a backslash followed by one;
/// <c>-\markup</c> and <c>^\markup</c> likewise; and hyphenated words such as
/// <c>page-breaking</c> hold together.
/// <para>
/// The behaviour is the editor's word navigation — Ctrl+Left/Right and the
/// selecting variants — so it applies wherever the user moves by words. Word
/// DELETE keeps the editor's own boundaries: upstream leaves it alone too.
/// </para>
/// </summary>
public static class WordBoundary
{
    /// <summary>
    /// What counts as a word. Upstream's LilyPond-specific expression: an
    /// optional direction character and backslash before a word, hyphenated
    /// words as one, a doubled backslash, and the zero-width line ends.
    /// </summary>
    public static readonly Regex WordRegex = new Regex(
        @"([-^_]?\\)?\w+(-\w+)*|\\\\|^|$", RegexOptions.Compiled);

    /// <summary>Finds the words in a line of text.</summary>
    /// <param name="text">The line's text.</param>
    /// <returns>The start and end of each word, in order.</returns>
    public static IReadOnlyList<(int Start, int End)> Boundaries(string text)
        => WordRegex.Matches(text ?? string.Empty)
            .Select(m => (m.Index, m.Index + m.Length))
            .ToList();

    /// <summary>
    /// Finds the start of the word an offset is inside, or -1 when the offset
    /// is not inside one.
    /// </summary>
    /// <param name="document">The text store.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The offset of the word's start, or -1.</returns>
    public static int StartOfWord(TextDocument document, int offset)
    {
        DocumentLine line = LineAt(document, offset);
        int position = offset - line.Offset;
        if (position == 0) { return -1; }

        foreach (var (start, end) in Boundaries(LineText(document, line)).Reverse())
        {
            if (start >= position) { continue; }

            //A word that ended before the cursor is not the cursor's word.
            return end < position ? -1 : line.Offset + start;
        }

        return -1;
    }

    /// <summary>
    /// Finds the end of the word an offset is inside, or -1 when the offset is
    /// not inside one.
    /// </summary>
    /// <param name="document">The text store.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The offset of the word's end, or -1.</returns>
    public static int EndOfWord(TextDocument document, int offset)
    {
        DocumentLine line = LineAt(document, offset);
        int position = offset - line.Offset;

        foreach (var (start, end) in Boundaries(LineText(document, line)))
        {
            if (end <= position) { continue; }

            return start > position ? -1 : line.Offset + end;
        }

        return -1;
    }

    /// <summary>
    /// Finds where the cursor lands moving one word left, crossing into
    /// earlier lines when the current one runs out.
    /// </summary>
    /// <param name="document">The text store.</param>
    /// <param name="offset">The offset to move from.</param>
    /// <returns>The new offset; 0 at the start of the document.</returns>
    public static int PreviousWord(TextDocument document, int offset)
    {
        DocumentLine line = LineAt(document, offset);
        int position = offset - line.Offset;

        while (true)
        {
            List<int> starts = Boundaries(LineText(document, line))
                .Select(b => b.Start).TakeWhile(s => s < position).ToList();
            if (starts.Count > 0)
            {
                return line.Offset + starts[starts.Count - 1];
            }

            if (line.LineNumber <= 1) { return 0; }

            line = document.GetLineByNumber(line.LineNumber - 1);
            position = line.Length + 1;
        }
    }

    /// <summary>
    /// Finds where the cursor lands moving one word right, crossing into later
    /// lines when the current one runs out.
    /// </summary>
    /// <param name="document">The text store.</param>
    /// <param name="offset">The offset to move from.</param>
    /// <returns>The new offset; the end of the document when there is no
    /// further word.</returns>
    public static int NextWord(TextDocument document, int offset)
    {
        DocumentLine line = LineAt(document, offset);
        int position = offset - line.Offset;

        while (true)
        {
            List<int> starts = Boundaries(LineText(document, line))
                .Select(b => b.Start).SkipWhile(s => s <= position).ToList();
            if (starts.Count > 0)
            {
                return line.Offset + starts[0];
            }

            if (line.LineNumber >= document.LineCount) { return document.TextLength; }

            line = document.GetLineByNumber(line.LineNumber + 1);
            position = -1;
        }
    }

    /// <summary>
    /// Replaces the editor's word navigation with this one.
    /// </summary>
    /// <param name="textArea">The editor's text area.</param>
    /// <remarks>
    /// The built-in bindings for the four word-movement commands are removed
    /// first: the handler answers the FIRST binding that can run, so a binding
    /// merely added alongside would never be reached.
    /// </remarks>
    public static void Install(TextArea textArea)
    {
        if (textArea?.DefaultInputHandler?.CaretNavigation == null) { return; }

        var bindings = textArea.DefaultInputHandler.CaretNavigation.CommandBindings;
        EditorCommand[] replaced =
        {
            EditorCommands.MoveLeftByWord,
            EditorCommands.MoveRightByWord,
            EditorCommands.SelectLeftByWord,
            EditorCommands.SelectRightByWord,
        };

        foreach (var binding in bindings
            .Where(b => replaced.Contains(b.Command)).ToList())
        {
            bindings.Remove(binding);
        }

        bindings.Add(new EditorCommandBinding(EditorCommands.MoveLeftByWord,
            (_, e) => Move(textArea, e, forward: false, select: false)));
        bindings.Add(new EditorCommandBinding(EditorCommands.MoveRightByWord,
            (_, e) => Move(textArea, e, forward: true, select: false)));
        bindings.Add(new EditorCommandBinding(EditorCommands.SelectLeftByWord,
            (_, e) => Move(textArea, e, forward: false, select: true)));
        bindings.Add(new EditorCommandBinding(EditorCommands.SelectRightByWord,
            (_, e) => Move(textArea, e, forward: true, select: true)));
    }

    private static void Move(
        TextArea textArea, ExecutedEditorCommandEventArgs e, bool forward, bool select)
    {
        TextDocument document = textArea.Document;
        if (document == null) { return; }

        int from = textArea.Caret.Offset;
        int to = forward ? NextWord(document, from) : PreviousWord(document, from);

        if (select)
        {
            //Extending a selection keeps the end the caret is NOT at.
            ISegment current = textArea.Selection.SurroundingSegment;
            int anchor = textArea.Selection.IsEmpty || current == null
                ? from
                : current.Offset == from ? current.EndOffset : current.Offset;
            textArea.Caret.Offset = to;
            textArea.Selection = Selection.Create(textArea, anchor, to);
        }
        else
        {
            textArea.ClearSelection();
            textArea.Caret.Offset = to;
        }

        textArea.Caret.BringCaretToView();
        e.Handled = true;
    }

    private static DocumentLine LineAt(TextDocument document, int offset)
        => document.GetLineByOffset(Math.Clamp(offset, 0, document.TextLength));

    private static string LineText(TextDocument document, DocumentLine line)
        => document.GetText(line.Offset, line.Length);
}
