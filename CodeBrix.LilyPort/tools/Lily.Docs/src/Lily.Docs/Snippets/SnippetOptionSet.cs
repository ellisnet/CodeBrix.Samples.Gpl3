// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Lily.Docs.Snippets;

/// <summary>
/// One snippet's EFFECTIVE option set: what it asked for, merged over the formatter's
/// defaults, with every option lilypond-book derives rather than reads.
/// <para>
/// Ported from <c>python/book_snippets.py:429-546</c> (<c>parse_snippet_options</c>'s
/// post-processing and the option-dictionary merge) and <c>:87-117</c> (the
/// <c>PROCESSING_INDEPENDENT_OPTIONS</c> and <c>simple_options</c> lists), read in
/// <c>book-mirror/</c>.
/// </para>
/// <para>
/// The bracketed option list is NOT re-parsed here. The Texinfo package split it
/// already — its option vocabulary was measured across the same corpus — and
/// <see cref="CodeBrix.Texinfo2Html.LilypondSnippetOptions.All"/> hands over exactly
/// what <c>split_snippet_options</c> produces upstream. This type starts where that
/// leaves off: the DERIVATIONS, which is where the fidelity actually lives.
/// </para>
/// </summary>
public sealed class SnippetOptionSet
{
    /// <summary>
    /// The default <c>--inline-vshift</c>, from <c>lilypond-book.py:291-297</c>. It is a
    /// LaTeX-backend value that reaches the composed source only as the <c>inline</c>
    /// option's recorded value, but it is recorded, so it is reproduced.
    /// </summary>
    private const string InlineVerticalShift = "-0.3";

    /// <summary>
    /// <c>book_snippets.py:88-98</c>. Options with no effect on what LilyPond draws, so
    /// they are left out of the provenance comment and out of the engraving cache key.
    /// </summary>
    private static readonly HashSet<string> ProcessingIndependent =
        new HashSet<string>(StringComparer.Ordinal)
        {
            SnippetOptionNames.Alt,
            SnippetOptionNames.DocTitle,
            SnippetOptionNames.HtmlPrintFileName,
            SnippetOptionNames.NoGettext,
            SnippetOptionNames.PrintFileName,
            SnippetOptionNames.TexiDoc,
            SnippetOptionNames.Verbatim,
            SnippetOptionNames.LilypondVersion,
        };

    /// <summary>
    /// <c>book_snippets.py:100-117</c> — options that carry no template in
    /// <c>snippet_options</c> and so contribute nothing to the composed blocks. An
    /// option in NEITHER this list nor a template group draws upstream's "ignoring
    /// unknown ly option" warning, which is why the list is reproduced rather than
    /// inferred.
    /// </summary>
    private static readonly HashSet<string> SimpleOptions =
        new HashSet<string>(StringComparer.Ordinal)
        {
            SnippetOptionNames.Alt,
            SnippetOptionNames.DocTitle,
            SnippetOptionNames.ExampleIndent,
            "filename",
            SnippetOptionNames.Fragment,
            SnippetOptionNames.HtmlPrintFileName,
            SnippetOptionNames.Inline,
            SnippetOptionNames.NoFragment,
            SnippetOptionNames.NoGettext,
            SnippetOptionNames.NoIndent,
            SnippetOptionNames.PaperHeight,
            SnippetOptionNames.PaperWidth,
            SnippetOptionNames.PrintFileName,
            SnippetOptionNames.TexiDoc,
            SnippetOptionNames.Verbatim,
        };

    /// <summary><c>book_snippets.py:178</c> — <c>ly_dimen_re</c>, verbatim.</summary>
    private static readonly Regex DimensionRegex = new Regex(
        @"^([0-9]+\.?[0-9]*|\.[0-9]+)\s*\\(cm|mm|in|pt|bp)$", RegexOptions.Compiled);

    /// <summary>
    /// <c>book_snippets.py:183</c> — the unit form <c>classic_lilypond_book_compatibility</c>
    /// normalises, i.e. a dimension written WITHOUT its backslash.
    /// </summary>
    private static readonly Regex BareUnitRegex = new Regex(
        @"^([-.0-9]+)(cm|in|mm|pt|bp|staffspace)", RegexOptions.Compiled);

    private static readonly Regex ClassicRelativeRegex = new Regex(
        @"relative\s*([-0-9])", RegexOptions.Compiled);

    private static readonly Regex ClassicStaffSizeRegex = new Regex(
        @"^([0-9]+)pt", RegexOptions.Compiled);

    private readonly SortedDictionary<string, string> _effective;
    private readonly HashSet<string> _explicit;

    private SnippetOptionSet(SortedDictionary<string, string> effective,
        HashSet<string> explicitKeys, IReadOnlyList<string> outputRelevant,
        IReadOnlyList<string> deprecated)
    {
        _effective = effective;
        _explicit = explicitKeys;
        OutputRelevant = outputRelevant;
        DeprecatedOptions = deprecated;
    }

    /// <summary>
    /// The option strings that influence what LilyPond draws, sorted — upstream's
    /// <c>outputrelevant_option_list</c>. This is what the composed source records in its
    /// <c>%% Options:</c> comment and what upstream hashes into a snippet's file name.
    /// </summary>
    public IReadOnlyList<string> OutputRelevant { get; }

    /// <summary>
    /// Deprecated option spellings that were translated on the way in, as
    /// "written =&gt; translated" pairs. Upstream WARNS about each of these; the corpus in
    /// scope uses none of them (measured), so a non-empty list here is a finding.
    /// </summary>
    public IReadOnlyList<string> DeprecatedOptions { get; }

    /// <summary>Whether the snippet composes as a fragment rather than a whole file.</summary>
    public bool IsFragment => _effective.ContainsKey(SnippetOptionNames.Fragment);

    /// <summary>Every effective option, in the order upstream iterates them.</summary>
    public IReadOnlyDictionary<string, string> Effective => _effective;

    /// <summary>Whether an option is present, whatever its value.</summary>
    /// <param name="name">The option name.</param>
    /// <returns>True when the effective set carries it.</returns>
    public bool Has(string name) => _effective.ContainsKey(name);

    /// <summary>Whether the SNIPPET named an option itself, rather than inheriting it.</summary>
    /// <param name="name">The option name.</param>
    /// <returns>True when the snippet's own bracket list carried it.</returns>
    public bool WasGivenExplicitly(string name) => _explicit.Contains(name);

    /// <summary>An option's value, or an empty string for a flag or an absent option.</summary>
    /// <param name="name">The option name.</param>
    /// <returns>The value as written.</returns>
    public string Value(string name)
        => _effective.TryGetValue(name, out string value) && value != null ? value : string.Empty;

    /// <summary>Whether an option carries no template and so draws no unknown-option warning.</summary>
    /// <param name="name">The option name.</param>
    /// <returns>True when upstream lists it in <c>simple_options</c>.</returns>
    public static bool IsSimpleOption(string name) => SimpleOptions.Contains(name);

    /// <summary>
    /// Builds the effective option set for one snippet.
    /// </summary>
    /// <param name="writtenOptions">The snippet's own option list as written, i.e. the
    /// package's <c>Options.All</c>.</param>
    /// <param name="geometry">The manual's page geometry, which supplies the formatter
    /// defaults.</param>
    /// <returns>The effective set.</returns>
    public static SnippetOptionSet For(IReadOnlyList<string> writtenOptions,
        TexinfoPageGeometry geometry)
    {
        if (geometry == null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        // ── the snippet's own options ────────────────────────────────────────────────
        SortedDictionary<string, string> snippet =
            new SortedDictionary<string, string>(StringComparer.Ordinal);
        HashSet<string> explicitKeys = new HashSet<string>(StringComparer.Ordinal);
        List<string> deprecated = new List<string>();

        if (writtenOptions != null)
        {
            foreach (string written in writtenOptions)
            {
                if (string.IsNullOrWhiteSpace(written))
                {
                    continue;
                }

                SplitOption(written, out string key, out string value);

                // A `no...' option REMOVES the option it negates, and then survives in
                // the set itself — which is why `nofragment' appears in the provenance
                // comment of a snippet that composes as a whole file.
                if (string.Equals(key, SnippetOptionNames.NoFragment, StringComparison.Ordinal))
                {
                    snippet.Remove(SnippetOptionNames.Fragment);
                }
                else if (string.Equals(key, SnippetOptionNames.NoIndent, StringComparison.Ordinal))
                {
                    snippet.Remove(SnippetOptionNames.Indent);
                }

                ApplyClassicCompatibility(ref key, ref value, written, deprecated);
                if (key == null)
                {
                    continue;
                }

                snippet[key] = value;
                explicitKeys.Add(key);
            }
        }

        // ── the derivations, in upstream's own order ─────────────────────────────────

        // A named paper size is QUOTED, so `papersize=a6' reaches LilyPond as "a6".
        if (snippet.TryGetValue(SnippetOptionNames.PaperSize, out string paperSize)
            && paperSize != null)
        {
            snippet[SnippetOptionNames.PaperSize] = "\"" + paperSize + "\"";
        }

        // An explicit width or height OVERRIDES a named paper size, and a missing half is
        // filled from the format's own default before the pair is constructed.
        bool hasWidth = snippet.ContainsKey(SnippetOptionNames.PaperWidth);
        bool hasHeight = snippet.ContainsKey(SnippetOptionNames.PaperHeight);
        if (hasWidth || hasHeight)
        {
            if (!hasHeight)
            {
                snippet[SnippetOptionNames.PaperHeight] = geometry.PaperHeight;
            }

            if (!hasWidth)
            {
                snippet[SnippetOptionNames.PaperWidth] = geometry.PaperWidth;
            }

            SplitDimension(snippet[SnippetOptionNames.PaperWidth], geometry.PaperWidth,
                out string width, out string widthUnit);
            SplitDimension(snippet[SnippetOptionNames.PaperHeight], geometry.PaperHeight,
                out string height, out string heightUnit);
            snippet[SnippetOptionNames.PaperSize] = ConstructPaperSize(
                width, widthUnit, height, heightUnit);

            // ⚠ THE DERIVED PAPER SIZE COUNTS AS ONE THE SNIPPET ASKED FOR, and this is
            // load-bearing rather than bookkeeping. Upstream writes a paper size derived
            // from an explicit width or height into the SNIPPET's own option dictionary
            // (book_snippets.py:512-514), while a size it constructs from the format
            // defaults goes into the FORMATTER's (:506-510). `compose_ly' then tests
            // `PAPERSIZE in self.snippet_option_dict' to decide whether to drop the
            // default line width and the quote narrowing — so a snippet saying
            // `paper-width=10\cm' loses its line-width block, and one that names no paper
            // size at all keeps it. MEASURED against the oracle: paper-width.ly's paper
            // block is set-paper-size and indent, with no line-width at all.
            explicitKeys.Add(SnippetOptionNames.PaperSize);
        }

        // `line-width' with no value is dropped, so the format's default stands.
        if (snippet.TryGetValue(SnippetOptionNames.LineWidth, out string lineWidth)
            && string.IsNullOrEmpty(lineWidth))
        {
            snippet.Remove(SnippetOptionNames.LineWidth);
        }

        // "RELATIVE does not work without FRAGMENT, so imply that" — upstream's comment.
        if (snippet.ContainsKey(SnippetOptionNames.Relative))
        {
            snippet[SnippetOptionNames.Fragment] = null;
        }

        // `inline' bare takes the --inline-vshift value.
        if (snippet.TryGetValue(SnippetOptionNames.Inline, out string inline) && inline == null)
        {
            snippet[SnippetOptionNames.Inline] = InlineVerticalShift;
        }

        // ── the merge: an indent fallback, then the format's defaults, then the snippet ─
        SortedDictionary<string, string> effective =
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { SnippetOptionNames.Indent, @"0\mm" },
            };

        foreach (KeyValuePair<string, string> pair in FormatterDefaults(geometry, snippet))
        {
            effective[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, string> pair in snippet)
        {
            effective[pair.Key] = pair.Value;
        }

        // ── the output-relevant list ─────────────────────────────────────────────────
        List<string> relevant = new List<string>();
        foreach (KeyValuePair<string, string> pair in effective)
        {
            if (ProcessingIndependent.Contains(pair.Key))
            {
                continue;
            }

            relevant.Add(pair.Value == null ? pair.Key : pair.Key + "=" + pair.Value);
        }

        relevant.Sort(StringComparer.Ordinal);

        return new SnippetOptionSet(effective, explicitKeys, relevant, deprecated);
    }

    /// <summary>
    /// The formatter's default options — <c>book_base.py</c>'s <c>default_snippet_opts</c>
    /// widened by the texinfo format's own geometry.
    /// <para>
    /// ⚠ <c>papersize</c> IS one of them, and only when the snippet named neither a paper
    /// width nor a height: upstream writes the constructed default INTO the formatter's
    /// dictionary (<c>book_snippets.py:506-510</c>) rather than into the snippet's, which
    /// is why every snippet that does not size its own paper still carries a
    /// <c>#(set-paper-size ...)</c> line. Reproducing that as a per-snippet computation
    /// rather than as upstream's mutation of shared state gets the same composed bytes
    /// without the shared state.
    /// </para>
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> FormatterDefaults(
        TexinfoPageGeometry geometry, IDictionary<string, string> snippet)
    {
        yield return new KeyValuePair<string, string>(
            SnippetOptionNames.Alt, TexinfoPageGeometry.AltText);
        yield return new KeyValuePair<string, string>(
            SnippetOptionNames.PaperHeight, geometry.PaperHeight);
        yield return new KeyValuePair<string, string>(
            SnippetOptionNames.PaperWidth, geometry.PaperWidth);
        yield return new KeyValuePair<string, string>(
            SnippetOptionNames.LineWidth, geometry.LineWidth);
        yield return new KeyValuePair<string, string>(
            SnippetOptionNames.ExampleIndent, geometry.ExampleIndent);

        if (!snippet.ContainsKey(SnippetOptionNames.PaperWidth)
            && !snippet.ContainsKey(SnippetOptionNames.PaperHeight))
        {
            SplitDimension(geometry.PaperWidth, geometry.PaperWidth,
                out string width, out string _);
            SplitDimension(geometry.PaperHeight, geometry.PaperHeight,
                out string height, out string _2);
            yield return new KeyValuePair<string, string>(
                SnippetOptionNames.PaperSize,
                ConstructPaperSize(width, "pt", height, "pt"));
        }
    }

    private static string ConstructPaperSize(string width, string widthUnit, string height,
        string heightUnit)
        => "'(cons (* " + width + " " + widthUnit + ") (* " + height + " " + heightUnit + "))";

    /// <summary>
    /// Splits a dimension into its number and its unit the way
    /// <c>book_snippets.py:483-500</c> does, falling back to the format default's number
    /// with a <c>pt</c> unit when the value is not a dimension at all.
    /// </summary>
    private static void SplitDimension(string value, string fallback, out string number,
        out string unit)
    {
        Match match = DimensionRegex.Match(value ?? string.Empty);
        if (match.Success)
        {
            number = match.Groups[1].Value;
            unit = match.Groups[2].Value;
            return;
        }

        // `(w, w_unit) = (default_paper_width[:-3], "pt")' — three characters is exactly
        // the `\pt' the default carries.
        number = fallback != null && fallback.Length > 3
            ? fallback.Substring(0, fallback.Length - 3)
            : "0";
        unit = "pt";
    }

    /// <summary>
    /// <c>book_snippets.py:170-190</c> — <c>classic_lilypond_book_compatibility</c>. The
    /// corpus in scope triggers none of these (measured 2026-08-19), so this is a
    /// faithfulness net rather than a live path; a translation recorded in
    /// <see cref="DeprecatedOptions"/> is a finding worth reading.
    /// </summary>
    private static void ApplyClassicCompatibility(ref string key, ref string value,
        string written, List<string> deprecated)
    {
        string originalKey = key;
        string translatedKey = null;
        string translatedValue = null;

        if (string.Equals(key, "lilyquote", StringComparison.Ordinal))
        {
            translatedKey = SnippetOptionNames.Quote;
            translatedValue = value;
        }
        else if (string.Equals(key, "singleline", StringComparison.Ordinal) && value == null)
        {
            translatedKey = SnippetOptionNames.RaggedRight;
        }
        else
        {
            Match relative = ClassicRelativeRegex.Match(key);
            Match staffSize = ClassicStaffSizeRegex.Match(key);
            if (relative.Success && !string.Equals(key, SnippetOptionNames.Relative,
                    StringComparison.Ordinal))
            {
                translatedKey = SnippetOptionNames.Relative;
                translatedValue = relative.Groups[1].Value;
            }
            else if (staffSize.Success)
            {
                translatedKey = SnippetOptionNames.StaffSize;
                translatedValue = staffSize.Groups[1].Value;
            }
            else if ((string.Equals(key, SnippetOptionNames.Indent, StringComparison.Ordinal)
                    || string.Equals(key, SnippetOptionNames.LineWidth, StringComparison.Ordinal))
                && !string.IsNullOrEmpty(value))
            {
                Match bare = BareUnitRegex.Match(value);
                if (bare.Success)
                {
                    double amount = double.Parse(bare.Groups[1].Value,
                        CultureInfo.InvariantCulture);
                    translatedKey = key;
                    translatedValue = amount.ToString("F6", CultureInfo.InvariantCulture)
                        + "\\" + bare.Groups[2].Value;
                }
            }
        }

        if (translatedKey == null)
        {
            return;
        }

        deprecated.Add(written + " => " + translatedKey
            + (translatedValue == null ? string.Empty : "=" + translatedValue));
        key = translatedKey;
        value = translatedValue;
        _ = originalKey;
    }

    private static void SplitOption(string written, out string key, out string value)
    {
        int separator = written.IndexOf('=');
        if (separator < 0)
        {
            key = written.Trim();
            value = null;
            return;
        }

        // `re.split(r'\s*=\s*', option)' — the spaces around the sign are not part of
        // either half.
        key = written.Substring(0, separator).Trim();
        value = written.Substring(separator + 1).Trim();
    }
}
