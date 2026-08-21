// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/htmldiff.py + python's difflib

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What one line of a diff is.</summary>
public enum DiffKind
{
    /// <summary>The line is the same on both sides.</summary>
    Same,

    /// <summary>The line is only on the left.</summary>
    Removed,

    /// <summary>The line is only on the right.</summary>
    Added,
}

/// <summary>One row of a side-by-side comparison.</summary>
/// <param name="Kind">What happened to the line.</param>
/// <param name="LeftNumber">The line's number on the left, or 0.</param>
/// <param name="Left">The line on the left, or empty.</param>
/// <param name="RightNumber">The line's number on the right, or 0.</param>
/// <param name="Right">The line on the right, or empty.</param>
public readonly record struct DiffRow(
    DiffKind Kind, int LeftNumber, string Left, int RightNumber, string Right);

/// <summary>
/// A line-by-line comparison of two versions of a document, for the convert-ly
/// dialog's Changes and Diff views.
/// </summary>
/// <remarks>
/// ⚠ A DELIBERATE DIVERGENCE IN MECHANISM, not in what the user sees. Upstream
/// renders both views as HTML — <c>htmldiff.py</c> wraps python's
/// <c>difflib.HtmlDiff</c> and the unified view is <c>difflib.unified_diff</c>
/// coloured with HTML — and displays them in a <c>QTextBrowser</c>. Ruling FR8
/// puts NO WebView anywhere in this application, so the rows are produced here
/// and drawn as ordinary controls. The comparison itself is a plain longest-
/// common-subsequence over lines rather than a port of <c>SequenceMatcher</c>,
/// whose junk heuristics change which of several equally valid alignments is
/// shown and nothing else; no oracle rides on it.
/// </remarks>
public static class TextDiff
{
    /// <summary>Compares two documents line by line.</summary>
    /// <param name="left">The document before.</param>
    /// <param name="right">The document after.</param>
    /// <returns>Every line, in order, with what happened to it.</returns>
    public static IReadOnlyList<DiffRow> Compare(string left, string right)
    {
        string[] a = SplitLines(left);
        string[] b = SplitLines(right);
        int[,] lengths = LongestCommonSubsequence(a, b);

        List<DiffRow> rows = new List<DiffRow>();
        int i = 0;
        int j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (string.Equals(a[i], b[j], StringComparison.Ordinal))
            {
                rows.Add(new DiffRow(DiffKind.Same, i + 1, a[i], j + 1, b[j]));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                rows.Add(new DiffRow(DiffKind.Removed, i + 1, a[i], 0, string.Empty));
                i++;
            }
            else
            {
                rows.Add(new DiffRow(DiffKind.Added, 0, string.Empty, j + 1, b[j]));
                j++;
            }
        }

        while (i < a.Length)
        {
            rows.Add(new DiffRow(DiffKind.Removed, i + 1, a[i], 0, string.Empty));
            i++;
        }

        while (j < b.Length)
        {
            rows.Add(new DiffRow(DiffKind.Added, 0, string.Empty, j + 1, b[j]));
            j++;
        }

        return rows;
    }

    /// <summary>
    /// The comparison as a unified diff — the shape upstream's third tab shows,
    /// and the shape a user can paste into a bug report.
    /// </summary>
    /// <param name="left">The document before.</param>
    /// <param name="right">The document after.</param>
    /// <param name="leftName">What to call the left side.</param>
    /// <param name="rightName">What to call the right side.</param>
    /// <param name="context">How many unchanged lines to keep around a change.</param>
    /// <returns>The diff's lines, headers included.</returns>
    public static IReadOnlyList<DiffRow> Unified(
        string left, string right, string leftName, string rightName, int context = 3)
    {
        IReadOnlyList<DiffRow> rows = Compare(left, right);
        List<DiffRow> result = new List<DiffRow>();

        bool anyChange = false;
        foreach (DiffRow row in rows)
        {
            if (row.Kind != DiffKind.Same) { anyChange = true; break; }
        }

        if (!anyChange) { return result; }

        result.Add(new DiffRow(DiffKind.Removed, 0, "--- " + leftName, 0, string.Empty));
        result.Add(new DiffRow(DiffKind.Added, 0, string.Empty, 0, "+++ " + rightName));

        //Keep only the lines near a change, which is what makes a unified diff
        //readable on a document that is mostly unchanged.
        bool[] keep = new bool[rows.Count];
        for (int index = 0; index < rows.Count; index++)
        {
            if (rows[index].Kind == DiffKind.Same) { continue; }

            for (int near = Math.Max(0, index - context);
                near <= Math.Min(rows.Count - 1, index + context);
                near++)
            {
                keep[near] = true;
            }
        }

        bool inHunk = false;
        for (int index = 0; index < rows.Count; index++)
        {
            if (!keep[index])
            {
                inHunk = false;
                continue;
            }

            if (!inHunk)
            {
                DiffRow start = rows[index];
                int leftLine = start.LeftNumber != 0 ? start.LeftNumber : 0;
                int rightLine = start.RightNumber != 0 ? start.RightNumber : 0;
                result.Add(new DiffRow(
                    DiffKind.Same, 0,
                    "@@ -" + leftLine + " +" + rightLine + " @@",
                    0, string.Empty));
                inHunk = true;
            }

            result.Add(rows[index]);
        }

        return result;
    }

    /// <summary>Answers how many rows differ.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The count.</returns>
    public static int ChangeCount(IReadOnlyList<DiffRow> rows)
    {
        int changes = 0;
        foreach (DiffRow row in rows)
        {
            if (row.Kind != DiffKind.Same) { changes++; }
        }

        return changes;
    }

    /// <summary>Splits a document into lines, keeping no terminators.</summary>
    /// <param name="text">The document.</param>
    /// <returns>The lines.</returns>
    private static string[] SplitLines(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

    /// <summary>
    /// The classic LCS table: <c>lengths[i, j]</c> is how many lines the tails
    /// <c>a[i..]</c> and <c>b[j..]</c> share.
    /// </summary>
    /// <param name="a">The left lines.</param>
    /// <param name="b">The right lines.</param>
    /// <returns>The table.</returns>
    private static int[,] LongestCommonSubsequence(string[] a, string[] b)
    {
        int[,] lengths = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
        {
            for (int j = b.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        return lengths;
    }
}
