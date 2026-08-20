// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// Point-and-click anchors in the SVG output, and the per-run host adjustments that
/// control them (<see cref="BatchRunOptions"/>).
/// <para>
/// The anchor's shape is hand-computed from <c>output-svg.scm</c>'s <c>grob-cause</c>:
/// <c>&lt;a style="color:inherit;" xlink:href="textedit://&lt;abs-file&gt;:&lt;line&gt;:&lt;char&gt;:&lt;column+1&gt;"&gt;</c>,
/// closed by <c>no-origin</c>'s <c>&lt;/a&gt;</c>. In
/// <c>\version "2.27.2"\n{ c'4 r4 }\n</c> the note starts at line 2, zero-based
/// char 2 — so its URL ends <c>:2:2:3</c> (the FORMAT adds one to the column and to
/// nothing else) — and the rest starts at char 6, so <c>:2:6:7</c>. The option's three
/// declared shapes (boolean, event-class symbol, symbol list) are upstream's own
/// <c>cond</c>, exercised one each. Nothing here is recorded from the port's output.
/// </para>
/// <para>
/// The defect this class fences against re-opening: the backend collected
/// <c>Causes</c> and emitted NO anchor for the life of the port, invisible to the
/// whole battery because the reference corpus is generated with
/// <c>-dno-point-and-click</c> and the sweep mirrors it.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class PointAndClickEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";
    private const string NoteAndRest = Version + "{ c'4 r4 }\n";

    private const string AnchorOpen = "<a style=\"color:inherit;\" xlink:href=\"textedit://";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-pnc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Render(string text, string name, BatchRunOptions options)
    {
        BatchRunResult result = BatchRunner.RunText(
            text, name, null, ScratchDirectory(), options);

        result.SvgPath.Should().NotBeNull();
        return File.ReadAllText(result.SvgPath);
    }

    [Fact]
    public void with_the_option_on_a_note_gets_a_textedit_anchor_with_its_line_char_and_one_based_column()
    {
        //Arrange
        BatchRunOptions options = new BatchRunOptions { PointAndClick = true };

        //Act
        string svg = Render(NoteAndRest, "pnc-on", options);

        //Assert
        svg.Should().Contain(AnchorOpen);
        svg.Should().Contain("/pnc-on.ly:2:2:3\">");
        svg.Should().Contain("/pnc-on.ly:2:6:7\">");

        //Every anchor the page opens is closed: <a ...> and </a> counts agree
        //(the tagline's url-link anchor is inside both counts).
        Regex.Matches(svg, "<a ").Count.Should().Be(Regex.Matches(svg, "</a>").Count);
    }

    [Fact]
    public void with_the_option_off_the_same_music_gets_no_anchor_at_all()
    {
        //Arrange
        BatchRunOptions options = new BatchRunOptions { PointAndClick = false };

        //Act
        string svg = Render(NoteAndRest, "pnc-off", options);

        //Assert
        svg.Should().NotContain(AnchorOpen);
    }

    [Fact]
    public void an_event_class_symbol_keeps_only_causes_in_that_class()
    {
        //Arrange
        BatchRunOptions options = new BatchRunOptions
        {
            PointAndClick = Symbol.Intern("note-event"),
        };

        //Act
        string svg = Render(NoteAndRest, "pnc-note-only", options);

        //Assert
        svg.Should().Contain("/pnc-note-only.ly:2:2:3\">");
        svg.Should().NotContain("/pnc-note-only.ly:2:6:7\">");
    }

    [Fact]
    public void an_event_class_list_keeps_a_cause_in_any_of_its_classes()
    {
        //Arrange
        BatchRunOptions options = new BatchRunOptions
        {
            PointAndClick = Pair.List(
                Symbol.Intern("note-event"), Symbol.Intern("rest-event")),
        };

        //Act
        string svg = Render(NoteAndRest, "pnc-note-and-rest", options);

        //Assert
        svg.Should().Contain("/pnc-note-and-rest.ly:2:2:3\">");
        svg.Should().Contain("/pnc-note-and-rest.ly:2:6:7\">");
    }

    [Fact]
    public void the_anchor_option_lives_for_one_run_and_the_next_run_is_back_on_the_default()
    {
        //Arrange
        //The engine's own default is upstream's #t, so a run that sets NOTHING after a
        //run that set #f measures whether the per-file restore took the override off.
        Render(NoteAndRest, "pnc-lifetime-off", new BatchRunOptions { PointAndClick = false });

        //Act
        string svg = Render(NoteAndRest, "pnc-lifetime-default", null);

        //Assert
        svg.Should().Contain(AnchorOpen);
    }

    [Fact]
    public void a_message_writer_receives_the_run_report_and_is_taken_back_off_afterwards()
    {
        //Arrange
        StringWriter messages = new StringWriter();
        TextWriter before = Flower.Warn.Output;
        BatchRunOptions options = new BatchRunOptions { MessageWriter = messages };

        //Act
        Render(NoteAndRest, "pnc-messages", options);

        //Assert
        messages.ToString().Should().Contain("Parsing...");
        messages.ToString().Should().Contain("Drawing systems...");
        Flower.Warn.Output.Should().BeSameAs(before);
    }

    [Fact]
    public void a_cancelled_token_stops_the_run_before_it_writes_anything()
    {
        //Arrange
        using CancellationTokenSource source = new CancellationTokenSource();
        source.Cancel();
        string scratch = ScratchDirectory();
        BatchRunOptions options = new BatchRunOptions { CancellationToken = source.Token };

        //Act
        Action act = () => BatchRunner.RunText(
            NoteAndRest, "pnc-cancelled", null, scratch, options);

        //Assert
        act.Should().Throw<OperationCanceledException>();
        Directory.GetFiles(scratch).Should().BeEmpty();
    }

    [Fact]
    public void the_result_reports_the_version_the_main_input_declared()
    {
        //Arrange
        string scratch = ScratchDirectory();

        //Act
        BatchRunResult versioned = BatchRunner.RunText(
            "\\version \"2.24.0\"\n{ c'4 }\n", "pnc-versioned", null, scratch);
        BatchRunResult bare = BatchRunner.RunText(
            "{ c'4 }\n", "pnc-unversioned", null, scratch);

        //Assert
        versioned.DeclaredVersion.Should().Be("2.24.0");
        bare.DeclaredVersion.Should().BeNull();
    }
}
