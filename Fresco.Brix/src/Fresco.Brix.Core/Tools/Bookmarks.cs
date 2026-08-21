// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/bookmarks.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The marked lines of a document, remembered across sessions.
/// <para>
/// A mark is anchored rather than stored as a line number, so that inserting
/// text above a marked line moves the mark with it; the line NUMBERS are what
/// gets written to the metainfo when the document is saved or closed.
/// </para>
/// </summary>
public sealed class Bookmarks : Plugin<EditorDocument, Bookmarks>
{
    /// <summary>The kind of mark the user sets by hand.</summary>
    public const string MarkType = "mark";

    /// <summary>The kind of mark an engrave error leaves behind.</summary>
    public const string ErrorType = "error";

    /// <summary>The metainfo value the marks are stored in.</summary>
    public const string MetaInfoName = "bookmarks";

    /// <summary>The kinds of mark, in the order they are stored.</summary>
    public static readonly IReadOnlyList<string> Types = new[] { MarkType, ErrorType };

    private readonly Dictionary<string, List<ITextAnchor>> _marks
        = new Dictionary<string, List<ITextAnchor>>(StringComparer.Ordinal);

    private Bookmarks(EditorDocument document)
        : base(document)
    {
        foreach (var type in Types)
        {
            _marks[type] = new List<ITextAnchor>();
        }

        document.Loaded += (_, _) => Load();
        document.Saved += (_, _) => Save();
        document.Closed += (_, _) => Save();
        Load();
    }

    /// <summary>Raised when a mark is set, cleared or moved.</summary>
    public event EventHandler MarksChanged;

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the marks for a document, creating them on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The marks.</returns>
    public static Bookmarks For(EditorDocument document)
        => Instance(document, owner => new Bookmarks(owner));

    /// <summary>Declares the metainfo value the marks live in.</summary>
    public static void Define() => MetaInfo.Define(MetaInfoName, string.Empty);

    /// <summary>Gets the marked lines of one kind, from 0, in order.</summary>
    /// <param name="type">The kind, or null for every kind.</param>
    /// <returns>The line numbers.</returns>
    public IReadOnlyList<int> MarkedLines(string type = null)
        => AnchorsOf(type)
            .Select(LineOf)
            .Where(n => n >= 0)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

    /// <summary>Answers whether a line carries a mark.</summary>
    /// <param name="lineNumber">The line, from 0.</param>
    /// <param name="type">The kind, or null for any kind.</param>
    /// <returns>Whether it does.</returns>
    public bool HasMark(int lineNumber, string type = null)
        => AnchorsOf(type).Any(a => LineOf(a) == lineNumber);

    /// <summary>Marks a line.</summary>
    /// <param name="lineNumber">The line, from 0.</param>
    /// <param name="type">The kind.</param>
    public void SetMark(int lineNumber, string type)
    {
        if (HasMark(lineNumber, type)) { return; }

        ITextAnchor anchor = AnchorForLine(lineNumber);
        if (anchor == null) { return; }

        List<ITextAnchor> list = _marks[type];
        list.Add(anchor);
        Sort(list);
        MarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes a line's mark.</summary>
    /// <param name="lineNumber">The line, from 0.</param>
    /// <param name="type">The kind.</param>
    public void UnsetMark(int lineNumber, string type)
    {
        //Upstream removes DOUBLE occurrences too: two anchors can drift onto
        //one line when the text between them is deleted.
        int removed = _marks[type].RemoveAll(a => LineOf(a) == lineNumber);
        if (removed > 0) { MarksChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Sets or clears a line's mark.</summary>
    /// <param name="lineNumber">The line, from 0.</param>
    /// <param name="type">The kind.</param>
    public void ToggleMark(int lineNumber, string type)
    {
        if (HasMark(lineNumber, type))
        {
            UnsetMark(lineNumber, type);
            return;
        }

        SetMark(lineNumber, type);
    }

    /// <summary>Removes every mark, or every mark of one kind.</summary>
    /// <param name="type">The kind, or null for every kind.</param>
    public void Clear(string type = null)
    {
        foreach (var name in type == null ? Types : new[] { type })
        {
            _marks[name].Clear();
        }

        MarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Finds the first marked line after a line.</summary>
    /// <param name="lineNumber">The line, from 0.</param>
    /// <param name="type">The kind, or null for any kind.</param>
    /// <returns>The line, or -1 when there is none.</returns>
    public int NextMark(int lineNumber, string type = null)
        => MarkedLines(type).Where(n => n > lineNumber).DefaultIfEmpty(-1).First();

    /// <summary>Finds the last marked line before a line.</summary>
    /// <param name="lineNumber">The line, from 0.</param>
    /// <param name="type">The kind, or null for any kind.</param>
    /// <returns>The line, or -1 when there is none.</returns>
    public int PreviousMark(int lineNumber, string type = null)
        => MarkedLines(type).Where(n => n < lineNumber).DefaultIfEmpty(-1).Last();

    /// <summary>Reads the marks back from the metainfo.</summary>
    public void Load()
    {
        foreach (var type in Types)
        {
            _marks[type].Clear();
        }

        string stored = DocumentEditorState.For(Document)?.MetaInfo?.Get(MetaInfoName);
        foreach (var (type, lineNumber) in Decode(stored))
        {
            if (!_marks.TryGetValue(type, out var list)) { continue; }

            ITextAnchor anchor = AnchorForLine(lineNumber);
            if (anchor != null) { list.Add(anchor); }
        }

        foreach (var list in _marks.Values)
        {
            Sort(list);
        }

        MarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the marks out to the metainfo.</summary>
    public void Save()
    {
        MetaInfo info = DocumentEditorState.For(Document)?.MetaInfo;
        info?.Set(MetaInfoName, Encode());
    }

    /// <summary>
    /// Encodes the marks as they are stored: kinds separated by <c>;</c>, each
    /// a name, a colon and its line numbers.
    /// </summary>
    /// <returns>The encoded marks.</returns>
    /// <remarks>Upstream stores JSON. This is one metainfo STRING either way,
    /// and a plain list keeps the settings store readable — nothing else reads
    /// the value, so the format is ours to pick.</remarks>
    public string Encode()
        => string.Join(";", Types.Select(
            t => t + ":" + string.Join(",", MarkedLines(t))));

    /// <summary>Decodes what <see cref="Encode"/> wrote.</summary>
    /// <param name="text">The encoded marks.</param>
    /// <returns>The kind and line of each mark.</returns>
    public static IEnumerable<(string Type, int LineNumber)> Decode(string text)
    {
        if (string.IsNullOrEmpty(text)) { yield break; }

        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0) { continue; }

            string type = part.Substring(0, colon);
            foreach (var number in part.Substring(colon + 1)
                .Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(
                        number, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int line))
                {
                    yield return (type, line);
                }
            }
        }
    }

    private IEnumerable<ITextAnchor> AnchorsOf(string type)
        => type == null
            ? Types.SelectMany(t => _marks[t])
            : _marks.TryGetValue(type, out var list)
                ? list
                : Enumerable.Empty<ITextAnchor>();

    private int LineOf(ITextAnchor anchor)
        => anchor is { IsDeleted: false } ? anchor.Line - 1 : -1;

    private void Sort(List<ITextAnchor> anchors)
        => anchors.Sort((a, b) => LineOf(a).CompareTo(LineOf(b)));

    private ITextAnchor AnchorForLine(int lineNumber)
    {
        TextDocument store = Document?.Document;
        if (store == null || lineNumber < 0 || lineNumber >= store.LineCount)
        {
            return null;
        }

        ITextAnchor anchor = store.CreateAnchor(
            store.GetLineByNumber(lineNumber + 1).Offset);

        //A mark belongs to the line it is ON: text typed at its start pushes
        //the line down and the mark with it, which is what upstream's
        //keepPositionOnInsert gives.
        anchor.MovementType = AnchorMovementType.AfterInsertion;
        return anchor;
    }
}

/// <summary>The marked-line commands, which the View menu shows.</summary>
public sealed class BookmarkActions : Commands.ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "bookmarkmanager";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public BookmarkActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the "mark this line" toggle.</summary>
    public Commands.AppAction ViewBookmark { get; private set; }

    /// <summary>Gets the "clear the error marks" command.</summary>
    public Commands.AppAction ViewClearErrorMarks { get; private set; }

    /// <summary>Gets the "clear every mark" command.</summary>
    public Commands.AppAction ViewClearAllMarks { get; private set; }

    /// <summary>Gets the "go to the next mark" command.</summary>
    public Commands.AppAction ViewNextMark { get; private set; }

    /// <summary>Gets the "go to the previous mark" command.</summary>
    public Commands.AppAction ViewPreviousMark { get; private set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Bookmarks");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        ViewBookmark = Add("view_bookmark")
            .AsToggle().WithIcon("bookmark-new").WithShortcut("Ctrl+B");
        ViewClearErrorMarks = Add("view_clear_error_marks");
        ViewClearAllMarks = Add("view_clear_all_marks").WithIcon("edit-clear");
        ViewNextMark = Add("view_next_mark").WithShortcut("Alt+PageDown");
        ViewPreviousMark = Add("view_previous_mark").WithShortcut("Alt+PageUp");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        ViewBookmark.Text = I18n.Get("&Mark Current Line");
        ViewClearErrorMarks.Text = I18n.Get("Clear &Error Marks");
        ViewClearAllMarks.Text = I18n.Get("Clear &All Marks");
        ViewNextMark.Text = I18n.Get("Next Mark");
        ViewPreviousMark.Text = I18n.Get("Previous Mark");
    }
}
