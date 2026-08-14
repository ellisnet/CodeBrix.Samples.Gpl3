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
using System.Diagnostics;
using System.Text;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Audio; //was previously: lily/midi-chunk.cc, lily/include/midi-chunk.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - to_string() becomes ToBytes() throughout, for the reason recorded at the top of
//     MidiItem.cs: a std::string is a byte container and a System.String is not.

/// <summary>One MIDI event: how long after the previous one, and what it is.</summary>
public sealed class MidiEvent
{
    /// <summary>Initializes an event.</summary>
    /// <param name="deltaTicks">Ticks since the previous event on this track.</param>
    /// <param name="midi">The item this event carries.</param>
    public MidiEvent(int deltaTicks, MidiItem midi)
    {
        DeltaTicks = deltaTicks;
        Midi = midi;
    }

    /// <summary>Gets or sets the ticks since the previous event on this track.</summary>
    public int DeltaTicks { get; set; }

    /// <summary>Gets the item this event carries.</summary>
    public MidiItem Midi { get; }

    /// <summary>Returns the event's bytes: its delta time, then the item.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToBytes()
    {
        byte[] delta = MidiItem.Int2MidiVarintBytes(DeltaTicks);
        byte[] midi = Midi.ToBytes();

        byte[] result = new byte[delta.Length + midi.Length];
        Buffer.BlockCopy(delta, 0, result, 0, delta.Length);
        Buffer.BlockCopy(midi, 0, result, delta.Length, midi.Length);
        return result;
    }
}

/// <summary>A length-prefixed MIDI chunk: a header, a track.</summary>
public class MidiChunk
{
    private byte[] _dataBytes = Array.Empty<byte>();
    private byte[] _footerBytes = Array.Empty<byte>();
    private byte[] _headerBytes = Array.Empty<byte>();

    /// <summary>Gets the C++ class name this chunk corresponds to.</summary>
    public virtual string ClassName => "Midi_chunk";

    /// <summary>Sets the chunk's three parts.</summary>
    /// <param name="header">The four-character chunk tag.</param>
    /// <param name="data">The chunk body.</param>
    /// <param name="footer">Anything after the body.</param>
    public void Set(string header, byte[] data, byte[] footer)
    {
        _headerBytes = Encoding.ASCII.GetBytes(header ?? string.Empty);
        _dataBytes = data ?? Array.Empty<byte>();
        _footerBytes = footer ?? Array.Empty<byte>();
    }

    /// <summary>Returns the chunk body.</summary>
    /// <returns>The body bytes.</returns>
    public virtual byte[] DataBytes() => _dataBytes;

    /// <summary>Returns the whole chunk: tag, big-endian length, body, footer.</summary>
    /// <returns>The bytes.</returns>
    public virtual byte[] ToBytes()
    {
        byte[] data = DataBytes();
        uint total = (uint)(data.Length + _footerBytes.Length);

        List<byte> bytes = new List<byte>(_headerBytes.Length + 4 + (int)total);
        bytes.AddRange(_headerBytes);
        bytes.AddRange(StringConvert.BigEndianBytesU32(total));
        bytes.AddRange(data);
        bytes.AddRange(_footerBytes);

        return bytes.ToArray();
    }
}

/// <summary>The <c>MThd</c> chunk that opens a MIDI file.</summary>
public sealed class MidiHeader : MidiChunk
{
    /// <summary>Initializes a file header.</summary>
    /// <param name="format">The SMF format number.</param>
    /// <param name="tracks">How many tracks follow.</param>
    /// <param name="clocksPer4">Ticks per quarter note.</param>
    public MidiHeader(int format, int tracks, int clocksPer4)
    {
        List<byte> bytes = new List<byte>(6);
        bytes.AddRange(StringConvert.BigEndianBytesU16((ushort)format));
        bytes.AddRange(StringConvert.BigEndianBytesU16((ushort)tracks));
        bytes.AddRange(StringConvert.BigEndianBytesU16((ushort)clocksPer4));

        Set("MThd", bytes.ToArray(), Array.Empty<byte>());
    }

    /// <summary>Gets the C++ class name this chunk corresponds to.</summary>
    public override string ClassName => "Midi_header";
}

/// <summary>An <c>MTrk</c> chunk: one track's worth of events.</summary>
public sealed class MidiTrack : MidiChunk
{
    private readonly List<MidiEvent> _events = new List<MidiEvent>();

    /// <summary>Initializes a track.</summary>
    /// <param name="number">The track number.</param>
    /// <param name="port">Whether to emit a MIDI port meta-event naming the track.</param>
    public MidiTrack(int number, bool port)
    {
        Number = number;

        //                4D 54 72 6B     MTrk
        //                00 00 00 3B     chunk length (59)
        //        00      FF 58 04 04 02 18 08    time signature
        //        00      FF 51 03 07 A1 20       tempo
        //
        // FF 59 02 sf mi  Key Signature
        //         sf = -7:  7 flats
        //         sf = -1:  1 flat
        //         sf = 0:  key of C
        //         sf = 1:  1 sharp
        //         sf = 7: 7 sharps
        //         mi = 0:  major key
        //         mi = 1:  minor key
        //
        // (Upstream's commentary, kept. Its data_str0 is the empty string: the commented
        //  out hex above it is a record of what a format-0 file would have carried, and
        //  LilyPond writes format 1.)
        List<byte> data = new List<byte>(5);
        data.AddRange(StringConvert.HexToBytes(string.Empty));

        if (port)
        {
            data.AddRange(new byte[] { 0x00, 0xFF, 0x21, 0x01, (byte)number });
        }

        Set("MTrk", data.ToArray(), Array.Empty<byte>());
    }

    /// <summary>Gets the track number.</summary>
    public int Number { get; }

    /// <summary>Gets the events on this track, in the order they will be written.</summary>
    public IReadOnlyList<MidiEvent> Events => _events;

    /// <summary>Gets the C++ class name this chunk corresponds to.</summary>
    public override string ClassName => "Midi_track";

    /// <summary>Appends an event without reordering anything.</summary>
    /// <param name="deltaTicks">Ticks since the previous event.</param>
    /// <param name="midi">The item to add.</param>
    public void PushBack(int deltaTicks, MidiItem midi)
    {
        Debug.Assert(deltaTicks >= 0, "delta ticks must not be negative");
        _events.Add(new MidiEvent(deltaTicks, midi));
    }

    /// <summary>
    /// Adds an event, placing it before any notes that already start at the same time.
    /// <para>
    /// This is what makes an instrument change take effect before the note it applies to.
    /// When the new event happens at the same instant as the most recent ones and is not
    /// itself the start of a note, it is walked backwards past every note-on already
    /// queued at that instant. The delta-tick SWAP in the middle is the subtle part: an
    /// event is inserted in front of one that carried a non-zero delta, so the two must
    /// exchange deltas or the whole track shifts in time.
    /// </para>
    /// </summary>
    /// <param name="deltaTicks">Ticks since the previous event.</param>
    /// <param name="midi">The item to add.</param>
    public void Add(int deltaTicks, MidiItem midi)
    {
        Debug.Assert(deltaTicks >= 0, "delta ticks must not be negative");

        MidiEvent added = new MidiEvent(deltaTicks, midi);

        // Insertion position for the new event in the track.
        int position = _events.Count;
        if (deltaTicks == 0 && (!(midi is MidiNote) || midi is MidiNoteOff))
        {
            while (position != 0)
            {
                int previous = position - 1;
                MidiItem previousMidi = _events[previous].Midi;

                if (!(previousMidi is MidiNote) || previousMidi is MidiNoteOff)
                {
                    // Found an event that does not represent the start of a note. Exit
                    // the loop to insert the new event in the track after this event.
                    break;
                }

                if (_events[previous].DeltaTicks != 0)
                {
                    // Found the start of a new note with a non-zero delta. Insert the new
                    // event before it, swapping the deltas to keep the sequence of deltas
                    // consistent.
                    added.DeltaTicks = _events[previous].DeltaTicks;
                    _events[previous].DeltaTicks = 0;
                    position = previous;
                    break;
                }

                // Otherwise, the event in the track is the start of a note occurring at
                // the same time as the new event: continue searching.
                position = previous;
            }
        }

        _events.Insert(position, added);
    }

    /// <summary>Returns the track body: the chunk's own data, then every event.</summary>
    /// <returns>The bytes.</returns>
    public override byte[] DataBytes()
    {
        List<byte> bytes = new List<byte>(base.DataBytes());

        foreach (MidiEvent midiEvent in _events)
        {
            bytes.AddRange(midiEvent.ToBytes());
        }

        return bytes.ToArray();
    }
}
