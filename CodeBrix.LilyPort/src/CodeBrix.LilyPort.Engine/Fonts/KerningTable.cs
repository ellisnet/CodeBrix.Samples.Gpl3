// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace CodeBrix.LilyPort.Engine.Fonts;

/// <summary>
/// A face's kerning data: the GPOS <c>kern</c> feature's pair-positioning lookups,
/// with the legacy <c>kern</c> table as the fallback.
/// <para>
/// New-in-family. Upstream measures text by asking Pango for a SHAPED run's logical
/// rectangle, and shaping (HarfBuzz underneath) applies the font's kerning to the
/// advances — so the port's advance sum owes the same adjustment (trap 6f: when
/// upstream hands a job to a shaping library, the port owes the library's BEHAVIOUR,
/// not an arithmetic that looks equivalent). What is implemented is the part of that
/// behaviour the <c>kern</c> feature reaches:
/// </para>
/// <list type="bullet">
/// <item>Lookups are the ones the <c>kern</c> feature names for the default script,
/// applied in lookup-list order; their adjustments ACCUMULATE.</item>
/// <item>Within one lookup, subtables are tried in order and the FIRST one that
/// applies wins. A PairPos Format 1 subtable applies when its coverage holds the left
/// glyph AND its pair set holds the right glyph; a Format 2 subtable applies whenever
/// its coverage holds the left glyph — even when the class pair's value is zero,
/// exactly as HarfBuzz stops there.</item>
/// <item>Extension lookups (type 9) are unwrapped to the pair-positioning subtables
/// they carry.</item>
/// <item>The legacy <c>kern</c> table (format 0, horizontal, non-cross-stream) is
/// read ONLY when GPOS names no <c>kern</c> feature, which is HarfBuzz's own gate.</item>
/// </list>
/// <para>
/// Every vendored face that kerns at all does it with a single plain type-2 lookup of
/// Format 1 subtables, <c>ValueFormat1 = XAdvance</c>, <c>ValueFormat2 = 0</c>, and
/// names the SAME lookups from every script it declares (measured across
/// all 24 faces; the monospace quartets carry no kerning and no face carries a legacy
/// <c>kern</c> table). The wider surface here — Format 2, Extension, the legacy
/// fallback — is for any face D23's chain may one day grow, parsed by the spec rather
/// than guessed at. (It used to say "the pinned Roboto package"; there has never been
/// one — D23 amended 2026-08-17, and the chain ends at TeX Gyre.)
/// </para>
/// <para>
/// Device-table adjustments (hint-driven, per-ppem) are deliberately not applied:
/// they exist for rasterization at a specific pixel size, and the backend's output is
/// scalable SVG, where Pango applies none either.
/// </para>
/// </summary>
public sealed class KerningTable
{
    private readonly List<List<PairSubtable>> _lookups;
    private readonly Dictionary<long, double> _legacyPairs;

    private KerningTable(List<List<PairSubtable>> lookups, Dictionary<long, double> legacyPairs)
    {
        _lookups = lookups;
        _legacyPairs = legacyPairs;
    }

    /// <summary>Gets whether GPOS supplied the kerning.</summary>
    public bool HasGposKerning => _lookups.Count > 0;

    /// <summary>Gets whether the legacy <c>kern</c> table supplied the kerning.</summary>
    public bool HasLegacyKerning => _legacyPairs != null && _legacyPairs.Count > 0;

    /// <summary>
    /// Reads a face's kerning, or returns <see langword="null"/> when the face has
    /// none.
    /// </summary>
    /// <param name="reader">The face's container reader.</param>
    /// <returns>The kerning table, or <see langword="null"/>.</returns>
    public static KerningTable Read(SfntReader reader)
    {
        if (reader == null)
        {
            return null;
        }

        List<List<PairSubtable>> lookups = ReadGpos(reader.GetTable("GPOS"));
        Dictionary<long, double> legacy = null;
        if (lookups.Count == 0)
        {
            legacy = ReadLegacyKern(reader.GetTable("kern"));
        }

        if (lookups.Count == 0 && (legacy == null || legacy.Count == 0))
        {
            return null;
        }

        return new KerningTable(lookups, legacy);
    }

    /// <summary>
    /// Returns the advance adjustment between two adjacent glyphs of one run, in
    /// design units. Positive values widen; most kern pairs are negative.
    /// </summary>
    /// <param name="leftGlyph">The earlier glyph's index.</param>
    /// <param name="rightGlyph">The later glyph's index.</param>
    /// <returns>The adjustment, or 0 when the pair carries none.</returns>
    public double Adjustment(int leftGlyph, int rightGlyph)
    {
        if (_lookups.Count > 0)
        {
            double total = 0.0;
            foreach (List<PairSubtable> lookup in _lookups)
            {
                foreach (PairSubtable subtable in lookup)
                {
                    if (subtable.TryApply(leftGlyph, rightGlyph, out double value))
                    {
                        total += value;
                        break;
                    }
                }
            }

            return total;
        }

        if (_legacyPairs != null
            && _legacyPairs.TryGetValue(PairKey(leftGlyph, rightGlyph), out double legacy))
        {
            return legacy;
        }

        return 0.0;
    }

    private static long PairKey(int left, int right) => ((long)left << 32) | (uint)right;

    // ---------------------------------------------------------------- GPOS ----

    private static List<List<PairSubtable>> ReadGpos(byte[] gpos)
    {
        List<List<PairSubtable>> result = new List<List<PairSubtable>>();
        if (gpos == null || gpos.Length < 10)
        {
            return result;
        }

        int scriptList = ReadUInt16(gpos, 4);
        int featureList = ReadUInt16(gpos, 6);
        int lookupList = ReadUInt16(gpos, 8);
        if (scriptList == 0 || featureList == 0 || lookupList == 0)
        {
            return result;
        }

        List<int> featureIndices = DefaultLangSysFeatures(gpos, scriptList);
        if (featureIndices.Count == 0)
        {
            return result;
        }

        // The kern feature's lookup indices, applied in lookup-list order — HarfBuzz
        // collects a feature's lookups and walks them by ascending index.
        SortedSet<int> kernLookups = new SortedSet<int>();
        int featureCount = ReadUInt16(gpos, featureList);
        foreach (int featureIndex in featureIndices)
        {
            if (featureIndex >= featureCount)
            {
                continue;
            }

            int record = featureList + 2 + (featureIndex * 6);
            if (record + 6 > gpos.Length)
            {
                continue;
            }

            string tag = ReadTag(gpos, record);
            if (tag != "kern")
            {
                continue;
            }

            int feature = featureList + ReadUInt16(gpos, record + 4);
            if (feature + 4 > gpos.Length)
            {
                continue;
            }

            int lookupIndexCount = ReadUInt16(gpos, feature + 2);
            for (int i = 0; i < lookupIndexCount; i++)
            {
                int at = feature + 4 + (i * 2);
                if (at + 2 <= gpos.Length)
                {
                    kernLookups.Add(ReadUInt16(gpos, at));
                }
            }
        }

        int lookupCount = ReadUInt16(gpos, lookupList);
        foreach (int lookupIndex in kernLookups)
        {
            if (lookupIndex >= lookupCount)
            {
                continue;
            }

            int lookup = lookupList + ReadUInt16(gpos, lookupList + 2 + (lookupIndex * 2));
            List<PairSubtable> subtables = ReadLookup(gpos, lookup);
            if (subtables.Count > 0)
            {
                result.Add(subtables);
            }
        }

        return result;
    }

    private static List<int> DefaultLangSysFeatures(byte[] gpos, int scriptList)
    {
        // The script: DFLT when present, then latn, then whatever comes first — and
        // its default LangSys, falling back to the first LangSys record. Measured
        // Measured: every vendored face names the SAME kern lookups from every
        // script it declares, so the choice cannot change an answer today.
        List<int> result = new List<int>();
        if (scriptList + 2 > gpos.Length)
        {
            return result;
        }

        int scriptCount = ReadUInt16(gpos, scriptList);
        int chosen = 0;
        int chosenRank = -1;
        for (int i = 0; i < scriptCount; i++)
        {
            int record = scriptList + 2 + (i * 6);
            if (record + 6 > gpos.Length)
            {
                break;
            }

            string tag = ReadTag(gpos, record);
            int rank = tag == "DFLT" ? 2 : tag == "latn" ? 1 : 0;
            if (rank > chosenRank)
            {
                chosenRank = rank;
                chosen = scriptList + ReadUInt16(gpos, record + 4);
            }
        }

        if (chosenRank < 0 || chosen + 4 > gpos.Length)
        {
            return result;
        }

        int defaultLangSys = ReadUInt16(gpos, chosen);
        int langSys;
        if (defaultLangSys != 0)
        {
            langSys = chosen + defaultLangSys;
        }
        else
        {
            int langSysCount = ReadUInt16(gpos, chosen + 2);
            if (langSysCount == 0 || chosen + 4 + 6 > gpos.Length)
            {
                return result;
            }

            langSys = chosen + ReadUInt16(gpos, chosen + 4 + 4);
        }

        if (langSys + 6 > gpos.Length)
        {
            return result;
        }

        int featureIndexCount = ReadUInt16(gpos, langSys + 4);
        for (int i = 0; i < featureIndexCount; i++)
        {
            int at = langSys + 6 + (i * 2);
            if (at + 2 <= gpos.Length)
            {
                result.Add(ReadUInt16(gpos, at));
            }
        }

        return result;
    }

    private static List<PairSubtable> ReadLookup(byte[] gpos, int lookup)
    {
        List<PairSubtable> result = new List<PairSubtable>();
        if (lookup + 6 > gpos.Length)
        {
            return result;
        }

        int lookupType = ReadUInt16(gpos, lookup);
        int subTableCount = ReadUInt16(gpos, lookup + 4);

        for (int i = 0; i < subTableCount; i++)
        {
            int offsetAt = lookup + 6 + (i * 2);
            if (offsetAt + 2 > gpos.Length)
            {
                break;
            }

            int subtable = lookup + ReadUInt16(gpos, offsetAt);
            int type = lookupType;

            // An Extension lookup carries a 32-bit offset to the real subtable so a
            // large font can push positioning data past the 16-bit horizon.
            if (type == 9)
            {
                if (subtable + 8 > gpos.Length)
                {
                    continue;
                }

                type = ReadUInt16(gpos, subtable + 2);
                subtable = subtable + (int)ReadUInt32(gpos, subtable + 4);
            }

            if (type != 2)
            {
                continue;
            }

            PairSubtable parsed = ReadPairSubtable(gpos, subtable);
            if (parsed != null)
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    private static PairSubtable ReadPairSubtable(byte[] gpos, int subtable)
    {
        if (subtable + 10 > gpos.Length)
        {
            return null;
        }

        int format = ReadUInt16(gpos, subtable);
        int coverageOffset = subtable + ReadUInt16(gpos, subtable + 2);
        int valueFormat1 = ReadUInt16(gpos, subtable + 4);
        int valueFormat2 = ReadUInt16(gpos, subtable + 6);
        int size1 = ValueRecordSize(valueFormat1);
        int size2 = ValueRecordSize(valueFormat2);

        if (format == 1)
        {
            Dictionary<int, int> coverage = ReadCoverage(gpos, coverageOffset);
            if (coverage == null)
            {
                return null;
            }

            int pairSetCount = ReadUInt16(gpos, subtable + 8);
            Dictionary<int, Dictionary<int, double>> pairSets
                = new Dictionary<int, Dictionary<int, double>>();

            foreach (KeyValuePair<int, int> entry in coverage)
            {
                int setIndex = entry.Value;
                if (setIndex >= pairSetCount)
                {
                    continue;
                }

                int offsetAt = subtable + 10 + (setIndex * 2);
                if (offsetAt + 2 > gpos.Length)
                {
                    continue;
                }

                int pairSet = subtable + ReadUInt16(gpos, offsetAt);
                if (pairSet + 2 > gpos.Length)
                {
                    continue;
                }

                int pairCount = ReadUInt16(gpos, pairSet);
                Dictionary<int, double> pairs = new Dictionary<int, double>();
                int record = pairSet + 2;
                int recordSize = 2 + size1 + size2;
                for (int i = 0; i < pairCount; i++, record += recordSize)
                {
                    if (record + recordSize > gpos.Length)
                    {
                        break;
                    }

                    int secondGlyph = ReadUInt16(gpos, record);
                    double value = XAdvance(gpos, record + 2, valueFormat1)
                                   + XAdvance(gpos, record + 2 + size1, valueFormat2);
                    pairs[secondGlyph] = value;
                }

                pairSets[entry.Key] = pairs;
            }

            return new PairPosFormat1(pairSets);
        }

        if (format == 2)
        {
            if (subtable + 16 > gpos.Length)
            {
                return null;
            }

            Dictionary<int, int> coverage = ReadCoverage(gpos, coverageOffset);
            if (coverage == null)
            {
                return null;
            }

            Dictionary<int, int> classDef1 = ReadClassDef(gpos, subtable + ReadUInt16(gpos, subtable + 8));
            Dictionary<int, int> classDef2 = ReadClassDef(gpos, subtable + ReadUInt16(gpos, subtable + 10));
            int class1Count = ReadUInt16(gpos, subtable + 12);
            int class2Count = ReadUInt16(gpos, subtable + 14);

            double[,] matrix = new double[class1Count, class2Count];
            int at = subtable + 16;
            int cellSize = size1 + size2;
            for (int c1 = 0; c1 < class1Count; c1++)
            {
                for (int c2 = 0; c2 < class2Count; c2++, at += cellSize)
                {
                    if (at + cellSize > gpos.Length)
                    {
                        break;
                    }

                    matrix[c1, c2] = XAdvance(gpos, at, valueFormat1)
                                     + XAdvance(gpos, at + size1, valueFormat2);
                }
            }

            return new PairPosFormat2(
                new HashSet<int>(coverage.Keys), classDef1, classDef2, matrix);
        }

        return null;
    }

    private static int ValueRecordSize(int valueFormat)
    {
        int size = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((valueFormat & (1 << bit)) != 0)
            {
                size += 2;
            }
        }

        return size;
    }

    private static double XAdvance(byte[] gpos, int record, int valueFormat)
    {
        // A value record lays its fields out in bit order: XPlacement (0x1),
        // YPlacement (0x2), XAdvance (0x4), YAdvance (0x8), then the four device
        // offsets. Only XAdvance moves the pen of a horizontal run.
        if ((valueFormat & 0x4) == 0)
        {
            return 0.0;
        }

        int at = record;
        if ((valueFormat & 0x1) != 0)
        {
            at += 2;
        }

        if ((valueFormat & 0x2) != 0)
        {
            at += 2;
        }

        if (at + 2 > gpos.Length)
        {
            return 0.0;
        }

        return (short)ReadUInt16(gpos, at);
    }

    private static Dictionary<int, int> ReadCoverage(byte[] gpos, int coverage)
    {
        if (coverage + 4 > gpos.Length)
        {
            return null;
        }

        Dictionary<int, int> result = new Dictionary<int, int>();
        int format = ReadUInt16(gpos, coverage);

        if (format == 1)
        {
            int glyphCount = ReadUInt16(gpos, coverage + 2);
            for (int i = 0; i < glyphCount; i++)
            {
                int at = coverage + 4 + (i * 2);
                if (at + 2 > gpos.Length)
                {
                    break;
                }

                result[ReadUInt16(gpos, at)] = i;
            }

            return result;
        }

        if (format == 2)
        {
            int rangeCount = ReadUInt16(gpos, coverage + 2);
            for (int i = 0; i < rangeCount; i++)
            {
                int at = coverage + 4 + (i * 6);
                if (at + 6 > gpos.Length)
                {
                    break;
                }

                int start = ReadUInt16(gpos, at);
                int end = ReadUInt16(gpos, at + 2);
                int startIndex = ReadUInt16(gpos, at + 4);
                for (int glyph = start; glyph <= end; glyph++)
                {
                    result[glyph] = startIndex + (glyph - start);
                }
            }

            return result;
        }

        return null;
    }

    private static Dictionary<int, int> ReadClassDef(byte[] gpos, int classDef)
    {
        // Any glyph the table does not mention is class 0, so only the mentioned
        // ones are stored.
        Dictionary<int, int> result = new Dictionary<int, int>();
        if (classDef + 4 > gpos.Length)
        {
            return result;
        }

        int format = ReadUInt16(gpos, classDef);
        if (format == 1)
        {
            int startGlyph = ReadUInt16(gpos, classDef + 2);
            int glyphCount = ReadUInt16(gpos, classDef + 4);
            for (int i = 0; i < glyphCount; i++)
            {
                int at = classDef + 6 + (i * 2);
                if (at + 2 > gpos.Length)
                {
                    break;
                }

                int value = ReadUInt16(gpos, at);
                if (value != 0)
                {
                    result[startGlyph + i] = value;
                }
            }
        }
        else if (format == 2)
        {
            int rangeCount = ReadUInt16(gpos, classDef + 2);
            for (int i = 0; i < rangeCount; i++)
            {
                int at = classDef + 4 + (i * 6);
                if (at + 6 > gpos.Length)
                {
                    break;
                }

                int start = ReadUInt16(gpos, at);
                int end = ReadUInt16(gpos, at + 2);
                int value = ReadUInt16(gpos, at + 4);
                if (value != 0)
                {
                    for (int glyph = start; glyph <= end; glyph++)
                    {
                        result[glyph] = value;
                    }
                }
            }
        }

        return result;
    }

    // --------------------------------------------------------- legacy kern ----

    private static Dictionary<long, double> ReadLegacyKern(byte[] kern)
    {
        // The 16-bit-version Windows layout only; the Apple 32-bit-version layout
        // never appears in an OpenType text face and is skipped rather than guessed
        // at. Subtable format 0, horizontal, non-cross-stream, non-minimum; a
        // subtable with the override bit REPLACES earlier values for its pairs,
        // everything else accumulates — the spec's own composition rule.
        if (kern == null || kern.Length < 4 || ReadUInt16(kern, 0) != 0)
        {
            return null;
        }

        Dictionary<long, double> result = new Dictionary<long, double>();
        int tableCount = ReadUInt16(kern, 2);
        int position = 4;

        for (int t = 0; t < tableCount && position + 6 <= kern.Length; t++)
        {
            int length = ReadUInt16(kern, position + 2);
            int coverage = ReadUInt16(kern, position + 4);
            int format = coverage >> 8;
            bool horizontal = (coverage & 0x1) != 0;
            bool minimum = (coverage & 0x2) != 0;
            bool crossStream = (coverage & 0x4) != 0;
            bool over = (coverage & 0x8) != 0;

            if (format == 0 && horizontal && !minimum && !crossStream)
            {
                int pairsAt = position + 6;
                if (pairsAt + 8 <= kern.Length)
                {
                    int pairCount = ReadUInt16(kern, pairsAt);
                    int record = pairsAt + 8;
                    for (int i = 0; i < pairCount && record + 6 <= kern.Length; i++, record += 6)
                    {
                        long key = PairKey(ReadUInt16(kern, record), ReadUInt16(kern, record + 2));
                        double value = (short)ReadUInt16(kern, record + 4);
                        if (over || !result.TryGetValue(key, out double existing))
                        {
                            result[key] = value;
                        }
                        else
                        {
                            result[key] = existing + value;
                        }
                    }
                }
            }

            position += length < 6 ? 6 : length;
        }

        return result;
    }

    // ------------------------------------------------------------- helpers ----

    private static int ReadUInt16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

    private static uint ReadUInt32(byte[] data, int offset)
        => ((uint)data[offset] << 24)
           | ((uint)data[offset + 1] << 16)
           | ((uint)data[offset + 2] << 8)
           | data[offset + 3];

    private static string ReadTag(byte[] data, int offset)
        => new string(new[]
        {
            (char)data[offset],
            (char)data[offset + 1],
            (char)data[offset + 2],
            (char)data[offset + 3],
        });

    /// <summary>One positioning subtable of a pair lookup.</summary>
    private abstract class PairSubtable
    {
        /// <summary>
        /// Tries to apply the subtable to a glyph pair. A subtable that applies stops
        /// its lookup's subtable walk even when its adjustment is zero.
        /// </summary>
        /// <param name="left">The earlier glyph.</param>
        /// <param name="right">The later glyph.</param>
        /// <param name="adjustment">The advance adjustment, in design units.</param>
        /// <returns><see langword="true"/> when the subtable applied.</returns>
        public abstract bool TryApply(int left, int right, out double adjustment);
    }

    /// <summary>Format 1: explicit second-glyph pair sets per covered first glyph.</summary>
    private sealed class PairPosFormat1 : PairSubtable
    {
        private readonly Dictionary<int, Dictionary<int, double>> _pairSets;

        internal PairPosFormat1(Dictionary<int, Dictionary<int, double>> pairSets)
            => _pairSets = pairSets;

        /// <inheritdoc/>
        public override bool TryApply(int left, int right, out double adjustment)
        {
            adjustment = 0.0;

            // Covered but with no record for THIS second glyph does NOT apply — the
            // walk moves on to the next subtable, which is HarfBuzz's rule for
            // Format 1 and the opposite of Format 2's.
            return _pairSets.TryGetValue(left, out Dictionary<int, double> pairs)
                   && pairs.TryGetValue(right, out adjustment);
        }
    }

    /// <summary>Format 2: a class-pair matrix.</summary>
    private sealed class PairPosFormat2 : PairSubtable
    {
        private readonly HashSet<int> _coverage;
        private readonly Dictionary<int, int> _classDef1;
        private readonly Dictionary<int, int> _classDef2;
        private readonly double[,] _matrix;

        internal PairPosFormat2(
            HashSet<int> coverage,
            Dictionary<int, int> classDef1,
            Dictionary<int, int> classDef2,
            double[,] matrix)
        {
            _coverage = coverage;
            _classDef1 = classDef1;
            _classDef2 = classDef2;
            _matrix = matrix;
        }

        /// <inheritdoc/>
        public override bool TryApply(int left, int right, out double adjustment)
        {
            adjustment = 0.0;
            if (!_coverage.Contains(left))
            {
                return false;
            }

            int class1 = _classDef1.TryGetValue(left, out int c1) ? c1 : 0;
            int class2 = _classDef2.TryGetValue(right, out int c2) ? c2 : 0;
            if (class1 >= _matrix.GetLength(0) || class2 >= _matrix.GetLength(1))
            {
                return false;
            }

            // A zero cell still APPLIES — coverage decided, not the value.
            adjustment = _matrix[class1, class2];
            return true;
        }
    }
}
