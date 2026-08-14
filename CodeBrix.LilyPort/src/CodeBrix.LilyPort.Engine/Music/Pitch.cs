/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using System.Globalization;
using System.Text;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/pitch.cc, lily/include/pitch.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A "tonal" pitch: a pitch used in diatonic western music (24 quartertones in an
/// octave), as opposed to a frequency in Hz or an integer number of semitones.
/// <para>
/// Pitch is lexicographically ordered by octave, note name, alteration.
/// </para>
/// </summary>
public sealed class Pitch : IEquatable<Pitch>, IComparable<Pitch>, ISchemeEqual, ISchemePrintable
{
    /// <summary>The alteration of a double flat, in quarter tones.</summary>
    public const int DoubleFlat = -4;

    /// <summary>The alteration of a three-quarter flat, in quarter tones.</summary>
    public const int ThreeQuarterFlat = -3;

    /// <summary>The alteration of a flat, in quarter tones.</summary>
    public const int Flat = -2;

    /// <summary>The alteration of a semi flat, in quarter tones.</summary>
    public const int SemiFlat = -1;

    /// <summary>The alteration of a natural.</summary>
    public const int Natural = 0;

    /// <summary>The alteration of a semi sharp, in quarter tones.</summary>
    public const int SemiSharp = 1;

    /// <summary>The alteration of a sharp, in quarter tones.</summary>
    public const int Sharp = 2;

    /// <summary>The alteration of a three-quarter sharp, in quarter tones.</summary>
    public const int ThreeQuarterSharp = 3;

    /// <summary>The alteration of a double sharp, in quarter tones.</summary>
    public const int DoubleSharp = 4;

    // FIXME upstream: merge with *pitch->name* in chord-name.scm
    private static readonly string[] AccidentalNames =
    {
        "eses", "eseh", "es", "eh", string.Empty, "ih", "is", "isih", "isis",
    };

    private int _octave;
    private int _noteName;
    private Rational _alteration;

    /// <summary>Initializes a pitch.</summary>
    /// <param name="octave">The octave.</param>
    /// <param name="noteName">The note name, as a scale step index.</param>
    /// <param name="alteration">The alteration, in 200-cent tones.</param>
    /// <param name="scale">The scale to interpret the pitch against, or null for the default.</param>
    public Pitch(int octave, int noteName, Rational alteration, Scale scale)
    {
        _noteName = noteName;
        _alteration = alteration;
        _octave = octave;
        PitchScale = scale ?? Scale.DefaultGlobal;
        NormalizeOctave();
    }

    /// <summary>Initializes a pitch against the default global scale.</summary>
    /// <param name="octave">The octave.</param>
    /// <param name="noteName">The note name, as a scale step index.</param>
    /// <param name="alteration">The alteration, in 200-cent tones.</param>
    public Pitch(int octave, int noteName, Rational alteration)
        : this(octave, noteName, alteration, null)
    {
    }

    // FIXME upstream: why is octave == 0 and default not middle C?
    /// <summary>Initializes the default pitch: octave 0, note name 0, no alteration.</summary>
    public Pitch()
        : this(0, 0, Rational.Zero, null)
    {
    }

    /// <summary>Gets the octave.</summary>
    public int Octave => _octave;

    /// <summary>Gets the note name, as a scale step index.</summary>
    public int NoteName => _noteName;

    /// <summary>Gets the alteration, in 200-cent tones.</summary>
    public Rational Alteration => _alteration;

    // The slur group carried these: pitch.cc defines them as file-scope globals and
    // pitch.hh externs them, but nothing in the port had asked for one until
    // Slur_score_state::get_extra_encompass_infos, which shifts an accidental's collision
    // box by a different amount for each. Their absence was silent, not diagnosed.

    /// <summary>The alteration of a natural: none.</summary>
    public static Rational NaturalAlteration => new Rational(0);

    /// <summary>The alteration of a flat: down a semitone.</summary>
    public static Rational FlatAlteration => new Rational(-1, 2);

    /// <summary>The alteration of a double flat: down a whole tone.</summary>
    public static Rational DoubleFlatAlteration => new Rational(-1);

    /// <summary>The alteration of a sharp: up a semitone.</summary>
    public static Rational SharpAlteration => new Rational(1, 2);

    /// <summary>The alteration of a double sharp: up a whole tone.</summary>
    public static Rational DoubleSharpAlteration => new Rational(1);

    /// <summary>Gets the scale this pitch is interpreted against.</summary>
    public Scale PitchScale { get; }

    /// <summary>Compares two pitches lexicographically by octave, note name, alteration.</summary>
    /// <param name="left">The first pitch.</param>
    /// <param name="right">The second pitch.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public static int Compare(Pitch left, Pitch right)
    {
        if (left == null || right == null)
        {
            throw new ArgumentNullException(left == null ? nameof(left) : nameof(right));
        }

        int octave = left._octave - right._octave;
        if (octave != 0)
        {
            return octave;
        }

        int noteName = left._noteName - right._noteName;
        if (noteName != 0)
        {
            return noteName;
        }

        Rational alteration = left._alteration - right._alteration;
        if (alteration.IsNonZero)
        {
            return alteration.IsNegative ? -1 : 1;
        }

        return 0;
    }

    /// <summary>Gets the pitch height in scale steps.</summary>
    /// <returns>The number of steps above the tonic of octave zero.</returns>
    public int Steps() => _noteName + (_octave * PitchScale.StepCount);

    /// <summary>Gets the pitch height in 200-cent tones.</summary>
    /// <returns>The tone height, including the alteration.</returns>
    public Rational TonePitch() => PitchScale.TonesAtStep(_noteName, _octave) + _alteration;

    /// <summary>
    /// Gets the pitch height rounded to semitones. The pitch need not be normalized --
    /// normalization itself uses this.
    /// </summary>
    /// <returns>The height in semitones.</returns>
    public int RoundedSemitonePitch()
        => (int)Math.Floor(((TonePitch() * new Rational(2)) + new Rational(1, 2)).ToDouble());

    /// <summary>Gets the pitch height rounded to quarter tones.</summary>
    /// <returns>The height in quarter tones.</returns>
    public int RoundedQuartertonePitch()
        => (int)Math.Floor(((TonePitch() * new Rational(4)) + new Rational(1, 2)).ToDouble());

    /// <summary>Returns this pitch transposed by another, treated as an interval.</summary>
    /// <param name="delta">The interval to transpose by.</param>
    /// <returns>The transposed pitch.</returns>
    public Pitch Transposed(Pitch delta)
    {
        Pitch result = Copy();
        result.Transpose(delta);
        return result;
    }

    /// <summary>Returns this pitch with its alteration and octave normalized.</summary>
    /// <returns>The normalized pitch.</returns>
    public Pitch Normalized()
    {
        Pitch result = Copy();
        result.NormalizeAlteration();
        result.NormalizeOctave();
        return result;
    }

    /// <summary>Returns the interval from one pitch to another.</summary>
    /// <param name="from">The starting pitch.</param>
    /// <param name="to">The ending pitch.</param>
    /// <returns>The interval, expressed as a pitch.</returns>
    public static Pitch Interval(Pitch from, Pitch to)
    {
        if (from == null || to == null)
        {
            throw new ArgumentNullException(from == null ? nameof(from) : nameof(to));
        }

        Rational sound = to.TonePitch() - from.TonePitch();
        Pitch difference = new Pitch(
            to.Octave - from.Octave,
            to.NoteName - from.NoteName,
            to.Alteration - from.Alteration,
            from.PitchScale);

        return difference.Transposed(
            new Pitch(0, 0, sound - difference.TonePitch(), from.PitchScale));
    }

    /// <summary>
    /// Returns this pitch re-read as relative to another, counting from the last pitch.
    /// </summary>
    /// <param name="previous">The pitch to be relative to.</param>
    /// <returns>The absolute pitch.</returns>
    public Pitch ToRelativeOctave(Pitch previous)
    {
        if (previous == null)
        {
            throw new ArgumentNullException(nameof(previous));
        }

        // Account for c' = octave 1 rather than 0.
        int octaveModifier = _octave + 1;
        Pitch upPitch = previous.Copy();
        Pitch downPitch = previous.Copy();

        upPitch._alteration = _alteration;
        downPitch._alteration = _alteration;

        upPitch.UpTo(_noteName);
        downPitch.DownTo(_noteName);

        int height = previous.Steps();
        Pitch chosen = Math.Abs(upPitch.Steps() - height) < Math.Abs(downPitch.Steps() - height)
            ? upPitch
            : downPitch;

        chosen._octave += octaveModifier;
        return chosen;
    }

    /// <summary>Returns the pitch with the opposite alteration and inverted position.</summary>
    /// <returns>The negated pitch.</returns>
    public Pitch Negated() => Interval(this, new Pitch(0, 0, Rational.Zero, PitchScale));

    /// <summary>Returns LilyPond's textual form of this pitch, for example <c>cis'</c>.</summary>
    /// <returns>The pitch name.</returns>
    public override string ToString()
    {
        int name = (_noteName + 2) % PitchScale.StepCount;
        StringBuilder builder = new StringBuilder();
        builder.Append((char)(name + 'a'));

        Rational quarterTones = _alteration * new Rational(4, 1);
        int index = (int)Math.Round(quarterTones.ToDouble() + 4.0, MidpointRounding.ToEven);
        builder.Append(index >= 0 && index < AccidentalNames.Length ? AccidentalNames[index] : "??");

        if (_octave >= 0)
        {
            builder.Append('\'', _octave + 1);
        }
        else
        {
            builder.Append(',', -_octave - 1);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns the smob's external representation, <c>#&lt;Pitch e' &gt;</c>.
    /// <para>
    /// Upstream: <c>Pitch::print_smob</c> (<c>lily/pitch.cc</c>), which writes
    /// <c>"#&lt;Pitch "</c>, DISPLAYS <c>to_string ()</c> — so the name is unquoted — and
    /// closes with <c>" &gt;"</c>. The SPACE BEFORE the angle bracket is upstream's and is
    /// load-bearing for byte parity with the generated manual; it is not a typo here.
    /// </para>
    /// <para>
    /// This is deliberately separate from <see cref="ToString"/>, which stays the bare
    /// <c>to_string ()</c> content that <c>ly:pitch-&gt;string</c> and error messages read.
    /// </para>
    /// </summary>
    /// <returns>The external representation.</returns>
    public string PrintRepresentation() => "#<Pitch " + ToString() + " >";

    /// <summary>Compares this pitch with another.</summary>
    /// <param name="other">The pitch to compare with.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public int CompareTo(Pitch other) => Compare(this, other);

    /// <summary>
    /// Determines whether two pitches are equal. Upstream compares note name, octave and
    /// alteration but NOT the scale, and this follows it.
    /// </summary>
    /// <param name="other">The pitch to compare with.</param>
    /// <returns><see langword="true"/> when the pitches match.</returns>
    public bool Equals(Pitch other)
        => other != null
           && _noteName == other._noteName
           && _octave == other._octave
           && _alteration == other._alteration;

    /// <summary>Compares this pitch with another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal pitch.</returns>
    public override bool Equals(object obj) => Equals(obj as Pitch);

    /// <summary>Returns a hash code consistent with <see cref="Equals(Pitch)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(_octave, _noteName, _alteration);

    private Pitch Copy()
    {
        Pitch copy = new Pitch(0, 0, Rational.Zero, PitchScale);
        copy._octave = _octave;
        copy._noteName = _noteName;
        copy._alteration = _alteration;
        return copy;
    }

    private void Transpose(Pitch delta)
    {
        Rational newAlteration = TonePitch() + delta.TonePitch();

        _octave += delta._octave;
        _noteName += delta._noteName;
        _alteration += newAlteration - TonePitch();

        NormalizeOctave();
    }

    private void UpTo(int noteName)
    {
        if (_noteName > noteName)
        {
            _octave++;
        }

        _noteName = noteName;
    }

    private void DownTo(int noteName)
    {
        if (_noteName < noteName)
        {
            _octave--;
        }

        _noteName = noteName;
    }

    private void NormalizeOctave()
    {
        int normalizedStep = _noteName % PitchScale.StepCount;
        if (normalizedStep < 0)
        {
            normalizedStep += PitchScale.StepCount;
        }

        _octave += (_noteName - normalizedStep) / PitchScale.StepCount;
        _noteName = normalizedStep;
    }

    private void NormalizeAlteration()
    {
        while (_alteration > Rational.One)
        {
            _alteration -= PitchScale.StepSize(_noteName);
            _noteName++;
        }

        while (_alteration < -Rational.One)
        {
            _noteName--;
            _alteration += PitchScale.StepSize(_noteName);
        }
    }

    /* TODO: find a good place for this function */

    /// <summary>
    /// Recomputes a context's <c>middleCPosition</c> from its clef position and octave
    /// offset — <c>set_middle_C</c>.
    /// </summary>
    /// <param name="c">The context to update.</param>
    /// <remarks>
    /// A free function in <c>lily/pitch.cc</c>, carried here as a static because that is
    /// the file's class in this port. Added later: <c>pitch.cc</c> has read
    /// <c>ported</c> from the start, but this function had never come across — nothing asked
    /// until <c>Ottava_spanner_engraver</c> needed to shift middle C by an octave.
    /// </remarks>
    public static void SetMiddleC(Translation.Context c)
    {
        if (c == null)
        {
            return;
        }

        int clefPos = ToInt(c.GetProperty(MiddleCClefPositionSymbol), 0);
        int offset = ToInt(c.GetProperty(MiddleCOffsetSymbol), 0);

        /* middleCCuePosition overrides the clef! */
        object cuePos = c.GetProperty(MiddleCCuePositionSymbol);
        if (SchemeConvert.IsNumber(cuePos))
        {
            clefPos = ToInt(cuePos, 0);
        }

        c.SetProperty(MiddleCPositionSymbol, (long)(clefPos + offset));
    }

    private static readonly Symbol MiddleCClefPositionSymbol
        = Symbol.Intern("middleCClefPosition");
    private static readonly Symbol MiddleCOffsetSymbol = Symbol.Intern("middleCOffset");
    private static readonly Symbol MiddleCCuePositionSymbol
        = Symbol.Intern("middleCCuePosition");
    private static readonly Symbol MiddleCPositionSymbol = Symbol.Intern("middleCPosition");

    private static int ToInt(object value, int fallback)
        => SchemeConvert.IsNumber(value)
            ? (int)SchemeConvert.ToLong(value, "middle-C")
            : fallback;

    /// <summary>
    /// Compares by VALUE for Scheme's <c>equal?</c>.
    /// <para>Upstream: <c>Pitch::equal_p</c>, the smob equality handler
    /// <c>scm_equal_p</c> dispatches to. Without it two distinct objects holding the
    /// same value answer <c>#f</c>, which is identity, not equality.</para>
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the two are equal by value.</returns>
    public bool SchemeEquals(object other) => Equals(other);

}
