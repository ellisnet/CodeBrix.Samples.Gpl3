// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// <c>\lyricsto</c> end to end: every syllable a stanza has, and the track order the
/// contexts were created in.
/// </summary>
/// <remarks>
/// <para>
/// Both facts here were WRONG together and for one reason.
/// <c>Lyric_combine_music_iterator</c> only starts a syllable once it has heard a melodic
/// event from the voice it is bound to (<c>start_new_syllable</c>'s
/// <c>busy_moment_ &gt;= now</c> test), and it binds to that voice either at
/// <c>create_contexts</c> time or, when the voice does not exist yet, from the
/// <c>CreateContext</c> listener upstream installs for exactly that case
/// (<c>lily/lyric-combine-music-iterator.cc:237-239</c>). The port relayed
/// <c>CreateContext</c> out of the context before the context itself acted on it, so that
/// listener ran a search that could not yet succeed; the binding happened one timestep
/// late, the first syllable of every such stanza was dropped, and the Lyrics context's
/// audio staff was announced a timestep after every staff and voice — which is what put
/// all the lyrics tracks after all the music tracks.
/// </para>
/// <para>
/// These assert RELATIONSHIPS — syllables against notes, each lyrics track against the
/// staff it belongs to — never a recorded count, and each is paired with a control.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class LyricSyllableEndToEndTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-lyrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] RunToMidi(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(source, name, null, ScratchDirectory());
        result.MidiPaths.Should().HaveCount(1);
        return File.ReadAllBytes(result.MidiPaths[0]);
    }

    /// <summary>Reads a variable-length quantity.</summary>
    private static int ReadVlq(byte[] data, ref int index)
    {
        int value = 0;
        while (true)
        {
            byte b = data[index++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }
    }

    /// <summary>
    /// Walks the Standard MIDI File and returns, per track in file order, its name
    /// (meta 0x03) and the ticks at which lyric metas (0x05) and note-ons appear.
    /// </summary>
    private static List<(string Name, List<int> LyricTicks, List<int> NoteOnTicks)> ReadTracks(
        byte[] data)
    {
        List<(string, List<int>, List<int>)> tracks
            = new List<(string, List<int>, List<int>)>();

        int headerLength = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
        int i = 8 + headerLength;

        while (i + 8 <= data.Length)
        {
            int length = (data[i + 4] << 24) | (data[i + 5] << 16) | (data[i + 6] << 8) | data[i + 7];
            int end = i + 8 + length;
            int j = i + 8;
            int tick = 0;
            byte running = 0;
            string name = string.Empty;
            List<int> lyrics = new List<int>();
            List<int> noteOns = new List<int>();

            while (j < end)
            {
                tick += ReadVlq(data, ref j);
                byte status = data[j];
                if (status == 0xFF)
                {
                    byte type = data[j + 1];
                    j += 2;
                    int metaLength = ReadVlq(data, ref j);
                    if (type == 0x03)
                    {
                        name = Encoding.UTF8.GetString(data, j, metaLength);
                    }
                    else if (type == 0x05)
                    {
                        lyrics.Add(tick);
                    }

                    j += metaLength;
                    if (type == 0x2F)
                    {
                        break;
                    }
                }
                else if (status == 0xF0 || status == 0xF7)
                {
                    j++;
                    int sysexLength = ReadVlq(data, ref j);
                    j += sysexLength;
                }
                else
                {
                    if ((status & 0x80) != 0)
                    {
                        running = status;
                        j++;
                    }

                    int dataBytes = (running & 0xF0) == 0xC0 || (running & 0xF0) == 0xD0 ? 1 : 2;
                    if ((running & 0xF0) == 0x90 && data[j + 1] != 0)
                    {
                        noteOns.Add(tick);
                    }

                    j += dataBytes;
                }
            }

            tracks.Add((name, lyrics, noteOns));
            i = end;
        }

        return tracks;
    }

    // THE VOICE MUST BE CREATED LATE, and that is the whole point. Upstream's comment
    // on the CreateContext listener says it exists because "lyrics can be delayed when
    // voices are created implicitly", so a case where the Voice already exists by the
    // time the lyric iterator runs create_contexts binds directly and passes either way.
    // Wrapping the \new Voice in the Staff's SEQUENTIAL music defers its creation to
    // process time -- which is what \include "satb.ly" does through \make-voice, and it
    // is the shape the defect was found on. VERIFIED AGAINST THE PINNED ORACLE: it
    // renders four syllables at ticks 0, 384, 768, 1152, and the port now reproduces the
    // whole file byte for byte. (An earlier draft put the stanza textually first instead;
    // the oracle answers "cannot find context: Voice = V" to that and writes no lyrics at
    // all, so it fenced nothing.)
    private const string FourNotesFourSyllables = @"
\version ""2.27.2""
\score {
  <<
    \new Staff = ""S"" { \clef ""treble"" \new Voice = ""V"" \relative { c''4 c c c } }
    \new Lyrics \lyricsto ""V"" { one two three four }
  >>
  \midi { }
}
";

    // The control: the SAME staff with no stanza at all. Every count below has to come
    // out different here, or it would be satisfied by a file that simply drew nothing.
    private const string FourNotesNoLyrics = @"
\version ""2.27.2""
\score {
  <<
    \new Staff = ""S"" { \clef ""treble"" \new Voice = ""V"" \relative { c''4 c c c } }
  >>
  \midi { }
}
";

    [Fact]
    public void every_syllable_of_a_stanza_reaches_the_output()
    {
        //Arrange
        byte[] midi = RunToMidi(FourNotesFourSyllables, "lyrics-count");

        //Act
        List<(string Name, List<int> LyricTicks, List<int> NoteOnTicks)> tracks
            = ReadTracks(midi);
        int syllables = 0;
        int notes = 0;
        foreach ((string _, List<int> lyricTicks, List<int> noteOnTicks) in tracks)
        {
            syllables += lyricTicks.Count;
            notes += noteOnTicks.Count;
        }

        //Assert
        // The relationship, not a literal: four syllables set to four notes, one each.
        // Dropping the first syllable made this three against four.
        notes.Should().Be(4);
        syllables.Should().Be(notes);

        //Arrange (control)
        byte[] control = RunToMidi(FourNotesNoLyrics, "lyrics-count-control");

        //Act
        int controlSyllables = 0;
        foreach ((string _, List<int> lyricTicks, List<int> _) in ReadTracks(control))
        {
            controlSyllables += lyricTicks.Count;
        }

        //Assert
        controlSyllables.Should().Be(0);
    }

    [Fact]
    public void the_first_syllable_sits_on_the_first_note()
    {
        //Arrange
        byte[] midi = RunToMidi(FourNotesFourSyllables, "lyrics-first");

        //Act
        List<int> lyricTicks = new List<int>();
        List<int> noteOnTicks = new List<int>();
        foreach ((string _, List<int> l, List<int> n) in ReadTracks(midi))
        {
            lyricTicks.AddRange(l);
            noteOnTicks.AddRange(n);
        }

        lyricTicks.Sort();
        noteOnTicks.Sort();

        //Assert
        // The defect showed here as the whole stanza sitting one note late, so this is
        // the sharper of the two facts: the syllable ticks and the note ticks are the
        // SAME sequence.
        lyricTicks.Should().Equal(noteOnTicks);
    }

    [Fact]
    public void a_lyrics_track_follows_the_staff_it_is_set_to()
    {
        //Arrange
        // Two staves, each with its own stanza. Upstream announces audio staves in
        // context-creation order, so each Lyrics track lands directly after its own
        // Staff track; the port emitted both staves and then both stanzas.
        const string source = @"
\version ""2.27.2""
\score {
  <<
    \new Staff = ""A"" { \new Voice = ""AV"" \relative { c''4 c } }
    \new Lyrics = ""AL"" \lyricsto ""AV"" { a b }
    \new Staff = ""B"" { \new Voice = ""BV"" \relative { g'4 g } }
    \new Lyrics = ""BL"" \lyricsto ""BV"" { c d }
  >>
  \midi { }
}
";
        byte[] midi = RunToMidi(source, "lyrics-order");

        //Act
        List<string> named = new List<string>();
        foreach ((string name, List<int> _, List<int> _) in ReadTracks(midi))
        {
            if (name.Length != 0)
            {
                named.Add(name);
            }
        }

        //Assert
        // The relationship: each Lyrics track is the NEXT named track after the staff
        // whose voice it is set to. Asserting the literal four names would lock in the
        // port's own spelling of a track name.
        named.Should().HaveCount(4);
        int aStaff = named.FindIndex(n => n.StartsWith("A:", StringComparison.Ordinal));
        int aLyrics = named.FindIndex(n => n.StartsWith("AL:", StringComparison.Ordinal));
        int bStaff = named.FindIndex(n => n.StartsWith("B:", StringComparison.Ordinal));
        int bLyrics = named.FindIndex(n => n.StartsWith("BL:", StringComparison.Ordinal));

        aStaff.Should().BeGreaterThanOrEqualTo(0);
        aLyrics.Should().Be(aStaff + 1);
        bStaff.Should().Be(aLyrics + 1);
        bLyrics.Should().Be(bStaff + 1);
    }
}
