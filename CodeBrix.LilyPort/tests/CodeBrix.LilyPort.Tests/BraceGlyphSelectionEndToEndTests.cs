// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 6 (2026-08-14) end to end: <c>\left-brace</c> chooses its glyph by binary
/// searching the <c>fetaBraces</c> font's Y extents, so the font must be SCALED the way
/// upstream scales it and must report the number of glyphs upstream reports.
/// <para>
/// Two defects on one path, both found by a probe that ran unchanged on both engines.
/// First, <c>font-select.cc:164</c> runs fetaMusic, fetaBraces and fetaText through ONE
/// call to <c>best_rounded_design_size</c> and then scales fetaMusic and fetaBraces alike
/// by <c>requested_size / actual_size</c>; the port's brace branch instead divided by the
/// brace OTF's own <c>design_size</c>, which is recorded in millimetres, so every brace
/// glyph measured 2.84528x too tall — exactly <c>1 / ly:pt(1)</c>. Second,
/// <c>Open_type_font::count</c> answers <c>index_to_charcode_map_.size ()</c>, counting
/// only glyphs a charcode reaches, where the port counted the CFF charset and so included
/// <c>.notdef</c>: one too many, which handed the search a top index whose glyph does not
/// exist and whose extent is empty.
/// </para>
/// <para>
/// Together they cost 136 pages, every one of them GLYPHS-DIFFER rather than a placement
/// error, because the port compensated with the transform: the oracle drew
/// <c>brace177@0.0040</c> where the port drew <c>brace49@0.0114</c> and the brace came out
/// about the right height on the page while being the wrong glyph.
/// </para>
/// <para>
/// Read off the ORACLE before it was asserted (rule 35): pinned LilyPond 2.27.2, run
/// under the corpus's own font pinning per trap 8b, answers glyph-count 575 (after the
/// vendored <c>(1- ...)</c>), and a search that lands on 91 for a 35 pt brace and 121 for
/// a 45 pt one. The two sizes are each other's CONTROL — a font scaled wrongly by any
/// constant still answers SOME index for both, so a single expected number would pass on
/// a broken scale; two indices in the right RATIO will not.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class BraceGlyphSelectionEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    /// <summary>
    /// Reports the brace font's glyph count and the index the binary search picks at two
    /// sizes, as one space-free token so the whole verdict survives as a single text run.
    /// </summary>
    private const string Source = Version
        + "#(define-markup-command (brace-verdict layout props) ()\n"
        + "   (let* ((font (ly:paper-get-font layout\n"
        + "                  (cons '((font-encoding . fetaBraces) (font-name . #f)) props)))\n"
        + "          (count (1- (ly:otf-glyph-count font)))\n"
        + "          (scale (ly:output-def-lookup layout 'output-scale))\n"
        + "          (gy (lambda (n)\n"
        + "                (interval-length\n"
        + "                 (ly:stencil-extent\n"
        + "                  (ly:font-get-glyph font\n"
        + "                    (string-append \"brace\" (number->string n))) Y))))\n"
        + "          (pick (lambda (pt)\n"
        + "                  (binary-search 0 count gy (/ (ly:pt pt) scale)))))\n"
        + "     (interpret-markup layout props\n"
        + "       (markup (string-append \"COUNT=\" (number->string count)\n"
        + "                              \"|P35=\" (number->string (pick 35))\n"
        + "                              \"|P45=\" (number->string (pick 45)))))))\n"
        + "\\markup \\brace-verdict\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-brace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void brace_font_reports_the_glyph_count_and_search_indices_the_oracle_reports()
    {
        //Arrange & Act
        BatchRunResult result = BatchRunner.RunText(
            Source, "brace", null, ScratchDirectory());

        //Assert
        result.SvgPath.Should().NotBeNull();
        string svg = File.ReadAllText(result.SvgPath);

        Match verdict = Regex.Match(svg, "<tspan>(COUNT=[^<]*)</tspan>");
        verdict.Success.Should().BeTrue();

        // The oracle's own three numbers. Before the fix this read COUNT=576|P35=7|P45=22.
        verdict.Groups[1].Value.Should().Be("COUNT=575|P35=91|P45=121");
    }

    [Fact]
    public void the_brace_a_grand_staff_draws_is_not_the_brace_a_tiny_staff_draws()
    {
        //Arrange
        // The relationship fence (rule 33): whatever the absolute indices are, a smaller
        // staff MUST select a smaller brace. A font stuck at one scale would answer the
        // same index for both and this would fail without naming any literal.
        const string Pair = Version
            + "#(define-markup-command (pick layout props sz) (number?)\n"
            + "   (let* ((font (ly:paper-get-font layout\n"
            + "                  (cons '((font-encoding . fetaBraces) (font-name . #f)) props)))\n"
            + "          (count (1- (ly:otf-glyph-count font)))\n"
            + "          (scale (ly:output-def-lookup layout 'output-scale))\n"
            + "          (gy (lambda (n)\n"
            + "                (interval-length\n"
            + "                 (ly:stencil-extent\n"
            + "                  (ly:font-get-glyph font\n"
            + "                    (string-append \"brace\" (number->string n))) Y)))))\n"
            + "     (interpret-markup layout props\n"
            + "       (markup (number->string (binary-search 0 count gy (/ (ly:pt sz) scale)))))))\n"
            + "\\markup \\concat { \"BIG=\" \\pick #60 \"|SMALL=\" \\pick #12 }\n";

        //Act
        BatchRunResult result = BatchRunner.RunText(
            Pair, "bracepair", null, ScratchDirectory());

        //Assert
        result.SvgPath.Should().NotBeNull();
        string svg = File.ReadAllText(result.SvgPath);

        Match big = Regex.Match(svg, "<tspan>BIG=</tspan>");
        big.Success.Should().BeTrue();

        string text = Regex.Replace(svg, "<[^>]*>", string.Empty);
        Match numbers = Regex.Match(text, @"BIG=\s*(\d+)\s*\|SMALL=\s*(\d+)");
        numbers.Success.Should().BeTrue();

        int bigIndex = int.Parse(numbers.Groups[1].Value);
        int smallIndex = int.Parse(numbers.Groups[2].Value);
        bigIndex.Should().BeGreaterThan(smallIndex);
    }
}
