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
