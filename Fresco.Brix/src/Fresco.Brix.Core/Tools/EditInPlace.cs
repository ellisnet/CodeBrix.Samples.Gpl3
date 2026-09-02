// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit;
using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/editinplace.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Edit in Place dialog: one line of a document, edited without leaving
/// where the user is — the Music View's own way of fixing a note.
/// </summary>
/// <remarks>
/// <para>
/// What comes back is written with <see cref="CursorDiff"/> rather than
/// replaced wholesale, so that the point-and-click anchors of the parts of the
/// line that did NOT change survive; that is the reason upstream reaches for
/// its own diff here rather than a plain insert.
/// </para>
/// <para>
/// Upstream's dialog carries its own highlighter, matcher and completer over a
/// throw-away document. That is a fair amount of machinery for one line, and
/// the pieces it needs are per-document here rather than per-view; the dialog
/// therefore edits plain text and the line is re-indented on the way back in,
/// which is what upstream does with it too.
/// </para>
/// </remarks>
public static class EditInPlace
{
    /// <summary>Puts the dialog in front of the user.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="document">The document.</param>
    /// <param name="offset">Where in it to edit.</param>
    /// <param name="settings">The settings store, or null.</param>
    /// <param name="editorFont">The monospace font the box uses.</param>
    /// <returns>Whether the document was changed.</returns>
    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        EditorDocument document,
        int offset,
        SettingsStore settings = null,
        FontFamily editorFont = null)
    {
        if (document == null) { return false; }

        TextDocument store = document.Document;
        DocumentLine line = store.GetLineByOffset(offset);
        string text = store.GetText(line.Offset, line.Length);

        //The indent is not part of what is edited: it belongs to the document's
        //shape and is put back by the re-indent on the way out.
        int indent = 0;
        while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t'))
        {
            indent++;
        }

        TextBox box = new TextBox
        {
            Text = text.Substring(indent),
            AcceptsReturn = false,
            MinWidth = 480,
            SelectionStart = Math.Max(0, offset - line.Offset - indent),
        };
        if (editorFont != null) { box.FontFamily = editorFont; }

        StackPanel panel = new StackPanel { Spacing = 6, MinWidth = 480 };
        panel.Children.Add(new TextBlock
        {
            Text = I18n.Format(
                I18n.Get("Editing line {linenum} of \"{document}\" ({variable})"),
                ("linenum", line.LineNumber.ToString(
                    System.Globalization.CultureInfo.CurrentCulture)),
                ("document", document.DocumentName()),
                ("variable", DocumentTooltip.Definition(document, offset)
                    ?? I18n.Get("<unknown>"))),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(box);

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("Edit in Place"),
            Content = panel,
            PrimaryButtonText = StandardButtons.Ok,
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) { return false; }

        return Apply(document, line.LineNumber, indent, box.Text, settings);
    }

    /// <summary>Writes an edited line back into the document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="lineNumber">The line, from 1.</param>
    /// <param name="indent">How many characters of indent to keep.</param>
    /// <param name="text">The edited text, without its indent.</param>
    /// <param name="settings">The settings store, or null.</param>
    /// <returns>Whether anything changed.</returns>
    public static bool Apply(
        EditorDocument document,
        int lineNumber,
        int indent,
        string text,
        SettingsStore settings = null)
    {
        DocumentEditorState state = DocumentEditorState.For(document, settings);
        TextDocument store = document.Document;
        DocumentLine line = store.GetLineByNumber(lineNumber);
        int start = line.Offset + indent;
        int length = Math.Max(0, line.EndOffset - start);
        string current = store.GetText(start, length);
        if (string.Equals(current, text, StringComparison.Ordinal)) { return false; }

        store.BeginUpdate();
        try
        {
            //A diffed write leaves the untouched parts of the line — and the
            //anchors over them — exactly where they were.
            CursorDiff.Replace(store, start, length, text);

            DocumentLine written = store.GetLineByNumber(lineNumber);
            Indenting.ReIndent(
                state.LyDocument,
                Indenting.CreateIndenter(settings, document.Text),
                written.Offset,
                written.EndOffset);
        }
        finally
        {
            store.EndUpdate();
        }

        return true;
    }
}
