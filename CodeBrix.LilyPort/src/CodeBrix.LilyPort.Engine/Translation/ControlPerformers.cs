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

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/piano-pedal-performer.cc, lily/midi-cc-performer.cc, lily/control-track-performer.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>Turns pedal events into MIDI pedal control changes.</summary>
public sealed class PianoPedalPerformer : Performer
{
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol SustainEventSymbol = Symbol.Intern("sustain-event");
    private static readonly Symbol SostenutoEventSymbol = Symbol.Intern("sostenuto-event");
    private static readonly Symbol UnaCordaEventSymbol = Symbol.Intern("una-corda-event");

    private const int PedalTypeCount = 3;

    private readonly PedalInfo[] _infoAlist = new PedalInfo[PedalTypeCount];
    private readonly List<AudioPianoPedal> _audios = new List<AudioPianoPedal>();

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public PianoPedalPerformer(Context context)
        : base(context)
    {
        for (int i = 0; i < PedalTypeCount; i++)
        {
            _infoAlist[i] = new PedalInfo();
        }
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Piano_pedal_performer";

    /// <summary>Starts listening for the three pedals.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(SostenutoEventSymbol, ev => ListenPedal(PedalType.Sostenuto, ev));
        ListenTo(SustainEventSymbol, ev => ListenPedal(PedalType.Sustain, ev));
        ListenTo(UnaCordaEventSymbol, ev => ListenPedal(PedalType.UnaCorda, ev));
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Clears every pedal event.</summary>
    public override void Initialize()
    {
        foreach (PedalInfo info in _infoAlist)
        {
            info.EventDrul = new DrulArray<StreamEvent>(null, null);
            info.StartEvent = null;
        }
    }

    /// <summary>Forgets last timestep's pedal events.</summary>
    public override void StartTranslationTimestep()
    {
        foreach (PedalInfo info in _infoAlist)
        {
            info.EventDrul = new DrulArray<StreamEvent>(null, null);
        }
    }

    /// <summary>Emits a pedal-up for every stop and a pedal-down for every start.</summary>
    public override void ProcessMusic()
    {
        for (int i = 0; i < PedalTypeCount; i++)
        {
            PedalInfo info = _infoAlist[i];

            StreamEvent stop = info.EventDrul[Direction.Positive];
            if (stop != null)
            {
                if (info.StartEvent == null)
                {
                    TranslatorSchemeHelpers.EventWarning(stop, "cannot find start of piano pedal");
                }
                else
                {
                    AudioPianoPedal pedal = new AudioPianoPedal
                    {
                        PedalType = (PedalType)i,
                        Dir = Direction.Positive,
                    };

                    _audios.Add(pedal);
                    AnnounceElement(new AudioElementInfo(pedal, stop));
                }

                info.StartEvent = null;
            }

            StreamEvent start = info.EventDrul[Direction.Negative];
            if (start != null)
            {
                info.StartEvent = start;

                AudioPianoPedal pedal = new AudioPianoPedal
                {
                    PedalType = (PedalType)i,
                    Dir = Direction.Negative,
                };

                _audios.Add(pedal);
                AnnounceElement(new AudioElementInfo(pedal, start));
            }

            info.EventDrul = new DrulArray<StreamEvent>(null, null);
        }
    }

    /// <summary>Forgets this timestep's pedals.</summary>
    public override void StopTranslationTimestep() => _audios.Clear();

    private void ListenPedal(PedalType type, StreamEvent ev)
    {
        Direction d = DirectionalElementInterface.FromScheme(
            ev.GetProperty(SpanDirectionSymbol), Direction.Center);

        if (d != Direction.Center)
        {
            _infoAlist[(int)type].EventDrul[d] = ev;
        }
    }

    /// <summary>One pedal's start event and this timestep's start/stop events.</summary>
    private sealed class PedalInfo
    {
        internal StreamEvent StartEvent;

        // A FIELD, not a property: DrulArray is a struct, and `info.EventDrul[d] = ev'
        // through a property setter would mutate a temporary copy and silently lose the
        // event. The compiler catches it (CS1612), which is the only reason this is not
        // a bug instead of a note.
        internal DrulArray<StreamEvent> EventDrul = new DrulArray<StreamEvent>(null, null);
    }
}

/// <summary>
/// Listens to <c>SetProperty</c> events on the MIDI context properties and turns them
/// into control changes.
/// </summary>
public sealed class MidiControlChangePerformer : Performer
{
    private static readonly Symbol SetPropertySymbol = Symbol.Intern("SetProperty");
    private static readonly Symbol SymbolSymbol = Symbol.Intern("symbol");
    private static readonly Symbol ValueSymbol = Symbol.Intern("value");

    private Listener _listener;

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public MidiControlChangePerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Midi_control_change_performer";

    /// <summary>Starts listening for property changes.</summary>
    /// <remarks>
    /// Registered directly on <c>events_below</c> rather than through
    /// <see cref="Translator.ListenTo(Symbol, Action{StreamEvent})"/>, because <c>SetProperty</c> is not a music event
    /// class and upstream registers it by hand for the same reason.
    /// </remarks>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        _listener = Context?.EventsBelow.AddListener(
            this, AnnounceControlChange, SetPropertySymbol);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        if (_listener != null)
        {
            Context?.EventsBelow.RemoveListener(_listener, SetPropertySymbol);
            _listener = null;
        }

        base.DisconnectFromContext();
    }

    /// <summary>Announces control changes for a property that has just been set.</summary>
    /// <param name="streamEvent">The <c>SetProperty</c> event.</param>
    public void AnnounceControlChange(StreamEvent streamEvent)
    {
        if (!(streamEvent.GetProperty(SymbolSymbol) is Symbol symbol))
        {
            return;
        }

        ControlChangeAnnouncer announcer
            = new ControlChangeAnnouncer(this, streamEvent, symbol.Name);
        announcer.AnnounceControlChanges();
    }

    /// <summary>
    /// Reads the new value out of the <c>SetProperty</c> event, but only for the one
    /// property the event is about.
    /// </summary>
    private sealed class ControlChangeAnnouncer : MidiControlChangeAnnouncer
    {
        private readonly MidiControlChangePerformer _performer;
        private readonly StreamEvent _event;
        private readonly string _symbol;

        internal ControlChangeAnnouncer(
            MidiControlChangePerformer performer, StreamEvent ev, string symbol)
            : base(ev.Origin as Input)
        {
            _performer = performer;
            _event = ev;
            _symbol = symbol;
        }

        protected override object GetPropertyValue(string propertyName)
            => _symbol == propertyName ? _event.GetProperty(ValueSymbol) : Nil.Instance;

        protected override void DoAnnounce(AudioControlChange item)
            => _performer.AnnounceElement(new AudioElementInfo(item, null));
    }
}

/// <summary>
/// Builds the MIDI sequence's control track: its name, the creator string, and every
/// tempo, marker and time signature announced anywhere in the score.
/// </summary>
public sealed class ControlTrackPerformer : Performer
{
    private static readonly Symbol MidiSkipOffsetSymbol = Symbol.Intern("midiSkipOffset");

    private AudioControlTrackStaff _controlTrack;

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public ControlTrackPerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Control_track_performer";

    /// <summary>Creates the control track, once, and fills in its opening text.</summary>
    public override void ProcessMusic()
    {
        if (_controlTrack != null)
        {
            return;
        }

        _controlTrack = new AudioControlTrackStaff();
        AnnounceElement(new AudioElementInfo(_controlTrack, null));

        string idString = StringConvert.PadTo(
            "LilyPond " + LilyVersion.VersionString(), 30);

        _controlTrack.TrackName = "control track";

        // The first audio element in the control track is a placeholder for the name of
        // the MIDI sequence. The actual name is stored in the element later before
        // outputting the track (in Performance::output, see performance.cc).
        AddText(AudioTextType.TrackName, _controlTrack.TrackName);
        AddText(AudioTextType.Text, "creator: ");
        AddText(AudioTextType.Text, idString);
    }

    /// <summary>Collects tempos, markers and time signatures onto the control track.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeAudioElement(AudioElementInfo info)
    {
        if (_controlTrack == null)
        {
            return;
        }

        switch (info.Element)
        {
            case AudioTempo tempo:
                _controlTrack.AddAudioItem(tempo);
                break;
            case AudioText text when text.TextType == AudioTextType.Marker:
                _controlTrack.AddAudioItem(text);
                break;
            case AudioTimeSignature signature:
                _controlTrack.AddAudioItem(signature);
                break;
            default:
                break;
        }
    }

    /// <summary>Closes the control track.</summary>
    public override void FinalizeTranslation()
    {
        // There should almost always be a control track here. There won't be a control
        // track if skipTypesetting has skipped the entire score, preventing
        // process_music() from ever being called.
        if (_controlTrack != null)
        {
            _controlTrack.EndMoment = NowMoment
                + (GetProperty(MidiSkipOffsetSymbol) is Moment offset
                    ? offset
                    : Moment.Zero);
        }
    }

    private void AddText(AudioTextType textType, string str)
    {
        AudioText text = new AudioText(textType, str);
        _controlTrack.AddAudioItem(text);

        AnnounceElement(new AudioElementInfo(text, null));
    }
}
