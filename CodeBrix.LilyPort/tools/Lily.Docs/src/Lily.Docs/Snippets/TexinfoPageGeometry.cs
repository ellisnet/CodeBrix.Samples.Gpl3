// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Lily.Docs.Snippets;

/// <summary>
/// The page geometry lilypond-book hands every snippet as its formatter defaults: the
/// width its music is set to, the indentation the <c>quote</c> option subtracts from
/// that width, and the paper size a snippet gets when it names none.
/// <para>
/// Ported from <c>python/book_base.py:67-73</c> (<c>default_snippet_opts</c>) and
/// <c>python/book_texinfo.py</c> (<c>texinfo_line_widths</c> and
/// <c>get_texinfo_width_indent</c>), read in <c>book-mirror/</c> and in the pinned
/// checkout at committish <c>2d621459bd44cb1758f822a69757242eab843060</c>.
/// </para>
/// <para>
/// ⚠ WHY THIS TYPE EXISTS AT ALL, AND WHY IT CARRIES CONSTANTS. Upstream does not
/// declare this geometry — it MEASURES it, by writing a small Texinfo document and
/// running <c>texi2pdf</c> on it to read TeX's own <c>\hsize</c> and
/// <c>\lispnarrowing</c> back out (<c>get_texinfo_width_indent</c>). That probe is a
/// TeX-backend question, and decision D28 gives our rendering to the Texinfo packages
/// and their Html2Pdf chain, which has no TeX in it. So the probe is both unavailable
/// here and meaningless: there is no <c>\hsize</c> to ask about.
/// </para>
/// <para>
/// Upstream itself defines what to do when the probe cannot run — it falls back to
/// <c>texinfo_line_widths</c> keyed on the document's page-size command, with
/// <c>6\in</c> and <c>0.4\in</c> as last resorts. That documented fallback is the
/// behaviour this type reproduces, with ONE refinement that was MEASURED rather than
/// assumed, on 2026-08-19:
/// </para>
/// <para>
/// For <c>@afourpaper</c> — which every snippet-bearing manual in scope declares
/// (measured across all nine of D48's manuals) — the probe was run by hand and returned
/// <c>textwidth=455.24408pt, exampleindent=28.90755pt</c>. Put through
/// <c>get_texinfo_width_indent</c>'s own arithmetic those become <c>160\mm</c> and
/// <c>10.16\mm</c>. The width is EXACTLY the value the fallback table already carries,
/// which is why that table says <c>160\mm</c>. The indent is the same LENGTH as the
/// <c>0.4\in</c> fallback but a different SPELLING of it — and the spelling is
/// load-bearing, because it is written into the composed source verbatim in two places:
/// the <c>%% Options:</c> provenance comment on every snippet, and the <c>quote</c>
/// option's <c>line-width = W - 2.0 * INDENT</c> arithmetic. Carrying the probed
/// spelling is what lets our composed source match lilypond-book's byte for byte;
/// carrying <c>0.4\in</c> would differ on every snippet of every manual while meaning
/// precisely the same thing.
/// </para>
/// <para>
/// The probe that produced those numbers, for a future session that needs to re-measure
/// (it is an ORACLE run — by hand, never a build step):
/// </para>
/// <code>
/// printf '\\input texinfo\n@settitle t\n@afourpaper\n\n@message{Global: textwidth=@the@hsize,exampleindent=@the@lispnarrowing}\n\ndummy\n\n@bye\n' &gt; p.texi
/// LC_ALL=C texi2pdf -c -o p.pdf p.texi
/// </code>
/// </summary>
public sealed class TexinfoPageGeometry
{
    /// <summary>
    /// A4 paper defaults, from <c>book_base.py:67-73</c>. The comment there records what
    /// they are: "A4 paper defaults. The default line width is set in function
    /// <c>compose_ly</c>".
    /// </summary>
    internal const string A4PaperWidth = @"597.508\pt";

    /// <summary>The A4 paper height default, from <c>book_base.py:67-73</c>.</summary>
    internal const string A4PaperHeight = @"845.047\pt";

    /// <summary>
    /// The alt text every snippet carries, from <c>book_base.py:67-73</c>. It is a
    /// processing-independent option, so it never reaches the composed source — but it
    /// IS in the option dictionary, and leaving it out would change nothing visible and
    /// still be a divergence from the authority.
    /// </summary>
    internal const string AltText = "[image of music]";

    /// <summary>
    /// The page-size commands <c>get_texinfo_width_indent</c> looks for, in the order
    /// its own regular expression lists them.
    /// </summary>
    private static readonly string[] PageSizeCommands =
    {
        "@afourpaper", "@afourwide", "@afourlatex", "@afivepaper", "@smallbook",
        "@letterpaper",
    };

    /// <summary>
    /// <c>book_texinfo.py</c>'s <c>texinfo_line_widths</c>, verbatim. ⚠ Note that
    /// <c>@afivepaper</c> is ABSENT from it upstream, so a document declaring that size
    /// falls to the <c>6\in</c> default — reproduced rather than corrected.
    /// </summary>
    private static readonly Dictionary<string, string> FallbackLineWidths =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "@afourpaper", @"160\mm" },
            { "@afourwide", @"6.5\in" },
            { "@afourlatex", @"150\mm" },
            { "@smallbook", @"5\in" },
            { "@letterpaper", @"6\in" },
        };

    /// <summary>
    /// The example indents MEASURED from the TeX probe, per page size. Only
    /// <c>@afourpaper</c> has been measured, because it is the only size any manual in
    /// D48's scope declares; every other size takes upstream's documented
    /// <c>0.4\in</c> fallback, and a session that renders such a manual should measure
    /// its size with the probe in this type's remarks and add it here.
    /// </summary>
    private static readonly Dictionary<string, string> MeasuredExampleIndents =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "@afourpaper", @"10.16\mm" },
        };

    private static readonly Regex PageSizeRegex = new Regex(
        @"@(?:afourpaper|afourwide|afourlatex|afivepaper|smallbook|letterpaper)",
        RegexOptions.Compiled);

    private TexinfoPageGeometry(string pageSizeCommand, string lineWidth, string exampleIndent)
    {
        PageSizeCommand = pageSizeCommand;
        LineWidth = lineWidth;
        ExampleIndent = exampleIndent;
    }

    /// <summary>
    /// The geometry of the manuals in scope — every one of them declares
    /// <c>@afourpaper</c> (measured 2026-08-19 across all nine of D48's manuals).
    /// </summary>
    public static TexinfoPageGeometry AfourPaper { get; } = ForPageSize("@afourpaper");

    /// <summary>The page-size command this geometry was derived from, or an empty string.</summary>
    public string PageSizeCommand { get; }

    /// <summary>The default <c>line-width</c>, as a LilyPond dimension string.</summary>
    public string LineWidth { get; }

    /// <summary>The default <c>exampleindent</c>, as a LilyPond dimension string.</summary>
    public string ExampleIndent { get; }

    /// <summary>The default paper width, as a LilyPond dimension string.</summary>
    public string PaperWidth => A4PaperWidth;

    /// <summary>The default paper height, as a LilyPond dimension string.</summary>
    public string PaperHeight => A4PaperHeight;

    /// <summary>
    /// Derives the geometry from a manual's own source text, the way
    /// <c>get_texinfo_width_indent</c> does: by finding its page-size command.
    /// </summary>
    /// <param name="source">The manual's source text, or any part of it carrying the
    /// page-size command.</param>
    /// <returns>The geometry that text implies.</returns>
    public static TexinfoPageGeometry ForSource(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return ForPageSize(string.Empty);
        }

        Match match = PageSizeRegex.Match(source);
        return ForPageSize(match.Success ? match.Value : string.Empty);
    }

    /// <summary>
    /// Returns the geometry for one page-size command.
    /// </summary>
    /// <param name="pageSizeCommand">A command such as <c>@afourpaper</c>, or an empty
    /// string for a document that declares none.</param>
    /// <returns>The geometry for that page size.</returns>
    public static TexinfoPageGeometry ForPageSize(string pageSizeCommand)
    {
        string command = pageSizeCommand ?? string.Empty;

        // texinfo_line_widths.get(pagesize, "6\\in") — book_texinfo.py's own last resort.
        string width = @"6\in";
        if (FallbackLineWidths.TryGetValue(command, out string tabled))
        {
            width = tabled;
        }

        // "0.4\\in" is what get_texinfo_width_indent falls back to when the probe gives
        // it no exampleindent to read.
        string indent = @"0.4\in";
        if (MeasuredExampleIndents.TryGetValue(command, out string measured))
        {
            indent = measured;
        }

        return new TexinfoPageGeometry(command, width, indent);
    }

    /// <summary>Whether a page-size command is one upstream recognises.</summary>
    /// <param name="pageSizeCommand">The command to test.</param>
    /// <returns>True when upstream's regular expression would match it.</returns>
    public static bool IsKnownPageSize(string pageSizeCommand)
        => Array.IndexOf(PageSizeCommands, pageSizeCommand ?? string.Empty) >= 0;
}
