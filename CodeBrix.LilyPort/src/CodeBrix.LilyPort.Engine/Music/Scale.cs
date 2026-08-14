/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2007--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/scale.cc, lily/include/scale.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A scale: the tone offsets of each step above the tonic, measured in 200-cent tones.
/// <para>
/// Every <see cref="Pitch"/> is interpreted against a scale, which is what lets LilyPond
/// support non-diatonic tunings without the pitch type knowing about them.
/// </para>
/// </summary>
public sealed class Scale
{
    private readonly Rational[] _stepTones;

    /// <summary>Initializes a scale from its step tones.</summary>
    /// <param name="stepTones">The number of 200-cent tones of each step above the tonic.</param>
    public Scale(IReadOnlyList<Rational> stepTones)
    {
        if (stepTones == null)
        {
            throw new ArgumentNullException(nameof(stepTones));
        }

        _stepTones = new Rational[stepTones.Count];
        for (int i = 0; i < stepTones.Count; i++)
        {
            _stepTones[i] = stepTones[i];
        }
    }

    /// <summary>
    /// The default global scale: the seven steps of the major scale, in tones.
    /// <para>
    /// Upstream builds this in <c>scm/lily.scm</c> as
    /// <c>(ly:make-scale #(0 1 2 5/2 7/2 9/2 11/2))</c> and stores it where the C++
    /// reaches it as <c>Lily::default_global_scale</c>. A pitch constructed before that
    /// runs would assert, so the same default lives here.
    /// </para>
    /// </summary>
    public static readonly Scale DefaultGlobal = new Scale(new[]
    {
        new Rational(0),
        new Rational(1),
        new Rational(2),
        new Rational(5, 2),
        new Rational(7, 2),
        new Rational(9, 2),
        new Rational(11, 2),
    });

    /// <summary>Gets the number of steps in the scale.</summary>
    public int StepCount => _stepTones.Length;

    /// <summary>Gets the step tones.</summary>
    public IReadOnlyList<Rational> StepTones => _stepTones;

    /// <summary>Returns the tone offset of a step in a given octave.</summary>
    /// <param name="step">The step index, which need not be normalized.</param>
    /// <param name="octave">The octave.</param>
    /// <returns>The offset in 200-cent tones.</returns>
    public Rational TonesAtStep(int step, int octave)
    {
        int normalizedStep = NormalizeStep(step);
        octave += (step - normalizedStep) / StepCount;

        // There are 6 tones in an octave.
        return _stepTones[normalizedStep] + new Rational(octave * 6);
    }

    /// <summary>Returns the tone distance from a step to the next.</summary>
    /// <param name="step">The step index, which need not be normalized.</param>
    /// <returns>The size of the step in 200-cent tones.</returns>
    public Rational StepSize(int step)
    {
        int normalizedStep = NormalizeStep(step);

        // Wrap around if we are asked for the final note of the scale (6 is the number
        // of tones of the octave above the first note).
        if (normalizedStep + 1 == StepCount)
        {
            return new Rational(6) - _stepTones[normalizedStep];
        }

        return _stepTones[normalizedStep + 1] - _stepTones[normalizedStep];
    }

    /// <summary>Reduces a step index into the range of the scale.</summary>
    /// <param name="step">The step index.</param>
    /// <returns>The normalized step index.</returns>
    public int NormalizeStep(int step)
    {
        int result = step % StepCount;
        if (result < 0)
        {
            result += StepCount;
        }

        return result;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description naming the step count.</returns>
    public override string ToString() => "#<Scale " + StepCount + " steps>";
}
