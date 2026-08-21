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
/// EPG15 end to end: LilyPond text that cannot fit on one line in, a page carrying
/// SEVERAL systems out, through the real <c>ly/engraver-init.ly</c> tree.
/// <para>
/// This is the reachability probe standing rule 4 asks for, and EPG15 needs it more than
/// most groups did, because its failure mode is silence. Line breaking can choose the
/// right lines and produce nothing visible: the breaker chose three lines for
/// <c>break.ly</c> for a whole session while the runner drew one of them. Registered ≠
/// behaving ≠ reachable ≠ DRAWN, and only the last of those is what a reader assumes.
/// </para>
/// <para>
/// The measurement is STAFF LINES rather than anything the port reports about itself: a
/// five-line staff draws five SVG <c>&lt;line&gt;</c> elements per system, so a page of
/// three systems holds fifteen and a page of one holds five. That number is derivable
/// from the notation rather than recorded from a run.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class LineBreakingEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    /// <summary>Five lines to a staff, so five per system.</summary>
    private const int StaffLinesPerSystem = 5;

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-breaks-" + Guid.NewGuid().ToString("N"));
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

    private static int StaffLineCount(BatchRunResult result)
        => Regex.Matches(File.ReadAllText(result.SvgPath), "<line ").Count;

    [Fact]
    public void music_too_long_for_one_line_is_broken_into_several()
    {
        //Arrange
        // Six whole notes at a four-centimetre line width. Before EPG15 this produced one
        // system holding all six, because PlaceColumnsOnOneLine spaced every score as a
        // single unbroken line whatever its width.
        string source =
            Version
            + "\\layout { indent = 0.0 line-width = 4.0\\cm }\n"
            + "\\score { \\new Staff { c'1 c'1 c'1 c'1 c'1 c'1 } }\n";

        //Act
        BatchRunResult result = Run(source, "epg15-narrow");

        //Assert
        result.SystemCount.Should().BeGreaterThan(1);
        StaffLineCount(result).Should().Be(result.SystemCount * StaffLinesPerSystem);
    }

    [Fact]
    public void the_same_music_on_a_wide_line_stays_one_system()
    {
        //Arrange
        // The control, and it is the one that matters: without it a build that broke every
        // score into pieces regardless of width would pass the test above.
        string source =
            Version
            + "\\layout { indent = 0.0 line-width = 40.0\\cm }\n"
            + "\\score { \\new Staff { c'1 c'1 c'1 c'1 c'1 c'1 } }\n";

        //Act
        BatchRunResult result = Run(source, "epg15-wide");

        //Assert
        result.SystemCount.Should().Be(1);
        StaffLineCount(result).Should().Be(StaffLinesPerSystem);
    }

    [Fact]
    public void a_manual_break_splits_music_that_would_otherwise_fit()
    {
        //Arrange
        // Two whole notes on a line wide enough for a dozen, with \break between them.
        // Nothing but the manual break can split this, so the second system is
        // handle_manual_breaks' own work: it writes line-break-permission = 'force onto
        // the command column, and get_line_forces stops extending a line past a forced
        // break.
        string source =
            Version
            + "\\layout { indent = 0.0 line-width = 40.0\\cm }\n"
            + "\\score { \\new Staff { c'1 \\break c'1 } }\n";

        //Act
        BatchRunResult result = Run(source, "epg15-manual-break");

        //Assert
        result.SystemCount.Should().Be(2);
        StaffLineCount(result).Should().Be(2 * StaffLinesPerSystem);
    }

    [Fact]
    public void the_same_music_without_the_break_stays_one_system()
    {
        //Arrange
        // The control for the manual break. Until EPG15's close-out the port had no
        // break-event listener at all, so \break did nothing of its own and BOTH of these
        // would have produced one system -- which is why the pair is written as a pair.
        string source =
            Version
            + "\\layout { indent = 0.0 line-width = 40.0\\cm }\n"
            + "\\score { \\new Staff { c'1 c'1 } }\n";

        //Act
        BatchRunResult result = Run(source, "epg15-no-manual-break");

        //Assert
        result.SystemCount.Should().Be(1);
        StaffLineCount(result).Should().Be(StaffLinesPerSystem);
    }

    [Fact]
    public void every_chosen_line_reaches_the_page()
    {
        //Arrange
        // The fence for the defect this session found: Engrave() took the FIRST paper
        // system and discarded the rest, so a score that broke into three lines drew one.
        // Two forced breaks give three systems whatever the spacing does, and the page has
        // to carry all three -- fifteen staff lines, and a taller drawing than the same
        // music unbroken.
        string broken =
            Version
            + "\\layout { indent = 0.0 line-width = 40.0\\cm }\n"
            + "\\score { \\new Staff { c'1 \\break c'1 \\break c'1 } }\n";
        string unbroken =
            Version
            + "\\layout { indent = 0.0 line-width = 40.0\\cm }\n"
            + "\\score { \\new Staff { c'1 c'1 c'1 } }\n";

        //Act
        BatchRunResult three = Run(broken, "epg15-three-lines");
        BatchRunResult one = Run(unbroken, "epg15-one-line");

        //Assert
        three.SystemCount.Should().Be(3);
        StaffLineCount(three).Should().Be(3 * StaffLinesPerSystem);
        StaffLineCount(three).Should().Be(3 * StaffLineCount(one));
    }
}
