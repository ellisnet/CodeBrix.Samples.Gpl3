// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using Fresco.Brix.Ly.Slexing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Ly; //was previously: ly/duration.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Functions dealing with LilyPond durations.</summary>
public static class Durations
{
    /// <summary>The duration names, from <c>\maxima</c> down to 2048th.</summary>
    public static readonly string[] Names =
    [
        "\\maxima", "\\longa", "\\breve",
        "1", "2", "4", "8", "16", "32", "64", "128", "256", "512", "1024", "2048",
    ];

    /// <summary>
    /// Returns the LilyPond string of a logarithmic duration (-3 up to and
    /// including 11; -2 is <c>\longa</c>, 0 is <c>1</c>), with dots and the
    /// scaling factor when it is not one.
    /// </summary>
    /// <param name="duration">The logarithmic duration.</param>
    /// <param name="dots">The number of dots.</param>
    /// <param name="factor">The scaling factor.</param>
    /// <returns>The text.</returns>
    public static string ToString(int duration, int dots = 0, Fraction? factor = null)
    {
        string result = Names[duration + 3] + new string('.', dots);
        Fraction scaling = factor ?? Fraction.One;
        if (scaling != Fraction.One)
        {
            result += "*" + scaling;
        }

        return result;
    }

    /// <summary>Returns (base, scaling) as two fractions for a list of
    /// duration tokens (the length, dots and scalings).</summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>The base duration and the scaling.</returns>
    public static (Fraction Base, Fraction Scaling) BaseScaling(
        IReadOnlyList<Token> tokens)
        => BaseScalingTexts(tokens.Select(t => t.Text).ToList());

    /// <summary>Returns (base, scaling) for duration token texts.</summary>
    /// <param name="texts">The token texts.</param>
    /// <returns>The base duration and the scaling.</returns>
    public static (Fraction Base, Fraction Scaling) BaseScalingTexts(
        IReadOnlyList<string> texts)
    {
        Fraction baseValue = new Fraction(8, 1L << Array.IndexOf(Names, texts[0]));
        Fraction scaling = Fraction.One;
        Fraction half = baseValue;
        foreach (string t in texts.Skip(1))
        {
            if (t == ".")
            {
                half /= 2;
                baseValue += half;
            }
            else if (t.StartsWith("*", StringComparison.Ordinal))
            {
                scaling *= Fraction.Parse(t.Substring(1));
            }
        }

        return (baseValue, scaling);
    }

    /// <summary>Returns (base, scaling) for a duration string such as
    /// <c>4..*2/3</c>.</summary>
    /// <param name="duration">The duration text.</param>
    /// <returns>The base duration and the scaling.</returns>
    public static (Fraction Base, Fraction Scaling) BaseScalingString(string duration)
    {
        string[] items = duration.Split('*');
        string[] dots = items[0].Split('.');
        Fraction baseValue = new Fraction(8, 1L << Array.IndexOf(Names, dots[0].Trim()));
        Fraction scaling = Fraction.One;
        Fraction half = baseValue;
        for (int i = 1; i < dots.Length; i++)
        {
            half /= 2;
            baseValue += half;
        }

        for (int i = 1; i < items.Length; i++)
        {
            scaling *= Fraction.Parse(items[i].Trim());
        }

        return (baseValue, scaling);
    }

    /// <summary>Returns the duration of the tokens as one fraction.</summary>
    /// <param name="tokens">The duration tokens.</param>
    /// <returns>The duration.</returns>
    public static Fraction DurationFraction(IReadOnlyList<Token> tokens)
    {
        (Fraction baseValue, Fraction scaling) = BaseScaling(tokens);
        return baseValue * scaling;
    }

    /// <summary>Returns the duration of the string as one fraction.</summary>
    /// <param name="duration">The duration text.</param>
    /// <returns>The duration.</returns>
    public static Fraction FractionString(string duration)
    {
        (Fraction baseValue, Fraction scaling) = BaseScalingString(duration);
        return baseValue * scaling;
    }

    /// <summary>Formats a fraction as <c>5/1</c> etc; zero as <c>0</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string FormatFraction(Fraction value)
    {
        if (value == Fraction.Zero)
        {
            return "0";
        }

        return value.Numerator + "/" + value.Denominator;
    }
}
