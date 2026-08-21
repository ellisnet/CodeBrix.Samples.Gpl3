// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Text;

namespace Fresco.Brix.Editor;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Turns the character-set name a hyphenation dictionary writes on its first
/// line into the decoding that name asks for.
/// </summary>
/// <remarks>
/// <para>
/// Upstream hands the name straight to Python's <c>codecs.getreader()</c>,
/// whose registry answers for every legacy single-byte set. .NET's built-in
/// set is far smaller — outside the Unicode forms and ASCII only Latin-1 is
/// there — and four of the dictionaries this application bundles name sets
/// that are not (ISO8859-2, ISO8859-7, KOI8-R, KOI8-U). The tables in
/// <see cref="CharsetTables"/> stand in for that registry; they are generated
/// out of it rather than written by hand.
/// </para>
/// <para>
/// A name this class cannot answer for makes <see cref="TryDecode"/> answer
/// false, which is upstream's <c>LookupError</c>: the caller then tries the
/// next name on the line and finally falls back to Latin-1.
/// </para>
/// </remarks>
public static class Charsets
{
    /// <summary>Decodes bytes with a named character set.</summary>
    /// <param name="name">The name as the dictionary spells it.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="text">The decoded text.</param>
    /// <returns>Whether the name is one this application knows.</returns>
    public static bool TryDecode(string name, byte[] bytes, out string text)
    {
        text = null;
        if (bytes == null) { return false; }

        Encoding unicode = UnicodeEncodingFor(name);
        if (unicode != null)
        {
            text = unicode.GetString(bytes);
            return true;
        }

        if (!CharsetTables.TryGetTable(name, out string table)) { return false; }

        text = DecodeSingleByte(bytes, table);
        return true;
    }

    /// <summary>Decodes bytes as Latin-1, which never fails.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    /// <remarks>Upstream's fallback when no name on the first line named a
    /// character set it could find.</remarks>
    public static string DecodeLatin1(byte[] bytes)
        => bytes == null ? string.Empty : Encoding.Latin1.GetString(bytes);

    /// <summary>Decodes bytes with a high-half table.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="table">The 128 characters bytes 0x80-0xFF stand for.</param>
    /// <returns>The text.</returns>
    private static string DecodeSingleByte(byte[] bytes, string table)
    {
        char[] characters = new char[bytes.Length];
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            characters[index] = value < 0x80
                ? (char)value
                : table[value - 0x80];
        }

        return new string(characters);
    }

    /// <summary>Answers the Unicode encodings by name, or null.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The encoding, or null when the name is not a Unicode one.</returns>
    private static Encoding UnicodeEncodingFor(string name)
    {
        if (string.IsNullOrEmpty(name)) { return null; }

        switch (name.Replace("-", string.Empty).ToUpperInvariant())
        {
            case "UTF8": return new UTF8Encoding(false);
            case "UTF16": case "UTF16LE": return Encoding.Unicode;
            case "UTF16BE": return Encoding.BigEndianUnicode;
            case "ASCII": case "USASCII": return Encoding.ASCII;
            default: return null;
        }
    }
}
