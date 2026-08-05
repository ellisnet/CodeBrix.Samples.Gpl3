/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CodeBrix.LilyPort.Engine.Origins; //was previously: lily/source-file.cc, lily/include/source-file.hh;

// Modified by Jeremy Ellis on 2026-08-04 as part of the CodeBrix port.

/// <summary>
/// One file of LilyPond input, holding its text and the index of its line breaks so any
/// position in it can be turned into a line, column and quoted excerpt.
/// <para>
/// DIVERGENCE — POSITIONS ARE CHARACTER OFFSETS, NOT BYTE POINTERS. Upstream's
/// <c>Source_file</c> works in <c>char const *</c> into a UTF-8 buffer, and
/// <c>get_counts</c> explicitly skips UTF-8 continuation bytes so that its column count
/// comes out in CHARACTERS. This port stores the text as a .NET string and counts
/// characters directly, which produces the same line and column numbers by construction
/// while removing the continuation-byte special case. The one figure that differs is the
/// within-line offset, which is a character offset here and a byte offset upstream; it is
/// used only to split a line for the error caret, and both split at the same place.
/// </para>
/// </summary>
public sealed class SourceFile
{
    private readonly List<int> _newlines = new List<int>();

    private int _lineOffset;

    /// <summary>Initializes a source file from text already in hand.</summary>
    /// <param name="name">The file name, as it should appear in diagnostics.</param>
    /// <param name="data">The file's text.</param>
    public SourceFile(string name, string data)
    {
        Name = name ?? string.Empty;
        Text = data ?? string.Empty;

        for (int i = 0; i < Text.Length; i++)
        {
            if (Text[i] == '\n')
            {
                _newlines.Add(i);
            }
        }
    }

    /// <summary>Gets the file name, as diagnostics should show it.</summary>
    public string Name { get; }

    /// <summary>Gets the file's text.</summary>
    public string Text { get; }

    /// <summary>Gets the length of the text, in characters.</summary>
    public int Length => Text.Length;

    /// <summary>Reads a file from disk, warning and yielding empty text when it cannot.</summary>
    /// <param name="fileName">The path to read.</param>
    /// <returns>The file's contents, or an empty string when it could not be read.</returns>
    public static string GulpFile(string fileName)
    {
        try
        {
            // Read as UTF-8 without byte-order-mark interpretation surprises. Upstream
            // opens "rb" specifically so no CR/LF translation happens; the .NET default
            // does not translate either, so nothing extra is needed.
            return File.ReadAllText(fileName, Encoding.UTF8);
        }
        catch (IOException)
        {
            Flower.Warn.Warning("cannot open file: `" + fileName + "'");
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            Flower.Warn.Warning("cannot open file: `" + fileName + "'");
            return string.Empty;
        }
    }

    /// <summary>Opens a file from disk.</summary>
    /// <param name="fileName">The path to read.</param>
    /// <returns>The source file.</returns>
    public static SourceFile Open(string fileName)
        => new SourceFile(fileName, GulpFile(fileName));

    /// <summary>Determines whether an offset lies within this file.</summary>
    /// <param name="offset">The character offset to test.</param>
    /// <returns><see langword="true"/> when the offset is in range.</returns>
    public bool Contains(int offset) => offset >= 0 && offset <= Length;

    /// <summary>Gets the one-based line number an offset falls on.</summary>
    /// <param name="offset">The character offset.</param>
    /// <returns>The line number, or zero when the offset is not in this file.</returns>
    public int GetLine(int offset)
    {
        if (!Contains(offset))
        {
            return 0;
        }

        if (_newlines.Count == 0)
        {
            return 1 + _lineOffset;
        }

        // lower_bound: the first newline at or after the offset -- which is the newline
        // ENDING our line, so its index is the count of lines before us.
        int index = _newlines.BinarySearch(offset);
        if (index < 0)
        {
            index = ~index;
        }

        return index + 1 + _lineOffset;
    }

    /// <summary>
    /// Renumbers the file so a given offset reports a given line, as <c>\sourcefilename</c>
    /// and the <c>#line</c>-style directives need.
    /// </summary>
    /// <param name="offset">The offset to pin, or a negative value to set the base directly.</param>
    /// <param name="line">The line number the offset should report.</param>
    public void SetLine(int offset, int line)
    {
        if (offset >= 0)
        {
            _lineOffset += line - GetLine(offset);
        }
        else
        {
            _lineOffset = line;
        }
    }

    /// <summary>Gets the half-open character range of the line containing an offset.</summary>
    /// <param name="offset">The character offset.</param>
    /// <param name="start">The first character of the line.</param>
    /// <param name="end">One past the last character of the line.</param>
    public void LineSlice(int offset, out int start, out int end)
    {
        start = 0;
        end = 0;

        if (!Contains(offset))
        {
            return;
        }

        int position = offset;
        if (position == Length && Length > 0)
        {
            position--;
        }

        int begin = position;
        while (begin > 0)
        {
            if (Text[--begin] == '\n')
            {
                begin++;
                break;
            }
        }

        int stop = position;
        while (stop < Length)
        {
            if (Text[stop++] == '\n')
            {
                stop--;
                break;
            }
        }

        start = begin;
        end = stop;
    }

    /// <summary>Gets the text of the line containing an offset, without its newline.</summary>
    /// <param name="offset">The character offset.</param>
    /// <returns>The line's text, or an empty string when the offset is out of range.</returns>
    public string LineString(int offset)
    {
        if (!Contains(offset))
        {
            return string.Empty;
        }

        LineSlice(offset, out int start, out int end);
        return Text.Substring(start, end - start);
    }

    /// <summary>
    /// Counts an offset's position within its line: the line number, the character index,
    /// the display column (tabs advance to the next multiple of eight) and the offset of
    /// the position within the line.
    /// </summary>
    /// <param name="offset">The character offset.</param>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <param name="lineChar">The zero-based character index within the line.</param>
    /// <param name="column">The zero-based display column within the line.</param>
    /// <param name="lineOffset">The zero-based offset of the position within the line.</param>
    public void GetCounts(
        int offset, out int lineNumber, out int lineChar, out int column, out int lineOffset)
    {
        // Defaults matter: upstream sets them before the range check, so a position that
        // is not in this file reads as zeroes rather than as garbage.
        lineNumber = 0;
        lineChar = 0;
        column = 0;
        lineOffset = 0;

        if (!Contains(offset))
        {
            return;
        }

        lineNumber = GetLine(offset);

        LineSlice(offset, out int start, out int _);
        lineOffset = offset - start;

        for (int i = start; i < offset; i++)
        {
            // A surrogate pair is ONE character, the same way upstream's continuation-byte
            // skip makes a multi-byte sequence one character.
            if (char.IsLowSurrogate(Text[i]))
            {
                continue;
            }

            if (Text[i] == '\t')
            {
                column = ((column / 8) + 1) * 8;
            }
            else
            {
                column++;
            }

            lineChar++;
        }
    }

    /// <summary>Formats an offset as <c>file:line:column</c>.</summary>
    /// <param name="offset">The character offset.</param>
    /// <returns>The formatted location.</returns>
    public string FileLineColumnString(int offset)
    {
        if (Length == 0 && !Contains(offset))
        {
            return " (position unknown)";
        }

        GetCounts(offset, out int line, out int _, out int column, out int _);
        return Name + ":" + line.ToString(CultureInfo.InvariantCulture)
               + ":" + (column + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Quotes the line an offset falls on, with the remainder of the line moved to a second
    /// line so the break marks the position — LilyPond's characteristic two-line excerpt.
    /// </summary>
    /// <param name="offset">The character offset.</param>
    /// <returns>The quoted excerpt.</returns>
    public string QuoteInput(int offset)
    {
        if (!Contains(offset))
        {
            return " (position unknown)";
        }

        GetCounts(offset, out int _, out int _, out int column, out int lineOffset);
        string line = LineString(offset);

        StringBuilder context = new StringBuilder();
        context.Append(line, 0, lineOffset);
        context.Append('\n');
        if (column > 0)
        {
            context.Append(' ', column);
        }

        context.Append(line, lineOffset, line.Length - lineOffset);
        return context.ToString();
    }

    /// <summary>Gets the file name, as diagnostics should show it.</summary>
    /// <returns>The file name.</returns>
    public string NameString() => Name;

    /// <summary>
    /// Finds the character offset of a one-based line and column.
    /// <para>
    /// NEW IN THE PORT, with no upstream counterpart: upstream's parser carries pointers
    /// into the buffer, so it never needs to go the other way. This port's lexer reports
    /// line and column, and this is the bridge that turns one of its spans back into the
    /// offset an <see cref="Input"/> wants.
    /// </para>
    /// </summary>
    /// <param name="line">The one-based line number.</param>
    /// <param name="column">The one-based column number.</param>
    /// <returns>The character offset, clamped into the file.</returns>
    public int OffsetOfLineColumn(int line, int column)
    {
        if (line <= 1 && _newlines.Count == 0)
        {
            return Clamp(column - 1);
        }

        int target = line - 1 - _lineOffset;
        int lineStart = target <= 0
            ? 0
            : (target - 1 < _newlines.Count ? _newlines[target - 1] + 1 : Length);

        return Clamp(lineStart + Math.Max(0, column - 1));
    }

    private int Clamp(int offset) => Math.Max(0, Math.Min(Length, offset));

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description naming the file.</returns>
    public override string ToString() => "#<Source_file " + Name + " >";
}
