// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Midi;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The tests that share <see cref="MidiNote.LastPitch"/>.
/// </summary>
/// <remarks>
/// Upstream's <c>Note.LastPitch</c> is a CLASS attribute — one per application
/// — and the port keeps it that way, so relative mode is process-wide state and
/// two test classes writing it at once would answer each other's questions.
/// One collection puts them in a queue.
/// </remarks>
[CollectionDefinition(MidiRelativeStateCollection.Name, DisableParallelization = true)]
public sealed class MidiRelativeStateCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "midi-relative-state";
}

/// <summary>
/// The MIDI note-entry port against Frescobaldi's own <c>midiinput</c>:
/// <c>fixtures/midiinput.json</c> holds what <c>elements.py</c> ITSELF answered
/// for every MIDI note in every key signature, in eleven pitch-name languages,
/// in relative mode and in chords — and what upstream's own
/// <c>LY_REG_EXPR</c> matches at every caret position of ten documents
/// (regenerate with <c>tools/midiinputprobe</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>elements.py</c> is pure logic wearing a Qt costume, so the probe reuses
/// <c>tools/scorewizprobe/qtshim.py</c> and runs upstream's classes UNCHANGED
/// (board trap 46) — plus a stand-in for the one live keyboard question
/// <c>Note.output()</c> asks, <c>QApplication.keyboardModifiers()</c>. The
/// pattern comes out of a module that imports PortMIDI and so cannot be
/// imported at all, and is lifted by AST instead (trap 21).
/// </para>
/// <para>
/// Ruling FR5.4 defers the panel, not the logic: everything the panel would
/// drive is here and is driven by scripted events instead.
/// </para>
/// </remarks>
[Collection(MidiRelativeStateCollection.Name)]
public class MidiInputParityTests
{
    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "midiinput.json");

    private static JsonElement Fixture()
        => JsonDocument.Parse(File.ReadAllText(FixturePath())).RootElement;

    private static double AsDouble(JsonElement element)
        => double.Parse(element.GetString(), CultureInfo.InvariantCulture);

    private static double AsDouble(Fraction fraction)
        => fraction.Numerator / (double)fraction.Denominator;

    /// <summary>Every recorded mapping, as test data.</summary>
    /// <returns>The key signature and preference of each.</returns>
    public static IEnumerable<object[]> Mappings()
        => Fixture().GetProperty("mappings").EnumerateArray()
            .Select(entry => new object[]
            {
                entry.GetProperty("key_signature").GetInt32(),
                entry.GetProperty("sharps").GetBoolean(),
            })
            .ToList();

    /// <summary>Every recorded run of notes, as test data.</summary>
    /// <returns>The index of each.</returns>
    public static IEnumerable<object[]> NoteRuns()
        => Enumerable.Range(0, Fixture().GetProperty("notes").GetArrayLength())
            .Select(index => new object[] { index })
            .ToList();

    /// <summary>Every recorded relative-mode run, as test data.</summary>
    /// <returns>The index of each.</returns>
    public static IEnumerable<object[]> RelativeRuns()
        => Enumerable.Range(0, Fixture().GetProperty("relative").GetArrayLength())
            .Select(index => new object[] { index })
            .ToList();

    [Theory]
    [MemberData(nameof(Mappings))]
    public void every_key_signature_table_matches_frescobaldis_own(
        int keySignature, bool sharps)
    {
        //Arrange
        JsonElement entry = Fixture().GetProperty("mappings").EnumerateArray()
            .First(m => m.GetProperty("key_signature").GetInt32() == keySignature
                && m.GetProperty("sharps").GetBoolean() == sharps);

        //Act
        NoteMapping mapping = new NoteMapping(keySignature, sharps);

        //Assert
        JsonElement expected = entry.GetProperty("entries");
        mapping.Count.Should().Be(expected.GetArrayLength());
        for (int semitone = 0; semitone < expected.GetArrayLength(); semitone++)
        {
            (int note, Fraction alter) = mapping[semitone];
            note.Should().Be(expected[semitone][0].GetInt32());
            AsDouble(alter).Should().Be(AsDouble(expected[semitone][1]));
        }
    }

    [Theory]
    [MemberData(nameof(NoteRuns))]
    public void every_note_is_written_the_way_frescobaldi_writes_it(int index)
    {
        //Arrange
        JsonElement entry = Fixture().GetProperty("notes")[index];
        int keySignature = entry.GetProperty("key_signature").GetInt32();
        bool sharps = entry.GetProperty("sharps").GetBoolean();
        string language = entry.GetProperty("language").GetString();
        bool shift = entry.GetProperty("shift").GetBoolean();
        NoteMapping mapping = new NoteMapping(keySignature, sharps);

        //Act
        string[] produced = Enumerable.Range(0, 128)
            .Select(note => new MidiNote(note, mapping)
                .Output(relativeMode: false, language, octaveCheck: shift))
            .ToArray();

        //Assert
        string[] expected = entry.GetProperty("outputs").EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        produced.Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(RelativeRuns))]
    public void relative_mode_writes_the_same_sequence_frescobaldi_writes(int index)
    {
        //Arrange
        JsonElement entry = Fixture().GetProperty("relative")[index];
        NoteMapping mapping = new NoteMapping(
            entry.GetProperty("key_signature").GetInt32(),
            entry.GetProperty("sharps").GetBoolean());
        string language = entry.GetProperty("language").GetString();
        bool shift = entry.GetProperty("shift").GetBoolean();
        int[] sequence = entry.GetProperty("sequence").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();

        //Relative mode carries state from note to note, so each run starts from
        //the same place upstream's does.
        ResetLastPitch();

        //Act
        string[] produced = sequence
            .Select(note => new MidiNote(note, mapping)
                .Output(relativeMode: true, language, octaveCheck: shift))
            .ToArray();

        //Assert
        string[] expected = entry.GetProperty("outputs").EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        produced.Should().Equal(expected);

        JsonElement lastPitch = entry.GetProperty("last_pitch");
        MidiNote.LastPitch.Note.Should().Be(lastPitch[0].GetInt32());
        MidiNote.LastPitch.Octave.Should().Be(lastPitch[1].GetInt32());
    }

    [Fact]
    public void every_chord_is_written_the_way_frescobaldi_writes_it()
    {
        //Arrange
        JsonElement chords = Fixture().GetProperty("chords");

        foreach (JsonElement entry in chords.EnumerateArray())
        {
            NoteMapping mapping = new NoteMapping(
                entry.GetProperty("key_signature").GetInt32(),
                entry.GetProperty("sharps").GetBoolean());
            bool relative = entry.GetProperty("relative").GetBoolean();
            int[] notes = entry.GetProperty("notes").EnumerateArray()
                .Select(value => value.GetInt32()).ToArray();
            ResetLastPitch();

            //Act
            MidiChord chord = new MidiChord();
            foreach (int note in notes) { chord.Add(new MidiNote(note, mapping)); }

            string produced = chord.Output(relative, "nederlands");

            //The note AFTER a chord is what proves where the chord left the
            //last pitch: upstream leaves it at the chord's lowest note.
            string after = new MidiNote(60, mapping).Output(relative, "nederlands");

            //Assert
            produced.Should().Be(entry.GetProperty("output").GetString());
            after.Should().Be(entry.GetProperty("next_note").GetString());
        }
    }

    [Fact]
    public void the_repitch_pattern_is_upstreams_own()
    {
        //Arrange, Act
        string pattern = Fixture().GetProperty("pattern").GetString();

        //Assert
        //Written differently in C# only where the two languages spell an escape
        //differently; what it MATCHES has to be identical, which every probe
        //below then checks.
        Regex.Replace(MidiInput.PitchPattern.ToString(), @"\\'", "'")
            .Should().Be(Regex.Replace(pattern, @"\\'", "'"));
    }

    [Fact]
    public void the_repitch_search_finds_what_frescobaldis_search_finds()
    {
        //Arrange
        JsonElement probes = Fixture().GetProperty("repitch");
        int checkedProbes = 0;

        foreach (JsonElement probe in probes.EnumerateArray())
        {
            string text = probe.GetProperty("text").GetString();
            int caret = probe.GetProperty("caret").GetInt32();

            //Act
            //Upstream searches the SLICE from the caret onwards, which is what
            //MidiInput.AddToDocument does; doing it here the same way is what
            //makes the comparison meaningful.
            Match match = MidiInput.PitchPattern.Match(text.Substring(caret));

            //Assert
            JsonElement start = probe.GetProperty("start");
            if (start.ValueKind == JsonValueKind.Null)
            {
                match.Success.Should().BeFalse();
            }
            else
            {
                match.Success.Should().BeTrue();
                (caret + match.Index).Should().Be(start.GetInt32());
                (caret + match.Index + match.Length)
                    .Should().Be(probe.GetProperty("end").GetInt32());
                match.Value.Should().Be(probe.GetProperty("matched").GetString());
            }

            checkedProbes++;
        }

        checkedProbes.Should().Be(probes.GetArrayLength());
    }

    [Fact]
    public void the_note_event_codes_are_the_ones_upstream_uses()
    {
        //Arrange
        JsonElement fixture = Fixture();

        //Act, Assert
        ((int)MidiInputMessageType.NoteOn)
            .Should().Be(fixture.GetProperty("note_on_event").GetInt32());
        ((int)MidiInputMessageType.NoteOff)
            .Should().Be(fixture.GetProperty("note_off_event").GetInt32());
    }

    /// <summary>
    /// Puts the shared last pitch back where a fresh <c>ly.pitch.Pitch()</c>
    /// would leave it, which is what the probe does before each run.
    /// </summary>
    internal static void ResetLastPitch()
    {
        MidiNote.LastPitch.Note = 0;
        MidiNote.LastPitch.Alter = new Fraction(0);
        MidiNote.LastPitch.Octave = 0;
    }
}
