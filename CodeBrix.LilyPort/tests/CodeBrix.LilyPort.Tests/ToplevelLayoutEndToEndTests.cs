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
/// A toplevel <c>\layout</c> block reaches the contexts it names.
/// </summary>
/// <remarks>
/// <para>
/// The defect these fence was silent for the whole life of the batch runner and it split
/// cleanly in two, which is why it survived: a <c>\layout { \context { ... } }</c> block's
/// TRANSLATOR list worked — <c>\consists</c> added an engraver, and any probe that tested
/// with <c>\consists</c> would have reported the block working — while NO property
/// operation from the same block ever ran. Neither <c>\override</c> nor a plain assignment
/// had any effect at all.
/// </para>
/// <para>
/// The cause: parser.yy's <c>toplevel_expression</c> REBINDS the <c>$defaultlayout</c>
/// identifier to the definition the block built rather than mutating the old one, and
/// <c>print-book-with</c> (scm/lily-library.scm) therefore looks the layout up BY NAME at
/// book-processing time. The runner had captured the cached init-layer object before the
/// parse instead. It is EPG13's <c>$defaultpaper</c> finding one identifier over.
/// </para>
/// <para>
/// Every test pairs the layout-block spelling against the SAME operation written in the
/// music, which never went through <c>$defaultlayout</c> and so always worked. That makes
/// each test a comparison against a known-good control rather than against a number
/// recorded from this implementation.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class ToplevelLayoutEndToEndTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-layout-" + Guid.NewGuid().ToString("N"));
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

    // A music glyph carries the music font's fixed scale; font-size changes it, so the
    // set of DISTINCT scale transforms on the page is what a size override moves.
    private static int DistinctGlyphScaleCount(string svg)
    {
        System.Collections.Generic.HashSet<string> scales
            = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(svg, @"transform=""scale\((-?[\d.]+), -?[\d.]+\)"""))
        {
            scales.Add(match.Groups[1].Value);
        }

        return scales.Count;
    }

    [Fact]
    public void an_override_in_a_toplevel_layout_block_reaches_the_context()
    {
        //Arrange
        // The control is the same override written in the music, which reaches the
        // context through \override rather than through $defaultlayout.
        string plain = RunToSvg("\\version \"2.27.2\"\n{ b'2 }\n", "layout-plain");
        string inMusic = RunToSvg(
            "\\version \"2.27.2\"\n{ \\override NoteHead.font-size = #-6 b'2 }\n",
            "layout-in-music");

        //Act
        string inLayout = RunToSvg(
            "\\version \"2.27.2\"\n"
            + "\\layout { \\context { \\Voice \\override NoteHead.font-size = #-6 } }\n"
            + "{ b'2 }\n",
            "layout-in-block");

        //Assert
        DistinctGlyphScaleCount(plain).Should().Be(1);
        DistinctGlyphScaleCount(inMusic).Should().Be(2);
        DistinctGlyphScaleCount(inLayout).Should().Be(2);
    }

    [Fact]
    public void a_property_assignment_in_a_toplevel_layout_block_reaches_the_context()
    {
        //Arrange
        // THE spelling script-custom-definition.ly uses, and the half that made it fail:
        // a bare `name = value' line inside \context is an `assign' operation, and
        // \layout is the only place a regression file can write one for the Score.
        string plain = RunToSvg("\\version \"2.27.2\"\n{ b'2 }\n", "assign-plain");
        string inMusic = RunToSvg(
            "\\version \"2.27.2\"\n{ \\set Voice.fontSize = #-6 b'2 }\n",
            "assign-in-music");

        //Act
        string inLayout = RunToSvg(
            "\\version \"2.27.2\"\n"
            + "\\layout { \\context { \\Voice fontSize = #-6 } }\n"
            + "{ b'2 }\n",
            "assign-in-block");

        //Assert
        DistinctGlyphScaleCount(plain).Should().Be(1);
        DistinctGlyphScaleCount(inMusic).Should().Be(2);
        DistinctGlyphScaleCount(inLayout).Should().Be(2);
    }

    [Fact]
    public void a_score_layout_block_inherits_the_toplevel_one()
    {
        //Arrange
        // `get_layout' (lily-parser.cc) opens every \layout head by CLONING whatever
        // $defaultlayout names at that moment, so a score's own empty \layout block is a
        // copy of the toplevel one and keeps its settings rather than starting bare.
        // That is why the toplevel block still shrinks this note.
        //Act
        string svg = RunToSvg(
            "\\version \"2.27.2\"\n"
            + "\\layout { \\context { \\Voice fontSize = #-6 } }\n"
            + "\\score { { b'2 } \\layout { } }\n",
            "assign-score-inherits");

        //Assert
        DistinctGlyphScaleCount(svg).Should().Be(2);
    }

    [Fact]
    public void a_score_layout_block_overrides_the_toplevel_one()
    {
        //Arrange
        // The other half of the clone: the score's block starts from the toplevel one
        // and then adds its OWN operation for the same property. ApplyPropertyOperations
        // walks the list in order, so the later value is the one that survives.
        //Act
        string svg = RunToSvg(
            "\\version \"2.27.2\"\n"
            + "\\layout { \\context { \\Voice fontSize = #-6 } }\n"
            + "\\score { { b'2 } \\layout { \\context { \\Voice fontSize = #0 } } }\n",
            "assign-score-overrides");

        //Assert
        DistinctGlyphScaleCount(svg).Should().Be(1);
    }

    [Fact]
    public void the_last_toplevel_layout_block_wins()
    {
        //Arrange
        // Each toplevel \layout rebinds $defaultlayout, so the later block replaces the
        // earlier one outright rather than merging into it. Reading the identifier at
        // book-processing time is what gives that for free.
        //Act
        string svg = RunToSvg(
            "\\version \"2.27.2\"\n"
            + "\\layout { \\context { \\Voice fontSize = #-6 } }\n"
            + "\\layout { \\context { \\Voice fontSize = #0 } }\n"
            + "{ b'2 }\n",
            "assign-last-wins");

        //Assert
        DistinctGlyphScaleCount(svg).Should().Be(1);
    }
}
