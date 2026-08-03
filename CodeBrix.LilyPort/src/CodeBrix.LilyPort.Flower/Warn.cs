/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                           Jan Nieuwenhuizen <janneke@gnu.org>

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
using System.IO;

namespace CodeBrix.LilyPort.Flower; //was previously: flower/warn.cc, flower/include/warn.hh;
// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port:
//   - translated from C++17 to C# targeting net10.0
//   - the LOG_* preprocessor defines become a [Flags] enum
//   - upstream calls exit() on a fatal error. A library must not terminate its host
//     process, so Error throws LilyPondErrorException instead and the caller
//     decides. This is a DELIBERATE behavioural change and the one place in flower/
//     where the port does not mirror upstream control flow.

/// <summary>Message severity levels, as bit flags so a log level is a mask.</summary>
[Flags]
public enum LogLevel
{
    /// <summary>No output at all.</summary>
    None = 0,

    /// <summary>Errors only.</summary>
    Error = 1 << 0,

    /// <summary>Warnings.</summary>
    Warn = 1 << 1,

    /// <summary>Basic progress: the input file name and whether it succeeded.</summary>
    Basic = 1 << 2,

    /// <summary>Progress reporting.</summary>
    Progress = 1 << 3,

    /// <summary>Informational messages.</summary>
    Info = 1 << 4,

    /// <summary>Debug output.</summary>
    Debug = 1 << 8,

    /// <summary>Errors only.</summary>
    LevelError = Error,

    /// <summary>Errors and warnings.</summary>
    LevelWarn = LevelError | Warn,

    /// <summary>Errors, warnings and basic progress.</summary>
    LevelBasic = LevelWarn | Basic,

    /// <summary>Everything up to progress.</summary>
    LevelProgress = LevelBasic | Progress,

    /// <summary>Everything up to informational.</summary>
    LevelInfo = LevelProgress | Info,

    /// <summary>Everything, including debug.</summary>
    LevelDebug = LevelInfo | Debug,
}

/// <summary>
/// Raised where upstream would call <c>exit()</c>. A library must not terminate its
/// host process, so the decision is handed to the caller.
/// </summary>
public sealed class LilyPondErrorException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">The error text.</param>
    public LilyPondErrorException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a source location.</summary>
    /// <param name="message">The error text.</param>
    /// <param name="location">Where the error was detected.</param>
    public LilyPondErrorException(string message, string location)
        : base(string.IsNullOrEmpty(location) ? message : location + ": " + message)
    {
        Location = location;
    }

    /// <summary>Gets the source location, when one was supplied.</summary>
    public string Location { get; }
}

/// <summary>
/// Diagnostic output. The engine calls these constantly, and the message text is part
/// of LilyPond's user-facing behaviour, so the prefixes are upstream's.
/// </summary>
public static class Warn
{
    private static readonly List<string> RecordedMessages = new List<string>();

    /// <summary>Gets or sets the active log level mask.</summary>
    public static LogLevel Level { get; set; } = LogLevel.LevelWarn;

    /// <summary>
    /// Gets or sets a value indicating whether warnings are promoted to errors, which
    /// is LilyPond's <c>--warning-as-error</c>.
    /// </summary>
    public static bool WarningAsError { get; set; }

    /// <summary>Gets or sets the writer diagnostics go to.</summary>
    public static TextWriter Output { get; set; } = Console.Error;

    /// <summary>
    /// Gets or sets a value indicating whether messages are also recorded in memory.
    /// Tests use this to assert on diagnostics without capturing console output.
    /// </summary>
    public static bool RecordMessages { get; set; }

    /// <summary>Gets the messages recorded while <see cref="RecordMessages"/> was set.</summary>
    public static IReadOnlyList<string> Messages => RecordedMessages;

    /// <summary>Clears the recorded messages.</summary>
    public static void ClearMessages() => RecordedMessages.Clear();

    /// <summary>Determines whether a severity would be emitted at the current level.</summary>
    /// <param name="severity">The severity to test.</param>
    /// <returns><see langword="true"/> when the message would be shown.</returns>
    public static bool IsEnabled(LogLevel severity) => (Level & severity) != 0;

    /// <summary>Emits a warning.</summary>
    /// <param name="message">The warning text.</param>
    /// <param name="location">Where the problem was found, or <see langword="null"/>.</param>
    public static void Warning(string message, string location = null)
    {
        if (WarningAsError)
        {
            Error(message, location);
            return;
        }

        Emit(LogLevel.Warn, "warning: ", message, location);
    }

    /// <summary>Emits an informational message.</summary>
    /// <param name="message">The message text.</param>
    /// <param name="location">Where it applies, or <see langword="null"/>.</param>
    public static void Message(string message, string location = null)
        => Emit(LogLevel.Info, string.Empty, message, location);

    /// <summary>Emits a progress message.</summary>
    /// <param name="message">The message text.</param>
    public static void Progress(string message)
        => Emit(LogLevel.Progress, string.Empty, message, null);

    /// <summary>Emits a debug message.</summary>
    /// <param name="message">The message text.</param>
    public static void Debug(string message)
        => Emit(LogLevel.Debug, "debug: ", message, null);

    /// <summary>
    /// Reports an internal inconsistency. Upstream continues after printing, on the
    /// grounds that a partly-wrong score beats no score, so this does NOT throw.
    /// </summary>
    /// <param name="message">The message text.</param>
    /// <param name="location">Where the problem was found, or <see langword="null"/>.</param>
    public static void ProgrammingError(string message, string location = null)
        => Emit(LogLevel.Warn, "programming error: ", message, location);

    /// <summary>
    /// Reports a fatal error. Upstream calls <c>exit()</c>; a library cannot, so this
    /// throws <see cref="LilyPondErrorException"/> and the caller decides.
    /// </summary>
    /// <param name="message">The error text.</param>
    /// <param name="location">Where the problem was found, or <see langword="null"/>.</param>
    /// <exception cref="LilyPondErrorException">Always thrown.</exception>
    public static void Error(string message, string location = null)
    {
        Emit(LogLevel.Error, "error: ", message, location);
        throw new LilyPondErrorException(message, location);
    }

    /// <summary>Emits a non-fatal error, without throwing.</summary>
    /// <param name="message">The error text.</param>
    /// <param name="location">Where the problem was found, or <see langword="null"/>.</param>
    public static void NonFatalError(string message, string location = null)
        => Emit(LogLevel.Error, "error: ", message, location);

    private static void Emit(LogLevel severity, string prefix, string message, string location)
    {
        string text = string.IsNullOrEmpty(location)
            ? prefix + message
            : location + ": " + prefix + message;

        if (RecordMessages)
        {
            RecordedMessages.Add(text);
        }

        if (IsEnabled(severity))
        {
            Output?.WriteLine(text);
        }
    }
}

/// <summary>
/// A binary-heap priority queue. Upstream builds its own rather than using the
/// standard library's because the spacing code indexes into the heap directly.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class PriorityQueue<T>
{
    private readonly List<T> _heap = new List<T>();
    private readonly IComparer<T> _comparer;

    /// <summary>Initializes a queue using the default comparer.</summary>
    public PriorityQueue()
        : this(Comparer<T>.Default)
    {
    }

    /// <summary>Initializes a queue with an explicit comparer.</summary>
    /// <param name="comparer">The ordering to use; smallest comes out first.</param>
    public PriorityQueue(IComparer<T> comparer)
    {
        _comparer = comparer ?? Comparer<T>.Default;
    }

    /// <summary>Gets the number of queued elements.</summary>
    public int Count => _heap.Count;

    /// <summary>Gets the element at a heap index, without reordering.</summary>
    /// <param name="index">The heap index.</param>
    /// <returns>The element.</returns>
    public T this[int index] => _heap[index];

    /// <summary>Gets the smallest element without removing it.</summary>
    /// <returns>The front element.</returns>
    public T Front() => _heap[0];

    /// <summary>Inserts an element.</summary>
    /// <param name="value">The element to add.</param>
    public void Insert(T value)
    {
        _heap.Add(value);
        SiftUp(_heap.Count - 1);
    }

    /// <summary>Removes and returns the smallest element.</summary>
    /// <returns>The former front element.</returns>
    public T DeleteMinimum()
    {
        T minimum = _heap[0];
        _heap[0] = _heap[_heap.Count - 1];
        _heap.RemoveAt(_heap.Count - 1);
        if (_heap.Count != 0)
        {
            SiftDown(0);
        }

        return minimum;
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (_comparer.Compare(_heap[index], _heap[parent]) >= 0)
            {
                break;
            }

            (_heap[index], _heap[parent]) = (_heap[parent], _heap[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        while (true)
        {
            int left = (2 * index) + 1;
            int right = left + 1;
            int smallest = index;

            if (left < _heap.Count && _comparer.Compare(_heap[left], _heap[smallest]) < 0)
            {
                smallest = left;
            }

            if (right < _heap.Count && _comparer.Compare(_heap[right], _heap[smallest]) < 0)
            {
                smallest = right;
            }

            if (smallest == index)
            {
                return;
            }

            (_heap[index], _heap[smallest]) = (_heap[smallest], _heap[index]);
            index = smallest;
        }
    }
}
