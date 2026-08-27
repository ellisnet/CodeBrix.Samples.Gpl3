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

using System;
using System.Collections.Generic;
using System.Text;

namespace Fresco.Brix.Ly; //was previously: ly/util.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Utility functions: numbers as English words, roman numerals,
/// letters, and lower-camel-case identifier building.</summary>
public static class LyUtil
{
    private static readonly string[] Nums =
    [
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight",
        "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen",
        "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens =
    [
        "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty",
        "Ninety",
    ];

    private static readonly (string Letters, int Value)[] RomanNumerals =
    [
        ("M", 1000), ("CM", 900), ("D", 500), ("CD", 400), ("C", 100),
        ("XC", 90), ("L", 50), ("XL", 40), ("X", 10), ("IX", 9), ("V", 5),
        ("IV", 4), ("I", 1),
    ];

    /// <summary>
    /// Converts an integer (0..999999) to its English name, e.g. 1 to "One" —
    /// usable in LilyPond identifiers, which do not support digits.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <returns>The name.</returns>
    public static string Int2Text(int number)
    {
        StringBuilder result = new StringBuilder();
        if (number >= 1000)
        {
            int hundreds = number / 1000;
            number %= 1000;
            result.Append(Int2Text(hundreds)).Append("Thousand");
        }

        if (number >= 100)
        {
            int tens = number / 100;
            number %= 100;
            result.Append(Nums[tens]).Append("Hundred");
        }

        if (number < 20)
        {
            result.Append(Nums[number]);
        }
        else
        {
            int tens = number / 10;
            number %= 10;
            result.Append(Tens[tens - 2]).Append(Nums[number]);
        }

        string text = result.ToString();
        return text.Length > 0 ? text : "Zero";
    }

    /// <summary>Converts a positive integer to a roman number string,
    /// e.g. 12 to "XII".</summary>
    /// <param name="number">The number, at least 1.</param>
    /// <returns>The roman numeral.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When not positive.</exception>
    public static string Int2Roman(int number)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(number), number, "Roman numerals must be positive integers");
        }

        StringBuilder roman = new StringBuilder();
        foreach ((string letters, int value) in RomanNumerals)
        {
            int count = number / value;
            number %= value;
            for (int i = 0; i < count; i++)
            {
                roman.Append(letters);
            }
        }

        return roman.ToString();
    }

    /// <summary>Converts an integer to one or more letters:
    /// 1 is A, 26 is Z, 27 is AA; zero is the empty string.</summary>
    /// <param name="number">The number.</param>
    /// <param name="chars">The characters to pick from; A-Z when omitted.</param>
    /// <returns>The letters.</returns>
    public static string Int2Letter(int number, string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
    {
        int mod = chars.Length;
        List<int> result = new List<int>();
        while (number > 0)
        {
            number -= 1;
            result.Add(number % mod);
            number /= mod;
        }

        StringBuilder text = new StringBuilder(result.Count);
        for (int i = result.Count - 1; i >= 0; i--)
        {
            text.Append(chars[result[i]]);
        }

        return text.ToString();
    }

    /// <summary>Makes a lower-camel-case identifier of the given strings:
    /// ("soprano", "verse") becomes "sopranoVerse".</summary>
    /// <param name="args">The words.</param>
    /// <returns>The identifier.</returns>
    public static string MkId(params string[] args)
    {
        StringBuilder result = new StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Length == 0)
            {
                continue;
            }

            result.Append(i == 0 ? char.ToLowerInvariant(a[0]) : char.ToUpperInvariant(a[0]));
            result.Append(a, 1, a.Length - 1);
        }

        return result.ToString();
    }
}
