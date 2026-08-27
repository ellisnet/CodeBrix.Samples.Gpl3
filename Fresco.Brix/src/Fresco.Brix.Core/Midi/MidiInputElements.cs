// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Pitching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fresco.Brix.Midi; //was previously: frescobaldi/midiinput/elements.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The fifteen key signatures' worth of "which MIDI note is which written
/// note", built once and shared.
/// </summary>
/// <remarks>
/// Upstream builds the tables by taking C major's twelve semitones and
/// re-spelling one note at a time in the order the accidentals appear in a key
/// signature. Index 0 is seven flats and index 14 is seven sharps, with C major
/// in the middle at 7 — which is why the panel's key-signature list is in that
/// order and why its default index is 7.
/// </remarks>
public sealed class NoteMappings
{
    /// <summary>The one instance, as upstream's class attribute is.</summary>
    public static readonly NoteMappings Instance = new NoteMappings();

    private readonly int[] _keyOrderSharp;
    private readonly int[] _keyOrderFlat;
    private readonly (int Note, Fraction Alter)[] _sharps;
    private readonly (int Note, Fraction Alter)[] _flats;

    private NoteMappings()
    {
        //The order the flats and the sharps arrive in, as scale degrees.
        _keyOrderSharp = new[] { 6, 1, 8, 3, 10, 5, 0 };
        _keyOrderFlat = new[] { 10, 3, 8, 1, 6, 11, 4 };

        _sharps = new[]
        {
            (0, Half(0)),   //c
            (0, Half(1)),   //cis
            (1, Half(0)),   //d
            (1, Half(1)),   //dis
            (2, Half(0)),   //e
            (3, Half(0)),   //f
            (3, Half(1)),   //fis
            (4, Half(0)),   //g
            (4, Half(1)),   //gis
            (5, Half(0)),   //a
            (5, Half(1)),   //ais
            (6, Half(0)),   //b
        };

        _flats = new[]
        {
            (0, Half(0)),   //c
            (1, Half(-1)),  //des
            (1, Half(0)),   //d
            (2, Half(-1)),  //es
            (2, Half(0)),   //e
            (3, Half(0)),   //f
            (4, Half(-1)),  //ges
            (4, Half(0)),   //g
            (5, Half(-1)),  //aes
            (5, Half(0)),   //a
            (6, Half(-1)),  //bes
            (6, Half(0)),   //b
        };

        List<(int Note, Fraction Alter)[]> sharpMappings
            = new List<(int, Fraction)[]>();
        List<(int Note, Fraction Alter)[]> flatMappings
            = new List<(int, Fraction)[]>();

        //Seven flats down to one.
        for (int i = _keyOrderFlat.Length - 1; i >= 0; i--)
        {
            var flatMap = ((int Note, Fraction Alter)[])_flats.Clone();
            var sharpMap = ((int Note, Fraction Alter)[])_sharps.Clone();
            foreach (int k in _keyOrderFlat.Take(i + 1))
            {
                flatMap[k] = ToFlat(flatMap[k]);
                sharpMap[k] = ToFlat(sharpMap[k]);
            }

            flatMappings.Add(flatMap);
            sharpMappings.Add(sharpMap);
        }

        //C major, where nothing is re-spelled.
        sharpMappings.Add(((int Note, Fraction Alter)[])_sharps.Clone());
        flatMappings.Add(((int Note, Fraction Alter)[])_flats.Clone());

        //One sharp up to seven.
        for (int i = 0; i < _keyOrderSharp.Length; i++)
        {
            var flatMap = ((int Note, Fraction Alter)[])_flats.Clone();
            var sharpMap = ((int Note, Fraction Alter)[])_sharps.Clone();
            foreach (int k in _keyOrderSharp.Take(i + 1))
            {
                flatMap[k] = ToSharp(flatMap[k]);
                sharpMap[k] = ToSharp(sharpMap[k]);
            }

            flatMappings.Add(flatMap);
            sharpMappings.Add(sharpMap);
        }

        SharpMappings = sharpMappings;
        FlatMappings = flatMappings;
    }

    /// <summary>Gets the order sharps appear in a key signature.</summary>
    public IReadOnlyList<int> KeyOrderSharp => _keyOrderSharp;

    /// <summary>Gets the order flats appear in a key signature.</summary>
    public IReadOnlyList<int> KeyOrderFlat => _keyOrderFlat;

    /// <summary>Gets C major spelled with sharps.</summary>
    public IReadOnlyList<(int Note, Fraction Alter)> Sharps => _sharps;

    /// <summary>Gets C major spelled with flats.</summary>
    public IReadOnlyList<(int Note, Fraction Alter)> Flats => _flats;

    /// <summary>Gets the fifteen sharp-preferring tables.</summary>
    public IReadOnlyList<(int Note, Fraction Alter)[]> SharpMappings { get; }

    /// <summary>Gets the fifteen flat-preferring tables.</summary>
    public IReadOnlyList<(int Note, Fraction Alter)[]> FlatMappings { get; }

    /// <summary>Re-spells a note as the sharp of the one below it.</summary>
    /// <param name="entry">The note and its alteration.</param>
    /// <returns>The re-spelled note.</returns>
    public static (int Note, Fraction Alter) ToSharp((int Note, Fraction Alter) entry)
        => entry.Alter == Half(1) ? entry : (entry.Note - 1, Half(1));

    /// <summary>Re-spells a note as the flat of the one above it.</summary>
    /// <param name="entry">The note and its alteration.</param>
    /// <returns>The re-spelled note.</returns>
    public static (int Note, Fraction Alter) ToFlat((int Note, Fraction Alter) entry)
        => entry.Alter == Half(-1) ? entry : (entry.Note + 1, Half(-1));

    private static Fraction Half(int halves) => new Fraction(halves, 2);
}

/// <summary>One key signature's table of MIDI note to written note.</summary>
public sealed class NoteMapping
{
    private readonly (int Note, Fraction Alter)[] _mapping;

    /// <summary>Creates the mapping for a key signature.</summary>
    /// <param name="keySignature">0 is seven flats, 7 is C major, 14 is seven
    /// sharps.</param>
    /// <param name="sharps">Whether an unaltered note prefers a sharp
    /// spelling.</param>
    public NoteMapping(int keySignature, bool sharps = true)
    {
        IReadOnlyList<(int, Fraction)[]> tables = sharps
            ? NoteMappings.Instance.SharpMappings
            : NoteMappings.Instance.FlatMappings;
        _mapping = tables[Math.Clamp(keySignature, 0, tables.Count - 1)];
    }

    /// <summary>Gets how many semitones the table covers.</summary>
    public int Count => _mapping.Length;

    /// <summary>Gets the written note for a semitone.</summary>
    /// <param name="index">The semitone, 0 to 11.</param>
    /// <returns>The note and its alteration.</returns>
    public (int Note, Fraction Alter) this[int index] => _mapping[index];
}

/// <summary>A note played on a MIDI keyboard, as LilyPond would write it.</summary>
public sealed class MidiNote
{
    /// <summary>
    /// The pitch the next relative note is written against.
    /// </summary>
    /// <remarks>Upstream's <c>Note.LastPitch</c> is a CLASS attribute, so it is
    /// one per application and not one per document — the same arrangement as
    /// the rhythm clipboard (W6). It refers to the last KEY PRESSED, which is
    /// what upstream's own tooltip says.</remarks>
    public static Pitch LastPitch { get; } = new Pitch();

    private readonly Pitch _pitch;

    /// <summary>Creates a note from a MIDI note number.</summary>
    /// <param name="midiNote">The MIDI note number, 0 to 127.</param>
    /// <param name="mapping">The key signature's spelling table.</param>
    public MidiNote(int midiNote, NoteMapping mapping)
    {
        if (mapping == null) { throw new ArgumentNullException(nameof(mapping)); }

        MidiNoteNumber = midiNote;

        int octave = FloorDiv(midiNote, 12) - 4;
        int semitone = FloorMod(midiNote, 12);
        (int note, Fraction alter) = mapping[semitone];

        //⚠ Python's // and % FLOOR; C#'s / and % truncate toward zero. It
        //matters here: a table entry's note can be -1 (C re-spelled as B sharp
        //an octave down), and -1 % 7 is 6 with an octave of -1 in python but
        //-1 with an octave of 0 in C#.
        _pitch = new Pitch(FloorMod(note, 7), alter, octave + FloorDiv(note, 7));
    }

    /// <summary>Gets the MIDI note number this was made from.</summary>
    public int MidiNoteNumber { get; }

    /// <summary>Gets the written pitch.</summary>
    public Pitch Pitch => _pitch;

    /// <summary>Writes the note as LilyPond source.</summary>
    /// <param name="relativeMode">Whether to write the octave relative to the
    /// last key pressed.</param>
    /// <param name="language">The pitch-name language.</param>
    /// <param name="octaveCheck">Whether to add an octave check — upstream adds
    /// one while Shift is held.</param>
    /// <returns>The source text.</returns>
    public string Output(
        bool relativeMode = false,
        string language = "nederlands",
        bool octaveCheck = false)
    {
        Pitch pitch;
        if (relativeMode)
        {
            pitch = _pitch.Copy();
            pitch.MakeRelative(LastPitch);
            LastPitch.Note = _pitch.Note;
            LastPitch.Octave = _pitch.Octave;
        }
        else
        {
            pitch = _pitch;
        }

        return pitch.Output(language)
            + (octaveCheck ? "=" + Pitches.OctaveToString(_pitch.Octave) : string.Empty);
    }

    internal static int FloorDiv(int a, int b)
    {
        int quotient = a / b;
        return a % b != 0 && (a < 0) != (b < 0) ? quotient - 1 : quotient;
    }

    internal static int FloorMod(int a, int b) => a - (FloorDiv(a, b) * b);
}

/// <summary>Notes held down together, written as one chord.</summary>
public sealed class MidiChord
{
    private readonly List<MidiNote> _notes = new List<MidiNote>();

    /// <summary>Gets the notes, in the order they were played.</summary>
    public IReadOnlyList<MidiNote> Notes => _notes;

    /// <summary>Adds a note to the chord.</summary>
    /// <param name="note">The note.</param>
    public void Add(MidiNote note)
    {
        if (note != null) { _notes.Add(note); }
    }

    /// <summary>Writes the chord as LilyPond source.</summary>
    /// <param name="relativeMode">Whether to write octaves relative to the last
    /// key pressed.</param>
    /// <param name="language">The pitch-name language.</param>
    /// <param name="octaveCheck">Whether to add octave checks.</param>
    /// <returns>The source text; a lone note is written as a note, not a
    /// chord.</returns>
    public string Output(
        bool relativeMode = false,
        string language = "nederlands",
        bool octaveCheck = false)
    {
        if (_notes.Count == 0) { return string.Empty; }

        if (_notes.Count == 1)
        {
            return _notes[0].Output(relativeMode, language, octaveCheck);
        }

        //Python's sorted() is STABLE, so two notes of the same pitch keep the
        //order they were played in.
        List<MidiNote> sorted = _notes
            .OrderBy(note => note.MidiNoteNumber)
            .ToList();

        //The chord's LOWEST note is what the next relative note is written
        //against, so it is remembered before the notes overwrite it one by one.
        int lastNote = sorted[0].Pitch.Note;
        int lastOctave = sorted[0].Pitch.Octave;

        StringBuilder chord = new StringBuilder();
        foreach (MidiNote note in sorted)
        {
            chord.Append(note.Output(relativeMode, language, octaveCheck)).Append(' ');
        }

        MidiNote.LastPitch.Note = lastNote;
        MidiNote.LastPitch.Octave = lastOctave;
        return "<" + chord.ToString(0, chord.Length - 1) + ">";
    }
}
