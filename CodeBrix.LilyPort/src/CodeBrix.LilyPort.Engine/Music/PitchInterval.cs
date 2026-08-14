/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/pitch-interval.cc, lily/include/pitch-interval.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A pair of pitches bounding a range, kept as a <see cref="DrulArray{T}"/> of
/// <see cref="Pitch"/>, exactly as upstream's <c>Pitch_interval</c> derives from
/// <c>Drul_array&lt;Pitch&gt;</c>.
/// <para>
/// The default state is INVERTED — octave 100 on the left against octave -100 on the
/// right — so that the first <see cref="AddPoint"/> always expands both sides, which is
/// how the Ambitus engraver learns its first note. Points are compared by SOUNDING
/// height (<see cref="Pitch.TonePitch"/>), not lexicographically; the lexicographic
/// variant is <see cref="PitchLexicographicInterval"/>.
/// </para>
/// <para>
/// Upstream inherits from <c>Drul_array</c>; the port HOLDS one instead, because the
/// port's <see cref="DrulArray{T}"/> is a struct and cannot be a base class. Same
/// state, same operations.
/// </para>
/// </summary>
public sealed class PitchInterval
{
    private DrulArray<Pitch> _bounds;

    /// <summary>Initializes the interval to the inverted, empty state.</summary>
    public PitchInterval()
    {
        _bounds[Direction.Negative] = new Pitch(100, 0, Rational.Zero);
        _bounds[Direction.Positive] = new Pitch(-100, 0, Rational.Zero);
    }

    /// <summary>Initializes the interval with explicit bounds.</summary>
    /// <param name="p1">The lower (left) bound.</param>
    /// <param name="p2">The upper (right) bound.</param>
    public PitchInterval(Pitch p1, Pitch p2)
    {
        _bounds[Direction.Negative] = p1;
        _bounds[Direction.Positive] = p2;
    }

    /// <summary>Gets the bound on one side.</summary>
    /// <param name="direction">Negative for the low bound, positive for the high one.</param>
    /// <returns>The pitch on that side.</returns>
    public Pitch this[Direction direction] => _bounds[direction];

    /// <summary>
    /// Gets a value indicating whether the interval holds no points yet: the left bound
    /// lies lexicographically above the right one.
    /// </summary>
    /// <returns><see langword="true"/> when no point has been added.</returns>
    public bool IsEmpty() => Pitch.Compare(_bounds.Negative, _bounds.Positive) > 0;

    /// <summary>
    /// Widens the interval to include a pitch, compared by sounding height.
    /// </summary>
    /// <param name="p">The pitch to include.</param>
    /// <returns>Which sides actually moved: down and/or up.</returns>
    public DrulArray<bool> AddPoint(Pitch p)
    {
        DrulArray<bool> expansions = default;
        if (_bounds.Negative.TonePitch() > p.TonePitch())
        {
            _bounds.Negative = p;
            expansions.Negative = true;
        }

        if (_bounds.Positive.TonePitch() < p.TonePitch())
        {
            _bounds.Positive = p;
            expansions.Positive = true;
        }

        return expansions;
    }
}

/// <summary>
/// A pair of pitches bounding a range, compared LEXICOGRAPHICALLY — octave, then note
/// name, then alteration — rather than by sounding height. Upstream's
/// <c>Pitch_lexicographic_interval</c>.
/// </summary>
public sealed class PitchLexicographicInterval
{
    private DrulArray<Pitch> _bounds;

    /// <summary>Initializes the interval to the inverted, empty state.</summary>
    public PitchLexicographicInterval()
    {
        _bounds[Direction.Negative] = new Pitch(100, 0, Rational.Zero);
        _bounds[Direction.Positive] = new Pitch(-100, 0, Rational.Zero);
    }

    /// <summary>Initializes the interval with explicit bounds.</summary>
    /// <param name="p1">The lower (left) bound.</param>
    /// <param name="p2">The upper (right) bound.</param>
    public PitchLexicographicInterval(Pitch p1, Pitch p2)
    {
        _bounds[Direction.Negative] = p1;
        _bounds[Direction.Positive] = p2;
    }

    /// <summary>Gets the bound on one side.</summary>
    /// <param name="direction">Negative for the low bound, positive for the high one.</param>
    /// <returns>The pitch on that side.</returns>
    public Pitch this[Direction direction] => _bounds[direction];

    /// <summary>
    /// Gets a value indicating whether the interval holds no points yet.
    /// </summary>
    /// <returns><see langword="true"/> when no point has been added.</returns>
    public bool IsEmpty() => Pitch.Compare(_bounds.Negative, _bounds.Positive) > 0;

    /// <summary>Widens the interval to include a pitch, compared lexicographically.</summary>
    /// <param name="p">The pitch to include.</param>
    /// <returns>Which sides actually moved: down and/or up.</returns>
    public DrulArray<bool> AddPoint(Pitch p)
    {
        DrulArray<bool> expansions = default;
        if (Pitch.Compare(_bounds.Negative, p) > 0)
        {
            _bounds.Negative = p;
            expansions.Negative = true;
        }

        if (Pitch.Compare(_bounds.Positive, p) < 0)
        {
            _bounds.Positive = p;
            expansions.Positive = true;
        }

        return expansions;
    }
}
