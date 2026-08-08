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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/moment.cc, lily/include/moment.hh;

// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port.

/// <summary>
/// Musical timing: a main part and a grace part, each a <see cref="Rational"/> number of
/// whole notes.
/// <para>
/// The grace part is what lets grace notes occupy no main-timing duration while still
/// ordering correctly against each other, which is why a moment is a pair rather than a
/// single rational.
/// </para>
/// </summary>
public readonly struct Moment : IEquatable<Moment>, IComparable<Moment>, ISchemeEqual
{
    /// <summary>Initializes a moment from a main and a grace part.</summary>
    /// <param name="mainPart">The main timing.</param>
    /// <param name="gracePart">The grace timing.</param>
    public Moment(Rational mainPart, Rational gracePart)
    {
        MainPart = mainPart;
        GracePart = gracePart;
    }

    /// <summary>Initializes a moment with no grace part.</summary>
    /// <param name="mainPart">The main timing.</param>
    public Moment(Rational mainPart)
        : this(mainPart, Rational.Zero)
    {
    }

    /// <summary>Initializes a moment from a whole number of whole notes.</summary>
    /// <param name="mainPart">The main timing.</param>
    public Moment(long mainPart)
        : this(new Rational(mainPart), Rational.Zero)
    {
    }

    /// <summary>Gets the main timing.</summary>
    public Rational MainPart { get; }

    /// <summary>Gets the grace timing.</summary>
    public Rational GracePart { get; }

    /// <summary>The zero moment.</summary>
    public static Moment Zero => new Moment(Rational.Zero, Rational.Zero);

    /// <summary>Positive infinity.</summary>
    public static Moment Infinity => new Moment(Rational.Infinity, Rational.Zero);

    /// <summary>Gets a value indicating whether either part is non-zero.</summary>
    public bool IsNonZero => MainPart.IsNonZero || GracePart.IsNonZero;

    /// <summary>Negates a moment.</summary>
    /// <param name="value">The moment to negate.</param>
    /// <returns>The negated moment.</returns>
    public static Moment operator -(Moment value) => new Moment(-value.MainPart, -value.GracePart);

    /// <summary>Adds two moments part by part.</summary>
    /// <param name="left">The first moment.</param>
    /// <param name="right">The second moment.</param>
    /// <returns>The sum.</returns>
    public static Moment operator +(Moment left, Moment right)
        => new Moment(left.MainPart + right.MainPart, left.GracePart + right.GracePart);

    /// <summary>Subtracts one moment from another part by part.</summary>
    /// <param name="left">The moment to subtract from.</param>
    /// <param name="right">The moment to subtract.</param>
    /// <returns>The difference.</returns>
    public static Moment operator -(Moment left, Moment right)
        => new Moment(left.MainPart - right.MainPart, left.GracePart - right.GracePart);

    /// <summary>Scales both parts of a moment.</summary>
    /// <param name="left">The moment to scale.</param>
    /// <param name="right">The factor.</param>
    /// <returns>The scaled moment.</returns>
    public static Moment operator *(Moment left, Rational right)
        => new Moment(left.MainPart * right, left.GracePart * right);

    /// <summary>Divides both parts of a moment.</summary>
    /// <param name="left">The moment to divide.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The divided moment.</returns>
    public static Moment operator /(Moment left, Rational right)
        => new Moment(left.MainPart / right, left.GracePart / right);

    /// <summary>Compares two moments, main part first.</summary>
    /// <param name="left">The first moment.</param>
    /// <param name="right">The second moment.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public static int Compare(Moment left, Moment right)
    {
        int main = Rational.Compare(left.MainPart, right.MainPart);
        return main != 0 ? main : Rational.Compare(left.GracePart, right.GracePart);
    }

    /// <summary>Determines whether one moment is less than another.</summary>
    /// <param name="left">The first moment.</param>
    /// <param name="right">The second moment.</param>
    /// <returns><see langword="true"/> when the first is smaller.</returns>
    public static bool operator <(Moment left, Moment right) => Compare(left, right) < 0;

    /// <summary>Determines whether one moment is greater than another.</summary>
    /// <param name="left">The first moment.</param>
    /// <param name="right">The second moment.</param>
    /// <returns><see langword="true"/> when the first is larger.</returns>
    public static bool operator >(Moment left, Moment right) => Compare(left, right) > 0;

    /// <summary>Determines whether one moment is less than or equal to another.</summary>
    /// <param name="left">The first moment.</param>
    /// <param name="right">The second moment.</param>
    /// <returns><see langword="true"/> when the first is not larger.</returns>
    public static bool operator <=(Moment left, Moment right) => Compare(left, right) <= 0;

    /// <summary>Determines whether one moment is greater than or equal to another.</summary>
    /// <param name="left">The first moment.</param>
    /// <param name="right">The second moment.</param>
    /// <returns><see langword="true"/> when the first is not smaller.</returns>
    public static bool operator >=(Moment left, Moment right) => Compare(left, right) >= 0;

    /// <summary>Determines whether two moments are equal.</summary>
    /// <param name="left">The first moment.</param>
    /// <param name="right">The second moment.</param>
    /// <returns><see langword="true"/> when both parts match.</returns>
    public static bool operator ==(Moment left, Moment right) => Compare(left, right) == 0;

    /// <summary>Determines whether two moments differ.</summary>
    /// <param name="left">The first moment.</param>
    /// <param name="right">The second moment.</param>
    /// <returns><see langword="true"/> when either part differs.</returns>
    public static bool operator !=(Moment left, Moment right) => Compare(left, right) != 0;

    /// <summary>Compares this moment with another.</summary>
    /// <param name="other">The moment to compare with.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public int CompareTo(Moment other) => Compare(this, other);

    /// <summary>Determines whether this moment equals another.</summary>
    /// <param name="other">The moment to compare with.</param>
    /// <returns><see langword="true"/> when both parts match.</returns>
    public bool Equals(Moment other) => Compare(this, other) == 0;

    /// <summary>Compares this moment with another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal moment.</returns>
    public override bool Equals(object obj) => obj is Moment other && Equals(other);

    /// <summary>Returns a hash code consistent with <see cref="Equals(Moment)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(MainPart, GracePart);

    /// <summary>Returns LilyPond's textual form, for example <c>1/4</c> or <c>1/4G1/8</c>.</summary>
    /// <returns>The moment as text.</returns>
    public override string ToString()
        => GracePart.IsNonZero
            ? MainPart.ToString() + "G" + GracePart
            : MainPart.ToString();

    /// <summary>
    /// Compares by VALUE for Scheme's <c>equal?</c>.
    /// <para>Upstream: <c>Moment::equal_p</c>, the smob equality handler
    /// <c>scm_equal_p</c> dispatches to. Without it two distinct objects holding the
    /// same value answer <c>#f</c>, which is identity, not equality.</para>
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the two are equal by value.</returns>
    public bool SchemeEquals(object other) => Equals(other);

}
