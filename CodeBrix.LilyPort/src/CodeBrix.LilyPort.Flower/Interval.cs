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
using System.Globalization;

namespace CodeBrix.LilyPort.Flower; //was previously: flower/include/interval.hh, flower/include/interval.tcc, flower/interval.cc;
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - translated from C++17 to C# targeting net10.0
//   - the C++ template Interval_t<T> is realised as two concrete types, Interval
//     (over Real/double) and Slice (over int), which are the instantiations
//     LilyPond actually uses. A generic version would have to carry the
//     Interval_traits<T> min/max sentinels through a type parameter for no gain --
//     the sentinels differ in kind (infinity for double, int.MaxValue for int).

/// <summary>
/// A closed interval over reals, used throughout LilyPond for extents, and the type the
/// whole layout engine is built on.
/// </summary>
/// <remarks>
/// <para>
/// The empty interval is represented by an INVERTED pair — left is +infinity and right
/// is -infinity — so that <c>Unite</c> and <c>AddPoint</c> work correctly starting from
/// empty without any special-casing. That is why the default value is empty rather than
/// the zero-length interval at the origin, and why <see cref="IsEmpty"/> is simply
/// <c>Left &gt; Right</c>.
/// </para>
/// <para>
/// Note that <see cref="Center"/> deliberately does NOT assert on empty, unlike the
/// generic C++ template. Upstream specialises it for Real precisely so that a Real
/// result can carry infinity or NaN instead of crashing.
/// </para>
/// </remarks>
public struct Interval : IEquatable<Interval>
{
    // The bounds are stored behind an "assigned" flag, and this is load-bearing.
    //
    // Upstream's default-constructed Interval_t is EMPTY: left = +infinity,
    // right = -infinity. That is what lets Unite and AddPoint accumulate correctly
    // starting from a fresh interval. C# cannot reproduce it directly, because
    // `default(Interval)` and `new Interval[n]` both zero the fields and bypass any
    // constructor -- which would silently yield a zero-length interval AT THE ORIGIN
    // instead of an empty one, and quietly corrupt every extent computed that way.
    //
    // A bool defaults to false, so an unassigned interval reads back as the empty
    // sentinels while any explicit construction or assignment sets the flag.
    private bool _assigned;
    private double _left;
    private double _right;

    /// <summary>Initializes an interval from its two ends.</summary>
    /// <param name="left">The lower bound.</param>
    /// <param name="right">The upper bound.</param>
    public Interval(double left, double right)
    {
        _assigned = true;
        _left = left;
        _right = right;
    }

    /// <summary>Initializes a zero-length interval at a point.</summary>
    /// <param name="point">The single point the interval covers.</param>
    public Interval(double point)
        : this(point, point)
    {
    }

    /// <summary>Gets or sets the lower bound.</summary>
    public double Left
    {
        get => _assigned ? _left : MaxSentinel;
        set
        {
            EnsureAssigned();
            _left = value;
        }
    }

    /// <summary>Gets or sets the upper bound.</summary>
    public double Right
    {
        get => _assigned ? _right : MinSentinel;
        set
        {
            EnsureAssigned();
            _right = value;
        }
    }

    private void EnsureAssigned()
    {
        if (!_assigned)
        {
            _assigned = true;
            _left = MaxSentinel;
            _right = MinSentinel;
        }
    }

    /// <summary>Gets the sentinel used for an empty interval's lower bound.</summary>
    public static double MaxSentinel => double.PositiveInfinity;

    /// <summary>Gets the sentinel used for an empty interval's upper bound.</summary>
    public static double MinSentinel => double.NegativeInfinity;

    /// <summary>
    /// Gets the empty interval. Note this is the DEFAULT-constructed value: left is
    /// +infinity, right is -infinity.
    /// </summary>
    public static Interval Empty => new Interval(MaxSentinel, MinSentinel);

    /// <summary>Gets the interval covering everything.</summary>
    public static Interval Longest => new Interval(MinSentinel, MaxSentinel);

    /// <summary>Gets a value indicating whether the interval is empty.</summary>
    public bool IsEmpty => Left > Right;

    /// <summary>Gets the interval's length, or zero when empty.</summary>
    public double Length => !IsEmpty ? Right - Left : 0.0;

    /// <summary>
    /// Gets the midpoint. Does not throw on an empty interval — a Real can carry
    /// infinity or NaN, and upstream relies on that.
    /// </summary>
    public double Center => ((Left + Right) / 2.0);

    /// <summary>Gets the element on the given side.</summary>
    /// <param name="direction">Negative selects the left end, positive the right.</param>
    /// <returns>The bound on that side.</returns>
    public double this[Direction direction]
    {
        get => direction > Direction.Center ? Right : Left;
        set
        {
            if (direction > Direction.Center)
            {
                Right = value;
            }
            else
            {
                Left = value;
            }
        }
    }

    /// <summary>Empties the interval in place.</summary>
    public void SetEmpty()
    {
        Left = MaxSentinel;
        Right = MinSentinel;
    }

    /// <summary>Makes the interval cover everything, in place.</summary>
    public void SetFull()
    {
        Left = MinSentinel;
        Right = MaxSentinel;
    }

    /// <summary>Extends this interval to cover another.</summary>
    /// <param name="other">The interval to absorb.</param>
    public void Unite(Interval other)
    {
        Left = Math.Min(other.Left, Left);
        Right = Math.Max(other.Right, Right);
    }

    /// <summary>Narrows this interval to the overlap with another.</summary>
    /// <param name="other">The interval to intersect with.</param>
    public void Intersect(Interval other)
    {
        Left = Math.Max(other.Left, Left);
        Right = Math.Min(other.Right, Right);
    }

    /// <summary>Extends the interval to include a point.</summary>
    /// <param name="point">The point to include.</param>
    public void AddPoint(double point)
    {
        Left = Math.Min(Left, point);
        Right = Math.Max(Right, point);
    }

    /// <summary>Grows the interval by the given amount on both ends.</summary>
    /// <param name="amount">The amount to widen by.</param>
    public void Widen(double amount)
    {
        Left -= amount;
        Right += amount;
    }

    /// <summary>Shifts the interval.</summary>
    /// <param name="amount">The distance to move by.</param>
    public void Translate(double amount)
    {
        Left += amount;
        Right += amount;
    }

    /// <summary>Reflects the interval about the origin.</summary>
    public void Negate()
    {
        double newLeft = -Right;
        double newRight = -Left;
        Left = newLeft;
        Right = newRight;
    }

    /// <summary>Exchanges the two ends.</summary>
    public void Swap() => (Left, Right) = (Right, Left);

    /// <summary>Determines whether the interval contains a point.</summary>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> when the point lies within, inclusive.</returns>
    public bool Contains(double point) => point >= Left && point <= Right;

    /// <summary>Clamps a value into the interval, leaving it alone when the interval is empty.</summary>
    /// <param name="value">The value to clamp.</param>
    /// <returns>The clamped value.</returns>
    public double Clamp(double value)
    {
        if (!IsEmpty)
        {
            if (value < Left)
            {
                return Left;
            }

            if (value > Right)
            {
                return Right;
            }
        }

        return value;
    }

    /// <summary>Returns the distance from a point to the interval, or zero when inside.</summary>
    /// <param name="point">The point to measure from.</param>
    /// <returns>The gap, or zero.</returns>
    public double Distance(double point)
    {
        if (point > Right)
        {
            return point - Right;
        }

        if (point < Left)
        {
            return Left - point;
        }

        return 0.0;
    }

    /// <summary>
    /// Interpolates across the interval: -1 gives the left end, +1 the right, 0 the
    /// centre. LilyPond uses this to express alignment as a single number.
    /// </summary>
    /// <param name="x">The position, conventionally in -1..1 but not clamped.</param>
    /// <returns>The interpolated value.</returns>
    public double LinearCombination(double x)
        => (((1.0 - x) * Left) + ((x + 1.0) * Right)) * 0.5;

    /// <summary>The inverse of <see cref="LinearCombination"/>.</summary>
    /// <param name="value">A value on the interval's axis.</param>
    /// <returns>-1 at the left end, +1 at the right.</returns>
    public double InverseLinearCombination(double value)
        => (value - Center) / (Length * 0.5);

    /// <summary>
    /// Unites with another interval, first translating it so it does not overlap and
    /// sits on the given side with at least <paramref name="padding"/> between.
    /// </summary>
    /// <param name="other">The interval to absorb.</param>
    /// <param name="padding">The minimum gap to leave.</param>
    /// <param name="direction">The side <paramref name="other"/> should end up on.</param>
    public void UniteDisjoint(Interval other, double padding, Direction direction)
    {
        double translation = direction * (this[direction] + (direction * padding) - other[-direction]);
        if (translation > 0.0)
        {
            other.Translate(translation);
        }

        Unite(other);
    }

    /// <summary>Returns the union with a disjoint interval, without mutating this one.</summary>
    /// <param name="other">The interval to absorb.</param>
    /// <param name="padding">The minimum gap to leave.</param>
    /// <param name="direction">The side <paramref name="other"/> should end up on.</param>
    /// <returns>The united interval.</returns>
    public Interval UnionDisjoint(Interval other, double padding, Direction direction)
    {
        Interval result = this;
        result.UniteDisjoint(other, padding, direction);
        return result;
    }

    /// <summary>Returns the intersection of two intervals.</summary>
    /// <param name="a">The first interval.</param>
    /// <param name="b">The second interval.</param>
    /// <returns>The overlap, which may be empty.</returns>
    public static Interval Intersection(Interval a, Interval b)
    {
        a.Intersect(b);
        return a;
    }

    /// <summary>Orders intervals by their left bound.</summary>
    /// <param name="a">The first interval.</param>
    /// <param name="b">The second interval.</param>
    /// <returns><see langword="true"/> when the first starts earlier.</returns>
    public static bool LeftLess(Interval a, Interval b) => a.Left < b.Left;

    /// <summary>Shifts an interval.</summary>
    /// <param name="interval">The interval to shift.</param>
    /// <param name="amount">The distance to move by.</param>
    /// <returns>The shifted interval.</returns>
    public static Interval operator +(Interval interval, double amount)
        => new Interval(interval.Left + amount, interval.Right + amount);

    /// <summary>Shifts an interval negatively.</summary>
    /// <param name="interval">The interval to shift.</param>
    /// <param name="amount">The distance to move by.</param>
    /// <returns>The shifted interval.</returns>
    public static Interval operator -(Interval interval, double amount)
        => new Interval(interval.Left - amount, interval.Right - amount);

    /// <summary>
    /// Scales an interval. A negative factor swaps the ends, so the result stays
    /// properly ordered.
    /// </summary>
    /// <param name="interval">The interval to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled interval.</returns>
    public static Interval operator *(Interval interval, double factor)
    {
        if (interval.IsEmpty)
        {
            return interval;
        }

        Interval result = new Interval(interval.Left * factor, interval.Right * factor);
        if (factor < 0.0)
        {
            result.Swap();
        }

        return result;
    }

    /// <summary>
    /// Compares two intervals by containment, as upstream's <c>Interval__compare</c>
    /// does: 0 when identical, 1 when the first contains the second, -1 when contained.
    /// </summary>
    /// <param name="a">The first interval.</param>
    /// <param name="b">The second interval.</param>
    /// <returns>0, 1, or -1.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when neither interval contains the other. Upstream asserts here; the
    /// relation is a partial order and the caller is expected to know that.
    /// </exception>
    public static int Compare(Interval a, Interval b)
    {
        if (a.Left == b.Left && a.Right == b.Right)
        {
            return 0;
        }

        if (a.Left <= b.Left && a.Right >= b.Right)
        {
            return 1;
        }

        if (a.Left >= b.Left && a.Right <= b.Right)
        {
            return -1;
        }

        throw new InvalidOperationException("Intervals are not comparable by containment.");
    }

    /// <summary>Determines whether two intervals have the same bounds.</summary>
    /// <param name="other">The interval to compare with.</param>
    /// <returns><see langword="true"/> when both bounds match.</returns>
    public bool Equals(Interval other)
        => Left.Equals(other.Left) && Right.Equals(other.Right);

    /// <summary>Determines whether this equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal interval.</returns>
    public override bool Equals(object obj) => obj is Interval other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Left, Right);

    /// <summary>Tests equality.</summary>
    /// <param name="left">The first interval.</param>
    /// <param name="right">The second interval.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(Interval left, Interval right) => left.Equals(right);

    /// <summary>Tests inequality.</summary>
    /// <param name="left">The first interval.</param>
    /// <param name="right">The second interval.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(Interval left, Interval right) => !left.Equals(right);

    /// <summary>Returns the external representation.</summary>
    /// <returns><c>[empty]</c> or <c>[left,right]</c>, matching upstream.</returns>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return "[empty]";
        }

        return "[" + Left.ToString(CultureInfo.InvariantCulture)
               + "," + Right.ToString(CultureInfo.InvariantCulture) + "]";
    }
}

/// <summary>
/// A closed interval over integers — upstream calls this <c>Slice</c>, and its own
/// header calls that a "weird name". Used for index ranges.
/// </summary>
/// <remarks>
/// The empty sentinel differs from <see cref="Interval"/>: integers have no infinity,
/// so upstream uses <c>+int.MaxValue</c> and <c>-int.MaxValue</c>. Note the minimum is
/// the NEGATED maximum, not <c>int.MinValue</c>, which keeps negation symmetric.
/// </remarks>
public struct Slice : IEquatable<Slice>
{
    /// <summary>Initializes a slice from its two ends.</summary>
    /// <param name="left">The lower bound.</param>
    /// <param name="right">The upper bound.</param>
    public Slice(int left, int right)
    {
        Left = left;
        Right = right;
    }

    /// <summary>Gets or sets the lower bound.</summary>
    public int Left { get; set; }

    /// <summary>Gets or sets the upper bound.</summary>
    public int Right { get; set; }

    /// <summary>Gets the sentinel used for an empty slice's lower bound.</summary>
    public static int MaxSentinel => int.MaxValue;

    /// <summary>Gets the sentinel used for an empty slice's upper bound.</summary>
    public static int MinSentinel => -int.MaxValue;

    /// <summary>Gets the empty slice.</summary>
    public static Slice Empty => new Slice(MaxSentinel, MinSentinel);

    /// <summary>Gets the slice covering everything.</summary>
    public static Slice Longest => new Slice(MinSentinel, MaxSentinel);

    /// <summary>Gets a value indicating whether the slice is empty.</summary>
    public bool IsEmpty => Left > Right;

    /// <summary>Gets the slice's length, or zero when empty.</summary>
    public int Length => !IsEmpty ? Right - Left : 0;

    /// <summary>Gets the element on the given side.</summary>
    /// <param name="direction">Negative selects the left end, positive the right.</param>
    /// <returns>The bound on that side.</returns>
    public int this[Direction direction]
    {
        get => direction > Direction.Center ? Right : Left;
        set
        {
            if (direction > Direction.Center)
            {
                Right = value;
            }
            else
            {
                Left = value;
            }
        }
    }

    /// <summary>Extends this slice to cover another.</summary>
    /// <param name="other">The slice to absorb.</param>
    public void Unite(Slice other)
    {
        Left = Math.Min(other.Left, Left);
        Right = Math.Max(other.Right, Right);
    }

    /// <summary>Narrows this slice to the overlap with another.</summary>
    /// <param name="other">The slice to intersect with.</param>
    public void Intersect(Slice other)
    {
        Left = Math.Max(other.Left, Left);
        Right = Math.Min(other.Right, Right);
    }

    /// <summary>Extends the slice to include a point.</summary>
    /// <param name="point">The point to include.</param>
    public void AddPoint(int point)
    {
        Left = Math.Min(Left, point);
        Right = Math.Max(Right, point);
    }

    /// <summary>Determines whether the slice contains a point.</summary>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> when the point lies within, inclusive.</returns>
    public bool Contains(int point) => point >= Left && point <= Right;

    /// <summary>Shifts the slice.</summary>
    /// <param name="amount">The distance to move by.</param>
    public void Translate(int amount)
    {
        Left += amount;
        Right += amount;
    }

    /// <summary>Exchanges the two ends.</summary>
    public void Swap() => (Left, Right) = (Right, Left);

    /// <summary>Determines whether two slices have the same bounds.</summary>
    /// <param name="other">The slice to compare with.</param>
    /// <returns><see langword="true"/> when both bounds match.</returns>
    public bool Equals(Slice other) => Left == other.Left && Right == other.Right;

    /// <summary>Determines whether this equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal slice.</returns>
    public override bool Equals(object obj) => obj is Slice other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Left, Right);

    /// <summary>Tests equality.</summary>
    /// <param name="left">The first slice.</param>
    /// <param name="right">The second slice.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(Slice left, Slice right) => left.Equals(right);

    /// <summary>Tests inequality.</summary>
    /// <param name="left">The first slice.</param>
    /// <param name="right">The second slice.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(Slice left, Slice right) => !left.Equals(right);

    /// <summary>Returns the external representation.</summary>
    /// <returns><c>[empty]</c> or <c>[left,right]</c>.</returns>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return "[empty]";
        }

        return "[" + Left.ToString(CultureInfo.InvariantCulture)
               + "," + Right.ToString(CultureInfo.InvariantCulture) + "]";
    }
}
