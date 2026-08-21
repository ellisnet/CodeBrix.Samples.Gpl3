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
/// PARITY 4 (2026-08-14) end to end: a side-positioned grob must be placed OUTSIDE the
/// extent of the support it is positioned against, not merely padded away from its
/// support's origin.
/// <para>
/// The bar number is the cheapest witness. <c>Bar_number_engraver</c>
/// (<c>lily/bar-number-engraver.cc</c>) sets <c>side-support-elements</c> from
/// <c>stavesFound</c>, so a bar number is positioned against the StaffSymbol, whose own
/// Y-extent is ±(2 staff spaces + half a staff-line thickness) = ±2.05.
/// <c>Side_position_interface::aligned_side</c> measures the distance between the two
/// SKYLINES and adds <c>padding</c>, so the offset from the staff's centre is
/// 2.05 + padding + whatever the digit's own outline hangs below its reference point.
/// </para>
/// <para>
/// The defect this fences: the port's skyline read aliased the grob's STORED skylines
/// instead of copying them the way <c>from_scm&lt;Skyline_pair&gt;</c> does, so by the
/// time <c>aligned_side</c> read the bar number's skyline it had already been
/// translated by that grob's own X coordinate. The two skylines then had no horizontal
/// overlap at all, <c>Skyline::distance</c> answered −infinity, and
/// <c>aligned_side</c>'s <c>!std::isinf (dist)</c> guard turned the whole support term
/// into ZERO. The bar number kept only its padding and so sat 2.05 staff spaces low —
/// INSIDE the staff. The bar number is only the loudest instance: 227 pages of the
/// regression corpus differed by one of the two constants this produces.
/// </para>
/// <para>
/// Read off the ORACLE before it was asserted (rule 35): pinned LilyPond 2.27.2 puts
/// the bar number 3.05 above the staff's centre line at the default padding of 1.0
/// (3.0675 for a "3", whose outline hangs 0.0175 below its reference point), and 5.05
/// at padding 3.0. The digit-outline term is font metrics and is not asserted; what is
/// asserted is that the staff's own 2.05 is in the sum at all, and that padding still
/// moves the number by exactly what it is raised by. The second is the control: it held
/// even while the defect was live, so a test that only checked padding would have
/// passed throughout.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class SidePositionStaffSupportEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    /// <summary>The StaffSymbol's own half-extent: 2 staff spaces + half a line.</summary>
    private const double StaffHalfExtent = 2.05;

    /// <summary>
    /// One three-bar staff with every bar number made visible, so the whole fixture is a
    /// single system. <paramref name="paddingOverride"/> is the only variable.
    /// </summary>
    private static string Source(string paddingOverride)
        => Version
            + "\\score { \\new Staff { c'1 c'1 c'1 }\n"
            + "  \\layout { \\context { \\Score\n"
            + "    barNumberVisibility = #all-bar-numbers-visible\n"
            + "    \\override BarNumber.break-visibility = ##(#t #t #t)\n"
            + paddingOverride
            + "  } } }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-sidepos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// The translate-y of the staff's middle line: the five distinct horizontal
    /// <c>&lt;line&gt;</c> rows, middle one. Vertical lines (x1 == x2) must not be
    /// counted.
    /// </summary>
    private static double MiddleLineY(string svg)
    {
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
        rows.Count.Should().Be(5);
        return rows[2];
    }

    /// <summary>
    /// How far every bar number sits ABOVE the staff's centre line, left to right. SVG y
    /// grows downward, so this is (centre − number). Bar numbers are the only text at
    /// the grob's font-size of −2 in this fixture, and three bars carry FOUR of them —
    /// one at each bar line, the final one included.
    /// <para>
    /// Ordered by x rather than taken in document order, because the two renders being
    /// compared must line up number for number: the digits do not all hang the same
    /// distance below their reference point, so pairing the wrong two would read as a
    /// placement difference.
    /// </para>
    /// </summary>
    private static List<double> BarNumberRisesAboveCentre(string paddingOverride, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            Source(paddingOverride), name, null, ScratchDirectory());
        result.SvgPath.Should().NotBeNull();

        string svg = File.ReadAllText(result.SvgPath);
        double centre = MiddleLineY(svg);

        List<(double X, double Rise)> found = new List<(double, double)>();
        foreach (Match m in Regex.Matches(
            svg,
            "<g transform=\"translate\\(([-0-9.]+), ([-0-9.]+)\\)\">\\s*"
            + "<text[^>]*font-size=\"1\\.7459\"[^>]*>"))
        {
            found.Add((
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                centre - double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        }

        found.Count.Should().Be(4);
        return found.OrderBy(f => f.X).Select(f => f.Rise).ToList();
    }

    [Fact]
    public void a_bar_number_clears_the_staff_symbol_it_is_positioned_against()
    {
        //Arrange
        // aligned_side answers 2.05 + padding + the digit's own depth, so at the default
        // padding of 1.0 every bar number must rise at least 3.05 above the centre line.
        // The broken port answered padding alone: 1.0, which is INSIDE the staff, below
        // even the top line's 2.0.
        const double DefaultPadding = 1.0;

        //Act
        List<double> rises = BarNumberRisesAboveCentre(string.Empty, "sidepos-default");

        //Assert
        foreach (double rise in rises)
        {
            rise.Should().BeGreaterThanOrEqualTo(StaffHalfExtent + DefaultPadding);
        }
    }

    [Fact]
    public void raising_a_bar_numbers_padding_moves_it_by_exactly_that_much()
    {
        //Arrange
        // The control for the fact above: the padding term was correct even while the
        // support term was being thrown away, so this must keep holding, and it pins the
        // 2.05 as a constant of the SUPPORT rather than of the padding.
        const double RaisedPadding = 3.0;
        const double DefaultPadding = 1.0;

        //Act
        List<double> atDefault = BarNumberRisesAboveCentre(string.Empty, "sidepos-a");
        List<double> atRaised = BarNumberRisesAboveCentre(
            "    \\override BarNumber.padding = #3.0\n", "sidepos-b");

        //Assert
        // The tolerance is the output's own precision, not a fudge: the SVG carries four
        // decimal places and the staff's centre line is rounded to three before the two
        // renders are subtracted, so a difference of two measurements can be off by 0.001
        // without anything having moved.
        atRaised.Count.Should().Be(atDefault.Count);
        for (int i = 0; i < atRaised.Count; i++)
        {
            (atRaised[i] - atDefault[i]).Should()
                .BeApproximately(RaisedPadding - DefaultPadding, 2e-3);
        }
    }
}
