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
using System.Globalization;
using System.Numerics;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/duration.cc, lily/include/duration.hh;

// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port.

/// <summary>
/// A musical duration: a base duration expressed as a power of two, a number of
/// augmentation dots, and a scale factor.
/// </summary>
public readonly struct Duration : IEquatable<Duration>, IComparable<Duration>, ISchemeEqual
{
    private readonly int _durationLog;
    private readonly int _dotCount;
    private readonly Rational _factor;

    /// <summary>Initializes a duration from its log and dot count, unscaled.</summary>
    /// <param name="durationLog">The logarithm of the base duration.</param>
    /// <param name="dotCount">The number of augmentation dots.</param>
    public Duration(int durationLog, int dotCount)
    {
        _durationLog = durationLog;
        _dotCount = dotCount;
        _factor = Rational.One;
    }

    private Duration(int durationLog, int dotCount, Rational factor)
    {
        _durationLog = durationLog;
        _dotCount = dotCount;
        _factor = factor;
    }

    /// <summary>Gets the logarithm of the base duration; 2 is a quarter note.</summary>
    public int DurationLog => _durationLog;

    /// <summary>Gets the number of augmentation dots.</summary>
    public int DotCount => _dotCount;

    /// <summary>
    /// Gets the scale factor. Note that <c>default(Duration)</c> yields a zero factor,
    /// which is a degenerate duration; use <see cref="WholeNote"/> for a whole note.
    /// </summary>
    public Rational Factor => _factor;

    /// <summary>A whole note with no dots and no scaling.</summary>
    public static Duration WholeNote => new Duration(0, 0);

    /// <summary>
    /// Builds the duration equivalent to a number of whole notes.
    /// <para>
    /// The search finds the integer k for which 2q/p &gt; 2^k &gt;= q/p, then reads the
    /// dots off the run of consecutive one bits that follows. LilyPond only writes
    /// durations down to 64th notes, so a k above 6 collapses to a 64th note plus a
    /// scale factor rather than an unwritable duration.
    /// </para>
    /// </summary>
    /// <param name="wholeNotes">The length in whole notes.</param>
    /// <param name="scale">Whether to record the remainder as a scale factor.</param>
    /// <returns>The duration.</returns>
    public static Duration FromWholeNotes(Rational wholeNotes, bool scale)
    {
        Rational factor;
        Rational value = wholeNotes;
        if (!value.IsNegative)
        {
            factor = Rational.One;
        }
        else
        {
            factor = -Rational.One;
            value = -value;
        }

        if (!value.IsFinite || !value.IsNonZero)
        {
            return new Duration(0, 0, factor * value);
        }

        BigInteger numerator = value.Numerator;
        BigInteger denominator = value.Denominator;
        int k = IntegerLog2(denominator) - IntegerLog2(numerator);
        if (ShiftLeft(numerator, k) < denominator)
        {
            k++;
        }

        // If log(p/q) were written out in base 2, k is the position of the first non-zero
        // bit -- the duration log -- and the run of ones after it is the dot count.
        // Shift whichever side keeps every digit.
        BigInteger p = numerator;
        BigInteger q = denominator;
        if (k >= 0)
        {
            p <<= k;
        }
        else
        {
            q <<= -k;
        }

        p -= q;
        int dots = 0;
        while ((p *= 2) >= q)
        {
            p -= q;
            dots++;
        }

        int durationLog;

        // We only go up to 64th notes.
        if (k > 6)
        {
            durationLog = 6;
            dots = 0;
        }
        else
        {
            durationLog = k;
        }

        Duration built = new Duration(durationLog, dots, factor);
        if (scale || k > 6)
        {
            factor = value / built.ToWholeNotes();
            built = new Duration(durationLog, dots, factor);
        }

        return built;
    }

    /// <summary>Returns this duration scaled by a factor.</summary>
    /// <param name="factor">The factor to apply.</param>
    /// <returns>The compressed duration.</returns>
    public Duration Compressed(Rational factor)
        => new Duration(_durationLog, _dotCount, _factor * factor);

    /// <summary>Converts the duration to the equivalent number of whole notes.</summary>
    /// <returns>The length in whole notes.</returns>
    public Rational ToWholeNotes()
    {
        Rational length = new Rational(1L << Math.Abs(_durationLog));
        if (_durationLog > 0)
        {
            length = Rational.One / length;
        }

        Rational delta = length;
        for (int i = 0; i < _dotCount; i++)
        {
            delta /= new Rational(2);
            length += delta;
        }

        return length * _factor;
    }

    /// <summary>Compares two durations by their length in whole notes, then by dots.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public static int Compare(Duration left, Duration right)
        => Rational.Compare(left.ToWholeNotes(), right.ToWholeNotes());

    /// <summary>Compares this duration with another.</summary>
    /// <param name="other">The duration to compare with.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public int CompareTo(Duration other) => Compare(this, other);

    /// <summary>
    /// Determines whether two durations are equal. Upstream compares the log, the dot
    /// count and the factor rather than the resulting length, so two spellings of the
    /// same length are NOT equal; this follows it.
    /// </summary>
    /// <param name="other">The duration to compare with.</param>
    /// <returns><see langword="true"/> when the spellings match.</returns>
    public bool Equals(Duration other)
        => _dotCount == other._dotCount
           && _durationLog == other._durationLog
           && _factor == other._factor;

    /// <summary>Compares this duration with another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal duration.</returns>
    public override bool Equals(object obj) => obj is Duration other && Equals(other);

    /// <summary>Returns a hash code consistent with <see cref="Equals(Duration)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(_durationLog, _dotCount, _factor);

    /// <summary>Determines whether two durations have the same spelling.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the spellings match.</returns>
    public static bool operator ==(Duration left, Duration right) => left.Equals(right);

    /// <summary>Determines whether two durations differ in spelling.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the spellings differ.</returns>
    public static bool operator !=(Duration left, Duration right) => !left.Equals(right);

    /// <summary>Determines whether one duration is shorter than another.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the first is shorter.</returns>
    public static bool operator <(Duration left, Duration right) => Compare(left, right) < 0;

    /// <summary>Determines whether one duration is longer than another.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> when the first is longer.</returns>
    public static bool operator >(Duration left, Duration right) => Compare(left, right) > 0;

    /// <summary>Returns LilyPond's textual form, for example <c>4.</c> or <c>8*2/3</c>.</summary>
    /// <returns>The duration as text.</returns>
    public override string ToString()
    {
        string text = _durationLog < 0
            ? "log = " + _durationLog.ToString(CultureInfo.InvariantCulture)
            : (1 << _durationLog).ToString(CultureInfo.InvariantCulture);

        if (_dotCount > 0)
        {
            text += new string('.', _dotCount);
        }

        if (_factor != Rational.One)
        {
            text += "*" + _factor;
        }

        return text;
    }

    // Upstream's intlog2 asserts on a non-positive argument; BigInteger.Log2 has the
    // same domain, so the guard is kept rather than silently returning zero.
    private static int IntegerLog2(BigInteger value)
    {
        if (value <= BigInteger.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "intlog2 requires a positive value.");
        }

        return (int)BigInteger.Log2(value);
    }

    private static BigInteger ShiftLeft(BigInteger value, int amount)
        => amount >= 0 ? value << amount : value >> -amount;

    /// <summary>
    /// Compares by VALUE for Scheme's <c>equal?</c>.
    /// <para>Upstream: <c>Duration::equal_p</c>, the smob equality handler
    /// <c>scm_equal_p</c> dispatches to. Without it two distinct objects holding the
    /// same value answer <c>#f</c>, which is identity, not equality.</para>
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the two are equal by value.</returns>
    public bool SchemeEquals(object other) => Equals(other);

}
