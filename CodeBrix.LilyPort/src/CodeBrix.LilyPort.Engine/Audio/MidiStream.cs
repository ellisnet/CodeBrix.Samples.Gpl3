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
using System.IO;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Audio; //was previously: lily/midi-stream.cc, lily/include/midi-stream.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - Upstream opens a temp file with O_EXCL, retrying ten times against a random name,
//     writes to the file descriptor as it goes, and RENAMES over the destination in its
//     destructor. The port keeps the property that matters -- a reader never sees a
//     half-written MIDI file -- by buffering and writing the temp file atomically on
//     Dispose. It does not reproduce the ten-try loop: File.Move with a GUID-suffixed
//     name in the destination directory cannot collide in the way the O_EXCL retry exists
//     to survive.
//   - IDisposable is how C# spells "the destructor closes and renames it". The
//     using-statement in Performance.WriteOutput is the destructor's scope.

/// <summary>
/// Writes MIDI chunks to a file, appearing at the destination only when complete.
/// </summary>
public sealed class MidiStream : IDisposable
{
    private readonly List<byte> _buffer = new List<byte>();
    private readonly string _destinationFileName;

    private bool _disposed;

    /// <summary>Initializes a stream bound to a destination path.</summary>
    /// <param name="fileName">Where the finished file goes.</param>
    public MidiStream(string fileName)
        => _destinationFileName = fileName
            ?? throw new ArgumentNullException(nameof(fileName));

    /// <summary>Appends raw bytes.</summary>
    /// <param name="bytes">The bytes to append.</param>
    public void Write(byte[] bytes)
    {
        if (bytes != null)
        {
            _buffer.AddRange(bytes);
        }
    }

    /// <summary>Appends a chunk.</summary>
    /// <param name="midi">The chunk to append.</param>
    public void Write(MidiChunk midi) => Write(midi?.ToBytes());

    /// <summary>Returns everything written so far.</summary>
    /// <returns>The bytes.</returns>
    /// <remarks>
    /// Not upstream's — upstream has already handed each write to the operating system by
    /// this point. It exists so a test can assert the BYTES a performance produces
    /// without going through the file system, which is the only way to fence MIDI output
    /// at the level the comparator grades it.
    /// </remarks>
    public byte[] ToBytes() => _buffer.ToArray();

    /// <summary>Closes the stream, putting the finished file at its destination.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        string directory = Path.GetDirectoryName(_destinationFileName);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = _destinationFileName + "." + Guid.NewGuid().ToString("N")
            + ".tmp";

        try
        {
            File.WriteAllBytes(temporary, _buffer.ToArray());
            File.Move(temporary, _destinationFileName, true);
        }
        catch (IOException exception)
        {
            Warn.Error("error writing MIDI file: " + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Warn.Error("error writing MIDI file: " + exception.Message);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
