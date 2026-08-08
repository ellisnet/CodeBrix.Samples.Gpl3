// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Numerics;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// Conversions between LilyScheme's numeric tower and the engine's value types.
/// <para>
/// The two representations exist for different reasons and are deliberately not merged:
/// LilyScheme's tower is Guile's, exact integers through bignums to ratios and reals,
/// while <see cref="Rational"/> is LilyPond's own <c>flower/</c> type with its infinity
/// and not-a-number encodings. This is the seam between them.
/// </para>
/// </summary>
public static class SchemeConvert
{
    /// <summary>Converts a Scheme number to a flower <see cref="Rational"/>.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <param name="procedureName">The caller's name, used in the error message.</param>
    /// <returns>The rational.</returns>
    public static Rational ToRational(object value, string procedureName)
    {
        switch (value)
        {
            case long integer:
                return new Rational(integer);
            case int integer:
                return new Rational(integer);
            case BigInteger integer:
                return new Rational((long)integer);
            case Ratio ratio:
                return new Rational((long)ratio.Numerator, (long)ratio.Denominator);
            case double real:
                return FromDouble(real);
            case Rational already:
                return already;
            default:
                throw SchemeErrors.WrongType(procedureName, "rational", value);
        }
    }

    /// <summary>Converts a flower <see cref="Rational"/> to a Scheme number.</summary>
    /// <param name="value">The rational to convert.</param>
    /// <returns>An exact Scheme number, or an inexact one for the non-finite values.</returns>
    public static object FromRational(Rational value)
    {
        if (value.IsNaN)
        {
            return double.NaN;
        }

        if (value.IsInfinite)
        {
            return value.IsNegative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        return value.Denominator == 1
            ? value.Numerator
            : SchemeNumber.MakeRatio(value.Numerator, value.Denominator);
    }

    /// <summary>Converts a Scheme number to a double.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <param name="procedureName">The caller's name, used in the error message.</param>
    /// <returns>The value as a double.</returns>
    public static double ToDouble(object value, string procedureName)
    {
        switch (value)
        {
            case double real:
                return real;
            case long integer:
                return integer;
            case int integer:
                return integer;
            case BigInteger integer:
                return (double)integer;
            case Ratio ratio:
                return ratio.ToDouble();
            default:
                throw SchemeErrors.WrongType(procedureName, "number", value);
        }
    }

    /// <summary>Converts a Scheme number to a 32-bit integer.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <param name="procedureName">The caller's name, used in the error message.</param>
    /// <returns>The integer.</returns>
    public static int ToInt(object value, string procedureName) => (int)ToLong(value, procedureName);

    /// <summary>Converts a Scheme number to a 64-bit integer.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <param name="procedureName">The caller's name, used in the error message.</param>
    /// <returns>The integer.</returns>
    public static long ToLong(object value, string procedureName)
    {
        switch (value)
        {
            case long integer:
                return integer;
            case int integer:
                return integer;
            case BigInteger integer:
                return (long)integer;
            case double real:
                return (long)real;
            case Ratio ratio:
                return (long)(ratio.Numerator / ratio.Denominator);
            default:
                throw SchemeErrors.WrongType(procedureName, "integer", value);
        }
    }

    /// <summary>
    /// Determines whether a value is a Scheme number, the way <c>scm_is_number</c>
    /// does.
    /// <para>
    /// The engine asks this constantly, because a great many LilyPond properties are
    /// "a number, or the empty list meaning unset" and the two have to be told apart
    /// before any conversion is attempted.
    /// </para>
    /// </summary>
    /// <param name="value">The Scheme value.</param>
    /// <returns><see langword="true"/> when the value is a number.</returns>
    public static bool IsNumber(object value)
        => value is long || value is int || value is BigInteger
           || value is double || value is Ratio;

    /// <summary>
    /// Reads a pair of reals into a <see cref="DrulArray{T}"/>, the way
    /// <c>from_scm (value, Drul_array&lt;Real&gt; {...})</c> does — LEFT from the car,
    /// RIGHT from the cdr.
    /// </summary>
    /// <param name="value">The Scheme value, expected to be a pair of numbers.</param>
    /// <param name="fallback">The answer when the value is not such a pair.</param>
    /// <returns>The pair, or the fallback.</returns>
    public static DrulArray<double> ToDrulDouble(object value, DrulArray<double> fallback)
    {
        if (value is Pair pair && IsNumber(pair.Car) && IsNumber(pair.Cdr))
        {
            return new DrulArray<double>(
                ToDouble(pair.Car, "from-scm-drul"), ToDouble(pair.Cdr, "from-scm-drul"));
        }

        return fallback;
    }

    /// <summary>
    /// Reads a pair of booleans into a <see cref="DrulArray{T}"/>. As everywhere in the
    /// engine, only <c>#t</c> counts as true.
    /// </summary>
    /// <param name="value">The Scheme value, expected to be a pair.</param>
    /// <returns>The pair; both sides are <see langword="false"/> when it is not a pair.</returns>
    public static DrulArray<bool> ToDrulBool(object value)
    {
        if (value is Pair pair)
        {
            return new DrulArray<bool>(
                pair.Car is bool left && left, pair.Cdr is bool right && right);
        }

        return new DrulArray<bool>(false, false);
    }

    // A double reaching a Rational is always a whole-note count that came through
    // Scheme arithmetic; approximate it over a fixed denominator rather than trying to
    // recover an exact value that was already lost.
    private static Rational FromDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return Rational.NaN;
        }

        if (double.IsPositiveInfinity(value))
        {
            return Rational.Infinity;
        }

        if (double.IsNegativeInfinity(value))
        {
            return -Rational.Infinity;
        }

        const long Denominator = 1000000L;
        return new Rational((long)Math.Round(value * Denominator), Denominator);
    }
}

/// <summary>Builders for the Scheme conditions the engine raises.</summary>
public static class SchemeErrors
{
    /// <summary>Builds the <c>wrong-type-arg</c> condition Guile raises.</summary>
    /// <param name="procedureName">The procedure that rejected the argument.</param>
    /// <param name="expected">A description of what was expected.</param>
    /// <param name="value">The offending value.</param>
    /// <returns>The exception to throw.</returns>
    public static Exception WrongType(string procedureName, string expected, object value)
        => new SchemeThrow(
            Symbol.Intern("wrong-type-arg"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString("Wrong type argument, expected " + expected + ": ~S"),
                Pair.List(value),
                false));

    /// <summary>Builds the <c>misc-error</c> condition <c>scm_misc_error</c> raises.</summary>
    /// <param name="procedureName">The procedure reporting the problem.</param>
    /// <param name="message">The message, already formatted.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// Added 2026-08-08 by EPG14 for <c>skyline-scheme.cc</c>, which rejects a mismatched
    /// skyline direction and an infinite non-horizontal building this way rather than as a
    /// type error.
    /// </remarks>
    public static Exception MiscError(string procedureName, string message)
        => new SchemeThrow(
            Symbol.Intern("misc-error"),
            Pair.List(
                new MutableString(procedureName),
                new MutableString(message),
                Nil.Instance,
                false));
}
