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
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/performance.cc, lily/include/performance.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - derived_mark() is not carried, for the reason recorded on AudioElement: reachability
//     IS the mark here.
//   - The destructor's `delete element' loop has no analogue.

/// <summary>
/// What interpreting music produced on the MIDI side: every audio element, grouped into
/// the staves that will become tracks.
/// <para>
/// The layout path's twin of this is <see cref="PaperScore"/>. Both are
/// <see cref="MusicOutput"/>s, which is what lets the same toplevel handler in
/// <c>scm/lily.scm</c> drive either one — <c>ly:format-output</c> asks a finished context
/// for its output and calls <see cref="Process"/> on whatever it gets.
/// </para>
/// </summary>
public sealed class Performance : MusicOutput
{
    private readonly List<AudioElement> _audioElements = new List<AudioElement>();
    private readonly List<AudioStaff> _audioStaffs = new List<AudioStaff>();

    private Moment _startMoment;

    /// <summary>Initializes a performance.</summary>
    /// <param name="ports">Whether each track gets a MIDI port meta-event.</param>
    public Performance(bool ports = false)
    {
        // Upstream opens at +infinity so the first real column always wins the minimum.
        _startMoment = new Moment(Rational.Infinity);
        Midi = null;
        Ports = ports;
        Headers = Nil.Instance;
    }

    /// <summary>Gets the staves that will become MIDI tracks, in track order.</summary>
    public IList<AudioStaff> AudioStaffs => _audioStaffs;

    /// <summary>Gets or sets the <c>\midi</c> output definition this was performed under.</summary>
    public OutputDef Midi { get; set; }

    /// <summary>Gets or sets a value indicating whether each track gets a port meta-event.</summary>
    public bool Ports { get; set; }

    /// <summary>Gets the list of headers, innermost first.</summary>
    public object Headers { get; private set; }

    /// <summary>Gets the C++ class name this output corresponds to.</summary>
    public override string ClassName => "Performance";

    /// <summary>Adds a header to the front of the list, making it the innermost.</summary>
    /// <param name="header">The header module.</param>
    public void PushHeader(object header)
    {
        Debug.Assert(header is SchemeModule, "a header must be a module");
        Headers = new Pair(header, Headers);
    }

    /// <summary>Records an audio element and the event that caused it.</summary>
    /// <param name="element">The element.</param>
    /// <param name="cause">The causing event, or <see langword="null"/>.</param>
    public void AddElement(AudioElement element, StreamEvent cause)
    {
        _audioElements.Add(element);
        element.Cause = cause;
    }

    /// <summary>
    /// Finds the moment the performance starts at, so every staff begins on the same
    /// tick.
    /// </summary>
    public override void Process()
    {
        // TODO: Could this be done on the fly rather than in a separate pass?
        // (upstream's own question, kept)
        foreach (AudioElement element in _audioElements)
        {
            if (element is AudioItem item)
            {
                AudioColumn column = item.GetColumn();
                if (column != null && column.When() < _startMoment)
                {
                    _startMoment = column.When();
                }
            }
        }
    }

    /// <summary>Writes this performance to a MIDI file.</summary>
    /// <param name="output">The path to write to; <c>-</c> means <c>lelie.midi</c>.</param>
    /// <param name="performanceName">The name to store in the file's metadata.</param>
    public void WriteOutput(string output, string performanceName)
    {
        if (output == "-")
        {
            output = "lelie.midi";
        }

        /* Maybe a bit crude, but we had this before */
        output = new FileName(output).ToString();

        if (_audioStaffs.Count == 0)
        {
            // The only known way to get here is to skip the entire piece with
            // skipTypesetting.
            Warn.Warning("cannot create a zero-track MIDI file; skipping `" + output + "'");
            return;
        }

        Warn.Message("MIDI output to `" + output + "'...");

        using (MidiStream midiStream = new MidiStream(output))
        {
            Output(midiStream, performanceName);
        }

        object afterWriting = Midi?.CVariable("after-writing");
        if (afterWriting != null && SchemeUtilities.IsProcedure(afterWriting))
        {
            SchemeUtilities.CallCallback(afterWriting, this, output);
        }
    }

    /// <summary>
    /// Renders this performance into a stream: the file header, then one track per staff.
    /// </summary>
    /// <param name="midiStream">The stream to write into.</param>
    /// <param name="performanceName">The name to store in the file's metadata.</param>
    public void Output(MidiStream midiStream, string performanceName)
    {
        if (_audioStaffs.Count > ushort.MaxValue)
        {
            Warn.ProgrammingError("too many MIDI tracks: " + _audioStaffs.Count);
        }

        ushort numTracks = (ushort)_audioStaffs.Count;

        midiStream.Write(new MidiHeader(1, numTracks, 384));

        for (ushort i = 0; i < numTracks; ++i)
        {
            AudioStaff staff = _audioStaffs[i];
            if (staff is AudioControlTrackStaff controlTrack)
            {
                // The control track, created by Control_track_performer, should contain a
                // placeholder for the name of the MIDI sequence as its initial audio
                // element. Fill in the name of the sequence to this element before
                // outputting MIDI.
                Debug.Assert(
                    controlTrack.AudioItems.Count != 0, "the control track is empty");

                AudioText text = controlTrack.AudioItems[0] as AudioText;
                Debug.Assert(text != null, "the control track does not open with a text");
                Debug.Assert(
                    text.TextType == AudioTextType.TrackName,
                    "the control track does not open with a track name");
                Debug.Assert(
                    text.TextString == "control track",
                    "the control track's placeholder has been overwritten");

                if (!string.IsNullOrEmpty(performanceName))
                {
                    text.TextString = performanceName;
                    controlTrack.TrackName = performanceName;
                }
                else
                {
                    // This is an efficiency misdemeanor, but the control track is not
                    // expected to be humongous in size.
                    controlTrack.RemoveAudioItemAt(0);
                }
            }

            staff.Output(midiStream, i, Ports, _startMoment);
        }
    }
}
