/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/box.cc, lily/include/box.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// An axis-aligned rectangle: one <see cref="Interval"/> per axis.
/// <para>
/// Every grob extent, every glyph bounding box and every skyline building starts life
/// as one of these. A default-constructed box is empty on both axes, matching
/// upstream, because <see cref="Interval"/> itself defaults to empty.
/// </para>
/// </summary>
public struct Box : IEquatable<Box>
{
    private Interval _x;
    private Interval _y;

    /// <summary>Initializes a box from its two extents.</summary>
    /// <param name="x">The horizontal extent.</param>
    /// <param name="y">The vertical extent.</param>
    public Box(Interval x, Interval y)
    {
        _x = x;
        _y = y;
    }

    /// <summary>Gets or sets the horizontal extent.</summary>
    public Interval X
    {
        get => _x;
        set => _x = value;
    }

    /// <summary>Gets or sets the vertical extent.</summary>
    public Interval Y
    {
        get => _y;
        set => _y = value;
    }

    /// <summary>Gets or sets the extent on one axis.</summary>
    /// <param name="axis">The axis to address.</param>
    /// <returns>That axis's extent.</returns>
    public Interval this[Axis axis]
    {
        get => axis == Axis.X ? _x : _y;
        set
        {
            if (axis == Axis.X)
            {
                _x = value;
            }
            else
            {
                _y = value;
            }
        }
    }

    /// <summary>Gets the enclosed area, which is zero when either extent is empty.</summary>
    public double Area => _x.Length * _y.Length;

    /// <summary>Gets the midpoint of both extents.</summary>
    public Offset Center => new Offset(_x.Center, _y.Center);

    /// <summary>
    /// Gets a value indicating whether the box is empty on BOTH axes. A box empty on
    /// only one axis reads as non-empty here, exactly as upstream's does.
    /// </summary>
    public bool IsEmpty => IsEmptyOn(Axis.X) && IsEmptyOn(Axis.Y);

    /// <summary>
    /// Determines whether one axis carries the empty sentinels.
    /// <para>
    /// This is a test for the sentinel VALUES, not for <see cref="Interval.IsEmpty"/>.
    /// Upstream compares against a default-constructed interval bound for bound, so an
    /// inverted-but-finite interval — left greater than right — is NOT empty by this
    /// test even though it is by <see cref="Interval.IsEmpty"/>. Skyline construction
    /// depends on the distinction.
    /// </para>
    /// </summary>
    /// <param name="axis">The axis to test.</param>
    /// <returns><see langword="true"/> when that axis holds the empty sentinels.</returns>
    public bool IsEmptyOn(Axis axis)
    {
        Interval extent = this[axis];
        return extent.Left == Interval.MaxSentinel && extent.Right == Interval.MinSentinel;
    }

    /// <summary>Empties both extents in place.</summary>
    public void SetEmpty()
    {
        _x.SetEmpty();
        _y.SetEmpty();
    }

    /// <summary>Moves the box. Empty axes are left alone, so they stay empty.</summary>
    /// <param name="offset">The distance to move by.</param>
    public void Translate(Offset offset)
    {
        if (!IsEmptyOn(Axis.X))
        {
            _x += offset.X;
        }

        if (!IsEmptyOn(Axis.Y))
        {
            _y += offset.Y;
        }
    }

    /// <summary>Grows the box to enclose another.</summary>
    /// <param name="other">The box to absorb.</param>
    public void Unite(Box other)
    {
        _x.Unite(other._x);
        _y.Unite(other._y);
    }

    /// <summary>Shrinks the box to the overlap with another.</summary>
    /// <param name="other">The box to intersect with.</param>
    public void Intersect(Box other)
    {
        _x.Intersect(other._x);
        _y.Intersect(other._y);
    }

    /// <summary>Grows the box to enclose a point.</summary>
    /// <param name="point">The point to include.</param>
    public void AddPoint(Offset point)
    {
        _x.AddPoint(point.X);
        _y.AddPoint(point.Y);
    }

    /// <summary>Expands the box on every side.</summary>
    /// <param name="x">The horizontal amount to add to each side.</param>
    /// <param name="y">The vertical amount to add to each side.</param>
    public void Widen(double x, double y)
    {
        _x.Widen(x);
        _y.Widen(y);
    }

    /// <summary>Scales both extents about the origin.</summary>
    /// <param name="factor">The scale factor.</param>
    public void Scale(double factor)
    {
        _x *= factor;
        _y *= factor;
    }

    /// <summary>Determines whether two boxes have equal extents.</summary>
    /// <param name="other">The box to compare with.</param>
    /// <returns><see langword="true"/> when both extents match.</returns>
    public bool Equals(Box other) => _x.Equals(other._x) && _y.Equals(other._y);

    /// <summary>Determines whether this equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal box.</returns>
    public override bool Equals(object obj) => obj is Box other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(_x, _y);

    /// <summary>Tests equality.</summary>
    /// <param name="left">The first box.</param>
    /// <param name="right">The second box.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(Box left, Box right) => left.Equals(right);

    /// <summary>Tests inequality.</summary>
    /// <param name="left">The first box.</param>
    /// <param name="right">The second box.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(Box left, Box right) => !left.Equals(right);

    /// <summary>Returns the external representation, in upstream's debug wording.</summary>
    /// <returns>The four bounds.</returns>
    public override string ToString()
        => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "X left {0:F4} right {1:F4} Y down {2:F4} up {3:F4}",
            _x.Left,
            _x.Right,
            _y.Left,
            _y.Right);
}
