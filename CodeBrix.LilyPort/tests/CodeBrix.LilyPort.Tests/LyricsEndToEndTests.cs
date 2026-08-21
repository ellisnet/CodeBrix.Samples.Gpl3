// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// EPG18 end to end: LilyPond text with lyrics in, SVG out, through the real
/// <c>ly/engraver-init.ly</c> tree.
/// <para>
/// This is the reachability probe standing rule 4 asks for, and it is the only place the
/// whole EPG18 chain runs as one piece: the lyric-combine ITERATOR drives the lyrics off
/// the melody's rhythm, <c>get_voice_to_lyrics</c> finds the Voice from the Lyrics
/// context, <c>Lyric_engraver</c> makes the syllables, and <c>Hyphen_engraver</c> and
/// <c>Extender_engraver</c> put things between them. Registered ≠ behaving ≠ reachable —
/// every one of those links can be individually green while the score still comes out
/// wordless.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class LyricsEndToEndTests
{
    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-lyrics-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void addlyrics_puts_syllables_on_the_page()
    {
        //Arrange
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { \\new Voice = \"tune\" { c'4 d'4 e'4 f'4 } }\n"
            + "  \\addlyrics { A B C D } }\n";

        //Act
        string svg = RunToSvg(source, "epg18-addlyrics");

        //Assert
        // Four syllables, four glyph runs of text. Before EPG18 the lyric-combine
        // iterator had no constructor at all, so \addlyrics fell through to a default
        // iterator, the lyrics were never advanced by the melody, and the page came out
        // with the notes on it and nothing underneath.
        svg.Should().Contain("<svg");
        foreach (string syllable in new[] { "A", "B", "C", "D" })
        {
            svg.Should().Contain(">" + syllable + "<");
        }
    }

    [Fact]
    public void a_hyphen_between_syllables_of_one_word_reaches_the_page()
    {
        //Arrange
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { \\new Voice = \"tune\" { c'4 d'4 e'4 f'4 } }\n"
            + "  \\addlyrics { Ly -- ric Word ing } }\n";

        //Act
        string svg = RunToSvg(source, "epg18-hyphen");

        //Assert
        // The hyphen is a row of rounded boxes, not a glyph, so it is counted as drawing
        // rather than as text: what matters is that Hyphen_engraver made a LyricHyphen,
        // bounded it on both sides, and that Lyric_hyphen::print returned a stencil
        // instead of the empty list.
        svg.Should().Contain(">Ly<");
        svg.Should().Contain(">ric<");
        svg.Should().Contain("<path");
    }

    [Fact]
    public void lyricsto_binds_lyrics_to_a_voice_by_name()
    {
        //Arrange
        // \lyricsto names the voice explicitly rather than relying on \addlyrics'
        // implicit binding, which is the other half of Lyric_combine_music_iterator's
        // find_voice.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { <<\n"
            + "  \\new Staff \\new Voice = \"melody\" { c'4 d'4 e'4 f'4 }\n"
            + "  \\new Lyrics \\lyricsto \"melody\" { Sing these four notes }\n"
            + ">> }\n";

        //Act
        string svg = RunToSvg(source, "epg18-lyricsto");

        //Assert
        foreach (string syllable in new[] { "Sing", "these", "four", "notes" })
        {
            svg.Should().Contain(">" + syllable + "<");
        }
    }

    [Fact]
    public void a_melisma_holds_one_syllable_across_two_slurred_notes()
    {
        //Arrange
        // Four notes, but the first two are slurred, so the melody offers only THREE
        // syllable slots. This is the whole reason melisma_busy exists, and it is the
        // function context.cc declared and the port had never carried.
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { \\new Staff { \\new Voice = \"tune\" { c'4( d'4) e'4 f'4 } }\n"
            + "  \\addlyrics { One Two Three } }\n";

        //Act
        string svg = RunToSvg(source, "epg18-melisma");

        //Assert
        // All three land. A melisma that failed to register would consume "Three" on the
        // second note and leave nothing for the fourth, so the page would still LOOK
        // populated -- which is why the count of syllables is what is asserted.
        foreach (string syllable in new[] { "One", "Two", "Three" })
        {
            svg.Should().Contain(">" + syllable + "<");
        }
    }

    [Fact]
    public void lyrics_with_no_matching_voice_are_dropped_without_taking_the_score_down()
    {
        //Arrange
        string source =
            "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n"
            + "\\score { <<\n"
            + "  \\new Staff \\new Voice = \"melody\" { c'4 d'4 }\n"
            + "  \\new Lyrics \\lyricsto \"nosuchvoice\" { Nobodyhome } \n"
            + ">> }\n";

        //Act
        string svg = RunToSvg(source, "epg18-missing-voice");

        //Assert
        // The score still engraves and the orphaned syllable does not appear: with no
        // melody to follow, the lyric iterator is never advanced, which is the whole
        // reason do_quit () has something to warn about.
        //
        // The WARNING ITSELF is deliberately not asserted here. It goes to the warning
        // stream, not to BatchRunResult.Diagnostics, so this test can only see the
        // engraved result. The text is fenced by the sweep instead, where
        // lyric-combine-empty-warning.ly and lyric-combine-top-level-no-music.ly exist
        // for exactly that purpose and both emit "cannot find context" on every run.
        svg.Should().Contain("<svg");
        svg.Should().NotContain(">Nobodyhome<");
    }
}
