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

using System.Collections.Generic;
using System.Globalization;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/misc.cc, lily/include/misc.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// Small shared helpers that do not belong to any one object.
/// </summary>
public static class Misc
{
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
    public static int IntLog2(int d)
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
