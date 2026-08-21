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
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 5 (2026-08-14) end to end: a text run's width, as it reaches the SVG, is a
/// whole number of Pango's device dots — and the rounding happens PER GLYPH, with the
/// kern pair adjustment inside it.
/// <para>
/// Upstream never renders to a device, but every text metric it takes still travels
/// through Pango at <c>PANGO_RESOLUTION</c> = 1200 dots per inch
/// (<c>lily/include/pango-font.hh</c>), and Pango rounds each shaped glyph's advance to
/// a whole dot before anything reads the run. One dot is
/// <c>INCH_TO_BP / (PANGO_RESOLUTION * output_scale)</c>, which at the default staff
/// size is 72 / (1200 * 1.7572990176) = 0.0341433 staff spaces.
/// </para>
/// <para>
/// The defect this fences: the port summed EXACT real advances. Each glyph was then
/// wrong by up to half a dot and the error accumulated along the run, which is why the
/// width error grew with the string. 158 pages of the regression corpus differed by
/// exactly 0.0148 — half the width error of one digit, because a bar number is centred
/// on its bar line — and 47 more by exactly twice that.
/// </para>
/// <para>
/// Read off the ORACLE before it was asserted (rule 35): pinned LilyPond 2.27.2, run
/// under the corpus's own font pinning, measures these four runs in C059-Roman at
/// A = 47 dots, H = 54, AV = 87, HH = 108. Two of those four are load bearing as
/// CONTROLS rather than as facts:
/// </para>
/// <list type="bullet">
/// <item><c>HH</c> is 108 = 2 * 54, not <c>round(2 * 53.676) = 107</c>. A port that
/// rounded the run's TOTAL instead of each glyph passes the single-glyph rows and fails
/// this one.</item>
/// <item><c>AV</c> is 87, not 88. The pair adjustment is inside the rounding —
/// <c>round(722 - 96)</c> — not outside it, where <c>47 + 47 - round(6.186)</c> would
/// give 88. A port that rounded advances and kerns separately passes every unkerned row
/// and fails this one.</item>
/// </list>
/// <para>
/// The width is read out of a <c>\box</c> markup, whose horizontal edge rect carries the
/// run's width plus twice the default <c>box-padding</c> of 0.3 and nothing else. The
/// DIFFERENCE fences below cancel that padding entirely, so they hold whatever the
/// padding default becomes.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class TextAdvanceGridEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    /// <summary>Twice <c>box-padding</c>, the only thing <c>\box</c> adds to the width.</summary>
    private const double BoxPadding = 0.6;

    /// <summary>
    /// One of Pango's device dots at the default staff size, in staff spaces:
    /// <c>INCH_TO_BP / (PANGO_RESOLUTION * output_scale)</c>, where the default
    /// <c>output-scale</c> is a 20 pt staff over four spaces expressed in millimetres.
    /// </summary>
    private static readonly double Dot
        = 72.0 / (1200.0 * (20.0 / 72.27 * 25.4 / 4.0));

    /// <summary>The SVG is written to four decimals, so a dot count carries that slack.</summary>
    private const double DotTolerance = 0.01;

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-textgrid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Every run in <paramref name="runs"/> boxed on its own note, measured in dots.
    /// <para>
    /// Boxes are keyed by ORDER, which is the order the notes appear in: the rects are
    /// emitted per note and the widths are read off the horizontal edges, whose
    /// <c>x</c> is the box's own left padding and whose height is the line thickness.
    /// </para>
    /// </summary>
    private static List<double> BoxedRunWidthsInDots(params string[] runs)
    {
        string source = Version + "\\score { \\new Staff {\n"
            + string.Concat(runs.Select(r => "  c'1^\\markup \\box \"" + r + "\"\n"))
            + "} }\n";

        BatchRunResult result = BatchRunner.RunText(
            source, "textgrid", null, ScratchDirectory());
        result.SvgPath.Should().NotBeNull();
        string svg = File.ReadAllText(result.SvgPath);

        // A box is four rects in one group: two vertical edges and two horizontal ones.
        // The horizontal pair share the box's full width and are the widest rects whose
        // left edge sits at the negative padding.
        List<(double X, double Width)> edges = new List<(double, double)>();
        foreach (Match m in Regex.Matches(
            svg,
            "<g transform=\"translate\\(([-0-9.]+), [-0-9.]+\\)\">\\s*"
            + "<rect x=\"(-?[0-9.]+)\" y=\"[-0-9.]+\" width=\"([0-9.]+)\" "
            + "height=\"([0-9.]+)\""))
        {
            double left = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double width = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            double height = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (Math.Abs(left + 0.3) < 1e-9 && height < width)
            {
                edges.Add((
                    double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), width));
            }
        }

        // Two horizontal edges per box, both the same width; one group per box, and the
        // groups run left to right across the system.
        List<double> widths = edges
            .OrderBy(e => e.X)
            .Select(e => e.Width)
            .Distinct()
            .ToList();
        widths.Count.Should().Be(runs.Length);
        return widths.Select(w => (w - BoxPadding) / Dot).ToList();
    }

    [Fact]
    public void a_text_run_reaches_the_svg_on_a_whole_number_of_device_dots()
    {
        //Arrange & Act
        // Ordered by the x they land at, which is the order the notes are written in.
        List<double> dots = BoxedRunWidthsInDots("A", "H", "AV", "HH");

        //Assert
        // The oracle's own dot counts, read first. Each is an INTEGER, which is the
        // contract: an exact-real advance sum lands between dots and cannot pass.
        dots[0].Should().BeApproximately(47.0, DotTolerance);
        dots[1].Should().BeApproximately(54.0, DotTolerance);
        dots[2].Should().BeApproximately(87.0, DotTolerance);
        dots[3].Should().BeApproximately(108.0, DotTolerance);
    }

    [Fact]
    public void the_dot_rounding_is_per_glyph_and_not_per_run()
    {
        //Arrange & Act
        List<double> dots = BoxedRunWidthsInDots("H", "HH");

        //Assert
        // THE CONTROL that separates the two roundings. H's exact advance is 53.676
        // dots. Rounding the RUN gives round(107.35) = 107; rounding each GLYPH gives
        // 54 + 54 = 108. The oracle says 108, so doubling the single-glyph count must
        // land exactly on the pair's count.
        (2.0 * dots[0]).Should().BeApproximately(dots[1], 2.0 * DotTolerance);
        dots[1].Should().BeApproximately(108.0, DotTolerance);
    }

    [Fact]
    public void a_kern_pair_is_rounded_together_with_the_advance_it_belongs_to()
    {
        //Arrange & Act
        List<double> dots = BoxedRunWidthsInDots("A", "AV");

        //Assert
        // THE CONTROL for where the kern sits relative to the rounding. A is 47 dots
        // and its exact advance is 46.523; the A,V pair adjustment is -96 design units,
        // -6.186 dots. Inside the rounding: round(46.523 - 6.186) + 47 = 40 + 47 = 87.
        // Outside it: 47 - 6 + 47 = 88. The oracle says 87.
        dots[1].Should().BeApproximately(87.0, DotTolerance);
        (dots[1] - dots[0]).Should().BeApproximately(40.0, 2.0 * DotTolerance);

        // ...and the kern is still doing work at all: without it the pair would be
        // 2 * 47 = 94 dots, so the run must be strictly narrower than twice one glyph.
        dots[1].Should().BeLessThan(2.0 * dots[0] - 1.0);
    }
}
