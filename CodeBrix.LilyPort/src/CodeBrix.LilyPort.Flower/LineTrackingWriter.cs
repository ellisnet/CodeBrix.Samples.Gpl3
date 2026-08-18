// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using System.Text;

namespace CodeBrix.LilyPort.Flower;

/// <summary>
/// A writer that remembers whether the last character it wrote was a newline, so that
/// anything else sharing the stream can start on a line of its own.
/// <para>
/// New-in-family, and it exists for one reason: upstream engraves one file per PROCESS,
/// so output left mid-line at the end of a run is finished by the process exiting. This
/// port engraves 2,146 files in one, and whatever comes next lands on the same line.
/// <c>Scheme/lily/graphviz.scm</c> writes a digraph whose last byte is <c>}</c> with no
/// newline after it; on the oracle the next thing written is "Success: compilation
/// successfully completed", which nothing grades, and in a shared log it was a WARNING,
/// which the diagnostics comparator then could not parse — one graded row lost to a
/// missing newline. Ruling R17 puts formatting at that boundary in scope and upstream's
/// own bytes out of it, which is exactly this: the digraph is written unchanged, and the
/// DIAGNOSTIC that follows starts its own line.
/// </para>
/// </summary>
public sealed class LineTrackingWriter : TextWriter
{
    private readonly TextWriter _inner;

    /// <summary>Initializes a new instance wrapping <paramref name="inner"/>.</summary>
    /// <param name="inner">The writer to forward to.</param>
    public LineTrackingWriter(TextWriter inner) => _inner = inner;

    /// <summary>Gets the wrapped writer's encoding.</summary>
    public override Encoding Encoding => _inner.Encoding;

    /// <summary>
    /// Gets a value indicating whether nothing has been written since the last newline.
    /// A writer that has written nothing at all counts as at a line start.
    /// </summary>
    public bool AtLineStart { get; private set; } = true;

    /// <summary>Writes one character.</summary>
    /// <param name="value">The character.</param>
    public override void Write(char value)
    {
        _inner.Write(value);
        AtLineStart = value == '\n';
    }

    /// <summary>Writes a string.</summary>
    /// <param name="value">The string, which may be null or empty.</param>
    public override void Write(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        _inner.Write(value);
        AtLineStart = value[value.Length - 1] == '\n';
    }

    /// <summary>Flushes the wrapped writer.</summary>
    public override void Flush() => _inner.Flush();

    /// <summary>
    /// Ends the current line when one is open, so the caller's next write begins at
    /// column zero. Does nothing when the stream is already at a line start.
    /// </summary>
    public void EndOpenLine()
    {
        if (!AtLineStart)
        {
            Write('\n');
        }
    }
}
