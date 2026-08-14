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

namespace CodeBrix.LilyPort.Flower; //was previously: flower/rational.cc, flower/include/rational.hh;
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - translated from C++17 to C# targeting net10.0
//   - class replaced by a readonly struct, since Rational is a value type with
//     value semantics and is copied constantly through the layout code
//   - the sign/numerator/denominator representation, including the infinity and
//     not-a-number encodings, is preserved EXACTLY; see the remarks below

/// <summary>
/// An exact rational number, with support for positive and negative infinity and for
/// not-a-number. LilyPond represents every musical duration as one of these, so exact
/// arithmetic here is what keeps rhythms from drifting.
/// </summary>
/// <remarks>
/// <para>
/// The representation is upstream's and is deliberately unchanged, because the whole
/// engine depends on its edge cases:
/// </para>
/// <list type="bullet">
/// <item><description><c>Sign</c> ranges over -2..2. ±2 are the infinities, ±1 are
/// ordinary negative and positive values, 0 is zero.</description></item>
/// <item><description>Not-a-number is encoded as a zero <c>Denominator</c>. A NaN must
/// never carry sign 0, so the normalizer forces sign 1 in that case.</description></item>
/// <item><description>Numerator and denominator are unsigned; the sign lives only in
/// <c>Sign</c>.</description></item>
/// </list>
/// </remarks>
public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
{
    private readonly int _sign;
    private readonly ulong _numerator;

    // The denominator is stored BIASED BY ONE, and this is load-bearing.
    //
    // Upstream's default constructor produces zero: sign 0, numerator 0,
    // denominator 1. A C# struct cannot guarantee that, because `default(Rational)`
    // and array allocation both bypass any parameterless constructor and simply zero
    // the fields. Storing the denominator directly would therefore make
    // `default(Rational)` a zero denominator -- which this type reads as
    // not-a-number, not as zero.
    //
    // Biasing by one makes the all-zeroes bit pattern mean denominator 1, so
    // `default(Rational)` is zero exactly as upstream intends. NaN's denominator of
    // 0 is stored as ulong.MaxValue and unchecked wrap-around brings it back.
    private readonly ulong _denominatorMinusOne;

    private ulong _denominator => unchecked(_denominatorMinusOne + 1UL);

    private Rational(int sign, ulong numerator, ulong denominator)
    {
        _sign = sign;
        _numerator = numerator;
        _denominatorMinusOne = unchecked(denominator - 1UL);
    }

    /// <summary>Initializes a rational from an integer.</summary>
    /// <param name="value">The integer value.</param>
    public Rational(long value)
    {
        _sign = Math.Sign(value);
        _numerator = value < 0 ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
        _denominatorMinusOne = 0UL;
    }

    /// <summary>Initializes a rational from a numerator and denominator.</summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator; zero yields an infinity or NaN.</param>
    public Rational(long numerator, long denominator)
    {
        // Upstream takes the sign of the numerator when the denominator is zero, and
        // uses signbit on the denominator so that a negative zero denominator flips
        // the sign.
        this = Normalize(
            Math.Sign(numerator) * (IsNegativeIncludingZero(denominator) ? -1 : 1),
            AbsoluteValue(numerator),
            AbsoluteValue(denominator));
    }

    /// <summary>Gets the signed numerator.</summary>
    public long Numerator => _sign * (long)_numerator;

    /// <summary>Gets the denominator, which is always non-negative.</summary>
    public long Denominator => (long)_denominator;

    /// <summary>Gets the sign field, ranging over -2..2 as described in the remarks.</summary>
    internal int Sign => _sign;

    /// <summary>Gets a rational equal to zero.</summary>
    public static Rational Zero => new Rational(0, 0, 1);

    /// <summary>Gets a rational equal to one.</summary>
    public static Rational One => new Rational(1, 1, 1);

    /// <summary>Gets positive infinity.</summary>
    public static Rational Infinity => new Rational(2, 1, 1);

    /// <summary>Gets not-a-number.</summary>
    public static Rational NaN => new Rational(1, 0, 0);

    /// <summary>Gets a value indicating whether this is finite — neither infinite nor NaN.</summary>
    public bool IsFinite => _denominator != 0 && !IsInfinite;

    /// <summary>Gets a value indicating whether this is positive or negative infinity.</summary>
    public bool IsInfinite => (_sign / 2) != 0;

    /// <summary>Gets a value indicating whether this is not-a-number.</summary>
    public bool IsNaN => _denominator == 0;

    /// <summary>
    /// Gets a value indicating whether the value carries a negative sign. True for
    /// negative finite values, negative infinity, and a negative NaN.
    /// </summary>
    public bool IsNegative => _sign < 0;

    /// <summary>Gets a value indicating whether the value is non-zero.</summary>
    public bool IsNonZero => _sign != 0;

    private static bool IsNegativeIncludingZero(long value) => value < 0;

    private static ulong AbsoluteValue(long value)
        => value < 0 ? (ulong)(-(value + 1)) + 1UL : (ulong)value;

    private static Rational Normalize(int sign, ulong numerator, ulong denominator)
    {
        if (denominator != 0)
        {
            if (sign == 0)
            {
                return new Rational(0, 0, 1);
            }

            if (numerator == 0)
            {
                return new Rational(0, 0, 1);
            }

            ulong divisor = GreatestCommonDivisor(numerator, denominator);
            return new Rational(sign, numerator / divisor, denominator / divisor);
        }

        if (numerator != 0)
        {
            // A zero denominator with a non-zero numerator is an infinity.
            return sign < 0 ? -Infinity : Infinity;
        }

        // Zero over zero is NaN, which must not keep sign 0.
        return new Rational(sign == 0 ? 1 : sign, 0, 0);
    }

    private static ulong GreatestCommonDivisor(ulong a, ulong b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    /// <summary>Negates a rational.</summary>
    /// <param name="value">The value to negate.</param>
    /// <returns>The additive inverse.</returns>
    public static Rational operator -(Rational value)
        => new Rational(-value._sign, value._numerator, value._denominator);

    /// <summary>Adds two rationals.</summary>
    /// <param name="left">The first addend.</param>
    /// <param name="right">The second addend.</param>
    /// <returns>The sum.</returns>
    public static Rational operator +(Rational left, Rational right)
    {
        if (left.IsNaN)
        {
            return left;
        }

        if (right.IsNaN)
        {
            return right;
        }

        if (right._sign == 0)
        {
            return left;
        }

        if (left._sign == 0)
        {
            return right;
        }

        if (left.IsInfinite)
        {
            // Opposite infinities cancel to NaN; like infinities absorb.
            return left._sign == -right._sign ? NaN : left;
        }

        if (right.IsInfinite)
        {
            return right;
        }

        // Work in BigInteger so that a common denominator cannot overflow before
        // normalization brings it back down.
        BigInteger leftNumerator = left.Numerator;
        BigInteger rightNumerator = right.Numerator;
        BigInteger leftDenominator = left._denominator;
        BigInteger rightDenominator = right._denominator;

        BigInteger denominator = leftDenominator * rightDenominator / BigInteger.GreatestCommonDivisor(leftDenominator, rightDenominator);
        BigInteger numerator = (leftNumerator * (denominator / leftDenominator))
                               + (rightNumerator * (denominator / rightDenominator));

        return FromBigInteger(numerator, denominator);
    }

    /// <summary>Subtracts one rational from another.</summary>
    /// <param name="left">The minuend.</param>
    /// <param name="right">The subtrahend.</param>
    /// <returns>The difference.</returns>
    public static Rational operator -(Rational left, Rational right) => left + (-right);

    /// <summary>Multiplies two rationals.</summary>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <returns>The product.</returns>
    public static Rational operator *(Rational left, Rational right)
    {
        int sign = left._sign * Math.Sign(right._sign);
        if (right.IsInfinite)
        {
            return new Rational(Math.Sign(sign) * 2, left._numerator, left._denominator);
        }

        BigInteger numerator = (BigInteger)left._numerator * right._numerator;
        BigInteger denominator = (BigInteger)left._denominator * right._denominator;
        return NormalizeBig(sign, numerator, denominator);
    }

    /// <summary>Divides one rational by another.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The quotient.</returns>
    public static Rational operator /(Rational left, Rational right)
    {
        Rational reciprocal;
        if (right.IsInfinite)
        {
            // Dividing by an infinity yields zero, which upstream expresses by
            // replacing the divisor with a default-constructed (zero) Rational.
            reciprocal = Zero;
        }
        else
        {
            // Swap numerator and denominator; a resulting zero denominator is NaN and
            // must not carry sign 0.
            int sign = right._sign == 0 ? 1 : right._sign;
            reciprocal = new Rational(right._numerator == 0 ? sign : right._sign, right._denominator, right._numerator);
        }

        return left * reciprocal;
    }

    /// <summary>Computes the remainder, as upstream's <c>mod_rat</c> does.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The remainder.</returns>
    public static Rational operator %(Rational left, Rational right) => left.ModuloRational(right);

    private static Rational FromBigInteger(BigInteger numerator, BigInteger denominator)
    {
        int sign = numerator.Sign * (denominator.Sign < 0 ? -1 : 1);
        return NormalizeBig(sign, BigInteger.Abs(numerator), BigInteger.Abs(denominator));
    }

    private static Rational NormalizeBig(int sign, BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            if (!numerator.IsZero)
            {
                return sign < 0 ? -Infinity : Infinity;
            }

            return new Rational(sign == 0 ? 1 : sign, 0, 0);
        }

        if (sign == 0 || numerator.IsZero)
        {
            return Zero;
        }

        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        numerator /= divisor;
        denominator /= divisor;
        return new Rational(sign, (ulong)numerator, (ulong)denominator);
    }

    /// <summary>Truncates towards zero, returning an integer.</summary>
    /// <returns>The truncated value. Not valid for infinities.</returns>
    public long TruncatedInteger() => (long)(_numerator / _denominator) * _sign;

    /// <summary>Truncates towards zero, returning a rational.</summary>
    /// <returns>The truncated value, or the value itself when not finite.</returns>
    public Rational TruncatedRational()
    {
        if (!IsFinite)
        {
            return this;
        }

        return new Rational((long)(_numerator - (_numerator % _denominator)) * _sign, (long)_denominator);
    }

    /// <summary>Divides and truncates towards zero.</summary>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The truncated quotient.</returns>
    public Rational DivideRational(Rational divisor) => (this / divisor).TruncatedRational();

    /// <summary>Computes the remainder of a truncating division.</summary>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The remainder.</returns>
    public Rational ModuloRational(Rational divisor)
    {
        if (divisor.IsInfinite)
        {
            return this;
        }

        return ((this / divisor) - DivideRational(divisor)) * divisor;
    }

    /// <summary>
    /// Computes the Euclidean remainder, which is always non-negative for a finite
    /// divisor. LilyPond uses this for bar-relative timing.
    /// </summary>
    /// <param name="dividend">The dividend.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The non-negative remainder, or NaN when the divisor is not finite.</returns>
    public static Rational EuclideanRemainder(Rational dividend, Rational divisor)
    {
        if (!divisor.IsFinite)
        {
            return NaN;
        }

        Rational absoluteDivisor = divisor.IsNegative ? -divisor : divisor;
        Rational remainder = dividend.ModuloRational(absoluteDivisor);
        if (remainder < Zero)
        {
            remainder += absoluteDivisor;
        }

        return remainder;
    }

    /// <summary>Compares two rationals.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public static int Compare(Rational left, Rational right)
    {
        if (left._sign < right._sign)
        {
            return -1;
        }

        if (left._sign > right._sign)
        {
            return 1;
        }

        if (left.IsInfinite)
        {
            // Same sign, and both infinite.
            return 0;
        }

        if (left._sign == 0)
        {
            return 0;
        }

        BigInteger leftCross = (BigInteger)left._numerator * right._denominator;
        BigInteger rightCross = (BigInteger)right._numerator * left._denominator;
        if (leftCross < rightCross)
        {
            return -left._sign;
        }

        if (leftCross > rightCross)
        {
            return left._sign;
        }

        return 0;
    }

    /// <summary>Compares this value with another.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public int CompareTo(Rational other) => Compare(this, other);

    /// <summary>Determines whether two rationals are equal.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public bool Equals(Rational other)
        => _sign == other._sign && _numerator == other._numerator && _denominator == other._denominator;

    /// <summary>Determines whether this value equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when the object is an equal rational.</returns>
    public override bool Equals(object obj) => obj is Rational other && Equals(other);

    /// <summary>Returns a hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(_sign, _numerator, _denominator);

    /// <summary>Tests equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(Rational left, Rational right) => Compare(left, right) == 0;

    /// <summary>Tests inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(Rational left, Rational right) => Compare(left, right) != 0;

    /// <summary>Tests ordering.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the first is smaller.</returns>
    public static bool operator <(Rational left, Rational right) => Compare(left, right) < 0;

    /// <summary>Tests ordering.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the first is larger.</returns>
    public static bool operator >(Rational left, Rational right) => Compare(left, right) > 0;

    /// <summary>Tests ordering.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the first is not larger.</returns>
    public static bool operator <=(Rational left, Rational right) => Compare(left, right) <= 0;

    /// <summary>Tests ordering.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the first is not smaller.</returns>
    public static bool operator >=(Rational left, Rational right) => Compare(left, right) >= 0;

    /// <summary>Converts an integer to a rational implicitly, as upstream does.</summary>
    /// <param name="value">The integer value.</param>
    public static implicit operator Rational(long value) => new Rational(value);

    /// <summary>Converts an integer to a rational implicitly, as upstream does.</summary>
    /// <param name="value">The integer value.</param>
    public static implicit operator Rational(int value) => new Rational(value);

    /// <summary>Converts to a double, explicitly.</summary>
    /// <param name="value">The value to convert.</param>
    public static explicit operator double(Rational value) => value.ToDouble();

    /// <summary>Returns the value as a double.</summary>
    /// <returns>The nearest double, with the infinities and NaN preserved.</returns>
    public double ToDouble()
    {
        if (IsNaN)
        {
            return double.NaN;
        }

        if (IsInfinite)
        {
            return _sign < 0 ? double.NegativeInfinity : double.PositiveInfinity;
        }

        return _sign * ((double)_numerator / _denominator);
    }

    /// <summary>Builds a rational from a double, the way upstream's <c>Rational (double)</c>
    /// does.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The rational.</returns>
    /// <remarks>
    /// <para>
    /// CORRECTED BY THE MIDI GROUP. This USED to build the EXACT dyadic rational equal to
    /// the double's binary value, using BigInteger — which is precisely what upstream's own
    /// comment warns against, in as many words: "do not blindly substitute by libg++ code,
    /// since that uses arbitrary-size integers. The rationals would overflow too easily."
    /// They do. A tempo ramp in the MIDI subsuite produced a double whose exact dyadic
    /// numerator does not fit a <c>ulong</c>, and the conversion THREW, truncating eleven
    /// MIDI files after their header.
    /// </para>
    /// <para>
    /// Upstream's algorithm is deliberately LOSSY and is reproduced here: take the mantissa
    /// to twenty bits (<c>FACT = 1 &lt;&lt; 20</c>), normalize, then apply the exponent as a
    /// shift and normalize again. Two rationals built from the same double therefore agree
    /// with upstream's rather than being "more accurate" than it — which for a port is the
    /// only kind of accuracy that counts.
    /// </para>
    /// <para>
    /// ONE DELIBERATE DIVERGENCE: when the exponent shift would exceed 63 bits, upstream
    /// shifts a <c>uint64_t</c> by more than its width, which is UNDEFINED BEHAVIOUR in
    /// C++. The port saturates to zero or to the signed infinity instead — the
    /// mathematical limit of what the shift is approaching, and a defined answer where
    /// upstream has none.
    /// </para>
    /// </remarks>
    public static Rational FromDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return NaN;
        }

        if (double.IsInfinity(value))
        {
            return double.IsNegative(value) ? -Infinity : Infinity;
        }

        if (value == 0.0)
        {
            return Zero;
        }

        int sign = value < 0 ? -1 : 1;
        double magnitude = value * sign;

        // frexp: magnitude == mantissa * 2^exponent, with mantissa in [0.5, 1).
        int exponent = Math.ILogB(magnitude) + 1;
        double mantissa = Math.ScaleB(magnitude, -exponent);

        const ulong Fact = 1UL << 20;

        BigInteger numerator = new BigInteger((ulong)(mantissa * Fact));
        BigInteger denominator = new BigInteger(Fact);

        // Upstream normalizes here, before the shift, and again after it. The first pass
        // is what keeps the shifted value small enough to be worth having.
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (!divisor.IsZero)
        {
            numerator /= divisor;
            denominator /= divisor;
        }

        if (exponent < 0)
        {
            int shift = -exponent;
            if (shift >= 64 && numerator.IsZero)
            {
                return Zero;
            }

            denominator <<= shift;
        }
        else
        {
            numerator <<= exponent;
        }

        if (numerator > ulong.MaxValue || denominator > ulong.MaxValue)
        {
            // The saturation described above. Reduce first: the pair may still fit.
            BigInteger reducer = BigInteger.GreatestCommonDivisor(numerator, denominator);
            if (!reducer.IsZero)
            {
                numerator /= reducer;
                denominator /= reducer;
            }

            if (numerator > ulong.MaxValue)
            {
                return sign < 0 ? -Infinity : Infinity;
            }

            if (denominator > ulong.MaxValue)
            {
                return Zero;
            }
        }

        return NormalizeBig(sign, numerator, denominator);
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>
    /// <c>n</c>, <c>n/d</c>, <c>infinity</c>, <c>-infinity</c>, <c>nan</c> or
    /// <c>-nan</c>, matching upstream's <c>to_string</c>.
    /// </returns>
    public override string ToString()
    {
        if (IsInfinite)
        {
            return (_sign > 0 ? string.Empty : "-") + "infinity";
        }

        if (IsNaN)
        {
            return (_sign > 0 ? string.Empty : "-") + "nan";
        }

        string text = Numerator.ToString(CultureInfo.InvariantCulture);
        if (Denominator != 1 && Numerator != 0)
        {
            text += "/" + Denominator.ToString(CultureInfo.InvariantCulture);
        }

        return text;
    }
}
