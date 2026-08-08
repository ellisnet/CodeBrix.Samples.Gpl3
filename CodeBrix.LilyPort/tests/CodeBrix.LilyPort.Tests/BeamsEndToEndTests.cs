// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// EPG10 end to end: LilyPond text with beamed music in, SVG out, through the real
/// <c>ly/engraver-init.ly</c> tree.
/// <para>
/// This is the reachability probe standing rule 4 asks for. Every link in the chain can
/// be individually green while the page still comes out beamless: the ENGRAVER has to
/// claim the stems, <c>Beaming_pattern</c> has to hand them beamlet counts,
/// <c>Beam::calc_beam_segments</c> has to turn those into segments and
/// <c>Beam::print</c> has to draw them — and a beam is drawn as a
/// <c>&lt;polygon&gt;</c>, which no other grob in these scores emits, so counting
/// polygons counts beams.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class BeamsEndToEndTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-beams-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RunToSvg(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.SvgPath.Should().NotBeNull();
        return File.ReadAllText(result.SvgPath);
    }

    private static int PolygonCount(string svg)
        => Regex.Matches(svg, "<polygon").Count;

    [Fact]
    public void four_eighths_are_beamed_automatically()
    {
        //Arrange
        // The Auto_beam_engraver's whole job in one line of music. Before EPG10 it was an
        // unknown translator, so this produced four separate flags and no beam at all.
        string source =
            "\\version \"2.27.2\"\n"
            + "\\score { \\new Staff { c'8 c'8 c'8 c'8 } }\n";

        //Act
        string svg = RunToSvg(source, "epg10-autobeam");

        //Assert
        svg.Should().Contain("<svg");
        PolygonCount(svg).Should().Be(1);
    }

    [Fact]
    public void autobeaming_off_leaves_the_notes_unbeamed()
    {
        //Arrange
        // The control for the test above: same notes, autobeaming switched off. If the
        // first test passed because something else drew a polygon, this one catches it.
        string source =
            "\\version \"2.27.2\"\n"
            + "\\score { \\new Staff { \\set Staff.autoBeaming = ##f c'8 c'8 c'8 c'8 } }\n";

        //Act
        string svg = RunToSvg(source, "epg10-autobeam-off");

        //Assert
        svg.Should().Contain("<svg");
        PolygonCount(svg).Should().Be(0);
    }

    [Fact]
    public void a_manual_beam_is_engraved_where_it_is_written()
    {
        //Arrange
        // Beam_engraver rather than Auto_beam_engraver: an explicit [ ] spanning notes
        // the autobeamer would have grouped differently, with autobeaming off so the
        // only beam on the page is the one that was asked for.
        string source =
            "\\version \"2.27.2\"\n"
            + "\\score { \\new Staff { \\set Staff.autoBeaming = ##f c'8[ d'8 e'8] f'8 } }\n";

        //Act
        string svg = RunToSvg(source, "epg10-manual-beam");

        //Assert
        svg.Should().Contain("<svg");
        PolygonCount(svg).Should().Be(1);
    }

    [Fact]
    public void a_sixteenth_run_draws_two_beams()
    {
        //Arrange
        // Two beam ranks, so calc_beam_segments has to emit a segment at each vertical
        // count rather than merging them. Four sixteenths make one group of two beams.
        string source =
            "\\version \"2.27.2\"\n"
            + "\\score { \\new Staff { c'16 c'16 c'16 c'16 } }\n";

        //Act
        string svg = RunToSvg(source, "epg10-sixteenths");

        //Assert
        svg.Should().Contain("<svg");
        PolygonCount(svg).Should().Be(2);
    }

    [Fact]
    public void a_chord_tremolo_draws_its_beam()
    {
        //Arrange
        // Chord_tremolo_engraver makes a Beam of its own, and it is the only engraver
        // that sets gap-count, which is what makes a tremolo beam detached from its stems.
        string source =
            "\\version \"2.27.2\"\n"
            + "\\score { \\new Staff { \\repeat tremolo 4 { c'16 e'16 } } }\n";

        //Act
        string svg = RunToSvg(source, "epg10-chord-tremolo");

        //Assert
        svg.Should().Contain("<svg");
        PolygonCount(svg).Should().BeGreaterThan(0);
    }
}
