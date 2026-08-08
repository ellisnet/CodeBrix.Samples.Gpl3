// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The marks family reached through the real pipeline: the tracker choosing what the
/// <c>Mark_engraver</c> engraves, the metronome mark formatted by the real Scheme
/// formatter, and <c>Jump_engraver</c>'s <em>Fine</em>.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class MarkEngraversTests : IDisposable
{
    /// <summary>Removes the fixture translators from the process-global registry.</summary>
    public void Dispose() => Epg8TestHarness.Cleanup();

    private static (string Name, object Value)[] ScoreProps(
        params (string Name, object Value)[] extra)
    {
        List<(string, object)> props = new List<(string, object)>
        {
            ("timeSignature", new Pair(4L, 4L)),
            ("timeSignatureSettings",
                Epg8TestHarness.Eval("default-time-signature-settings")),
            ("timing", true),
        };
        props.AddRange(extra);
        return props.ToArray();
    }

    [Fact]
    public void the_tracker_chooses_the_rehearsal_mark_the_engraver_prints()
    {
        //Arrange
        // \mark \default: the label comes from the rehearsalMark counter, the text
        // from the real format-mark-letters, and the counter advances afterwards.
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            ScoreProps(
                ("rehearsalMark", 1L),
                ("rehearsalMarkFormatter", Epg8TestHarness.Eval("format-mark-letters"))),
            new[] { "Timing_translator", "Mark_tracking_translator", "Mark_engraver" },
            Array.Empty<string>(),
            Array.Empty<string>());
        MusicObject music = Epg8TestHarness.QuarterNotes(
            1, "(make-music 'RehearsalMarkEvent)");

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> marks = tree.GrobsNamed("RehearsalMark");
        marks.Count.Should().Be(1);
        (marks[0].GetProperty("text") is Nil).Should().BeFalse();

        // The tracker updates the counter at the END of the timestep, so the next
        // \mark \default would be "B".
        Context score = tree.FindContext("Score");
        SchemeConvert.ToLong(score.GetProperty("rehearsalMark"), "test").Should().Be(2);
    }

    [Fact]
    public void a_tempo_change_becomes_a_metronome_mark_with_formatted_text()
    {
        //Arrange
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            ScoreProps(
                ("metronomeMarkFormatter",
                    Epg8TestHarness.Eval("format-metronome-markup"))),
            new[] { "Timing_translator", "Metronome_mark_engraver" },
            Array.Empty<string>(),
            Array.Empty<string>());
        MusicObject music = Epg8TestHarness.QuarterNotes(
            1,
            "(make-music 'TempoChangeEvent"
            + " 'metronome-count 60 'tempo-unit (ly:make-duration 2))");

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> marks = tree.GrobsNamed("MetronomeMark");
        marks.Count.Should().Be(1);
        (marks[0].GetProperty("text") is Nil).Should().BeFalse();
    }

    [Fact]
    public void a_mid_piece_fine_prints_a_jump_script()
    {
        //Arrange
        // \fine between notes: the JumpScript survives (the suicide in finalize only
        // covers a Fine at the very END of the music).
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            ScoreProps(("fineText", new MutableString("Fine"))),
            new[] { "Timing_translator", "Jump_engraver" },
            Array.Empty<string>(),
            Array.Empty<string>());
        MusicObject music = (MusicObject)Epg8TestHarness.Eval(
            "(make-music 'SequentialMusic 'elements (list"
            + " (make-music 'NoteEvent 'duration (ly:make-duration 2)"
            + "  'pitch (ly:make-pitch 0 0 0))"
            + " (make-music 'FineEvent)"
            + " (make-music 'NoteEvent 'duration (ly:make-duration 2)"
            + "  'pitch (ly:make-pitch 0 0 0))))");

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> scripts = tree.GrobsNamed("JumpScript");
        scripts.Count.Should().Be(1);
        (scripts[0].GetProperty("text") as MutableString)?.ToString().Should().Be("Fine");
    }
}
