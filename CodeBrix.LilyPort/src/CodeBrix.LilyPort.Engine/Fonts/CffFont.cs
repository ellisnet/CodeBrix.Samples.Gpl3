// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// A Compact Font Format table, read far enough to execute its glyph programs.
/// <para>
/// New-in-family. Upstream never needs this — it hands the font to FreeType and asks
/// Pango for extents — but a glyph's INK extent is not recorded anywhere in an OpenType
/// font. It is a property of the outline, so the only way to learn it is to run the
/// charstring, and text layout needs it: how tall a line of text is decides what the
/// engraver puts above and below it.
/// </para>
/// <para>
/// The music fonts do not come through here. Their outlines are copied out of the
/// shipped SVG fonts (see <see cref="SvgFontOutlines"/>) because the SVG backend has to
/// emit upstream's exact bytes, and their extents come out of the <c>LILC</c> metadata
/// table rather than from any outline at all. This is the TEXT path.
/// </para>
/// <para>
/// Type 2 charstrings are implemented; Type 1 are not, and no vendored face uses them.
/// </para>
/// </summary>
public sealed class CffFont
{
    private readonly byte[] _data;

    private List<(int Start, int End)> _charStrings = new List<(int, int)>();
    private List<(int Start, int End)> _globalSubrs = new List<(int, int)>();
    private List<(int Start, int End)> _localSubrs = new List<(int, int)>();
    private List<string> _strings = new List<string>();

    // CID-keyed fonts pick a different Private DICT — and therefore a different local
    // subroutine set — per glyph. Emmentaler is not CID-keyed but several of the
    // vendored text faces could be, and using the wrong subrs silently draws nonsense.
    private List<List<(int Start, int End)>> _fdLocalSubrs;
    private byte[] _fdSelect;

    private double[] _fontMatrix = { 0.001, 0, 0, 0.001, 0, 0 };

    // A face is loaded once and shared for the life of the process (AllFontMetrics
    // caches it), so this memo is reachable from more than one thread even though a
    // sweep engraves one file at a time. It guards the cache only: a glyph's box is a
    // deterministic function of its index, so serializing the memo cannot change any
    // measurement, and computing one twice is harmless.
    private readonly object _boxGate = new object();

    private readonly Dictionary<int, Box> _boxCache = new Dictionary<int, Box>();

    /// <summary>Initializes a font from a bare CFF table.</summary>
    /// <param name="cff">The table bytes.</param>
    public CffFont(byte[] cff)
    {
        _data = cff ?? throw new ArgumentNullException(nameof(cff));
        Parse();
    }

    /// <summary>Gets the number of glyphs the font defines.</summary>
    public int GlyphCount => _charStrings.Count;

    /// <summary>
    /// Gets the font matrix, which maps charstring units to em units. Almost always
    /// 1/1000, but a face is free to say otherwise and several TeX Gyre faces do.
    /// </summary>
    public IReadOnlyList<double> FontMatrix => _fontMatrix;

    /// <summary>
    /// Returns a glyph's INK bounding box, in font design units, by running its
    /// charstring. Cached, because text layout asks repeatedly for the same few.
    /// </summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The box, empty when the glyph draws nothing.</returns>
    public Box GlyphBox(int index)
    {
        lock (_boxGate)
        {
            if (_boxCache.TryGetValue(index, out Box cached))
            {
                return cached;
            }
        }

        Box box = default;
        if (index >= 0 && index < _charStrings.Count)
        {
            CharstringRun run = new CharstringRun(this, index);
            run.Execute();
            box = run.Bounds;
        }

        lock (_boxGate)
        {
            _boxCache[index] = box;
        }

        return box;
    }

    /// <summary>
    /// Returns a glyph's outline as SVG path data, in font design units.
    /// <para>
    /// Not used to render the music fonts — those come from the SVG fonts verbatim —
    /// but it is what makes the interpreter TESTABLE: the shipped SVG fonts carry every
    /// Emmentaler glyph's outline as a ready-made oracle, so running the same font's
    /// charstrings and comparing the geometry proves the interpreter without needing a
    /// second implementation to disagree with.
    /// </para>
    /// </summary>
    /// <param name="index">The glyph index.</param>
    /// <returns>The path data.</returns>
    public string GlyphPath(int index)
    {
        if (index < 0 || index >= _charStrings.Count)
        {
            return string.Empty;
        }

        CharstringRun run = new CharstringRun(this, index) { Recording = true };
        run.Execute();
        return run.Path;
    }

    /// <summary>
    /// Traces a glyph's outline into a skyline collector.
    /// <para>
    /// The walk itself is <see cref="GlyphOutlineSkyline"/> — a port of
    /// <c>lily/freetype.cc</c>, which is why it lives in a file of its own with
    /// upstream's provenance rather than here. This is the convenience entry point.
    /// </para>
    /// </summary>
    /// <param name="skyline">The collector to trace into.</param>
    /// <param name="transform">
    /// The transform from font design units to output units.
    /// </param>
    /// <param name="index">The glyph index.</param>
    public void AddOutlineToSkyline(LazySkylinePair skyline, Transform transform, int index)
        => GlyphOutlineSkyline.AddOutline(this, skyline, transform, index);

    private void Parse()
    {
        int position = _data[2];

        position = SkipIndex(position);
        List<(int Start, int End)> topDicts = ReadIndex(ref position);
        _strings = ReadStringIndex(ref position);
        _globalSubrs = ReadIndex(ref position);

        if (topDicts.Count == 0)
        {
            return;
        }

        Dictionary<int, List<double>> top = ParseDict(topDicts[0].Start, topDicts[0].End);

        if (top.TryGetValue(1207, out List<double> matrix) && matrix.Count >= 6)
        {
            _fontMatrix = matrix.ToArray();
        }

        if (top.TryGetValue(17, out List<double> charStrings) && charStrings.Count > 0)
        {
            int cursor = (int)charStrings[charStrings.Count - 1];
            _charStrings = ReadIndex(ref cursor);
        }

        if (top.TryGetValue(18, out List<double> priv) && priv.Count >= 2)
        {
            _localSubrs = ReadPrivateSubrs((int)priv[1], (int)priv[0]);
        }

        if (top.TryGetValue(1236, out List<double> fdArray) && fdArray.Count > 0)
        {
            ReadFdArray((int)fdArray[fdArray.Count - 1]);
        }

        if (top.TryGetValue(1237, out List<double> fdSelect) && fdSelect.Count > 0)
        {
            ReadFdSelect((int)fdSelect[fdSelect.Count - 1]);
        }
    }

    private void ReadFdArray(int offset)
    {
        int cursor = offset;
        List<(int Start, int End)> dicts = ReadIndex(ref cursor);
        _fdLocalSubrs = new List<List<(int Start, int End)>>();

        foreach ((int Start, int End) entry in dicts)
        {
            Dictionary<int, List<double>> dict = ParseDict(entry.Start, entry.End);
            _fdLocalSubrs.Add(
                dict.TryGetValue(18, out List<double> priv) && priv.Count >= 2
                    ? ReadPrivateSubrs((int)priv[1], (int)priv[0])
                    : new List<(int, int)>());
        }
    }

    private void ReadFdSelect(int offset)
    {
        _fdSelect = new byte[_charStrings.Count];
        int format = _data[offset];

        if (format == 0)
        {
            for (int i = 0; i < _charStrings.Count && offset + 1 + i < _data.Length; i++)
            {
                _fdSelect[i] = _data[offset + 1 + i];
            }

            return;
        }

        if (format != 3)
        {
            return;
        }

        int ranges = ReadUInt16(offset + 1);
        int position = offset + 3;
        int first = ReadUInt16(position);
        for (int i = 0; i < ranges; i++)
        {
            byte fd = _data[position + 2];
            int next = ReadUInt16(position + 3);
            for (int glyph = first; glyph < next && glyph < _fdSelect.Length; glyph++)
            {
                _fdSelect[glyph] = fd;
            }

            first = next;
            position += 3;
        }
    }

    private List<(int Start, int End)> ReadPrivateSubrs(int offset, int size)
    {
        if (offset <= 0 || offset + size > _data.Length)
        {
            return new List<(int, int)>();
        }

        Dictionary<int, List<double>> priv = ParseDict(offset, offset + size);
        if (!priv.TryGetValue(19, out List<double> subrs) || subrs.Count == 0)
        {
            return new List<(int, int)>();
        }

        // A Private DICT's Subrs offset is relative to the Private DICT itself.
        int cursor = offset + (int)subrs[subrs.Count - 1];
        return ReadIndex(ref cursor);
    }

    internal List<(int Start, int End)> LocalSubrsFor(int glyph)
    {
        if (_fdLocalSubrs != null && _fdSelect != null
            && glyph >= 0 && glyph < _fdSelect.Length)
        {
            int fd = _fdSelect[glyph];
            if (fd < _fdLocalSubrs.Count)
            {
                return _fdLocalSubrs[fd];
            }
        }

        return _localSubrs;
    }

    internal List<(int Start, int End)> GlobalSubrs => _globalSubrs;

    internal (int Start, int End) Charstring(int index) => _charStrings[index];

    internal byte[] Data => _data;

    /// <summary>
    /// The subroutine index bias. Type 2 numbers subroutines from the MIDDLE of the
    /// table so small indices reach the commonest entries, and getting it wrong runs a
    /// valid but entirely different program.
    /// </summary>
    /// <param name="count">How many subroutines there are.</param>
    /// <returns>The bias to add to a subroutine number.</returns>
    internal static int Bias(int count)
        => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    private List<string> ReadStringIndex(ref int position)
    {
        List<string> result = new List<string>();
        foreach ((int Start, int End) entry in ReadIndex(ref position))
        {
            result.Add(Encoding.ASCII.GetString(_data, entry.Start, entry.End - entry.Start));
        }

        return result;
    }

    private List<(int Start, int End)> ReadIndex(ref int position)
    {
        List<(int Start, int End)> entries = new List<(int, int)>();
        if (position + 2 > _data.Length)
        {
            return entries;
        }

        int count = ReadUInt16(position);
        position += 2;
        if (count == 0)
        {
            return entries;
        }

        int offsetSize = _data[position++];
        int[] offsets = new int[count + 1];
        for (int i = 0; i <= count; i++)
        {
            int value = 0;
            for (int b = 0; b < offsetSize; b++)
            {
                value = (value << 8) | _data[position++];
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
        ReadIndex(ref cursor);
        return cursor;
    }

    private Dictionary<int, List<double>> ParseDict(int start, int end)
    {
        Dictionary<int, List<double>> result = new Dictionary<int, List<double>>();
        List<double> operands = new List<double>();

        int position = start;
        while (position < end && position < _data.Length)
        {
            int b0 = _data[position];

            if (b0 <= 21)
            {
                int op = b0;
                position++;
                if (b0 == 12)
                {
                    op = 1200 + _data[position];
                    position++;
                }

                result[op] = new List<double>(operands);
                operands.Clear();
            }
            else if (b0 == 28)
            {
                operands.Add((short)((_data[position + 1] << 8) | _data[position + 2]));
                position += 3;
            }
            else if (b0 == 29)
            {
                operands.Add((_data[position + 1] << 24)
                             | (_data[position + 2] << 16)
                             | (_data[position + 3] << 8)
                             | _data[position + 4]);
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
                operands.Add(((b0 - 247) * 256) + _data[position + 1] + 108);
                position += 2;
            }
            else if (b0 >= 251 && b0 <= 254)
            {
                operands.Add((-(b0 - 251) * 256) - _data[position + 1] - 108);
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

        while (!done && position < _data.Length)
        {
            int b = _data[position++];
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
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : 0.0;
    }

    private int ReadUInt16(int offset) => (_data[offset] << 8) | _data[offset + 1];
}
