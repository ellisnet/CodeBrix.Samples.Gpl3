/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/misc.cc, lily/include/misc.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.
// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - EPG11/EPG12 finding: this file's ledger row has said `ported` since EPG0, but FOUR
//     of the functions upstream declares in misc.hh had never been carried —
//     peak_around and convex_amplifier (defined in misc.cc) and the two inline
//     linear_interpolate and normalize. Nothing had demanded them because they are used
//     only by the tie and slur scorers, which is exactly the silent-defect shape
//     standing rule 4 warns about. LinearInterpolate additionally existed as a PRIVATE
//     copy inside Layout/StencilIntegral.cs; that copy is retired and re-pointed here.

/// <summary>
/// Small shared helpers that do not belong to any one object.
/// </summary>
public static class Misc
{
    /// <summary>
    /// Returns a penalty that peaks at <paramref name="x"/> = 0 and falls to zero at
    /// <paramref name="threshold"/>, with <paramref name="epsilon"/> setting how sharply
    /// it spikes as <paramref name="x"/> approaches zero.
    /// </summary>
    /// <param name="epsilon">The sharpness of the peak.</param>
    /// <param name="threshold">The distance at which the penalty reaches zero.</param>
    /// <param name="x">The distance to score.</param>
    /// <returns>The penalty, never negative; one for a negative distance.</returns>
    public static double PeakAround(double epsilon, double threshold, double x)
    {
        if (x < 0)
        {
            return 1.0;
        }

        return Math.Max(-epsilon * (x - threshold) / ((x + epsilon) * threshold), 0.0);
    }

    /// <summary>
    /// Returns a value that is zero at zero, one at <paramref name="standardX"/>, and
    /// increasing thereafter.
    /// </summary>
    /// <param name="standardX">The distance that scores one.</param>
    /// <param name="increaseFactor">How steeply the curve rises.</param>
    /// <param name="x">The distance to score.</param>
    /// <returns>The amplified distance.</returns>
    public static double ConvexAmplifier(double standardX, double increaseFactor, double x)
        => (Math.Exp(increaseFactor * x / standardX) - 1.0) / (Math.Exp(increaseFactor) - 1.0);

    /// <summary>
    /// Interpolates linearly: maps <paramref name="x"/> from the range
    /// [<paramref name="x1"/>, <paramref name="x2"/>] onto
    /// [<paramref name="y1"/>, <paramref name="y2"/>].
    /// </summary>
    /// <param name="x">The value to map.</param>
    /// <param name="x1">The first input anchor.</param>
    /// <param name="x2">The second input anchor.</param>
    /// <param name="y1">The output for <paramref name="x1"/>.</param>
    /// <param name="y2">The output for <paramref name="x2"/>.</param>
    /// <returns>The interpolated value.</returns>
    public static double LinearInterpolate(double x, double x1, double x2, double y1, double y2)
        => ((x2 - x) / (x2 - x1) * y1) + ((x - x1) / (x2 - x1) * y2);

    /// <summary>
    /// Returns where <paramref name="x"/> falls between <paramref name="x1"/> and
    /// <paramref name="x2"/>, as a fraction.
    /// </summary>
    /// <param name="x">The value to place.</param>
    /// <param name="x1">The value that maps to zero.</param>
    /// <param name="x2">The value that maps to one.</param>
    /// <returns>The fraction.</returns>
    public static double Normalize(double x, double x1, double x2) => (x - x1) / (x2 - x1);

    /// <summary>
    /// Converts a CamelCase identifier into the hyphenated lisp form LilyPond uses for
    /// event classes and grob interfaces: <c>FooBar_Bla</c> becomes <c>foo-bar-bla</c>.
    /// <para>
    /// Underscores become hyphens too, which is what makes the two-word C++ class
    /// names line up with their Scheme interface names.
    /// </para>
    /// </summary>
    /// <param name="name">The CamelCase name.</param>
    /// <returns>The hyphenated identifier.</returns>
    public static string CamelCaseToLispIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        List<char> output = new List<char>(name.Length + 8);

        /* don't add '-' before first character */
        output.Add(char.ToLowerInvariant(name[0]));

        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                output.Add('-');
            }

            output.Add(char.ToLowerInvariant(name[i]));
        }

        for (int i = 0; i < output.Count; i++)
        {
            if (output[i] == '_')
            {
                output[i] = '-';
            }
        }

        return new string(output.ToArray());
    }

    /// <summary>Converts a CamelCase symbol into its hyphenated lisp symbol.</summary>
    /// <param name="name">The CamelCase symbol.</param>
    /// <returns>The hyphenated symbol.</returns>
    public static Symbol CamelCaseToLispIdentifier(Symbol name)
        => name == null ? null : Symbol.Intern(CamelCaseToLispIdentifier(name.Name));

    /// <summary>
    /// Formats a real the way LilyPond's output layer does: fixed point, invariant
    /// culture, so a decimal comma locale cannot corrupt the output stream.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="precision">The number of decimal places.</param>
    /// <returns>The formatted number.</returns>
    public static string FormatReal(double value, int precision = 4)
        => value.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns the 2-log of a positive integer, rounded down — upstream's
    /// <c>intlog2</c> from <c>lily/include/misc.hh</c>.
    /// </summary>
    /// <param name="d">The value, which must be positive.</param>
    /// <returns>The 2-log.</returns>
    public static int IntLog2(int d) => IntLog2((long)d);

    /// <summary>
    /// Returns the 2-log of a positive integer, rounded down — the 64-bit instantiation
    /// of upstream's <c>intlog2</c> template, which <c>beaming-pattern.cc</c> calls with
    /// the numerator and denominator of a <see cref="Rational"/>.
    /// </summary>
    /// <param name="d">The value, which must be positive.</param>
    /// <returns>The 2-log.</returns>
    public static int IntLog2(long d)
    {
        if (d <= 0)
        {
            Warn.Error("intlog2 with negative argument: " + d);
        }

        int i = 0;
        while (d != 1)
        {
            d /= 2;
            i++;
        }

        return i;
    }
}
