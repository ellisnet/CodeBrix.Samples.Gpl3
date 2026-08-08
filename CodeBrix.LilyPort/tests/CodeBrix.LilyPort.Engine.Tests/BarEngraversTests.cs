// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The bar family reached through the real pipeline: <c>Bar_engraver</c> drawing
/// measure and manual bars off what <c>Timing_translator</c> maintains,
/// <c>Bar_number_engraver</c> numbering them, and the span-bar pair reacting to bar
/// lines across contexts.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class BarEngraversTests : IDisposable
{
    /// <summary>Removes the fixture translators from the process-global registry.</summary>
    public void Dispose() => Epg8TestHarness.Cleanup();

    private static (string Name, object Value)[] TimingProps(
        params (string Name, object Value)[] extra)
    {
        List<(string, object)> props = new List<(string, object)>
        {
            ("timeSignature", new Pair(4L, 4L)),
            ("timeSignatureSettings", Epg8TestHarness.Eval("default-time-signature-settings")),
            ("timing", true),
            ("measureBarType", new MutableString("|")),
        };
        props.AddRange(extra);
        return props.ToArray();
    }

    [Fact]
    public void bar_lines_appear_at_measure_boundaries_and_nowhere_else()
    {
        //Arrange
        // Eight quarters in 4/4: boundaries at 1 and 2 (the end). The first timestep
        // must NOT get a bar - first_time_ suppresses the initial bar line.
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            TimingProps(),
            new[] { "Timing_translator" },
            new[] { "Bar_engraver" },
            Array.Empty<string>());
        MusicObject music = Epg8TestHarness.QuarterNotes(8);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> bars = tree.GrobsNamed("BarLine");
        bars.Count.Should().Be(2);

        // The glyph comes back from scm/bar-line.scm's
        // calc-glyph-name-for-direction, which is the whole print path's front door.
        (bars[0].GetProperty("glyph") as MutableString)?.ToString().Should().Be("|");
    }

    [Fact]
    public void which_bar_makes_a_manual_bar_line()
    {
        //Arrange
        // A BarEvent mid-measure: Timing_translator sets whichBar from the event and
        // Bar_engraver prints it even though no measure boundary is near.
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            TimingProps(),
            new[] { "Timing_translator" },
            new[] { "Bar_engraver" },
            Array.Empty<string>());
        MusicObject music = (MusicObject)Epg8TestHarness.Eval(
            "(make-music 'SequentialMusic 'elements (list"
            + " (make-music 'NoteEvent 'duration (ly:make-duration 2)"
            + "  'pitch (ly:make-pitch 0 0 0))"
            + " (make-music 'BarEvent 'bar-type \"||\")"
            + " (make-music 'NoteEvent 'duration (ly:make-duration 2)"
            + "  'pitch (ly:make-pitch 0 0 0))))");

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> bars = tree.GrobsNamed("BarLine");
        bars.Count.Should().BeGreaterThan(0);

        bool foundDouble = false;
        foreach (Grob bar in bars)
        {
            if (bar.GetProperty("glyph") is MutableString glyph
                && glyph.ToString() == "||")
            {
                foundDouble = true;
            }
        }

        foundDouble.Should().BeTrue();
    }

    [Fact]
    public void the_current_bar_line_property_carries_the_bar_through_the_timestep()
    {
        //Arrange
        // Whoever reads currentBarLine during stop-translation-timestep must see the
        // grob; the reset deliberately waits for the NEXT timestep's start. After
        // iteration the last timestep's bar is therefore still visible.
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            TimingProps(),
            new[] { "Timing_translator" },
            new[] { "Bar_engraver" },
            Array.Empty<string>());
        MusicObject music = Epg8TestHarness.QuarterNotes(4);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        Context staff = tree.FindContext("Staff");
        (staff.GetProperty("currentBarLine") is Grob).Should().BeTrue();
    }

    [Fact]
    public void bar_numbers_appear_from_the_second_measure_on()
    {
        //Arrange
        // The default visibility procedure hides bar 1; measure 2's bar line brings
        // the first BarNumber, formatted by the real robust-bar-number-function.
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            TimingProps(
                ("barNumberVisibility", Epg8TestHarness.Eval(
                    "first-bar-number-invisible-and-no-parenthesized-bar-numbers")),
                ("barNumberFormatter", Epg8TestHarness.Eval("robust-bar-number-function")),
                ("centerBarNumbers", false)),
            new[] { "Timing_translator", "Bar_number_engraver" },
            new[] { "Bar_engraver" },
            Array.Empty<string>());
        MusicObject music = Epg8TestHarness.QuarterNotes(8);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> numbers = tree.GrobsNamed("BarNumber");
        numbers.Count.Should().BeGreaterThan(0);
        (numbers[0].GetProperty("text") is Nil).Should().BeFalse();
    }

    [Fact]
    public void two_bar_lines_in_one_timestep_get_a_span_bar()
    {
        //Arrange
        // Upstream needs two staves for this; the fixture maker announces two
        // BarLine items in one timestep instead, which is what the engraver actually
        // reacts to. Without EPG7's vertical alignment the sort key is -1 for both,
        // and announcement order is preserved.
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            TimingProps(),
            new[] { "Timing_translator", "Span_bar_engraver" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            makerGrobName: "BarLine",
            makerCount: 2);
        MusicObject music = Epg8TestHarness.QuarterNotes(1);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> spanBars = tree.GrobsNamed("SpanBar");
        spanBars.Count.Should().Be(1);
        PointerGroupInterface.ExtractGrobSet(spanBars[0], "elements").Count.Should().Be(2);

        // has-span-bar is the pair Span_bar_engraver writes on every member bar.
        List<Grob> bars = tree.GrobsNamed("BarLine");
        bars.Count.Should().Be(2);
        (bars[0].GetObject("has-span-bar") is Pair).Should().BeTrue();
    }

    [Fact]
    public void the_stub_engraver_stays_quiet_before_vertical_alignment_exists()
    {
        //Arrange
        // Span_bar_stub_engraver only acts once a VerticalAlignment grob exists,
        // which is EPG7's. Until then its process_acknowledged returns at the
        // beginning-of-score check - registered and reachable, doing nothing.
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            TimingProps(),
            new[] { "Timing_translator", "Span_bar_engraver", "Span_bar_stub_engraver" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            makerGrobName: "BarLine",
            makerCount: 2);
        MusicObject music = Epg8TestHarness.QuarterNotes(1);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        tree.GrobsNamed("SpanBarStub").Count.Should().Be(0);
        tree.GrobsNamed("SpanBar").Count.Should().Be(1);
    }
}
