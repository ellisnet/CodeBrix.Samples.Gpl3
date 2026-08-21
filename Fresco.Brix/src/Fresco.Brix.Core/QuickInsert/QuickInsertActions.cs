// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.QuickInsert;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What pressing a Quick Insert button actually writes into the document.
/// </summary>
/// <remarks>
/// Upstream spreads this over four <c>ButtonGroup</c> subclasses, one per
/// page, each with its own <c>actionTriggered</c>. Here the button's NAME is
/// enough to say what to do, so the four are one dispatcher — which is also
/// what lets a keyboard shortcut reach a button whose page has never been
/// opened.
/// </remarks>
public static class QuickInsertActions
{
    /// <summary>Performs the insertion a button asks for.</summary>
    /// <param name="document">The document.</param>
    /// <param name="name">The button name.</param>
    /// <param name="selectionStart">Where the selection starts.</param>
    /// <param name="selectionEnd">Where it ends, or the same as the start.</param>
    /// <param name="direction">Which way the sign points.</param>
    /// <param name="allowShorthands">Whether short articulation forms are on.</param>
    /// <param name="settings">The settings store, or null.</param>
    public static void Insert(
        EditorDocument document,
        string name,
        int selectionStart,
        int selectionEnd,
        InsertDirection direction,
        bool allowShorthands,
        SettingsStore settings = null)
    {
        if (document == null || string.IsNullOrEmpty(name)) { return; }

        if (name.StartsWith("articulation_", StringComparison.Ordinal))
        {
            InsertArticulation(
                document,
                name.Substring("articulation_".Length),
                selectionStart, selectionEnd, direction, allowShorthands);
            return;
        }

        if (name.StartsWith("dynamic_", StringComparison.Ordinal))
        {
            InsertDynamic(
                document, name.Substring("dynamic_".Length),
                selectionStart, selectionEnd, direction);
            return;
        }

        if (name.StartsWith("spanner_", StringComparison.Ordinal))
        {
            InsertSpanner(document, name, selectionStart, selectionEnd, direction);
            return;
        }

        if (name.StartsWith("arpeggio_", StringComparison.Ordinal))
        {
            InsertArpeggio(document, name, selectionStart, selectionEnd, settings);
            return;
        }

        if (name.StartsWith("glissando_", StringComparison.Ordinal))
        {
            InsertAfterFirstItem(
                document, QuickInsertLogic.GlissandoText(name),
                selectionStart, selectionEnd);
            return;
        }

        if (name.StartsWith("grace_", StringComparison.Ordinal))
        {
            InsertGrace(
                document, name, selectionStart, selectionEnd, direction, settings);
            return;
        }

        if (name.StartsWith("bar_", StringComparison.Ordinal))
        {
            InsertBarLine(document, name, selectionStart, settings);
            return;
        }

        if (name.StartsWith("breathe_", StringComparison.Ordinal))
        {
            (string text, bool blankLine) = QuickInsertLogic.BreatheText(name);
            InsertText(document, selectionStart, text, blankLine, settings);
        }
    }

    private static void InsertArticulation(
        EditorDocument document,
        string articulation,
        int selectionStart,
        int selectionEnd,
        InsertDirection direction,
        bool allowShorthands)
    {
        string text = QuickInsertLogic.ArticulationText(
            articulation, direction, allowShorthands);
        IReadOnlyList<int> positions = QuickInsertLogic.ArticulationPositions(
            document, selectionStart, selectionEnd);
        if (positions.Count == 0)
        {
            //With nothing to attach to, the sign goes in where the caret is —
            //which is what upstream falls back to.
            if (selectionEnd <= selectionStart)
            {
                document.Document.Insert(selectionStart, text);
            }

            return;
        }

        InsertAt(document, positions, text);
    }

    private static void InsertDynamic(
        EditorDocument document,
        string dynamic,
        int selectionStart,
        int selectionEnd,
        InsertDirection direction)
    {
        string operatorText = QuickInsertLogic.DirectionOperator(direction);
        bool isSpanner = QuickInsertLogic.DynamicSpanners.ContainsKey(dynamic);
        string text = isSpanner
            ? QuickInsertLogic.DynamicSpanners[dynamic]
            : "\\" + dynamic;

        if (selectionEnd <= selectionStart)
        {
            IReadOnlyList<int> positions = QuickInsertLogic.ArticulationPositions(
                document, selectionStart, selectionStart);
            int at = positions.Count > 0 ? positions[0] : selectionStart;
            document.Document.Insert(at, operatorText + text);
            return;
        }

        IReadOnlyList<int> spanned = QuickInsertLogic.SpannerPositions(
            document, selectionStart, selectionEnd);
        if (spanned.Count == 0) { return; }

        TextDocument store = document.Document;
        store.BeginUpdate();
        try
        {
            //Back to front: the later offset is written first so the earlier
            //one stays where the reader found it.
            if (isSpanner && spanned.Count > 1)
            {
                //A spanner needs terminating, unless a dynamic already does it.
                if (QuickInsertLogic.DynamicsAt(document, spanned[^1]).Count == 0)
                {
                    store.Insert(spanned[^1], "\\!");
                }
            }

            store.Insert(spanned[0], operatorText + text);
        }
        finally
        {
            store.EndUpdate();
        }
    }

    private static void InsertSpanner(
        EditorDocument document,
        string name,
        int selectionStart,
        int selectionEnd,
        InsertDirection direction)
    {
        (string start, string end) = QuickInsertLogic.Spanner(name, direction);
        IReadOnlyList<int> positions = QuickInsertLogic.SpannerPositions(
            document, selectionStart, selectionEnd);
        if (positions.Count == 0) { return; }

        TextDocument store = document.Document;
        store.BeginUpdate();
        try
        {
            if (positions.Count > 1) { store.Insert(positions[1], end); }

            store.Insert(positions[0], start);
        }
        finally
        {
            store.EndUpdate();
        }
    }

    private static void InsertArpeggio(
        EditorDocument document,
        string name,
        int selectionStart,
        int selectionEnd,
        SettingsStore settings)
    {
        if (!QuickInsertLogic.ArpeggioTypes.TryGetValue(name, out string wanted))
        {
            return;
        }

        IReadOnlyList<int> positions = QuickInsertLogic.ArticulationPositions(
            document, selectionStart, selectionStart);
        if (positions.Count == 0) { return; }

        string lastUsed = QuickInsertLogic.LastUsedArpeggioType(
            document, selectionStart);
        TextDocument store = document.Document;
        int at = positions[0];

        store.BeginUpdate();
        try
        {
            store.Insert(at, "\\arpeggio");
            if (string.Equals(wanted, lastUsed, StringComparison.Ordinal)) { return; }

            //A different arpeggio type needs the switch written above the
            //chord, indented like the line it goes before.
            DocumentLine line = store.GetLineByOffset(at);
            string text = store.GetText(line.Offset, line.Length);
            int indent = 0;
            while (indent < text.Length
                && (text[indent] == ' ' || text[indent] == '\t'))
            {
                indent++;
            }

            store.Insert(
                line.Offset,
                wanted + "\n" + text.Substring(0, indent));
        }
        finally
        {
            store.EndUpdate();
        }
    }

    private static void InsertGrace(
        EditorDocument document,
        string name,
        int selectionStart,
        int selectionEnd,
        InsertDirection direction,
        SettingsStore settings)
    {
        var (outerStart, outerEnd, innerStart, innerEnd, single)
            = QuickInsertLogic.Grace(name, direction);
        TextDocument store = document.Document;

        if (selectionEnd > selectionStart)
        {
            store.BeginUpdate();
            try
            {
                if (innerEnd.Length > 0) { store.Insert(selectionEnd, innerEnd); }

                store.Insert(selectionEnd, outerEnd);
                store.Insert(selectionStart, outerStart);
                if (innerStart.Length > 0)
                {
                    store.Insert(selectionStart + outerStart.Length, innerStart);
                }
            }
            finally
            {
                store.EndUpdate();
            }

            return;
        }

        if (single.Length > 0)
        {
            store.Insert(selectionStart, single);
            return;
        }

        //No selection and no single-note form: the wrapper closes after the
        //THIRD music item, which is upstream's rule for \afterGrace.
        IReadOnlyList<int> items = QuickInsertLogic.SpannerPositions(
            document, selectionStart, selectionStart);
        int end = items.Count > 1 ? items[^1] : store.GetLineByOffset(selectionStart).EndOffset;

        store.BeginUpdate();
        try
        {
            store.Insert(end, outerEnd);
            store.Insert(selectionStart, outerStart + innerStart);
        }
        finally
        {
            store.EndUpdate();
        }
    }

    private static void InsertBarLine(
        EditorDocument document, string name, int offset, SettingsStore settings)
    {
        var entry = QuickInsertPanel.BarLines.FirstOrDefault(
            b => string.Equals(b.Name, name, StringComparison.Ordinal));
        if (entry.Name == null) { return; }

        int[] version = DocumentInfo.For(document).DocInfo().Version();
        bool old = version != null && version.Length >= 2
            && (version[0] < 2 || (version[0] == 2 && version[1] < 18));
        string glyph = old ? entry.Old ?? entry.New : entry.New;
        InsertText(document, offset, QuickInsertLogic.BarLineText(glyph), false, settings);
    }

    private static void InsertAfterFirstItem(
        EditorDocument document, string text, int selectionStart, int selectionEnd)
    {
        IReadOnlyList<int> positions = QuickInsertLogic.ArticulationPositions(
            document, selectionStart, selectionStart);
        if (positions.Count == 0) { return; }

        document.Document.Insert(positions[0], text);
    }

    private static void InsertAt(
        EditorDocument document, IReadOnlyList<int> positions, string text)
    {
        TextDocument store = document.Document;
        store.BeginUpdate();
        try
        {
            //Back to front, so the earlier offsets stay valid.
            foreach (var at in positions.OrderByDescending(p => p))
            {
                store.Insert(at, text);
            }
        }
        finally
        {
            store.EndUpdate();
        }
    }

    private static void InsertText(
        EditorDocument document,
        int offset,
        string text,
        bool blankLineBefore,
        SettingsStore settings)
    {
        TextDocument store = document.Document;
        DocumentLine line = store.GetLineByOffset(offset);
        string before = store.GetText(line.Offset, offset - line.Offset);
        if (blankLineBefore && before.Trim().Length > 0) { text = "\n" + text; }

        store.BeginUpdate();
        try
        {
            store.Insert(offset, text);
            if (!text.Contains('\n')) { return; }

            DocumentEditorState state = DocumentEditorState.For(document, settings);
            Indenting.ReIndent(
                state.LyDocument,
                Indenting.CreateIndenter(settings, document.Text),
                line.Offset,
                Math.Min(offset + text.Length, store.TextLength));
        }
        finally
        {
            store.EndUpdate();
        }
    }
}
