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
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/key-performer.cc, lily/time-signature-performer.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port.

/// <summary>Turns key changes into MIDI key-signature events.</summary>
public sealed class KeyPerformer : Performer
{
    private static readonly Symbol PitchAlistSymbol = Symbol.Intern("pitch-alist");
    private static readonly Symbol InstrumentTranspositionSymbol
        = Symbol.Intern("instrumentTransposition");
    private static readonly Symbol AlterationsInKeySymbol
        = Symbol.Intern("alterations-in-key");
    private static readonly Symbol KeyChangeEventSymbol = Symbol.Intern("key-change-event");

    private StreamEvent _keyEvent;

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public KeyPerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Key_performer";

    /// <summary>Starts listening for key changes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(KeyChangeEventSymbol, ListenKeyChange);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>
    /// Emits a key signature, deciding major or minor from the transposed scale.
    /// </summary>
    public override void ProcessMusic()
    {
        if (_keyEvent == null)
        {
            return;
        }

        object pitchList = _keyEvent.GetProperty(PitchAlistSymbol);

        if (GetProperty(InstrumentTranspositionSymbol) is Pitch transposition)
        {
            pitchList = MusicSequence.TransposeKeyAlist(pitchList, transposition);
        }

        object procedure = LilyPondScheme.LookupProcedure(AlterationsInKeySymbol);
        object accidentals = procedure == null
            ? null
            : SchemeUtilities.CallCallback(procedure, pitchList);

        if (!(pitchList is Pair first) || !(first.Car is Pair head))
        {
            return;
        }

        Pitch keyDo = new Pitch(
            0,
            (int)Convert.ToInt64(head.Car),
            SchemeConvert.TryToRational(head.Cdr, out Rational alteration)
                ? alteration
                : Rational.Zero);

        object cPitchList = MusicSequence.TransposeKeyAlist(pitchList, keyDo.Negated());

        /* MIDI keys are too limited for lilypond scales.
           We check for minor scale and assume major otherwise.  */
        Pair third = Assoc(2L, cPitchList);
        bool minor = third != null
            && SchemeConvert.TryToRational(third.Cdr, out Rational thirdAlteration)
            && thirdAlteration == Pitch.FlatAlteration;

        Announce(
            _keyEvent,
            new AudioKey(accidentals == null ? 0 : (int)Convert.ToInt64(accidentals), !minor));

        _keyEvent = null;
    }

    private static Pair Assoc(object key, object alist)
    {
        foreach (object entry in Pair.ToList(alist))
        {
            if (entry is Pair pair && SchemeUtilities.IsEqual(pair.Car, key))
            {
                return pair;
            }
        }

        return null;
    }

    private void ListenKeyChange(StreamEvent ev)
    {
        if (_keyEvent == null)
        {
            _keyEvent = ev;
        }
    }
}

/// <summary>
/// Emits a MIDI time signature whenever <c>timeSignature</c> changes or a <c>\time</c>
/// command is issued.
/// </summary>
public sealed class TimeSignaturePerformer : Performer
{
    private static readonly Symbol TimeSignatureSymbol = Symbol.Intern("timeSignature");
    private static readonly Symbol BeatBaseSymbol = Symbol.Intern("beatBase");
    private static readonly Symbol BeatStructureSymbol = Symbol.Intern("beatStructure");
    private static readonly Symbol TimeSignatureToFractionSymbol
        = Symbol.Intern("time-signature->fraction");
    private static readonly Symbol ReferenceTimeSignatureEventSymbol
        = Symbol.Intern("reference-time-signature-event");

    private AudioTimeSignature _audio;
    private object _lastTimeFraction = false;
    private StreamEvent _event;

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public TimeSignaturePerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Time_signature_performer";

    /// <summary>Starts listening for reference time signatures.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(ReferenceTimeSignatureEventSymbol, ListenReferenceTimeSignature);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Emits a time signature when one is due.</summary>
    public override void ProcessMusic()
    {
        if (_audio != null)
        {
            return;
        }

        // TODO: For a strictly alternating time signature, it would likely be better to
        // insert a change for each component, though some users might prefer that to be
        // optional. (Any components with subdivided numerators would still need to have
        // their numerators totaled.)   (upstream's TODO, kept)
        object procedure = LilyPondScheme.LookupProcedure(TimeSignatureToFractionSymbol);
        object fraction = procedure == null
            ? null
            : SchemeUtilities.CallCallback(procedure, GetProperty(TimeSignatureSymbol));

        // If there is a \time event, we emit the time signature even if it is the same as
        // previously. Midi may need it in some cases. In particular:
        //
        // TODO: when a \partial command runs out, the time signature should get reemitted
        // at the start of the next bar in order to have MIDI devices resynchronise to the
        // meter. \partial has no viable representation in Midi.
        if (!(fraction is Pair fractionPair)
            || (_event == null && SchemeUtilities.IsEqual(fraction, _lastTimeFraction)))
        {
            return;
        }

        _lastTimeFraction = fraction;

        Rational beatBase = SchemeConvert.TryToRational(
            GetProperty(BeatBaseSymbol), out Rational baseValue)
            ? baseValue
            : new Rational(1, 4);

        Rational beatBaseClocks = new Rational(96) * beatBase;

        object commonBeat = CalcCommonBeat(GetProperty(BeatStructureSymbol));
        if (SchemeConvert.TryToRational(commonBeat, out Rational common)
            && common != Rational.Zero)
        {
            beatBaseClocks *= common;
        }

        if (beatBaseClocks.Denominator != 1
            || beatBaseClocks.Numerator < 1
            || beatBaseClocks.Numerator > 255)
        {
            const string message = "bad beatBase/beatStructure for MIDI time signature";
            if (_event != null)
            {
                Epg8Support.EventWarning(_event, message);
            }
            else
            {
                Warn.Warning(message);
            }

            // Use a quarter note, 24 MIDI clocks
            beatBaseClocks = new Rational(24);
        }

        _audio = new AudioTimeSignature(
            SchemeConvert.TryToRational(fractionPair.Car, out Rational numerator)
                ? numerator
                : Rational.Zero,
            SchemeConvert.TryToRational(fractionPair.Cdr, out Rational denominator)
                ? denominator
                : Rational.One,
            (int)beatBaseClocks.Numerator);

        AnnounceElement(new Audio.AudioElementInfo(_audio, _event));
    }

    /// <summary>Forgets this timestep's signature and event.</summary>
    public override void StopTranslationTimestep()
    {
        _audio = null;
        _event = null;
    }

    /// <summary>
    /// Returns the metronome period implied by a beat structure.
    /// </summary>
    /// <param name="beatStructure">The beat structure.</param>
    /// <returns>The common beat, or 1 when any beat is fractional.</returns>
    private static object CalcCommonBeat(object beatStructure)
    {
        Rational commonBeat = Rational.Zero;

        foreach (object beat in Pair.ToList(beatStructure))
        {
            if (SchemeConvert.TryToRational(beat, out Rational value)
                && value.Denominator == 1)
            {
                commonBeat = Gcd(commonBeat, value);
            }
            else
            {
                // When any beat has a fractional part, just use the beat base as the
                // metronome period. Example: Time signature 2½/4, yielding beatBase 1/4
                // and beatStructure (1 1 1/2).
                //
                // The closest approximation of the fraction that can be encoded in MIDI
                // is 5/8, but we distinguish it from 2½/4 by setting the metronome period
                // to a quarter note rather than an eighth note.
                return Rational.One;
            }
        }

        return commonBeat;
    }

    private static Rational Gcd(Rational a, Rational b)
    {
        long left = Math.Abs(a.Numerator);
        long right = Math.Abs(b.Numerator);

        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return new Rational(left);
    }

    private void ListenReferenceTimeSignature(StreamEvent ev) => _event = ev;
}
