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
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/tempo-performer.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - derived_mark() is not carried; see the note on AudioElement.

/// <summary>
/// Turns tempo marks and gradual tempo changes into the MIDI tempo track.
/// <para>
/// It keeps two things at once: an <see cref="AudioSpanTempo"/> modelling the tempo as a
/// piecewise-LINEAR function, and a run of <see cref="AudioTempo"/> samples of it, because
/// MIDI can only express a piecewise-CONSTANT one. Each sample asks the span for the
/// average tempo over its own life span, which is what keeps the total playback time right
/// however finely the samples are spaced.
/// </para>
/// </summary>
public sealed class TempoPerformer : Performer
{
    private static readonly Symbol TempoWholesPerMinuteSymbol
        = Symbol.Intern("tempoWholesPerMinute");
    private static readonly Symbol SkipTypesettingSymbol = Symbol.Intern("skipTypesetting");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol TempoUnitSymbol = Symbol.Intern("tempo-unit");
    private static readonly Symbol MetronomeCountSymbol = Symbol.Intern("metronome-count");
    private static readonly Symbol TempoChangeEventSymbol = Symbol.Intern("tempo-change-event");
    private static readonly Symbol TempoGradualChangeEventSymbol
        = Symbol.Intern("tempo-gradual-change-event");

    private readonly List<AudioTempo> _spannedChanges = new List<AudioTempo>();

    private StreamEvent _accelEvent;      // or decel.
    private StreamEvent _accelStopEvent;  // or decel.
    private StreamEvent _tempoEvent;
    private AudioSpanTempo _span;
    private bool _spanRamping;
    private Moment _lastChangeMoment = -Moment.Infinity;
    private Rational _wpm = -Rational.Infinity;
    private Rational _accelStopWpm = -Rational.Infinity;
    private bool _ramping;

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public TempoPerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Tempo_performer";

    /// <summary>Starts listening for tempo marks and gradual changes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();

        // TODO: caesura   (upstream's TODO, kept)
        ListenTo(TempoChangeEventSymbol, ListenTempoChange);
        ListenTo(TempoGradualChangeEventSymbol, ListenTempoGradualChange);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Decides whether the tempo has changed, and closes the previous span if so.</summary>
    public override void PreProcessMusic()
    {
        Rational wpm = SchemeConvert.TryToRational(
            GetProperty(TempoWholesPerMinuteSymbol), out Rational value)
            ? value
            : _wpm;

        bool changeWpm = wpm != _wpm;
        if (changeWpm || _accelStopEvent != null)
        {
            _wpm = wpm;
            _lastChangeMoment = NowMoment;
            _ramping = false;
        }

        if (_accelEvent != null)
        {
            if (!_ramping)
            {
                _lastChangeMoment = NowMoment;
                _ramping = true;
            }
            else
            {
                Epg8Support.EventWarning(
                    _accelEvent, "tempo change already in progress; ignoring");
                _accelEvent = null;
            }
        }

        // At a change, finalize the previous span.
        if (_span != null && _span.StartMoment < _lastChangeMoment)
        {
            CloseSpanTempo();
        }

        // When part of the performance is skipped, end an ongoing Audio_tempo so that it
        // doesn't receive any contribution for the skipped music.
        if (SchemeUtilities.ToBool(GetProperty(SkipTypesettingSymbol)))
        {
            CloseTempo(NowMoment);
        }
    }

    /// <summary>Opens a span when there is none, and samples it when the tempo moves.</summary>
    public override void ProcessMusic()
    {
        if (_span == null)
        {
            StreamEvent cause = _ramping ? _accelEvent : _tempoEvent;
            _span = new AudioSpanTempo(_lastChangeMoment, _wpm);
            _spanRamping = _ramping;
            AnnounceElement(new AudioElementInfo(_span, cause));
        }

        // Create Audio_tempo on discrete changes. Create Audio_tempo at every timestep
        // while ramping. We may ignore some of these after the fact if they are closer
        // together than they need to be.
        Moment now = NowMoment;
        if (_tempoEvent != null || _accelEvent != null || _ramping || _lastChangeMoment == now)
        {
            CloseTempo(now);

            StreamEvent cause = _tempoEvent ?? _span.Cause;
            AudioTempo sample = new AudioTempo(_span, now);
            _spannedChanges.Add(sample);
            AnnounceElement(new AudioElementInfo(sample, cause));
        }
    }

    /// <summary>Forgets this timestep's events.</summary>
    public override void StopTranslationTimestep()
    {
        _accelEvent = null;
        _accelStopEvent = null;
        _tempoEvent = null;

        _accelStopWpm = -Rational.Infinity;
    }

    /// <summary>Closes the open tempo span, warning about an unterminated change.</summary>
    public override void FinalizeTranslation()
    {
        if (_span == null)
        {
            return;
        }

        if (_ramping && _spannedChanges.Count != 0)
        {
            AudioTempo last = _spannedChanges[_spannedChanges.Count - 1];
            if (!last.HasEndMoment)
            {
                Warn.Warning("unterminated tempo change");
            }
        }

        CloseSpanTempo();
    }

    /// <summary>Closes the most recent tempo sample at a moment, if it is still open.</summary>
    private void CloseTempo(Moment now)
    {
        if (_spannedChanges.Count == 0)
        {
            return;
        }

        AudioTempo last = _spannedChanges[_spannedChanges.Count - 1];
        if (!last.HasEndMoment)
        {
            last.SetEndMoment(now);
        }
    }

    /// <summary>Closes the tempo span and every sample of it.</summary>
    private void CloseSpanTempo()
    {
        Moment now = NowMoment;
        CloseTempo(now);
        _span.SetEndMoment(now);

        if (_spanRamping)
        {
            // If a TempoGradualChangeEvent provided a target tempo, use it. Otherwise,
            // use the going-forward tempo.
            _span.SetEndWholesPerMinute(_accelStopWpm > Rational.Zero ? _accelStopWpm : _wpm);
        }

        // If the automatically generated changes were unnecessarily frequent, this would
        // be a good time to decimate them. A reasonable way to do it would be to coalesce
        // adjacent changes so that one's interval is expanded to cover both and the
        // other's is reduced to a point. Midi_item::get_midi already filters out the
        // latter for robustness.   (upstream's note, kept)
        _spannedChanges.Clear();
        _span = null;
        _spanRamping = false;
    }

    private void ListenTempoChange(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _tempoEvent, ev);

    private void ListenTempoGradualChange(StreamEvent ev)
    {
        Direction d = DirectionalElementInterface.FromScheme(
            ev.GetProperty(SpanDirectionSymbol), Direction.Center);

        if (d == Direction.Negative)
        {
            StreamEvent.AssignEventOnce(ref _accelEvent, ev);
            return;
        }

        if (d != Direction.Positive || !StreamEvent.AssignEventOnce(ref _accelStopEvent, ev))
        {
            return;
        }

        object unitValue = _accelStopEvent.GetProperty(TempoUnitSymbol);
        object countValue = _accelStopEvent.GetProperty(MetronomeCountSymbol);

        bool hasUnit = !(unitValue is Nil);
        bool hasCount = !(countValue is Nil);

        if (hasUnit && hasCount)
        {
            // TODO: from_scm<Duration>   (upstream's own TODO at this spot)
            Duration unit = unitValue is Duration duration ? duration : new Duration(1, 0);
            Rational count = SchemeConvert.TryToRational(countValue, out Rational rational)
                ? rational
                : Rational.Zero;

            _accelStopWpm = unit.ToWholeNotes() * count;
        }
        else
        {
            if (hasUnit)
            {
                Warn.ProgrammingError("tempo-unit without metronome-count");
            }

            if (hasCount)
            {
                Warn.ProgrammingError("metronome-count without tempo-unit");
            }
        }
    }
}
