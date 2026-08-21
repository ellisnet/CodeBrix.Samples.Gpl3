// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Midi;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// <see cref="MidiSong"/> against Frescobaldi's own <c>midifile.song</c>:
/// <c>fixtures/midi/song.json</c> holds what <c>song.py</c> ITSELF answered
/// over 106 MIDI files — the 90 in LilyPort's regression harness, which a real
/// LilyPond wrote, and 16 synthetic ones covering the headers no engraved file
/// ever carries (regenerate with <c>tools/midiprobe</c>). Nothing here is
/// recorded from the port's own output.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>midifile/</c> imports nothing but the standard library, so the
/// probe imports and CALLS it rather than lifting definitions out of it by AST
/// (board trap 21) or standing in for PyQt (trap 46) — board trap 49.
/// </para>
/// <para>
/// FOUR of the 106 files upstream cannot answer at all: two hang forever and
/// two raise. Those are the ruling-FR14 divergences <see cref="MidiSong"/>
/// documents at their sites, and <see cref="KnownDivergences"/> below is the
/// declaration FR14 obligation 2 asks for — a declared divergence that stops
/// applying FAILS, so the day upstream fixes one of these the entry says so.
/// </para>
/// </remarks>
public class MidiParityTests
{
    /// <summary>
    /// The four files upstream cannot answer, what it does instead, and what
    /// the port answers. Each is a defect in <c>song.py</c>, not a design
    /// decision: a header field the MIDI format permits, read without a check,
    /// producing a hang or an exception rather than a song.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Divergence> KnownDivergences
        = new Dictionary<string, Divergence>(StringComparer.Ordinal)
        {
            ["syn-smpte.midi"] = new Divergence(
                "song.py did not return within 20 seconds",
                "beats() is handed the RAW header division while TempoMap is handed the "
                + "smpte_division() of it; parser.py unpacks the header as SIGNED shorts, so a "
                + "SMPTE division arrives negative, the beat step comes out negative, and "
                + "`while time <= times[-1]` never ends.",
                ExpectedDivision: 960),

            ["syn-den-huge.midi"] = new Divergence(
                "song.py did not return within 20 seconds",
                "a time-signature denominator of 2**255 floors the beat step "
                + "(4 * division) // (2 ** den) to zero, so `time += step` never moves.",
                ExpectedDivision: 384),

            ["syn-num-zero.midi"] = new Divergence(
                "ZeroDivisionError: integer modulo by zero",
                "a time-signature numerator of zero makes `beat % num` divide by zero on the "
                + "second beat, though Display.updateDisplay tests for exactly that value.",
                ExpectedDivision: 384),

            ["syn-no-tracks.midi"] = new Divergence(
                "ValueError: max() iterable argument is empty",
                "a header declaring no tracks leaves Song.events empty and "
                + "`max(self.events)` has nothing to take the maximum of.",
                ExpectedDivision: 384),
        };

    /// <summary>
    /// The FR14 divergence the oracle is generated WITH, mirroring
    /// <c>tools/midiprobe/gen-midi-fixtures.py</c>'s <c>KNOWN_FIXES</c> entry
    /// for entry — route (i), the precedent W8a set. Unlike the four files
    /// above, this one is a wrong ANSWER rather than a hang, so the fixture
    /// would otherwise record a beat grid the port deliberately does not
    /// produce; the oracle instead answers what upstream's own arithmetic gives
    /// with the one constant corrected. A fix added to the tool and not
    /// declared here, or a fixture regenerated without it, fails
    /// <see cref="the_fixture_declares_the_known_fixes_it_was_generated_with"/>.
    /// </summary>
    private static readonly (string Module, string Old, string New, string Why)[] KnownFixes
        = new[]
        {
            (
                "midifile.song",
                "time_sigs.insert(0, (0, (4, 4, 24, 8)))",
                "time_sigs.insert(0, (0, (4, 2, 24, 8)))",
                "beats() inserts its default time signature with the numerator "
                + "in the denominator byte, so a file with no time signature is "
                + "gridded in 16ths and displays 4/16 instead of 4/4 (FR14)"
            ),
        };

    /// <summary>What upstream does with a file, and why it is wrong.</summary>
    /// <param name="UpstreamReason">What the probe recorded upstream doing.</param>
    /// <param name="Defect">Why that is a defect and not a decision.</param>
    /// <param name="ExpectedDivision">The division the port resolves.</param>
    public sealed record Divergence(
        string UpstreamReason, string Defect, int ExpectedDivision);

    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "midi", "song.json");

    private static string MidiPath(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "midi", "files", name);

    private static JsonElement Fixture()
        => JsonDocument.Parse(File.ReadAllText(FixturePath())).RootElement;

    /// <summary>Every song in the fixture, as test data.</summary>
    /// <returns>The song names.</returns>
    public static IEnumerable<object[]> Songs()
        => Fixture().GetProperty("songs").EnumerateArray()
            .Select(song => new object[] { song.GetProperty("name").GetString() })
            .ToList();

    private static JsonElement SongEntry(string name)
        => Fixture().GetProperty("songs").EnumerateArray()
            .First(song => song.GetProperty("name").GetString() == name);

    [Theory]
    [MemberData(nameof(Songs))]
    public void every_song_matches_frescobaldis_own_midifile_song(string name)
    {
        //Arrange
        JsonElement entry = SongEntry(name);
        bool answered = entry.GetProperty("answered").GetBoolean();

        //Act
        MidiSong song = MidiSong.Load(MidiPath(name));

        //Assert
        if (!answered)
        {
            //Upstream has no answer to compare against; the divergence is
            //declared instead, and the port must simply have produced a song.
            KnownDivergences.Should().ContainKey(name);
            KnownDivergences[name].UpstreamReason
                .Should().Be(entry.GetProperty("reason").GetString());
            song.Division.Should().Be(KnownDivergences[name].ExpectedDivision);
            return;
        }

        KnownDivergences.Should().NotContainKey(name);
        song.RawDivision.Should().Be(entry.GetProperty("division").GetInt32());
        song.TrackCount.Should().Be(entry.GetProperty("ntracks").GetInt32());
        song.Length.Should().Be(entry.GetProperty("length").GetInt64());
    }

    [Theory]
    [MemberData(nameof(Songs))]
    public void every_tempo_map_matches_frescobaldis_own(string name)
    {
        //Arrange
        JsonElement entry = SongEntry(name);
        if (!entry.GetProperty("answered").GetBoolean()) { return; }

        //Act
        MidiSong song = MidiSong.Load(MidiPath(name));

        //Assert
        long[][] expected = entry.GetProperty("tempo_times").EnumerateArray()
            .Select(pair => pair.EnumerateArray().Select(v => v.GetInt64()).ToArray())
            .ToArray();
        song.TempoMap.Times.Count.Should().Be(expected.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            song.TempoMap.Times[index].MidiTime.Should().Be(expected[index][0]);
            song.TempoMap.Times[index].MicrosecondsPerQuarter
                .Should().Be(expected[index][1]);
        }
    }

    [Theory]
    [MemberData(nameof(Songs))]
    public void every_beat_matches_frescobaldis_own(string name)
    {
        //Arrange
        JsonElement entry = SongEntry(name);
        if (!entry.GetProperty("answered").GetBoolean()) { return; }

        //Act
        MidiSong song = MidiSong.Load(MidiPath(name));

        //Assert
        long[][] expected = entry.GetProperty("beats").EnumerateArray()
            .Select(beat => beat.EnumerateArray().Select(v => v.GetInt64()).ToArray())
            .ToArray();
        song.Beats.Count.Should().Be(expected.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            SongBeat actual = song.Beats[index];
            actual.Time.Should().Be(expected[index][0]);
            actual.Measure.Should().Be((int)expected[index][1]);
            actual.Beat.Should().Be((int)expected[index][2]);
            actual.Numerator.Should().Be((int)expected[index][3]);
            actual.Denominator.Should().Be((int)expected[index][4]);
        }
    }

    [Theory]
    [MemberData(nameof(Songs))]
    public void every_event_time_maps_to_the_same_real_time(string name)
    {
        //Arrange
        JsonElement entry = SongEntry(name);
        if (!entry.GetProperty("answered").GetBoolean()) { return; }

        //Act
        MidiSong song = MidiSong.Load(MidiPath(name));

        //Assert
        long[] expected = entry.GetProperty("music_times").EnumerateArray()
            .Select(v => v.GetInt64()).ToArray();
        song.MusicTimes.ToArray().Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(Songs))]
    public void beat_at_a_time_matches_frescobaldis_own(string name)
    {
        //Arrange
        JsonElement entry = SongEntry(name);
        if (!entry.GetProperty("answered").GetBoolean()) { return; }

        //Act
        MidiSong song = MidiSong.Load(MidiPath(name));

        //Assert
        foreach (JsonElement query in entry.GetProperty("beat_queries").EnumerateArray())
        {
            long time = query[0].GetInt64();
            long[] expected = query[1].EnumerateArray().Select(v => v.GetInt64()).ToArray();
            SongBeat actual = song.Beat(time);

            actual.Time.Should().Be(expected[0]);
            actual.Measure.Should().Be((int)expected[1]);
            actual.Beat.Should().Be((int)expected[2]);
            actual.Numerator.Should().Be((int)expected[3]);
            actual.Denominator.Should().Be((int)expected[4]);
        }
    }

    [Fact]
    public void the_fixture_declares_exactly_the_files_upstream_could_not_answer()
    {
        //Arrange
        JsonElement fixture = Fixture();

        //Act
        string[] unanswered = fixture.GetProperty("songs").EnumerateArray()
            .Where(song => !song.GetProperty("answered").GetBoolean())
            .Select(song => song.GetProperty("name").GetString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        //Assert
        unanswered.Should().Equal(
            KnownDivergences.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void the_oracle_is_frescobaldis_own_module()
    {
        //Arrange
        JsonElement fixture = Fixture();

        //Act
        string oracle = fixture.GetProperty("oracle").GetString();

        //Assert
        oracle.Should().Be(
            "frescobaldi/midifile/song.py (imported and called, "
            + "with the declared known_fixes below)");
    }

    [Fact]
    public void the_fixture_declares_the_known_fixes_it_was_generated_with()
    {
        //Arrange
        JsonElement fixture = Fixture();

        //Act
        var declared = new List<string>();
        foreach (JsonElement fix in fixture.GetProperty("known_fixes").EnumerateArray())
        {
            declared.Add(string.Join("\n", new[]
            {
                fix.GetProperty("module").GetString(),
                fix.GetProperty("old").GetString(),
                fix.GetProperty("new").GetString(),
                fix.GetProperty("why").GetString(),
            }));
        }

        //Assert
        string.Join("\n\n", declared).Should().Be(string.Join("\n\n",
            KnownFixes.Select(f => string.Join("\n", new[] { f.Module, f.Old, f.New, f.Why }))));
    }
}
