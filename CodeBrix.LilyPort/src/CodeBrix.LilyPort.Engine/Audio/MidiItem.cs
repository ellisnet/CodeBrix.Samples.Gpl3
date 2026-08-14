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
using System.Text;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Audio; //was previously: lily/midi-item.cc, lily/include/midi-item.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - to_string() BECOMES ToBytes(), RETURNING byte[]. Upstream's std::string is a byte
//     container: a status byte of 0x90 and a UTF-8 lyric live in the same type and are
//     concatenated without conversion. System.String is UTF-16 and cannot do that without
//     picking an encoding, and no single encoding is right for both. The whole MIDI layer
//     is therefore byte[] end to end. This is the one place in the MIDI layer where following the
//     letter of upstream would have produced wrong FILES rather than merely different
//     code — see the note on Midi_text below, where the length prefix must count UTF-8
//     BYTES and would silently have counted UTF-16 chars.
//   - `Midi_item::name' / VIRTUAL_CLASS_NAME become the ClassName property the rest of
//     the engine already uses.

/// <summary>
/// One piece of MIDI information: a note on, a tempo change, a text meta-event.
/// <para>
/// This is the byte-emitting half of the MIDI subsystem. An <see cref="AudioItem"/> says
/// what happened musically; the <see cref="MidiItem"/> made from it by
/// <see cref="GetMidi"/> says what bytes that is.
/// </para>
/// </summary>
public abstract class MidiItem : IDiagnostics
{
    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public virtual string ClassName => "Midi_item";

    /// <summary>Gets the item's name, as diagnostics refer to it.</summary>
    public virtual string Name => ClassName;

    /// <summary>
    /// Returns the audio item this was made from, or <see langword="null"/> when there is
    /// none.
    /// <para>
    /// Upstream makes this pure virtual even though some subclasses answer null, so that
    /// nobody can forget to implement it: it is what supplies the input location a
    /// diagnostic is reported against.
    /// </para>
    /// </summary>
    /// <returns>The audio item, or <see langword="null"/>.</returns>
    public abstract AudioItem Audio();

    /// <summary>Returns the MIDI bytes for this item.</summary>
    /// <returns>The bytes.</returns>
    public abstract byte[] ToBytes();

    /// <summary>Returns where this item came from, by way of its audio item.</summary>
    /// <returns>The origin, or <see langword="null"/>.</returns>
    public Input Origin() => Audio()?.Origin();

    /// <summary>
    /// Makes the MIDI item for an audio item — upstream's <c>Midi_item::get_midi</c>.
    /// </summary>
    /// <param name="item">The audio item to convert.</param>
    /// <returns>The MIDI item, or <see langword="null"/> when the item emits nothing.</returns>
    public static MidiItem GetMidi(AudioItem item)
    {
        switch (item)
        {
            case AudioKey key:
                return new MidiKey(key);
            case AudioInstrument instrument:
                return instrument.Str.Length != 0 ? new MidiInstrument(instrument) : null;
            case AudioNote note:
                return new MidiNote(note);
            case AudioPianoPedal pedal:
                return new MidiPianoPedal(pedal);
            case AudioTempo tempo:
                // Filter out tempo changes that cover no time. Upstream's note: it is
                // trickier to avoid creating them in the first place than to ignore them
                // here.
                return tempo.StartMoment < tempo.EndMoment ? new MidiTempo(tempo) : null;
            case AudioTimeSignature signature:
                return new MidiTimeSignature(signature);
            case AudioText text:
                return new MidiText(text);
            case AudioControlChange control:
                return new MidiControlChange(control);
            default:
                Warn.ProgrammingError(
                    "no MIDI representation for " + (item?.ClassName ?? "null"));
                return null;
        }
    }

    /// <summary>
    /// Encodes an integer as a MIDI variable-length quantity.
    /// <para>
    /// Upstream builds the answer in an int used as a little byte stack, which is why
    /// this looks the way it does rather than like the usual seven-bits-at-a-time loop.
    /// The shape is kept: it is what decides the byte ORDER, and a rewrite that merely
    /// looked cleaner would be a parity bug.
    /// </para>
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The variable-length bytes.</returns>
    public static byte[] Int2MidiVarintBytes(int value)
    {
        int buffer = value & 0x7f;
        while ((value >>= 7) > 0)
        {
            buffer <<= 8;
            buffer |= 0x80;
            buffer += value & 0x7f;
        }

        List<byte> bytes = new List<byte>(4);
        while (true)
        {
            bytes.Add((byte)buffer);
            if ((buffer & 0x80) != 0)
            {
                buffer >>= 8;
            }
            else
            {
                break;
            }
        }

        return bytes.ToArray();
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The item's class name.</returns>
    public override string ToString() => "#<" + ClassName + ">";
}

/// <summary>The end-of-track meta-event that closes every MIDI track.</summary>
public sealed class MidiEndOfTrack : MidiItem
{
    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_end_of_track";

    /// <summary>Returns no audio item: this event has no musical cause.</summary>
    /// <returns><see langword="null"/>.</returns>
    public override AudioItem Audio() => null;

    /// <summary>Returns the end-of-track bytes.</summary>
    /// <returns><c>FF 2F 00</c>.</returns>
    /// <remarks>
    /// THREE bytes, not two. Upstream writes <c>std::string ("\xff\x2f", 3)</c> and says
    /// so in a comment: the string literal's terminating NUL is part of the MIDI command,
    /// and the explicit length is what keeps it.
    /// </remarks>
    public override byte[] ToBytes() => new byte[] { 0xFF, 0x2F, 0x00 };
}

/// <summary>A MIDI item that plays on a particular channel.</summary>
public abstract class MidiChannelItem : MidiItem
{
    /// <summary>Initializes the item from the audio item's channel.</summary>
    /// <param name="item">The audio item this was made from.</param>
    protected MidiChannelItem(AudioItem item) => Channel = item.Channel;

    /// <summary>Gets or sets the MIDI channel this item plays on.</summary>
    public int Channel { get; set; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_channel_item";
}

/// <summary>A MIDI control change.</summary>
public sealed class MidiControlChange : MidiChannelItem
{
    private readonly AudioControlChange _audio;

    /// <summary>Initializes a control change.</summary>
    /// <param name="item">The audio item this was made from.</param>
    public MidiControlChange(AudioControlChange item)
        : base(item)
        => _audio = item;

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_control_change";

    /// <summary>Returns the audio item this was made from.</summary>
    /// <returns>The control change.</returns>
    public override AudioItem Audio() => _audio;

    /// <summary>Returns the control-change bytes.</summary>
    /// <returns>Status, control number, value.</returns>
    public override byte[] ToBytes()
        => new[]
        {
            (byte)(0xB0 + Channel),
            (byte)_audio.Control,
            (byte)_audio.Value,
        };
}

/// <summary>A program change: the instrument this channel now plays.</summary>
public sealed class MidiInstrument : MidiChannelItem
{
    private static readonly Symbol MidiProgramSymbol = Symbol.Intern("midi-program");

    private readonly AudioInstrument _audio;

    /// <summary>Initializes an instrument change, lowercasing the name in place.</summary>
    /// <param name="item">The audio item this was made from.</param>
    /// <remarks>
    /// The lowercasing MUTATES the audio item, which is upstream's own behaviour and is
    /// observable: <c>midi-program</c> is keyed on the lowercased name.
    /// </remarks>
    public MidiInstrument(AudioInstrument item)
        : base(item)
    {
        _audio = item;
        _audio.Str = StringConvert.ToLower(_audio.Str);
    }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_instrument";

    /// <summary>Returns the audio item this was made from.</summary>
    /// <returns>The instrument change.</returns>
    public override AudioItem Audio() => _audio;

    /// <summary>Returns the program-change bytes.</summary>
    /// <returns>Status and program number.</returns>
    public override byte[] ToBytes()
    {
        byte programByte = 0;

        object procedure = LilyPondScheme.LookupProcedure(MidiProgramSymbol);
        object program = procedure == null
            ? null
            : SchemeUtilities.CallCallback(procedure, Symbol.Intern(_audio.Str));

        // Upstream tests with scm_is_true, not from_scm<bool>: midi-program answers an
        // INTEGER or #f, and an integer is Scheme-true. Testing for #t instead would
        // reject every instrument that has a program number.
        if (program != null && SchemeUtilities.IsSchemeTrue(program))
        {
            programByte = (byte)Convert.ToInt64(program);
        }
        else
        {
            Warn.Warning("no such MIDI instrument: `" + _audio.Str + "'");
        }

        // YIKES! FIXME : Should be track. -rz   (upstream's comment, kept)
        return new[] { (byte)(0xC0 + Channel), programByte };
    }
}

/// <summary>A key-signature meta-event.</summary>
public sealed class MidiKey : MidiItem
{
    private readonly AudioKey _audio;

    /// <summary>Initializes a key signature.</summary>
    /// <param name="item">The audio item this was made from.</param>
    public MidiKey(AudioKey item) => _audio = item;

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_key";

    /// <summary>Returns the audio item this was made from.</summary>
    /// <returns>The key.</returns>
    public override AudioItem Audio() => _audio;

    /// <summary>Returns the key-signature bytes.</summary>
    /// <returns><c>FF 59 02</c>, the accidental count, and the mode.</returns>
    public override byte[] ToBytes()
        => new[]
        {
            (byte)0xFF,
            (byte)0x59,
            (byte)0x02,
            (byte)(_audio.Accidentals & 0xFF),
            (byte)(_audio.Major ? 0 : 1),
        };
}

/// <summary>A time-signature meta-event.</summary>
public sealed class MidiTimeSignature : MidiItem
{
    private readonly AudioTimeSignature _audio;

    /// <summary>Initializes a time signature.</summary>
    /// <param name="item">The audio item this was made from.</param>
    public MidiTimeSignature(AudioTimeSignature item) => _audio = item;

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_time_signature";

    /// <summary>Returns the audio item this was made from.</summary>
    /// <returns>The time signature.</returns>
    public override AudioItem Audio() => _audio;

    /// <summary>
    /// Returns the time-signature bytes, or none when the signature cannot be encoded.
    /// <para>
    /// MIDI can only express numerator over a POWER OF TWO. The two-pass loop is
    /// upstream's: pass one takes the signature as written, and if that will not encode,
    /// pass two reduces it the easy way and tries again — which is what lets 9/12 come
    /// out as the measure-length-preserving 3/4.
    /// </para>
    /// </summary>
    /// <returns>The bytes, or an empty array.</returns>
    public override byte[] ToBytes()
    {
        bool warned = false;

        void WarnUnsupported()
        {
            if (!warned)
            {
                warned = true;
                Warn.Warning(
                    "Unsupported MIDI time signature: (" + _audio.Num + ")/("
                    + _audio.Den + ")");
            }
        }

        Rational num = _audio.Num;
        if (!num.IsFinite || num < Rational.Zero)
        {
            WarnUnsupported();
            return Array.Empty<byte>();
        }

        Rational den = _audio.Den;
        if (!den.IsFinite || den <= Rational.Zero)
        {
            WarnUnsupported();
            return Array.Empty<byte>();
        }

        int midiDlog = 0x100; // out-of-range signals unsupported
        for (int tryCount = 0; tryCount < 2; ++tryCount)
        {
            if (num.Denominator == 1)
            {
                int dlog = Misc.IntLog2(den.Numerator);
                if (den.Numerator == (1L << dlog))
                {
                    // e.g., 4/4, which can be encoded without loss;
                    // or 4/(1/2), which this converts to 8/1;
                    // or 2/(8/3), which this converts to 6/8
                    if (dlog <= 0xff)
                    {
                        Rational newNum = num * new Rational(den.Denominator);
                        if (newNum <= new Rational(0xff))
                        {
                            num = newNum;
                            midiDlog = dlog;
                            break;
                        }
                    }
                }
            }

            // Reduce the time signature the easy way and try one more time.
            num = num / den;
            den = new Rational(num.Denominator);
            num = new Rational(num.Numerator);
        }

        if (midiDlog > 0xff) // Couldn't find a way to preserve the measure length.
        {
            // num and den are integers as a result of the loop above. Keep the powers of
            // two in the denominator and throw away other factors. This multiplies the
            // measure length in the MIDI file, so it will on occasion be not horrible:
            // when the notated number of measures in notated time signature matches the
            // factor we have discarded. This seems unlikely.
            WarnUnsupported();
            midiDlog = Misc.IntLog2(den.Numerator);
        }

        if (num.Numerator > 0xff || midiDlog > 0xff)
        {
            WarnUnsupported();
            return Array.Empty<byte>();
        }

        return new[]
        {
            (byte)0xFF,
            (byte)0x58,
            (byte)0x04,
            (byte)(num.Numerator & 0xFF),
            (byte)(midiDlog & 0xFF),
            (byte)(_audio.BeatBaseClocks & 0xFF),
            (byte)0x08,
        };
    }
}

/// <summary>A note-on event.</summary>
public class MidiNote : MidiChannelItem
{
    /// <summary>The pitch wheel's neutral position.</summary>
    protected const int PitchWheelCenter = 0x2000;

    private const int PitchWheelSemitone = 0x1000;

    /// <summary>Middle C's MIDI note number.</summary>
    public const int C0Pitch = 60;

    private readonly AudioNote _audio;

    /// <summary>Initializes a note-on and computes its velocity.</summary>
    /// <param name="item">The audio item this was made from.</param>
    public MidiNote(AudioNote item)
        : base(item)
    {
        _audio = item;

        double raw = item.Dynamic != null
            ? item.Dynamic.GetVolume(item.AudioColumn.When()) * 0x7f
            : 0x5a;

        Velocity = ClampVelocity(raw + item.ExtraVelocity);
    }

    /// <summary>Gets or sets the velocity, 0 to 127.</summary>
    public byte Velocity { get; protected set; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_note";

    /// <summary>Returns the audio item this was made from.</summary>
    /// <returns>The note.</returns>
    public override AudioItem Audio() => _audio;

    /// <summary>Gets the audio note this was made from, typed.</summary>
    public AudioNote AudioNote => _audio;

    /// <summary>Returns the note's MIDI semitone number, relative to middle C.</summary>
    /// <returns>The semitone offset.</returns>
    /// <remarks>
    /// Upstream rounds with <c>rint</c>, which under the default rounding mode is
    /// round-half-to-EVEN. .NET's default <see cref="MidpointRounding.ToEven"/> is the
    /// same rule, and it is named here rather than left implicit because half-away-from-
    /// zero would move quarter-tone pitches by a semitone.
    /// </remarks>
    public int GetSemitonePitch()
    {
        double tune = ((_audio.Pitch.TonePitch() + _audio.Transposing.TonePitch())
            * new Rational(2)).ToDouble();
        return (int)Math.Round(tune, MidpointRounding.ToEven);
    }

    /// <summary>Returns how far the pitch wheel must bend to reach a non-semitone pitch.</summary>
    /// <returns>The pitch-wheel offset, zero when the pitch is a whole semitone.</returns>
    public int GetFineTuning()
    {
        Rational tune = (_audio.Pitch.TonePitch() + _audio.Transposing.TonePitch())
            * new Rational(2);
        tune -= new Rational(GetSemitonePitch());

        tune *= new Rational(PitchWheelSemitone);
        return (int)tune.ToDouble();
    }

    /// <summary>Returns the note-on bytes, preceded by a pitch bend when one is needed.</summary>
    /// <returns>The bytes.</returns>
    public override byte[] ToBytes()
    {
        byte statusByte = (byte)(0x90 + Channel);
        List<byte> bytes = new List<byte>(8);

        // print warning if fine tuning was needed, HJJ   (upstream's comment, kept)
        int fine = GetFineTuning();
        if (fine != 0)
        {
            int finetune = PitchWheelCenter + fine;

            bytes.Add((byte)(0xE0 + Channel));
            bytes.Add((byte)(finetune & 0x7F));
            bytes.Add((byte)(finetune >> 7));
            bytes.Add(0x00);
        }

        bytes.Add(statusByte);
        bytes.Add((byte)(GetSemitonePitch() + C0Pitch));
        bytes.Add(Velocity);

        return bytes.ToArray();
    }

    /// <summary>
    /// Reproduces upstream's narrowing of the computed velocity.
    /// </summary>
    /// <param name="value">The computed velocity.</param>
    /// <returns>The velocity byte.</returns>
    /// <remarks>
    /// UPSTREAM NARROWS TO uint8_t BEFORE IT CLAMPS, and the order is observable: a
    /// value of 300 becomes 44, not 127. The clamp that follows is <c>min (max (v, 0),
    /// 0x7f)</c>, whose <c>max</c> against zero is a no-op on an unsigned type. Both are
    /// reproduced rather than tidied, because tidying them would change the bytes for any
    /// music that reaches the range — which is what a parity bug looks like.
    /// </remarks>
    private static byte ClampVelocity(double value)
    {
        long truncated = (long)value;
        byte narrowed = (byte)(truncated & 0xFF);
        return Math.Min(narrowed, (byte)0x7f);
    }
}

/// <summary>A note-off event, which MIDI expresses as a note-on with zero velocity.</summary>
public sealed class MidiNoteOff : MidiNote
{
    /// <summary>Initializes a note-off for a note-on.</summary>
    /// <param name="note">The note being stopped.</param>
    public MidiNoteOff(MidiNote note)
        : base(note.AudioNote)
    {
        On = note;
        Channel = note.Channel;

        // use note_on with velocity=0 instead of note_off
        Velocity = 0;
    }

    /// <summary>Gets the note-on this stops.</summary>
    public MidiNote On { get; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_note_off";

    /// <summary>Returns the note-off bytes, following them with a pitch-bend reset when needed.</summary>
    /// <returns>The bytes.</returns>
    public override byte[] ToBytes()
    {
        byte statusByte = (byte)(0x90 + Channel);
        List<byte> bytes = new List<byte>(8)
        {
            statusByte,
            (byte)(GetSemitonePitch() + C0Pitch),
            Velocity,
        };

        if (GetFineTuning() != 0)
        {
            // Move pitch wheel back to the central position.
            bytes.Add(0x00);
            bytes.Add((byte)(0xE0 + Channel));
            bytes.Add((byte)(PitchWheelCenter & 0x7F));
            bytes.Add((byte)(PitchWheelCenter >> 7));
        }

        return bytes.ToArray();
    }
}

/// <summary>A text meta-event: a lyric, a marker, a track name.</summary>
public sealed class MidiText : MidiItem
{
    private readonly AudioText _audio;

    /// <summary>Initializes a text event.</summary>
    /// <param name="item">The audio item this was made from.</param>
    public MidiText(AudioText item) => _audio = item;

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_text";

    /// <summary>Returns the audio item this was made from.</summary>
    /// <returns>The text.</returns>
    public override AudioItem Audio() => _audio;

    /// <summary>Returns the text meta-event bytes.</summary>
    /// <returns><c>FF</c>, the type, a variable-length byte count, and the UTF-8 text.</returns>
    /// <remarks>
    /// THE LENGTH PREFIX COUNTS UTF-8 BYTES. Upstream's <c>text_string_.length ()</c> is
    /// a byte count because a std::string already holds the encoded bytes; taking the
    /// .NET string's Length instead would count UTF-16 units and write a wrong,
    /// too-short length for every non-ASCII lyric — a file that parses but truncates.
    /// </remarks>
    public override byte[] ToBytes()
    {
        byte[] text = Encoding.UTF8.GetBytes(_audio.TextString ?? string.Empty);

        List<byte> bytes = new List<byte>(text.Length + 6)
        {
            0xFF,
            (byte)_audio.TextType,
        };

        bytes.AddRange(Int2MidiVarintBytes(text.Length));
        bytes.AddRange(text);

        return bytes.ToArray();
    }
}

/// <summary>A piano-pedal control change.</summary>
public sealed class MidiPianoPedal : MidiChannelItem
{
    private readonly AudioPianoPedal _audio;

    /// <summary>Initializes a pedal event.</summary>
    /// <param name="item">The audio item this was made from.</param>
    public MidiPianoPedal(AudioPianoPedal item)
        : base(item)
        => _audio = item;

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_piano_pedal";

    /// <summary>Returns the audio item this was made from.</summary>
    /// <returns>The pedal event.</returns>
    public override AudioItem Audio() => _audio;

    /// <summary>Returns the pedal control-change bytes.</summary>
    /// <returns>Status, control number, and the on/off value.</returns>
    public override byte[] ToBytes()
    {
        byte statusByte = (byte)(0xB0 + Channel);
        byte control = 0;

        if (_audio.PedalType == PedalType.Sostenuto)
        {
            control = 0x42;
        }
        else if (_audio.PedalType == PedalType.Sustain)
        {
            control = 0x40;
        }
        else if (_audio.PedalType == PedalType.UnaCorda)
        {
            control = 0x43;
        }

        // Upstream's `(audio_->dir_ == LEFT) * 0x7f': a pedal STARTING is LEFT/-1, so
        // the pedal goes DOWN (0x7f) at the start of the span and up at its end.
        byte pedal = (byte)(_audio.Dir == Direction.Negative ? 0x7f : 0x00);

        return new[] { statusByte, control, pedal };
    }
}

/// <summary>A tempo meta-event.</summary>
public sealed class MidiTempo : MidiItem
{
    private const long MinMicrosecondsPerQuarter = 1;
    private const long MaxMicrosecondsPerQuarter = 0xFFFFFF;

    private readonly AudioTempo _audio;

    /// <summary>Initializes a tempo change.</summary>
    /// <param name="item">The audio item this was made from.</param>
    public MidiTempo(AudioTempo item) => _audio = item;

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_tempo";

    /// <summary>Returns the audio item this was made from.</summary>
    /// <returns>The tempo change.</returns>
    public override AudioItem Audio() => _audio;

    /// <summary>Returns the tempo meta-event bytes.</summary>
    /// <returns><c>FF 51 03</c> and the microseconds per quarter note.</returns>
    public override byte[] ToBytes()
    {
        // I don't see any statement in the MIDI spec about what 0 might do. I assume it
        // could cause trouble. [DE]   (upstream's comment, kept)
        Rational microsecondsPerMinute = new Rational(60L * 1000000L);
        Rational wholesPerMinute = _audio.CalcWholesPerMinute();

        long usPerQuarter = (microsecondsPerMinute / (wholesPerMinute * new Rational(4)))
            .TruncatedInteger();

        if (usPerQuarter < MinMicrosecondsPerQuarter
            || MaxMicrosecondsPerQuarter < usPerQuarter)
        {
            usPerQuarter = Math.Clamp(
                usPerQuarter, MinMicrosecondsPerQuarter, MaxMicrosecondsPerQuarter);
            Warn.Warning(
                "Unsupported MIDI tempo (wholes/minute): " + wholesPerMinute);
        }

        List<byte> bytes = new List<byte>(6) { 0xFF, 0x51, 0x03 };
        bytes.AddRange(StringConvert.BigEndianBytesU24((uint)usPerQuarter));
        return bytes.ToArray();
    }
}

/// <summary>
/// A duration, which is never written to a file — upstream keeps it for tracing.
/// </summary>
public sealed class MidiDuration : MidiItem
{
    /// <summary>Initializes a duration.</summary>
    /// <param name="seconds">How long it is.</param>
    public MidiDuration(double seconds) => Seconds = seconds;

    /// <summary>Gets the duration in seconds.</summary>
    public double Seconds { get; }

    /// <summary>Gets the C++ class name this item corresponds to.</summary>
    public override string ClassName => "Midi_duration";

    /// <summary>Returns no audio item.</summary>
    /// <returns><see langword="null"/>.</returns>
    public override AudioItem Audio() => null;

    /// <summary>Returns the trace text as bytes, exactly as upstream's to_string does.</summary>
    /// <returns>The bytes.</returns>
    public override byte[] ToBytes()
        => Encoding.UTF8.GetBytes("<duration: " + Seconds + ">");
}
