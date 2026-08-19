// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// The internal batch runner, end to end: LilyPond TEXT in — not a music tree —
/// and an SVG document out, through the real toplevel handlers.
/// <para>
/// This is EPG3's exit criterion made a fence. The prologue defines
/// <c>init.ly</c>'s session variables, the parse collects scores through
/// <c>toplevel-score-handler</c>, the epilogue builds the book with upstream's own
/// code, and D20's <c>default-toplevel-book-handler</c> hands it back for the
/// score → SVG shortcut. If any link along that path regresses, these fail.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class BatchRunnerTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void a_language_change_does_not_leak_into_the_next_file()
    {
        //Arrange
        // THE THIRD per-file leak the runner has had to close, after \paper and
        // set-global-staff-size (EPG13) and the toplevel \layout block. \language
        // rebinds (lily)'s `pitchnames' through ly:parser-set-note-names, which is
        // session state, not file state — upstream never notices because it engraves
        // one file per process.
        //
        // Measured cost before the fix: one regression file includes arabic.ly, whose
        // first line is \language "italiano", and every file swept after it parsed with
        // ITALIAN note names. The whole \partCombine family died on "not a note name: g",
        // and two of those files still produced a page — built from music that had
        // silently lost 12 and 26 tokens to parse errors.
        string output = ScratchDirectory();

        //Act
        BatchRunner.RunText(
            "\\version \"2.27.2\"\n\\language \"italiano\"\n\\score { { do'4 } }\n",
            "batch-language-italiano",
            null,
            output);
        BatchRunResult after = BatchRunner.RunText(
            "\\version \"2.27.2\"\n\\score { { c'4 d'4 e'4 g'4 } }\n",
            "batch-language-after",
            null,
            output);

        //Assert
        // Dutch note names again, and NO parse errors — the count is what matters, since
        // a file whose notes fail to parse still yields a page, just an emptier one.
        string.Join(" || ", after.Diagnostics).Should().Be(string.Empty);
        after.ErrorCount.Should().Be(0);
        after.SvgPath.Should().NotBeNull();
    }

    [Fact]
    public void a_named_output_writes_under_the_given_base_name_not_the_input_s()
    {
        //Arrange
        // The other half of `lilypond -o out/dir/name': BatchRunner.SplitOutputName
        // decides WHICH part is the name, and this is the part that USES it. Lily.Shell's
        // `engrave -o' goes through exactly this pair, so a wiring break here is a wiring
        // break there.
        string output = ScratchDirectory();
        string source = Path.Combine(output, "the-input-name.ly");
        File.WriteAllText(source, "\\version \"2.27.2\"\n\\score { { c'4 } }\n");

        //Act
        BatchRunResult named = BatchRunner.RunFile(source, output, "the-output-name");

        //Assert
        named.SvgPath.Should().NotBeNull();
        Path.GetFileName(named.SvgPath).Should().Be("the-output-name.svg");

        // THE CONTROL: with no name given the input's own is used, so a runner that
        // ignored the argument entirely would still pass the assertion above if the two
        // names happened to agree.
        BatchRunResult derived = BatchRunner.RunFile(source, output);
        Path.GetFileName(derived.SvgPath).Should().Be("the-input-name.svg");
    }

    [Fact]
    public void a_score_reaches_svg_through_the_toplevel_handlers()
    {
        //Arrange
        string output = ScratchDirectory();

        //Act
        BatchRunResult result = BatchRunner.RunText(
            "\\version \"2.27.2\"\n\\score { { c'4 } }\n",
            "batch-first-light",
            null,
            output);

        //Assert
        string.Join(" || ", result.Diagnostics).Should().Be(string.Empty);
        result.BookCount.Should().Be(1);
        result.SystemCount.Should().Be(1);
        result.SvgPath.Should().NotBeNull();
        File.Exists(result.SvgPath).Should().BeTrue();
        File.ReadAllText(result.SvgPath).Should().Contain("<svg");
    }

    [Fact]
    public void a_toplevel_music_expression_becomes_a_score_the_same_way()
    {
        //Arrange
        string output = ScratchDirectory();

        //Act
        // No \score block: toplevel music goes through toplevel-music-handler →
        // collect-music-for-book → scores, which is a longer stretch of
        // scm/lily-library.scm than the explicit form exercises.
        BatchRunResult result = BatchRunner.RunText(
            "\\version \"2.27.2\"\n{ c'4 d'4 }\n",
            "batch-toplevel-music",
            null,
            output);

        //Assert
        result.BookCount.Should().Be(1);
        result.SystemCount.Should().Be(1);
        result.SvgPath.Should().NotBeNull();
    }

    [Fact]
    public void two_files_run_in_sequence_without_state_bleeding_between_them()
    {
        //Arrange
        string output = ScratchDirectory();

        //Act
        BatchRunResult first = BatchRunner.RunText(
            "\\version \"2.27.2\"\n\\score { { e'4 } }\n", "batch-a", null, output);
        BatchRunResult second = BatchRunner.RunText(
            "\\version \"2.27.2\"\n\\score { { g'4 } }\n", "batch-b", null, output);

        //Assert
        // The prologue re-defines the collection state per run; a leak here would
        // show as the second book carrying the first file's score too.
        first.BookCount.Should().Be(1);
        second.BookCount.Should().Be(1);
        second.SystemCount.Should().Be(1);
    }

    [Fact]
    public void a_real_regression_file_runs_from_disk()
    {
        //Arrange
        string suite = Path.Combine(
            AppContext.BaseDirectory, RelativeSuitePath());
        string file = Path.Combine(suite, "accidental.ly");
        if (!File.Exists(file))
        {
            Assert.Skip("regression suite not present beside the build");
        }

        string output = ScratchDirectory();

        //Act
        BatchRunResult result = BatchRunner.RunFile(file, output);

        //Assert
        result.ErrorCount.Should().Be(0);
        result.BookCount.Should().Be(1);
        result.SvgPath.Should().NotBeNull();
    }

    private static string RelativeSuitePath()
        => Path.Combine("..", "..", "..", "..", "..", "tests", "regression");
}
