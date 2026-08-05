/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using System.Globalization;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Origins; //was previously: lily/input.cc, lily/include/input.hh;

// Modified by Jeremy Ellis on 2026-08-04 as part of the CodeBrix port.

/// <summary>
/// Where something came from in the input: a source file and a range within it.
/// <para>
/// Everything that can be blamed in a diagnostic carries one — music expressions, context
/// definitions, grobs. It is the difference between "syntax error" and "syntax error at
/// chorale.ly:14:8, here:", so it is worth more than its size suggests.
/// </para>
/// <para>
/// DIVERGENCE — A REFERENCE TYPE HOLDING OFFSETS. Upstream's <c>Input</c> is a value type
/// holding two <c>char const *</c> into the source buffer, copied freely and compared by
/// pointer. This port holds a <see cref="SourceFile"/> and two CHARACTER OFFSETS, which
/// is the same information without the buffer-lifetime hazard, and makes it a class so an
/// origin can be stored in a Scheme property as one object. <see cref="Spot"/> therefore
/// returns a copy explicitly, where upstream's copy is implicit in the return by value.
/// </para>
/// </summary>
public sealed class Input
{
    /// <summary>Initializes an origin with no location, which reports "position unknown".</summary>
    public Input()
    {
        SourceFile = null;
        Start = 0;
        End = 0;
    }

    /// <summary>Initializes an origin spanning a range of a file.</summary>
    /// <param name="sourceFile">The file, or <see langword="null"/> for an unknown position.</param>
    /// <param name="start">The first character offset.</param>
    /// <param name="end">One past the last character offset.</param>
    public Input(SourceFile sourceFile, int start, int end)
    {
        SourceFile = sourceFile;
        Start = start;
        End = end;
    }

    /// <summary>Initializes a copy of another origin.</summary>
    /// <param name="other">The origin to copy.</param>
    public Input(Input other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        SourceFile = other.SourceFile;
        Start = other.Start;
        End = other.End;
    }

    /// <summary>Gets the file this origin points into, or <see langword="null"/>.</summary>
    public SourceFile SourceFile { get; private set; }

    /// <summary>Gets the first character offset.</summary>
    public int Start { get; private set; }

    /// <summary>Gets one past the last character offset.</summary>
    public int End { get; private set; }

    /// <summary>Gets the length of the span, in characters.</summary>
    public int Size => End - Start;

    /// <summary>Gets the origin's file, or <see langword="null"/> when it has none.</summary>
    /// <returns>The source file.</returns>
    public SourceFile GetSourceFile() => SourceFile;

    /// <summary>Points this origin at a range of a file.</summary>
    /// <param name="sourceFile">The file.</param>
    /// <param name="start">The first character offset.</param>
    /// <param name="end">One past the last character offset.</param>
    public void Set(SourceFile sourceFile, int start, int end)
    {
        SourceFile = sourceFile;
        Start = start;
        End = end;
    }

    /// <summary>Returns a copy of this origin.</summary>
    /// <returns>The copy.</returns>
    public Input Spot() => new Input(this);

    /// <summary>Copies another origin over this one.</summary>
    /// <param name="other">The origin to copy from.</param>
    public void SetSpot(Input other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        SourceFile = other.SourceFile;
        Start = other.Start;
        End = other.End;
    }

    /// <summary>Advances the origin by one character.</summary>
    public void StepForward()
    {
        if (End == Start)
        {
            End++;
        }

        Start++;
    }

    /// <summary>Spans from the start of one origin to the end of another.</summary>
    /// <param name="start">The origin supplying the file and start.</param>
    /// <param name="end">The origin supplying the end.</param>
    public void SetLocation(Input start, Input end)
    {
        if (start == null)
        {
            throw new ArgumentNullException(nameof(start));
        }

        if (end == null)
        {
            throw new ArgumentNullException(nameof(end));
        }

        SourceFile = start.SourceFile;
        Start = start.Start;
        End = end.End;
    }

    /// <summary>Gets the origin as <c>file:line:column</c>, or a "position unknown" note.</summary>
    /// <returns>The formatted location.</returns>
    public string LocationString()
        => SourceFile != null
            ? SourceFile.FileLineColumnString(Start)
            : " (position unknown)";

    /// <summary>Gets the origin's line number as text, or <c>?</c>.</summary>
    /// <returns>The line number.</returns>
    public string LineNumberString()
        => SourceFile != null
            ? SourceFile.GetLine(Start).ToString(CultureInfo.InvariantCulture)
            : "?";

    /// <summary>Gets the origin's file name, or an empty string.</summary>
    /// <returns>The file name.</returns>
    public string FileString() => SourceFile != null ? SourceFile.NameString() : string.Empty;

    /// <summary>Gets the one-based line the origin starts on, or zero.</summary>
    /// <returns>The line number.</returns>
    public int LineNumber() => SourceFile != null ? SourceFile.GetLine(Start) : 0;

    /// <summary>Gets the display column the origin starts at, or zero.</summary>
    /// <returns>The column.</returns>
    public int ColumnNumber()
    {
        if (SourceFile == null)
        {
            return 0;
        }

        SourceFile.GetCounts(Start, out int _, out int _, out int column, out int _);
        return column;
    }

    /// <summary>Gets the one-based line the origin ends on, or zero.</summary>
    /// <returns>The line number.</returns>
    public int EndLineNumber() => SourceFile != null ? SourceFile.GetLine(End) : 0;

    /// <summary>Gets the display column the origin ends at, or zero.</summary>
    /// <returns>The column.</returns>
    public int EndColumnNumber()
    {
        if (SourceFile == null)
        {
            return 0;
        }

        SourceFile.GetCounts(End, out int _, out int _, out int column, out int _);
        return column;
    }

    /// <summary>Counts the origin's start position within its line.</summary>
    /// <param name="line">The one-based line number.</param>
    /// <param name="lineChar">The zero-based character index within the line.</param>
    /// <param name="column">The zero-based display column within the line.</param>
    /// <param name="lineOffset">The zero-based offset of the position within the line.</param>
    public void GetCounts(out int line, out int lineChar, out int column, out int lineOffset)
    {
        if (SourceFile == null)
        {
            line = 0;
            lineChar = 0;
            column = 0;
            lineOffset = 0;
            return;
        }

        SourceFile.GetCounts(Start, out line, out lineChar, out column, out lineOffset);
    }

    /// <summary>Reports a fatal error at this origin.</summary>
    /// <param name="message">The error text.</param>
    /// <exception cref="LilyPondErrorException">Always thrown, carrying the message.</exception>
    public void Error(string message) => Warn.Error(MessageString(message), MessageLocation());

    /// <summary>Reports an internal error at this origin.</summary>
    /// <param name="message">The error text.</param>
    public void ProgrammingError(string message)
        => Warn.ProgrammingError(MessageString(message), MessageLocation());

    /// <summary>Reports a non-fatal error at this origin.</summary>
    /// <param name="message">The error text.</param>
    public void NonFatalError(string message)
        => Warn.NonFatalError(MessageString(message), MessageLocation());

    /// <summary>Reports a warning at this origin.</summary>
    /// <param name="message">The warning text.</param>
    public void Warning(string message) => Warn.Warning(MessageString(message), MessageLocation());

    /// <summary>Reports a deprecation warning at this origin, once per distinct message.</summary>
    /// <param name="message">The warning text.</param>
    public void DeprecationWarning(string message)
        => Warn.DeprecationWarning(MessageString(message), MessageLocation());

    /// <summary>Reports an informational message at this origin.</summary>
    /// <param name="message">The message text.</param>
    public void Message(string message) => Warn.Message(MessageString(message), MessageLocation());

    /// <summary>Reports debug output at this origin.</summary>
    /// <param name="message">The message text.</param>
    public void DebugOutput(string message) => Warn.Debug(MessageString(message));

    /// <summary>
    /// Builds the message body: the text, then the quoted source line with the position
    /// marked. This is what gives LilyPond diagnostics their two-line excerpt.
    /// </summary>
    /// <param name="message">The message text.</param>
    /// <returns>The message with its excerpt, or the message alone when there is no file.</returns>
    private string MessageString(string message)
        => SourceFile != null
            ? message + "\n" + SourceFile.QuoteInput(Start)
            : message;

    private string MessageLocation() => SourceFile != null ? LocationString() : string.Empty;

    /// <summary>Determines whether two origins point at the same range of the same file.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the origins are equal.</returns>
    public override bool Equals(object obj)
        => obj is Input other
           && ReferenceEquals(SourceFile, other.SourceFile)
           && Start == other.Start
           && End == other.End;

    /// <summary>Returns a hash code consistent with <see cref="Equals(object)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(SourceFile, Start, End);

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description naming the location.</returns>
    public override string ToString() => "#<location " + LocationString() + ">";
}
