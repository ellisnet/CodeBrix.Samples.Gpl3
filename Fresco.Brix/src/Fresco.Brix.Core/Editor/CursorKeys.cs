// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using CodeBrix.Platform.UI.AdvancedTextEdit.Editing;
using Fresco.Brix.Services;
using System;
using System.Linq;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/cursorkeys.py and gadgets/cursorkeys.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Home key with a little intelligence: it goes to the first non-blank
/// character of the line, and only to column one if the caret is already
/// there. On an indented LilyPond file that is where the user nearly always
/// means to go.
/// </summary>
/// <remarks>
/// Upstream's handler also offers "keep cursor in line" and "smart start/end"
/// for the vertical and horizontal keys. Those two settings default to off and
/// on respectively and change how Up/Down/Left/Right behave at line ends; they
/// arrive with their preference page in W12, which is where a user would find
/// them. The smart Home key defaults to ON, so it belongs with the editor.
/// </remarks>
public static class CursorKeys
{
    /// <summary>The setting deciding whether Home is the smart one.</summary>
    public const string SmartHomeSettingKey = "view_preferences/smart_home_key";

    /// <summary>
    /// Works out where Home should put the caret.
    /// </summary>
    /// <param name="document">The text store.</param>
    /// <param name="offset">Where the caret is.</param>
    /// <returns>Where it should go.</returns>
    public static int SmartHome(TextDocument document, int offset)
    {
        DocumentLine line = document.GetLineByOffset(
            Math.Clamp(offset, 0, document.TextLength));
        string text = document.GetText(line.Offset, line.Length);

        int indent = 0;
        while (indent < text.Length && char.IsWhiteSpace(text[indent]))
        {
            indent++;
        }

        //A line that is nothing but blanks has no "first real character", so
        //Home takes the caret to column one and stays there.
        if (indent == text.Length) { indent = 0; }

        int firstReal = line.Offset + indent;
        return offset == firstReal ? line.Offset : firstReal;
    }

    /// <summary>
    /// Gives an editor the smart Home key.
    /// </summary>
    /// <param name="textArea">The editor's text area.</param>
    /// <param name="settings">The settings store, or null for the default.</param>
    public static void Install(TextArea textArea, SettingsStore settings = null)
    {
        if (textArea?.DefaultInputHandler?.CaretNavigation == null) { return; }

        if (!(settings?.GetBool(SmartHomeSettingKey, true) ?? true)) { return; }

        var bindings = textArea.DefaultInputHandler.CaretNavigation.CommandBindings;
        foreach (var binding in bindings
            .Where(b => b.Command == EditorCommands.MoveToLineStart
                || b.Command == EditorCommands.SelectToLineStart).ToList())
        {
            bindings.Remove(binding);
        }

        bindings.Add(new EditorCommandBinding(EditorCommands.MoveToLineStart,
            (_, e) => Home(textArea, e, select: false)));
        bindings.Add(new EditorCommandBinding(EditorCommands.SelectToLineStart,
            (_, e) => Home(textArea, e, select: true)));
    }

    private static void Home(
        TextArea textArea, ExecutedEditorCommandEventArgs e, bool select)
    {
        TextDocument document = textArea.Document;
        if (document == null) { return; }

        int from = textArea.Caret.Offset;
        int to = SmartHome(document, from);

        if (select)
        {
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
}
