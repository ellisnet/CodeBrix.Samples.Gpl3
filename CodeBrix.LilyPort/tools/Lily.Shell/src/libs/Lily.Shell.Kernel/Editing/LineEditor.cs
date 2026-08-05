// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Text;

namespace Lily.Shell.Kernel.Editing;

/// <summary>
/// A single-line input editor. Each editing operation mutates the buffer and
/// returns the VT escape/echo string that makes the terminal display match —
/// the caller writes that string to the terminal verbatim. The editor knows
/// nothing about the prompt; all cursor arithmetic is relative to the start
/// of the edited text.
/// </summary>
/// <remarks>
/// Known limitation, accepted for now: cursor movement uses plain CUB/CUF
/// sequences, which do not wrap across terminal rows — editing behaves
/// correctly only while prompt + line fit on one terminal row.
/// </remarks>
public sealed class LineEditor
{
    private readonly StringBuilder _buffer = new();
    private int _cursor;

    /// <summary>The current text of the line being edited.</summary>
    public string Text => _buffer.ToString();

    /// <summary>The cursor position within <see cref="Text"/> (0 = before the first character).</summary>
    public int CursorPosition => _cursor;

    /// <summary>Inserts a character at the cursor. Returns the echo string.</summary>
    public string Insert(char c)
    {
        _buffer.Insert(_cursor, c);
        _cursor++;
        var tail = _buffer.ToString(_cursor, _buffer.Length - _cursor);
        return c + tail + CursorBack(tail.Length);
    }

    /// <summary>Inserts a string (e.g. a paste) at the cursor. Returns the echo string.</summary>
    public string Insert(string text)
    {
        if (string.IsNullOrEmpty(text)) { return string.Empty; }

        _buffer.Insert(_cursor, text);
        _cursor += text.Length;
        var tail = _buffer.ToString(_cursor, _buffer.Length - _cursor);
        return text + tail + CursorBack(tail.Length);
    }

    /// <summary>Deletes the character before the cursor. Returns the echo string.</summary>
    public string Backspace()
    {
        if (_cursor == 0) { return string.Empty; }

        _cursor--;
        _buffer.Remove(_cursor, 1);
        var tail = _buffer.ToString(_cursor, _buffer.Length - _cursor);
        return "\b" + tail + " " + CursorBack(tail.Length + 1);
    }

    /// <summary>Deletes the character under the cursor. Returns the echo string.</summary>
    public string Delete()
    {
        if (_cursor >= _buffer.Length) { return string.Empty; }

        _buffer.Remove(_cursor, 1);
        var tail = _buffer.ToString(_cursor, _buffer.Length - _cursor);
        return tail + " " + CursorBack(tail.Length + 1);
    }

    /// <summary>Moves the cursor one character left. Returns the echo string.</summary>
    public string MoveLeft()
    {
        if (_cursor == 0) { return string.Empty; }

        _cursor--;
        return "\b";
    }

    /// <summary>Moves the cursor one character right. Returns the echo string.</summary>
    public string MoveRight()
    {
        if (_cursor >= _buffer.Length) { return string.Empty; }

        _cursor++;
        return "\x1b[C";
    }

    /// <summary>Moves the cursor to the start of the line. Returns the echo string.</summary>
    public string MoveHome()
    {
        var distance = _cursor;
        _cursor = 0;
        return CursorBack(distance);
    }

    /// <summary>Moves the cursor to the end of the line. Returns the echo string.</summary>
    public string MoveEnd()
    {
        var distance = _buffer.Length - _cursor;
        _cursor = _buffer.Length;
        return CursorForward(distance);
    }

    /// <summary>
    /// Replaces the whole line with new text (history navigation). Returns the
    /// echo string, which erases the old line and writes the new one.
    /// </summary>
    public string ReplaceWith(string text)
    {
        text = text ?? string.Empty;
        var echo = CursorBack(_cursor) + "\x1b[K" + text;
        _buffer.Clear();
        _buffer.Append(text);
        _cursor = _buffer.Length;
        return echo;
    }

    /// <summary>
    /// Returns the finished line and resets the editor for the next one.
    /// Produces no echo — the caller echoes the line ending itself.
    /// </summary>
    public string TakeLine()
    {
        var line = _buffer.ToString();
        Reset();
        return line;
    }

    /// <summary>
    /// Re-emits the current line text and restores the cursor column — used to
    /// repaint the input line after a screen clear. Returns the echo string.
    /// </summary>
    public string Redraw()
    {
        var tail = _buffer.Length - _cursor;
        return _buffer.ToString() + CursorBack(tail);
    }

    /// <summary>Clears the buffer and cursor without producing any echo.</summary>
    public void Reset()
    {
        _buffer.Clear();
        _cursor = 0;
    }

    private static string CursorBack(int count) =>
        count <= 0 ? string.Empty : "\x1b[" + count + "D";

    private static string CursorForward(int count) =>
        count <= 0 ? string.Empty : "\x1b[" + count + "C";
}
