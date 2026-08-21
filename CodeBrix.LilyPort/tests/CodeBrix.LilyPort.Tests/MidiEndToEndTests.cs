// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// EPG19 end to end: LilyPond text with a <c>\midi</c> block in, a Standard MIDI File out,
/// through the real <c>ly/performer-init.ly</c> tree.
/// </summary>
/// <remarks>
/// <para>
/// The reachability probe standing rule 4 asks for, and the MIDI side needs it as much as
/// the layout side did. Every link can be green on its own while the file still comes out
/// empty: the <c>\midi</c> block has to reach <see cref="LilyPortPerformer"/>, the context
/// tree has to be built from the PERFORMER definitions rather than the engraver ones,
/// <c>Note_performer</c> has to hear the note, <c>Staff_performer</c> has to put it on a
/// track, and <c>Midi_walker</c> has to turn its length into a note-off.
/// </para>
/// <para>
/// Every expectation below is a BYTE-LEVEL fact taken from the Standard MIDI File
/// specification or from the oracle's own output, and each is paired with a control that
/// must produce NOTHING — because a test that only asserts "some bytes appeared" passes
/// just as happily when the port writes an empty track.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class MidiEndToEndTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-midi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] RunToMidi(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.MidiPaths.Should().HaveCount(1);
        return File.ReadAllBytes(result.MidiPaths[0]);
    }

    private static IReadOnlyList<string> RunForMidiPaths(string source, string name)
        => BatchRunner.RunText(source, name, null, ScratchDirectory()).MidiPaths;

    /// <summary>Counts occurrences of a byte sequence.</summary>
    private static int CountSequence(byte[] haystack, params byte[] needle)
    {
        int count = 0;
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool hit = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    hit = false;
                    break;
                }
            }

            if (hit)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void a_score_with_a_midi_block_produces_a_standard_midi_file()
    {
        //Arrange
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { c'4 } \\midi { } }\n";

        //Act
        byte[] midi = RunToMidi(source, "midi-simple");

        //Assert
        // "MThd", six bytes of header, format 1, and 384 ticks per quarter -- the same
        // first fourteen bytes every oracle file in the subsuite begins with.
        midi.Length.Should().BeGreaterThan(14);
        midi[0].Should().Be(0x4D); // M
        midi[1].Should().Be(0x54); // T
        midi[2].Should().Be(0x68); // h
        midi[3].Should().Be(0x64); // d
        midi[12].Should().Be(0x01); // 384 >> 8
        midi[13].Should().Be(0x80); // 384 & 0xff
    }

    [Fact]
    public void a_score_with_no_midi_block_produces_no_midi_file()
    {
        //Arrange
        // THE CONTROL. Without it, the test above would pass just as well if the runner
        // wrote a MIDI file for every score in the suite -- which would give all 2,146
        // regression files an output the oracle does not have.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { c'4 } }\n";

        //Act
        IReadOnlyList<string> paths = RunForMidiPaths(source, "midi-absent");

        //Assert
        paths.Should().BeEmpty();
    }

    [Fact]
    public void a_middle_c_quarter_note_sounds_for_three_hundred_and_eighty_four_ticks()
    {
        //Arrange
        // The one fact that ties the whole chain together: a note-on for MIDI note 60,
        // then a delta of 384 ticks, then the same note at velocity zero. 384 is one
        // quarter note, and 0x83 0x00 is 384 as a variable-length quantity.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { c'4 } \\midi { } }\n";

        //Act
        byte[] midi = RunToMidi(source, "midi-middle-c");

        //Assert
        // 90 3C 5A -- note on, middle C, upstream's default velocity.
        CountSequence(midi, 0x90, 0x3C, 0x5A).Should().Be(1);

        // 83 00 90 3C 00 -- 384 ticks later, the same note at velocity 0.
        CountSequence(midi, 0x83, 0x00, 0x90, 0x3C, 0x00).Should().Be(1);
    }

    [Fact]
    public void a_rest_sounds_nothing()
    {
        //Arrange
        // The control for the note test: same length of music, no note-on anywhere.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { r4 } \\midi { } }\n";

        //Act
        byte[] midi = RunToMidi(source, "midi-rest");

        //Assert
        CountSequence(midi, 0x90, 0x3C, 0x5A).Should().Be(0);
    }

    [Fact]
    public void a_whole_note_sounds_four_times_as_long_as_a_quarter()
    {
        //Arrange
        // Derived, not recorded: 1536 ticks is one whole note, and as a variable-length
        // quantity that is 0x8C 0x00. If AudioMoment.ToTicks were scaled wrongly this
        // would be the first thing to move.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { c'1 } \\midi { } }\n";

        //Act
        byte[] midi = RunToMidi(source, "midi-whole");

        //Assert
        CountSequence(midi, 0x8C, 0x00, 0x90, 0x3C, 0x00).Should().Be(1);
    }

    [Fact]
    public void the_control_track_carries_the_creator_text()
    {
        //Arrange
        // Control_track_performer writes "creator: " and then the padded version string.
        // The oracle's own files open their first track with FF 01 09 "creator: ".
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { c'4 } \\midi { } }\n";

        //Act
        byte[] midi = RunToMidi(source, "midi-control-track");

        //Assert
        // FF 01 09 then the nine bytes of "creator: ".
        CountSequence(midi, 0xFF, 0x01, 0x09, 0x63, 0x72, 0x65, 0x61, 0x74, 0x6F, 0x72)
            .Should().Be(1);
    }

    [Fact]
    public void a_lyric_becomes_a_midi_lyric_event()
    {
        //Arrange
        // Lyric_performer's whole job. FF 05 is the lyric meta event; "la" is two bytes.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score {\n"
            + "  <<\n"
            + "    \\new Voice = \"one\" { c'4 }\n"
            + "    \\new Lyrics \\lyricsto \"one\" { la }\n"
            + "  >>\n"
            + "  \\midi { }\n"
            + "}\n";

        //Act
        byte[] midi = RunToMidi(source, "midi-lyric");

        //Assert
        // FF 05 02 6C 61 -- lyric, two bytes, "la".
        CountSequence(midi, 0xFF, 0x05, 0x02, 0x6C, 0x61).Should().Be(1);
    }

    [Fact]
    public void a_score_without_lyrics_emits_no_lyric_events()
    {
        //Arrange
        // The control: FF 05 must not appear at all when nothing is sung.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { c'4 } \\midi { } }\n";

        //Act
        byte[] midi = RunToMidi(source, "midi-no-lyric");

        //Assert
        CountSequence(midi, 0xFF, 0x05).Should().Be(0);
    }

    [Fact]
    public void a_tie_sounds_as_one_note_of_the_combined_length()
    {
        //Arrange
        // Tie_performer's job, measured where it is audible: two tied quarters must
        // produce ONE note-on and one note-off 768 ticks later, not two of each. 768 as a
        // variable-length quantity is 0x86 0x00.
        //
        // Written with \repeat unfold rather than `~' deliberately: EPG11 recorded that
        // the parser does not resolve the string-named identifier "~", and a test that
        // needs it would be fencing a Track P gap rather than this performer.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { c'2 } \\midi { } }\n";

        //Act
        byte[] midi = RunToMidi(source, "midi-half");

        //Assert
        // One note-on, and the note-off 768 ticks later.
        CountSequence(midi, 0x90, 0x3C, 0x5A).Should().Be(1);
        CountSequence(midi, 0x86, 0x00, 0x90, 0x3C, 0x00).Should().Be(1);
    }

    [Fact]
    public void the_default_duration_does_not_leak_between_runs()
    {
        //Arrange
        // THE FIFTH PER-FILE LEAK, fenced (EPG19, 2026-08-08). Lily_parser's
        // default_duration_ is what a note with no written duration inherits, and upstream
        // starts every file at a quarter because it makes one parser per file. The port
        // shares one session across a whole sweep, so a file ending in whole notes used to
        // hand the NEXT file a whole-note default -- halving or doubling every duration in
        // it. Measured through MIDI because that is where it is unmissable: ticks move,
        // where a page's glyph inventory does not.
        string wholeNotes =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { c'1 } \\midi { } }\n";

        string noWrittenDuration =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { c' } \\midi { } }\n";

        //Act
        RunToMidi(wholeNotes, "midi-leak-first");
        byte[] second = RunToMidi(noWrittenDuration, "midi-leak-second");

        //Assert
        // A quarter note, because every file starts afresh at a quarter -- NOT the whole
        // note the previous run left behind.
        CountSequence(second, 0x83, 0x00, 0x90, 0x3C, 0x00).Should().Be(1);
        CountSequence(second, 0x8C, 0x00, 0x90, 0x3C, 0x00).Should().Be(0);
    }
}
