/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using System.Text;

namespace CodeBrix.LilyPort.Flower; //was previously: flower/string-convert.cc, flower/include/string-convert.hh;
// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port:
//   - translated from C++17 to C# targeting net10.0
//   - form_string / vform_string are NOT ported: they are printf wrappers, and C#
//     has string interpolation and composite formatting. Callers use those instead.

/// <summary>
/// String and byte conversions. The big-endian helpers exist because LilyPond writes
/// MIDI, which is a big-endian format.
/// </summary>
public static class StringConvert
{
    /// <summary>Converts a nibble to its lowercase hexadecimal digit.</summary>
    /// <param name="nibble">The value; only the low four bits are used.</param>
    /// <returns>The hexadecimal digit.</returns>
    public static char NibbleToHex(int nibble)
    {
        int value = nibble & 0xF;
        return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
    }

    /// <summary>Converts a hexadecimal digit to its value.</summary>
    /// <param name="digit">The digit to convert.</param>
    /// <returns>The value, or -1 when the character is not a hexadecimal digit.</returns>
    public static int HexToNibble(char digit)
    {
        if (digit >= '0' && digit <= '9')
        {
            return digit - '0';
        }

        if (digit >= 'A' && digit <= 'F')
        {
            return digit - 'A' + 10;
        }

        if (digit >= 'a' && digit <= 'f')
        {
            return digit - 'a' + 10;
        }

        return -1;
    }

    /// <summary>Converts a byte to two hexadecimal digits.</summary>
    /// <param name="value">The byte to convert.</param>
    /// <returns>The two-digit hexadecimal representation.</returns>
    public static string BinToHex(byte value)
        => new string(new[] { NibbleToHex(value >> 4), NibbleToHex(value) });

    /// <summary>Converts each byte of a string to two hexadecimal digits.</summary>
    /// <param name="value">The text to convert.</param>
    /// <returns>The hexadecimal representation.</returns>
    public static string BinToHex(string value)
    {
        StringBuilder builder = new StringBuilder();
        foreach (char c in value ?? string.Empty)
        {
            builder.Append(BinToHex((byte)c));
        }

        return builder.ToString();
    }

    /// <summary>Converts a hexadecimal string back to bytes.</summary>
    /// <param name="value">The hexadecimal text, which must have an even length.</param>
    /// <returns>The decoded text.</returns>
    public static string HexToBin(string value)
    {
        string text = value ?? string.Empty;
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i + 1 < text.Length; i += 2)
        {
            int high = HexToNibble(text[i]);
            int low = HexToNibble(text[i + 1]);
            if (high < 0 || low < 0)
            {
                return string.Empty;
            }

            builder.Append((char)((high << 4) | low));
        }

        return builder.ToString();
    }

    /// <summary>Encodes a 32-bit value big-endian, as MIDI requires.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>Four characters, most significant byte first.</returns>
    public static string BigEndianU32(uint value)
        => new string(new[]
        {
            (char)((value >> 24) & 0xFF),
            (char)((value >> 16) & 0xFF),
            (char)((value >> 8) & 0xFF),
            (char)(value & 0xFF),
        });

    /// <summary>Encodes a 24-bit value big-endian.</summary>
    /// <param name="value">The value to encode; only the low 24 bits are used.</param>
    /// <returns>Three characters, most significant byte first.</returns>
    public static string BigEndianU24(uint value)
        => new string(new[]
        {
            (char)((value >> 16) & 0xFF),
            (char)((value >> 8) & 0xFF),
            (char)(value & 0xFF),
        });

    /// <summary>Encodes a 16-bit value big-endian.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>Two characters, most significant byte first.</returns>
    public static string BigEndianU16(ushort value)
        => new string(new[]
        {
            (char)((value >> 8) & 0xFF),
            (char)(value & 0xFF),
        });

    /// <summary>Pads a string on the right with spaces.</summary>
    /// <param name="value">The text to pad.</param>
    /// <param name="length">The target length; shorter values are returned unchanged.</param>
    /// <returns>The padded text.</returns>
    public static string PadTo(string value, int length)
    {
        string text = value ?? string.Empty;
        return length <= text.Length ? text : text + new string(' ', length - text.Length);
    }

    /// <summary>Converts text to lower case, invariantly.</summary>
    /// <param name="value">The text to convert.</param>
    /// <returns>The lower-cased text.</returns>
    public static string ToLower(string value) => (value ?? string.Empty).ToLowerInvariant();

    /// <summary>Converts text to upper case, invariantly.</summary>
    /// <param name="value">The text to convert.</param>
    /// <returns>The upper-cased text.</returns>
    public static string ToUpper(string value) => (value ?? string.Empty).ToUpperInvariant();

    /// <summary>
    /// Percent-encodes text, leaving unreserved characters alone. Note the unreserved
    /// set is upstream's and is WIDER than RFC 3986's: it also leaves <c>/</c> and
    /// <c>:</c> unescaped, because these encode file URIs for point-and-click.
    /// </summary>
    /// <param name="value">The text to encode.</param>
    /// <returns>The percent-encoded text.</returns>
    public static string PercentEncode(string value)
    {
        StringBuilder builder = new StringBuilder();
        foreach (char c in value ?? string.Empty)
        {
            if (IsNotEscapeCharacter(c))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(BinToHex((byte)c));
            }
        }

        return builder.ToString();
    }

    private static bool IsNotEscapeCharacter(char c)
    {
        if (c >= 'a' && c <= 'z')
        {
            return true;
        }

        if (c >= 'A' && c <= 'Z')
        {
            return true;
        }

        if (c >= '0' && c <= '9')
        {
            return true;
        }

        switch (c)
        {
            case '-':
            case '.':
            case '/':
            case ':':
            case '_':
                return true;
            default:
                return false;
        }
    }
}
