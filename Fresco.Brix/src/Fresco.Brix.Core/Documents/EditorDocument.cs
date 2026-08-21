// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Services;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/document.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One document the user can edit: the editor's text store plus everything
/// about the file behind it — where it lives, what encoding it is in, whether
/// it has unsaved changes, and the events the rest of the app watches.
/// </summary>
/// <remarks>
/// <para>
/// Upstream has two classes: <c>Document</c> for documents that are not in the
/// editor (generated input handed to a job) and <c>EditorDocument</c> for the
/// ones in a tab, which adds the signals. In-process engraving takes text, not
/// a QTextDocument, so the non-editor half has no job to do; this class is the
/// editor one, and the app-wide announcements live on
/// <see cref="DocumentManager"/> rather than on a module-level singleton.
/// </para>
/// <para>
/// The text store is the AdvancedTextEdit <see cref="TextDocument"/> — one per
/// document, shared by every view that shows it — so an edit made through a
/// ported ly tool and an edit typed by the user are the same edit.
/// </para>
/// </remarks>
public sealed class EditorDocument
{
    private const int ChangesStoppedDelayMilliseconds = 900;

    private readonly Timer _changeTimer;
    private string _path;
    private int _number;
    private bool _isChanging;

    /// <summary>Creates an empty, never-saved document.</summary>
    /// <param name="path">The file it belongs to, or null for a new one.</param>
    /// <param name="encoding">The encoding to save in, or null to decide from
    /// the document's own <c>coding</c> variable (and UTF-8 failing that).</param>
    public EditorDocument(string path = null, Encoding encoding = null)
    {
        Document = new TextDocument();
        Encoding = encoding;
        _path = Normalize(path);

        Document.TextChanged += OnTextChanged;
        Document.UndoStack.PropertyChanged += OnUndoStackPropertyChanged;
        _changeTimer = new Timer(_ => RaiseChangesStopped(), null,
            Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised when the document's file changes (new path, old path).</summary>
    public event EventHandler<UrlChangedEventArgs> UrlChanged;

    /// <summary>Raised when the document is closed.</summary>
    public event EventHandler Closed;

    /// <summary>Raised after the document's text is (re)loaded from its file.</summary>
    public event EventHandler Loaded;

    /// <summary>Raised after the document is written to its file.</summary>
    public event EventHandler Saved;

    /// <summary>Raised when the modified flag changes.</summary>
    public event EventHandler ModificationChanged;

    /// <summary>Raised on every change to the document's text.</summary>
    /// <remarks>Fires per keystroke; anything costly listens to
    /// <see cref="ChangesStopped"/> instead, and uses this only to mark what it
    /// knows as stale.</remarks>
    public event EventHandler ContentsChanged;

    /// <summary>
    /// Raised a short time after the last change, so work too costly to redo
    /// on every keystroke can wait for the user to pause.
    /// </summary>
    public event EventHandler ChangesStopped;

    /// <summary>Gets the text store.</summary>
    public TextDocument Document { get; }

    /// <summary>Gets the file path, or null for a document never saved.</summary>
    public string Path => _path;

    /// <summary>Gets or sets the encoding the document is written in.</summary>
    /// <remarks>Upstream reads the document's <c>coding</c> variable first;
    /// <see cref="ResolvedEncoding"/> does that, this is the explicit
    /// override.</remarks>
    public Encoding Encoding { get; set; }

    /// <summary>Gets or sets whether there are unsaved changes.</summary>
    public bool IsModified
    {
        get => !Document.UndoStack.IsOriginalFile;
        set
        {
            if (value)
            {
                Document.UndoStack.DiscardOriginalFileMarker();
            }
            else
            {
                Document.UndoStack.MarkAsOriginalFile();
            }
        }
    }

    /// <summary>
    /// Gets whether the document changed within the last moment — the flag a
    /// caller checks before starting work the next keystroke would waste.
    /// </summary>
    public bool IsChanging => _isChanging;

    /// <summary>
    /// Gets the number a nameless document is distinguished by: 0 for a
    /// document with a file, otherwise the lowest number free among the
    /// nameless documents when the name was assigned.
    /// </summary>
    public int Number => _number;

    /// <summary>Gets the document's whole text.</summary>
    public string Text => Document.Text;

    /// <summary>
    /// Gets the name to display: the file name, or <c>Untitled</c> (with a
    /// number once more than one is open).
    /// </summary>
    /// <returns>The display name.</returns>
    public string DocumentName()
    {
        if (_path != null)
        {
            return System.IO.Path.GetFileName(_path);
        }

        return _number == 1
            ? I18n.Get("Untitled")
            : I18n.Format(I18n.Get("Untitled ({num})"), ("num", _number));
    }

    /// <summary>
    /// Reads a file, answering its text with line endings normalized to
    /// <c>\n</c> — the form everything downstream assumes.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="encoding">The encoding, or null to detect it.</param>
    /// <returns>The text.</returns>
    public static string LoadData(string path, Encoding encoding = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new IOException("not a local file");
        }

        byte[] data = File.ReadAllBytes(path);
        return UniversalNewlines(Decode(data, encoding));
    }

    /// <summary>Creates a document with a file's contents already in it.</summary>
    /// <param name="path">The file to load.</param>
    /// <param name="encoding">The encoding, or null to detect it.</param>
    /// <returns>The document.</returns>
    /// <remarks>Upstream's <c>new_from_url</c>: the read happens BEFORE the
    /// document exists, so a failure leaves no half-made tab behind.</remarks>
    public static EditorDocument NewFromPath(string path, Encoding encoding = null)
    {
        string text = LoadData(path, encoding);
        EditorDocument document = new EditorDocument(path, encoding);
        document.Document.Text = text;
        document.IsModified = false;
        document.Loaded?.Invoke(document, EventArgs.Empty);
        return document;
    }

    /// <summary>(Re)reads the document's text from a file.</summary>
    /// <param name="path">The file, or null to re-read the current one.</param>
    /// <param name="encoding">The encoding, or null to detect it.</param>
    /// <param name="keepUndo">When true the load can be undone.</param>
    public void Load(string path = null, Encoding encoding = null, bool keepUndo = false)
    {
        string target = Normalize(path) ?? _path;
        string text = LoadData(target, encoding ?? Encoding);

        if (keepUndo)
        {
            //Replacing the whole range keeps one undoable step, so Ctrl+Z
            //puts the previous contents back.
            Document.Replace(0, Document.TextLength, text);
        }
        else
        {
            Document.Text = text;
            Document.UndoStack.ClearAll();
        }

        IsModified = false;
        if (path != null)
        {
            SetPath(target);
        }

        Loaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the document to a file.</summary>
    /// <param name="path">The file, or null to write the current one.</param>
    /// <param name="encoding">The encoding, or null to resolve it.</param>
    public void Save(string path = null, Encoding encoding = null)
    {
        string target = Normalize(path) ?? _path;
        if (string.IsNullOrEmpty(target))
        {
            throw new IOException("not a local file");
        }

        //Upstream keeps a newly given path even if the write then fails.
        if (_path == null && path != null)
        {
            SetPath(target);
        }

        File.WriteAllBytes(target, EncodedText(encoding));
        IsModified = false;
        if (path != null)
        {
            SetPath(target);
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the document's text encoded for writing: platform line endings,
    /// in the resolved encoding.
    /// </summary>
    /// <param name="encoding">The encoding, or null to resolve it.</param>
    /// <returns>The bytes.</returns>
    public byte[] EncodedText(Encoding encoding = null)
    {
        string text = PlatformNewlines(Document.Text);
        return (encoding ?? ResolvedEncoding()).GetBytes(text);
    }

    /// <summary>
    /// Resolves the encoding to write in: the document's own <c>coding</c>
    /// variable wins, then the explicit <see cref="Encoding"/>, then UTF-8.
    /// </summary>
    /// <returns>The encoding.</returns>
    public Encoding ResolvedEncoding()
    {
        string name = DocumentVariables.Get(Document.Text, "coding");
        if (!string.IsNullOrEmpty(name))
        {
            try
            {
                return Encoding.GetEncoding(name);
            }
            catch (ArgumentException)
            {
                //An unknown coding in the document must not stop a save.
            }
        }

        return Encoding ?? new UTF8Encoding(false);
    }

    /// <summary>Changes the file this document belongs to.</summary>
    /// <param name="path">The new path, or null to make it nameless.</param>
    public void SetPath(string path)
    {
        string old = _path;
        _path = Normalize(path);
        if (!string.Equals(old, _path, StringComparison.Ordinal))
        {
            UrlChanged?.Invoke(this, new UrlChangedEventArgs(_path, old));
        }
    }

    /// <summary>
    /// Assigns the number a nameless document is displayed with. Called by the
    /// <see cref="DocumentManager"/>, which is the only place that can see the
    /// numbers already taken.
    /// </summary>
    /// <param name="number">The number, or 0 for a document with a file.</param>
    internal void SetNumber(int number) => _number = number;

    /// <summary>Announces that the document is closing.</summary>
    internal void RaiseClosed()
    {
        _changeTimer.Change(Timeout.Infinite, Timeout.Infinite);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the offset of a line and column, both counted from 1, clamping
    /// anything out of range to the nearest valid position.
    /// </summary>
    /// <param name="line">The line number, from 1.</param>
    /// <param name="column">The column, from 1, in UTF-8 characters.</param>
    /// <returns>The offset.</returns>
    /// <remarks>
    /// Upstream's <c>cursorAtPosition</c>. Columns are counted the way
    /// LilyPond counts them — in characters of the UTF-8 text — which is why a
    /// column cannot simply be added to the line's offset.
    /// </remarks>
    public int OffsetAtPosition(int line, int column)
    {
        if (line < 1)
        {
            line = 1;
            column = 1;
        }

        if (column < 1) { column = 1; }

        if (line > Document.LineCount)
        {
            return Document.TextLength;
        }

        DocumentLine documentLine = Document.GetLineByNumber(line);
        string text = Document.GetText(documentLine.Offset, documentLine.Length);
        if (column - 1 >= text.Length)
        {
            return documentLine.EndOffset;
        }

        //Never land between the halves of a surrogate pair.
        int offset = documentLine.Offset + column - 1;
        if (char.IsLowSurrogate(Document.GetCharAt(offset)))
        {
            offset--;
        }

        return offset;
    }

    /// <summary>Normalizes a path, or answers null for an empty one.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The full path, or null.</returns>
    private static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) { return null; }

        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>Decodes file bytes, honouring a byte-order mark.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="encoding">The encoding, or null to detect it.</param>
    /// <returns>The text.</returns>
    /// <remarks>Upstream's <c>util.decode</c>: a BOM decides, then the given
    /// encoding, then UTF-8, then latin-1 — which cannot fail, so a file with
    /// an unknown encoding still opens.</remarks>
    private static string Decode(byte[] data, Encoding encoding)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return new UTF8Encoding(false).GetString(data, 3, data.Length - 3);
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        }

        foreach (var candidate in new[] { encoding, new UTF8Encoding(false, true) })
        {
            if (candidate == null) { continue; }

            try
            {
                return candidate.GetString(data);
            }
            catch (DecoderFallbackException)
            {
                //Try the next one.
            }
        }

        return Encoding.Latin1.GetString(data);
    }

    private static string UniversalNewlines(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string PlatformNewlines(string text)
        => Environment.NewLine == "\n"
            ? UniversalNewlines(text)
            : UniversalNewlines(text).Replace("\n", Environment.NewLine);

    private void OnTextChanged(object sender, EventArgs e)
    {
        _isChanging = true;
        _changeTimer.Change(ChangesStoppedDelayMilliseconds, Timeout.Infinite);
        ContentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnUndoStackPropertyChanged(
        object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UndoStack.IsOriginalFile))
        {
            ModificationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseChangesStopped()
    {
        _isChanging = false;
        ChangesStopped?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>The old and new file of a document whose path changed.</summary>
public sealed class UrlChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="path">The new path, or null.</param>
    /// <param name="oldPath">The previous path, or null.</param>
    public UrlChangedEventArgs(string path, string oldPath)
    {
        Path = path;
        OldPath = oldPath;
    }

    /// <summary>Gets the new path, or null.</summary>
    public string Path { get; }

    /// <summary>Gets the previous path, or null.</summary>
    public string OldPath { get; }
}
