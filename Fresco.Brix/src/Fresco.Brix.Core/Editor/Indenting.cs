// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Lex;
using Fresco.Brix.Services;
using System.Collections.Generic;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/indent.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The indentation preferences in force for a document: the application
/// settings, with the document's own variables allowed to override them, so a
/// file that says <c>indent-width: 4;</c> is indented that way whoever opens
/// it.
/// </summary>
public sealed class IndentPreferences
{
    /// <summary>Gets or sets whether to indent with tab characters.</summary>
    public bool IndentTabs { get; set; }

    /// <summary>Gets or sets how many spaces one indent step is.</summary>
    public int IndentWidth { get; set; } = 2;

    /// <summary>Gets or sets how wide a tab character is taken to be.</summary>
    public int TabWidth { get; set; } = 8;

    /// <summary>Gets or sets whether the document itself uses tabs.</summary>
    public bool DocumentTabs { get; set; }

    /// <summary>Gets or sets the document's own tab width.</summary>
    public int DocumentTabWidth { get; set; } = 8;

    /// <summary>Reads the preferences.</summary>
    /// <param name="settings">The settings store, or null for the defaults.</param>
    /// <param name="documentText">The document's text, so its own variables
    /// can override the settings; null to skip that.</param>
    /// <returns>The preferences.</returns>
    /// <remarks>
    /// Upstream stores <c>indent_spaces</c> and <c>document_spaces</c> where 0
    /// MEANS tabs, keeping the width that was in force; that encoding is kept
    /// so a settings file moves between the two applications unchanged.
    /// </remarks>
    public static IndentPreferences Read(
        SettingsStore settings = null, string documentText = null)
    {
        int indentSpaces = settings?.GetInt("indent/indent_spaces", 2) ?? 2;
        int documentSpaces = settings?.GetInt("indent/document_spaces", 8) ?? 8;

        IndentPreferences preferences = new IndentPreferences
        {
            IndentTabs = indentSpaces == 0,
            IndentWidth = indentSpaces == 0 ? 2 : indentSpaces,
            TabWidth = settings?.GetInt("indent/tab_width", 8) ?? 8,
            DocumentTabs = documentSpaces == 0,
            DocumentTabWidth = documentSpaces == 0 ? 8 : documentSpaces,
        };

        if (documentText == null) { return preferences; }

        preferences.IndentTabs =
            DocumentVariables.GetBool(documentText, "indent-tabs", preferences.IndentTabs);
        preferences.IndentWidth =
            DocumentVariables.GetInt(documentText, "indent-width", preferences.IndentWidth);
        preferences.TabWidth =
            DocumentVariables.GetInt(documentText, "tab-width", preferences.TabWidth);
        preferences.DocumentTabs =
            DocumentVariables.GetBool(documentText, "document-tabs", preferences.DocumentTabs);
        preferences.DocumentTabWidth = DocumentVariables.GetInt(
            documentText, "document-tab-width", preferences.DocumentTabWidth);
        return preferences;
    }
}

/// <summary>
/// Indentation: the automatic indent applied as the user types, and the
/// explicit indent/unindent/re-indent commands, all driven by the ported
/// <see cref="Indenter"/> over the live editor document.
/// </summary>
public static class Indenting
{
    /// <summary>Builds an indenter set up for a document.</summary>
    /// <param name="settings">The settings store, or null.</param>
    /// <param name="documentText">The document text, or null.</param>
    /// <returns>The indenter.</returns>
    public static Indenter CreateIndenter(
        SettingsStore settings = null, string documentText = null)
    {
        IndentPreferences preferences = IndentPreferences.Read(settings, documentText);
        return new Indenter
        {
            IndentWidth = preferences.IndentWidth,
            IndentTabs = preferences.IndentTabs,
        };
    }

    /// <summary>Re-indents one line, as typing a closing brace should.</summary>
    /// <param name="lyDocument">The ly bridge over the editor document.</param>
    /// <param name="indenter">The indenter.</param>
    /// <param name="lineNumber">The line number, from 1.</param>
    public static void AutoIndentLine(
        AteLyDocument lyDocument, Indenter indenter, int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > lyDocument.Count) { return; }

        DocumentBlock block = lyDocument[lineNumber - 1];
        string current = indenter.GetIndent(lyDocument, block);

        //GetIndent answers null for a line the indenter will not touch (one
        //that starts inside a multi-line string or comment).
        if (current == null) { return; }

        string wanted = indenter.ComputeIndent(lyDocument, block);
        if (wanted == null || string.Equals(current, wanted, System.StringComparison.Ordinal))
        {
            return;
        }

        int start = lyDocument.Position(block);
        using (lyDocument.Writing())
        {
            lyDocument.SetText(start, start + current.Length, wanted);
        }
    }

    /// <summary>
    /// Says whether re-indenting the line makes sense at this position — the
    /// test run after a keystroke, so typing a brace re-indents but typing a
    /// note does not.
    /// </summary>
    /// <param name="highlighter">The document's highlighter.</param>
    /// <param name="document">The text store.</param>
    /// <param name="offset">The caret offset.</param>
    /// <returns>Whether to re-indent.</returns>
    public static bool IsIndentable(
        LyHighlighter highlighter, TextDocument document, int offset)
    {
        DocumentLine line = document.GetLineByOffset(offset);
        int position = offset - line.Offset;

        foreach (var token in TokenIter.Tokens(highlighter, line.LineNumber))
        {
            if (token.End >= position)
            {
                return token is IDedent || token is BlockCommentEnd;
            }

            //Anything before the caret other than space or a dedent means the
            //line has real content already, so leave its indent alone.
            if (!(token is Space) && !(token is IDedent))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Indents the line or selection one step further.</summary>
    /// <param name="lyDocument">The ly bridge over the editor document.</param>
    /// <param name="indenter">The indenter.</param>
    /// <param name="start">The selection start offset.</param>
    /// <param name="end">The selection end offset, or the start when there is
    /// no selection.</param>
    public static void IncreaseIndent(
        AteLyDocument lyDocument, Indenter indenter, int start, int end)
        => indenter.IncreaseIndent(new Cursor(lyDocument, start, end));

    /// <summary>Takes the line or selection back one indent step.</summary>
    /// <param name="lyDocument">The ly bridge over the editor document.</param>
    /// <param name="indenter">The indenter.</param>
    /// <param name="start">The selection start offset.</param>
    /// <param name="end">The selection end offset.</param>
    public static void DecreaseIndent(
        AteLyDocument lyDocument, Indenter indenter, int start, int end)
        => indenter.DecreaseIndent(new Cursor(lyDocument, start, end));

    /// <summary>Re-indents a whole region, or the whole document.</summary>
    /// <param name="lyDocument">The ly bridge over the editor document.</param>
    /// <param name="indenter">The indenter.</param>
    /// <param name="start">The region start offset.</param>
    /// <param name="end">The region end offset; equal to the start re-indents
    /// the whole document, as upstream's select_all does.</param>
    /// <param name="indentBlankLines">Whether to grow the indent of blank
    /// lines too.</param>
    public static void ReIndent(
        AteLyDocument lyDocument,
        Indenter indenter,
        int start,
        int end,
        bool indentBlankLines = false)
    {
        Cursor cursor = start == end
            ? new Cursor(lyDocument, 0, null)
            : new Cursor(lyDocument, start, end);
        if (start == end)
        {
            cursor.SelectAll();
        }

        indenter.Indent(cursor, indentBlankLines);
    }
}
