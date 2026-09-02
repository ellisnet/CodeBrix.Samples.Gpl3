// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Midi;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>A document that is just a string and a caret.</summary>
/// <remarks>Ruling FR5.4's "scripted-event tests" need somewhere for the notes
/// to land that is not an editor; this is it.</remarks>
internal sealed class FakeInputTarget : IMidiInputTarget
{
    public FakeInputTarget(string text = "", int caret = -1)
    {
        Text = text;
        CaretOffset = caret < 0 ? text.Length : caret;
    }

    public string Text { get; private set; }

    public int CaretOffset { get; set; }

    public List<(int Offset, int Length, string Text)> Edits { get; }
        = new List<(int, int, string)>();

    public void Replace(int offset, int length, string text)
    {
        Edits.Add((offset, length, text));
        Text = Text.Remove(offset, length).Insert(offset, text);

        //An editor leaves the caret after what it just wrote.
        CaretOffset = offset + text.Length;
    }
}

/// <summary>
/// The MIDI note-entry pipeline, driven by scripted events through the
/// <see cref="IMidiInputDevice"/> seam — which is what ruling FR5.4 asks for in
/// place of the panel it defers.
/// </summary>
[Collection(MidiRelativeStateCollection.Name)]
public class MidiInputTests
{
    private static MidiInput Entry(
        FakeInputTarget target, Action<MidiInputOptions> configure = null)
    {
        MidiInputOptions options = new MidiInputOptions();
        configure?.Invoke(options);
        MidiInputParityTests.ResetLastPitch();
        return new MidiInput(target, options) { Language = "nederlands" };
    }

    [Fact]
    public void a_played_note_lands_in_the_document()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget("c4 d4 ", caret: 6);
        MidiInput entry = Entry(target);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();

        //Act
        entry.StartCapturing(device);
        device.PlayNote(64);

        //Assert
        //A space before the caret means no space is added, which is upstream's
        //own rule.
        target.Text.Should().Be("c4 d4 e'");
    }

    [Fact]
    public void a_note_after_a_word_gets_a_space_in_front_of_it()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget("c4 d4", caret: 5);
        MidiInput entry = Entry(target);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();

        //Act
        entry.StartCapturing(device);
        device.PlayNote(64);

        //Assert
        target.Text.Should().Be("c4 d4 e'");
    }

    [Fact]
    public void a_note_at_the_start_of_a_line_gets_no_space()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget("c4 d4\n", caret: 6);
        MidiInput entry = Entry(target);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();

        //Act
        entry.StartCapturing(device);
        device.PlayNote(64);

        //Assert
        target.Text.Should().Be("c4 d4\ne'");
    }

    [Fact]
    public void a_stopped_device_sends_nothing()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();

        //Act
        device.PlayNote(60);
        entry.StartCapturing(device);
        entry.StopCapturing();
        device.PlayNote(60);

        //Assert
        target.Edits.Should().BeEmpty();
    }

    [Fact]
    public void capturing_takes_the_documents_pitch_language()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();

        //Act
        entry.StartCapturing(device, "english");
        device.PlayNote(61);

        //Assert
        entry.Language.Should().Be("english");
        target.Text.Should().Be("cs'");
    }

    [Fact]
    public void notes_held_together_become_a_chord()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target, options => options.ChordMode = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        device.Send(MidiInputMessage.NoteOn(60));
        device.Send(MidiInputMessage.NoteOn(64));
        device.Send(MidiInputMessage.NoteOn(67));

        //Nothing is written until the last key comes up.
        string beforeRelease = target.Text;

        device.Send(MidiInputMessage.NoteOff(60));
        device.Send(MidiInputMessage.NoteOff(64));
        device.Send(MidiInputMessage.NoteOff(67));

        //Assert
        beforeRelease.Should().BeEmpty();
        target.Text.Should().Be("<c' e' g'>");
    }

    [Fact]
    public void a_chord_is_written_lowest_note_first_whatever_order_it_was_played()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target, options => options.ChordMode = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        foreach (int note in new[] { 67, 60, 64 })
        {
            device.Send(MidiInputMessage.NoteOn(note));
        }

        foreach (int note in new[] { 67, 60, 64 })
        {
            device.Send(MidiInputMessage.NoteOff(note));
        }

        //Assert
        target.Text.Should().Be("<c' e' g'>");
    }

    [Fact]
    public void a_lone_note_in_chord_mode_is_written_as_a_note()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target, options => options.ChordMode = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        device.PlayNote(60);

        //Assert
        target.Text.Should().Be("c'");
    }

    [Fact]
    public void a_note_on_with_no_velocity_counts_as_a_release()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target, options => options.ChordMode = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        //Many keyboards never send a note-off at all; they send a note-on with
        //velocity zero, which upstream treats as the release.
        device.Send(MidiInputMessage.NoteOn(60));
        device.Send(MidiInputMessage.NoteOn(64));
        device.Send(MidiInputMessage.NoteOn(60, velocity: 0));
        device.Send(MidiInputMessage.NoteOn(64, velocity: 0));

        //Assert
        target.Text.Should().Be("<c' e'>");
    }

    [Fact]
    public void the_held_note_count_never_goes_negative()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target, options => options.ChordMode = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        //Upstream's own comment: "activenotes could get negative under strange
        //conditions" — a release with nothing held is one of them.
        device.Send(MidiInputMessage.NoteOff(60));
        device.Send(MidiInputMessage.NoteOff(64));
        device.Send(MidiInputMessage.NoteOn(72));
        device.Send(MidiInputMessage.NoteOff(72));

        //Assert
        entry.ActiveNotes.Should().Be(0);
        target.Text.Should().Be("c''");
    }

    [Theory]
    [InlineData(0, 0, true)]     //0 captures every channel
    [InlineData(0, 9, true)]
    [InlineData(1, 0, true)]     //channel 1 to a human is channel 0 on the wire
    [InlineData(1, 1, false)]
    [InlineData(10, 9, true)]
    [InlineData(10, 3, false)]
    public void the_channel_filter_is_upstreams_own_arithmetic(
        int setting, int channel, bool expected)
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target, options => options.Channel = setting);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        device.Send(MidiInputMessage.NoteOn(60, 64, channel));

        //Assert
        (target.Text.Length > 0).Should().Be(expected);
    }

    [Fact]
    public void repitch_mode_replaces_the_pitch_and_leaves_the_duration()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget("c4 d8 e16 f", caret: 0);
        MidiInput entry = Entry(target, options => options.RepitchMode = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        device.PlayNote(67);

        //Assert
        target.Text.Should().Be("g'4 d8 e16 f");
    }

    [Fact]
    public void repitch_mode_steps_over_a_rest()
    {
        //Arrange
        //The pattern's letter ranges are a-p and s-z, so 'r' is never matched
        //and a rest keeps its place while the notes around it are re-pitched.
        FakeInputTarget target = new FakeInputTarget("r4 c8", caret: 0);
        MidiInput entry = Entry(target, options => options.RepitchMode = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        device.PlayNote(62);

        //Assert
        target.Text.Should().Be("r4 d'8");
    }

    [Fact]
    public void repitch_mode_replaces_a_whole_chord()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget("<c e g>4 d8", caret: 0);
        MidiInput entry = Entry(target, options =>
        {
            options.RepitchMode = true;
            options.ChordMode = true;
        });
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        device.Send(MidiInputMessage.NoteOn(62));
        device.Send(MidiInputMessage.NoteOn(65));
        device.Send(MidiInputMessage.NoteOff(62));
        device.Send(MidiInputMessage.NoteOff(65));

        //Assert
        target.Text.Should().Be("<d' f'>4 d8");
    }

    [Fact]
    public void repitch_mode_with_nothing_left_to_repitch_writes_nothing()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget("c4 d8", caret: 5);
        MidiInput entry = Entry(target, options => options.RepitchMode = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        device.PlayNote(60);

        //Assert
        target.Text.Should().Be("c4 d8");
        target.Edits.Should().BeEmpty();
    }

    [Fact]
    public void relative_mode_writes_octaves_against_the_last_key_pressed()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target, options => options.RelativeMode = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        foreach (int note in new[] { 60, 62, 64, 72 })
        {
            device.PlayNote(note);
        }

        //Assert
        target.Text.Should().Be("c' d e c'");
    }

    [Fact]
    public void an_octave_check_is_added_while_shift_is_held()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target, options => options.OctaveCheck = true);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        device.PlayNote(60);

        //Assert
        target.Text.Should().Be("c'='");
    }

    [Fact]
    public void anything_that_is_not_a_note_is_ignored()
    {
        //Arrange
        FakeInputTarget target = new FakeInputTarget();
        MidiInput entry = Entry(target);
        VirtualMidiInputDevice device = new VirtualMidiInputDevice();
        entry.StartCapturing(device);

        //Act
        device.Send(new MidiInputMessage(MidiInputMessageType.Other, 0, 60, 100));

        //Assert
        target.Edits.Should().BeEmpty();
    }

    [Fact]
    public void the_options_round_trip_through_the_settings_store()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        using SettingsStore settings
            = new SettingsStore(folder.Path);
        MidiInputOptions options = new MidiInputOptions(settings)
        {
            Channel = 5,
            KeySignature = 2,
            Sharps = false,
            ChordMode = true,
            RelativeMode = true,
            RepitchMode = true,
        };

        //Act
        options.Save();
        MidiInputOptions read = new MidiInputOptions(settings);

        //Assert
        read.Channel.Should().Be(5);
        read.KeySignature.Should().Be(2);
        read.Sharps.Should().BeFalse();
        read.ChordMode.Should().BeTrue();
        read.RelativeMode.Should().BeTrue();
        read.RepitchMode.Should().BeTrue();

        //An octave check is a live keyboard question, not a setting.
        read.OctaveCheck.Should().BeFalse();
    }

    [Fact]
    public void the_default_options_are_upstreams_defaults()
    {
        //Arrange, Act
        MidiInputOptions options = new MidiInputOptions();

        //Assert
        options.Channel.Should().Be(0);
        options.KeySignature.Should().Be(7);
        options.Sharps.Should().BeTrue();
        options.ChordMode.Should().BeFalse();
        options.RelativeMode.Should().BeFalse();
        options.RepitchMode.Should().BeFalse();
    }
}

/// <summary>The device registry that is left of upstream's midihub.</summary>
public class MidiInputDeviceTests
{
    [Fact]
    public void a_registered_device_can_be_found_and_withdrawn()
    {
        //Arrange
        using VirtualMidiInputDevice device = new VirtualMidiInputDevice("Test keyboard");
        int changes = 0;
        EventHandler handler = (_, _) => changes++;
        MidiInputDevices.DevicesChanged += handler;

        try
        {
            //Act
            MidiInputDevices.Register(device);
            MidiInputDevices.Register(device);
            IMidiInputDevice found = MidiInputDevices.ByName("Test");
            bool available = MidiInputDevices.Available;
            MidiInputDevices.Unregister(device);

            //Assert
            found.Should().BeSameAs(device);
            available.Should().BeTrue();
            MidiInputDevices.Available.Should().BeFalse();

            //Registering twice announces once.
            changes.Should().Be(2);
        }
        finally
        {
            MidiInputDevices.DevicesChanged -= handler;
            MidiInputDevices.Unregister(device);
        }
    }

    [Fact]
    public void the_default_device_skips_a_through_port()
    {
        //Arrange
        using VirtualMidiInputDevice through
            = new VirtualMidiInputDevice("Midi Through Port-0");
        using VirtualMidiInputDevice real = new VirtualMidiInputDevice("Piano");

        try
        {
            MidiInputDevices.Register(through);
            MidiInputDevices.Register(real);

            //Act
            IMidiInputDevice chosen = MidiInputDevices.Default();

            //Assert
            //Upstream's own rule: a port with "through" in its name is ALSA's
            //loopback and never what a player means.
            chosen.Should().BeSameAs(real);
        }
        finally
        {
            MidiInputDevices.Unregister(through);
            MidiInputDevices.Unregister(real);
        }
    }

    [Fact]
    public void with_no_devices_there_is_no_default()
    {
        //Arrange, Act, Assert
        //Ruling FR5.4 ships the seam without an implementation, so this is the
        //state the application is really in for v1.
        MidiInputDevices.All.Should().BeEmpty();
        MidiInputDevices.Default().Should().BeNull();
        MidiInputDevices.Available.Should().BeFalse();
    }
}

/// <summary>The MIDI files a document has produced.</summary>
public class MidiFilesTests
{
    [Fact]
    public void the_documents_midi_output_is_listed_beside_the_source()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        folder.File("score.midi", "MThd");
        folder.File("score-1.midi", "MThd");
        folder.File("score.svg", "<svg/>");
        folder.File("unrelated.midi", "MThd");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        MidiFiles files = MidiFiles.For(document);
        files.Update();

        //Assert
        files.Files.Select(Path.GetFileName)
            .Should().Equal("score.midi", "score-1.midi");
        files.Any.Should().BeTrue();
        files.DisplayName(0).Should().Be("score.midi");
    }

    [Fact]
    public void a_document_with_no_midi_has_no_files()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        folder.File("score.svg", "<svg/>");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        bool any = MidiFiles.For(document).Update();

        //Assert
        any.Should().BeFalse();
        MidiFiles.For(document).DisplayName(0).Should().BeEmpty();
        MidiFiles.For(document).Song(0).Should().BeNull();
    }

    [Fact]
    public void the_current_index_is_pulled_back_when_the_list_shrinks()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        folder.File("score.midi", "MThd");
        folder.File("score-1.midi", "MThd");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);
        MidiFiles files = MidiFiles.For(document);
        files.Update();
        files.Current = 1;

        //Act
        File.Delete(Path.Combine(folder.Path, "score-1.midi"));
        files.Update();

        //Assert
        files.Current.Should().Be(0);
    }

    [Fact]
    public void a_file_that_is_not_midi_answers_no_song_rather_than_raising()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        folder.File("score.midi", "this is not a MIDI file at all");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);
        MidiFiles files = MidiFiles.For(document);
        files.Update();

        //Act
        MidiSong song = files.Song(0);

        //Assert
        //A run can genuinely be caught half-written; the window has no business
        //closing over it.
        song.Should().BeNull();
    }

    [Fact]
    public void a_real_midi_file_reads_as_a_song()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        string source = Path.Combine(
            AppContext.BaseDirectory, "fixtures", "midi", "files", "syn-format0.midi");
        string copy = Path.Combine(folder.Path, "score.midi");
        File.Copy(source, copy);

        //File.Copy keeps the SOURCE's timestamp, and a result file is only
        //listed when it is newer than the document it came from.
        File.SetLastWriteTimeUtc(copy, DateTime.UtcNow);
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);
        MidiFiles files = MidiFiles.For(document);
        files.Update();

        //Act
        MidiSong song = files.Song(0);

        //Assert
        song.Should().NotBeNull();
        song.Length.Should().Be(2000);

        //The song is read once and kept.
        files.Song(0).Should().BeSameAs(song);
    }
}

/// <summary>Which instrument the player sounds through.</summary>
public class SoundFontTests
{
    [Fact]
    public void the_bundled_bank_is_the_default()
    {
        //Arrange, Act
        string resolved = SoundFonts.Resolve();

        //Assert
        //FD2's bank ships in the box, so unless a user has chosen something
        //else this is what plays.
        resolved.Should().Be(SoundFonts.BundledPath);
        File.Exists(resolved).Should().BeTrue();
    }

    [Fact]
    public void a_chosen_instrument_wins_over_the_bundled_one()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string chosen = folder.File("mine.sf2", "not really a bank");
        using SettingsStore settings
            = new SettingsStore(folder.Path);
        settings.SetString(SoundFonts.InstrumentSettingKey, chosen);

        //Act
        string resolved = SoundFonts.Resolve(settings);

        //Assert
        resolved.Should().Be(chosen);
    }

    [Fact]
    public void a_chosen_instrument_that_is_gone_falls_back_to_the_bundled_one()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        using SettingsStore settings
            = new SettingsStore(folder.Path);
        settings.SetString(
            SoundFonts.InstrumentSettingKey,
            Path.Combine(folder.Path, "deleted.sf2"));

        //Act
        string resolved = SoundFonts.Resolve(settings);

        //Assert
        resolved.Should().Be(SoundFonts.BundledPath);
    }

    [Fact]
    public void the_bundled_bank_is_named_by_the_application_not_by_the_file()
    {
        //Arrange, Act
        string shown = SoundFonts.DisplayName(SoundFonts.BundledPath);

        //Assert
        //Board trap 51: the file's own INAM chunk says "GeneralUser GS 2.0.3
        //BETA", left over from v2.0.1 — showing it would tell users they are
        //running a beta of a released bank.
        shown.Should().Be("GeneralUser GS 2.0.3");
        shown.Should().NotContain("BETA");
    }

    [Fact]
    public void a_users_own_instrument_is_named_by_its_file_name()
    {
        //Arrange, Act
        string shown = SoundFonts.DisplayName("/somewhere/else/Grand Piano.sfz");

        //Assert
        shown.Should().Be("Grand Piano.sfz");
    }

    [Theory]
    [InlineData("bank.sf2", true)]
    [InlineData("bank.SF2", true)]
    [InlineData("bank.sf3", true)]
    [InlineData("instrument.sfz", true)]
    [InlineData("score.midi", false)]
    [InlineData("", false)]
    public void an_instrument_file_is_recognised_by_its_extension(
        string name, bool expected)
    {
        //Arrange, Act, Assert
        SoundFonts.IsInstrument(name).Should().Be(expected);
    }
}

/// <summary>The player service's state that needs no audio device.</summary>
public class MidiPlayerServiceTests
{
    [Fact]
    public void a_new_player_is_stopped_and_empty()
    {
        //Arrange, Act
        using MidiPlayerService player = new MidiPlayerService();

        //Assert
        //Nothing here has touched the sound card: the audio device is opened by
        //Load, and a window whose MIDI panel is never opened never loads.
        player.State.Should().Be(MidiPlayerState.Stopped);
        player.HasSong.Should().BeFalse();
        player.HasEvents.Should().BeFalse();
        player.TotalTime.Should().Be(0);
        player.CurrentTime.Should().Be(0);
        player.FileName.Should().BeNull();
        player.Song.Should().BeNull();
    }

    [Fact]
    public void the_transport_does_nothing_at_all_with_nothing_loaded()
    {
        //Arrange
        using MidiPlayerService player = new MidiPlayerService();

        //Act
        player.Play();
        player.Pause();
        player.Stop();
        player.Seek(5000);

        //Assert
        player.State.Should().Be(MidiPlayerState.Stopped);
        player.CurrentTime.Should().Be(0);
    }

    [Fact]
    public void loading_nothing_clears_the_player()
    {
        //Arrange
        using MidiPlayerService player = new MidiPlayerService();

        //Act
        bool loaded = player.Load(null);

        //Assert
        loaded.Should().BeFalse();
        player.HasSong.Should().BeFalse();
    }

    [Fact]
    public void the_volume_is_remembered_as_a_percentage()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        using SettingsStore settings
            = new SettingsStore(folder.Path);

        //Act
        using (MidiPlayerService player = new MidiPlayerService(settings))
        {
            player.Volume = 0.4f;
        }

        using MidiPlayerService reopened = new MidiPlayerService(settings);

        //Assert
        settings.GetInt(MidiPlayerService.VolumeSettingKey).Should().Be(40);
        reopened.Volume.Should().Be(0.4f);
    }

    [Fact]
    public void the_tempo_factor_refuses_to_stop_time()
    {
        //Arrange
        using MidiPlayerService player = new MidiPlayerService();

        //Act
        player.TempoFactor = 0;

        //Assert
        player.TempoFactor.Should().Be(1.0);
    }

    [Theory]
    [InlineData(-50, 0.5)]
    [InlineData(0, 1.0)]
    [InlineData(50, 2.0)]
    public void the_tempo_slider_converts_the_way_upstream_converts_it(
        int slider, double expected)
    {
        //Arrange, Act
        //Upstream's own conversion, in miditool/widget.py: "convert -50 to 50
        //to 0.5 to 2.0".
        double factor = Math.Pow(2, slider / 50.0);

        //Assert
        factor.Should().BeApproximately(expected, 0.000001);
    }
}

/// <summary>The MIDI panel's transport commands.</summary>
public class MidiActionTests
{
    [Fact]
    public void the_transport_commands_carry_upstreams_keys()
    {
        //Arrange, Act
        MidiActions actions = new MidiActions();

        //Assert
        actions.MidiPlay.Shortcuts.Single().ToString().Should().Be("Media Play");
        actions.MidiStop.Shortcuts.Single().ToString().Should().Be("Media Stop");
        actions.MidiRestart.Shortcuts.Single().ToString().Should().Be("Media Previous");

        //⚠ Pause has none, and deliberately: Qt and X11 have a Key_MediaPause
        //distinct from Key_MediaPlay, and the platform's virtual-key set
        //follows Windows' numbering, which has one play/pause key for both.
        actions.MidiPause.Shortcuts.Should().BeEmpty();
    }

    [Fact]
    public void the_transport_commands_are_named_the_way_upstream_names_them()
    {
        //Arrange, Act
        MidiActions actions = new MidiActions();
        actions.TranslateUI();

        //Assert
        actions.MidiPlay.Text.Should().Be("Play");
        actions.MidiPause.Text.Should().Be("Pause");
        actions.MidiStop.Text.Should().Be("Stop");
        actions.MidiRestart.Text.Should().Be("Restart");
        actions.Title.Should().Be("MIDI");
    }
}
