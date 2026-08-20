/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2019--2026 Han-Wen Nienhuys <hanwen@lilypond.org>

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

using System.IO;
using System.Text;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Origins; //was previously: lily/point-and-click.cc, lily/include/point-and-click.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The <c>textedit://</c> anchors that let a click in a rendered score open the
/// <c>.ly</c> line that produced it.
/// <para>
/// The URL is built from the event's <c>origin</c>, which is why this only works once
/// <c>Input</c> origins are real — an event with no origin gets no anchor and no
/// diagnostic, which is the correct behaviour and also the reason a half-built origin
/// layer looks exactly like a working one.
/// </para>
/// </summary>
public static class PointAndClick
{
    private static readonly Symbol OriginSymbol = Symbol.Intern("origin");

    /// <summary>
    /// Formats a stream event's origin as a <c>textedit://</c> URL.
    /// </summary>
    /// <param name="streamEvent">The event to locate.</param>
    /// <returns>The URL, or the empty string when the event carries no origin.</returns>
    public static string FormatUrl(StreamEvent streamEvent)
    {
        if (!(streamEvent?.GetProperty(OriginSymbol) is Input origin))
        {
            return string.Empty;
        }

        return FormatUrl(origin);
    }

    /// <summary>Formats a source location as a <c>textedit://</c> URL.</summary>
    /// <param name="origin">The location.</param>
    /// <returns>The URL, or the empty string when there is no file behind it.</returns>
    public static string FormatUrl(Input origin)
    {
        if (origin?.SourceFile == null)
        {
            return string.Empty;
        }

        origin.GetCounts(out int line, out int lineChar, out int column, out int _);

        string name = origin.FileString();
        if (name.Length == 0)
        {
            return string.Empty;
        }

        string absolute = Path.IsPathRooted(name)
            ? name
            : Path.GetFullPath(name);

        return "textedit://" + PercentEncode(absolute)
            + ":" + line + ":" + lineChar + ":" + column;
    }

    /// <summary>
    /// Percent-encodes a string the way upstream's <c>String_convert::percent_encode</c>
    /// does: everything except the characters its <c>is_not_escape_character</c> keeps.
    /// <para>
    /// The kept set is upstream's own — letters, digits, <c>-</c>, <c>.</c>, <c>/</c>,
    /// <c>:</c> and <c>_</c> (<c>flower/string-convert.cc:180-203</c>). The first version
    /// of this method kept <c>~</c> and escaped <c>:</c>, which its own doc comment
    /// claimed was upstream's set (trap 26): a home directory path came out with a raw
    /// tilde where upstream writes <c>%7E</c>, and every anchor's <c>:</c> separators
    /// would have doubled as encoded ones. Encoding walks the UTF-8 BYTES because
    /// upstream walks a <c>std::string</c>'s bytes.
    /// </para>
    /// </summary>
    /// <param name="value">The text to encode.</param>
    /// <returns>The encoded text.</returns>
    public static string PercentEncode(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        StringBuilder result = new StringBuilder(value.Length);
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            char c = (char)b;
            bool unreserved = (c >= 'A' && c <= 'Z')
                              || (c >= 'a' && c <= 'z')
                              || (c >= '0' && c <= '9')
                              || c == '-' || c == '.' || c == '/' || c == ':' || c == '_';

            if (unreserved)
            {
                result.Append(c);
            }
            else
            {
                result.Append('%').Append(b.ToString("X2"));
            }
        }

        return result.ToString();
    }
}
