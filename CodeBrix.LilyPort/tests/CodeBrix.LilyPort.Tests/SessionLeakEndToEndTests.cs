// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// Fences the NINTH per-file leak (STAFF-LINES follow-up, 2026-08-11): a toplevel
/// variable one file INVENTS must not be visible to the next file in the same
/// session.
/// <para>
/// Upstream makes one parser per file, so a file's toplevel assignments die with it.
/// The shared batch session kept them, and the built-in vocal templates read OPTIONAL
/// variables with <c>ly:parser-lookup</c> — so <c>satb-template-with-changed-instrument-names</c>'s
/// leftover <c>Time = @{ s1 \break s1 @}</c> forced a line break inside every later
/// template in the sweep, and the <c>ssaattbb-template-*</c> family wrote two pages
/// where the oracle writes one, while producing exactly one page run alone. Found by
/// bisecting the ordinal file list against the victim, RATCHET-FIX's own recipe.
/// </para>
/// <para>
/// The reader below is the templates' mechanism in miniature: it looks the variable
/// up BY NAME at parse time and injects whatever music it finds. The pair matters —
/// the control run proves the conditional actually injects when the variable IS
/// defined in the same file, so the leak fact cannot pass because the reader is
/// broken.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class SessionLeakEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    /// <summary>
    /// <c>volta-bracket-nest.ly</c>'s own music — the file that was the leak's VICTIM.
    /// <para>
    /// Rule 35b: invented material would not do. A two-alternative score is not enough,
    /// because its brackets do not end on an ordinary <c>"|"</c> bar line and allowing a
    /// hook there changes nothing at all; it was MEASURED to render identically with and
    /// without. The nested alternatives are what put a bracket end on a plain bar line.
    /// </para>
    /// </summary>
    private const string VoltaScore =
        "\\fixed c' {\n"
        + "  \\repeat volta 6 {\n"
        + "    d1\n"
        + "    \\alternative {\n"
        + "      \\volta 1 e1\n"
        + "      \\volta 2,3,4,5,6 {\n"
        + "        f1\n"
        + "        \\alternative { \\volta 2,3,4,5 g1 \\volta 6 a1 }\n"
        + "      }\n"
        + "    }\n"
        + "  }\n"
        + "  b1\n"
        + "}\n";

    /// <summary>
    /// A score whose system count doubles when <c>LeakCanaryTime</c> is visible: the
    /// injected music carries a forced <c>\break</c>, the exact shape the templates
    /// leaked.
    /// </summary>
    private const string ReaderSource =
        Version
        + "condMusic = #(let ((leaked (ly:parser-lookup 'LeakCanaryTime)))\n"
        + "    (if (ly:music? leaked) leaked (make-music 'SequentialMusic 'elements '())))\n"
        + "\\layout { indent = 0.0 line-width = 40.0\\cm }\n"
        + "\\score { \\new Staff << \\condMusic { c'1 c'1 } >> }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-leak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void a_variable_one_file_invents_is_gone_when_the_next_file_parses()
    {
        //Arrange
        // File A invents LeakCanaryTime, exactly as the satb template file did.
        string leaker =
            Version
            + "LeakCanaryTime = { s1 \\break s1 }\n"
            + "\\score { \\new Staff { c'1 } }\n";

        //Act
        BatchRunResult first = BatchRunner.RunText(
            leaker, "leak-writer", null, ScratchDirectory());
        BatchRunResult second = BatchRunner.RunText(
            ReaderSource, "leak-reader", null, ScratchDirectory());

        //Assert
        first.SvgPath.Should().NotBeNull();
        second.SvgPath.Should().NotBeNull();
        second.SystemCount.Should().Be(1);
    }

    [Fact]
    public void the_same_variable_defined_in_the_same_file_does_inject_its_break()
    {
        //Arrange
        // The control that must come out DIFFERENTLY: with the variable defined in
        // the READER's own file, the conditional injects the break and the score is
        // two systems. Without this, the fact above would pass just as happily if
        // ly:parser-lookup answered nothing at all.
        string selfContained =
            Version
            + "LeakCanaryTime = { s1 \\break s1 }\n"
            + ReaderSource.Substring(Version.Length);

        //Act
        BatchRunResult result = BatchRunner.RunText(
            selfContained, "leak-control", null, ScratchDirectory());

        //Assert
        result.SvgPath.Should().NotBeNull();
        result.SystemCount.Should().Be(2);
    }

    /// <summary>
    /// The TENTH per-file leak (PARITY 10, 2026-08-15): a PROGRAM OPTION one file sets
    /// with <c>ly:set-option</c> must not still be set when the next file parses.
    /// <para>
    /// <c>debug-skylines</c> is the one that bit: <c>System</c> and
    /// <c>VerticalAxisGroup</c> default <c>show-vertical-skylines</c> to
    /// <c>grob::show-skylines-if-debug-skylines-set</c>, which reads the option at
    /// STENCIL time, so <c>skyline-debug.ly</c> setting it drew the debug outlines over
    /// all 376 files swept after it. The leak had been there for the life of the port
    /// and cost nothing, because the drawing block itself was missing.
    /// </para>
    /// <para>
    /// The reader counts <c>&lt;line&gt;</c> elements, which is what the debug drawing
    /// adds; the CONTROL below sets the option in the reader's OWN file, so the count
    /// cannot pass by the drawing being broken.
    /// </para>
    /// </summary>
    [Fact]
    public void a_program_option_one_file_sets_is_back_to_its_default_for_the_next_file()
    {
        //Arrange
        string setter =
            Version
            + "#(ly:set-option 'debug-skylines #t)\n"
            + "\\score { \\new Staff { c'1 } }\n";
        string reader = Version + "\\score { \\new Staff { c'1 } }\n";

        //Act
        BatchRunner.RunText(setter, "option-leak-writer", null, ScratchDirectory())
            .SvgPath.Should().NotBeNull();
        BatchRunResult second = BatchRunner.RunText(
            reader, "option-leak-reader", null, ScratchDirectory());

        string plain = Version + "\\score { \\new Staff { c'1 } }\n";
        BatchRunResult baseline = BatchRunner.RunText(
            plain, "option-leak-baseline", null, ScratchDirectory());

        //Assert
        second.SvgPath.Should().NotBeNull();
        baseline.SvgPath.Should().NotBeNull();
        LineCount(second.SvgPath).Should().Be(LineCount(baseline.SvgPath));
    }

    /// <summary>
    /// The CONTROL for the above: setting the option in the reader's own file DOES draw
    /// the skyline outlines, so an equal count there cannot pass on the drawing being
    /// absent.
    /// </summary>
    [Fact]
    public void the_same_option_set_in_the_same_file_does_draw_its_skylines()
    {
        //Arrange
        string selfContained =
            Version
            + "#(ly:set-option 'debug-skylines #t)\n"
            + "\\score { \\new Staff { c'1 } }\n";
        string plain = Version + "\\score { \\new Staff { c'1 } }\n";

        //Act
        BatchRunResult shown = BatchRunner.RunText(
            selfContained, "option-control-shown", null, ScratchDirectory());
        BatchRunResult baseline = BatchRunner.RunText(
            plain, "option-control-plain", null, ScratchDirectory());

        //Assert
        shown.SvgPath.Should().NotBeNull();
        baseline.SvgPath.Should().NotBeNull();
        LineCount(shown.SvgPath).Should().BeGreaterThan(LineCount(baseline.SvgPath));
    }

    /// <summary>
    /// The TWELFTH per-file leak (PARITY 18, 2026-08-16): a <c>define-session</c> variable
    /// one file mutates must be back to the value <c>session-save</c> recorded before the
    /// next file parses.
    /// <para>
    /// <c>#(allow-volta-hook "|")</c> APPENDS to <c>bar-line.scm</c>'s
    /// <c>volta-bracket-allow-volta-hook-list</c>, and
    /// <c>volta-bracket-add-volta-hook.ly</c> does exactly that — so the file swept right
    /// after it, <c>volta-bracket-nest.ly</c>, drew volta hooks on ordinary bar lines and
    /// sat 2.0 staff spaces out. It MATCHED run alone: the full-sweep-only trap again, and
    /// the reason the port now runs <c>session-terminate</c>'s declaration restore for
    /// EVERY session variable rather than hand-restoring the ones already caught.
    /// </para>
    /// <para>
    /// The baseline is rendered FIRST, before anything mutates the list — otherwise a
    /// leak would reach the baseline too and the comparison would pass by both sides
    /// being equally wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void a_session_variable_one_file_mutates_is_restored_for_the_next_file()
    {
        //Arrange
        string plain = Version + VoltaScore;
        string setter = Version + "#(allow-volta-hook \"|\")\n" + VoltaScore;

        //Act
        BatchRunResult baseline = BatchRunner.RunText(
            plain, "session-var-baseline", null, ScratchDirectory());
        BatchRunner.RunText(setter, "session-var-writer", null, ScratchDirectory())
            .SvgPath.Should().NotBeNull();
        BatchRunResult reader = BatchRunner.RunText(
            plain, "session-var-reader", null, ScratchDirectory());

        //Assert
        baseline.SvgPath.Should().NotBeNull();
        reader.SvgPath.Should().NotBeNull();
        Placements(reader.SvgPath).Should().Be(Placements(baseline.SvgPath));
    }

    /// <summary>
    /// The CONTROL for the above, and it must come out DIFFERENTLY: allowing the hook in
    /// a file's OWN source really does move this music, so equal placements in the test
    /// above cannot pass on the hook doing nothing.
    /// <para>
    /// ⚠ The observable is a POSITION, not a count. The first draft of this control
    /// counted <c>&lt;line&gt;</c> elements and FAILED — the hook adds no element, it
    /// changes a bracket's extent, and the music below it moves by 2.0 staff spaces.
    /// That is the same 2.0 the corpus row carried. Rule 35a: a fence that fails is as
    /// likely to be reporting a bad expectation as a bad port.
    /// </para>
    /// </summary>
    [Fact]
    public void allowing_the_volta_hook_in_the_same_file_does_move_the_music()
    {
        //Arrange
        string plain = Version + VoltaScore;
        string selfContained = Version + "#(allow-volta-hook \"|\")\n" + VoltaScore;

        //Act
        BatchRunResult baseline = BatchRunner.RunText(
            plain, "volta-control-plain", null, ScratchDirectory());
        BatchRunResult hooked = BatchRunner.RunText(
            selfContained, "volta-control-hooked", null, ScratchDirectory());

        //Assert
        baseline.SvgPath.Should().NotBeNull();
        hooked.SvgPath.Should().NotBeNull();
        Placements(hooked.SvgPath).Should().NotBe(Placements(baseline.SvgPath));
    }

    /// <summary>
    /// Every <c>translate(...)</c> in a page, joined — the page's placements as one
    /// comparable string.
    /// </summary>
    /// <param name="svgPath">The page to read.</param>
    /// <returns>The joined placements.</returns>
    private static string Placements(string svgPath)
    {
        string svg = File.ReadAllText(svgPath);
        System.Text.StringBuilder joined = new System.Text.StringBuilder();
        int at = 0;
        while ((at = svg.IndexOf("translate(", at, StringComparison.Ordinal)) >= 0)
        {
            int close = svg.IndexOf(')', at);
            if (close < 0)
            {
                break;
            }

            joined.Append(svg, at, close - at + 1).Append('|');
            at = close + 1;
        }

        return joined.ToString();
    }

    private static int LineCount(string svgPath)
    {
        string svg = File.ReadAllText(svgPath);
        int count = 0;
        int at = 0;
        while ((at = svg.IndexOf("<line", at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += 5;
        }

        return count;
    }
}
