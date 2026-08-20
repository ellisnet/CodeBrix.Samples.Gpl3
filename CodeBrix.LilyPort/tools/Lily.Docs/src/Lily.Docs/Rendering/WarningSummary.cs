// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Lily.Docs.Rendering;

/// <summary>
/// Counts render warnings by category, and reads and writes the frozen baseline files
/// under <c>tools/Lily.Docs/expected-warnings/</c>.
/// <para>
/// A baseline is asserted EXACTLY, and a change in EITHER direction is a signal —
/// fewer warnings is as much a change to look at as more. The file is frozen from a
/// measured run that was then READ; it is never regenerated to make a test pass.
/// </para>
/// </summary>
public static class WarningSummary
{
    /// <summary>
    /// The categories CodeBrix.Texinfo2Html prefixes its messages with. Listed so that
    /// a message carrying an unfamiliar prefix lands in <see cref="UncategorizedName"/>
    /// and shows up in the baseline, rather than being silently folded into a
    /// neighbouring count.
    /// </summary>
    private static readonly string[] KnownCategories =
    {
        "Include", "Conditional", "Macro", "Value", "RawBlockSkipped", "Encoding",
        "Syntax", "UnknownCommand", "Reference", "Emit",
    };

    /// <summary>The bucket a message with no recognizable category prefix lands in.</summary>
    public const string UncategorizedName = "(uncategorized)";

    /// <summary>Counts messages by category.</summary>
    /// <param name="messages">The messages to count.</param>
    /// <returns>Category to count, ordered by category name.</returns>
    public static SortedDictionary<string, int> Count(IEnumerable<string> messages)
    {
        SortedDictionary<string, int> counts =
            new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (messages == null)
        {
            return counts;
        }

        foreach (string message in messages)
        {
            string category = CategoryOf(message);
            counts.TryGetValue(category, out int existing);
            counts[category] = existing + 1;
        }

        return counts;
    }

    /// <summary>Reads the category prefix off one message.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The category, or <see cref="UncategorizedName"/>.</returns>
    public static string CategoryOf(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return UncategorizedName;
        }

        foreach (string category in KnownCategories)
        {
            // The separator is not assumed: a prefix followed by ':' and by ' ' are both
            // accepted, so a change of punctuation upstream shows up as a count that
            // moved rather than as every message falling into the uncategorized bucket
            // at once.
            if (message.Length > category.Length
                && message.StartsWith(category, StringComparison.Ordinal)
                && !char.IsLetterOrDigit(message[category.Length]))
            {
                return category;
            }
        }

        return UncategorizedName;
    }

    /// <summary>
    /// Writes a baseline file: one tab-separated <c>category  count</c> line per
    /// category, sorted, with a total line last.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="counts">The counts to write.</param>
    public static void WriteBaseline(string path, IReadOnlyDictionary<string, int> counts)
    {
        StringBuilder text = new StringBuilder(ReadHeaderComment(path));
        int total = 0;
        foreach (KeyValuePair<string, int> entry in Ordered(counts))
        {
            text.Append(entry.Key).Append('\t')
                .Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            total += entry.Value;
        }

        text.Append("TOTAL\t").Append(total.ToString(CultureInfo.InvariantCulture)).Append('\n');
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Reads a baseline file written by <see cref="WriteBaseline"/>.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>Category to count. The TOTAL line is not included.</returns>
    public static SortedDictionary<string, int> ReadBaseline(string path)
    {
        SortedDictionary<string, int> counts =
            new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 2 || parts[0] == "TOTAL")
            {
                // Wider rows belong to another reader — the PDF baseline's DROP rows are
                // four columns. Skipped rather than rejected so the two schemas can share
                // one file without either reader knowing the other's rows.
                continue;
            }

            // A value that is not an integer belongs to ReadPdfBaselineValues, which reads
            // the same rows as text. Skipped for the same reason.
            if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int value))
            {
                counts[parts[0]] = value;
            }
        }

        return counts;
    }

    /// <summary>The prefix of a PDF baseline's per-code-point drop rows.</summary>
    /// <remarks>
    /// Four columns — <c>DROP</c>, the warning's stable code, the code point, and the
    /// occurrence count — where every other row in these files is two. The two-column
    /// readers skip it and this one skips them, so one file carries both schemas without
    /// either reader having to know about the other.
    /// </remarks>
    public const string DropRowPrefix = "DROP";

    /// <summary>
    /// Writes the PDF-side baseline: the render's scalar facts, then one row per DISTINCT
    /// DROPPED CODE POINT.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="values">The scalar facts — page count, warning counts, the page size
    /// actually used, and the two shipped defaults this phase deliberately did not
    /// change.</param>
    /// <param name="dropRows">The drop rows, already formatted by
    /// <see cref="FormatDropRow"/>.</param>
    /// <remarks>
    /// <para>
    /// ⚠ THE DROP ROWS ARE THE POINT, AND A COUNT ALONE WOULD NOT DO. Until the packages
    /// gained <c>TexinfoPdfWarnings.PdfItems</c> the PDF stage reported prose carrying
    /// "first seen: U+XXXX" and no occurrence count, so a drop baseline could only have
    /// been a string match on a message. Freezing code, code point and count per item means
    /// a drop that MOVES — same total, different character — is a red gate rather than a
    /// number that still adds up.
    /// </para>
    /// <para>
    /// The page size is frozen alongside them because a manual that silently reverted to US
    /// Letter would change the page count and nothing else, and a page count that moved
    /// would then have two candidate explanations instead of one.
    /// </para>
    /// </remarks>
    public static void WritePdfBaseline(string path, IReadOnlyDictionary<string, string> values,
        IEnumerable<string> dropRows)
    {
        StringBuilder text = new StringBuilder(ReadHeaderComment(path));
        SortedDictionary<string, string> sorted =
            new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in values ?? EmptyValues)
        {
            sorted[entry.Key] = entry.Value;
        }

        foreach (KeyValuePair<string, string> entry in sorted)
        {
            text.Append(entry.Key).Append('\t').Append(entry.Value).Append('\n');
        }

        if (dropRows != null)
        {
            List<string> rows = new List<string>(dropRows);
            rows.Sort(StringComparer.Ordinal);
            foreach (string row in rows)
            {
                text.Append(row).Append('\n');
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Formats one drop row.</summary>
    /// <param name="code">The warning's stable code, e.g. <c>font.svg-text.notdef</c>.</param>
    /// <param name="codePoint">The code point involved, or null when the warning carries
    /// none.</param>
    /// <param name="occurrences">How many times it occurred.</param>
    /// <returns>The row.</returns>
    public static string FormatDropRow(string code, int? codePoint, int occurrences)
    {
        string point = codePoint.HasValue
            ? "U+" + codePoint.Value.ToString("X4", CultureInfo.InvariantCulture)
            : "-";
        return DropRowPrefix + "\t" + code + "\t" + point + "\t"
            + occurrences.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reads the two-column rows of a PDF baseline as TEXT.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>Key to value.</returns>
    /// <remarks>
    /// Text rather than integers because not every scalar is one: the SVG raster scale is a
    /// ratio and the two switches are booleans, and rendering them all as the exact
    /// characters the freezer wrote is what keeps the assertion an equality rather than a
    /// parse followed by a comparison.
    /// </remarks>
    public static SortedDictionary<string, string> ReadPdfBaselineValues(string path)
    {
        SortedDictionary<string, string> values =
            new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length == 2)
            {
                values[parts[0]] = parts[1];
            }
        }

        return values;
    }

    /// <summary>Reads the DROP rows of a PDF baseline, verbatim and sorted.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The rows.</returns>
    public static List<string> ReadPdfBaselineDrops(string path)
    {
        List<string> rows = new List<string>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.StartsWith(DropRowPrefix + "\t", StringComparison.Ordinal))
            {
                rows.Add(line);
            }
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private static readonly SortedDictionary<string, string> EmptyValues =
        new SortedDictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Writes the engraving baseline: what the manual's snippet renderer was ASKED to do and
    /// what came back.
    /// <para>
    /// ⚠ ASKED AND FAILED ARE THE LOAD-BEARING NUMBERS. The Texinfo package CATCHES a
    /// renderer that throws and shows the snippet's source instead, so a render that
    /// completed is compatible with every engraving having failed. A count of what was
    /// produced cannot tell those apart; a count of what was asked for, paired with a count
    /// of what failed, can.
    /// </para>
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="counts">The engraving counts, by name.</param>
    public static void WriteSnippetBaseline(string path, IReadOnlyDictionary<string, int> counts)
    {
        StringBuilder text = new StringBuilder(ReadHeaderComment(path));
        foreach (KeyValuePair<string, int> entry in Ordered(counts))
        {
            text.Append(entry.Key).Append('\t')
                .Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Reads a PDF baseline written by <see cref="WritePdfBaseline"/>.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>Key to value, e.g. PAGES and PDF_WARNINGS.</returns>
    public static SortedDictionary<string, int> ReadPdfBaseline(string path)
    {
        return ReadBaseline(path);
    }


    /// <summary>
    /// Reads back the leading COMMENT BLOCK of a baseline file so that re-freezing keeps it.
    /// </summary>
    /// <param name="path">The baseline file, which may not exist.</param>
    /// <returns>The comment block including its trailing blank line, or the empty string.</returns>
    /// <remarks>
    /// ⚠ ADDED AT WAVE LD4, BECAUSE RE-FREEZING HAD JUST DESTROYED FORTY LINES OF ANALYSIS.
    /// <c>notation-snippets.tsv</c> carried wave LD3's account of what its twelve engraving
    /// failures ARE — the eleven glyph charts blocked on a CodeBrix.LilyScheme arity gap, and
    /// the one <c>\skip</c> snippet left failing on purpose — written by hand beside the
    /// numbers it explains. The freezer wrote six data rows and the explanation was gone,
    /// which is the worst possible failure for a file whose entire value is that a later
    /// reader knows why a number is what it is.
    /// <para>
    /// So the block is preserved rather than the rule being "remember not to use --baseline
    /// on that file". A comment that survives the tool is a comment that can be trusted to
    /// still be there.
    /// </para>
    /// </remarks>
    public static string ReadHeaderComment(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        StringBuilder header = new StringBuilder();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length != 0 && line[0] != '#')
            {
                break;
            }

            header.Append(line).Append('\n');
        }

        return header.ToString();
    }

    private static IEnumerable<KeyValuePair<string, int>> Ordered(
        IReadOnlyDictionary<string, int> counts)
    {
        SortedDictionary<string, int> sorted =
            new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> entry in counts)
        {
            sorted[entry.Key] = entry.Value;
        }

        return sorted;
    }
}
