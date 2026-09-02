// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Text;

namespace Fresco.Brix.Tools; //was previously: CPython's unicodeobject.c (str.upper / str.lower / str.title)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Changes the case of a string the way PYTHON does, because three of the
/// commands ruling FD10 makes native are Frescobaldi snippets whose entire body
/// is <c>text.upper()</c>, <c>text.lower()</c> or <c>text.title()</c>.
/// </summary>
/// <remarks>
/// <para>
/// .NET's invariant casing is Unicode's SIMPLE mapping — one code point in, one
/// out. Python's is the FULL mapping, so a string can grow: <c>straße</c>
/// uppercases to <c>STRASSE</c> and the ligature <c>ﬁ</c> to <c>FI</c>. Python
/// also applies the Greek FINAL SIGMA rule when lowercasing, and its
/// <c>title()</c> uses the TITLECASE mapping, which for the 58 title-case
/// letters is not the uppercase one.
/// </para>
/// <para>
/// The tables are in <c>PythonCase.g.cs</c>, read out of Python itself by
/// <c>tools/snippetprobe/gen-case-tables.py</c>; the sweep fixture
/// <c>fixtures/python-case.txt</c> names every code point in Unicode whose case
/// differs from itself, and the tests reproduce all three mappings over the lot.
/// The tables hold EVERY mapping rather than only the ones .NET gets wrong,
/// because .NET's simple mapping does disagree with Python's in places — U+0131
/// DOTLESS I uppercases to I in Python and to itself under invariant casing —
/// and which places is an ICU version's business rather than a fact about
/// Python. A code point the tables do not name maps to itself.
/// </para>
/// </remarks>
public static partial class PythonCase
{
    private const int CapitalSigma = 0x03A3;
    private const int SmallSigma = 0x03C3;
    private const int FinalSigma = 0x03C2;

    /// <summary>Uppercases a string as <c>str.upper()</c> does.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The uppercased text.</returns>
    public static string Upper(string text)
    {
        if (string.IsNullOrEmpty(text)) { return text ?? string.Empty; }

        StringBuilder builder = new StringBuilder(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            AppendUpper(builder, rune);
        }

        return builder.ToString();
    }

    /// <summary>Lowercases a string as <c>str.lower()</c> does.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The lowercased text.</returns>
    public static string Lower(string text)
    {
        if (string.IsNullOrEmpty(text)) { return text ?? string.Empty; }

        Rune[] runes = ToRunes(text);
        StringBuilder builder = new StringBuilder(text.Length);
        for (int i = 0; i < runes.Length; i++)
        {
            AppendLower(builder, runes, i);
        }

        return builder.ToString();
    }

    /// <summary>Title-cases a string as <c>str.title()</c> does.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The title-cased text.</returns>
    /// <remarks>
    /// Python walks the string remembering whether the PREVIOUS character was
    /// cased: the character after an uncased one is title-cased, every other
    /// one is lowercased. That is why <c>they're</c> becomes <c>They'Re</c> and
    /// <c>de5f</c> becomes <c>De5F</c> — odd, but deliberate, and ported
    /// faithfully.
    /// </remarks>
    public static string Title(string text)
    {
        if (string.IsNullOrEmpty(text)) { return text ?? string.Empty; }

        Rune[] runes = ToRunes(text);
        StringBuilder builder = new StringBuilder(text.Length);
        bool previousIsCased = false;
        for (int i = 0; i < runes.Length; i++)
        {
            if (previousIsCased)
            {
                AppendLower(builder, runes, i);
            }
            else
            {
                AppendTitle(builder, runes[i]);
            }

            previousIsCased = IsCased(runes[i].Value);
        }

        return builder.ToString();
    }

    /// <summary>Answers whether a code point has Unicode's Cased property.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>Whether it is cased.</returns>
    public static bool IsCased(int codePoint) => InRanges(CasedRanges, codePoint);

    /// <summary>Answers whether a code point is case-ignorable.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>Whether the final-sigma rule skips over it.</returns>
    public static bool IsCaseIgnorable(int codePoint)
        => InRanges(CaseIgnorableRanges, codePoint);

    private static void AppendUpper(StringBuilder builder, Rune rune)
    {
        if (FullUpper.TryGetValue(rune.Value, out string full))
        {
            builder.Append(full);
            return;
        }

        builder.Append(rune.ToString());
    }

    private static void AppendTitle(StringBuilder builder, Rune rune)
    {
        if (TitleMap.TryGetValue(rune.Value, out string title))
        {
            builder.Append(title);
            return;
        }

        builder.Append(rune.ToString());
    }

    private static void AppendLower(StringBuilder builder, Rune[] runes, int index)
    {
        Rune rune = runes[index];
        if (rune.Value == CapitalSigma)
        {
            builder.Append((char)(IsFinalSigma(runes, index) ? FinalSigma : SmallSigma));
            return;
        }

        if (FullLower.TryGetValue(rune.Value, out string full))
        {
            builder.Append(full);
            return;
        }

        builder.Append(rune.ToString());
    }

    /// <summary>
    /// Unicode's Final_Sigma condition, in the shape CPython evaluates it: the
    /// nearest character BEFORE the sigma that is not case-ignorable must be
    /// cased, and the nearest one AFTER it must not be (or there must be none).
    /// </summary>
    /// <param name="runes">The whole string.</param>
    /// <param name="index">Where the sigma is.</param>
    /// <returns>Whether the sigma is final.</returns>
    private static bool IsFinalSigma(Rune[] runes, int index)
    {
        int before = index - 1;
        while (before >= 0 && IsCaseIgnorable(runes[before].Value)) { before--; }

        if (before < 0 || !IsCased(runes[before].Value)) { return false; }

        int after = index + 1;
        while (after < runes.Length && IsCaseIgnorable(runes[after].Value)) { after++; }

        return after >= runes.Length || !IsCased(runes[after].Value);
    }

    private static bool InRanges(int[] ranges, int codePoint)
    {
        int low = 0;
        int high = (ranges.Length / 2) - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (codePoint < ranges[middle * 2])
            {
                high = middle - 1;
            }
            else if (codePoint > ranges[(middle * 2) + 1])
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static Rune[] ToRunes(string text)
    {
        Rune[] runes = new Rune[text.Length];
        int count = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            runes[count++] = rune;
        }

        Array.Resize(ref runes, count);
        return runes;
    }
}
