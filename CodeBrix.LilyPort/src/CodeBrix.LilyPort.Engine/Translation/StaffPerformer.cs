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
using System.Linq;
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/staff-performer.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - THE THREE STATIC MEMBERS ARE UPSTREAM'S AND SO IS THE MECHANISM THAT CLEARS THEM,
//     but the port is exposed to them in a way upstream is not. Upstream gets one process
//     per input file; this port sweeps 2,146 files in ONE process, and per-file state that
//     is not restored is the exact per-file leak class chased three times over (\paper,
//     $defaultlayout, \language). Upstream's own guard -- the last Staff_performer to
//     finalize clears them -- is reproduced faithfully AND ResetStaticChannelState is
//     added for LilyPondInit.RestoreDefaults to call, because a score that dies before
//     finalize would otherwise hand its channel assignments to the next file in the
//     sweep. Upstream's own comment says these "would prefer to be members of the
//     containing class Performance"; this is that wish, minus the restructuring.
//   - UPSTREAM'S `tempo_' MEMBER IS DEAD AND IS NOT CARRIED. Staff_performer declares
//     `Audio_tempo *tempo_ = nullptr;' and the ONLY statement anywhere that touches it is
//     stop_translation_timestep's `tempo_ = nullptr;' -- nothing ever reads it and nothing
//     ever assigns it a value. Reproducing it dead would cost a warning (CS0414) for a
//     field that cannot affect anything; it is recorded here instead, as PORT-COVERAGE
//     requires for upstream dead code.
//   - staff_map_ is a std::unordered_map upstream, whose iteration order is unspecified;
//     get_audio_staff's `staff_map_.begin ()->second' therefore picks an ARBITRARY staff
//     when more than one is present. The port uses insertion order, which is at least
//     reproducible. Recorded in PORT-COVERAGE.

/// <summary>
/// Performs one staff: it assigns every note announced below it to a MIDI track and
/// channel, and emits the instrument the staff plays.
/// </summary>
public sealed class StaffPerformer : Performer
{
    private static readonly Symbol MidiChannelMappingSymbol
        = Symbol.Intern("midiChannelMapping");
    private static readonly Symbol MidiMergeUnisonsSymbol = Symbol.Intern("midiMergeUnisons");
    private static readonly Symbol MidiSkipOffsetSymbol = Symbol.Intern("midiSkipOffset");
    private static readonly Symbol MidiInstrumentSymbol = Symbol.Intern("midiInstrument");
    private static readonly Symbol InstrumentSymbol = Symbol.Intern("instrument");
    private static readonly Symbol StaffSymbol = Symbol.Intern("staff");
    private static readonly Symbol VoiceSymbol = Symbol.Intern("voice");
    private static readonly Symbol VoiceContextSymbol = Symbol.Intern("Voice");
    private static readonly Symbol PercussionSymbol = Symbol.Intern("percussion?");

    // Would prefer to have the following two items be members of the containing class
    // Performance, so they can be reset for each new midi file output.
    // (upstream's comment, kept)
    private static readonly Dictionary<string, int> StaticChannelMap
        = new Dictionary<string, int>();

    private static int _channelCount;

    // For now, ask the last Staff_performer clean up during its finalize method
    private static int _staffPerformerCount;

    private readonly Dictionary<string, AudioStaff> _staffMap
        = new Dictionary<string, AudioStaff>();

    private readonly Dictionary<string, int> _channelMap = new Dictionary<string, int>();

    private string _instrumentString = string.Empty;
    private int _channel = -1;
    private AudioInstrument _instrument;
    private AudioText _instrumentName;
    private AudioText _name;

    /// <summary>Initializes a staff performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public StaffPerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Staff_performer";

    /// <summary>
    /// Clears the process-global channel assignments.
    /// </summary>
    /// <remarks>
    /// PORT-ONLY, and it exists because the port sweeps many files in one process. See
    /// the note at the top of this file.
    /// </remarks>
    public static void ResetStaticChannelState()
    {
        StaticChannelMap.Clear();
        _channelCount = 0;
        _staffPerformerCount = 0;
    }

    /// <summary>Counts this performer in, so the last one out can clean up.</summary>
    public override void Initialize() => ++_staffPerformerCount;

    /// <summary>Does nothing, exactly as upstream's does.</summary>
    public override void ProcessMusic()
    {
    }

    /// <summary>Forgets the elements made during this timestep.</summary>
    public override void StopTranslationTimestep()
    {
        _name = null;
        _instrumentName = null;
        _instrument = null;
    }

    /// <summary>Closes every track this staff owns, and cleans up if it is the last one.</summary>
    public override void FinalizeTranslation()
    {
        Moment endMoment = NowMoment
            + (GetProperty(MidiSkipOffsetSymbol) is Moment offset ? offset : Moment.Zero);

        foreach (AudioStaff staff in _staffMap.Values)
        {
            staff.EndMoment = endMoment;
        }

        _staffMap.Clear();
        _channelMap.Clear();

        if (_staffPerformerCount != 0)
        {
            --_staffPerformerCount;
        }

        if (_staffPerformerCount == 0)
        {
            StaticChannelMap.Clear();
            _channelCount = 0;
        }
    }

    /// <summary>
    /// Assigns an announced element to a track and a channel.
    /// <para>
    /// This is where one MIDI track per Voice is arranged: the announcing context chain's
    /// innermost entry names the voice, and every audio item announced under it is added
    /// to that voice's staff.
    /// </para>
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeAudioElement(AudioElementInfo info)
    {
        /* map each context (voice) to its own track */
        List<Context> contexts = info.OriginContexts(this);
        Context context = contexts.Count > 0 ? contexts[0] : null;

        string voice = string.Empty;
        if (context != null && context.IsAlias(VoiceContextSymbol))
        {
            voice = context.IdString ?? string.Empty;
        }

        object channelMapping = GetProperty(MidiChannelMappingSymbol);
        string str = NewInstrumentString();

        if (!ReferenceEquals(channelMapping, InstrumentSymbol))
        {
            _channel = GetChannel(voice);
        }
        else if (_channel < 0 && str.Length == 0)
        {
            _channel = GetChannel(str);
        }

        if (str.Length != 0)
        {
            if (!ReferenceEquals(channelMapping, VoiceSymbol))
            {
                _channel = GetChannel(str);
            }

            SetInstrument(_channel, voice);
            SetInstrumentName(voice);
        }

        AudioStaff audioStaff = GetAudioStaff(voice);
        if (info.Element is AudioItem item)
        {
            item.Channel = _channel;
            audioStaff.AddAudioItem(item);
        }
    }

    /// <summary>Creates the track for a voice, naming it and setting its initial controls.</summary>
    private AudioStaff NewAudioStaff(string voice)
    {
        AudioStaff audioStaff = new AudioStaff
        {
            MergeUnisons = SchemeUtilities.ToBool(GetProperty(MidiMergeUnisonsSymbol)),
        };

        audioStaff.TrackName = (Context?.IdString ?? string.Empty) + ":" + voice;

        if (audioStaff.TrackName != ":")
        {
            _name = new AudioText(AudioTextType.TrackName, audioStaff.TrackName);
            audioStaff.AddAudioItem(_name);
            AnnounceElement(new AudioElementInfo(_name, null));
        }
        else
        {
            audioStaff.TrackName = string.Empty;
        }

        AnnounceElement(new AudioElementInfo(audioStaff, null));
        _staffMap[voice] = audioStaff;

        if (_instrumentString.Length != 0)
        {
            SetInstrument(_channel, voice);
        }

        // Set initial values (if any) for MIDI controls.
        MidiControlInitializer initializer
            = new MidiControlInitializer(this, audioStaff, _channel);
        initializer.AnnounceControlChanges();

        return audioStaff;
    }

    /// <summary>Finds the track for a voice, making one when the mapping calls for it.</summary>
    private AudioStaff GetAudioStaff(string voice)
    {
        object channelMapping = GetProperty(MidiChannelMappingSymbol);
        if (!ReferenceEquals(channelMapping, InstrumentSymbol) && _staffMap.Count != 0)
        {
            return _staffMap.Values.First();
        }

        if (_staffMap.TryGetValue(voice, out AudioStaff found))
        {
            return found;
        }

        if (_staffMap.Count == 1 && _staffMap.TryGetValue(string.Empty, out AudioStaff empty))
        {
            _staffMap[voice] = empty;
            return empty;
        }

        return NewAudioStaff(voice);
    }

    /// <summary>Emits the instrument this staff now plays.</summary>
    private void SetInstrument(int channel, string voice)
    {
        _instrument = new AudioInstrument(_instrumentString) { Channel = channel };
        AnnounceElement(new AudioElementInfo(_instrument, null));

        AudioStaff audioStaff = GetAudioStaff(voice);
        audioStaff.AddAudioItem(_instrument);

        object procedure = LilyPondScheme.LookupProcedure(PercussionSymbol);
        object drums = procedure == null
            ? null
            : SchemeUtilities.CallCallback(procedure, Symbol.Intern(_instrumentString));

        audioStaff.Percussion = SchemeUtilities.ToBool(drums);
    }

    /// <summary>Emits the instrument-name meta-event for this staff.</summary>
    private void SetInstrumentName(string voice)
    {
        _instrumentName = new AudioText(AudioTextType.InstrumentName, _instrumentString);
        AnnounceElement(new AudioElementInfo(_instrumentName, null));
        GetAudioStaff(voice).AddAudioItem(_instrumentName);
    }

    /// <summary>
    /// Returns the instrument name when it has just changed, and the empty string
    /// otherwise.
    /// </summary>
    private string NewInstrumentString()
    {
        // mustn't ask Score for instrument: it will return piano!
        object instrument = GetProperty(MidiInstrumentSymbol);

        string text = instrument is MutableString mutable ? mutable.ToString()
            : instrument as string;

        if (text == null || text == _instrumentString)
        {
            return string.Empty;
        }

        _instrumentString = text;
        return _instrumentString;
    }

    /// <summary>Assigns a MIDI channel, following the <c>midiChannelMapping</c> policy.</summary>
    private int GetChannel(string instrument)
    {
        object channelMapping = GetProperty(MidiChannelMappingSymbol);
        Dictionary<string, int> channelMap
            = !ReferenceEquals(channelMapping, InstrumentSymbol)
                ? _channelMap
                : StaticChannelMap;

        if (ReferenceEquals(channelMapping, StaffSymbol) && _channel >= 0)
        {
            return _channel;
        }

        if (channelMap.TryGetValue(instrument, out int found))
        {
            return found;
        }

        int channel = ReferenceEquals(channelMapping, StaffSymbol)
            ? _channelCount++
            : channelMap.Count;

        /* MIDI players tend to ignore instrument settings on channel
           10, the percussion channel.  */
        if (channel % 16 == 9)
        {
            // TODO: Shouldn't this assign 9 rather than channel++?
            //
            // TODO: A hard-coded percussion entry ought to be created at the beginning,
            // otherwise an early lookup of the key might cause it to be allocated an
            // unexpected value. Fixing this requires decoupling the next channel number
            // from the map size.
            //
            // TODO: Should this entry really be created for any case of channel mapping,
            // or perhaps only for the per-instrument case?
            // (upstream's three TODOs, kept)
            channelMap["percussion"] = channel++;

            // TODO: Above, channel_count_ is incremented in the per-staff case only;
            // should that be considered here as well?
            _channelCount++;
        }

        if (channel > 15) // TODO: warn the first time only, maybe
        {
            Warn.Warning("MIDI channel wrapped around");
            Warn.Warning("remapping modulo 16");
            channel = channel % 16;
        }

        channelMap[instrument] = channel;
        return channel;
    }

    /// <summary>
    /// Reads the MIDI-control context properties when a track is created, so the track
    /// starts with the balance, pan and expression the score asked for.
    /// </summary>
    private sealed class MidiControlInitializer : MidiControlChangeAnnouncer
    {
        private readonly StaffPerformer _performer;
        private readonly AudioStaff _audioStaff;
        private readonly int _channel;

        internal MidiControlInitializer(
            StaffPerformer performer, AudioStaff audioStaff, int channel)
        {
            _performer = performer;
            _audioStaff = audioStaff;
            _channel = channel;
        }

        protected override object GetPropertyValue(string propertyName)
            => _performer.GetProperty(propertyName);

        protected override void DoAnnounce(AudioControlChange item)
        {
            item.Channel = _channel;
            _audioStaff.AddAudioItem(item);
            _performer.AnnounceElement(new AudioElementInfo(item, null));
        }
    }
}
