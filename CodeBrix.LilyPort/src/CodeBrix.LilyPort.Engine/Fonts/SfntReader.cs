// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// A minimal OpenType/sfnt container reader: the table directory, the <c>head</c>
/// table, and the CFF charset.
/// <para>
/// New-in-family. Upstream reaches all of this through FreeType, which LilyPort does
/// not take a dependency on — the engine needs exactly three things from the font
/// binary (the two custom LilyPond tables, units-per-em, and glyph name to glyph
/// index), and those are cheaper to read directly than to bind a native library for.
/// </para>
/// <para>
/// The glyph-name lookup comes from the CFF <c>charset</c>, NOT from the <c>post</c>
/// table. Emmentaler's <c>post</c> is format 3.0, which by definition carries no
/// glyph names at all — a reader written to parse <c>post</c> format 2.0 finds
/// nothing. Master plan section 11, correction 2.
/// </para>
/// </summary>
public sealed class SfntReader
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (uint Offset, uint Length)> _tables
        = new Dictionary<string, (uint, uint)>(StringComparer.Ordinal);

    /// <summary>Initializes a reader over a font file's bytes.</summary>
    /// <param name="data">The whole font file.</param>
    public SfntReader(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        ReadTableDirectory();
    }

    /// <summary>Reads a font file from disk.</summary>
    /// <param name="path">The path to the font.</param>
    /// <returns>The reader.</returns>
    public static SfntReader FromFile(string path) => new SfntReader(File.ReadAllBytes(path));

    /// <summary>Gets the four-character tags of every table in the font.</summary>
    public IEnumerable<string> TableTags => _tables.Keys;

    /// <summary>Determines whether the font carries a table.</summary>
    /// <param name="tag">The four-character table tag.</param>
    /// <returns><see langword="true"/> when present.</returns>
    public bool HasTable(string tag) => _tables.ContainsKey(tag);

    /// <summary>Returns a table's raw bytes.</summary>
    /// <param name="tag">The four-character table tag.</param>
    /// <returns>The bytes, or <see langword="null"/> when the table is absent.</returns>
    public byte[] GetTable(string tag)
    {
        if (!_tables.TryGetValue(tag, out (uint Offset, uint Length) entry))
        {
            return null;
        }

        byte[] result = new byte[entry.Length];
        Array.Copy(_data, entry.Offset, result, 0, entry.Length);
        return result;
    }

    /// <summary>
    /// Gets the font's design units per em, from the <c>head</c> table. Emmentaler
    /// uses 1000.
    /// </summary>
    public int UnitsPerEm
    {
        get
        {
            if (!_tables.TryGetValue("head", out (uint Offset, uint Length) head))
            {
                return 1000;
            }

            // head: version(4) fontRevision(4) checkSumAdjustment(4) magic(4)
            //       flags(2) unitsPerEm(2)
            return ReadUInt16((int)head.Offset + 18);
        }
    }

    /// <summary>
    /// Returns the family name the font calls itself, from the <c>name</c> table, or
    /// <see langword="null"/> when it does not say.
    /// <para>
    /// A DOCUMENT asks for a font by FAMILY (<c>\override #'(fonts . ((serif .
    /// "DummyGPL")))</c>), and fontconfig indexes a file it is handed by the family the
    /// file declares — so registering a document-supplied face means reading this. The
    /// TYPOGRAPHIC family (name ID 16) wins where a face declares one, because that is
    /// the name that groups a family of more than four styles; ID 1 is the fallback, and
    /// is what both of the corpus's dummy faces carry.
    /// </para>
    /// <para>
    /// Records are searched Windows-first (platform 3, English), then Macintosh
    /// (platform 1, Roman, English), which is the order a face's own names are usually
    /// ordered in and the order fontconfig's own reader prefers.
    /// </para>
    /// </summary>
    /// <returns>The family name, or <see langword="null"/>.</returns>
    public string ReadFamilyName()
    {
        if (!_tables.TryGetValue("name", out (uint Offset, uint Length) name))
        {
            return null;
        }

        int baseOffset = (int)name.Offset;
        int count = ReadUInt16(baseOffset + 2);
        int storage = baseOffset + ReadUInt16(baseOffset + 4);

        // (nameId, platformId) pairs in order of preference. A typographic family beats
        // the plain one, and within each, Windows beats Macintosh.
        foreach ((int wantedId, int wantedPlatform) in new[]
                 {
                     (16, 3), (16, 1), (1, 3), (1, 1),
                 })
        {
            for (int i = 0; i < count; i++)
            {
                int record = baseOffset + 6 + (12 * i);
                if (record + 12 > baseOffset + name.Length)
                {
                    break;
                }

                if (ReadUInt16(record) != wantedPlatform || ReadUInt16(record + 6) != wantedId)
                {
                    continue;
                }

                int length = ReadUInt16(record + 8);
                int offset = storage + ReadUInt16(record + 10);
                if (length == 0 || offset + length > _data.Length)
                {
                    continue;
                }

                // Platform 3 stores UTF-16BE; platform 1 stores one byte per character.
                // Neither is decoded through a general converter: the names that matter
                // here are ASCII, and a face with a name in another script would answer
                // a name no document could type anyway.
                char[] text = wantedPlatform == 3
                    ? new char[length / 2]
                    : new char[length];
                for (int c = 0; c < text.Length; c++)
                {
                    text[c] = wantedPlatform == 3
                        ? (char)ReadUInt16(offset + (c * 2))
                        : (char)_data[offset + c];
                }

                string result = new string(text).Trim();
                if (result.Length > 0)
                {
                    return result;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the font's x-height and cap height in DESIGN UNITS, from <c>OS/2</c>, or
    /// <c>(0, 0)</c> when the table is absent or too old to carry them.
    /// <para>
    /// The two fields arrived in <c>OS/2</c> version 2, so a version-0 or version-1
    /// table is not merely missing the values — reading at those offsets would read
    /// whatever follows the table. That is why the version is checked rather than the
    /// length.
    /// </para>
    /// </summary>
    /// <returns>The x-height and cap height, in design units.</returns>
    public (int XHeight, int CapHeight) ReadXAndCapHeight()
    {
        if (!_tables.TryGetValue("OS/2", out (uint Offset, uint Length) os2))
        {
            return (0, 0);
        }

        if (ReadUInt16((int)os2.Offset) < 2 || os2.Length < 90)
        {
            return (0, 0);
        }

        // OS/2 v2: sxHeight at byte 86, sCapHeight at 88, both signed.
        return (ReadInt16((int)os2.Offset + 86), ReadInt16((int)os2.Offset + 88));
    }

    /// <summary>
    /// Gets the font's TYPOGRAPHIC ascender and descender in DESIGN UNITS, from
    /// <c>OS/2</c>, or <c>(0, 0)</c> when the table is absent or too short.
    /// <para>
    /// Unlike x-height and cap height, these two are present from <c>OS/2</c> VERSION 0
    /// onwards, so only the table's length is checked. The descender is signed and is
    /// normally NEGATIVE — the pair spans the baseline, and Emmentaler's is
    /// <c>(800, -200)</c>, exactly one em.
    /// </para>
    /// <para>
    /// ⚠ NOT <c>hhea</c>'s ascender/descender and NOT <c>usWinAscent</c>/<c>Descent</c>.
    /// For Emmentaler those are 2127/-2314 — 4.44 em — because a music font's glyphs
    /// reach far above and below the staff. The typographic pair is the one that means
    /// "a line of this font", which is what a stand-in for a missing glyph wants.
    /// </para>
    /// </summary>
    /// <returns>The typographic ascender and descender, in design units.</returns>
    public (int TypoAscender, int TypoDescender) ReadTypoAscenderDescender()
    {
        if (!_tables.TryGetValue("OS/2", out (uint Offset, uint Length) os2))
        {
            return (0, 0);
        }

        if (os2.Length < 72)
        {
            return (0, 0);
        }

        // OS/2 v0 onwards: sTypoAscender at byte 68, sTypoDescender at 70, both signed.
        return (ReadInt16((int)os2.Offset + 68), ReadInt16((int)os2.Offset + 70));
    }

    /// <summary>
    /// Returns the glyph names in glyph-index order, read from the CFF charset.
    /// <para>
    /// Index 0 is always <c>.notdef</c> and is not listed in the charset itself, so it
    /// is supplied here.
    /// </para>
    /// </summary>
    /// <returns>The glyph names, or an empty list when the font has no CFF table.</returns>
    public List<string> ReadCffGlyphNames()
    {
        byte[] cff = GetTable("CFF ");
        if (cff == null)
        {
            return new List<string>();
        }

        return new CffCharsetReader(cff).ReadGlyphNames();
    }

    /// <summary>
    /// Returns the character-to-glyph map, from the <c>cmap</c> table.
    /// <para>
    /// Formats 4 and 12 are read, which between them cover every vendored text face:
    /// format 4 is the sixteen-bit Unicode map every OpenType font must carry, and
    /// format 12 is the one that reaches beyond the basic plane. A subtable in any
    /// other format is skipped rather than guessed at.
    /// </para>
    /// </summary>
    /// <returns>Code point to glyph index; empty when the font has no usable subtable.</returns>
    public Dictionary<int, int> ReadCmap()
    {
        Dictionary<int, int> map = new Dictionary<int, int>();
        if (!_tables.TryGetValue("cmap", out (uint Offset, uint Length) cmap))
        {
            return map;
        }

        int baseOffset = (int)cmap.Offset;
        int tableCount = ReadUInt16(baseOffset + 2);

        int best = -1;
        int bestScore = -1;
        for (int i = 0; i < tableCount; i++)
        {
            int record = baseOffset + 4 + (i * 8);
            int platform = ReadUInt16(record);
            int encoding = ReadUInt16(record + 2);
            int subtable = baseOffset + (int)ReadUInt32(record + 4);

            // Prefer a full-repertoire Unicode map, then any Unicode map, then the
            // Windows symbol map several of the URW faces carry.
            int score = platform == 3 && encoding == 10 ? 4
                : platform == 0 && encoding >= 4 ? 4
                : platform == 3 && encoding == 1 ? 3
                : platform == 0 ? 3
                : platform == 3 && encoding == 0 ? 1
                : 0;

            if (score > bestScore)
            {
                bestScore = score;
                best = subtable;
            }
        }

        if (best < 0)
        {
            return map;
        }

        int format = ReadUInt16(best);
        if (format == 4)
        {
            ReadCmapFormat4(best, map);
        }
        else if (format == 12)
        {
            ReadCmapFormat12(best, map);
        }

        return map;
    }

    private void ReadCmapFormat4(int offset, Dictionary<int, int> map)
    {
        int segCountX2 = ReadUInt16(offset + 6);
        int segCount = segCountX2 / 2;

        int endCodes = offset + 14;
        int startCodes = endCodes + segCountX2 + 2;
        int idDeltas = startCodes + segCountX2;
        int idRangeOffsets = idDeltas + segCountX2;

        for (int segment = 0; segment < segCount; segment++)
        {
            int end = ReadUInt16(endCodes + (segment * 2));
            int start = ReadUInt16(startCodes + (segment * 2));
            int delta = (short)ReadUInt16(idDeltas + (segment * 2));
            int rangeOffsetAt = idRangeOffsets + (segment * 2);
            int rangeOffset = ReadUInt16(rangeOffsetAt);

            if (start > end)
            {
                continue;
            }

            for (int code = start; code <= end && code != 0xFFFF; code++)
            {
                int glyph;
                if (rangeOffset == 0)
                {
                    glyph = (code + delta) & 0xFFFF;
                }
                else
                {
                    // The offset is measured from the idRangeOffset SLOT itself, not
                    // from the table. That indirection is the one thing every hand
                    // written format 4 reader gets wrong.
                    int at = rangeOffsetAt + rangeOffset + ((code - start) * 2);
                    if (at + 1 >= _data.Length)
                    {
                        continue;
                    }

                    glyph = ReadUInt16(at);
                    if (glyph != 0)
                    {
                        glyph = (glyph + delta) & 0xFFFF;
                    }
                }

                if (glyph != 0)
                {
                    map[code] = glyph;
                }
            }
        }
    }

    private void ReadCmapFormat12(int offset, Dictionary<int, int> map)
    {
        uint groups = ReadUInt32(offset + 12);
        for (uint i = 0; i < groups; i++)
        {
            int record = offset + 16 + ((int)i * 12);
            if (record + 12 > _data.Length)
            {
                break;
            }

            uint start = ReadUInt32(record);
            uint end = ReadUInt32(record + 4);
            uint startGlyph = ReadUInt32(record + 8);

            for (uint code = start; code <= end && code - start < 0x10000; code++)
            {
                map[(int)code] = (int)(startGlyph + (code - start));
            }
        }
    }

    /// <summary>
    /// Returns the horizontal advance of every glyph, in design units, from
    /// <c>hhea</c> and <c>hmtx</c>.
    /// <para>
    /// The last entry in <c>hmtx</c>'s metrics array applies to every glyph after it —
    /// that is how a monospaced font stores one advance for a thousand glyphs — so the
    /// array is filled forward rather than read one-to-one.
    /// </para>
    /// </summary>
    /// <returns>The advances, indexed by glyph.</returns>
    public double[] ReadAdvances()
    {
        if (!_tables.TryGetValue("hhea", out (uint Offset, uint Length) hhea)
            || !_tables.TryGetValue("hmtx", out (uint Offset, uint Length) hmtx)
            || !_tables.TryGetValue("maxp", out (uint Offset, uint Length) maxp))
        {
            return Array.Empty<double>();
        }

        int glyphCount = ReadUInt16((int)maxp.Offset + 4);
        int metricCount = ReadUInt16((int)hhea.Offset + 34);
        if (metricCount == 0 || glyphCount == 0)
        {
            return Array.Empty<double>();
        }

        double[] advances = new double[glyphCount];
        double last = 0;
        for (int i = 0; i < glyphCount; i++)
        {
            if (i < metricCount)
            {
                int at = (int)hmtx.Offset + (i * 4);
                if (at + 1 < _data.Length)
                {
                    last = ReadUInt16(at);
                }
            }

            advances[i] = last;
        }

        return advances;
    }

    private void ReadTableDirectory()
    {
        if (_data.Length < 12)
        {
            throw new InvalidDataException("Not an sfnt font: file is too short.");
        }

        int numTables = ReadUInt16(4);
        int position = 12;

        for (int i = 0; i < numTables; i++)
        {
            if (position + 16 > _data.Length)
            {
                break;
            }

            string tag = Encoding.ASCII.GetString(_data, position, 4);
            uint offset = ReadUInt32(position + 8);
            uint length = ReadUInt32(position + 12);

            if (offset <= _data.Length && offset + length <= _data.Length)
            {
                _tables[tag] = (offset, length);
            }

            position += 16;
        }
    }

    private int ReadUInt16(int offset) => (_data[offset] << 8) | _data[offset + 1];

    private int ReadInt16(int offset) => (short)ReadUInt16(offset);

    private uint ReadUInt32(int offset)
        => ((uint)_data[offset] << 24)
           | ((uint)_data[offset + 1] << 16)
           | ((uint)_data[offset + 2] << 8)
           | _data[offset + 3];
}

/// <summary>
/// Reads glyph names out of a bare CFF table: the string INDEX plus the charset.
/// <para>
/// New-in-family. Only the parts needed to answer "what is glyph N called" are
/// implemented — the charstrings themselves are the backend's problem, not the
/// engine's.
/// </para>
/// </summary>
internal sealed class CffCharsetReader
{
    private readonly byte[] _cff;

    internal CffCharsetReader(byte[] cff) => _cff = cff;

    internal List<string> ReadGlyphNames()
    {
        List<string> names = new List<string>();

        // Header: major(1) minor(1) hdrSize(1) offSize(1)
        int position = _cff[2];

        // Name INDEX, then Top DICT INDEX, then String INDEX, then Global Subr INDEX.
        position = SkipIndex(position);
        int topDictStart = position;
        List<(int Start, int End)> topDicts = ReadIndexEntries(ref position);
        List<string> strings = ReadStringIndex(ref position);

        if (topDicts.Count == 0)
        {
            return names;
        }

        Dictionary<int, List<double>> topDict = ParseDict(topDicts[0].Start, topDicts[0].End);

        // CharStrings offset is operator 17; charset is operator 15.
        if (!topDict.TryGetValue(17, out List<double> charStringsOperands)
            || charStringsOperands.Count == 0)
        {
            return names;
        }

        int charStringsOffset = (int)charStringsOperands[charStringsOperands.Count - 1];
        int cursor = charStringsOffset;
        List<(int Start, int End)> charStrings = ReadIndexEntries(ref cursor);
        int glyphCount = charStrings.Count;

        names.Add(".notdef");
        if (glyphCount <= 1)
        {
            return names;
        }

        if (!topDict.TryGetValue(15, out List<double> charsetOperands) || charsetOperands.Count == 0)
        {
            // No charset means the predefined ISOAdobe ordering, which Emmentaler
            // never uses. Nothing more can be said about the names.
            return names;
        }

        int charsetOffset = (int)charsetOperands[charsetOperands.Count - 1];
        if (charsetOffset <= 2)
        {
            // 0, 1 and 2 name the predefined charsets rather than an offset.
            return names;
        }

        ReadCharset(charsetOffset, glyphCount, strings, names);

        // Keep the top-dict start referenced so the layout above stays readable.
        _ = topDictStart;
        return names;
    }

    private void ReadCharset(int offset, int glyphCount, List<string> strings, List<string> names)
    {
        int position = offset;
        int format = _cff[position++];

        switch (format)
        {
            case 0:
                for (int i = 1; i < glyphCount && position + 1 < _cff.Length; i++)
                {
                    int sid = (_cff[position] << 8) | _cff[position + 1];
                    position += 2;
                    names.Add(SidToString(sid, strings));
                }

                break;

            case 1:
            case 2:
                while (names.Count < glyphCount && position < _cff.Length)
                {
                    int first = (_cff[position] << 8) | _cff[position + 1];
                    position += 2;

                    int left;
                    if (format == 1)
                    {
                        left = _cff[position];
                        position += 1;
                    }
                    else
                    {
                        left = (_cff[position] << 8) | _cff[position + 1];
                        position += 2;
                    }

                    for (int i = 0; i <= left && names.Count < glyphCount; i++)
                    {
                        names.Add(SidToString(first + i, strings));
                    }
                }

                break;

            default:
                break;
        }
    }

    private static string SidToString(int sid, List<string> strings)
    {
        if (sid < CffStandardStrings.Names.Length)
        {
            return CffStandardStrings.Names[sid];
        }

        int index = sid - CffStandardStrings.Names.Length;
        return index >= 0 && index < strings.Count ? strings[index] : "sid" + sid;
    }

    private List<string> ReadStringIndex(ref int position)
    {
        List<string> result = new List<string>();
        foreach ((int Start, int End) entry in ReadIndexEntries(ref position))
        {
            result.Add(Encoding.ASCII.GetString(_cff, entry.Start, entry.End - entry.Start));
        }

        return result;
    }

    private List<(int Start, int End)> ReadIndexEntries(ref int position)
    {
        List<(int Start, int End)> entries = new List<(int, int)>();

        int count = (_cff[position] << 8) | _cff[position + 1];
        position += 2;

        if (count == 0)
        {
            return entries;
        }

        int offsetSize = _cff[position++];
        int[] offsets = new int[count + 1];
        for (int i = 0; i <= count; i++)
        {
            int value = 0;
            for (int b = 0; b < offsetSize; b++)
            {
                value = (value << 8) | _cff[position++];
            }

            offsets[i] = value;
        }

        int dataStart = position - 1;
        for (int i = 0; i < count; i++)
        {
            entries.Add((dataStart + offsets[i], dataStart + offsets[i + 1]));
        }

        position = dataStart + offsets[count];
        return entries;
    }

    private int SkipIndex(int position)
    {
        int cursor = position;
        ReadIndexEntries(ref cursor);
        return cursor;
    }

    private Dictionary<int, List<double>> ParseDict(int start, int end)
    {
        Dictionary<int, List<double>> result = new Dictionary<int, List<double>>();
        List<double> operands = new List<double>();

        int position = start;
        while (position < end)
        {
            int b0 = _cff[position];

            if (b0 <= 21)
            {
                int op = b0;
                position++;
                if (b0 == 12)
                {
                    op = 1200 + _cff[position];
                    position++;
                }

                result[op] = new List<double>(operands);
                operands.Clear();
            }
            else if (b0 == 28)
            {
                operands.Add((short)((_cff[position + 1] << 8) | _cff[position + 2]));
                position += 3;
            }
            else if (b0 == 29)
            {
                operands.Add((_cff[position + 1] << 24)
                             | (_cff[position + 2] << 16)
                             | (_cff[position + 3] << 8)
                             | _cff[position + 4]);
                position += 5;
            }
            else if (b0 == 30)
            {
                position++;
                operands.Add(ReadRealOperand(ref position));
            }
            else if (b0 >= 32 && b0 <= 246)
            {
                operands.Add(b0 - 139);
                position++;
            }
            else if (b0 >= 247 && b0 <= 250)
            {
                operands.Add(((b0 - 247) * 256) + _cff[position + 1] + 108);
                position += 2;
            }
            else if (b0 >= 251 && b0 <= 254)
            {
                operands.Add((-(b0 - 251) * 256) - _cff[position + 1] - 108);
                position += 2;
            }
            else
            {
                position++;
            }
        }

        return result;
    }

    private double ReadRealOperand(ref int position)
    {
        StringBuilder text = new StringBuilder();
        bool done = false;

        while (!done && position < _cff.Length)
        {
            int b = _cff[position++];
            for (int half = 0; half < 2; half++)
            {
                int nibble = half == 0 ? (b >> 4) & 0xF : b & 0xF;
                switch (nibble)
                {
                    case 0xA:
                        text.Append('.');
                        break;
                    case 0xB:
                        text.Append('E');
                        break;
                    case 0xC:
                        text.Append("E-");
                        break;
                    case 0xE:
                        text.Append('-');
                        break;
                    case 0xF:
                        done = true;
                        break;
                    case 0xD:
                        break;
                    default:
                        text.Append((char)('0' + nibble));
                        break;
                }

                if (done)
                {
                    break;
                }
            }
        }

        return double.TryParse(
            text.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double value)
            ? value
            : 0.0;
    }
}
