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
/// STAFF-LINES session (2026-08-11) end to end: staff lines must have real LENGTH, and
/// systems must sit at the distances the paper asks for.
/// <para>
/// The defect these fence was invisible to the comparator by construction: a page whose
/// staff lines are collapsed to points holds the same glyphs in the same order, and
/// 74.4% of the port's horizontal lines were collapsed against the oracle's 4.9%. The
/// cause was an ordinary Y-extent read in <c>Separation_item::boxes</c> during
/// horizontal spacing — BEFORE line breaking — which dragged the ordinary side-position
/// and skyline machinery in and CACHED the StaffSymbol's stencil over still-unplaced
/// columns. The outside-staff markup in these sources is load-bearing: it is what makes
/// the side-position chain run during spacing, which is exactly the path that used to
/// poison the cache.
/// </para>
/// <para>
/// The system-distance pair fences a second defect of the same session:
/// <c>Page_layout_problem</c>'s <c>alter_spring_from_spacing_spec</c> took its
/// <c>Spring</c> — a struct — BY VALUE, so no spacing spec ever reached any page
/// spring, and every system sat at the skyline minimum instead of
/// <c>basic-distance</c>. The override case must come out DIFFERENTLY from the default
/// case, because a port that ignored the spec entirely would answer the default pair
/// identically.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class StaffLinesEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-stafflines-" + Guid.NewGuid().ToString("N"));
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
    /// Horizontal line spans on the page: |x2 - x1| for every <c>&lt;line&gt;</c>
    /// whose y1 == y2. Vertical bar lines and stems have x1 == x2 and must not be
    /// counted, which is why the horizontal filter is not optional.
    /// </summary>
    private static List<double> HorizontalLineSpans(BatchRunResult result)
    {
        string text = File.ReadAllText(result.SvgPath);
        List<double> spans = new List<double>();
        foreach (Match m in Regex.Matches(text, "<line [^>]*>"))
        {
            Dictionary<string, double> attrs = Regex
                .Matches(m.Value, "\\b(x1|x2|y1|y2)=\"([-0-9.]+)\"")
                .ToDictionary(
                    a => a.Groups[1].Value,
                    a => double.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture));
            if (attrs.Count == 4 && Math.Abs(attrs["y1"] - attrs["y2"]) < 1e-6)
            {
                spans.Add(Math.Abs(attrs["x2"] - attrs["x1"]));
            }
        }

        return spans;
    }

    /// <summary>
    /// The vertical gaps between consecutive staff-line rows: the translate-y of each
    /// group directly wrapping a <c>&lt;line&gt;</c>, deduplicated and differenced,
    /// keeping only the between-system gaps (anything larger than the 1-unit
    /// line-to-line pitch).
    /// </summary>
    private static List<double> SystemGaps(BatchRunResult result)
    {
        string text = File.ReadAllText(result.SvgPath);
        List<double> ys = Regex
            .Matches(text, "<g transform=\"translate\\(([-0-9.]+), ([-0-9.]+)\\)\"[^>]*>\\s*<line ")
            .Select(m => Math.Round(
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), 3))
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        List<double> gaps = new List<double>();
        for (int i = 1; i < ys.Count; i++)
        {
            double d = ys[i] - ys[i - 1];
            if (d > 2.0)
            {
                gaps.Add(d);
            }
        }

        return gaps;
    }

    [Fact]
    public void staff_lines_span_the_system_even_with_an_outside_staff_markup()
    {
        //Arrange
        // The markup is the trigger: side-positioning it during spacing is what used to
        // compute and cache the StaffSymbol stencil over unplaced columns, drawing all
        // five lines as 0.1-unit points. Two whole notes guarantee several units of
        // real width.
        string source =
            Version
            + "\\score { \\new Staff { c'1^\\markup { x } c'1 } }\n";

        //Act
        BatchRunResult result = Run(source, "stafflines-markup");

        //Assert
        List<double> spans = HorizontalLineSpans(result);
        spans.Count.Should().Be(5);
        foreach (double span in spans)
        {
            (span > 10.0).Should().BeTrue(
                "a staff line under two whole notes spans the system, not a point"
                + " (got " + span.ToString("F4", CultureInfo.InvariantCulture) + ")");
        }
    }

    [Fact]
    public void omitting_the_staff_symbol_draws_no_horizontal_lines_at_all()
    {
        //Arrange
        // The control that must come out DIFFERENTLY: without it, the fact above could
        // be satisfied by any long horizontal line — a beam is a polygon and a bar line
        // is vertical, but nothing else proves the measurement is the staff symbol's.
        string source =
            Version
            + "\\score { \\new Staff \\with { \\omit StaffSymbol }"
            + " { c'1^\\markup { x } c'1 } }\n";

        //Act
        BatchRunResult result = Run(source, "stafflines-omitted");

        //Assert
        HorizontalLineSpans(result).Count.Should().Be(0);
    }

    [Fact]
    public void two_systems_sit_at_the_paper_basic_distance()
    {
        //Arrange
        // Default system-system-spacing has basic-distance 12 (staff spaces, refpoint
        // to refpoint) — ly/paper-defaults-init.ly — and a five-line staff is 4 units
        // tall, so the visible gap between the bottom line of one system and the top
        // line of the next is 12 - 4 = 8. Derivable from the defaults, not recorded
        // from a run; the oracle emits exactly this.
        string source =
            Version
            + "\\layout { indent = 0.0 line-width = 40.0\\cm }\n"
            + "\\score { \\new Staff { c'1 \\break c'1 } }\n";

        //Act
        BatchRunResult result = Run(source, "stafflines-basic-distance");

        //Assert
        List<double> gaps = SystemGaps(result);
        gaps.Count.Should().Be(1);
        (Math.Abs(gaps[0] - 8.0) < 1e-3).Should().BeTrue(
            "two default systems sit basic-distance apart"
            + " (got " + gaps[0].ToString("F4", CultureInfo.InvariantCulture) + ")");
    }

    [Fact]
    public void a_spacing_override_moves_the_systems()
    {
        //Arrange
        // The pair that fences the by-value Spring defect: before the STAFF-LINES
        // session no spec value ever reached a page spring, so THIS case answered the
        // same gap as the default case above — which is why the two are written as a
        // pair that must come out differently. basic-distance 20 gives 20 - 4 = 16.
        string source =
            Version
            + "\\paper { system-system-spacing.basic-distance = #20 }\n"
            + "\\layout { indent = 0.0 line-width = 40.0\\cm }\n"
            + "\\score { \\new Staff { c'1 \\break c'1 } }\n";

        //Act
        BatchRunResult result = Run(source, "stafflines-spacing-override");

        //Assert
        List<double> gaps = SystemGaps(result);
        gaps.Count.Should().Be(1);
        (Math.Abs(gaps[0] - 16.0) < 1e-3).Should().BeTrue(
            "an explicit basic-distance must reach the page spring"
            + " (got " + gaps[0].ToString("F4", CultureInfo.InvariantCulture) + ")");
    }
}
