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
/// PARITY 12's fence for D35: a CROSS-STAFF grob must not change how far apart the
/// staves sit.
/// <para>
/// The defect was never in the spacing arithmetic, and every stage of it answered
/// correctly — the alignment stacked the staves at their minimum distance, the spring
/// carried <c>basic-distance</c> as its ideal, and the page layout solved that spring to
/// exactly 9. What went wrong is that the staves had ALREADY been positioned by the
/// before-line-breaking stand-in, and <c>find_system_offsets</c> places a staff with
/// <c>translate_axis</c>, which ADDS. The correct 9 landed on top of an existing 9 and
/// every cross-staff score came out with its staves exactly one staff-staff-spacing too
/// far apart.
/// </para>
/// <para>
/// What triggered the early positioning was <c>Stem::internal_pure_height</c> reading the
/// ORDINARY relative coordinate where upstream reads <c>pure_relative_y_coordinate</c>.
/// The two answer the same NUMBER — which is what the port's note recorded, and why the
/// divergence looked free — but reading an ordinary Y offset forces
/// <c>Y-parent-positioning</c>, which forces the alignment's <c>positioning-done</c>. The
/// cost of that read is a SIDE EFFECT, not a value, and it is trap 24: a pure callback
/// must not reach into the unpure machinery. Only cross-staff scores paid it, because
/// walking <c>normal-stems</c> for coordinates is what a cross-staff beam makes the stem
/// do.
/// </para>
/// <para>
/// Expected values are read off their authority, not off the port:
/// <c>scm/define-grobs.scm</c> gives <c>VerticalAxisGroup.default-staff-staff-spacing</c>
/// a <c>basic-distance</c> of 9, and a staff's refpoint is its middle line. The third
/// case is the control that must come out DIFFERENTLY — without it, a port that answered
/// one fixed distance to every question would satisfy the first two.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class CrossStaffSpacingEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-crossstaff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// The distance between the two staves' middle lines: ten distinct horizontal
    /// staff-line rows, middles at index 2 and 7. Vertical lines (x1 == x2) must not be
    /// counted, and the y a line is DRAWN at lives in its enclosing translate.
    /// </summary>
    private static double MiddleLineDistance(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());
        result.SvgPath.Should().NotBeNull();

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

    /// <summary>
    /// The upper staff's music, over a bass staff of spacer rests.
    /// <para>
    /// This is <c>tests/regression/dynamics-avoid-cross-staff-stem.ly</c>'s own material,
    /// used rather than invented because an invented near-equivalent DID NOT REPRODUCE:
    /// the first draft of this fence — the same four eighths, same staff change, same
    /// dynamic, but wrapped in <c>\score</c> and spelled with absolute octaves — passed
    /// identically with the fix reverted and fenced nothing at all. What reaches
    /// <c>Stem::internal_pure_height</c> early enough to matter depends on the whole
    /// pure-height demand chain, and that is not something to guess at.
    /// </para>
    /// </summary>
    private static string CrossStaffSource(string with)
        => Version
        + "<<\n"
        + "  \\new Staff = \"up\" " + with + "\n"
        + "    \\relative {\n"
        + "      f'8\n"
        + "      \\change Staff = \"down\"\n"
        + "      c e\\f\n"
        + "      \\change Staff = \"up\"\n"
        + "      f\n"
        + "    }\n"
        + "  \\new Staff = \"down\" { \\clef bass s2 }\n"
        + ">>\n";

    /// <summary>The same two staves, with the staff change and the dynamic removed.</summary>
    private static string PlainSource(string with)
        => Version
        + "<<\n"
        + "  \\new Staff = \"up\" " + with + "\n"
        + "    \\relative { f'8 c' e' f' }\n"
        + "  \\new Staff = \"down\" { \\clef bass s2 }\n"
        + ">>\n";

    [Fact]
    public void a_cross_staff_score_sits_at_the_basic_distance()
    {
        //Arrange / Act
        // define-grobs.scm gives default-staff-staff-spacing a basic-distance of 9, and
        // nothing here demands more, so the refpoints are 9 apart. Before the fix this
        // read 18.0 -- the same 9 applied twice.
        double distance = MiddleLineDistance(
            CrossStaffSource(string.Empty), "crossstaff-change");

        //Assert
        distance.Should().Be(9.0);
    }

    [Fact]
    public void the_same_music_without_the_staff_change_sits_at_the_same_distance()
    {
        //Arrange / Act
        // The RELATIONSHIP this fence is really about: whether notes are engraved on the
        // other staff is not a question about how far apart the staves go. This half
        // always passed -- it is here so the pair states the invariant rather than a
        // number, and so a future change that moved BOTH would still be visible.
        double distance = MiddleLineDistance(PlainSource(string.Empty), "crossstaff-plain");

        //Assert
        distance.Should().Be(9.0);
    }

    [Fact]
    public void an_overridden_basic_distance_does_move_the_staves_apart()
    {
        //Arrange / Act
        // THE CONTROL, and it must come out differently: an engine that answered one
        // fixed number to every spacing question would satisfy both facts above. The
        // override names the distance, so the expected value is the override itself.
        double distance = MiddleLineDistance(
            CrossStaffSource(
                "\\with { \\override VerticalAxisGroup.staff-staff-spacing.basic-distance = #20 }"),
            "crossstaff-override");

        //Assert
        distance.Should().Be(20.0);
    }
}
