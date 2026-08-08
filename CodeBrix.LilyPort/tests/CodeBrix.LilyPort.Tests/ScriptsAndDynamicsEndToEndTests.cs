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
/// EPG14 end to end: LilyPond text with scripts, dynamics, ottava brackets and ledger
/// lines in, SVG out, through the real <c>ly/engraver-init.ly</c> tree.
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
/// were chosen by measuring what each family actually emits: a script is a MUSIC GLYPH
/// (the port writes those as <c>&lt;path transform="scale(0.0040, -0.0040)"&gt;</c>,
/// upstream's own bytes), a hairpin is a pair of <c>&lt;line&gt;</c> elements from
/// <c>Line_interface::line</c>, and a ledger line is a <c>&lt;rect&gt;</c>.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class ScriptsAndDynamicsEndToEndTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-epg14-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RunToSvg(string body, string name)
    {
        string source = "\\version \"2.27.2\"\n\\score { \\new Staff { " + body + " } }\n";
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.SvgPath.Should().NotBeNull();
        return File.ReadAllText(result.SvgPath);
    }

    // A music glyph is emitted as upstream's own outline bytes, at the music font's
    // fixed scale. Nothing else on these pages carries that transform.
    private static int MusicGlyphCount(string svg)
        => Regex.Matches(svg, @"transform=""scale\(0\.0040, -0\.0040\)""").Count;

    private static int LineCount(string svg) => Regex.Matches(svg, "<line").Count;

    private static int RectCount(string svg) => Regex.Matches(svg, "<rect").Count;

    [Fact]
    public void an_accent_on_every_note_adds_one_music_glyph_per_note()
    {
        //Arrange
        // Script_engraver's whole job: four articulation events, four Script grobs, four
        // glyphs. Before EPG14 the engraver was an unknown translator, so the
        // articulations were announced to nobody.
        string plain = RunToSvg("c'4 c'4 c'4 c'4", "epg14-plain");

        //Act
        string accented = RunToSvg("c'4-> c'4-> c'4-> c'4->", "epg14-accent");

        //Assert
        accented.Should().Contain("<svg");
        (MusicGlyphCount(accented) - MusicGlyphCount(plain)).Should().Be(4);
    }

    [Fact]
    public void the_same_music_without_articulations_adds_no_glyphs()
    {
        //Arrange
        // The control for the test above. Two runs of IDENTICAL music must agree exactly,
        // which is what makes the difference of four above attributable to the accents
        // and not to run-to-run variation.
        string first = RunToSvg("c'4 c'4 c'4 c'4", "epg14-plain-a");

        //Act
        string second = RunToSvg("c'4 c'4 c'4 c'4", "epg14-plain-b");

        //Assert
        MusicGlyphCount(second).Should().Be(MusicGlyphCount(first));
    }

    [Fact]
    public void a_crescendo_draws_the_two_arms_of_its_hairpin()
    {
        //Arrange
        // Hairpin::print adds exactly two Line_interface::line stencils — the upper and
        // lower arm of the wedge — so a page with one hairpin carries two more <line>
        // elements than the same page without.
        string plain = RunToSvg("c'4 c'4 c'4 c'4", "epg14-nohairpin");

        //Act
        string hairpin = RunToSvg("c'4\\< c'4 c'4 c'4\\!", "epg14-hairpin");

        //Assert
        hairpin.Should().Contain("<svg");
        (LineCount(hairpin) - LineCount(plain)).Should().Be(2);
    }

    [Fact]
    public void a_dynamic_text_is_not_a_hairpin()
    {
        //Arrange
        // The control: \f goes through the SAME Dynamic_engraver and the same
        // DynamicLineSpanner, but makes a DynamicText ITEM rather than a Hairpin spanner.
        // If the test above passed because any dynamic draws lines, this catches it.
        string plain = RunToSvg("c'4 c'4 c'4 c'4", "epg14-nodynamic");

        //Act
        string dynamic = RunToSvg("c'4\\f c'4 c'4 c'4", "epg14-dynamictext");

        //Assert
        LineCount(dynamic).Should().Be(LineCount(plain));
    }

    [Fact]
    public void middle_c_gets_a_ledger_line_and_the_note_above_it_does_not()
    {
        //Arrange
        // Hand-derivable from the staff, not recorded: in a treble clef b' sits ON the
        // middle line and needs no ledger, while c' sits one step BELOW the bottom line
        // and needs exactly one. Four notes, so four ledger rects.
        string onStaff = RunToSvg("b'4 b'4 b'4 b'4", "epg14-noledger");

        //Act
        string belowStaff = RunToSvg("c'4 c'4 c'4 c'4", "epg14-ledger");

        //Assert
        belowStaff.Should().Contain("<svg");
        (RectCount(belowStaff) - RectCount(onStaff)).Should().Be(4);
    }

    [Fact]
    public void an_ottava_bracket_draws_its_text_and_line()
    {
        //Arrange
        // Ottava_spanner_engraver reads ottavationMarkups — an alist keyed by the OCTAVE
        // COUNT, which is a number. That lookup is what exposed the assq defect EPG14
        // fixed: with identity comparison on boxed numbers it never matched, and the
        // engraver warned "Could not find ottavation markup" on every \ottava.
        string plain = RunToSvg("c''4 c''4 c''4 c''4", "epg14-noottava");

        //Act
        string ottava = RunToSvg("\\ottava #1 c''4 c''4 c''4 c''4", "epg14-ottava");

        //Assert
        // The bracket draws a horizontal line plus its descending edge, so the ottava
        // page carries strictly more <line> elements than the same notes without it.
        // Before the assq fix the markup lookup missed, the text came out empty and the
        // engraver warned on every \ottava — but the bracket was still drawn, so the
        // line count alone would NOT have caught it. The text is what did.
        ottava.Should().Contain("<svg");
        LineCount(ottava).Should().BeGreaterThan(LineCount(plain));
        ottava.Should().Contain("<text");
    }
}
