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
/// BOOK-ORDER session (2026-08-12) end to end: a multi-score file renders its scores in
/// PARSE order.
/// <para>
/// The defect this fences was a silent DOUBLE reversal: upstream's <c>ly:make-book</c>
/// appends the score list wholesale (<c>ly_append</c>, book-scheme.cc) and
/// <c>Book::process</c> reverses on the way out, while the port's primitive consed per
/// score — so every multi-score file rendered BACKWARDS, byte-identical outputs under
/// each other's names. The comparator could not see it: a reordered page is inside the
/// GLYPHS-DIFFER bucket already. Found by the MIDI scoreboard re-read (65/90 where the
/// board said 79/90), sha256-proven on <c>swing-tripletfeel-partial.ly</c>.
/// </para>
/// <para>
/// The two facts are a swapped pair, per the fences-assert-RELATIONSHIPS rule: each
/// asserts which score sits in the TOP system by a hand-computed notehead offset
/// (c'' is position +1 → half a unit ABOVE its staff's middle line; a' is position
/// −1 → half a unit BELOW), and the pair must come out OPPOSITE — a port that ignored
/// source order entirely would answer both the same way.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class BookOrderEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-bookorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Renders a two-score file and answers the notehead offset from the middle line,
    /// in staff units, for the TOP system's staff and the BOTTOM system's staff.
    /// </summary>
    private static (double Top, double Bottom) TopAndBottomNoteheadOffsets(
        string firstPitch, string secondPitch, string name)
    {
        string source =
            Version
            + "\\score { \\new Staff { " + firstPitch + "1 } }\n"
            + "\\score { \\new Staff { " + secondPitch + "1 } }\n";

        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());
        result.SvgPath.Should().NotBeNull();
        string svg = File.ReadAllText(result.SvgPath);

        // The two staves' middle lines, from the ten distinct horizontal line rows.
        List<double> rows = new List<double>();
        foreach (Match m in Regex.Matches(
            svg, "<g transform=\"translate\\(([-0-9.]+), ([-0-9.]+)\\)\"[^>]*>\\s*(<line [^>]*>)"))
        {
            Dictionary<string, double> attrs = Regex
                .Matches(m.Groups[3].Value, "\\b(x1|x2|y1|y2)=\"([-0-9.]+)\"")
                .ToDictionary(
                    x => x.Groups[1].Value,
                    x => double.Parse(x.Groups[2].Value, CultureInfo.InvariantCulture));
            if (attrs.Count == 4
                && Math.Abs(attrs["y1"] - attrs["y2"]) < 1e-6
                && Math.Abs(attrs["x2"] - attrs["x1"]) > 5.0)
            {
                rows.Add(Math.Round(
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                    + attrs["y1"], 3));
            }
        }

        List<double> lines = rows.Distinct().OrderBy(y => y).ToList();
        lines.Count.Should().Be(10);
        double topMiddle = lines[2];
        double bottomMiddle = lines[7];

        // Each system's notehead: the right-most music-glyph path in that staff's zone.
        var glyphs = new List<(double X, double Y)>();
        foreach (Match m in Regex.Matches(
            svg, "<g transform=\"translate\\(([-0-9.]+), ([-0-9.]+)\\)\">\\s*<path transform=\"scale\\("))
        {
            glyphs.Add((
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        }

        double topHead = glyphs
            .Where(g => Math.Abs(g.Y - topMiddle) < 4.0)
            .OrderBy(g => g.X).Last().Y;
        double bottomHead = glyphs
            .Where(g => Math.Abs(g.Y - bottomMiddle) < 4.0)
            .OrderBy(g => g.X).Last().Y;

        return (Math.Round(topHead - topMiddle, 3), Math.Round(bottomHead - bottomMiddle, 3));
    }

    [Fact]
    public void the_first_score_of_a_two_score_file_renders_in_the_top_system()
    {
        //Arrange / Act
        // c'' is staff position +1 (−0.5 from the middle line, SVG y grows down);
        // a' is position −1 (+0.5). First score c'', second a'.
        (double top, double bottom) = TopAndBottomNoteheadOffsets("c''", "a'", "bookorder-forward");

        //Assert
        (Math.Abs(top - -0.5) < 1e-3).Should().BeTrue(
            "the FIRST score (c'') renders in the top system"
            + " (top offset " + top.ToString("F4", CultureInfo.InvariantCulture) + ")");
        (Math.Abs(bottom - 0.5) < 1e-3).Should().BeTrue(
            "the SECOND score (a') renders in the bottom system"
            + " (bottom offset " + bottom.ToString("F4", CultureInfo.InvariantCulture) + ")");
    }

    [Fact]
    public void swapping_the_scores_swaps_the_systems()
    {
        //Arrange / Act
        // The control that must come out OPPOSITE to the fact above.
        (double top, double bottom) = TopAndBottomNoteheadOffsets("a'", "c''", "bookorder-swapped");

        //Assert
        (Math.Abs(top - 0.5) < 1e-3).Should().BeTrue(
            "after the swap the a' score renders in the top system"
            + " (top offset " + top.ToString("F4", CultureInfo.InvariantCulture) + ")");
        (Math.Abs(bottom - -0.5) < 1e-3).Should().BeTrue(
            "after the swap the c'' score renders in the bottom system"
            + " (bottom offset " + bottom.ToString("F4", CultureInfo.InvariantCulture) + ")");
    }
}
