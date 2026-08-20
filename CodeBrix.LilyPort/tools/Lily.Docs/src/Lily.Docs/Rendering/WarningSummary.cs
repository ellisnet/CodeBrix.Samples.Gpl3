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
        StringBuilder text = new StringBuilder();
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
                continue;
            }

            counts[parts[0]] = int.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        return counts;
    }

    /// <summary>
    /// Writes the PDF-side baseline: the page count and the PDF stage's own warning
    /// count. Kept in its own file rather than as extra rows in the warning baseline,
    /// so that neither gate has to know which rows belong to the other.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="pageCount">The page count the renderer reported.</param>
    /// <param name="pdfWarningCount">How many warnings the PDF stage produced.</param>
    public static void WritePdfBaseline(string path, int pageCount, int pdfWarningCount)
    {
        StringBuilder text = new StringBuilder();
        text.Append("PAGES\t").Append(pageCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("PDF_WARNINGS\t")
            .Append(pdfWarningCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

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
        StringBuilder text = new StringBuilder();
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
