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
using System.IO;
using System.Linq;
using System.Text;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/cut_assign.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Takes the selected music out of where it is and gives it a name: the
/// selection becomes <c>name = { … }</c> near the top of the document, and
/// what is left behind is <c>\name</c>.
/// </summary>
public static class CutAssign
{
    /// <summary>
    /// Cuts a selection and assigns it to a variable.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="selectionStart">The offset the selection starts at.</param>
    /// <param name="selectionEnd">The offset it ends at.</param>
    /// <returns>Whether anything was cut.</returns>
    public static bool Assign(
        EditorDocument document, string name, int selectionStart, int selectionEnd)
    {
        if (document == null || string.IsNullOrEmpty(name)) { return false; }

        DocumentEditorState state = DocumentEditorState.For(document);
        AteLyDocument bridge = state?.LyDocument;
        if (bridge == null) { return false; }

        TextDocument store = document.Document;

        //Whitespace at either end of the selection belongs to what stays, not
        //to what is named.
        Cursor cursor = new Cursor(bridge, selectionStart, selectionEnd);
        cursor.Strip();
        int start = cursor.Start;
        int end = cursor.End ?? selectionEnd;
        if (end <= start) { return false; }

        string text = store.GetText(start, end - start);
        string mode = ModeSuffix(state, store, start);
        int insertAt = InsertionOffset(state, store, start);
        string separator = text.Contains('\n') ? "\n" : " ";
        string assignment = string.Concat(
            name, " =", mode, " {", separator, text, separator, "}\n\n");

        //One undo group: the reference and the definition are one edit, and
        //the later offset is written first so the earlier one stays valid.
        store.BeginUpdate();
        try
        {
            store.Replace(start, end - start, "\\" + name);
            store.Insert(insertAt, assignment);
        }
        finally
        {
            store.EndUpdate();
        }

        if (state.MetaInfo?.GetBool(DocumentActions.AutoIndentName) ?? true)
        {
            Indenting.ReIndent(
                bridge,
                Indenting.CreateIndenter(state.Settings, document.Text),
                insertAt,
                insertAt + assignment.Length);
        }

        return true;
    }

    /// <summary>
    /// Works out the file a selection would go into and the
    /// <c>\include</c> line that would replace it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="selectionStart">The offset the selection starts at.</param>
    /// <param name="selectionEnd">The offset it ends at.</param>
    /// <returns>The suggested path and the text to write, or null when there
    /// is nothing selected.</returns>
    /// <remarks>The dialog and the write itself belong to the window; what is
    /// here is the part that has to know about LilyPond — the mode the text is
    /// in, the extension that goes with it, and the <c>\version</c> header a
    /// standalone include file needs.</remarks>
    public static IncludeFileProposal ProposeIncludeFile(
        EditorDocument document, int selectionStart, int selectionEnd)
    {
        if (document == null || selectionEnd <= selectionStart) { return null; }

        string text = document.Text.Substring(
            selectionStart, selectionEnd - selectionStart);
        string mode = LyFileInfo.TextMode(text);
        string directory = document.Path == null
            ? null
            : Path.GetDirectoryName(document.Path);
        string baseName = document.Path == null
            ? document.DocumentName()
            : Path.GetFileNameWithoutExtension(document.Path);
        string extension = document.Path == null
            ? null
            : Path.GetExtension(document.Path);

        if (string.IsNullOrEmpty(extension)
            || string.Equals(mode, "lilypond", StringComparison.Ordinal))
        {
            extension = ".ily";
            string version = DocumentInfo.For(document).DocInfo().VersionString();
            if (!string.IsNullOrEmpty(version))
            {
                text = $"\\version \"{version}\"\n\n{text}";
            }
        }

        string fileName = baseName + "-include" + extension;
        return new IncludeFileProposal(
            directory == null ? fileName : Path.Combine(directory, fileName),
            text,
            mode);
    }

    /// <summary>Gets the <c>\include</c> line for a written file.</summary>
    /// <param name="document">The document the line goes into.</param>
    /// <param name="path">The file that was written.</param>
    /// <returns>The line, ending in a newline.</returns>
    public static string IncludeCommand(EditorDocument document, string path)
    {
        string directory = document?.Path == null
            ? null
            : Path.GetDirectoryName(document.Path);
        string relative = directory == null
            ? Path.GetFileName(path)
            : Path.GetRelativePath(directory, path);
        return $"\\include \"{relative}\"\n";
    }

    /// <summary>
    /// Gets the input-mode suffix the assignment needs, so that cutting lyrics
    /// out of a <c>\lyricmode</c> block gives a variable that is still lyrics.
    /// </summary>
    /// <param name="state">The document's editor state.</param>
    /// <param name="store">The text store.</param>
    /// <param name="offset">Where the selection starts.</param>
    /// <returns>The suffix, or the empty string.</returns>
    public static string ModeSuffix(
        DocumentEditorState state, TextDocument store, int offset)
    {
        DocumentLine line = store.GetLineByOffset(offset);
        State lexState = TokenIter.StateAt(state.Highlighter, line.LineNumber);
        if (lexState == null) { return string.Empty; }

        //The stored state is the one at the START of the line; following the
        //tokens to the left of the cursor brings it up to the cursor itself.
        (Token[] left, _, _) = TokenIter.Partition(state.Highlighter, store, offset);
        foreach (var token in left)
        {
            lexState.Follow(token);
        }

        foreach (var parser in lexState.Parsers())
        {
            if (parser is not Lily.ParseInputMode) { continue; }

            return parser switch
            {
                Lily.ParseLyricMode => " \\lyricmode",
                Lily.ParseChordMode => " \\chordmode",
                Lily.ParseFigureMode => " \\figuremode",
                Lily.ParseDrumMode => " \\drummode",
                _ => string.Empty,
            };
        }

        return string.Empty;
    }

    /// <summary>
    /// Finds where the assignment goes: the first line above the selection
    /// that is at the document's top level, or that begins an assignment of
    /// its own.
    /// </summary>
    /// <param name="state">The document's editor state.</param>
    /// <param name="store">The text store.</param>
    /// <param name="offset">Where the selection starts.</param>
    /// <returns>The offset to insert at.</returns>
    public static int InsertionOffset(
        DocumentEditorState state, TextDocument store, int offset)
    {
        DocumentLine line = store.GetLineByOffset(offset);
        while (line.PreviousLine != null)
        {
            line = line.PreviousLine;
            State lexState = TokenIter.StateAt(state.Highlighter, line.LineNumber);
            if (lexState?.CurrentParser() is Lily.ParseGlobal)
            {
                return line.Offset;
            }

            foreach (var token in TokenIter.Tokens(state.Highlighter, line.LineNumber))
            {
                if (token is Lily.Name) { return line.Offset; }

                if (token is not Space && token is not Comment) { break; }
            }
        }

        return line.Offset;
    }
}

/// <summary>What Move to Include File proposes.</summary>
public sealed class IncludeFileProposal
{
    /// <summary>Creates a proposal.</summary>
    /// <param name="path">The suggested file.</param>
    /// <param name="text">What to write into it.</param>
    /// <param name="mode">The ly mode the text is in.</param>
    public IncludeFileProposal(string path, string text, string mode)
    {
        Path = path;
        Text = text;
        Mode = mode;
    }

    /// <summary>Gets the suggested file.</summary>
    public string Path { get; }

    /// <summary>Gets what to write into it.</summary>
    public string Text { get; }

    /// <summary>Gets the ly mode the text is in.</summary>
    public string Mode { get; }

    /// <summary>Gets the text encoded for writing, with the platform's own
    /// line endings.</summary>
    /// <returns>The bytes.</returns>
    public byte[] EncodedText()
        => new UTF8Encoding(false).GetBytes(
            Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
}
