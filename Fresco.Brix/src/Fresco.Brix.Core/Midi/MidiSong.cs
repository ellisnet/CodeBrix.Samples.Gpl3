// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Audio.Midi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Midi; //was previously: frescobaldi/midifile/song.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One beat of a song: where it falls, and what it is.</summary>
/// <remarks>Upstream's five-tuple <c>(msec, measnum, beat, num, den)</c>.
/// <see cref="Denominator"/> is the POWER, as the MIDI file stores it: 2 means
/// a quarter note, 3 an eighth.</remarks>
public readonly struct SongBeat : IEquatable<SongBeat>
{
    /// <summary>Creates a beat.</summary>
    /// <param name="time">When it falls, in milliseconds.</param>
    /// <param name="measure">Which measure it is in, counting from 1.</param>
    /// <param name="beat">Which beat of the measure, counting from 1.</param>
    /// <param name="numerator">The time signature's numerator.</param>
    /// <param name="denominator">The time signature's denominator, as the
    /// power of two the MIDI file stores.</param>
    public SongBeat(long time, int measure, int beat, int numerator, int denominator)
    {
        Time = time;
        Measure = measure;
        Beat = beat;
        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>Gets when the beat falls, in milliseconds.</summary>
    public long Time { get; }

    /// <summary>Gets which measure it is in, counting from 1.</summary>
    public int Measure { get; }

    /// <summary>Gets which beat of the measure it is, counting from 1.</summary>
    public int Beat { get; }

    /// <summary>Gets the time signature's numerator.</summary>
    public int Numerator { get; }

    /// <summary>Gets the time signature's denominator, as a power of two.</summary>
    public int Denominator { get; }

    /// <inheritdoc/>
    public bool Equals(SongBeat other)
        => Time == other.Time
            && Measure == other.Measure
            && Beat == other.Beat
            && Numerator == other.Numerator
            && Denominator == other.Denominator;

    /// <inheritdoc/>
    public override bool Equals(object obj) => obj is SongBeat other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Time, Measure, Beat, Numerator, Denominator);

    /// <inheritdoc/>
    public override string ToString()
        => $"{Measure}.{Beat} ({Numerator}/{1 << Math.Min(Denominator, 30)}) at {Time} ms";
}

/// <summary>
/// Turns MIDI time into real time: the file's tempo events, in order, and the
/// piecewise arithmetic that adds up the microseconds between them.
/// </summary>
public sealed class TempoMap
{
    /// <summary>The tempo a file with no tempo event of its own plays at —
    /// 500,000 microseconds a quarter note, which is 120 a minute.</summary>
    public const long DefaultMicrosecondsPerQuarter = 500000;

    private readonly List<(long MidiTime, long MicrosecondsPerQuarter)> _times;

    /// <summary>Creates the map over a file's tempo events.</summary>
    /// <param name="tempos">The tempo events, as (MIDI time, microseconds per
    /// quarter note) in ascending time order — at most one per time.</param>
    /// <param name="division">The header division, already resolved by
    /// <see cref="MidiSong.ResolveDivision"/>.</param>
    public TempoMap(
        IEnumerable<(long MidiTime, long MicrosecondsPerQuarter)> tempos, int division)
    {
        //A division of zero cannot be divided by. Upstream would raise
        //ZeroDivisionError on the first conversion; one tick is the smallest
        //value that keeps the arithmetic meaningful.
        Division = division > 0 ? division : 1;
        _times = tempos == null
            ? new List<(long, long)>()
            : tempos.ToList();

        if (_times.Count == 0 || _times[0].MidiTime != 0)
        {
            _times.Insert(0, (0, DefaultMicrosecondsPerQuarter));
        }
    }

    /// <summary>Gets the ticks per quarter note the map divides by.</summary>
    public int Division { get; }

    /// <summary>Gets the tempo changes, in time order.</summary>
    public IReadOnlyList<(long MidiTime, long MicrosecondsPerQuarter)> Times => _times;

    /// <summary>Gets the real time in microseconds for a MIDI time.</summary>
    /// <param name="midiTime">The MIDI time, in ticks.</param>
    /// <returns>The real time, in microseconds.</returns>
    public long RealTime(long midiTime)
    {
        long realTime = 0;
        bool broke = false;
        for (int index = 1; index < _times.Count; index++)
        {
            if (_times[index].MidiTime >= midiTime)
            {
                realTime += (midiTime - _times[index - 1].MidiTime)
                    * _times[index - 1].MicrosecondsPerQuarter;
                broke = true;
                break;
            }

            realTime += (_times[index].MidiTime - _times[index - 1].MidiTime)
                * _times[index - 1].MicrosecondsPerQuarter;
        }

        //Python's for/else: the last segment runs on to whatever was asked for.
        if (!broke)
        {
            realTime += (midiTime - _times[^1].MidiTime)
                * _times[^1].MicrosecondsPerQuarter;
        }

        return realTime / Division;
    }

    /// <summary>Gets the real time in milliseconds for a MIDI time.</summary>
    /// <param name="midiTime">The MIDI time, in ticks.</param>
    /// <returns>The real time, in milliseconds.</returns>
    public long Msec(long midiTime) => RealTime(midiTime) / 1000;
}

/// <summary>
/// A loaded MIDI file, seen as a song: how long it is, where its beats and
/// measures fall, and what real time each of its event times happens at.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what the MIDI panel needs from a file, and it is the
/// only part of upstream's <c>midifile/</c> package the port carries.
/// <c>parser.py</c> and <c>event.py</c> read the bytes, which
/// <see cref="CodeBrix.Audio.Midi.MidiFile"/> already does; <c>player.py</c>
/// steps through the events on a thread, which
/// <c>CodeBrix.Audio.Playback.MidiMusicPlayer</c> already does; and
/// <c>output.py</c> sends them to a PortMidi port, which ruling FR6 removed.
/// What was left was this: the tempo map and the beat grid, which nothing in
/// the audio library computes because nothing in it displays a bar number.
/// </para>
/// <para>
/// Upstream keeps every event, grouped by time and track, in
/// <c>Song.events</c> and <c>Song.music</c> — because its own player reads the
/// events back out to send them. Here the sequencer owns the events, so only
/// their TIMES are kept (<see cref="MusicTimes"/>), which is what the parity
/// test compares and all the position arithmetic needs.
/// </para>
/// </remarks>
public sealed class MidiSong
{
    private readonly List<SongBeat> _beats = new List<SongBeat>();
    private readonly List<long> _musicTimes = new List<long>();

    private MidiSong(int rawDivision, IReadOnlyList<IReadOnlyList<MidiEvent>> tracks)
    {
        RawDivision = rawDivision;
        Division = ResolveDivision(rawDivision);
        TrackCount = tracks.Count;

        //Upstream's events_dict: every event, grouped by time and then by
        //track. Only the two meta events the beat grid is made of are kept.
        List<long> eventTimes = new List<long>();
        SortedDictionary<long, List<(int Track, MidiEvent Event)>> meta
            = new SortedDictionary<long, List<(int, MidiEvent)>>();
        HashSet<long> seenTimes = new HashSet<long>();

        for (int track = 0; track < tracks.Count; track++)
        {
            foreach (MidiEvent midiEvent in tracks[track])
            {
                long time = midiEvent.AbsoluteTime;
                if (seenTimes.Add(time)) { eventTimes.Add(time); }

                if (midiEvent is TempoEvent or TimeSignatureEvent)
                {
                    if (!meta.TryGetValue(time, out var list))
                    {
                        list = new List<(int, MidiEvent)>();
                        meta[time] = list;
                    }

                    list.Add((track, midiEvent));
                }
            }
        }

        eventTimes.Sort();

        //Upstream walks the per-track dictionary in TRACK NUMBER order at each
        //time, so a tempo set on the first track wins over one set on a later
        //track at the same instant.
        foreach (var entry in meta)
        {
            entry.Value.Sort((left, right) => left.Track.CompareTo(right.Track));
        }

        List<(long MidiTime, long MicrosecondsPerQuarter)> tempos
            = new List<(long, long)>();
        List<(long MidiTime, int Numerator, int Denominator)> signatures
            = new List<(long, int, int)>();

        foreach (var entry in meta)
        {
            //Only the FIRST tempo at a time counts (upstream breaks out of the
            //inner loop); every time signature at a time is recorded.
            bool haveTempo = false;
            foreach ((int _, MidiEvent midiEvent) in entry.Value)
            {
                if (!haveTempo && midiEvent is TempoEvent tempo)
                {
                    tempos.Add((entry.Key, tempo.MicrosecondsPerQuarterNote));
                    haveTempo = true;
                }
                else if (midiEvent is TimeSignatureEvent signature)
                {
                    signatures.Add((entry.Key, signature.Numerator, signature.Denominator));
                }
            }
        }

        TempoMap = new TempoMap(tempos, Division);
        Length = eventTimes.Count == 0 ? 0 : TempoMap.Msec(eventTimes[^1]);

        int measure = 0;
        foreach ((long midiTime, int beat, int numerator, int denominator)
            in BeatGrid(eventTimes, signatures, Division))
        {
            if (beat == 1) { measure++; }

            _beats.Add(new SongBeat(
                TempoMap.Msec(midiTime), measure, beat, numerator, denominator));
        }

        foreach (long time in eventTimes)
        {
            _musicTimes.Add(TempoMap.Msec(time));
        }
    }

    /// <summary>Gets the division exactly as the file's header stores it.</summary>
    public int RawDivision { get; }

    /// <summary>Gets the ticks per quarter note, SMPTE divisions resolved.</summary>
    public int Division { get; }

    /// <summary>Gets how many tracks the song was built from.</summary>
    public int TrackCount { get; }

    /// <summary>Gets the length in milliseconds — the time of the last event.</summary>
    public long Length { get; }

    /// <summary>Gets the map from MIDI time to real time.</summary>
    public TempoMap TempoMap { get; }

    /// <summary>Gets every beat in the song, in time order.</summary>
    public IReadOnlyList<SongBeat> Beats => _beats;

    /// <summary>Gets the real time of every distinct event time, in order.</summary>
    public IReadOnlyList<long> MusicTimes => _musicTimes;

    /// <summary>Loads a song from a MIDI file.</summary>
    /// <param name="fileName">The file.</param>
    /// <returns>The song.</returns>
    /// <remarks>A format 2 file holds independent sequences rather than one
    /// piece, so — as upstream does — only its first track is read.</remarks>
    public static MidiSong Load(string fileName)
    {
        if (fileName == null) { throw new ArgumentNullException(nameof(fileName)); }

        using FileStream stream = File.OpenRead(fileName);
        return Load(stream);
    }

    /// <summary>Loads a song from an open MIDI stream.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The song.</returns>
    public static MidiSong Load(Stream stream)
    {
        if (stream == null) { throw new ArgumentNullException(nameof(stream)); }

        //Not strict: a file found in the wild may have note-ons without their
        //note-offs, and upstream's own parser does not pair them at all.
        MidiFile file = new MidiFile(stream, strictChecking: false);
        MidiEventCollection events = file.Events;

        int trackCount = events.Tracks;
        if (file.FileFormat == 2 && trackCount > 1) { trackCount = 1; }

        List<IReadOnlyList<MidiEvent>> tracks = new List<IReadOnlyList<MidiEvent>>();
        for (int track = 0; track < trackCount; track++)
        {
            tracks.Add((IReadOnlyList<MidiEvent>)events.GetTrackEvents(track));
        }

        return new MidiSong(file.DeltaTicksPerQuarterNote, tracks);
    }

    /// <summary>
    /// Converts a header division from a SMPTE type, if it is one.
    /// </summary>
    /// <param name="division">The raw header division, as the unsigned 16-bit
    /// value the file stores.</param>
    /// <returns>The ticks per quarter note.</returns>
    /// <remarks>
    /// <para>
    /// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14) — first of three in
    /// this file, all of them the same defect wearing different clothes:
    /// upstream reads a header field, does not check what is in it, and then
    /// either loops forever or divides by zero.
    /// </para>
    /// <para>
    /// UPSTREAM: <c>midifile/parser.py</c> unpacks the header with
    /// <c>struct.Struct(b'&gt;hhh')</c> — SIGNED shorts — so a SMPTE division
    /// arrives NEGATIVE, while <c>song.smpte_division()</c> is written for the
    /// UNSIGNED word (its <c>256 - (div &gt;&gt; 8)</c> is the standard "negate
    /// the frames byte" idiom, correct only when the high byte reads as
    /// 128..255). Fed -6104 it answers 11200 instead of 960. Worse,
    /// <c>Song.__init__</c> passes the RAW division to <c>beats()</c> while
    /// giving <c>TempoMap</c> the converted one — so <c>beats()</c> computes a
    /// NEGATIVE step, <c>time += step</c> counts backwards, and the loop
    /// <c>while time &lt;= times[-1]</c> NEVER ENDS. Frescobaldi hangs on any
    /// SMPTE-timed MIDI file. (Verified: <c>tools/midiprobe</c> gives upstream
    /// twenty seconds per file and <c>syn-smpte.midi</c> is recorded as
    /// unanswered.)
    /// </para>
    /// <para>
    /// HERE: the division is read as the unsigned word the format actually
    /// stores, the conversion is upstream's own arithmetic on that value, and
    /// the SAME resolved division is used by both the tempo map and the beat
    /// grid. Non-SMPTE files are unaffected — the conversion is the identity
    /// for them, which is why 102 of the corpus's 106 files match upstream
    /// exactly.
    /// </para>
    /// </remarks>
    public static int ResolveDivision(int division)
    {
        //The unsigned 16 bits the header holds; anything wider is a caller's
        //mistake rather than a file's.
        int raw = division & 0xFFFF;
        if ((raw & 0x8000) == 0) { return raw; }

        int frames = 256 - ((raw >> 8) & 0xFF);
        int resolution = raw & 0xFF;
        return frames * resolution;
    }

    /// <summary>Gets the beat at a time.</summary>
    /// <param name="time">The time, in milliseconds.</param>
    /// <returns>The beat; an empty 4/4 first beat when the song has none.</returns>
    public SongBeat Beat(long time)
    {
        if (_beats.Count == 0) { return new SongBeat(0, 0, 0, 4, 2); }

        int position = 0;
        if (time != 0)
        {
            //Upstream's own bisection, kept exactly: the answer for a time one
            //millisecond past a beat is the NEXT beat, not that one.
            int end = _beats.Count;
            while (position < end)
            {
                int middle = (position + end) / 2;
                if (time > _beats[middle].Time) { position = middle + 1; }
                else { end = middle; }
            }
        }

        return _beats[Math.Min(position, _beats.Count - 1)];
    }

    /// <summary>
    /// Yields a tuple for every beat: the MIDI time, which beat of the measure
    /// it is, and the time signature in force.
    /// </summary>
    /// <param name="eventTimes">Every distinct event time, ascending.</param>
    /// <param name="signatures">The time-signature events, in time order.</param>
    /// <param name="division">The resolved ticks per quarter note.</param>
    /// <returns>The beats.</returns>
    private static IEnumerable<(long MidiTime, int Beat, int Numerator, int Denominator)>
        BeatGrid(
            IReadOnlyList<long> eventTimes,
            IReadOnlyList<(long MidiTime, int Numerator, int Denominator)> signatures,
            int division)
    {
        if (eventTimes.Count == 0) { yield break; }

        List<(long MidiTime, int Numerator, int Denominator)> timeSignatures
            = signatures.ToList();
        if (timeSignatures.Count == 0 || timeSignatures[0].MidiTime != 0)
        {
            //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14), and the one
            //that changes an ANSWER rather than preventing a hang.
            //UPSTREAM: `time_sigs.insert(0, (0, (4, 4, 24, 8)))`. Every other
            //entry in that list comes from get_time_signature(), which returns
            //the raw bytes of a MIDI Time Signature event — where byte 1 is dd,
            //the POWER, so a quarter note is 2. Writing 4 there means a
            //SIXTEENTH note, so a file with no time signature of its own is
            //gridded four times too finely, counts four times too many
            //measures, and shows "4/16" on the panel's display (widget.py
            //builds it as f"{num}/{2 ** den}"). That the other two members are
            //24 and 8 — exactly 4/4's standard clocks and 32nds — is what
            //shows the intent: it is the standard 4/4 default event with the
            //numerator written twice.
            //HERE: the standard default, 4/4 with the denominator as its power.
            //The oracle is generated WITH this fix declared (route (i), the
            //W8a precedent): tools/midiprobe's KNOWN_FIXES, recorded in the
            //fixture and asserted by MidiParityTests.
            timeSignatures.Insert(0, (0, 4, 2));
        }

        long last = eventTimes[^1];
        long time = 0;
        long step = 0;
        int beat = 1;
        int numerator = 4;
        int denominator = 2;
        int index = 0;

        while (time <= last)
        {
            if (index < timeSignatures.Count && time >= timeSignatures[index].MidiTime)
            {
                //A signature landing between beats moves the grid BACK to its
                //own time, which is upstream's behaviour and is why the beats
                //of a song are not always strictly increasing.
                time = timeSignatures[index].MidiTime;
                numerator = timeSignatures[index].Numerator;
                denominator = timeSignatures[index].Denominator;
                step = BeatStep(division, denominator);
                beat = 1;
                index++;
            }

            //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14), second of
            //three. UPSTREAM: nothing stops `step` being zero — it is
            //`(4 * division) // (2 ** den)` and a denominator power big enough
            //(the field holds 0..255) floors it to nothing — after which
            //`time += step` never moves and the loop spins forever. Verified:
            //`syn-den-huge.midi` is recorded as unanswered. HERE: a step that
            //cannot advance ends the grid, having yielded the downbeat the
            //signature does establish.
            if (step <= 0) { yield return (time, beat, numerator, denominator); yield break; }

            yield return (time, beat, numerator, denominator);
            time += step;

            //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14), third of
            //three. UPSTREAM: `beat = beat % num + 1` raises ZeroDivisionError
            //on the second beat of a 0/n signature, which the four-byte field
            //permits and `Display.updateDisplay` even tests for
            //(`if num else ""`). Verified: `syn-num-zero.midi` is recorded as
            //unanswered. HERE: with no beats to a measure the counter simply
            //does not wrap, so the song stays one long measure.
            beat = numerator > 0 ? (beat % numerator) + 1 : beat + 1;
        }
    }

    /// <summary>Gets the ticks between beats for a time-signature denominator.</summary>
    /// <param name="division">The resolved ticks per quarter note.</param>
    /// <param name="denominator">The denominator, as a power of two.</param>
    /// <returns>The step, or zero when the beat is finer than one tick.</returns>
    private static long BeatStep(int division, int denominator)
    {
        //2**den, where den comes from a file and may be up to 255. Anything
        //past 62 is larger than any division four times over, so the step is
        //zero by the same arithmetic upstream does — without the overflow that
        //shifting by 255 would be in C#.
        if (denominator < 0 || denominator > 62) { return 0; }

        return 4L * division / (1L << denominator);
    }
}
