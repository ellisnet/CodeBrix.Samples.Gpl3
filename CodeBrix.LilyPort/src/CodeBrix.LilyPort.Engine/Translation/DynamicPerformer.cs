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
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/dynamic-performer.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - THE FAITHFULNESS RULE APPLIES HERE. This is not a scorer, but calc_departure_volume
//     is the same kind of thing: a decades-tuned heuristic with two magic paddings whose
//     values upstream justifies at length in a comment. Both the comment and the numbers
//     are carried verbatim, and the state machine's three states are reproduced as
//     written rather than restructured.

/// <summary>
/// Works out how loud every note is: it turns dynamic marks and hairpins into
/// <see cref="AudioSpanDynamic"/> spans and points each note at the one in force.
/// </summary>
/// <remarks>
/// <para>
/// The performer QUEUES spans rather than resolving them as they arrive, because a
/// hairpin's target volume is not known until the music says where it is going. It waits
/// for this pattern:
/// </para>
/// <list type="number">
/// <item><description>the first (de)crescendo, followed by …</description></item>
/// <item><description>zero or more spans that either change in the same direction as the
/// first or do not change, followed by …</description></item>
/// <item><description>zero or more spans that either change in the opposite direction as
/// the first or do not change.</description></item>
/// </list>
/// <para>
/// The search may be cut short by an absolute dynamic or the end of the context.
/// </para>
/// </remarks>
public sealed class DynamicPerformer : Performer
{
    private static readonly Symbol MidiMinimumVolumeSymbol = Symbol.Intern("midiMinimumVolume");
    private static readonly Symbol MidiMaximumVolumeSymbol = Symbol.Intern("midiMaximumVolume");
    private static readonly Symbol MidiInstrumentSymbol = Symbol.Intern("midiInstrument");
    private static readonly Symbol InstrumentNameSymbol = Symbol.Intern("instrumentName");
    private static readonly Symbol InstrumentEqualizerSymbol
        = Symbol.Intern("instrumentEqualizer");
    private static readonly Symbol DynamicAbsoluteVolumeFunctionSymbol
        = Symbol.Intern("dynamicAbsoluteVolumeFunction");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol CrescendoEventSymbol = Symbol.Intern("crescendo-event");
    private static readonly Symbol DecrescendoEventSymbol = Symbol.Intern("decrescendo-event");
    private static readonly Symbol AbsoluteDynamicEventSymbol
        = Symbol.Intern("absolute-dynamic-event");

    private readonly List<AudioNote> _notes = new List<AudioNote>();
    private readonly DynamicQueue _departQueue = new DynamicQueue();
    private readonly DynamicQueue _returnQueue = new DynamicQueue();

    private StreamEvent _scriptEvent;
    private DrulArray<StreamEvent> _spanEvents = new DrulArray<StreamEvent>(null, null);
    private Direction _nextGrowDir = Direction.Center;
    private Direction _departDir = Direction.Center;
    private UnfinishedSpan _openSpan = new UnfinishedSpan();
    private State _state = State.Initial;

    /// <summary>Where the performer is in the depart/return pattern.</summary>
    private enum State
    {
        /// <summary>Waiting for a (de)crescendo.</summary>
        Initial = 0,

        /// <summary>Enqueued the first span, gathering same-direction spans.</summary>
        Depart,

        /// <summary>Gathering opposite-direction spans.</summary>
        Return,
    }

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public DynamicPerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Dynamic_performer";

    /// <summary>Starts listening for dynamics and hairpins.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(DecrescendoEventSymbol, ev => ListenHairpin(ev, Direction.Negative));
        ListenTo(CrescendoEventSymbol, ev => ListenHairpin(ev, Direction.Positive));
        ListenTo(AbsoluteDynamicEventSymbol, ListenAbsoluteDynamic);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Notes every audio note announced this timestep, to point at the dynamic.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeAudioElement(AudioElementInfo info)
    {
        // Keep track of the notes played in this translation time step so that they can
        // be pointed to the current dynamic in stop_translation_timestep.
        if (info.Element is AudioNote note)
        {
            _notes.Add(note);
        }
    }

    /// <summary>Closes and opens dynamic spans as the music asks for them.</summary>
    public override void ProcessMusic()
    {
        double volume = -1;

        if (_scriptEvent != null) // explicit dynamic
        {
            volume = LookUpAbsoluteVolume(
                _scriptEvent.GetProperty(TextSymbol), AudioSpanDynamic.DefaultVolume);
            volume = EqualizeVolume(volume);
        }
        else if (_openSpan.Dynamic == null) // first time only
        {
            // Idea: look_up_absolute_volume (ly_symbol2scm ("mf")).
            // It is likely to change regtests.   (upstream's note, kept)
            volume = EqualizeVolume(AudioSpanDynamic.DefaultVolume);
        }

        // end the current span at relevant points
        if (_openSpan.Dynamic != null
            && (_spanEvents[Direction.Negative] != null
                || _spanEvents[Direction.Positive] != null
                || _scriptEvent != null))
        {
            CloseAndEnqueueSpan();
            if (_scriptEvent != null)
            {
                _state = State.Initial;
                volume = FinishQueuedSpans(volume);
            }
        }

        // start a new span so that some dynamic is always in effect
        if (_openSpan.Dynamic == null)
        {
            if (DriveStateMachine(_nextGrowDir))
            {
                volume = FinishQueuedSpans(volume);
            }

            // if not known by now, use a default volume for robustness
            if (volume < 0)
            {
                volume = EqualizeVolume(AudioSpanDynamic.DefaultVolume);
            }

            StreamEvent cause = _spanEvents[Direction.Negative]
                ?? _scriptEvent
                ?? _spanEvents[Direction.Positive];

            _openSpan.Dynamic = new AudioSpanDynamic(NowMoment, volume);
            _openSpan.GrowDir = _nextGrowDir;
            AnnounceElement(new AudioElementInfo(_openSpan.Dynamic, cause));
        }
    }

    /// <summary>Points this timestep's notes at the dynamic in force.</summary>
    public override void StopTranslationTimestep()
    {
        // link notes to the current dynamic
        if (_openSpan.Dynamic == null)
        {
            Warn.ProgrammingError("no current dynamic");
        }
        else
        {
            foreach (AudioNote note in _notes)
            {
                note.Dynamic = _openSpan.Dynamic;
            }
        }

        _notes.Clear();

        _scriptEvent = null;
        _spanEvents = new DrulArray<StreamEvent>(null, null);
        _nextGrowDir = Direction.Center;
    }

    /// <summary>Closes the last span and resolves everything still queued.</summary>
    public override void FinalizeTranslation()
    {
        if (_openSpan.Dynamic != null)
        {
            CloseAndEnqueueSpan();
        }

        FinishQueuedSpans();
    }

    /// <summary>
    /// Advances the depart/return state machine.
    /// </summary>
    /// <param name="nextGrowDir">Which way the next span grows.</param>
    /// <returns>
    /// <see langword="true"/> when the pattern has completed and the queues should be
    /// resolved.
    /// </returns>
    private bool DriveStateMachine(Direction nextGrowDir)
    {
        switch (_state)
        {
            case State.Initial:
                if (nextGrowDir != Direction.Center)
                {
                    _state = State.Depart;
                    _departDir = nextGrowDir;
                }

                break;

            case State.Depart:
                if (nextGrowDir == -_departDir)
                {
                    _state = State.Return;
                }

                break;

            case State.Return:
                if (nextGrowDir == _departDir)
                {
                    _state = State.Depart;
                    return true;
                }

                break;

            default:
                break;
        }

        return false;
    }

    private void CloseAndEnqueueSpan()
    {
        if (_openSpan.Dynamic == null)
        {
            Warn.ProgrammingError("no open dynamic span");
        }
        else
        {
            DynamicQueue queue = _state == State.Return ? _returnQueue : _departQueue;

            // Changing equalizer settings in the course of the performance does not seem
            // very likely. This is a fig leaf: Equalize these limit volumes now as the
            // required context properties are current. Note that only the limits at the
            // end of the last span in the queue are kept.

            // Resist diminishing to silence. (Idea: Look up "ppppp" with
            // dynamicAbsoluteVolumeFunction, however that would yield 0.25.)
            double minTarget = EqualizeVolume(0.1);
            double maxTarget = EqualizeVolume(AudioSpanDynamic.MaximumVolume);

            _openSpan.Dynamic.SetEndMoment(NowMoment);
            queue.PushBack(_openSpan, minTarget, maxTarget);
        }

        _openSpan = new UnfinishedSpan();
    }

    /// <summary>
    /// Returns a volume reasonably distant from the given start and end volumes in the
    /// given direction, for use as a peak in a crescendo-then-decrescendo passage.
    /// </summary>
    /// <remarks>
    /// The two paddings are upstream's and so is the reasoning: with <c>mf &lt; … &gt;
    /// p</c>, a 25% change cannot be used for BOTH the crescendo and the decrescendo
    /// while meeting the constraints, so 25% is used for the greater change and 7% for
    /// the lesser. Upstream's own idea for improving it — reading the difference between
    /// two dynamics out of <c>dynamicAbsoluteVolumeFunction</c> — is recorded there and
    /// is not done here either.
    /// </remarks>
    private static double CalcDepartureVolume(
        Direction departDir, double startVol, double endVol, double minVol, double maxVol)
    {
        if (departDir == Direction.Center)
        {
            return startVol;
        }

        const double farPadding = 0.25;
        const double nearPadding = 0.07;

        // If for some reason one of the endpoints is already below the supposed minimum
        // or maximum, just accept it.
        minVol = Math.Min(Math.Min(minVol, startVol), endVol);
        maxVol = Math.Max(Math.Max(maxVol, startVol), endVol);

        double volRange = maxVol - minVol;

        double nearVol = Direction.MinMax(departDir, startVol, endVol)
            + ((int)departDir * nearPadding * volRange);
        double farVol = Direction.MinMax(-departDir, startVol, endVol)
            + ((int)departDir * farPadding * volRange);
        double departVol = Direction.MinMax(departDir, nearVol, farVol);

        return Math.Max(Math.Min(departVol, maxVol), minVol);
    }

    /// <summary>Resolves the queued spans, choosing a target when the music gives none.</summary>
    /// <param name="nextVol">The next known volume, or negative when there is none.</param>
    /// <returns>The volume in force after the queues are resolved.</returns>
    private double FinishQueuedSpans(double nextVol = -1.0)
    {
        if (_departQueue.Spans.Count == 0)
        {
            Warn.ProgrammingError("no dynamic span to finish");
            return nextVol;
        }

        double startVol = _departQueue.Spans[0].Dynamic.StartVolume;

        if (_returnQueue.Spans.Count == 0)
        {
            double departVol = nextVol;

            // If the next dynamic is not specified or is inconsistent with the direction
            // of growth, choose a reasonable target.
            if (nextVol < 0 || _departDir != SignOf(nextVol - startVol))
            {
                departVol = CalcDepartureVolume(
                    _departDir, startVol, startVol,
                    _departQueue.MinTargetVol, _departQueue.MaxTargetVol);
            }

            _departQueue.SetVolume(startVol, departVol);
            _departQueue.Clear();
            return nextVol >= 0 ? nextVol : departVol;
        }

        // If the next dynamic is not specified, return to the starting volume.
        double returnVol = nextVol >= 0 ? nextVol : startVol;
        double peak = CalcDepartureVolume(
            _departDir, startVol, returnVol,
            _departQueue.MinTargetVol, _departQueue.MaxTargetVol);

        _departQueue.SetVolume(startVol, peak);
        _departQueue.Clear();
        _returnQueue.SetVolume(peak, returnVol);
        _returnQueue.Clear();
        return returnVol;
    }

    /// <summary>Upstream's <c>Direction (Real)</c> conversion: the sign of a number.</summary>
    private static Direction SignOf(double value)
        => value > 0 ? Direction.Positive
            : value < 0 ? Direction.Negative
            : Direction.Center;

    /// <summary>Maps a nominal volume onto the range this instrument actually uses.</summary>
    private double EqualizeVolume(double volume)
    {
        /*
          properties override default equaliser setting
        */
        object min = GetProperty(MidiMinimumVolumeSymbol);
        object max = GetProperty(MidiMaximumVolumeSymbol);

        if (IsNumber(min) || IsNumber(max))
        {
            Interval iv = new Interval(
                AudioSpanDynamic.MinimumVolume, AudioSpanDynamic.MaximumVolume);

            if (IsNumber(min))
            {
                iv[Direction.Negative] = Convert.ToDouble(min);
            }

            if (IsNumber(max))
            {
                iv[Direction.Positive] = Convert.ToDouble(max);
            }

            volume = iv[Direction.Negative] + (iv.Length * volume);
        }
        else
        {
            /*
              urg, code duplication:: staff_performer
            */
            object s = GetProperty(MidiInstrumentSymbol);

            if (!IsString(s))
            {
                s = GetProperty(InstrumentNameSymbol);
            }

            if (!IsString(s))
            {
                s = new MutableString("piano");
            }

            object equalizer = GetProperty(InstrumentEqualizerSymbol);
            if (SchemeUtilities.IsProcedure(equalizer))
            {
                s = SchemeUtilities.CallCallback(equalizer, s);
            }

            if (s is Pair pair && IsNumber(pair.Car) && IsNumber(pair.Cdr))
            {
                Interval iv = new Interval(
                    Convert.ToDouble(pair.Car), Convert.ToDouble(pair.Cdr));
                volume = iv[Direction.Negative] + (iv.Length * volume);
            }
        }

        return Math.Max(
            Math.Min(volume, AudioSpanDynamic.MaximumVolume),
            AudioSpanDynamic.MinimumVolume);
    }

    private double LookUpAbsoluteVolume(object dynamicString, double defaultValue)
    {
        object procedure = GetProperty(DynamicAbsoluteVolumeFunctionSymbol);

        object volume = Nil.Instance;
        if (SchemeUtilities.IsProcedure(procedure))
        {
            volume = SchemeUtilities.CallCallback(procedure, dynamicString);
        }

        return IsNumber(volume) ? Convert.ToDouble(volume) : defaultValue;
    }

    // NOT SchemeConvert.IsNumber: that one predates the flower Rational appearing in
    // properties and does not count it, and upstream's test here is scm_is_number.
    // TryToRational accepts everything scm_is_number would.
    private static bool IsNumber(object value) => SchemeConvert.TryToRational(value, out _);

    private static bool IsString(object value) => value is MutableString || value is string;

    private void ListenHairpin(StreamEvent ev, Direction growth)
    {
        Direction d = DirectionalElementInterface.FromScheme(
            ev.GetProperty(SpanDirectionSymbol), Direction.Center);

        if (d == Direction.Center)
        {
            return;
        }

        StreamEvent existing = _spanEvents[d];
        bool assigned = StreamEvent.AssignEventOnce(ref existing, ev);
        _spanEvents[d] = existing;

        if (assigned && d == Direction.Negative)
        {
            _nextGrowDir = growth;
        }
    }

    private void ListenAbsoluteDynamic(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _scriptEvent, ev);

    /// <summary>An open dynamic span and which way it grows.</summary>
    private sealed class UnfinishedSpan
    {
        internal AudioSpanDynamic Dynamic { get; set; }

        internal Direction GrowDir { get; set; } = Direction.Center;
    }

    /// <summary>
    /// A run of spans waiting for a target volume, and the total time they spend changing.
    /// </summary>
    private sealed class DynamicQueue
    {
        internal List<UnfinishedSpan> Spans { get; } = new List<UnfinishedSpan>();

        /// <summary>
        /// Gets the total duration of the (de)crescendi — that is, excluding
        /// fixed-volume spans.
        /// </summary>
        internal double ChangeDuration { get; private set; }

        internal double MinTargetVol { get; private set; }

        internal double MaxTargetVol { get; private set; }

        internal void Clear()
        {
            Spans.Clear();
            ChangeDuration = 0;
        }

        internal void PushBack(UnfinishedSpan span, double minTargetVol, double maxTargetVol)
        {
            if (span.GrowDir != Direction.Center)
            {
                ChangeDuration += span.Dynamic.Duration;
            }

            MinTargetVol = minTargetVol;
            MaxTargetVol = maxTargetVol;
            Spans.Add(span);
        }

        /// <summary>
        /// Sets the starting and target volume for each span in the queue. The gain (or
        /// loss) of any (de)crescendo is proportional to its share of the total time
        /// spent changing.
        /// </summary>
        internal void SetVolume(double startVol, double targetVol)
        {
            double gain = targetVol - startVol;
            double duration = 0; // duration of (de)crescendi processed so far
            double volume = startVol;

            foreach (UnfinishedSpan span in Spans)
            {
                double previousVolume = volume;
                if (span.GrowDir != Direction.Center)
                {
                    // grant this (de)crescendo its portion of the gain
                    duration += span.Dynamic.Duration;
                    volume = startVol + (gain * (duration / ChangeDuration));
                }

                span.Dynamic.SetVolume(previousVolume, volume);
            }
        }
    }
}
