// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/preview.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Fills the empty music stubs of a wizard-built document with example music,
/// so that the preview shows a page of notes rather than a page of rests.
/// </summary>
public static class ScorePreview
{
    private static readonly string[] LyricSyllables = { "ha", "hi", "he", "ho", "hu" };

    /// <summary>Fills in the example music, in place.</summary>
    /// <param name="document">The document the builder made.</param>
    public static void Examplify(Dom.Document document)
    {
        if (document == null) { return; }

        List<(object Global, Dom.LyNode Stub)> stubs =
            new List<(object, Dom.LyNode)>();
        Dictionary<string, (Dom.KeySignature Key, List<(int Duration, int Dots)> Durations)>
            globals = new Dictionary<string, (Dom.KeySignature, List<(int, int)>)>(
                StringComparer.Ordinal);

        foreach (Dom.Assignment assignment in document.FindChildren<Dom.Assignment>(2))
        {
            if (assignment.Name is Dom.Reference)
            {
                //A music stub: the variable it reads is the global section.
                object global = null;
                foreach (Dom.Identifier identifier in
                    assignment.FindChildren<Dom.Identifier>())
                {
                    if (identifier.Name is not Dom.Reference)
                    {
                        global = identifier.Name;
                        break;
                    }
                }

                stubs.Add((global, (Dom.LyNode)assignment[assignment.Count - 1]));
                continue;
            }

            Dom.KeySignature key = assignment.FindChild<Dom.KeySignature>();
            Dom.TimeSignature time = assignment.FindChild<Dom.TimeSignature>();
            Dom.Partial partial = assignment.FindChild<Dom.Partial>();

            //The durations of the example notes: the pickup, then a bar's worth.
            List<(int Duration, int Dots)> durations = new List<(int, int)>();
            if (partial != null) { durations.Add((partial.Dur, partial.Dots)); }

            int duration;
            int count;
            if (time != null)
            {
                duration = (int)(Math.Log(time.Beat) / Math.Log(2));
                count = Math.Min(time.Numerator * 2, 10);
            }
            else
            {
                duration = 2;
                count = 4;
            }

            for (int index = 0; index < count; index++) { durations.Add((duration, 0)); }

            globals[Convert.ToString(assignment.Name, CultureInfo.InvariantCulture)] =
                (key, durations);
        }

        int lyricIndex = 0;
        int syllableCount = 10;
        List<(int Duration, int Dots)> currentDurations = new List<(int, int)>();

        foreach ((object global, Dom.LyNode stub) in stubs)
        {
            Dom.KeySignature key = null;
            string name = global == null
                ? null
                : Convert.ToString(global, CultureInfo.InvariantCulture);
            if (name != null
                && globals.TryGetValue(
                    name,
                    out (Dom.KeySignature Key, List<(int Duration, int Dots)> Durations) found))
            {
                key = found.Key;
                currentDurations = found.Durations;
                syllableCount = currentDurations.Count;
            }

            void AddItems(Dom.Container into, IEnumerator<Dom.LyNode> generator)
            {
                foreach ((int duration, int dots) in currentDurations)
                {
                    generator.MoveNext();
                    Dom.LyNode node = generator.Current;
                    node.Append(new Dom.Duration(duration, dots));
                    into.Append(node);
                }
            }

            switch (stub)
            {
                case Dom.LyricMode lyricMode:
                    lyricMode.Append(new Dom.Text(string.Join(
                        " ",
                        Enumerable.Repeat(
                            LyricSyllables[lyricIndex++ % LyricSyllables.Length],
                            syllableCount))));
                    break;
                case Dom.Relative relative:
                    AddItems(
                        (Dom.Container)relative[relative.Count - 1],
                        PitchGenerator(key).GetEnumerator());
                    break;
                case Dom.ChordMode chordMode:
                    AddItems(chordMode, ChordGenerator(key).GetEnumerator());
                    break;
                case Dom.FigureMode figureMode:
                    AddItems(figureMode, FigureGenerator().GetEnumerator());
                    break;
                case Dom.DrumMode drumMode:
                    AddItems(drumMode, DrumGenerator().GetEnumerator());
                    break;
            }
        }
    }

    /// <summary>Answers an endless run of single-note chords around a key.</summary>
    /// <param name="startPitch">The key signature to walk around.</param>
    /// <returns>The chords.</returns>
    private static IEnumerable<Dom.LyNode> PitchGenerator(Dom.KeySignature startPitch)
    {
        int note = startPitch?.Note ?? 0;
        Fraction alter = startPitch?.Alter ?? Fraction.Zero;
        int[] steps = { note, note, (note + 9) % 7, (note + 8) % 7, note, (note + 11) % 7, note };
        while (true)
        {
            foreach (int step in steps)
            {
                Dom.Chord chord = new Dom.Chord();
                new Dom.Pitch(-1, step, alter, chord);
                yield return chord;
            }
        }
    }

    /// <summary>Answers an endless run of chords, each held for four beats.</summary>
    /// <param name="startPitch">The key signature to walk around.</param>
    /// <returns>The chords and skips.</returns>
    private static IEnumerable<Dom.LyNode> ChordGenerator(Dom.KeySignature startPitch)
    {
        foreach (Dom.LyNode chord in PitchGenerator(startPitch))
        {
            yield return chord;
            for (int index = 0; index < 3; index++)
            {
                yield return new Dom.TextDur("\\skip");
            }
        }
    }

    /// <summary>Answers an endless run of bass figures.</summary>
    /// <returns>The figures and skips.</returns>
    private static IEnumerable<Dom.LyNode> FigureGenerator()
    {
        int[] figures = { 5, 6, 3, 8, 7 };
        while (true)
        {
            foreach (int figure in figures)
            {
                yield return new Dom.TextDur(
                    string.Create(CultureInfo.InvariantCulture, $"<{figure}>"));
                yield return new Dom.TextDur("\\skip");
                yield return new Dom.TextDur("\\skip");
            }
        }
    }

    /// <summary>Answers an endless run of drum notes.</summary>
    /// <returns>The notes.</returns>
    private static IEnumerable<Dom.LyNode> DrumGenerator()
    {
        string[] notes = { "bd", "hh", "sn", "hh" };
        while (true)
        {
            foreach (string note in notes) { yield return new Dom.TextDur(note); }
        }
    }
}
