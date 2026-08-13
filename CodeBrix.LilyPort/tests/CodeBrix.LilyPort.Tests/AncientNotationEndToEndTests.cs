// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// EPG21 end to end: ancient-notation text in, ligature shapes out.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ EVERY FAILURE MODE IN THIS GROUP STILL PRODUCES A PAGE, AND A PLAUSIBLE ONE. An
/// unregistered engraver leaves a warning and engraves the notes loose; an unregistered
/// stencil binding leaves each head with its ordinary note-head glyph; a ligature that
/// collects no heads junks itself quietly. In all three cases the music is there and the
/// only thing missing is the thing the group exists to draw.
/// </para>
/// <para>
/// So each fact below is paired with a control that must come out DIFFERENTLY, and each
/// measurement is derivable from the NOTATION rather than recorded from the port: a
/// connected shape is drawn geometry where loose heads are font glyphs; a bracket is a
/// mark plain notes do not carry; a pes is two heads joined by a stroke; an episema is a
/// line above the neume.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class AncientNotationEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";
    private const string Layout
        = "\\layout { indent = 0.0 line-width = 8.0\\cm ragged-right = ##t }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-ancient-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BatchRunResult Run(string body, string name)
        => BatchRunner.RunText(Version + Layout + body, name, null, ScratchDirectory());

    /// <summary>
    /// The three vaticana pitches with no ligature at all — the baseline every vaticana
    /// fact here is counted against, so that page furniture (staff lines, the tagline's
    /// invisible link hot-zone) cannot satisfy or break an assertion about the ligature.
    /// </summary>
    private const string Unligatured = "\\new VaticanaScore \\new VaticanaVoice { a g a }\n";

    private static int Count(BatchRunResult result, string element)
        => result.SvgPaths.Sum(
            p => Regex.Matches(File.ReadAllText(p), "<" + element + @"\b").Count);

    // ----- mensural: the obliqua is DRAWN, not set from the font -----

    [Fact]
    public void a_mensural_obliqua_is_drawn_geometry_rather_than_note_head_glyphs()
    {
        //Arrange
        // Two descending breves inside \[ \] are a flexa (obliqua): upstream splits it
        // into MLP_FLEXA_BEGIN and MLP_FLEXA_END and draws each half with Lookup::beam,
        // so the shape reaches the page as POLYGONS. A port that registered the engraver
        // but not ly:mensural-ligature::brew-ligature-primitive would leave both heads
        // with their ordinary glyphs and draw no polygon at all -- and would still
        // produce this page, with two note heads on it.
        string body = "\\new MensuralStaff \\new MensuralVoice { \\[ b\\breve a\\breve \\] }\n";

        //Act
        BatchRunResult result = Run(body, "mensural-obliqua");

        //Assert
        Count(result, "polygon").Should().BeGreaterThan(0);
    }

    [Fact]
    public void the_same_two_notes_outside_a_ligature_draw_no_connected_shape()
    {
        //Arrange
        // The control, and the half that makes the fact above a fact. The notes, the
        // clef, the staff and the context are identical -- only the \[ \] is gone. A port
        // that drew polygons here would be drawing them for something other than the
        // ligature.
        string body = "\\new MensuralStaff \\new MensuralVoice { b\\breve a\\breve }\n";

        //Act
        BatchRunResult result = Run(body, "mensural-loose");

        //Assert
        Count(result, "polygon").Should().Be(0);
    }

    // ----- the bracket: a mark, not a shape -----

    [Fact]
    public void a_ligature_in_an_ordinary_voice_is_marked_with_a_bracket()
    {
        //Arrange
        // Ligature_bracket_engraver is in the DEFAULT Voice context, which is why its
        // absence cost 4,224 "unknown translator" warnings per sweep while nothing looked
        // wrong: a bracket is additive. It draws with ly:tuplet-bracket::print, so it
        // reaches the page as line segments over the notes.
        string body = "{ \\[ c'4 d' \\] }\n";

        //Act
        BatchRunResult result = Run(body, "bracket-yes");
        BatchRunResult control = Run("{ c'4 d' }\n", "bracket-no");

        //Assert
        Count(result, "line").Should().BeGreaterThan(Count(control, "line"));
    }

    [Fact]
    public void an_ordinary_voice_with_no_ligature_draws_only_its_staff_lines()
    {
        //Arrange
        // The control stated as its own fact, so that a change in staff-line drawing
        // cannot quietly satisfy the comparison above. Five staff lines is what a
        // five-line staff has, and it is what two unbracketed quarter notes add nothing
        // to.
        string body = "{ c'4 d' }\n";

        //Act
        BatchRunResult result = Run(body, "bracket-none");

        //Assert
        Count(result, "line").Should().Be(5);
    }

    // ----- vaticana: the pes is JOINED -----

    [Fact]
    public void a_vaticana_pes_joins_its_two_heads_with_a_drawn_stroke()
    {
        //Arrange
        // "a \flexa g \pes a" is a porrectus: the first two heads fuse into one curved
        // flexa shape and the third is stacked on the second, joined to it by a vertical
        // stroke that vaticana_brew_join draws as a round-filled box. Nothing in the
        // Vaticana font supplies that stroke -- it exists only if the engraver decided
        // this head was the upper head of a pes and the backend drew the join.
        //
        // Counted AGAINST the same pitches unligatured rather than against zero: a page
        // carries rects of its own -- the engraving tagline's link hot-zone is one -- so
        // "more than none" would pass on furniture alone. The fact is that the ligature
        // ADDS one.
        string body = "\\new VaticanaScore \\new VaticanaVoice { \\[ a \\flexa g \\pes a \\] }\n";

        //Act
        BatchRunResult result = Run(body, "vaticana-porrectus");
        BatchRunResult unligatured = Run(Unligatured, "vaticana-porrectus-control");

        //Assert
        Count(result, "rect").Should().BeGreaterThan(
            Count(unligatured, "rect"),
            "the pes join is drawn geometry the unligatured pitches never produce");
    }

    [Fact]
    public void three_plain_puncta_in_a_vaticana_ligature_are_not_joined()
    {
        //Arrange
        // The control: the SAME three pitches in the SAME ligature, without \flexa and
        // \pes. They are three separate puncta, so there is no join to draw and no curved
        // shape to draw either. A port that joined them anyway would be reading
        // context-info that provide_context_info never set.
        //
        // Compared with the unligatured pitches rather than with zero. This assertion
        // USED to read Be(0), which was a value recorded from the port's own output at a
        // time when the SVG backend dropped url-link entirely; restoring that element
        // (2026-08-12) put the tagline's invisible link hot-zone on every page and broke
        // it. Zero was never the fact -- "the ligature adds no rect" is.
        string body = "\\new VaticanaScore \\new VaticanaVoice { \\[ a g a \\] }\n";

        //Act
        BatchRunResult result = Run(body, "vaticana-puncta");
        BatchRunResult unligatured = Run(Unligatured, "vaticana-puncta-control");

        //Assert
        Count(result, "rect").Should().Be(
            Count(unligatured, "rect"),
            "three plain puncta have nothing to join, so the ligature draws no stroke");
    }

    // ----- episema: legal over a SINGLE neume -----

    [Fact]
    public void an_episema_over_a_single_neume_draws_its_line()
    {
        //Arrange
        // ⚠ THIS IS WHY Episema_engraver USES A *LAST* SPAN-EVENT LISTENER AND NOT THE
        // UNIQUE ONE EVERY OTHER ENGRAVER IN THIS GROUP USES. Upstream says so in a
        // comment and this is the input it is about: \episemInitium and \episemFinis on
        // the SAME note, so both events arrive in one timestep. The episema is a line, so
        // it adds one to what the staff draws.
        string body
            = "\\new VaticanaScore \\new VaticanaVoice { a\\episemInitium\\episemFinis }\n";

        //Act
        BatchRunResult result = Run(body, "episema-single");
        BatchRunResult control
            = Run("\\new VaticanaScore \\new VaticanaVoice { a }\n", "episema-none");

        //Assert
        Count(result, "line").Should().BeGreaterThan(Count(control, "line"));
    }

    [Fact]
    public void a_vaticana_staff_without_an_episema_draws_only_its_staff_lines()
    {
        //Arrange
        // The control as its own fact. A Vaticana staff has FOUR lines, not five, which
        // is also a check that the ancient context reached its own staff definition
        // rather than falling back on \Staff.
        string body = "\\new VaticanaScore \\new VaticanaVoice { a }\n";

        //Act
        BatchRunResult result = Run(body, "vaticana-bare");

        //Assert
        Count(result, "line").Should().Be(4);
    }

    // ----- kievan: the melisma claims its own width -----

    [Fact]
    public void a_kievan_ligature_engraves_without_error()
    {
        //Arrange
        // Kievan is the one style whose heads keep their ordinary stencils, so there is
        // no drawn shape to count. What CAN go wrong is the spacing rod: build_ligature
        // writes `minimum-length' and ly:spanner::set-spacing-rods enforces it, and a
        // minimum-length computed from a null head extent would come out NaN and take the
        // whole score's spacing with it.
        string body = "\\new KievanStaff \\new KievanVoice { \\[ c' d' e' \\] }\n";

        //Act
        BatchRunResult result = Run(body, "kievan-melisma");

        //Assert
        result.ErrorCount.Should().Be(0);
        result.SvgPaths.Count.Should().Be(1);
        Count(result, "path").Should().BeGreaterThan(0);
    }
}
