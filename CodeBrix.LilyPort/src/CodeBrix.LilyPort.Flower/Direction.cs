/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Flower; //was previously: flower/include/direction.hh;
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - translated from C++17 to C# targeting net10.0
//   - final class replaced by a readonly struct; Direction is a value carrying one
//     int and is passed everywhere by value

/// <summary>
/// A direction: negative, zero (centre), or positive. LilyPond uses the same type for
/// up/down, left/right, and for the two sides of anything two-sided, which is why it is
/// deliberately not an enum with domain-specific names.
/// </summary>
/// <remarks>
/// <para>
/// The value is normalized to exactly -1, 0 or 1 on construction, so any non-zero input
/// collapses to its sign. Note that <c>Direction(double)</c> uses the sign bit, so
/// negative zero yields <see cref="Zero"/> (because -0.0 == 0.0) while a negative NaN
/// yields <see cref="Negative"/> — matching upstream's use of <c>std::signbit</c>.
/// </para>
/// <para>
/// PORTING HAZARD: C's <c>NAN</c> macro is POSITIVE-signed, but .NET's
/// <see cref="double.NaN"/> is NEGATIVE-signed (its bits are
/// <c>0xFFF8000000000000</c>, and <c>double.IsNegative(double.NaN)</c> is
/// <see langword="true"/>). So <c>Direction(NAN)</c> is positive in C and
/// <c>new Direction(double.NaN)</c> is negative here. Both follow the sign bit
/// correctly; only the language's default constant differs. Anywhere a NaN can reach a
/// sign-sensitive computation in this port, that difference matters.
/// </para>
/// </remarks>
public readonly struct Direction : IEquatable<Direction>, IComparable<Direction>
{
    private readonly int _value;

    /// <summary>Initializes a direction from an integer, collapsing it to its sign.</summary>
    /// <param name="value">Any integer; only its sign is kept.</param>
    public Direction(long value)
    {
        _value = value != 0 ? (value < 0 ? -1 : 1) : 0;
    }

    /// <summary>Initializes a direction from a real, using the sign bit.</summary>
    /// <param name="value">Any real; negative zero yields <see cref="Negative"/>.</param>
    public Direction(double value)
    {
        _value = value != 0.0 ? (double.IsNegative(value) ? -1 : 1) : 0;
    }

    /// <summary>Gets the negative direction — down, or left.</summary>
    public static Direction Negative => new Direction(-1L);

    /// <summary>Gets the positive direction — up, or right.</summary>
    public static Direction Positive => new Direction(1L);

    /// <summary>Gets the zero direction, also called centre.</summary>
    public static Direction Zero => new Direction(0L);

    /// <summary>Gets the centre direction, an alias for <see cref="Zero"/>.</summary>
    public static Direction Center => Zero;

    /// <summary>Gets the direction as -1, 0 or 1.</summary>
    public int Value => _value;

    /// <summary>Gets a value indicating whether the direction is non-zero.</summary>
    public bool IsNonZero => _value != 0;

    /// <summary>
    /// Gets the index this direction addresses in a two-element side array: 0 for
    /// negative, 1 for positive. Upstream's <c>to_index</c> adds one, giving 0..2.
    /// </summary>
    public int ToIndex => _value + 1;

    /// <summary>Converts a direction to its integer value.</summary>
    /// <param name="direction">The direction to convert.</param>
    public static implicit operator int(Direction direction) => direction._value;

    /// <summary>Reverses a direction.</summary>
    /// <param name="direction">The direction to reverse.</param>
    /// <returns>The opposite direction; zero stays zero.</returns>
    public static Direction operator -(Direction direction) => new Direction((long)(-direction._value));

    /// <summary>Returns the direction unchanged.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The same direction.</returns>
    public static Direction operator +(Direction direction) => direction;

    /// <summary>Multiplies two directions, giving the combined sign.</summary>
    /// <param name="left">The first direction.</param>
    /// <param name="right">The second direction.</param>
    /// <returns>The product's sign.</returns>
    public static Direction operator *(Direction left, Direction right)
        => new Direction((long)(left._value * right._value));

    /// <summary>Determines whether two directions point opposite ways.</summary>
    /// <param name="a">The first direction.</param>
    /// <param name="b">The second direction.</param>
    /// <returns><see langword="true"/> when the product is negative.</returns>
    public static bool DirectedOpposite(Direction a, Direction b) => (a * b)._value < 0;

    /// <summary>Determines whether two directions point the same way.</summary>
    /// <param name="a">The first direction.</param>
    /// <param name="b">The second direction.</param>
    /// <returns><see langword="true"/> when the product is positive.</returns>
    public static bool DirectedSame(Direction a, Direction b) => (a * b)._value > 0;

    /// <summary>
    /// Picks the maximum when the direction is positive and the minimum otherwise.
    /// LilyPond uses this constantly to write one piece of geometry code that serves
    /// both sides of a grob.
    /// </summary>
    /// <typeparam name="T">The comparable value type.</typeparam>
    /// <param name="direction">Positive selects the maximum; anything else the minimum.</param>
    /// <param name="a">The first candidate.</param>
    /// <param name="b">The second candidate.</param>
    /// <returns>The selected value.</returns>
    public static T MinMax<T>(Direction direction, T a, T b)
        where T : IComparable<T>
    {
        if (direction._value > 0)
        {
            return a.CompareTo(b) >= 0 ? a : b;
        }

        return a.CompareTo(b) <= 0 ? a : b;
    }

    /// <summary>Determines whether two directions are equal.</summary>
    /// <param name="other">The direction to compare with.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public bool Equals(Direction other) => _value == other._value;

    /// <summary>Determines whether this equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal direction.</returns>
    public override bool Equals(object obj) => obj is Direction other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => _value;

    /// <summary>Compares two directions by value.</summary>
    /// <param name="other">The direction to compare with.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public int CompareTo(Direction other) => _value.CompareTo(other._value);

    /// <summary>Tests equality.</summary>
    /// <param name="left">The first direction.</param>
    /// <param name="right">The second direction.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(Direction left, Direction right) => left._value == right._value;

    /// <summary>Tests inequality.</summary>
    /// <param name="left">The first direction.</param>
    /// <param name="right">The second direction.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(Direction left, Direction right) => left._value != right._value;

    /// <summary>Tests ordering.</summary>
    /// <param name="left">The first direction.</param>
    /// <param name="right">The second direction.</param>
    /// <returns><see langword="true"/> when the first is smaller.</returns>
    public static bool operator <(Direction left, Direction right) => left._value < right._value;

    /// <summary>Tests ordering.</summary>
    /// <param name="left">The first direction.</param>
    /// <param name="right">The second direction.</param>
    /// <returns><see langword="true"/> when the first is larger.</returns>
    public static bool operator >(Direction left, Direction right) => left._value > right._value;

    /// <summary>Tests ordering.</summary>
    /// <param name="left">The first direction.</param>
    /// <param name="right">The second direction.</param>
    /// <returns><see langword="true"/> when the first is not larger.</returns>
    public static bool operator <=(Direction left, Direction right) => left._value <= right._value;

    /// <summary>Tests ordering.</summary>
    /// <param name="left">The first direction.</param>
    /// <param name="right">The second direction.</param>
    /// <returns><see langword="true"/> when the first is not smaller.</returns>
    public static bool operator >=(Direction left, Direction right) => left._value >= right._value;

    /// <summary>Returns the external representation.</summary>
    /// <returns>The direction as <c>-1</c>, <c>0</c> or <c>1</c>.</returns>
    public override string ToString()
        => _value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A two-element array indexed by <see cref="Direction"/> — LilyPond's "Drul", for
/// down/up and left/right. Used for anything with two sides: the ends of a beam, the
/// edges of an interval, the sides of a stem.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public struct DrulArray<T> : IEquatable<DrulArray<T>>
{
    private T _negative;
    private T _positive;

    /// <summary>Initializes both sides.</summary>
    /// <param name="negative">The value on the negative side.</param>
    /// <param name="positive">The value on the positive side.</param>
    public DrulArray(T negative, T positive)
    {
        _negative = negative;
        _positive = positive;
    }

    /// <summary>Gets or sets the element on the given side.</summary>
    /// <param name="direction">
    /// The side to address. Upstream asserts the direction is non-zero, because a
    /// centre direction does not name a side.
    /// </param>
    /// <returns>The element on that side.</returns>
    public T this[Direction direction]
    {
        get => direction > Direction.Center ? _positive : _negative;
        set
        {
            if (direction > Direction.Center)
            {
                _positive = value;
            }
            else
            {
                _negative = value;
            }
        }
    }

    /// <summary>Gets or sets the element on the negative side.</summary>
    public T Negative
    {
        get => _negative;
        set => _negative = value;
    }

    /// <summary>Gets or sets the element on the positive side.</summary>
    public T Positive
    {
        get => _positive;
        set => _positive = value;
    }

    /// <summary>Exchanges the two sides in place.</summary>
    public void Swap() => (_negative, _positive) = (_positive, _negative);

    /// <summary>Determines whether two arrays hold equal elements.</summary>
    /// <param name="other">The array to compare with.</param>
    /// <returns><see langword="true"/> when both sides are equal.</returns>
    public bool Equals(DrulArray<T> other)
        => System.Collections.Generic.EqualityComparer<T>.Default.Equals(_negative, other._negative)
           && System.Collections.Generic.EqualityComparer<T>.Default.Equals(_positive, other._positive);

    /// <summary>Determines whether this equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal array.</returns>
    public override bool Equals(object obj) => obj is DrulArray<T> other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
        => HashCode.Combine(_negative, _positive);

    /// <summary>Tests equality.</summary>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(DrulArray<T> left, DrulArray<T> right) => left.Equals(right);

    /// <summary>Tests inequality.</summary>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(DrulArray<T> left, DrulArray<T> right) => !left.Equals(right);

    /// <summary>Returns the external representation.</summary>
    /// <returns>The two sides, negative first.</returns>
    public override string ToString() => "(" + _negative + ", " + _positive + ")";
}
