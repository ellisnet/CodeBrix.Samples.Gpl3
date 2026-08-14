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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Audio; //was previously: lily/audio-item.cc, lily/include/audio-item.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - NAMING: upstream nests `enum Type' inside Audio_text. `Type' is on the banned list
//     (standing rule 6 — a root .NET name), so it is the top-level AudioTextType here.
//     Its VALUES are upstream's and must stay so: they are written into the MIDI file as
//     the meta-event type byte, so renumbering them would silently change the bytes.
//   - Upstream declares Audio_item's copy constructor and assignment private to forbid
//     copying. A C# reference type is not copied by assignment, so there is nothing to
//     forbid and the declarations have no analogue.

/// <summary>
/// An <see cref="AudioElement"/> that happens AT a moment, and therefore belongs to an
/// <see cref="AudioColumn"/> and to a MIDI channel.
/// </summary>
public class AudioItem : AudioElement
{
    /// <summary>Gets or sets the column this item was placed in.</summary>
    public AudioColumn AudioColumn { get; set; }

    /// <summary>Gets or sets the MIDI channel this item plays on.</summary>
    public int Channel { get; set; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Audio_item";

    /// <summary>Returns the column this item was placed in.</summary>
    /// <returns>The column, or <see langword="null"/> when it has not been placed.</returns>
    public AudioColumn GetColumn() => AudioColumn;

    /// <summary>
    /// Renders the item. The base does nothing, exactly as upstream's does.
    /// </summary>
    public virtual void Render()
    {
    }
}

/// <summary>The kind of MIDI text meta-event an <see cref="AudioText"/> becomes.</summary>
/// <remarks>
/// The numbers are the MIDI meta-event type bytes and are upstream's; they are written
/// into the file verbatim by <c>Midi_text::to_string</c>.
/// </remarks>
public enum AudioTextType
{
    /// <summary>Generic text (meta event 0x01).</summary>
    Text = 1,

    /// <summary>A copyright notice (meta event 0x02).</summary>
    Copyright = 2,

    /// <summary>The track name (meta event 0x03).</summary>
    TrackName = 3,

    /// <summary>The instrument name (meta event 0x04).</summary>
    InstrumentName = 4,

    /// <summary>A lyric syllable (meta event 0x05).</summary>
    Lyric = 5,

    /// <summary>A marker (meta event 0x06).</summary>
    Marker = 6,

    /// <summary>A cue point (meta event 0x07).</summary>
    CuePoint = 7,
}

/// <summary>
/// One interval of a piecewise-linear loudness function.
/// <para>
/// The interval is OPEN AT THE END: the volume grows or diminishes toward a target, but
/// whether it gets there depends on the next span in the performance. Upstream's example
/// is worth keeping — a crescendo notated <c>mf &lt; p</c> is represented as
/// <c>[mf &lt; x)</c> followed by <c>[p …)</c>, growth to something louder than
/// <c>mf</c> and then an abrupt change to <c>p</c>.
/// </para>
/// </summary>
public sealed class AudioSpanDynamic : AudioElement
{
    /// <summary>The quietest volume any dynamic may reach.</summary>
    public const double MinimumVolume = 0.0;

    /// <summary>The loudest volume any dynamic may reach.</summary>
    public const double MaximumVolume = 1.0;

    /// <summary>The volume used when nothing says otherwise.</summary>
    public const double DefaultVolume = 90.0 / 127.0;

    private double _duration;
    private double _gain;

    /// <summary>Initializes a span starting at a moment and a volume.</summary>
    /// <param name="moment">Where the span starts.</param>
    /// <param name="volume">The volume it starts at.</param>
    public AudioSpanDynamic(Moment moment, double volume)
    {
        StartMoment = moment;
        _duration = 0;
        SetVolume(volume, volume);
    }

    /// <summary>Gets where the span starts.</summary>
    public Moment StartMoment { get; }

    /// <summary>Gets the volume at the start of the span.</summary>
    public double StartVolume { get; private set; }

    /// <summary>Gets how long the span lasts, in real time.</summary>
    public double Duration => _duration;

    /// <summary>Gets the C++ class name this element corresponds to.</summary>
    public override string ClassName => "Audio_span_dynamic";

    /// <summary>Closes the span at a moment.</summary>
    /// <param name="moment">Where the span ends.</param>
    public void SetEndMoment(Moment moment)
    {
        if (moment < StartMoment)
        {
            Warn.ProgrammingError(
                "end moment (" + moment + ") < start moment (" + StartMoment + ")");
            moment = StartMoment;
        }

        _duration = AudioMoment.ToReal(moment - StartMoment);
    }

    /// <summary>Sets where the span starts and where it is heading.</summary>
    /// <param name="start">The starting volume.</param>
    /// <param name="target">The volume it grows or diminishes toward.</param>
    public void SetVolume(double start, double target)
    {
        if (!(start >= 0))
        {
            Warn.ProgrammingError("invalid start volume: " + start.ToString("F6"));
            start = DefaultVolume;
        }

        if (!(target >= 0))
        {
            Warn.ProgrammingError("invalid target volume: " + target.ToString("F6"));
            target = start;
        }

        StartVolume = start;
        _gain = target - start;
    }

    /// <summary>Returns the volume at a moment inside the span.</summary>
    /// <param name="moment">The moment to evaluate at.</param>
    /// <returns>The volume.</returns>
    public double GetVolume(Moment moment)
    {
        double when = AudioMoment.ToReal(moment - StartMoment);

        if (when <= 0)
        {
            if (when < 0)
            {
                Warn.ProgrammingError(
                    "asked to compute volume at " + when.ToString("F6")
                    + " for dynamic span of duration " + _duration.ToString("F6")
                    + " starting at " + StartMoment);
            }

            return StartVolume;
        }

        if (when >= _duration)
        {
            Warn.ProgrammingError(
                "asked to compute volume at +" + when.ToString("F6")
                + " for dynamic span of duration " + _duration.ToString("F6")
                + " starting at " + StartMoment);
            return StartVolume + _gain;
        }

        return StartVolume + (_gain * (when / _duration));
    }
}

/// <summary>A key signature, as MIDI understands one: a count of accidentals and a mode.</summary>
public sealed class AudioKey : AudioItem
{
    /// <summary>Initializes a key.</summary>
    /// <param name="accidentals">The signed number of accidentals; negative is flats.</param>
    /// <param name="major">Whether the key is major.</param>
    public AudioKey(int accidentals, bool major)
    {
        Accidentals = accidentals;
        Major = major;
    }

    /// <summary>Gets the signed number of accidentals.</summary>
    public int Accidentals { get; }

    /// <summary>Gets a value indicating whether the key is major.</summary>
    public bool Major { get; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Audio_key";
}

/// <summary>A MIDI instrument change, named by the string a user wrote.</summary>
public sealed class AudioInstrument : AudioItem
{
    /// <summary>Initializes an instrument change.</summary>
    /// <param name="instrumentString">The instrument name.</param>
    public AudioInstrument(string instrumentString) => Str = instrumentString;

    /// <summary>Gets or sets the instrument name.</summary>
    /// <remarks>
    /// Settable because <c>Midi_instrument</c>'s constructor lowercases it in place
    /// before looking it up, which is upstream's behaviour and observable: the name is
    /// what <c>midi-program</c> is keyed on.
    /// </remarks>
    public string Str { get; set; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Audio_instrument";
}

/// <summary>One sounding note.</summary>
public sealed class AudioNote : AudioItem
{
    /// <summary>Initializes a note.</summary>
    /// <param name="pitch">The written pitch.</param>
    /// <param name="length">How long it sounds.</param>
    /// <param name="tieEvent">Whether the note carries a tie event.</param>
    /// <param name="transposing">The instrument's transposition.</param>
    /// <param name="velocity">Extra velocity from articulations.</param>
    public AudioNote(Pitch pitch, Moment length, bool tieEvent, Pitch transposing, int velocity)
    {
        Pitch = pitch;
        LengthMoment = length;
        Transposing = transposing;
        Dynamic = null;
        ExtraVelocity = velocity;
        Tied = null;
        TieEvent = tieEvent;
    }

    /// <summary>Gets the written pitch.</summary>
    public Pitch Pitch { get; }

    /// <summary>Gets or sets how long the note sounds.</summary>
    public Moment LengthMoment { get; set; }

    /// <summary>Gets the instrument's transposition.</summary>
    public Pitch Transposing { get; }

    /// <summary>Gets or sets the dynamic span this note takes its volume from.</summary>
    public AudioSpanDynamic Dynamic { get; set; }

    /// <summary>Gets the extra velocity contributed by articulations.</summary>
    public int ExtraVelocity { get; }

    /// <summary>Gets or sets the note this one is tied back to.</summary>
    public AudioNote Tied { get; set; }

    /// <summary>Gets a value indicating whether this note carries a tie event.</summary>
    public bool TieEvent { get; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Audio_note";

    /// <summary>
    /// Ties this note back to an earlier one, moving all the length onto the head of the
    /// tie.
    /// <para>
    /// With <c>tieWaitForNote</c> there may be a gap between the tied notes, which is
    /// what the skip is for.
    /// </para>
    /// </summary>
    /// <param name="target">The note being tied to.</param>
    /// <param name="skip">Any gap between the two.</param>
    public void TieTo(AudioNote target, Moment skip)
    {
        Tied = target;
        AudioNote first = TieHead();

        // Add the skip to the tied note and the length of the appended note to the full
        // duration of the tie.
        first.LengthMoment += skip + LengthMoment;
        LengthMoment = Moment.Zero;
    }

    /// <summary>Ties this note back to an earlier one with no gap.</summary>
    /// <param name="target">The note being tied to.</param>
    public void TieTo(AudioNote target) => TieTo(target, Moment.Zero);

    /// <summary>Returns the first note of the tie chain this note belongs to.</summary>
    /// <returns>The head of the tie.</returns>
    public AudioNote TieHead()
    {
        AudioNote first = this;
        while (first.Tied != null)
        {
            first = first.Tied;
        }

        return first;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>Upstream's <c>Audio_note::to_string</c> text.</returns>
    public override string ToString()
    {
        string s = "#<Audio_note pitch ";
        s += Pitch.ToString();
        s += " len ";
        s += LengthMoment.ToString();
        if (Tied != null)
        {
            s += " tied to " + Tied;
        }

        if (TieEvent)
        {
            s += " tie_event";
        }

        s += ">";
        return s;
    }
}

/// <summary>A piano pedal going down or coming up.</summary>
public sealed class AudioPianoPedal : AudioItem
{
    /// <summary>Gets or sets which pedal this is.</summary>
    public PedalType PedalType { get; set; }

    /// <summary>Gets or sets whether the pedal is starting or stopping.</summary>
    public Direction Dir { get; set; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Audio_piano_pedal";
}

/// <summary>A MIDI text meta-event: a lyric, a marker, a track name, a copyright.</summary>
public sealed class AudioText : AudioItem
{
    /// <summary>Initializes a text item from a plain string.</summary>
    /// <param name="type">Which kind of text event this is.</param>
    /// <param name="textString">The text.</param>
    public AudioText(AudioTextType type, string textString)
    {
        TextType = type;
        TextString = textString;
    }

    /// <summary>Initializes a text item from markup, flattening it to a string.</summary>
    /// <param name="type">Which kind of text event this is.</param>
    /// <param name="markup">The markup to flatten.</param>
    public AudioText(AudioTextType type, object markup)
        : this(type, MarkupToString(markup))
    {
    }

    /// <summary>Gets which kind of text event this is.</summary>
    public AudioTextType TextType { get; }

    /// <summary>Gets or sets the text.</summary>
    /// <remarks>
    /// Settable because <c>Performance::output</c> fills in the real sequence name over
    /// the control track's placeholder just before the file is written.
    /// </remarks>
    public string TextString { get; set; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Audio_text";

    /// <summary>
    /// Flattens markup to the string MIDI can carry, leaving a plain string alone.
    /// </summary>
    /// <param name="markup">The markup or string.</param>
    /// <returns>The text.</returns>
    public static string MarkupToString(object markup)
    {
        if (TextInterface.IsMarkup(markup))
        {
            object procedure = LilyPondScheme.LookupProcedure(Symbol.Intern("markup->string"));
            if (procedure != null)
            {
                markup = SchemeUtilities.CallCallback(procedure, markup);
            }
        }

        return markup is MutableString mutable ? mutable.ToString()
            : markup as string ?? string.Empty;
    }
}

/// <summary>
/// One interval of a piecewise-linear tempo function, open at the end exactly as
/// <see cref="AudioSpanDynamic"/> is.
/// <para>
/// The life span is measured in the FULL timeline of the score, which is what makes
/// <c>skipTypesetting</c> behave: a subinterval that is omitted from the output must not
/// let another one take up its slack.
/// </para>
/// </summary>
public sealed class AudioSpanTempo : AudioElement
{
    /// <summary>Sixty quarter notes a minute, expressed in wholes.</summary>
    public static Rational DefaultWholesPerMinute => new Rational(15);

    private DrulArray<Moment> _lifeSpan;
    private Rational _startWpm;
    private double _duration;
    private double _gain;

    /// <summary>Initializes a tempo span.</summary>
    /// <param name="start">Where the span starts.</param>
    /// <param name="initialWpm">The tempo it starts at, in wholes per minute.</param>
    public AudioSpanTempo(Moment start, Rational initialWpm)
    {
        _lifeSpan = new DrulArray<Moment>(start, start);
        _startWpm = initialWpm;
        _duration = 0;
        _gain = 0;

        if (!_startWpm.IsFinite || _startWpm < Rational.Zero)
        {
            Warn.ProgrammingError("invalid start tempo: " + _startWpm);
            _startWpm = DefaultWholesPerMinute;
        }
    }

    /// <summary>Gets where the span starts.</summary>
    public Moment StartMoment => _lifeSpan.Negative;

    /// <summary>Gets the tempo at the start of the span, in wholes per minute.</summary>
    public Rational StartWholesPerMinute => _startWpm;

    /// <summary>Gets the C++ class name this element corresponds to.</summary>
    public override string ClassName => "Audio_span_tempo";

    /// <summary>Closes the span at a moment.</summary>
    /// <param name="endMoment">Where the span ends.</param>
    public void SetEndMoment(Moment endMoment)
    {
        Moment startMoment = _lifeSpan.Negative;
        if (endMoment < startMoment)
        {
            Warn.ProgrammingError(
                "end moment (" + endMoment + ") < start moment (" + startMoment + ")");
            endMoment = startMoment;
        }

        _lifeSpan.Positive = endMoment;
        _duration = AudioMoment.ToReal(endMoment - startMoment);
    }

    /// <summary>Sets the tempo the span is heading toward.</summary>
    /// <param name="target">The target tempo, in wholes per minute.</param>
    public void SetEndWholesPerMinute(Rational target)
        => _gain = target.ToDouble() - _startWpm.ToDouble();

    /// <summary>
    /// Returns the average tempo over a right-open interval.
    /// <para>
    /// In a MIDI file tempo is a piecewise CONSTANT function, so the caller chooses
    /// intervals and asks this for each one's average — and it is highly desirable that
    /// the resulting playback time not depend on that choice. Given upstream's linear
    /// model of a gradual tempo change, the LOGARITHMIC MEAN is the value that yields
    /// equal playback time, which is why that is what this computes.
    /// </para>
    /// </summary>
    /// <param name="interval">The right-open interval to average over.</param>
    /// <returns>The average tempo, in wholes per minute.</returns>
    public Rational CalcAverageWholesPerMinute(DrulArray<Moment> interval)
    {
        if (!(_lifeSpan.Negative <= interval.Negative) || !(interval.Positive <= _lifeSpan.Positive))
        {
            Warn.ProgrammingError(
                "asked to compute tempo over [" + interval.Negative + ", " + interval.Positive
                + "), which is outside tempo span [" + _lifeSpan.Negative + ", "
                + _lifeSpan.Positive + ")");
            return _startWpm;
        }

        // Convert the Moment endpoints to scalars relative to the life span.
        double start = AudioMoment.ToReal(interval.Negative - _lifeSpan.Negative);
        double end = AudioMoment.ToReal(interval.Positive - _lifeSpan.Negative);
        if (!(0 <= start) || !(end <= _duration) || !(start < end))
        {
            Warn.ProgrammingError(
                "asked to compute tempo over [" + start.ToString("F6") + ", "
                + end.ToString("F6") + ") relative to tempo span [" + _lifeSpan.Negative
                + ", " + _lifeSpan.Positive + "), which has duration "
                + _duration.ToString("F6"));
            return _startWpm;
        }

        double InstantWpm(double when) => _startWpm.ToDouble() + (_gain * (when / _duration));

        double tl = InstantWpm(start);
        double tr = InstantWpm(end);

        double num = tr - tl;
        double den = Math.Log(tr / tl); // == log (tr) - log (tl)
        if (num == 0 || den == 0)
        {
            return _startWpm;
        }

        return Rational.FromDouble(num / den);
    }
}

/// <summary>A MIDI time signature.</summary>
public sealed class AudioTimeSignature : AudioItem
{
    /// <summary>Initializes a time signature.</summary>
    /// <param name="numerator">The numerator, which need not be an integer.</param>
    /// <param name="denominator">The denominator, which need not be an integer.</param>
    /// <param name="beatBaseClocks">The metronome period, in MIDI clocks.</param>
    public AudioTimeSignature(Rational numerator, Rational denominator, int beatBaseClocks)
    {
        Num = numerator;
        Den = denominator;
        BeatBaseClocks = beatBaseClocks;
    }

    /// <summary>Gets the numerator.</summary>
    public Rational Num { get; }

    /// <summary>Gets the denominator.</summary>
    public Rational Den { get; }

    /// <summary>Gets the metronome period, in MIDI clocks.</summary>
    public int BeatBaseClocks { get; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Audio_time_signature";
}

/// <summary>
/// An instantaneous tempo change, which is what MIDI can express — but carrying the time
/// of the NEXT change too, so a playback-time-preserving average can be computed from the
/// more descriptive <see cref="AudioSpanTempo"/> model.
/// </summary>
public sealed class AudioTempo : AudioItem
{
    private DrulArray<Moment> _lifeSpan;
    private readonly AudioSpanTempo _spanTempo;

    /// <summary>Initializes a tempo change.</summary>
    /// <param name="spanTempo">The span this change samples.</param>
    /// <param name="start">Where the change takes effect.</param>
    public AudioTempo(AudioSpanTempo spanTempo, Moment start)
    {
        _lifeSpan = new DrulArray<Moment>(start, Moment.Infinity);
        _spanTempo = spanTempo;
    }

    /// <summary>Gets where this change takes effect.</summary>
    public Moment StartMoment => _lifeSpan.Negative;

    /// <summary>Gets where the next change takes effect.</summary>
    public Moment EndMoment => _lifeSpan.Positive;

    /// <summary>Gets a value indicating whether the next change is known yet.</summary>
    public bool HasEndMoment => _lifeSpan.Positive < Moment.Infinity;

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Audio_tempo";

    /// <summary>Records where the next change takes effect.</summary>
    /// <param name="end">The next change's moment.</param>
    public void SetEndMoment(Moment end) => _lifeSpan.Positive = end;

    /// <summary>Returns the tempo to write, averaged over this change's life span.</summary>
    /// <returns>The tempo, in wholes per minute.</returns>
    public Rational CalcWholesPerMinute() => _spanTempo.CalcAverageWholesPerMinute(_lifeSpan);
}

/// <summary>A MIDI control change.</summary>
public sealed class AudioControlChange : AudioItem
{
    /// <summary>Initializes a control change.</summary>
    /// <param name="control">The MIDI control number.</param>
    /// <param name="value">The value to set it to.</param>
    public AudioControlChange(int control, int value)
    {
        Control = control;
        Value = value;
    }

    /// <summary>Gets the MIDI control number.</summary>
    public int Control { get; }

    /// <summary>Gets the value being set.</summary>
    public int Value { get; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Audio_control_change";
}

/// <summary>
/// The two free functions <c>audio-item.cc</c> declares at file scope: how a
/// <see cref="Moment"/> becomes real time, and how it becomes MIDI ticks.
/// </summary>
/// <remarks>
/// These are file-scope functions upstream, which C# has no place for; a static class is
/// the port's usual home for them. The grace-part weighting of 9/40 is upstream's own
/// constant and is what gives grace notes a small but non-zero playing time.
/// </remarks>
public static class AudioMoment
{
    /// <summary>Converts a moment to real time.</summary>
    /// <param name="moment">The moment.</param>
    /// <returns>The real-valued time.</returns>
    public static double ToReal(Moment moment)
        => (moment.MainPart + (new Rational(9, 40) * moment.GracePart)).ToDouble();

    /// <summary>Converts a moment to MIDI ticks, at 384 ticks per quarter note.</summary>
    /// <param name="moment">The moment.</param>
    /// <returns>The tick count.</returns>
    public static int ToTicks(Moment moment) => (int)(ToReal(moment) * 384 * 4);
}
