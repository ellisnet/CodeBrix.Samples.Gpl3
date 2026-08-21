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
/// EPG20 end to end: LilyPond text with arpeggios, chord brackets, clusters, drum notes
/// and figured bass in, SVG out, through the real <c>ly/engraver-init.ly</c> tree.
/// </summary>
/// <remarks>
/// <para>
/// The reachability probe standing rule 4 asks for. Every link can be green on its own
/// while the page still comes out bare: the ENGRAVER has to hear the event, the grob has
/// to be made and parented, the callback has to be registered under the name
/// <c>scm/define-grobs.scm</c> uses, and the stencil has to survive to the backend.
/// </para>
/// <para>
/// Each test is paired with a CONTROL that must draw NOTHING, so a test cannot pass
/// because some unrelated grob happened to emit the marker being counted. The markers
/// were chosen by reading what each family actually produces: an arpeggio squiggle is a
/// MUSIC GLYPH (<c>scripts.arpeggio</c>, written as upstream's own outline bytes), a
/// chord bracket is three <c>Lookup::round_filled_box</c> stencils and so three
/// <c>&lt;rect&gt;</c> elements, and a cluster in its default <c>ramp</c> style is one
/// <c>Lookup::round_polygon</c> and so one <c>&lt;polygon&gt;</c>.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class ChordsAndClustersEndToEndTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-epg20-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RunToSvg(string body, string name)
    {
        string source = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n\\score { \\new Staff { " + body + " } }\n";
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.SvgPath.Should().NotBeNull();
        return File.ReadAllText(result.SvgPath);
    }

    private static string RunScoreToSvg(string score, string name)
    {
        string source = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n" + score + "\n";
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.SvgPath.Should().NotBeNull();
        return File.ReadAllText(result.SvgPath);
    }

    // A music glyph is emitted as upstream's own outline bytes, at the music font's
    // fixed scale. Nothing else on these pages carries that transform.
    private static int MusicGlyphCount(string svg)
        => Regex.Matches(svg, @"transform=""scale\(0\.0040, -0\.0040\)""").Count;

    private static int RectCount(string svg) => Regex.Matches(svg, "<rect").Count;

    private static int PolygonCount(string svg) => Regex.Matches(svg, "<polygon").Count;

    [Fact]
    public void an_arpeggiated_chord_adds_squiggle_glyphs_to_the_page()
    {
        //Arrange
        // Arpeggio_engraver's whole job. Before EPG20 it was an unknown translator --
        // 4,132 misses per sweep -- so \arpeggio was announced to nobody and the chord
        // came out bare. The squiggle is a music glyph, so the page must gain at least
        // one; the exact count depends on how many wiggles cover the chord's height,
        // which is the arithmetic Epg20Tests fences separately.
        string plain = RunToSvg("<c' e' g' c''>1", "epg20-plain-chord");

        //Act
        string arpeggiated = RunToSvg("<c' e' g' c''>1\\arpeggio", "epg20-arpeggio");

        //Assert
        arpeggiated.Should().Contain("<svg");
        MusicGlyphCount(arpeggiated).Should().BeGreaterThan(MusicGlyphCount(plain));
    }

    [Fact]
    public void the_same_chord_without_the_arpeggio_adds_no_glyphs()
    {
        //Arrange
        // The control for the test above. Two runs of IDENTICAL music must agree exactly,
        // which is what makes the gain above attributable to \arpeggio and not to
        // run-to-run variation.
        string first = RunToSvg("<c' e' g' c''>1", "epg20-plain-chord-a");

        //Act
        string second = RunToSvg("<c' e' g' c''>1", "epg20-plain-chord-b");

        //Assert
        MusicGlyphCount(second).Should().Be(MusicGlyphCount(first));
    }

    [Fact]
    public void a_non_arpeggiated_chord_draws_a_bracket_of_three_boxes_and_no_squiggle()
    {
        //Arrange
        // \arpeggio inside \arpeggioBracket makes a ChordBracket rather than an Arpeggio,
        // and Chord_bracket::print is Lookup::bracket -- exactly three round_filled_box
        // stencils, which the SVG backend writes as three <rect>. It must add NO music
        // glyph, which is what separates this path from the one above.
        string plain = RunToSvg("<c' e' g' c''>1", "epg20-plain-bracket");

        //Act
        string bracketed = RunToSvg(
            "\\arpeggioBracket <c' e' g' c''>1\\arpeggio", "epg20-arpeggio-bracket");

        //Assert
        bracketed.Should().Contain("<svg");
        (RectCount(bracketed) - RectCount(plain)).Should().Be(3);
        MusicGlyphCount(bracketed).Should().Be(MusicGlyphCount(plain));
    }

    [Fact]
    public void a_cluster_draws_one_polygon_and_a_plain_chord_draws_none()
    {
        //Arrange
        // \makeClusters routes the notes to Cluster_spanner_engraver, whose spanner prints
        // through brew_cluster_piece. The default style is `ramp', which is the one arm of
        // that function that calls Lookup::round_polygon -- so the page gains exactly one
        // <polygon>. The control is the same pitches WITHOUT \makeClusters, and it is
        // load-bearing rather than decorative: a BEAM is also written as a <polygon>, so
        // the music here is deliberately unbeamed quarter notes and the control asserts
        // ZERO rather than merely "fewer".
        string plain = RunToSvg("<c' g'>4 <e' a'>4", "epg20-plain-cluster");

        //Act
        string clustered = RunToSvg(
            "\\makeClusters { <c' g'>4 <e' a'>4 }", "epg20-cluster");

        //Assert
        clustered.Should().Contain("<svg");
        PolygonCount(plain).Should().Be(0);
        PolygonCount(clustered).Should().Be(1);
    }

    [Fact]
    public void drum_notes_reach_the_page_and_an_empty_drum_staff_draws_none()
    {
        //Arrange
        // Drum_notes_engraver reads drumStyleTable and makes a NoteHead per drum event.
        // The control is a DrumStaff of the same length holding SKIPS, which go through
        // the same context and the same engraver list and draw nothing at all. Rests
        // were the obvious control and are the WRONG one -- a rest is itself a music
        // glyph, and four rests happen to come to the same count as four drum heads, so
        // that version of this test passed for a reason that had nothing to do with drums.
        string silent = RunScoreToSvg(
            "\\score { \\new DrumStaff \\drummode { s4 s4 s4 s4 } }", "epg20-drums-silent");

        //Act
        string drums = RunScoreToSvg(
            "\\score { \\new DrumStaff \\drummode { bd4 sn4 bd4 sn4 } }", "epg20-drums");

        //Assert
        drums.Should().Contain("<svg");
        MusicGlyphCount(drums).Should().BeGreaterThan(MusicGlyphCount(silent));
    }

    // UNSKIPPED 2026-08-08: the gap EPG20 measured was diagnosed to the keep-alive
    // machinery, not to figured bass at all — nothing anywhere populated
    // items-worth-living, so the moment EPG15's HaraKiriGroupSpanner landed, the
    // FiguredBass context's remove-empty axis group read as EMPTY and suicided the
    // figures. AxisGroupEngraver now carries upstream's keepAliveInterfaces block,
    // and a figure draws.
    [Fact]
    public void figured_bass_puts_numbers_under_the_staff_and_an_empty_figure_mode_does_not()
    {
        //Arrange
        // Figured_bass_engraver makes a BassFigure per figure event and hangs it off a
        // BassFigureAlignment. Before EPG20 it was an unknown translator at 3,429 misses
        // per sweep. The figures ride ALONGSIDE a voice, as tests/regression/figured-bass.ly
        // does -- a FiguredBass context on its own contributes no staff and the page comes
        // out empty either way, which would make this test pass for no reason. The control
        // is the same score with the \figures line holding SKIPS.
        string empty = RunScoreToSvg(
            "\\score { << \\figures { s4 s4 } \\context Voice { \\clef bass c4 c4 } >> }",
            "epg20-figures-none");

        //Act
        string figures = RunScoreToSvg(
            "\\score { << \\figures { <6>4 <6 4>4 } \\context Voice { \\clef bass c4 c4 } >> }",
            "epg20-figures");

        //Assert
        figures.Should().Contain("<svg");

        // The figure digits are MUSIC-FONT glyphs — \number sets its string under
        // fetaText, and the composed run draws each digit as a named-glyph path at the
        // figure's own size. MusicGlyphCount cannot see them because its regex is
        // pinned to the default music scale, so the honest signal is the path count:
        // three digits' worth of paths that the skips control does not have.
        PathCount(figures).Should().BeGreaterThan(PathCount(empty));
    }

    private static int PathCount(string svg) => Regex.Matches(svg, "<path").Count;
}
