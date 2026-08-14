/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys

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

namespace CodeBrix.LilyPort.Engine.Music; //was previously: flower/include/interval.hh (the Interval_t<Moment> instantiation);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the THIRD concrete instantiation of the C++ template Interval_t<T>. Flower's
//     Interval.cs realises the other two, Interval (over Real) and Slice (over int),
//     and its own note explains why the template is not carried across generically:
//     the empty sentinels differ in KIND per element type. Moment's are the third
//     such pair.
//   - it lives in the Engine rather than in Flower because Moment is an Engine type,
//     and Flower may not depend on the Engine. Upstream has no such constraint: its
//     interval.hh is a header template that any translation unit instantiates.

/// <summary>
/// A closed interval over <see cref="Moment"/> — the span of score time something
/// occupies. Upstream writes this as <c>Interval_t&lt;Moment&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// As with Flower's <c>Interval</c>, the empty interval is an INVERTED pair, so that
/// <see cref="Unite"/> and <see cref="AddPoint"/> accumulate correctly from empty
/// without special-casing, and <see cref="IsEmpty"/> is simply <c>Left &gt; Right</c>.
/// </para>
/// <para>
/// The bounds sit behind an "assigned" flag for the same load-bearing reason Flower's
/// Interval documents: <c>default(MomentInterval)</c> and <c>new MomentInterval[n]</c>
/// both zero the fields and bypass every constructor, which would silently yield a
/// zero-length interval AT MOMENT ZERO instead of an empty one.
/// </para>
/// </remarks>
public struct MomentInterval : IEquatable<MomentInterval>
{
    private bool _assigned;
    private Moment _left;
    private Moment _right;

    /// <summary>Initializes an interval from its two ends.</summary>
    /// <param name="left">The earlier bound.</param>
    /// <param name="right">The later bound.</param>
    public MomentInterval(Moment left, Moment right)
    {
        _assigned = true;
        _left = left;
        _right = right;
    }

    /// <summary>Initializes a zero-length interval at a point.</summary>
    /// <param name="point">The single moment the interval covers.</param>
    public MomentInterval(Moment point)
        : this(point, point)
    {
    }

    /// <summary>Gets the sentinel an empty interval's left bound reads back as.</summary>
    public static Moment MaxSentinel => Moment.Infinity;

    /// <summary>Gets the sentinel an empty interval's right bound reads back as.</summary>
    public static Moment MinSentinel => -Moment.Infinity;

    /// <summary>Gets the empty interval.</summary>
    public static MomentInterval Empty => new MomentInterval(MaxSentinel, MinSentinel);

    /// <summary>Gets or sets the earlier bound.</summary>
    public Moment Left
    {
        get => _assigned ? _left : MaxSentinel;
        set
        {
            EnsureAssigned();
            _left = value;
        }
    }

    /// <summary>Gets or sets the later bound.</summary>
    public Moment Right
    {
        get => _assigned ? _right : MinSentinel;
        set
        {
            EnsureAssigned();
            _right = value;
        }
    }

    /// <summary>Gets a value indicating whether the interval covers nothing.</summary>
    public bool IsEmpty => Left > Right;

    /// <summary>Grows the interval to include a moment.</summary>
    /// <param name="point">The moment to cover.</param>
    public void AddPoint(Moment point)
    {
        Moment left = Left;
        Moment right = Right;
        Left = point < left ? point : left;
        Right = point > right ? point : right;
    }

    /// <summary>Grows the interval to cover another one as well.</summary>
    /// <param name="other">The interval to absorb.</param>
    public void Unite(MomentInterval other)
    {
        Moment left = Left;
        Moment right = Right;
        Left = other.Left < left ? other.Left : left;
        Right = other.Right > right ? other.Right : right;
    }

    /// <summary>Compares two intervals for equality.</summary>
    /// <param name="left">The first interval.</param>
    /// <param name="right">The second interval.</param>
    /// <returns><see langword="true"/> when both bounds agree.</returns>
    public static bool operator ==(MomentInterval left, MomentInterval right) => left.Equals(right);

    /// <summary>Compares two intervals for inequality.</summary>
    /// <param name="left">The first interval.</param>
    /// <param name="right">The second interval.</param>
    /// <returns><see langword="true"/> when either bound differs.</returns>
    public static bool operator !=(MomentInterval left, MomentInterval right) => !left.Equals(right);

    /// <summary>Compares this interval with another.</summary>
    /// <param name="other">The interval to compare against.</param>
    /// <returns><see langword="true"/> when both bounds agree.</returns>
    public bool Equals(MomentInterval other) => Left == other.Left && Right == other.Right;

    /// <summary>Compares this interval with another object.</summary>
    /// <param name="obj">The object to compare against.</param>
    /// <returns><see langword="true"/> when it is an equal interval.</returns>
    public override bool Equals(object obj) => obj is MomentInterval other && Equals(other);

    /// <summary>Returns a hash code for the interval.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Left, Right);

    /// <summary>Returns the external representation.</summary>
    /// <returns>The interval as <c>[left,right]</c>.</returns>
    public override string ToString() => "[" + Left + "," + Right + "]";

    private void EnsureAssigned()
    {
        if (!_assigned)
        {
            _assigned = true;
            _left = MaxSentinel;
            _right = MinSentinel;
        }
    }
}
