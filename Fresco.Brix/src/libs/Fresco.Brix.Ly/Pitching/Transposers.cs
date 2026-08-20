// === python-ly ly.pitch.transpose module (the transposer classes) ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fresco.Brix.Ly.Pitching; //was previously: ly/pitch/transpose.py (the classes);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Anything that can change a pitch in place — what upstream expresses with
/// duck typing over the transposer classes.
/// </summary>
public abstract class TransposerBase
{
    /// <summary>Changes the pitch in place.</summary>
    /// <param name="pitch">The pitch to change.</param>
    public abstract void Transpose(Pitch pitch);
}

/// <summary>
/// Transposes pitches by the interval between two pitches, over a scale that
/// lists the pitch height of each unaltered step (0 to 6); the default is the
/// normal C D E F G A B scale.
/// </summary>
public class Transposer : TransposerBase
{
    /// <summary>The pitch height of every unaltered step.</summary>
    public static readonly Fraction[] DefaultScale =
    {
        new Fraction(0), new Fraction(1), new Fraction(2), new Fraction(5, 2),
        new Fraction(7, 2), new Fraction(9, 2), new Fraction(11, 2),
    };

    /// <summary>Initializes the transposer from one pitch to another.</summary>
    /// <param name="fromPitch">The pitch transposed from.</param>
    /// <param name="toPitch">The pitch transposed to.</param>
    /// <param name="scale">The scale, or <see langword="null"/> for the
    /// default one.</param>
    public Transposer(Pitch fromPitch, Pitch toPitch, Fraction[] scale = null)
    {
        if (scale != null) { Scale = scale; }

        //The number of octaves to transpose.
        Octave = toPitch.Octave - fromPitch.Octave;

        //The number of base note steps (c->d == 1, e->f == 1, and so on).
        Steps = toPitch.Note - fromPitch.Note;

        //The number (fraction) of real whole steps.
        Alter = Scale[toPitch.Note] + toPitch.Alter - Scale[fromPitch.Note] - fromPitch.Alter;
    }

    /// <summary>Initializes a transposer whose subclass sets the members.</summary>
    /// <param name="scale">The scale, or <see langword="null"/> for the
    /// default one.</param>
    protected Transposer(Fraction[] scale = null)
    {
        if (scale != null) { Scale = scale; }
    }

    /// <summary>Gets the scale in use.</summary>
    protected Fraction[] Scale { get; } = DefaultScale;

    /// <summary>Gets or sets the octave distance.</summary>
    protected int Octave { get; set; }

    /// <summary>Gets or sets the base-step distance.</summary>
    protected int Steps { get; set; }

    /// <summary>Gets or sets the alteration distance.</summary>
    protected Fraction Alter { get; set; }

    /// <inheritdoc/>
    public override void Transpose(Pitch pitch)
    {
        (int doct, int note) = PitchMath.DivMod(pitch.Note + Steps, 7);
        pitch.Alter += Alter - (doct * 6) - Scale[note] + Scale[pitch.Note];
        pitch.Octave += Octave + doct;
        pitch.Note = note;

        //Change the step if alterations fall outside -1 .. 1.
        while (pitch.Alter > new Fraction(1))
        {
            (doct, note) = PitchMath.DivMod(pitch.Note + 1, 7);
            pitch.Alter -= (doct * 6) + Scale[note] - Scale[pitch.Note];
            pitch.Octave += doct;
            pitch.Note = note;
        }

        while (pitch.Alter < new Fraction(-1))
        {
            (doct, note) = PitchMath.DivMod(pitch.Note - 1, 7);
            pitch.Alter += (doct * -6) + Scale[pitch.Note] - Scale[note];
            pitch.Octave += doct;
            pitch.Note = note;
        }
    }
}

/// <summary>
/// Makes complicated accidentals simpler by substituting naturals wherever
/// that names the same pitch.
/// </summary>
public class Simplifier : Transposer
{
    /// <summary>Initializes the simplifier.</summary>
    /// <param name="scale">The scale, or <see langword="null"/> for the
    /// default one.</param>
    public Simplifier(Fraction[] scale = null)
        : base(scale)
    {
    }

    /// <inheritdoc/>
    public override void Transpose(Pitch pitch)
    {
        if (pitch.Alter == new Fraction(1))
        {
            (int doct, int note) = PitchMath.DivMod(pitch.Note + 1, 7);
            pitch.Alter -= (doct * 6) + Scale[note] - Scale[pitch.Note];
            pitch.Octave += doct;
            pitch.Note = note;
        }
        else if (pitch.Alter == new Fraction(-1))
        {
            (int doct, int note) = PitchMath.DivMod(pitch.Note - 1, 7);
            pitch.Alter += (doct * -6) + Scale[pitch.Note] - Scale[note];
            pitch.Octave += doct;
            pitch.Note = note;
        }

        if (pitch.Alter == new Fraction(1, 2))
        {
            (int doct, int note) = PitchMath.DivMod(pitch.Note + 1, 7);
            Fraction alter = (doct * 6) + Scale[note] - Scale[pitch.Note];
            if (alter == new Fraction(1, 2))
            {
                pitch.Alter = Fraction.Zero;
                pitch.Octave += doct;
                pitch.Note = note;
            }
        }
        else if (pitch.Alter == new Fraction(-1, 2))
        {
            (int doct, int note) = PitchMath.DivMod(pitch.Note - 1, 7);
            Fraction alter = (doct * -6) + Scale[pitch.Note] - Scale[note];
            if (alter == new Fraction(1, 2))
            {
                pitch.Alter = Fraction.Zero;
                pitch.Octave += doct;
                pitch.Note = note;
            }
        }
    }
}

/// <summary>
/// Shifts pitches onto a mode or scale: a pitch already in the scale is left
/// alone, any other moves to the closest scale pitch.
/// </summary>
public class ModeShifter : Transposer
{
    private readonly List<Pitch>[] _modePitches = new List<Pitch>[7];

    /// <summary>
    /// Builds the scale's pitches from a key and a scale definition — a list
    /// of (step, alteration) pairs in the same shape as
    /// <see cref="Transposer"/>'s scale.
    /// </summary>
    /// <param name="key">The key the scale starts on.</param>
    /// <param name="scale">The scale definition.</param>
    public ModeShifter(Pitch key, IEnumerable<(int Step, Fraction Alter)> scale)
    {
        Octave = 0;
        foreach ((int step, Fraction alter) in scale)
        {
            Pitch p = key.Copy();
            Steps = step;
            Alter = alter;
            base.Transpose(p);
            if (_modePitches[p.Note] != null)
            {
                _modePitches[p.Note].Add(p);
            }
            else
            {
                _modePitches[p.Note] = new List<Pitch> { p };
            }
        }
    }

    /// <summary>
    /// Answers the closest pitch of the scale: the one scale note on the same
    /// base step when there is exactly one, otherwise whichever neighbour is
    /// nearer.
    /// </summary>
    /// <param name="pitch">The pitch to place.</param>
    /// <returns>The scale pitch.</returns>
    public Pitch ClosestPitch(Pitch pitch)
    {
        int step = pitch.Note;
        List<Pitch> modePitch = _modePitches[step];
        if (modePitch != null && modePitch.Count == 2)
        {
            return ComparePitch(pitch, modePitch[0], modePitch[1]);
        }

        Pitch up = NextPitch(step, true)[0];
        List<Pitch> downCandidates = NextPitch(step, false);
        Pitch down = downCandidates[downCandidates.Count - 1];
        return ComparePitch(pitch, up, down);
    }

    /// <inheritdoc/>
    public override void Transpose(Pitch pitch)
    {
        List<Pitch> modePitch = _modePitches[pitch.Note];
        if (modePitch != null)
        {
            foreach (Pitch mp in modePitch)
            {
                if (pitch.Note == mp.Note && pitch.Alter == mp.Alter) { return; }
            }
        }

        Pitch closest = ClosestPitch(pitch);
        Steps = closest.Note - pitch.Note;
        if (Steps > 3)
        {
            Octave = -1;
        }
        else if (Steps < -3)
        {
            Octave = 1;
        }
        else
        {
            Octave = 0;
        }

        Alter = Scale[closest.Note] + closest.Alter - Scale[pitch.Note] - pitch.Alter;
        base.Transpose(pitch);
    }

    private List<Pitch> NextPitch(int step, bool up)
    {
        while (true)
        {
            List<Pitch> modePitch = _modePitches[step];
            if (modePitch != null) { return modePitch; }

            step = PitchMath.Mod(up ? step + 1 : step - 1, 7);
        }
    }

    private Pitch ComparePitch(Pitch pitch, Pitch upPitch, Pitch downPitch)
    {
        Fraction upNum = Scale[upPitch.Note] + upPitch.Alter;
        Fraction downNum = Scale[downPitch.Note] + downPitch.Alter;
        Fraction pNum = Scale[pitch.Note] + pitch.Alter;
        return upNum - pNum < pNum - downNum ? upPitch : downPitch;
    }
}

/// <summary>
/// Transposes pitches by a number of steps inside a given major scale, named
/// by its index in the circle of fifths (C major = 0).
/// </summary>
public class ModalTransposer : TransposerBase
{
    private readonly int[] _notes = { 0, 1, 2, 3, 4, 5, 6 };
    private readonly Fraction[] _alter;

    /// <summary>Initializes the transposer.</summary>
    /// <param name="numSteps">The number of scale steps to move by.</param>
    /// <param name="scaleIndex">The scale's index in the circle of fifths.</param>
    public ModalTransposer(int numSteps = 1, int scaleIndex = 0)
    {
        NumSteps = numSteps;

        //Initialize to Db, then update to the desired mode.
        _alter = Enumerable.Repeat(new Fraction(-1, 2), 7).ToArray();
        for (int i = 0; i < scaleIndex; i++)
        {
            int keyNameIndex = ((i + 1) * 4) % _notes.Length;
            int accidentalIndex = PitchMath.Mod(keyNameIndex - 1, _notes.Length);
            _alter[accidentalIndex] += new Fraction(1, 2);
        }
    }

    /// <summary>Gets the number of scale steps moved by.</summary>
    public int NumSteps { get; }

    /// <summary>
    /// Answers the index of a key in the circle of fifths: Cb is 0, C is 7 and
    /// C# is 14. (Upstream's docstring says B# is 14, but its list — copied
    /// verbatim below — ends at C#, and upstream raises for B# just as this
    /// does.)
    /// </summary>
    /// <param name="text">The key name.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentException">When the name is not a key.</exception>
    public static int GetKeyIndex(string text)
    {
        string[] circleOfFifths =
        {
            "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F",
            "C", "G", "D", "A", "E", "B", "F#", "C#",
        };

        int index = Array.IndexOf(circleOfFifths, Capitalize(text));
        if (index < 0)
        {
            throw new ArgumentException($"'{text}' is not a key name", nameof(text));
        }

        return index;
    }

    /// <inheritdoc/>
    public override void Transpose(Pitch pitch)
    {
        //Upstream first looks for an exact match with `pitch.alter == self.alter`,
        //comparing a number to the WHOLE alteration list — never true in python
        //either, so the fall-through below is the only live path.
        int fromScaleDegree = Array.IndexOf(_notes, pitch.Note);
        Fraction accidental = pitch.Alter - _alter[fromScaleDegree];

        (int toOctaveMod, int toScaleDegree) = PitchMath.DivMod(fromScaleDegree + NumSteps, 7);
        pitch.Note = _notes[toScaleDegree];
        pitch.Alter = _alter[toScaleDegree] + accidental;
        pitch.Octave += toOctaveMod;
    }

    /// <summary>Python's <c>str.capitalize()</c>, in the invariant culture.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The capitalized text.</returns>
    private static string Capitalize(string text)
        => string.IsNullOrEmpty(text)
            ? text
            : char.ToUpperInvariant(text[0])
                + text.Substring(1).ToLowerInvariant();
}
