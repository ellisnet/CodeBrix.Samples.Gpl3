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
/// A face's glyph substitutions: the GSUB features a run asks for, resolved to lookups
/// and applied to the run's glyph indices.
/// <para>
/// New-in-family, and the sibling of <see cref="KerningTable"/>. Upstream reaches this
/// behaviour through Pango: <c>Text_interface::interpret_string</c> reads the
/// <c>font-features</c> property chain, joins the list with commas, and hands the string
/// to <c>pango_attr_font_features_new</c>, after which HarfBuzz applies the named
/// features to the shaped run. So the port owes the LIBRARY'S BEHAVIOUR here, not an
/// arithmetic that looks equivalent (trap 15) — and until this existed the port applied
/// no substitution at all, which is why every grob that asks for <c>ss01</c> drew the
/// plain digits where the oracle draws the <c>fattened</c> ones.
/// </para>
/// <para>
/// What the features are FOR is worth stating, because the names carry none of it:
/// Emmentaler's <c>ss01</c> is the stylistic set that swaps each digit for its
/// <c>fattened</c> variant, <c>tnum</c> swaps it for the <c>fixedwidth</c> one,
/// <c>cv47</c> swaps <c>four</c> and <c>seven</c> for their <c>.alt</c> shapes, and
/// <c>dlig</c> forms the slashed figured-bass digits. They COMPOSE — a BassFigure asks
/// for all three of <c>tnum</c>, <c>cv47</c> and <c>ss01</c> and gets
/// <c>fattened.fixedwidth.four.alt</c> — and they compose correctly only because
/// lookups run in LOOKUP-LIST order rather than in the order the features were named,
/// which is HarfBuzz's rule and is reproduced here.
/// </para>
/// <para>
/// What is implemented is the part of HarfBuzz's behaviour these fonts reach:
/// </para>
/// <list type="bullet">
/// <item>The features of the DEFAULT language system of the chosen script — <c>latn</c>,
/// then <c>DFLT</c>, then whatever comes first. NOT the order <see cref="KerningTable"/>
/// uses: every vendored face names the same <c>kern</c> lookups from every script, and
/// twelve of them name <c>liga</c> from <c>latn</c> alone.</item>
/// <item>The enabled set is HarfBuzz's default-on GSUB features, plus the tags the run
/// names, minus the tags it names with a leading <c>-</c>. LilyPond writes both:
/// <c>\typewriter</c> asks for <c>-liga</c>.</item>
/// <item>Every enabled feature's lookups are collected and applied ONCE EACH in
/// ascending lookup-list index — not once per feature — so a lookup two features both
/// name does not run twice.</item>
/// <item>Lookup types 1 (single), 2 (multiple), 3 (alternate) and 4 (ligature), plus
/// type 7 (extension) unwrapped to the type it carries. Within one lookup, subtables
/// are tried in order and the FIRST that applies wins, exactly as HarfBuzz stops
/// there.</item>
/// </list>
/// <para>
/// The CONTEXTUAL types (5, 6 and 8) are deliberately absent, and measured rather than
/// assumed: across all 24 vendored text faces and all 9 Emmentaler faces, no feature
/// that is default-on or that any LilyPond grob or markup command asks for names a
/// contextual lookup. The only contextual lookups present at all belong to <c>ordn</c>
/// and <c>frac</c>, which nothing enables. A face whose enabled features need one would
/// silently get less substitution than Pango gives it, so the limit is recorded in
/// PORT-COVERAGE rather than left to be rediscovered.
/// </para>
/// </summary>
public sealed class SubstitutionTable
{
    // HarfBuzz turns these GSUB features on for a horizontal run without being asked:
    // the common set (ccmp, locl, rlig) and the horizontal set (calt, clig, liga,
    // rclt). Its other defaults -- abvm, blwm, mark, mkmk, curs, dist, kern -- are all
    // GPOS and reach this table never; kern is KerningTable's business.
    private static readonly string[] DefaultOnFeatures =
    {
        "ccmp", "locl", "rlig", "calt", "clig", "liga", "rclt",
    };

    private readonly Dictionary<string, List<int>> _featureLookups;
    private readonly Dictionary<int, List<Subtable>> _lookups;

    private SubstitutionTable(
        Dictionary<string, List<int>> featureLookups, Dictionary<int, List<Subtable>> lookups)
    {
        _featureLookups = featureLookups;
        _lookups = lookups;
    }

    /// <summary>
    /// Reads a face's substitutions, or returns <see langword="null"/> when the face
    /// declares none this table can act on.
    /// </summary>
    /// <param name="reader">The face's container reader.</param>
    /// <returns>The substitution table, or <see langword="null"/>.</returns>
    public static SubstitutionTable Read(SfntReader reader)
    {
        byte[] gsub = reader?.GetTable("GSUB");
        if (gsub == null || gsub.Length < 10)
        {
            return null;
        }

        int scriptList = ReadUInt16(gsub, 4);
        int featureList = ReadUInt16(gsub, 6);
        int lookupList = ReadUInt16(gsub, 8);
        if (scriptList == 0 || featureList == 0 || lookupList == 0)
        {
            return null;
        }

        List<int> featureIndices = DefaultLangSysFeatures(gsub, scriptList);
        if (featureIndices.Count == 0)
        {
            return null;
        }

        Dictionary<string, List<int>> featureLookups = new Dictionary<string, List<int>>(
            StringComparer.Ordinal);
        int featureCount = ReadUInt16(gsub, featureList);
        foreach (int featureIndex in featureIndices)
        {
            if (featureIndex >= featureCount)
            {
                continue;
            }

            int record = featureList + 2 + (featureIndex * 6);
            if (record + 6 > gsub.Length)
            {
                continue;
            }

            string tag = ReadTag(gsub, record);
            int feature = featureList + ReadUInt16(gsub, record + 4);
            if (feature + 4 > gsub.Length)
            {
                continue;
            }

            if (!featureLookups.TryGetValue(tag, out List<int> indices))
            {
                indices = new List<int>();
                featureLookups[tag] = indices;
            }

            int lookupIndexCount = ReadUInt16(gsub, feature + 2);
            for (int i = 0; i < lookupIndexCount; i++)
            {
                int at = feature + 4 + (i * 2);
                if (at + 2 <= gsub.Length)
                {
                    indices.Add(ReadUInt16(gsub, at));
                }
            }
        }

        if (featureLookups.Count == 0)
        {
            return null;
        }

        Dictionary<int, List<Subtable>> lookups = ReadLookups(gsub, lookupList);
        return lookups.Count == 0
            ? null
            : new SubstitutionTable(featureLookups, lookups);
    }

    /// <summary>
    /// Applies the substitutions a run asks for, in place.
    /// </summary>
    /// <param name="glyphs">The run's glyph indices; rewritten in place.</param>
    /// <param name="features">
    /// The comma-separated feature string, as upstream builds it from the
    /// <c>font-features</c> property. A <see langword="null"/> or empty string still
    /// applies the default-on features, because HarfBuzz does.
    /// </param>
    /// <returns>Whether anything changed.</returns>
    public bool Apply(List<int> glyphs, string features)
    {
        if (glyphs == null || glyphs.Count == 0)
        {
            return false;
        }

        SortedSet<int> indices = EnabledLookups(features);
        if (indices.Count == 0)
        {
            return false;
        }

        bool changed = false;
        foreach (int index in indices)
        {
            if (_lookups.TryGetValue(index, out List<Subtable> subtables))
            {
                changed |= ApplyLookup(subtables, glyphs);
            }
        }

        return changed;
    }

    /// <summary>
    /// Returns whether the face declares a feature, which is what lets a caller tell
    /// "the run asked for something this face does not have" from "the run asked for
    /// nothing".
    /// </summary>
    /// <param name="tag">The four-letter feature tag.</param>
    /// <returns>Whether the default language system names it.</returns>
    public bool HasFeature(string tag) => tag != null && _featureLookups.ContainsKey(tag);

    // The enabled lookups, in ascending lookup-list index. A lookup named by two
    // enabled features appears ONCE: HarfBuzz collects lookups per stage and walks them
    // by index, so naming tnum and ss01 together does not run a shared lookup twice.
    private SortedSet<int> EnabledLookups(string features)
    {
        HashSet<string> enabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (string tag in DefaultOnFeatures)
        {
            if (_featureLookups.ContainsKey(tag))
            {
                enabled.Add(tag);
            }
        }

        if (!string.IsNullOrEmpty(features))
        {
            foreach (string entry in features.Split(','))
            {
                string tag = entry.Trim();
                if (tag.Length == 0)
                {
                    continue;
                }

                bool off = tag[0] == '-';
                if (off || tag[0] == '+')
                {
                    tag = tag.Substring(1);
                }

                if (off)
                {
                    enabled.Remove(tag);
                }
                else if (_featureLookups.ContainsKey(tag))
                {
                    enabled.Add(tag);
                }
            }
        }

        SortedSet<int> result = new SortedSet<int>();
        foreach (string tag in enabled)
        {
            foreach (int index in _featureLookups[tag])
            {
                result.Add(index);
            }
        }

        return result;
    }

    private static bool ApplyLookup(List<Subtable> subtables, List<int> glyphs)
    {
        bool changed = false;
        for (int at = 0; at < glyphs.Count;)
        {
            int consumed = 0;
            foreach (Subtable subtable in subtables)
            {
                consumed = subtable.TryApply(glyphs, at);
                if (consumed > 0)
                {
                    break;
                }
            }

            if (consumed > 0)
            {
                changed = true;
                at += consumed;
            }
            else
            {
                at++;
            }
        }

        return changed;
    }

    // ---------------------------------------------------------------- GSUB ----

    private static List<int> DefaultLangSysFeatures(byte[] gsub, int scriptList)
    {
        // The script: latn when present, then DFLT, then whatever comes first — and its
        // default LangSys, falling back to the first LangSys record.
        //
        //was previously: DFLT first, copied from KerningTable, whose comment records
        // that every vendored face names the SAME kern lookups from every script. That
        // measurement does NOT carry over to GSUB, and the counterexample is measured:
        // across the 24 vendored text faces, the twelve URW ones (C059, NimbusSans,
        // NimbusMonoPS) name `liga' from `latn' AND FROM NO OTHER SCRIPT, so preferring
        // DFLT left it inert for every Latin text run in the corpus -- no f-ligature
        // ever formed, and \typewriter's `-liga' was inert with it. Pango itemizes
        // Latin text under `latn', which is the script HarfBuzz then selects.
        //
        // The blast radius is measured rather than assumed: the twelve TeX Gyre faces
        // name the same features from every script, so they cannot move, and all nine
        // Emmentaler faces declare ONLY `DFLT', so the music font -- and with it the
        // `ss01'/`cv47'/`tnum' composition -- cannot move either.
        List<int> result = new List<int>();
        if (scriptList + 2 > gsub.Length)
        {
            return result;
        }

        int scriptCount = ReadUInt16(gsub, scriptList);
        int chosen = 0;
        int chosenRank = -1;
        for (int i = 0; i < scriptCount; i++)
        {
            int record = scriptList + 2 + (i * 6);
            if (record + 6 > gsub.Length)
            {
                break;
            }

            string tag = ReadTag(gsub, record);
            int rank = tag == "latn" ? 2 : tag == "DFLT" ? 1 : 0;
            if (rank > chosenRank)
            {
                chosenRank = rank;
                chosen = scriptList + ReadUInt16(gsub, record + 4);
            }
        }

        if (chosenRank < 0 || chosen + 4 > gsub.Length)
        {
            return result;
        }

        int defaultLangSys = ReadUInt16(gsub, chosen);
        int langSys;
        if (defaultLangSys != 0)
        {
            langSys = chosen + defaultLangSys;
        }
        else
        {
            int langSysCount = ReadUInt16(gsub, chosen + 2);
            if (langSysCount == 0 || chosen + 4 + 6 > gsub.Length)
            {
                return result;
            }

            langSys = chosen + ReadUInt16(gsub, chosen + 4 + 4);
        }

        if (langSys + 6 > gsub.Length)
        {
            return result;
        }

        int featureIndexCount = ReadUInt16(gsub, langSys + 4);
        for (int i = 0; i < featureIndexCount; i++)
        {
            int at = langSys + 6 + (i * 2);
            if (at + 2 <= gsub.Length)
            {
                result.Add(ReadUInt16(gsub, at));
            }
        }

        return result;
    }

    private static Dictionary<int, List<Subtable>> ReadLookups(byte[] gsub, int lookupList)
    {
        Dictionary<int, List<Subtable>> result = new Dictionary<int, List<Subtable>>();
        if (lookupList + 2 > gsub.Length)
        {
            return result;
        }

        int lookupCount = ReadUInt16(gsub, lookupList);
        for (int index = 0; index < lookupCount; index++)
        {
            int offsetAt = lookupList + 2 + (index * 2);
            if (offsetAt + 2 > gsub.Length)
            {
                break;
            }

            int lookup = lookupList + ReadUInt16(gsub, offsetAt);
            if (lookup + 6 > gsub.Length)
            {
                continue;
            }

            int lookupType = ReadUInt16(gsub, lookup);
            int subTableCount = ReadUInt16(gsub, lookup + 4);
            List<Subtable> subtables = new List<Subtable>();

            for (int i = 0; i < subTableCount; i++)
            {
                int at = lookup + 6 + (i * 2);
                if (at + 2 > gsub.Length)
                {
                    break;
                }

                int subtable = lookup + ReadUInt16(gsub, at);
                int type = lookupType;

                // An Extension lookup carries a 32-bit offset to the real subtable so a
                // large font can push substitution data past the 16-bit horizon.
                if (type == 7)
                {
                    if (subtable + 8 > gsub.Length)
                    {
                        continue;
                    }

                    type = ReadUInt16(gsub, subtable + 2);
                    subtable = subtable + (int)ReadUInt32(gsub, subtable + 4);
                }

                Subtable parsed = ReadSubtable(gsub, type, subtable);
                if (parsed != null)
                {
                    subtables.Add(parsed);
                }
            }

            if (subtables.Count > 0)
            {
                result[index] = subtables;
            }
        }

        return result;
    }

    private static Subtable ReadSubtable(byte[] gsub, int type, int subtable)
    {
        if (subtable + 4 > gsub.Length)
        {
            return null;
        }

        int format = ReadUInt16(gsub, subtable);
        Dictionary<int, int> coverage = ReadCoverage(gsub, subtable + ReadUInt16(gsub, subtable + 2));
        if (coverage == null || coverage.Count == 0)
        {
            return null;
        }

        switch (type)
        {
            case 1:
                return ReadSingle(gsub, subtable, format, coverage);
            case 2:
            case 3:
                return ReadSetIndexed(gsub, subtable, format, coverage, type == 3);
            case 4:
                return ReadLigature(gsub, subtable, format, coverage);
            default:
                // Contextual (5, 6, 8) and reverse-chaining lookups. See the class
                // comment: nothing LilyPond enables reaches one in any vendored face.
                return null;
        }
    }

    private static Subtable ReadSingle(
        byte[] gsub, int subtable, int format, Dictionary<int, int> coverage)
    {
        Dictionary<int, int> map = new Dictionary<int, int>();

        if (format == 1)
        {
            if (subtable + 6 > gsub.Length)
            {
                return null;
            }

            short delta = (short)ReadUInt16(gsub, subtable + 4);
            foreach (KeyValuePair<int, int> entry in coverage)
            {
                // The spec adds the delta modulo 65536, so a negative delta on a low
                // glyph wraps rather than going out of range.
                map[entry.Key] = (entry.Key + delta) & 0xFFFF;
            }
        }
        else if (format == 2)
        {
            if (subtable + 6 > gsub.Length)
            {
                return null;
            }

            int glyphCount = ReadUInt16(gsub, subtable + 4);
            foreach (KeyValuePair<int, int> entry in coverage)
            {
                int at = subtable + 6 + (entry.Value * 2);
                if (entry.Value < glyphCount && at + 2 <= gsub.Length)
                {
                    map[entry.Key] = ReadUInt16(gsub, at);
                }
            }
        }
        else
        {
            return null;
        }

        return map.Count > 0 ? new SingleSubtable(map) : null;
    }

    // Multiple (type 2) and Alternate (type 3) share a layout: coverage, a count, and
    // one offset per covered glyph to a counted array of glyph ids. They differ only in
    // what the array MEANS -- a multiple substitution writes the whole sequence out, an
    // alternate substitution picks one of them. HarfBuzz picks the FIRST when the
    // feature is simply switched on, which is the only way LilyPond ever switches one
    // on: `font-features` carries tags, never tag-value pairs.
    private static Subtable ReadSetIndexed(
        byte[] gsub, int subtable, int format, Dictionary<int, int> coverage, bool alternate)
    {
        if (format != 1 || subtable + 6 > gsub.Length)
        {
            return null;
        }

        int setCount = ReadUInt16(gsub, subtable + 4);
        Dictionary<int, int[]> map = new Dictionary<int, int[]>();

        foreach (KeyValuePair<int, int> entry in coverage)
        {
            int offsetAt = subtable + 6 + (entry.Value * 2);
            if (entry.Value >= setCount || offsetAt + 2 > gsub.Length)
            {
                continue;
            }

            int set = subtable + ReadUInt16(gsub, offsetAt);
            if (set + 2 > gsub.Length)
            {
                continue;
            }

            int count = ReadUInt16(gsub, set);
            if (count == 0)
            {
                continue;
            }

            if (alternate)
            {
                if (set + 4 <= gsub.Length)
                {
                    map[entry.Key] = new[] { ReadUInt16(gsub, set + 2) };
                }

                continue;
            }

            List<int> sequence = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int at = set + 2 + (i * 2);
                if (at + 2 > gsub.Length)
                {
                    break;
                }

                sequence.Add(ReadUInt16(gsub, at));
            }

            if (sequence.Count > 0)
            {
                map[entry.Key] = sequence.ToArray();
            }
        }

        return map.Count > 0 ? new SequenceSubtable(map) : null;
    }

    private static Subtable ReadLigature(
        byte[] gsub, int subtable, int format, Dictionary<int, int> coverage)
    {
        if (format != 1 || subtable + 6 > gsub.Length)
        {
            return null;
        }

        int setCount = ReadUInt16(gsub, subtable + 4);
        Dictionary<int, List<Ligature>> map = new Dictionary<int, List<Ligature>>();

        foreach (KeyValuePair<int, int> entry in coverage)
        {
            int offsetAt = subtable + 6 + (entry.Value * 2);
            if (entry.Value >= setCount || offsetAt + 2 > gsub.Length)
            {
                continue;
            }

            int set = subtable + ReadUInt16(gsub, offsetAt);
            if (set + 2 > gsub.Length)
            {
                continue;
            }

            int ligatureCount = ReadUInt16(gsub, set);
            List<Ligature> ligatures = new List<Ligature>(ligatureCount);
            for (int i = 0; i < ligatureCount; i++)
            {
                int at = set + 2 + (i * 2);
                if (at + 2 > gsub.Length)
                {
                    break;
                }

                int ligature = set + ReadUInt16(gsub, at);
                if (ligature + 4 > gsub.Length)
                {
                    continue;
                }

                int glyph = ReadUInt16(gsub, ligature);
                int componentCount = ReadUInt16(gsub, ligature + 2);

                // componentCount counts the FIRST glyph too, and the first glyph is the
                // one coverage matched, so only the remainder is listed.
                int[] rest = new int[Math.Max(0, componentCount - 1)];
                bool complete = true;
                for (int c = 0; c < rest.Length; c++)
                {
                    int componentAt = ligature + 4 + (c * 2);
                    if (componentAt + 2 > gsub.Length)
                    {
                        complete = false;
                        break;
                    }

                    rest[c] = ReadUInt16(gsub, componentAt);
                }

                if (complete)
                {
                    ligatures.Add(new Ligature(glyph, rest));
                }
            }

            if (ligatures.Count > 0)
            {
                map[entry.Key] = ligatures;
            }
        }

        return map.Count > 0 ? new LigatureSubtable(map) : null;
    }

    private static Dictionary<int, int> ReadCoverage(byte[] gsub, int coverage)
    {
        if (coverage + 4 > gsub.Length)
        {
            return null;
        }

        Dictionary<int, int> result = new Dictionary<int, int>();
        int format = ReadUInt16(gsub, coverage);

        if (format == 1)
        {
            int glyphCount = ReadUInt16(gsub, coverage + 2);
            for (int i = 0; i < glyphCount; i++)
            {
                int at = coverage + 4 + (i * 2);
                if (at + 2 > gsub.Length)
                {
                    break;
                }

                result[ReadUInt16(gsub, at)] = i;
            }

            return result;
        }

        if (format == 2)
        {
            int rangeCount = ReadUInt16(gsub, coverage + 2);
            for (int i = 0; i < rangeCount; i++)
            {
                int at = coverage + 4 + (i * 6);
                if (at + 6 > gsub.Length)
                {
                    break;
                }

                int start = ReadUInt16(gsub, at);
                int end = ReadUInt16(gsub, at + 2);
                int startIndex = ReadUInt16(gsub, at + 4);
                for (int glyph = start; glyph <= end; glyph++)
                {
                    result[glyph] = startIndex + (glyph - start);
                }
            }

            return result;
        }

        return null;
    }

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

    // One parsed subtable. TryApply answers how many glyphs of the OUTPUT the match
    // consumed, or zero when the subtable does not apply at that position.
    private abstract class Subtable
    {
        public abstract int TryApply(List<int> glyphs, int at);
    }

    private sealed class SingleSubtable : Subtable
    {
        private readonly Dictionary<int, int> _map;

        public SingleSubtable(Dictionary<int, int> map) => _map = map;

        public override int TryApply(List<int> glyphs, int at)
        {
            if (!_map.TryGetValue(glyphs[at], out int replacement))
            {
                return 0;
            }

            glyphs[at] = replacement;
            return 1;
        }
    }

    private sealed class SequenceSubtable : Subtable
    {
        private readonly Dictionary<int, int[]> _map;

        public SequenceSubtable(Dictionary<int, int[]> map) => _map = map;

        public override int TryApply(List<int> glyphs, int at)
        {
            if (!_map.TryGetValue(glyphs[at], out int[] sequence))
            {
                return 0;
            }

            glyphs[at] = sequence[0];
            for (int i = 1; i < sequence.Length; i++)
            {
                glyphs.Insert(at + i, sequence[i]);
            }

            return sequence.Length;
        }
    }

    private sealed class LigatureSubtable : Subtable
    {
        private readonly Dictionary<int, List<Ligature>> _map;

        public LigatureSubtable(Dictionary<int, List<Ligature>> map) => _map = map;

        public override int TryApply(List<int> glyphs, int at)
        {
            if (!_map.TryGetValue(glyphs[at], out List<Ligature> ligatures))
            {
                return 0;
            }

            // The spec orders a ligature set longest-first where it matters, and
            // HarfBuzz takes the first whose components all match; the order is the
            // font's to decide, so it is walked as stored rather than re-sorted.
            foreach (Ligature ligature in ligatures)
            {
                // The components sit at at+1 .. at+rest.Length, so a ligature longer
                // than what is left of the run cannot match.
                int[] rest = ligature.Components;
                if (at + rest.Length >= glyphs.Count)
                {
                    continue;
                }

                bool matched = true;
                for (int i = 0; i < rest.Length; i++)
                {
                    if (glyphs[at + 1 + i] != rest[i])
                    {
                        matched = false;
                        break;
                    }
                }

                if (!matched)
                {
                    continue;
                }

                glyphs[at] = ligature.Glyph;
                glyphs.RemoveRange(at + 1, rest.Length);
                return 1;
            }

            return 0;
        }
    }

    private readonly struct Ligature
    {
        public Ligature(int glyph, int[] components)
        {
            Glyph = glyph;
            Components = components;
        }

        public int Glyph { get; }

        public int[] Components { get; }
    }
}
