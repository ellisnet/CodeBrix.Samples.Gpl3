// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit;
using CodeBrix.Platform.UI.AdvancedTextEdit.Folding;
using CodeBrix.Platform.UI.AdvancedTextEdit.Highlighting;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/view.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One editor showing one document.
/// <para>
/// A document can have several views at once — that is what splitting the
/// window does — and they all share the document's text store and its
/// tokenization (<see cref="DocumentEditorState"/>), so an edit or a
/// re-highlight in one is immediately the truth in the others. What a view
/// owns for itself is the visible part: its caret, its selection, its scroll
/// position and the highlights drawn over it.
/// </para>
/// </summary>
public sealed class EditorView : Grid
{
    /// <summary>The metainfo value the caret position is remembered in.</summary>
    public const string RememberedPositionName = "position";

    private FoldingManager _foldingManager;

    /// <summary>Creates a view of a document.</summary>
    /// <param name="document">The document to show.</param>
    /// <param name="state">The document's shared editor state.</param>
    /// <param name="editorFontFamily">The monospace font resource for the
    /// editor text, or null for the inherited font.</param>
    /// <param name="editorFontSize">The text size, or 0 for the default —
    /// what the Fonts &amp; Colors preferences page writes.</param>
    public EditorView(
        EditorDocument document,
        DocumentEditorState state,
        FontFamily editorFontFamily = null,
        double editorFontSize = 0)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        State = state ?? throw new ArgumentNullException(nameof(state));

        Editor = new AdvancedTextEdit
        {
            //The SAME text store the other views use: this is the whole point.
            Document = document.Document,
            ShowLineNumbers = true,
            FontSize = editorFontSize > 0
                ? editorFontSize
                : Fresco.Brix.Editor.TextFormatData.DefaultFontSize,
        };

        if (editorFontFamily != null)
        {
            Editor.FontFamily = editorFontFamily;
        }

        //Parity highlighting: the shared ly.lex tokenization drawn through the
        //editor's colorizer pipeline.
        Editor.TextArea.TextView.LineTransformers.Add(
            new HighlightingColorizer(state.Highlighter));

        Highlighter = new ViewHighlighter(Editor.TextArea.TextView);
        _foldingManager = FoldingManager.Install(Editor.TextArea);
        WordBoundary.Install(Editor.TextArea);
        CursorKeys.Install(Editor.TextArea, state.Settings);

        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            UpdateMatchHighlight();
            UpdateCurrentLineHighlight();

            //Remembered in memory on every move, written out when the document
            //closes — so reopening a file lands where the user left it.
            State.MetaInfo?.SetInt(RememberedPositionName, Editor.CaretOffset);
            CursorPositionChanged?.Invoke(this, EventArgs.Empty);
        };
        Editor.Document.TextChanged += (_, _) => RefreshFoldings();
        Editor.GotFocus += (_, _) => Focused?.Invoke(this, EventArgs.Empty);
        Editor.TextArea.SelectionChanged
            += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);

        //Two rows: the editor fills the view, and anything the tools put
        //BELOW it (the search bar) takes only the height it asks for. Upstream
        //gets the same with its BorderLayout.
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRow(Editor, 0);
        Children.Add(Editor);
        RefreshFoldings();
        RestoreRememberedPosition();
        UpdateCurrentLineHighlight();
    }

    /// <summary>Raised when the caret moves.</summary>
    public event EventHandler CursorPositionChanged;

    /// <summary>Raised when the view takes keyboard focus.</summary>
    public event EventHandler Focused;

    /// <summary>Raised when what is selected changes.</summary>
    /// <remarks>The commands that only make sense over a selection — Cut and
    /// Assign, the Quick Remove family — follow this. Upstream's window has
    /// the same signal for the same reason.</remarks>
    public event EventHandler SelectionChanged;

    /// <summary>Gets the document being shown.</summary>
    public EditorDocument Document { get; }

    /// <summary>Gets the document's shared editor state.</summary>
    public DocumentEditorState State { get; }

    /// <summary>Gets the editor control.</summary>
    public AdvancedTextEdit Editor { get; }

    /// <summary>Gets the highlights drawn over this view alone.</summary>
    public ViewHighlighter Highlighter { get; }

    /// <summary>Gets the folding manager for this view.</summary>
    public FoldingManager FoldingManager => _foldingManager;

    /// <summary>Gets the caret's line, from 1.</summary>
    public int Line => Editor.TextArea.Caret.Line;

    /// <summary>Gets the caret's column, from 1.</summary>
    public int Column => Editor.TextArea.Caret.Column;

    /// <summary>Puts the caret at a line and column and scrolls it into view.</summary>
    /// <param name="line">The line, from 1.</param>
    /// <param name="column">The column, from 1.</param>
    public void GoTo(int line, int column = 1)
    {
        Editor.CaretOffset = Document.OffsetAtPosition(line, column);
        Editor.ScrollTo(line, column);
    }

    /// <summary>Puts the caret at a character offset and scrolls it into view.</summary>
    /// <param name="offset">The offset from the start of the document.</param>
    public void GoToOffset(int offset)
    {
        Editor.CaretOffset = Math.Clamp(offset, 0, Editor.Document.TextLength);
        Editor.ScrollTo(Line, Column);
    }

    /// <summary>Gives the view keyboard focus.</summary>
    public void FocusEditor() => Editor.Focus(FocusState.Programmatic);

    /// <summary>Gets or sets the strip shown under the editor, or null.</summary>
    /// <remarks>The search bar lives here. Only one thing at a time occupies
    /// it, which is what upstream's BorderLayout gives it too.</remarks>
    public FrameworkElement BottomBar
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) { return; }

            if (field != null) { Children.Remove(field); }

            field = value;
            if (value == null) { return; }

            SetRow(value, 1);
            Children.Add(value);
        }
    }

    /// <summary>Gets the offset the selection starts at, or the caret.</summary>
    public int SelectionStart
        => Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;

    /// <summary>Gets the offset the selection ends at, or the caret.</summary>
    public int SelectionEnd
        => Editor.SelectionLength > 0
            ? Editor.SelectionStart + Editor.SelectionLength
            : Editor.CaretOffset;

    /// <summary>Gets whether anything is selected.</summary>
    public bool HasSelection => Editor.SelectionLength > 0;

    /// <summary>Gets the selected text, or the empty string.</summary>
    public string SelectedText
        => Editor.SelectionLength > 0 ? Editor.SelectedText : string.Empty;

    /// <summary>Selects a range and scrolls it into view.</summary>
    /// <param name="start">Where the range starts.</param>
    /// <param name="length">How long it is.</param>
    public void Select(int start, int length)
    {
        Editor.Select(start, length);
        Editor.CaretOffset = start + length;
        Editor.ScrollTo(Line, Column);
    }

    /// <summary>Recomputes what can be folded.</summary>
    public void RefreshFoldings()
        => State.Folding.UpdateFoldings(_foldingManager, Editor.Document);

    /// <summary>Shows or hides the folding margin.</summary>
    /// <param name="enabled">Whether folding is on.</param>
    /// <remarks>Turning folding off unfolds everything first: a region left
    /// folded with no margin to unfold it by would hide text for good.</remarks>
    public void SetFoldingEnabled(bool enabled)
    {
        if (enabled == (_foldingManager != null)) { return; }

        if (enabled)
        {
            _foldingManager = FoldingManager.Install(Editor.TextArea);
            RefreshFoldings();
            return;
        }

        LyFoldingStrategy.UnfoldAll(_foldingManager);
        FoldingManager.Uninstall(_foldingManager);
        _foldingManager = null;
    }

    /// <summary>
    /// Puts the caret where it was when the document was last closed.
    /// </summary>
    /// <remarks>Upstream restores this from the document's metainfo the moment
    /// a view is made for it; a document with nothing remembered opens at its
    /// start, which is where a new view already is.</remarks>
    public void RestoreRememberedPosition()
    {
        int offset = State.MetaInfo?.GetInt(RememberedPositionName) ?? 0;
        if (offset <= 0 || offset > Editor.Document.TextLength) { return; }

        Editor.CaretOffset = offset;
        Editor.ScrollTo(Line, Column);
    }

    private void UpdateMatchHighlight()
    {
        var ranges = TokenMatcher.Matches(State.LyDocument, Editor.TextArea.Caret.Offset);
        Highlighter.Highlight(
            HighlightGroups.Match,
            ranges.Select(r => (r.Start, r.Length)),
            Color.FromArgb(0x60, 0x99, 0xdd, 0x77),
            HighlightGroups.PriorityOf(HighlightGroups.Match),
            fullWidth: false,
            borderColor: Color.FromArgb(0xa0, 0x44, 0x88, 0x22));
    }

    private void UpdateCurrentLineHighlight()
    {
        var line = Editor.Document.GetLineByNumber(Editor.TextArea.Caret.Line);
        Highlighter.Highlight(
            HighlightGroups.CurrentLine,
            new[] { (line.Offset, Math.Max(line.Length, 1)) },
            Color.FromArgb(0x50, 0xff, 0xfc, 0x95),
            HighlightGroups.PriorityOf(HighlightGroups.CurrentLine),
            fullWidth: true);
    }
}
