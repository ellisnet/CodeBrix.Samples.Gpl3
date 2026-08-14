/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2008--2026 Han-Wen Nienhuys <hanwen@lilypond.org>

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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/skyline-pair.cc, lily/include/skyline-pair.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The two outlines of an object: what it presents upward and what it presents
/// downward.
/// <para>
/// Almost everything the engine spaces vertically carries one of these rather than a
/// bare <see cref="Skyline"/>, because approach has to be answered on both sides.
/// </para>
/// </summary>
public sealed class SkylinePair
{
    private DrulArray<Skyline> _skylines;

    /// <summary>Initializes an empty pair.</summary>
    public SkylinePair()
    {
        _skylines = new DrulArray<Skyline>(new Skyline(Direction.Negative), new Skyline(Direction.Positive));
    }

    /// <summary>Initializes a pair covering a set of boxes.</summary>
    /// <param name="boxes">The boxes to cover.</param>
    /// <param name="horizonAxis">The axis the skylines run along.</param>
    public SkylinePair(IReadOnlyList<Box> boxes, Axis horizonAxis)
    {
        // TODO: The boxes sort equally for up & down,
        // so we can save ourselves one sort step.
        _skylines = new DrulArray<Skyline>(
            new Skyline(boxes, horizonAxis, Direction.Negative),
            new Skyline(boxes, horizonAxis, Direction.Positive));
    }

    /// <summary>Initializes a pair covering a set of line segments.</summary>
    /// <param name="segments">The segments to cover.</param>
    /// <param name="horizonAxis">The axis the skylines run along.</param>
    public SkylinePair(IReadOnlyList<DrulArray<Offset>> segments, Axis horizonAxis)
    {
        _skylines = new DrulArray<Skyline>(
            new Skyline(segments, horizonAxis, Direction.Negative),
            new Skyline(segments, horizonAxis, Direction.Positive));
    }

    /// <summary>Initializes a pair as the merge of several pairs.</summary>
    /// <param name="skyPairs">The pairs to merge.</param>
    public SkylinePair(IReadOnlyList<SkylinePair> skyPairs)
    {
        _skylines = new DrulArray<Skyline>(
            new Skyline(skyPairs, Direction.Negative),
            new Skyline(skyPairs, Direction.Positive));
    }

    /// <summary>Initializes a pair covering one box.</summary>
    /// <param name="box">The box to cover.</param>
    /// <param name="horizonAxis">The axis the skylines run along.</param>
    public SkylinePair(Box box, Axis horizonAxis)
    {
        _skylines = new DrulArray<Skyline>(
            new Skyline(box, horizonAxis, Direction.Negative),
            new Skyline(box, horizonAxis, Direction.Positive));
    }

    /// <summary>Initializes a pair from two ready-made skylines.</summary>
    /// <param name="down">The downward skyline.</param>
    /// <param name="up">The upward skyline.</param>
    public SkylinePair(Skyline down, Skyline up) => _skylines = new DrulArray<Skyline>(down, up);

    /// <summary>Gets or sets the skyline on one side.</summary>
    /// <param name="direction">The side to address.</param>
    /// <returns>That side's skyline.</returns>
    public Skyline this[Direction direction]
    {
        get => _skylines[direction];
        set => _skylines[direction] = value;
    }

    /// <summary>Gets the downward skyline.</summary>
    public Skyline Down => _skylines[Direction.Negative];

    /// <summary>Gets the upward skyline.</summary>
    public Skyline Up => _skylines[Direction.Positive];

    /// <summary>Gets a value indicating whether both skylines are empty.</summary>
    public bool IsEmpty => Up.IsEmpty && Down.IsEmpty;

    /// <summary>Returns the leftmost abscissa either skyline reaches.</summary>
    /// <returns>The left edge.</returns>
    public double Left() => Math.Min(Up.Left(), Down.Left());

    /// <summary>Returns the rightmost abscissa either skyline reaches.</summary>
    /// <returns>The right edge.</returns>
    public double Right() => Math.Max(Up.Right(), Down.Right());

    /// <summary>Moves both skylines outward from the baseline.</summary>
    /// <param name="amount">The distance to raise by.</param>
    public void Raise(double amount)
    {
        Up.Raise(amount);
        Down.Raise(amount);
    }

    /// <summary>Moves both skylines along the horizon axis.</summary>
    /// <param name="amount">The distance to shift by.</param>
    public void Shift(double amount)
    {
        Up.Shift(amount);
        Down.Shift(amount);
    }

    /// <summary>Widens both skylines horizontally.</summary>
    /// <param name="amount">The padding width. Zero is a no-op.</param>
    public void Pad(double amount)
    {
        if (amount == 0.0)
        {
            return;
        }

        _skylines[Direction.Positive] = _skylines[Direction.Positive].Padded(amount);
        _skylines[Direction.Negative] = _skylines[Direction.Negative].Padded(amount);
    }

    /// <summary>Merges another pair into this one, side by side.</summary>
    /// <param name="other">The pair to absorb.</param>
    public void Merge(SkylinePair other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        Up.Merge(other.Up);
        Down.Merge(other.Down);
    }

    /// <summary>
    /// Returns this pair in the form a grob property holds it: a Scheme CONS of the two
    /// skylines.
    /// <para>
    /// There is no skyline-pair object in Scheme. <c>ly:skyline-pair?</c> is defined in
    /// <c>scm/c++.scm</c> as "a pair whose car and cdr are both skylines", so a property
    /// holding anything else — including this class — fails its own type check and is
    /// rejected, and every Scheme reader that expects to <c>car</c> it breaks.
    /// </para>
    /// </summary>
    /// <returns>The Scheme representation.</returns>
    public object ToScheme() => new Pair(Down, Up);

    /// <summary>
    /// Reads a pair back out of its Scheme representation.
    /// <para>
    /// The two sides must face the right ways round — down/left first, up/right second —
    /// which upstream enforces by raising rather than by silently swapping them.
    /// </para>
    /// <para>
    /// THE RESULT IS A COPY, and that is load-bearing. Upstream's conversion ends
    /// <c>return Skyline_pair (*left, *right);</c> — it dereferences both smobs into a
    /// BY-VALUE <c>Skyline_pair</c>, so nothing a caller does to what it reads can reach
    /// the grob's stored skylines. <see cref="Skyline"/> is a CLASS here, so handing the
    /// stored instances straight back aliased them: every read-shift-measure site in the
    /// engine — side-position, axis-group skyline combination and outside-staff
    /// placement, alignment, horizontal spacing — translates what it reads into a common
    /// refpoint before measuring, and each one was permanently moving the grob's own
    /// skylines by that offset.
    /// </para>
    /// </summary>
    /// <param name="value">The Scheme value.</param>
    /// <returns>The pair, or <see langword="null"/> when the value is not one.</returns>
    public static SkylinePair FromScheme(object value)
    {
        if (!(value is Pair pair) || !(pair.Car is Skyline left) || !(pair.Cdr is Skyline right))
        {
            return null;
        }

        if (left.Sky != Direction.Negative)
        {
            throw new InvalidOperationException(
                "direction of first skyline in skyline pair must be DOWN/LEFT");
        }

        if (right.Sky != Direction.Positive)
        {
            throw new InvalidOperationException(
                "direction of second skyline in skyline pair must be UP/RIGHT");
        }

        //was previously: return new SkylinePair(left, right);
        return new SkylinePair(left.Copy(), right.Copy());
    }
}
