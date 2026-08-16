/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2020--2026 Han-Wen Nienhuys <hanwen@lilypond.org>

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
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/include/lazy-skyline-pair.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Which way round a closed contour is traced.
/// <para>
/// It decides which SIDE of a segment is inside the shape, and therefore which of the
/// two skylines the segment belongs to. PostScript and TrueType outlines disagree about
/// the convention, which is why the caller states it rather than the code assuming it.
/// </para>
/// </summary>
public enum Orientation
{
    /// <summary>Counter-clockwise, the PostScript/CFF convention.</summary>
    CounterClockwise = -1,

    /// <summary>Clockwise, the TrueType convention.</summary>
    Clockwise = 1,
}

/// <summary>
/// Collects line segments and turns them into a <see cref="SkylinePair"/> once, at the
/// end.
/// <para>
/// Building a skyline is a sort-and-sweep over all its buildings, so doing it per
/// segment would repeat that work for every stroke of a glyph. This accumulates the
/// segments instead and sweeps once — which is the whole reason it is called lazy.
/// </para>
/// <para>
/// Segments arrive in one of two kinds, and the distinction is the subtle part:
/// </para>
/// <list type="bullet">
/// <item>An ORIENTED contour segment (<see cref="AddContourSegment"/>) knows which side
/// of it is solid, so it joins ONE of the two skylines. Tracing a glyph's outline this
/// way is what lets the upper skyline follow the top of the shape and the lower one the
/// bottom, instead of both taking the bounding box.</item>
/// <item>A plain segment (<see cref="AddSegment(Transform, Offset, Offset)"/>) has no
/// inside, so it goes to BOTH.</item>
/// </list>
/// </summary>
public sealed class LazySkylinePair
{
    private readonly Axis _axis;
    private readonly List<DrulArray<Offset>> _todo = new List<DrulArray<Offset>>();
    private DrulArray<List<DrulArray<Offset>>> _perDirection
        = new DrulArray<List<DrulArray<Offset>>>(
            new List<DrulArray<Offset>>(), new List<DrulArray<Offset>>());

    private SkylinePair _skylines = new SkylinePair();

    /// <summary>Initializes an empty collector.</summary>
    /// <param name="axis">The horizon axis the skylines run along.</param>
    public LazySkylinePair(Axis axis) => _axis = axis;

    /// <summary>Gets the horizon axis.</summary>
    public Axis Axis => _axis;

    /// <summary>
    /// Gets a value indicating whether nothing has been added since the last merge.
    /// <para>
    /// Asked by the caller BEFORE <see cref="ToPair"/>, which is why it looks at the
    /// pending lists rather than at the merged skylines: an expression that contributed
    /// nothing is the signal to fall back to the stencil's extent box.
    /// </para>
    /// </summary>
    public bool IsEmpty
        => _todo.Count == 0
           && _perDirection[Direction.Positive].Count == 0
           && _perDirection[Direction.Negative].Count == 0;

    /// <summary>
    /// Gets how many segments have been added since the last merge, counting a plain
    /// segment once and a contour segment once.
    /// </summary>
    /// <remarks>
    /// Reads the same three lists <see cref="IsEmpty"/> does, and exists for the same
    /// kind of caller: the glyph-outline fence needs to know how many segments an
    /// outline was flattened into, which is upstream's <c>max (2, chord / 0.2)</c>
    /// answer and is not recoverable from the merged skyline, where the sweep has
    /// already collapsed the buildings. Internal because it is a measurement of the
    /// collector's state, not part of the skyline contract.
    /// </remarks>
    internal int PendingSegmentCount
        => _todo.Count
           + _perDirection[Direction.Positive].Count
           + _perDirection[Direction.Negative].Count;

    /// <summary>Adds a segment that is solid on neither side, so it joins both skylines.</summary>
    /// <param name="transform">The transform to place the points with.</param>
    /// <param name="first">One end.</param>
    /// <param name="second">The other end.</param>
    public void AddSegment(Transform transform, Offset first, Offset second)
        => _todo.Add(new DrulArray<Offset>(transform.Apply(first), transform.Apply(second)));

    /// <summary>
    /// Adds a segment of a closed contour, which is solid on one side only.
    /// </summary>
    /// <param name="transform">The transform to place the points with.</param>
    /// <param name="orientation">Which way round the contour is traced.</param>
    /// <param name="first">One end.</param>
    /// <param name="second">The other end.</param>
    public void AddContourSegment(
        Transform transform, Orientation orientation, Offset first, Offset second)
    {
        DrulArray<Offset> segment
            = new DrulArray<Offset>(transform.Apply(first), transform.Apply(second));

        // Which way the segment runs along the horizon axis, taken together with the
        // contour's handedness, says which side the shape is on.
        bool descending = segment[Direction.Negative][_axis] > segment[Direction.Positive][_axis];

        Direction side = descending == (orientation == Orientation.CounterClockwise)
            ? (_axis == Axis.X ? Direction.Positive : Direction.Negative)
            : (_axis == Axis.X ? Direction.Negative : Direction.Positive);

        _perDirection[side].Add(segment);
    }

    /// <summary>
    /// Adds a segment drawn with a pen of a given thickness.
    /// <para>
    /// The stroke is approximated by the segment displaced by the pen's radius to each
    /// side and lengthened by the same at both ends — which is the rectangle the pen
    /// sweeps, plus its round caps' bounding box.
    /// </para>
    /// </summary>
    /// <param name="transform">The transform to place the points with.</param>
    /// <param name="first">One end.</param>
    /// <param name="second">The other end.</param>
    /// <param name="thickness">The pen's diameter.</param>
    public void AddSegment(Transform transform, Offset first, Offset second, double thickness)
    {
        if (thickness == 0)
        {
            AddSegment(transform, first, second);
            return;
        }

        // The radius is measured AFTER transforming, so a scaled stencil's strokes
        // thicken with it.
        double radius = (transform.Apply(new Offset(thickness / 2, 0))
                         - transform.Apply(Offset.Zero)).Length;

        Offset widen = Offset.Zero.With(_axis, radius);
        Offset pad = Offset.Zero.With(Axes.Other(_axis), radius);

        Offset p1 = transform.Apply(first);
        Offset p2 = transform.Apply(second);
        if (p1[_axis] > p2[_axis])
        {
            (p1, p2) = (p2, p1);
        }

        p1 -= widen;
        p2 += widen;

        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            _perDirection[d].Add(
                new DrulArray<Offset>(p1 + (pad * d.Value), p2 + (pad * d.Value)));
        }
    }

    /// <summary>Adds a filled box, as its four sides traced clockwise.</summary>
    /// <param name="transform">The transform to place the corners with.</param>
    /// <param name="box">The box.</param>
    public void AddBox(Transform transform, Box box)
    {
        Offset[] corners =
        {
            new Offset(box[Axis.X][Direction.Negative], box[Axis.Y][Direction.Negative]),
            new Offset(box[Axis.X][Direction.Negative], box[Axis.Y][Direction.Positive]),
            new Offset(box[Axis.X][Direction.Positive], box[Axis.Y][Direction.Positive]),
            new Offset(box[Axis.X][Direction.Positive], box[Axis.Y][Direction.Negative]),
        };

        for (int i = 0; i < 4; i++)
        {
            AddContourSegment(
                transform, Orientation.Clockwise, corners[i], corners[(i + 1) % 4]);
        }
    }

    /// <summary>Sweeps everything collected so far into the skylines.</summary>
    public void Merge()
    {
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            List<DrulArray<Offset>> pending = _perDirection[d];
            if (_todo.Count == 0 && pending.Count == 0)
            {
                continue;
            }

            pending.AddRange(_todo);
            _skylines[d].Merge(new Skyline(pending, _axis, d));
            pending.Clear();
        }

        _todo.Clear();
    }

    /// <summary>Sweeps and returns the finished pair.</summary>
    /// <returns>The skyline pair.</returns>
    public SkylinePair ToPair()
    {
        Merge();
        return _skylines;
    }
}
