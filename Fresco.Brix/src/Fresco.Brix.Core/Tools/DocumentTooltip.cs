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
using Fresco.Brix.Services;
using System;
using System.Globalization;
using System.Linq;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using MusicTree = Fresco.Brix.Ly.Music.Document;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/documenttooltip.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What a tooltip says about a place in a document: the file, the line and
/// column, the variable the music there is assigned to, and where in the piece
/// it falls.
/// </summary>
public static class DocumentTooltip
{
    /// <summary>How many lines a preview shows when nothing is selected.</summary>
    public const int DefaultLineCount = 6;

    /// <summary>Gets the tooltip text for a place in a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The text.</returns>
    public static string Text(EditorDocument document, int offset)
    {
        if (document == null) { return string.Empty; }

        TextDocument store = document.Document;
        DocumentLine line = store.GetLineByOffset(offset);
        string text = string.Format(
            CultureInfo.CurrentCulture,
            "{0} ({1}:{2})",
            document.DocumentName(),
            line.LineNumber,
            offset - line.Offset);

        string definition = Definition(document, offset);
        if (definition != null) { text += "\n" + definition; }

        string position = TimePosition(document, offset);
        if (position != null)
        {
            text += "\n" + I18n.Format(
                I18n.Get("Position: {pos}"), ("pos", position));
        }

        return text;
    }

    /// <summary>
    /// Gets the variable the music at a place is assigned to, or
    /// <c>\score</c> when it is inside one.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The name, or null.</returns>
    public static string Definition(EditorDocument document, int offset)
    {
        DocumentEditorState state = DocumentEditorState.For(document);
        if (state == null) { return null; }

        TextDocument store = document.Document;
        DocumentLine line = store.GetLineByOffset(offset);

        //Walk up to the nearest line that is at the document's TOP level; the
        //first name or \score there is what the music belongs to.
        while (line != null)
        {
            if (TokenIter.StateAt(state.Highlighter, line.LineNumber)?.CurrentParser()
                is Lily.ParseGlobal)
            {
                foreach (var token in TokenIter
                    .Tokens(state.Highlighter, line.LineNumber).Take(2))
                {
                    if (token.GetType() == typeof(Lily.Name)) { return token.Text; }

                    if (token is Lily.Keyword
                        && string.Equals(token.Text, "\\score", StringComparison.Ordinal))
                    {
                        return "\\score";
                    }
                }
            }

            line = line.PreviousLine;
        }

        return null;
    }

    /// <summary>Gets where in the piece a place falls, as <c>5/1</c>.</summary>
    /// <param name="document">The document.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The position, or null.</returns>
    public static string TimePosition(EditorDocument document, int offset)
    {
        MusicTree music = DocumentInfo.For(document)?.Music();
        Fraction? position = music?.TimePosition(offset);
        return position == null ? null : Durations.FormatFraction(position.Value);
    }

    /// <summary>
    /// Gets the text a preview tooltip shows: the selected lines, or the next
    /// few from the caret's own line.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="selectionStart">Where the selection starts.</param>
    /// <param name="selectionEnd">Where it ends, or the same as the start.</param>
    /// <param name="lineCount">How many lines to show without a selection.</param>
    /// <returns>The text.</returns>
    public static string PreviewText(
        EditorDocument document,
        int selectionStart,
        int selectionEnd,
        int lineCount = DefaultLineCount)
    {
        if (document == null) { return string.Empty; }

        TextDocument store = document.Document;
        DocumentLine first = store.GetLineByOffset(selectionStart);
        DocumentLine last = selectionEnd > selectionStart
            ? store.GetLineByOffset(selectionEnd)
            : LineAfter(first, lineCount - 1);

        int start = first.Offset;
        int end = Math.Min(last.EndOffset, store.TextLength);
        return end <= start ? string.Empty : store.GetText(start, end - start);
    }

    private static DocumentLine LineAfter(DocumentLine line, int count)
    {
        for (int i = 0; i < count && line.NextLine != null; i++)
        {
            line = line.NextLine;
        }

        return line;
    }
}
