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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lily.Docs.Snippets;

/// <summary>
/// The VOCABULARY of the SVG the port's engine emits for documentation snippets: which
/// elements appear, which attributes appear, and which font families the text runs ask for.
/// <para>
/// This exists to answer ONE question for a downstream renderer — "what must I implement to
/// draw these pictures?" — and to keep answering it as the corpus grows. The frozen copy is
/// <c>tools/Lily.Docs/svg-dialect/inventory.tsv</c>, and the specification a renderer author
/// reads is <c>tools/Lily.Docs/svg-dialect/README.txt</c> beside it.
/// </para>
/// <para>
/// ⚠ WHAT IS ASSERTED IS THE SET, NOT THE COUNTS, and that is deliberate. The expected-warnings
/// baselines are asserted exactly because their numbers are the finding. Here the finding is
/// the VOCABULARY: an element or attribute or font family that has never been seen before is a
/// renderer requirement nobody has agreed to, and it must go red. The counts move whenever a
/// snippet does and are recorded for information only — freezing them would make the gate
/// fail for reasons that say nothing about the dialect, and a gate that cries wolf gets
/// regenerated instead of read.
/// </para>
/// </summary>
public sealed class SvgDialectInventory
{
    /// <summary>Matches one whole start tag, so attributes are only read inside a tag.</summary>
    private static readonly Regex TagPattern =
        new Regex("<([a-zA-Z][a-zA-Z0-9:_-]*)([^>]*)>", RegexOptions.Compiled);

    /// <summary>Matches an attribute name in the interior of a start tag.</summary>
    private static readonly Regex AttributePattern =
        new Regex("([a-zA-Z][a-zA-Z0-9:_-]*)\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled);

    private readonly SortedDictionary<string, int> _elements;
    private readonly SortedDictionary<string, int> _attributes;
    private readonly SortedDictionary<string, int> _fontFamilies;

    private SvgDialectInventory(int fileCount, SortedDictionary<string, int> elements,
        SortedDictionary<string, int> attributes, SortedDictionary<string, int> fontFamilies)
    {
        FileCount = fileCount;
        _elements = elements;
        _attributes = attributes;
        _fontFamilies = fontFamilies;
    }

    /// <summary>The row kind naming an element.</summary>
    public const string ElementKind = "ELEMENT";

    /// <summary>The row kind naming an attribute.</summary>
    public const string AttributeKind = "ATTRIBUTE";

    /// <summary>The row kind naming a <c>font-family</c> value.</summary>
    public const string FontFamilyKind = "FONT-FAMILY";

    /// <summary>The row kind naming a whole-inventory fact.</summary>
    public const string FactKind = "FACT";

    /// <summary>How many SVG files the inventory was taken over.</summary>
    public int FileCount { get; }

    /// <summary>Element name to the number of FILES it appears in.</summary>
    public IReadOnlyDictionary<string, int> Elements => _elements;

    /// <summary>Attribute name to the number of FILES it appears in.</summary>
    public IReadOnlyDictionary<string, int> Attributes => _attributes;

    /// <summary>
    /// <c>font-family</c> value to the number of OCCURRENCES, verbatim as written — the
    /// value is not split on commas, because a renderer has to cope with the whole list.
    /// </summary>
    public IReadOnlyDictionary<string, int> FontFamilies => _fontFamilies;

    /// <summary>Takes an inventory over every <c>.svg</c> beneath a directory.</summary>
    /// <param name="directory">The directory to walk, recursively.</param>
    /// <returns>The inventory.</returns>
    public static SvgDialectInventory Scan(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "no directory to take an SVG inventory over: " + (directory ?? "<null>"));
        }

        return ScanFiles(Directory.EnumerateFiles(directory, "*.svg", SearchOption.AllDirectories));
    }

    /// <summary>Takes an inventory over named files.</summary>
    /// <param name="paths">The SVG files to read.</param>
    /// <returns>The inventory.</returns>
    public static SvgDialectInventory ScanFiles(IEnumerable<string> paths)
    {
        SortedDictionary<string, int> elements =
            new SortedDictionary<string, int>(StringComparer.Ordinal);
        SortedDictionary<string, int> attributes =
            new SortedDictionary<string, int>(StringComparer.Ordinal);
        SortedDictionary<string, int> fontFamilies =
            new SortedDictionary<string, int>(StringComparer.Ordinal);
        int files = 0;

        foreach (string path in paths ?? Enumerable.Empty<string>())
        {
            files++;
            string text = File.ReadAllText(path);
            HashSet<string> elementsHere = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> attributesHere = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match tag in TagPattern.Matches(text))
            {
                elementsHere.Add(tag.Groups[1].Value);
                foreach (Match attribute in AttributePattern.Matches(tag.Groups[2].Value))
                {
                    string name = attribute.Groups[1].Value;
                    attributesHere.Add(name);
                    if (string.Equals(name, "font-family", StringComparison.Ordinal))
                    {
                        Add(fontFamilies, attribute.Groups[2].Value, 1);
                    }
                }
            }

            foreach (string element in elementsHere)
            {
                Add(elements, element, 1);
            }

            foreach (string attribute in attributesHere)
            {
                Add(attributes, attribute, 1);
            }
        }

        return new SvgDialectInventory(files, elements, attributes, fontFamilies);
    }

    /// <summary>
    /// The names this inventory carries for one kind — the SET, which is what the gate
    /// asserts.
    /// </summary>
    /// <param name="kind">One of the <c>*Kind</c> constants.</param>
    /// <returns>The names, ordinal-sorted.</returns>
    public IReadOnlyList<string> NamesOf(string kind)
    {
        return MapFor(kind).Keys.ToList();
    }

    /// <summary>Writes the frozen baseline: one <c>kind  name  count</c> line per member.</summary>
    /// <param name="path">The file to write.</param>
    public void WriteBaseline(string path)
    {
        StringBuilder text = new StringBuilder();
        text.Append(FactKind).Append("\tFILES\t")
            .Append(FileCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        AppendAll(text, ElementKind, _elements);
        AppendAll(text, AttributeKind, _attributes);
        AppendAll(text, FontFamilyKind, _fontFamilies);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Reads a baseline written by <see cref="WriteBaseline"/>.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The inventory the file records.</returns>
    public static SvgDialectInventory ReadBaseline(string path)
    {
        SortedDictionary<string, int> elements =
            new SortedDictionary<string, int>(StringComparer.Ordinal);
        SortedDictionary<string, int> attributes =
            new SortedDictionary<string, int>(StringComparer.Ordinal);
        SortedDictionary<string, int> fontFamilies =
            new SortedDictionary<string, int>(StringComparer.Ordinal);
        int files = 0;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 3)
            {
                continue;
            }

            int count = int.Parse(parts[2], CultureInfo.InvariantCulture);
            switch (parts[0])
            {
                case FactKind:
                    if (string.Equals(parts[1], "FILES", StringComparison.Ordinal))
                    {
                        files = count;
                    }

                    break;
                case ElementKind:
                    elements[parts[1]] = count;
                    break;
                case AttributeKind:
                    attributes[parts[1]] = count;
                    break;
                case FontFamilyKind:
                    fontFamilies[parts[1]] = count;
                    break;
                default:
                    break;
            }
        }

        return new SvgDialectInventory(files, elements, attributes, fontFamilies);
    }

    private IReadOnlyDictionary<string, int> MapFor(string kind)
    {
        switch (kind)
        {
            case ElementKind:
                return _elements;
            case AttributeKind:
                return _attributes;
            case FontFamilyKind:
                return _fontFamilies;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown inventory kind");
        }
    }

    private static void AppendAll(StringBuilder text, string kind,
        SortedDictionary<string, int> members)
    {
        foreach (KeyValuePair<string, int> entry in members)
        {
            text.Append(kind).Append('\t').Append(entry.Key).Append('\t')
                .Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
    }

    private static void Add(SortedDictionary<string, int> counts, string key, int amount)
    {
        counts.TryGetValue(key, out int existing);
        counts[key] = existing + amount;
    }
}
