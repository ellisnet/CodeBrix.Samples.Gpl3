// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Globalization;

namespace Fresco.Brix.Ly;

/// <summary>
/// An exact rational number with the semantics the ported python-ly code needs
/// from Python's <c>fractions.Fraction</c>: always normalized (lowest terms,
/// positive denominator), exact arithmetic, value equality, and parsing of
/// "n", "n/d" and sign forms. New-in-family plumbing, not a port of a Python
/// class — the library is platform-free and deliberately references no other
/// CodeBrix package for this.
/// </summary>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    private readonly long _denominator;

    /// <summary>Initializes a fraction, normalizing to lowest terms and a
    /// positive denominator.</summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator; must not be zero.</param>
    /// <exception cref="DivideByZeroException">When the denominator is zero.</exception>
    public Fraction(long numerator, long denominator = 1)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("Fraction denominator is zero");
        }

        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        long gcd = Gcd(Math.Abs(numerator), denominator);
        if (gcd > 1)
        {
            numerator /= gcd;
            denominator /= gcd;
        }

        Numerator = numerator;
        _denominator = denominator;
    }

    /// <summary>Gets the numerator, sign-carrying.</summary>
    public long Numerator { get; }

    /// <summary>Gets the denominator, always positive. The default instance
    /// reads as 0/1.</summary>
    public long Denominator => _denominator == 0 ? 1 : _denominator;

    /// <summary>Gets zero.</summary>
    public static Fraction Zero => new Fraction(0);

    /// <summary>Gets one.</summary>
    public static Fraction One => new Fraction(1);

    /// <summary>Parses "n", "n/d", or a signed form — the subset Python's
    /// <c>Fraction(str)</c> accepts that the ported code feeds it.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The fraction.</returns>
    /// <exception cref="FormatException">When the text is not a fraction.</exception>
    public static Fraction Parse(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        int slash = trimmed.IndexOf('/');
        if (slash < 0)
        {
            return new Fraction(long.Parse(trimmed, CultureInfo.InvariantCulture));
        }

        return new Fraction(
            long.Parse(trimmed.Substring(0, slash), CultureInfo.InvariantCulture),
            long.Parse(trimmed.Substring(slash + 1), CultureInfo.InvariantCulture));
    }

    /// <summary>Adds two fractions.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The exact sum.</returns>
    public static Fraction operator +(Fraction left, Fraction right)
        => new Fraction(
            left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

    /// <summary>Subtracts two fractions.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The exact difference.</returns>
    public static Fraction operator -(Fraction left, Fraction right)
        => new Fraction(
            left.Numerator * right.Denominator - right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

    /// <summary>Negates a fraction.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The negation.</returns>
    public static Fraction operator -(Fraction value)
        => new Fraction(-value.Numerator, value.Denominator);

    /// <summary>Multiplies two fractions.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The exact product.</returns>
    public static Fraction operator *(Fraction left, Fraction right)
        => new Fraction(
            left.Numerator * right.Numerator,
            left.Denominator * right.Denominator);

    /// <summary>Divides two fractions.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The exact quotient.</returns>
    public static Fraction operator /(Fraction left, Fraction right)
        => new Fraction(
            left.Numerator * right.Denominator,
            left.Denominator * right.Numerator);

    /// <summary>Divides a fraction by an integer.</summary>
    /// <param name="left">The value.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The exact quotient.</returns>
    public static Fraction operator /(Fraction left, long divisor)
        => new Fraction(left.Numerator, left.Denominator * divisor);

    /// <summary>Converts an integer to a fraction.</summary>
    /// <param name="value">The integer.</param>
    public static implicit operator Fraction(long value) => new Fraction(value);

    /// <summary>Compares for equality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether equal.</returns>
    public static bool operator ==(Fraction left, Fraction right) => left.Equals(right);

    /// <summary>Compares for inequality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether unequal.</returns>
    public static bool operator !=(Fraction left, Fraction right) => !left.Equals(right);

    /// <summary>Orders two fractions.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether left is smaller.</returns>
    public static bool operator <(Fraction left, Fraction right)
        => left.CompareTo(right) < 0;

    /// <summary>Orders two fractions.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether left is greater.</returns>
    public static bool operator >(Fraction left, Fraction right)
        => left.CompareTo(right) > 0;

    /// <summary>Orders two fractions.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether left is not greater.</returns>
    public static bool operator <=(Fraction left, Fraction right)
        => left.CompareTo(right) <= 0;

    /// <summary>Orders two fractions.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Whether left is not smaller.</returns>
    public static bool operator >=(Fraction left, Fraction right)
        => left.CompareTo(right) >= 0;

    /// <summary>Compares to another fraction.</summary>
    /// <param name="other">The other value.</param>
    /// <returns>The usual comparison result.</returns>
    public int CompareTo(Fraction other)
        => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    /// <summary>Value equality.</summary>
    /// <param name="other">The other value.</param>
    /// <returns>Whether equal.</returns>
    public bool Equals(Fraction other)
        => Numerator == other.Numerator && Denominator == other.Denominator;

    /// <summary>Value equality.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>Whether it is an equal fraction.</returns>
    public override bool Equals(object obj) => obj is Fraction other && Equals(other);

    /// <summary>The value hash.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    /// <summary>Formats as Python's Fraction does: "n/d", or just "n" when the
    /// denominator is 1.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
        => Denominator == 1
            ? Numerator.ToString(CultureInfo.InvariantCulture)
            : Numerator.ToString(CultureInfo.InvariantCulture) + "/"
                + Denominator.ToString(CultureInfo.InvariantCulture);

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
