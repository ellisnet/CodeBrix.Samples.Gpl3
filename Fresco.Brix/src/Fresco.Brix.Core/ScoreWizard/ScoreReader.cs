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
using Music = Fresco.Brix.Ly.Music;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/dialog.py's readScore()

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Fills a wizard in from a document that is already open, so that a score the
/// wizard once wrote can be taken up again.
/// </summary>
/// <remarks>
/// What can be read back is what the wizard itself writes: the header block,
/// the <c>global</c> section's key, time, pickup and tempo, and the part
/// assignments — <c>violinPart = …</c> names a Violin. Anything else in the
/// document is left alone, because the wizard does not describe it.
/// </remarks>
public static class ScoreReader
{
    /// <summary>Reads a document into a wizard.</summary>
    /// <param name="model">The wizard to fill in; it is emptied first.</param>
    /// <param name="text">The document's text.</param>
    public static void Read(ScoreWizardModel model, string text)
    {
        if (model == null) { return; }

        model.ClearHeaders();
        model.Root.Clear();

        if (string.IsNullOrEmpty(text)) { return; }

        Music.Document music = Music.MusicReader.ReadDocument(new Document(text));
        foreach (Music.Item item in music.Cast<Music.Item>())
        {
            switch (item)
            {
                case Music.Header header:
                    ReadHeader(model, header);
                    break;
                case Music.Assignment assignment:
                    ReadAssignment(model, assignment);
                    break;
            }
        }
    }

    /// <summary>Reads a header block into the wizard's title fields.</summary>
    /// <param name="model">The wizard.</param>
    /// <param name="header">The block.</param>
    private static void ReadHeader(ScoreWizardModel model, Music.Header header)
    {
        HashSet<string> known = new HashSet<string>(
            ScoreWizardModel.HeaderFields.Select(field => field.Name),
            StringComparer.Ordinal);

        foreach (Music.Assignment entry in header.OfType<Music.Assignment>())
        {
            string name = entry.Name();
            if (name == null || !known.Contains(name)) { continue; }

            model.SetHeader(name, entry.Value()?.PlainText() ?? string.Empty);
        }
    }

    /// <summary>Reads one top-level assignment.</summary>
    /// <param name="model">The wizard.</param>
    /// <param name="assignment">The assignment.</param>
    private static void ReadAssignment(
        ScoreWizardModel model, Music.Assignment assignment)
    {
        string name = assignment.Name();
        if (name == null) { return; }

        if (string.Equals(name, "global", StringComparison.Ordinal))
        {
            ReadGlobalSection(model.ScoreProperties, assignment);
            return;
        }

        if (name.EndsWith("Part", StringComparison.Ordinal))
        {
            ReadPart(model, name.Substring(0, name.Length - "Part".Length));
        }
    }

    /// <summary>Reads the key, time, pickup and tempo out of a global section.</summary>
    /// <param name="properties">The properties to fill in.</param>
    /// <param name="assignment">The assignment holding the section.</param>
    private static void ReadGlobalSection(
        ScoreProperties properties, Music.Assignment assignment)
    {
        Music.Item value = assignment.Value();
        if (value == null) { return; }

        foreach (Music.Item item in value.Cast<Music.Item>())
        {
            switch (item)
            {
                case Music.KeySignature key:
                    ReadKeySignature(properties, key);
                    break;
                case Music.Partial partial:
                    ReadPickup(properties, partial.PartialLength());
                    break;
                case Music.Tempo tempo:
                    ReadTempo(properties, tempo);
                    break;
                case Music.TimeSignature time:
                    //The fraction's numerator is always 1, so the printed upper
                    //number comes from the item itself.
                    properties.TimeSignature.SetText(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{time.Numerator()}/{time.Fraction().Denominator}"));
                    break;
            }
        }
    }

    /// <summary>Picks the key the signature names.</summary>
    /// <param name="properties">The properties.</param>
    /// <param name="key">The signature.</param>
    private static void ReadKeySignature(
        ScoreProperties properties, Music.KeySignature key)
    {
        Ly.Pitching.Pitch pitch = key.Pitch();
        if (pitch != null)
        {
            int index = -1;
            for (int candidate = 0; candidate < ScoreProperties.Keys.Count; candidate++)
            {
                (int note, int alter) = ScoreProperties.Keys[candidate];
                if (note == pitch.Note && alter == pitch.Alter.Numerator)
                {
                    index = candidate;
                    break;
                }
            }

            if (index >= 0) { properties.KeyNote.SelectedIndex = index; }
        }

        string mode = key.Mode();
        for (int index = 0; index < ScoreProperties.Modes.Count; index++)
        {
            if (string.Equals(
                ScoreProperties.Modes[index].Name, mode, StringComparison.Ordinal))
            {
                properties.KeyMode.SelectedIndex = index;
                return;
            }
        }
    }

    /// <summary>Picks the pickup measure a length names.</summary>
    /// <param name="properties">The properties.</param>
    /// <param name="length">The length.</param>
    private static void ReadPickup(ScoreProperties properties, Fraction length)
    {
        int index = IndexOfDuration(length);

        //Index 0 of the list is "None", so the durations start one further on.
        if (index >= 0) { properties.Pickup.SelectedIndex = index + 1; }
    }

    /// <summary>Reads the metronome mark and the tempo text.</summary>
    /// <param name="properties">The properties.</param>
    /// <param name="tempo">The tempo item.</param>
    private static void ReadTempo(ScoreProperties properties, Music.Tempo tempo)
    {
        int index = IndexOfDuration(tempo.FractionValue());
        if (index >= 0) { properties.MetronomeNote.SelectedIndex = index; }

        IReadOnlyList<int> values = tempo.TempoValues();
        if (values.Count > 0)
        {
            properties.MetronomeValue.SetText(
                values[0].ToString(CultureInfo.InvariantCulture));
        }

        Music.Item text = tempo.Text();
        if (text != null) { properties.Tempo.Value = text.PlainText(); }
    }

    /// <summary>Answers which of the wizard's durations a length is.</summary>
    /// <param name="length">The length.</param>
    /// <returns>The index, or -1.</returns>
    /// <remarks>Upstream compares the length's denominator and numerator with
    /// its MIDI duration table, which is the same list read the other way
    /// round.</remarks>
    private static int IndexOfDuration(Fraction length)
    {
        if (length.Numerator == 0) { return -1; }

        for (int index = 0; index < ScoreProperties.MidiDurations.Count; index++)
        {
            (int denominator, int numerator) = ScoreProperties.MidiDurations[index];
            if (length.Denominator == denominator && length.Numerator == numerator)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Adds the part an assignment's name stands for.</summary>
    /// <param name="model">The wizard.</param>
    /// <param name="identifier">The name without its <c>Part</c> ending.</param>
    private static void ReadPart(ScoreWizardModel model, string identifier)
    {
        foreach (PartEntry entry in PartRegistry.AllParts())
        {
            if (string.Equals(
                LyUtil.MkId(entry.Name), identifier, StringComparison.Ordinal))
            {
                model.Root.Add(entry.Create());
                return;
            }
        }
    }
}
