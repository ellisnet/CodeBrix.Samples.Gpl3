/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyPort.Engine.Music;

namespace CodeBrix.LilyPort.Engine.Audio; //was previously: lily/audio-staff.cc, lily/include/audio-staff.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port.

/// <summary>
/// One MIDI track's worth of audio items, and the thing that knows how to write itself
/// out as a track.
/// </summary>
public class AudioStaff : AudioElement
{
    private readonly List<AudioItem> _audioItems = new List<AudioItem>();

    /// <summary>Initializes an empty staff.</summary>
    public AudioStaff()
    {
        Percussion = false;
        MergeUnisons = false;
        TrackName = string.Empty;
    }

    /// <summary>Gets or sets the moment this staff stops sounding.</summary>
    public Moment EndMoment { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this staff plays on the percussion
    /// channel.
    /// </summary>
    public bool Percussion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether simultaneous equal pitches are merged into
    /// one note rather than being sounded twice.
    /// </summary>
    public bool MergeUnisons { get; set; }

    /// <summary>Gets or sets this staff's track name, which is also used for tracing.</summary>
    public string TrackName { get; set; }

    /// <summary>Gets the items on this staff.</summary>
    public IReadOnlyList<AudioItem> AudioItems => _audioItems;

    /// <summary>Gets the C++ class name this element corresponds to.</summary>
    public override string ClassName => "Audio_staff";

    /// <summary>Adds an item to this staff.</summary>
    /// <param name="item">The item to add.</param>
    public void AddAudioItem(AudioItem item) => _audioItems.Add(item);

    /// <summary>
    /// Removes the item at an index.
    /// <para>
    /// Exists for exactly one caller: <c>Performance::output</c> erases the control
    /// track's name placeholder when the performance has no name to put there. Upstream
    /// calls it "an efficiency misdemeanor, but the control track is not expected to be
    /// humongous in size".
    /// </para>
    /// </summary>
    /// <param name="index">The index to remove.</param>
    internal void RemoveAudioItemAt(int index) => _audioItems.RemoveAt(index);

    /// <summary>Writes this staff out as one MIDI track.</summary>
    /// <param name="midiStream">The stream to write to.</param>
    /// <param name="track">The track number.</param>
    /// <param name="port">Whether to emit a MIDI port meta-event for the track.</param>
    /// <param name="startMoment">Where the performance as a whole begins.</param>
    public void Output(MidiStream midiStream, int track, bool port, Moment startMoment)
    {
        MidiTrack midiTrack = new MidiTrack(track, port);

        MidiWalker walker = new MidiWalker(this, midiTrack, AudioMoment.ToTicks(startMoment));
        for (; walker.Ok(); walker.Advance())
        {
            walker.Process();
        }

        walker.FinalizeTrack(AudioMoment.ToTicks(EndMoment));

        midiStream.Write(midiTrack);
    }
}

/// <summary>
/// The staff that represents a MIDI sequence's control track, which
/// <see cref="Translation.ControlTrackPerformer"/> creates.
/// </summary>
/// <remarks>
/// Upstream makes this a distinct subtype for one reason and it is worth keeping in
/// sight: <c>Performance::output</c> uses the TYPE to find the control track among the
/// staves, so it can fill the sequence name into the placeholder the performer left
/// there.
/// </remarks>
public sealed class AudioControlTrackStaff : AudioStaff
{
    /// <summary>Gets the C++ class name this element corresponds to.</summary>
    public override string ClassName => "Audio_control_track_staff";
}
