// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG19's arithmetic, fenced against values taken from the MIDI SPECIFICATION and from
/// upstream's expressions — never from the port's own output.
/// </summary>
/// <remarks>
/// The distinction matters more here than usual. A MIDI file is bytes, so it is very easy
/// to write a test that records whatever the port emits and calls it green; such a test
/// locks in a wrong answer and then defends it. Every expected value below is either
/// stated by the Standard MIDI File specification, hand-computed from upstream's formula,
/// or lifted from the ORACLE's own bytes — and where it is the last of those, the comment
/// says so.
/// </remarks>
public class Epg19Tests
{
    [Fact]
    public void a_variable_length_quantity_matches_the_midi_specification()
    {
        //Arrange
        // These five pairs are FROM THE SPEC's own table of variable-length quantities,
        // not from the port. The interesting ones are 128 and 0x0FFFFF: the first is the
        // smallest value needing two bytes, the second the largest fitting three.
        (int Value, byte[] Expected)[] cases =
        {
            (0x00, new byte[] { 0x00 }),
            (0x40, new byte[] { 0x40 }),
            (0x7F, new byte[] { 0x7F }),
            (0x80, new byte[] { 0x81, 0x00 }),
            (0x2000, new byte[] { 0xC0, 0x00 }),
            (0x0FFFFF, new byte[] { 0xBF, 0xFF, 0x7F }),
        };

        //Act / Assert
        foreach ((int value, byte[] expected) in cases)
        {
            MidiItem.Int2MidiVarintBytes(value).Should().Equal(expected);
        }
    }

    [Fact]
    public void a_quarter_note_is_three_hundred_and_eighty_four_ticks()
    {
        //Arrange
        // 384 ticks per quarter is what Performance::output writes into the MThd chunk,
        // and moment_to_ticks is (whole notes) * 384 * 4. A quarter note is 1/4 whole, so
        // 1/4 * 1536 = 384 -- hand-computed, and confirmed by the oracle's own files,
        // whose division field reads 0x0180.
        Moment quarter = new Moment(new Rational(1, 4));
        Moment whole = new Moment(Rational.One);

        //Act / Assert
        AudioMoment.ToTicks(quarter).Should().Be(384);
        AudioMoment.ToTicks(whole).Should().Be(1536);
        AudioMoment.ToTicks(Moment.Zero).Should().Be(0);
    }

    [Fact]
    public void a_grace_moment_is_weighted_by_nine_fortieths()
    {
        //Arrange
        // moment_to_real is main_part + (9/40) * grace_part. A grace part of 1/4 therefore
        // contributes 9/160 of a whole note: 0.05625, and 0.05625 * 1536 = 86.4, which
        // truncates to 86. Hand-computed from upstream's constant; the truncation is
        // upstream's int() cast.
        Moment graceQuarter = new Moment(Rational.Zero, new Rational(1, 4));

        //Act / Assert
        AudioMoment.ToReal(graceQuarter).Should().BeApproximately(0.05625, 1e-12);
        AudioMoment.ToTicks(graceQuarter).Should().Be(86);
    }

    [Fact]
    public void a_common_time_signature_is_the_bytes_the_specification_names()
    {
        //Arrange
        // FF 58 04 nn dd cc bb: numerator, log2 of the denominator, MIDI clocks per
        // metronome click, 32nds per quarter. For 4/4 with a quarter-note click that is
        // 04 02 18 08 -- and those exact seven bytes appear in the oracle's own control
        // track (ff 58 04 04 02 18 08), which is where this expectation is checked from.
        AudioTimeSignature signature = new AudioTimeSignature(
            new Rational(4), new Rational(4), 24);

        //Act
        byte[] bytes = new MidiTimeSignature(signature).ToBytes();

        //Assert
        bytes.Should().Equal(new byte[] { 0xFF, 0x58, 0x04, 0x04, 0x02, 0x18, 0x08 });
    }

    [Fact]
    public void a_key_signature_encodes_its_accidental_count_and_mode()
    {
        //Arrange
        // FF 59 02 sf mi. Two sharps major is 02 00; two flats minor is FE 01, because sf
        // is a SIGNED count written as one byte and -2 is 0xFE. Both from the spec.
        AudioKey twoSharpsMajor = new AudioKey(2, true);
        AudioKey twoFlatsMinor = new AudioKey(-2, false);

        //Act / Assert
        new MidiKey(twoSharpsMajor).ToBytes()
            .Should().Equal(new byte[] { 0xFF, 0x59, 0x02, 0x02, 0x00 });
        new MidiKey(twoFlatsMinor).ToBytes()
            .Should().Equal(new byte[] { 0xFF, 0x59, 0x02, 0xFE, 0x01 });
    }

    [Fact]
    public void sixty_quarter_notes_a_minute_is_a_million_microseconds_a_quarter()
    {
        //Arrange
        // DEFAULT_WPM is 60/4 = 15 wholes a minute = 60 quarters a minute, so one quarter
        // lasts exactly one second: 1,000,000 microseconds, which is 0x0F4240. The oracle
        // writes precisely `ff 51 03 0f 42 40' at the head of every default-tempo file.
        AudioSpanTempo span = new AudioSpanTempo(
            Moment.Zero, AudioSpanTempo.DefaultWholesPerMinute);
        span.SetEndMoment(new Moment(Rational.One));

        AudioTempo tempo = new AudioTempo(span, Moment.Zero);
        tempo.SetEndMoment(new Moment(Rational.One));

        //Act
        byte[] bytes = new MidiTempo(tempo).ToBytes();

        //Assert
        bytes.Should().Equal(new byte[] { 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40 });
    }

    [Fact]
    public void a_constant_tempo_span_averages_to_its_own_tempo()
    {
        //Arrange
        // With no gain, instant_wpm is constant, so num = tr - tl = 0 and upstream's
        // guard returns start_wpm_ unchanged. Derivable rather than recorded: a tempo
        // that does not change cannot average to anything else.
        AudioSpanTempo span = new AudioSpanTempo(Moment.Zero, new Rational(15));
        span.SetEndMoment(new Moment(Rational.One));

        //Act
        Rational average = span.CalcAverageWholesPerMinute(
            new DrulArray<Moment>(Moment.Zero, new Moment(Rational.One)));

        //Assert
        average.Should().Be(new Rational(15));
    }

    [Fact]
    public void end_of_track_is_three_bytes_including_its_terminating_nul()
    {
        //Arrange
        // Upstream writes std::string ("\\xff\\x2f", 3) and comments that the literal's
        // terminating NUL is part of the command. FF 2F 00 is also what the spec says,
        // and what the oracle's files end with.

        //Act
        byte[] bytes = new MidiEndOfTrack().ToBytes();

        //Assert
        bytes.Should().Equal(new byte[] { 0xFF, 0x2F, 0x00 });
    }

    [Fact]
    public void middle_c_is_midi_note_sixty()
    {
        //Arrange
        // Pitch(0, 0, 0) is middle C, whose tone_pitch is 0, so get_semitone_pitch is 0
        // and the byte written is 0 + c0_pitch_ = 60. The MIDI standard puts middle C at
        // 60, which is the whole reason c0_pitch_ has that value.
        AudioNote note = new AudioNote(
            new Pitch(0, 0, Rational.Zero),
            new Moment(new Rational(1, 4)),
            false,
            new Pitch(0, 0, Rational.Zero),
            0);

        note.AudioColumn = new AudioColumn(Moment.Zero);
        MidiNote midi = new MidiNote(note);

        //Act
        byte[] bytes = midi.ToBytes();

        //Assert
        midi.GetSemitonePitch().Should().Be(0);
        midi.GetFineTuning().Should().Be(0);

        // 90 3C 5A: note-on channel 0, note 60, velocity 0x5A -- the 0x5a is upstream's
        // no-dynamic default, and it is what the oracle emits for an undynamic note.
        bytes.Should().Equal(new byte[] { 0x90, 0x3C, 0x5A });
    }

    [Fact]
    public void a_note_off_is_a_note_on_with_zero_velocity()
    {
        //Arrange
        // Upstream's comment says so in as many words. The oracle's files bear it out:
        // every note ends with `90 <pitch> 00' rather than an 0x80 status.
        AudioNote note = new AudioNote(
            new Pitch(0, 0, Rational.Zero),
            new Moment(new Rational(1, 4)),
            false,
            new Pitch(0, 0, Rational.Zero),
            0);

        note.AudioColumn = new AudioColumn(Moment.Zero);

        //Act
        byte[] bytes = new MidiNoteOff(new MidiNote(note)).ToBytes();

        //Assert
        bytes.Should().Equal(new byte[] { 0x90, 0x3C, 0x00 });
    }

    [Fact]
    public void a_chunk_carries_its_length_big_endian()
    {
        //Arrange
        // MThd is always six bytes of payload: format, track count, division. The header
        // of every SMF therefore begins 4D 54 68 64 00 00 00 06, which is exactly what the
        // oracle's files start with.

        //Act
        byte[] bytes = new MidiHeader(1, 2, 384).ToBytes();

        //Assert
        bytes.Should().Equal(new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, // "MThd"
            0x00, 0x00, 0x00, 0x06, // length
            0x00, 0x01,             // format 1
            0x00, 0x02,             // 2 tracks
            0x01, 0x80,             // 384 ticks per quarter
        });
    }

    [Fact]
    public void a_track_puts_a_non_note_event_ahead_of_notes_at_the_same_tick()
    {
        //Arrange
        // Midi_track::add exists for exactly this: an instrument change at the same
        // instant as a note must take effect BEFORE the note sounds. Derived from what
        // the method is for, not from what the port happens to do.
        MidiTrack track = new MidiTrack(0, false);

        AudioNote note = new AudioNote(
            new Pitch(0, 0, Rational.Zero),
            new Moment(new Rational(1, 4)),
            false,
            new Pitch(0, 0, Rational.Zero),
            0);
        note.AudioColumn = new AudioColumn(Moment.Zero);

        AudioControlChange control = new AudioControlChange(10, 64);

        //Act
        track.Add(0, new MidiNote(note));         // a note starts here
        track.Add(0, new MidiControlChange(control)); // and a control change joins it

        //Assert
        // The control change is placed FIRST despite being added second.
        track.Events.Should().HaveCount(2);
        track.Events[0].Midi.Should().BeOfType<MidiControlChange>();
        track.Events[1].Midi.Should().BeOfType<MidiNote>();
    }

    [Fact]
    public void a_track_leaves_a_note_start_where_it_was_added()
    {
        //Arrange
        // The control in the pair: the reordering above must NOT happen for a note, or
        // chords would come apart. Upstream's condition excludes Midi_note unless it is a
        // Midi_note_off, and this is the half that would silently pass if the condition
        // were inverted.
        MidiTrack track = new MidiTrack(0, false);

        AudioNote lower = new AudioNote(
            new Pitch(0, 0, Rational.Zero), new Moment(new Rational(1, 4)),
            false, new Pitch(0, 0, Rational.Zero), 0);
        lower.AudioColumn = new AudioColumn(Moment.Zero);

        AudioNote upper = new AudioNote(
            new Pitch(0, 2, Rational.Zero), new Moment(new Rational(1, 4)),
            false, new Pitch(0, 0, Rational.Zero), 0);
        upper.AudioColumn = new AudioColumn(Moment.Zero);

        //Act
        track.Add(0, new MidiNote(lower));
        track.Add(0, new MidiNote(upper));

        //Assert
        track.Events.Should().HaveCount(2);
        ((MidiNote)track.Events[0].Midi).GetSemitonePitch().Should().Be(0);
        ((MidiNote)track.Events[1].Midi).GetSemitonePitch().Should().Be(4);
    }

    [Fact]
    public void a_text_event_counts_utf_eight_bytes_not_characters()
    {
        //Arrange
        // THE DEFECT THIS FENCES would be invisible in ASCII. "é" is ONE .NET char and
        // TWO UTF-8 bytes; a length prefix taken from string.Length would say 1 and
        // truncate the payload, producing a file that parses and loses the text.
        AudioText text = new AudioText(AudioTextType.Lyric, "é");

        //Act
        byte[] bytes = new MidiText(text).ToBytes();

        //Assert
        // FF 05 02 C3 A9: meta, lyric, length TWO, then the two UTF-8 bytes.
        bytes.Should().Equal(new byte[] { 0xFF, 0x05, 0x02, 0xC3, 0xA9 });
    }

    [Fact]
    public void the_stop_note_queue_answers_the_earliest_tick_first()
    {
        //Arrange
        // Midi_walker's queue is ordered by stop tick, and do_stop_notes walks it while
        // the FRONT is at or before the current tick — so the ordering property, not any
        // particular internal layout, is what the walker depends on.
        PriorityQueue<MidiNoteEvent> queue
            = new PriorityQueue<MidiNoteEvent>(MidiNoteEventComparer.Instance);

        foreach (int key in new[] { 768, 192, 1536, 384 })
        {
            queue.Insert(new MidiNoteEvent { Key = key });
        }

        //Act
        List<int> order = new List<int>();
        while (queue.Count > 0)
        {
            order.Add(queue.DeleteMinimum().Key);
        }

        //Assert
        order.Should().Equal(new List<int> { 192, 384, 768, 1536 });
    }

    [Fact]
    public void a_double_becomes_the_rational_upstream_would_build()
    {
        //Arrange
        // UPSTREAM'S ALGORITHM IS LOSSY ON PURPOSE and this is the fence that keeps the
        // port from "improving" it back into the overflow EPG19 found. 0.1 has no exact
        // binary form; upstream takes twenty bits of mantissa, so the answer is a ratio
        // whose denominator is a power of two and whose numerator fits comfortably —
        // NOT the exact dyadic rational, whose numerator is 3602879701896397.
        Rational tenth = Rational.FromDouble(0.1);

        //Act / Assert
        // The exact dyadic value would have this denominator; upstream's never does.
        tenth.Denominator.Should().NotBe(36028797018963968L);
        tenth.ToDouble().Should().BeApproximately(0.1, 1e-6);

        // Exact powers of two survive exactly, which is what keeps musical durations
        // (all dyadic and small) unharmed by the lossy path.
        Rational.FromDouble(0.25).Should().Be(new Rational(1, 4));
        Rational.FromDouble(2.0).Should().Be(new Rational(2));
        Rational.FromDouble(0.0).Should().Be(Rational.Zero);
    }

    [Fact]
    public void a_huge_double_saturates_rather_than_throwing()
    {
        //Arrange
        // THE REGRESSION THIS FENCES cost eleven truncated MIDI files: the old exact
        // conversion threw on a value whose numerator did not fit a ulong, and the
        // exception escaped mid-write. Whatever the answer is, it must not be an
        // exception.

        //Act
        Rational huge = Rational.FromDouble(1e300);
        Rational tiny = Rational.FromDouble(1e-300);

        //Assert
        huge.Should().Be(Rational.Infinity);
        tiny.Should().Be(Rational.Zero);
    }

    [Fact]
    public void a_tie_moves_the_whole_length_onto_the_head_of_the_tie()
    {
        //Arrange
        // Audio_note::tie_to gives the FIRST note the combined duration and leaves the
        // second at zero, which is why a tied pair sounds as one note in MIDI. Two tied
        // quarters make a half: 1/4 + 1/4 = 1/2, hand-computed.
        AudioNote first = new AudioNote(
            new Pitch(0, 0, Rational.Zero), new Moment(new Rational(1, 4)),
            false, new Pitch(0, 0, Rational.Zero), 0);

        AudioNote second = new AudioNote(
            new Pitch(0, 0, Rational.Zero), new Moment(new Rational(1, 4)),
            true, new Pitch(0, 0, Rational.Zero), 0);

        //Act
        second.TieTo(first);

        //Assert
        first.LengthMoment.Should().Be(new Moment(new Rational(1, 2)));
        second.LengthMoment.Should().Be(Moment.Zero);
        second.TieHead().Should().BeSameAs(first);
    }

    [Fact]
    public void a_dynamic_span_interpolates_linearly_between_its_endpoints()
    {
        //Arrange
        // Audio_span_dynamic::get_volume is start + gain * (when / duration). Over one
        // whole note from 0.4 to 0.8, the halfway point is 0.6 — hand-computed from the
        // formula, not read off the port.
        AudioSpanDynamic span = new AudioSpanDynamic(Moment.Zero, 0.4);
        span.SetEndMoment(new Moment(Rational.One));
        span.SetVolume(0.4, 0.8);

        //Act
        double halfway = span.GetVolume(new Moment(new Rational(1, 2)));

        //Assert
        span.GetVolume(Moment.Zero).Should().BeApproximately(0.4, 1e-12);
        halfway.Should().BeApproximately(0.6, 1e-12);
    }
}
