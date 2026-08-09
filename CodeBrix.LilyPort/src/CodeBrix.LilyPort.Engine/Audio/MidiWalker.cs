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

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Audio; //was previously: lily/midi-walker.cc, lily/include/midi-walker.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - operator++(int) becomes Advance(). C# can overload ++ but not meaningfully on a
//     class used through a `for (; w.Ok (); w++)' idiom, and a named method is what the
//     rest of the engine does for upstream's iterator-shaped types.
//   - finalize() becomes FinalizeTrack(), for the same reason Translator::finalize became
//     FinalizeTranslation: `Finalize' is the C# destructor.
//   - The stop-note queue is Flower's PriorityQueue, which is upstream's PQueue and
//     therefore libstdc++'s heap. The tie-breaking is load bearing here: every chord ends
//     its notes on the same tick, and which one comes out first is which note-off byte is
//     written first.

/// <summary>
/// A note waiting to be stopped: the tick it stops at, and the note-off to emit.
/// </summary>
/// <remarks>
/// Upstream's <c>Midi_note_event</c> is a <c>PQueue_ent&lt;int, Midi_note *&gt;</c>, whose
/// <c>key</c> is the stop tick and whose <c>val</c> is the note. Both names are kept.
/// </remarks>
public sealed class MidiNoteEvent
{
    /// <summary>Gets or sets the tick this note stops at.</summary>
    public int Key { get; set; }

    /// <summary>Gets or sets the note-off to emit.</summary>
    public MidiNote Val { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this entry has been superseded and should
    /// be skipped when it surfaces.
    /// </summary>
    /// <remarks>
    /// Upstream mutates this THROUGH THE HEAP INDEXER, which is safe only because it is
    /// not part of the ordering key. See <see cref="PriorityQueue{T}"/>.
    /// </remarks>
    public bool Ignore { get; set; }
}

/// <summary>Orders stop-note entries by their stop tick — upstream's free <c>compare</c>.</summary>
public sealed class MidiNoteEventComparer : IComparer<MidiNoteEvent>
{
    /// <summary>The one instance needed.</summary>
    public static readonly MidiNoteEventComparer Instance = new MidiNoteEventComparer();

    /// <summary>Compares two entries by stop tick.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns>-1, 0 or 1.</returns>
    public int Compare(MidiNoteEvent left, MidiNoteEvent right)
    {
        int difference = left.Key - right.Key;
        return difference < 0 ? -1 : difference > 0 ? 1 : 0;
    }
}

/// <summary>
/// Walks one <see cref="AudioStaff"/> in time order and emits the MIDI events for it.
/// <para>
/// This is where a note's LENGTH becomes a pair of events: the note-on goes out as the
/// item is reached, and a note-off is queued for the tick the note ends at, to be emitted
/// when the walk passes it.
/// </para>
/// </summary>
public sealed class MidiWalker
{
    private readonly MidiTrack _track;
    private readonly bool _percussion;
    private readonly bool _mergeUnisons;
    private readonly List<AudioItem> _items;
    private readonly PriorityQueue<MidiNoteEvent> _stopNoteQueue
        = new PriorityQueue<MidiNoteEvent>(MidiNoteEventComparer.Instance);

    private int _index;
    private int _lastTick;

    /// <summary>Initializes a walker over a staff.</summary>
    /// <param name="audioStaff">The staff to walk.</param>
    /// <param name="track">The track to write into.</param>
    /// <param name="startTick">The tick the whole performance starts at.</param>
    public MidiWalker(AudioStaff audioStaff, MidiTrack track, int startTick)
    {
        _track = track;
        _index = 0;

        // A STABLE sort, and the stability is upstream's: items at the same moment keep
        // the order the performers announced them in, which is what puts an instrument
        // change before the note it applies to.
        _items = audioStaff.AudioItems
            .OrderBy(item => item.GetColumn().WhenMoment, Comparer<Moment>.Default)
            .ToList();

        // Scores that begin with grace notes start at negative times. This is OK — MIDI
        // output doesn't use absolute ticks, only differences.
        _lastTick = startTick;
        _percussion = audioStaff.Percussion;
        _mergeUnisons = audioStaff.MergeUnisons;
    }

    /// <summary>Returns whether there is anything left to walk.</summary>
    /// <returns><see langword="true"/> while items remain.</returns>
    public bool Ok() => _index < _items.Count;

    /// <summary>Moves to the next item.</summary>
    public void Advance()
    {
        Debug.Assert(Ok(), "walked past the end of the staff");
        _index++;
    }

    /// <summary>Emits the events for the item the walk is standing on.</summary>
    public void Process()
    {
        AudioItem audio = _items[_index];
        AudioColumn column = audio.GetColumn();
        DoStopNotes(column.Ticks());

        MidiItem midi = GetMidi(audio);
        if (midi == null)
        {
            return;
        }

        if (midi is MidiNote note)
        {
            if (note.AudioNote.LengthMoment.IsNonZero)
            {
                DoStartNote(note);
            }
        }
        else
        {
            OutputEvent(audio.AudioColumn.Ticks(), midi);
        }
    }

    /// <summary>Stops every note still sounding and closes the track.</summary>
    /// <param name="endTick">The tick the staff ends at.</param>
    public void FinalizeTrack(int endTick)
    {
        DoStopNotes(int.MaxValue);
        int deltaTicks = endTick >= _lastTick ? endTick - _lastTick : 0;
        _track.PushBack(deltaTicks, new MidiEndOfTrack());
    }

    /// <summary>
    /// Emits a note-on, unless an equal pitch is already sounding and should absorb it.
    /// </summary>
    /// <param name="note">The note starting.</param>
    private void DoStartNote(MidiNote note)
    {
        AudioItem item = _items[_index];
        Debug.Assert(ReferenceEquals(note.AudioNote, item), "walker lost its place");

        int nowTicks = item.AudioColumn.Ticks();
        int stopTicks = (int)(AudioMoment.ToReal(note.AudioNote.LengthMoment) * (384 * 4))
            + nowTicks;

        for (int i = 0; i < _stopNoteQueue.Count; i++)
        {
            MidiNoteEvent queued = _stopNoteQueue[i];

            /* if this pitch already in queue, and is not already ignored */
            if (!queued.Ignore
                && queued.Val.GetSemitonePitch() == note.GetSemitonePitch())
            {
                int queuedTicks = queued.Val.AudioNote.AudioColumn.Ticks();

                // If the two notes started at the same time, or option is set,
                if (nowTicks == queuedTicks || _mergeUnisons)
                {
                    // merge them.
                    if (queued.Key < stopTicks)
                    {
                        MidiNoteEvent extended = new MidiNoteEvent
                        {
                            Val = queued.Val,
                            Key = stopTicks,
                        };

                        queued.Ignore = true;
                        _stopNoteQueue.Insert(extended);
                    }

                    note = null;
                    break;
                }

                // A note was played that interrupted a played note. Stop the old note,
                // and continue to the greatest moment between the two.
                if (queued.Key > stopTicks)
                {
                    stopTicks = queued.Key;
                }

                OutputEvent(nowTicks, queued.Val);
                queued.Ignore = true;
                break;
            }
        }

        if (note != null)
        {
            MidiNoteEvent stopping = new MidiNoteEvent
            {
                Val = new MidiNoteOff(note),
                Key = stopTicks,
            };

            _stopNoteQueue.Insert(stopping);

            OutputEvent(nowTicks, note);
        }
    }

    /// <summary>Emits every queued note-off that falls at or before a tick.</summary>
    /// <param name="maxTicks">The tick to stop notes up to.</param>
    private void DoStopNotes(int maxTicks)
    {
        while (_stopNoteQueue.Count != 0 && _stopNoteQueue.Front().Key <= maxTicks)
        {
            MidiNoteEvent stopping = _stopNoteQueue.DeleteMinimum();
            if (stopping.Ignore)
            {
                continue;
            }

            OutputEvent(stopping.Key, stopping.Val);
        }
    }

    /// <summary>Adds an event to the track, converting absolute ticks to a delta.</summary>
    /// <param name="nowTicks">The absolute tick the event happens at.</param>
    /// <param name="item">The item to add.</param>
    private void OutputEvent(int nowTicks, MidiItem item)
    {
        int deltaTicks = nowTicks - _lastTick;
        _lastTick = nowTicks;

        /*
          this is not correct, but at least it doesn't crash when you
          start with graces
        */
        if (deltaTicks < 0)
        {
            Warn.ProgrammingError("Going back in MIDI time.");
            deltaTicks = 0;
        }

        _track.Add(deltaTicks, item);
    }

    /// <summary>Makes the MIDI item for an audio item, forcing percussion onto channel 9.</summary>
    /// <param name="item">The audio item.</param>
    /// <returns>The MIDI item, or <see langword="null"/> when it emits nothing.</returns>
    private MidiItem GetMidi(AudioItem item)
    {
        MidiItem midi = MidiItem.GetMidi(item);

        if (_percussion && midi is MidiChannelItem channelItem)
        {
            channelItem.Channel = 9;
        }

        return midi;
    }
}
