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
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// The fence the STAFF-LINES session (2026-08-11) owed once collapsed staff lines were
/// fixed: <c>alignment-distances</c> in <c>line-break-system-details</c> must really
/// place staves, observed in the SVG's translate transforms. It was deliberately not
/// written earlier because asserting staff placement over collapsed staff lines would
/// have fenced broken geometry.
/// <para>
/// Semantics from upstream (<c>alignment-vertical-manual-setting.ly</c> and
/// <c>VerticalAlignment</c>'s documented behaviour): each entry is the forced distance
/// between adjacent staves' refpoints, and a staff's refpoint is its middle line. The
/// expected gaps are the override values themselves — hand-carried, not recorded — and
/// the two facts are a pair that must come out DIFFERENTLY, because a port that ignored
/// the override entirely would answer both with the same skyline-driven distance.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class AlignmentDistancesEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-aligndist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BatchRunResult Run(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.SvgPath.Should().NotBeNull();
        return result;
    }

    /// <summary>
    /// The distance between the two staves' middle lines: ten distinct horizontal
    /// staff-line rows, middles at index 2 and 7. Vertical lines (x1 == x2) must not
    /// be counted.
    /// </summary>
    private static double MiddleLineDistance(BatchRunResult result)
    {
        string svg = File.ReadAllText(result.SvgPath);
        List<double> ys = new List<double>();
        foreach (Match m in Regex.Matches(
            svg, "<g transform=\"translate\\(([-0-9.]+), ([-0-9.]+)\\)\"[^>]*>\\s*(<line [^>]*>)"))
        {
            Dictionary<string, double> attrs = Regex
                .Matches(m.Groups[3].Value, "\\b(x1|x2|y1|y2)=\"([-0-9.]+)\"")
                .ToDictionary(
                    a => a.Groups[1].Value,
                    a => double.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture));
            if (attrs.Count == 4
                && Math.Abs(attrs["y1"] - attrs["y2"]) < 1e-6
                && Math.Abs(attrs["x2"] - attrs["x1"]) > 5.0)
            {
                ys.Add(Math.Round(
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                    + attrs["y1"], 3));
            }
        }

        List<double> rows = ys.Distinct().OrderBy(y => y).ToList();
        rows.Count.Should().Be(10);
        return Math.Round(rows[7] - rows[2], 3);
    }

    private static double DistanceUnder(int alignmentDistance, string name)
    {
        string source =
            Version
            + "\\score { \\new StaffGroup <<\n"
            + "  \\new Staff {\n"
            + "    \\once \\override Score.NonMusicalPaperColumn.line-break-system-details =\n"
            + "    #'((alignment-distances . (" + alignmentDistance.ToString(CultureInfo.InvariantCulture) + ")))\n"
            + "    c'1\n"
            + "  }\n"
            + "  \\new Staff { c'1 }\n"
            + ">> }\n";

        return MiddleLineDistance(Run(source, name));
    }

    [Fact]
    public void alignment_distances_place_the_staff_refpoints_exactly()
    {
        //Arrange / Act
        // The override value 15 is the whole arrangement; the helper renders and reads.
        double distance = DistanceUnder(15, "aligndist-fifteen");

        //Assert
        (Math.Abs(distance - 15.0) < 1e-3).Should().BeTrue(
            "a forced alignment-distance of 15 puts the middle lines 15 apart"
            + " (got " + distance.ToString("F4", CultureInfo.InvariantCulture) + ")");
    }

    [Fact]
    public void a_different_alignment_distance_moves_the_lower_staff()
    {
        //Arrange / Act
        // The control that must come out differently from the fact above.
        double distance = DistanceUnder(11, "aligndist-eleven");

        //Assert
        (Math.Abs(distance - 11.0) < 1e-3).Should().BeTrue(
            "a forced alignment-distance of 11 puts the middle lines 11 apart"
            + " (got " + distance.ToString("F4", CultureInfo.InvariantCulture) + ")");
    }
}
