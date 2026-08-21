// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 11 (2026-08-15) end to end: D36, the multi-glyph string transform.
/// <para>
/// A run of text set in the MUSIC font leaves the engine as one <c>glyph-string</c>
/// expression, and <c>scm/output-svg.scm</c> draws it by placing each glyph on the
/// PATH's own transform at the cumulative advance of the glyphs before it —
/// <c>translate(total-x, dy) scale(s, -s)</c>, where <c>total-x</c> is the
/// <c>next-horiz-adv</c> global, declared with the comment that it accumulates "only if
/// there is more than one glyph". The port composed the run as a pile of separately
/// translated <c>named-glyph</c> stencils instead, so it emitted that compound form
/// ZERO times across the whole regression corpus against the oracle's 3,019 on 179
/// pages. The marks landed in the same places; the document was a different document,
/// and the comparator graded the same mark as an engraver-drawn path on one side and a
/// named glyph on the other across 171 rows.
/// </para>
/// <para>
/// The fence is a RELATIONSHIP with a CONTROL (rules 33, 34): a time signature whose
/// numerator needs TWO digits must draw the compound form, and one whose numerator and
/// denominator are single digits must draw none — so the count cannot be satisfied by a
/// backend that writes the compound form always, or never.
/// </para>
/// <para>
/// Both expected values were read off the PINNED oracle before they were asserted
/// (rule 35, and trap 8b for the pinning): <c>\time 12/8</c> gives exactly one compound
/// transform and <c>\time 3/4</c> gives none.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class MusicFontRunEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    // Two glyphs in the numerator, one in the denominator.
    private const string TwoDigitNumerator = Version
        + "\\score { \\new Staff { \\time 12/8 c'1. } }\n";

    // The control: every digit of this one is a run of exactly one glyph.
    private const string SingleDigits = Version
        + "\\score { \\new Staff { \\time 3/4 c'2. } }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-musicrun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Render(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());
        result.SvgPath.Should().NotBeNull();
        return File.ReadAllText(result.SvgPath);
    }

    private static int CompoundTransforms(string svg)
        => Regex.Matches(svg, "<path transform=\"translate\\([^\"]*\\) scale\\(").Count;

    [Fact]
    public void a_two_digit_time_signature_places_its_second_digit_on_the_path()
    {
        //Act
        string wide = Render(TwoDigitNumerator, "musicrun-12-8");
        string narrow = Render(SingleDigits, "musicrun-3-4");

        //Assert
        // The oracle's own answer for these two files, under the corpus's font pinning.
        CompoundTransforms(wide).Should().Be(1);
        CompoundTransforms(narrow).Should().Be(0);

        // Both pages draw music, so the control is a control and not an empty render.
        narrow.Should().Contain("<path");
    }

    // `\box' draws the stencil's own extent, so a page of boxed strings is a page of
    // MEASUREMENTS. \fontsize #6 doubles everything, which is what makes a five-unit
    // difference in a thousand-unit em readable at four decimal places.
    private const string BoxedNumbers = Version
        + "\\markup \\box \\number \\fontsize #6 \"1.\"\n"
        + "\\markup \\box \\number \\fontsize #6 \"2.\"\n"
        + "\\markup \\box \\number \\fontsize #6 \"1\"\n"
        + "\\markup \\box \\number \\fontsize #6 \"2\"\n";

    [Fact]
    public void a_music_font_run_is_measured_by_its_ink_and_its_whole_device_dots()
    {
        //Arrange
        // Every one of these was read off the PINNED oracle before it was asserted
        // (rule 35, trap 8b): the top edge of each box, and its width. They are not
        // recorded port output — the port disagreed with all eight when they were
        // written down.
        (string Top, string Width)[] expected =
        {
            ("-4.3240", "4.0826"),  // "1."
            ("-4.3000", "4.4923"),  // "2."
            ("-4.3240", "2.9559"),  // "1"
            ("-4.3000", "3.4680"),  // "2"
        };

        //Act
        string svg = Render(BoxedNumbers, "musicrun-boxed-numbers");

        MatchCollection boxes = Regex.Matches(
            svg,
            "<rect x=\"-0\\.3000\" y=\"(-4\\.[0-9]+)\" width=\"([0-9.]+)\" height=\"0\\.1000\"");

        //Assert
        boxes.Count.Should().Be(expected.Length);

        // The page is written in the reverse of the markups' order.
        List<(string Top, string Width)> measured = new List<(string, string)>();
        foreach (Match box in boxes)
        {
            measured.Add((box.Groups[1].Value, box.Groups[2].Value));
        }

        measured.Reverse();
        measured.Should().Equal(expected);

        // THE CONTROLS, and they are what make the eight numbers mean something. The two
        // digits must NOT report the same height — Emmentaler declares one height for
        // every digit in its LILC table and DRAWS `fattened.one' about five thousandths
        // of an em taller than `fattened.two', so a run measured by the declared box
        // answers the same top for both and a run measured by the INK does not.
        expected[0].Top.Should().NotBe(expected[1].Top);

        // And the two must not report the same width either, or a width read off the
        // wrong axis of the wrong rectangle would pass.
        expected[2].Width.Should().NotBe(expected[3].Width);
    }

    [Fact]
    public void a_multi_glyph_run_is_wrapped_and_a_single_glyph_run_is_not()
    {
        //Act
        string wide = Render(TwoDigitNumerator, "musicrun-wrap-12-8");
        string narrow = Render(SingleDigits, "musicrun-wrap-3-4");

        //Assert
        // `glyph-string' wraps a run of more than one glyph in an attribute-less group
        // and writes a run of exactly one bare. The page with the two-digit numerator is
        // therefore the only one of the two that carries such a group.
        Regex.Matches(wide, "<g>\n").Count.Should().Be(1);
        Regex.Matches(narrow, "<g>\n").Count.Should().Be(0);
    }
}
