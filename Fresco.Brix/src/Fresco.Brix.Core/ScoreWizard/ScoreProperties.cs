// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/scoreproperties.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The properties a score has whatever is playing it: key signature, time
/// signature, pickup measure, metronome mark and tempo indication.
/// </summary>
/// <remarks>
/// Upstream this is a mixin stirred into two different widgets — the wizard's
/// Score settings page and the Score container part, which may carry its own.
/// Here it is a plain object that both of those own one of, which is what lets
/// the whole thing be read and written without a window.
/// </remarks>
public sealed class ScoreProperties
{
    private string _pitchLanguage = "nederlands";

    /// <summary>Initializes the properties with upstream's defaults.</summary>
    public ScoreProperties()
    {
        KeyNote = new ChoiceSetting(
            "keyNote",
            Enumerable.Range(0, Keys.Count).Select(
                index => new ChoiceItem(() => KeyNameAt(index))))
        {
            Label = () => I18n.Get("Key signature:"),
        };
        KeyMode = new ChoiceSetting(
            "keyMode",
            Modes.Select(mode => new ChoiceItem(mode.Title)));
        TimeSignature = new ChoiceSetting(
            "timeSignature",
            TimeSignaturePresets.Select(preset => new ChoiceItem(preset)),
            isEditable: true)
        {
            Label = () => I18n.Get("Time signature:"),
        };

        List<ChoiceItem> pickups = new List<ChoiceItem>
        {
            new ChoiceItem(() => I18n.Get("None"), string.Empty),
        };
        pickups.AddRange(Durations.Select(d => new ChoiceItem(d)));
        Pickup = new ChoiceSetting("pickup", pickups)
        {
            Label = () => I18n.Get("Pickup measure:"),
        };

        MetronomeNote = new ChoiceSetting(
            "metronomeNote",
            Durations.Select(d => new ChoiceItem(d)),
            Durations.ToList().IndexOf("4"))
        {
            Label = () => I18n.Get("Metronome mark:"),
        };
        MetronomeValue = new ChoiceSetting(
            "metronomeValue",
            MetronomeValues.Select(
                value => new ChoiceItem(
                    value.ToString(CultureInfo.InvariantCulture))),
            MetronomeValues.ToList().IndexOf(100),
            isEditable: true);
        MetronomeRound = new BoolSetting("metronomeRound", true)
        {
            Label = () => I18n.Get("Round tap tempo value"),
            ToolTip = () => I18n.Get(
                "Round the entered tap tempo to a common value."),
        };
        Tempo = new TextSetting("tempo")
        {
            Label = () => I18n.Get("Tempo indication:"),
        };
    }

    /// <summary>Gets the key signature's note.</summary>
    public ChoiceSetting KeyNote { get; }

    /// <summary>Gets the key signature's mode.</summary>
    public ChoiceSetting KeyMode { get; }

    /// <summary>Gets the time signature.</summary>
    public ChoiceSetting TimeSignature { get; }

    /// <summary>Gets the pickup measure's length.</summary>
    public ChoiceSetting Pickup { get; }

    /// <summary>Gets the note value the metronome mark counts.</summary>
    public ChoiceSetting MetronomeNote { get; }

    /// <summary>Gets how many of those there are per minute.</summary>
    public ChoiceSetting MetronomeValue { get; }

    /// <summary>Gets whether a tapped tempo is rounded to a common value.</summary>
    public BoolSetting MetronomeRound { get; }

    /// <summary>Gets the tempo indication text.</summary>
    public TextSetting Tempo { get; }

    /// <summary>Gets the settings in the order they are shown.</summary>
    public IReadOnlyList<PartSetting> Settings => new PartSetting[]
    {
        KeyNote, KeyMode, TimeSignature, Pickup,
        MetronomeNote, MetronomeValue, MetronomeRound, Tempo,
    };

    /// <summary>Gets or sets the language the key names are shown in.</summary>
    /// <remarks>Only the NAMES change: the chosen key keeps its index, so
    /// switching language re-labels the list rather than re-picking in it.</remarks>
    public string PitchLanguage
    {
        get => _pitchLanguage;
        set => _pitchLanguage = string.IsNullOrEmpty(value) ? "nederlands" : value;
    }

    /// <summary>Sets a tapped tempo, rounding it when the user asked for that.</summary>
    /// <param name="beatsPerMinute">The tapped tempo.</param>
    public void SetMetronomeValue(int beatsPerMinute)
    {
        if (MetronomeRound.Value)
        {
            int nearest = 0;
            int distance = int.MaxValue;
            for (int index = 0; index < MetronomeValues.Count; index++)
            {
                int candidate = Math.Abs(MetronomeValues[index] - beatsPerMinute);
                if (candidate < distance)
                {
                    distance = candidate;
                    nearest = index;
                }
            }

            if (distance < 6) { MetronomeValue.SelectedIndex = nearest; }
            return;
        }

        MetronomeValue.SetText(beatsPerMinute.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Adds the key, time, pickup and tempo commands to a node.</summary>
    /// <param name="node">The node to add to.</param>
    /// <param name="builder">The builder, read for the settings that decide
    /// what is written.</param>
    public void Ly(Dom.Container node, ScoreBuilder builder)
    {
        LyKeySignature(node);
        LyTimeSignature(node, builder);
        LyPickup(node);
        LyTempo(node, builder);
    }

    /// <summary>Answers a <c>{ }</c> holding what <see cref="Ly"/> writes.</summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The expression.</returns>
    public Dom.Seq GlobalSection(ScoreBuilder builder)
    {
        Dom.Seq sequence = new Dom.Seq();
        Ly(sequence, builder);
        return sequence;
    }

    /// <summary>Adds the key signature.</summary>
    /// <param name="node">The node to add to.</param>
    public void LyKeySignature(Dom.Container node)
    {
        (int Note, int Alter) key = Keys[Math.Max(0, KeyNote.SelectedIndex)];
        string mode = Modes[Math.Max(0, KeyMode.SelectedIndex)].Name;
        new Dom.KeySignature(
            key.Note, new Fraction(key.Alter, 2), mode, node)
        {
            After = 1,
        };
    }

    /// <summary>Adds the time signature.</summary>
    /// <param name="node">The node to add to.</param>
    /// <param name="builder">The builder, asked which spelling its version wants.</param>
    public void LyTimeSignature(Dom.Container node, ScoreBuilder builder)
    {
        string signature = (TimeSignature.Text ?? string.Empty).Trim();
        if (signature.Contains('+'))
        {
            //TODO: implement support for \compoundMeter — upstream's own note.
            return;
        }

        if (string.Equals(signature, "(2/2)", StringComparison.Ordinal))
        {
            new Dom.TimeSignature(2, 2, node) { After = 1 };
            return;
        }

        if (string.Equals(signature, "(4/4)", StringComparison.Ordinal))
        {
            new Dom.TimeSignature(4, 4, node) { After = 1 };
            return;
        }

        Match match = Regex.Match(signature, @"(\d+).*?(\d+)");
        if (!match.Success) { return; }

        if (builder != null && builder.LyVersionAtLeast(2, 11, 44))
        {
            new Dom.Line("\\numericTimeSignature", node);
        }
        else
        {
            new Dom.Line("\\override Staff.TimeSignature.style = #'()", node);
        }

        int numerator = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int beat = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        new Dom.TimeSignature(numerator, beat, node) { After = 1 };
    }

    /// <summary>Adds the pickup measure, when there is one.</summary>
    /// <param name="node">The node to add to.</param>
    public void LyPickup(Dom.Container node)
    {
        if (Pickup.SelectedIndex <= 0) { return; }

        (int Duration, int Dots) partial = PartialDurations[Pickup.SelectedIndex - 1];
        new Dom.Partial(partial.Duration, partial.Dots, parent: node);
    }

    /// <summary>Adds the tempo indication, when there is one.</summary>
    /// <param name="node">The node to add to.</param>
    /// <param name="builder">The builder, asked whether the mark is shown.</param>
    public void LyTempo(Dom.Container node, ScoreBuilder builder)
    {
        string text = (Tempo.Value ?? string.Empty).Trim();
        string duration = null;
        string value = null;
        if (builder != null && builder.ShowMetronomeMark)
        {
            duration = Durations[Math.Max(0, MetronomeNote.SelectedIndex)];
            value = MetronomeValueText();
        }
        else if (text.Length == 0)
        {
            return;
        }

        Dom.Tempo tempo = new Dom.Tempo(duration, value, node);
        if (text.Length > 0) { new Dom.QuotedString(text, tempo); }
    }

    /// <summary>Writes the tempo into a <c>\midi</c> block's variable.</summary>
    /// <param name="node">The block.</param>
    public void LyMidiTempo(Dom.VariableSection node)
        => node["tempoWholesPerMinute"] = new Dom.Scheme(SchemeMidiTempo());

    /// <summary>Answers the tempo as a scheme moment.</summary>
    /// <returns>Text such as <c>(ly:make-moment 100 4)</c>.</returns>
    public string SchemeMidiTempo()
    {
        (int Base, int Multiplier) duration =
            MidiDurations[Math.Max(0, MetronomeNote.SelectedIndex)];
        int value = MetronomeValueNumber() * duration.Multiplier;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"(ly:make-moment {value} {duration.Base})");
    }

    /// <summary>Adds a plain <c>\tempo x=y</c> for the current setting.</summary>
    /// <param name="node">The node to add to.</param>
    /// <returns>The tempo node.</returns>
    public Dom.Tempo LySimpleMidiTempo(Dom.Container node)
        => new Dom.Tempo(
            Durations[Math.Max(0, MetronomeNote.SelectedIndex)],
            MetronomeValueText(),
            node);

    /// <summary>Answers the metronome value as the user typed or picked it.</summary>
    /// <returns>The text, or <c>60</c> when it is empty.</returns>
    private string MetronomeValueText()
    {
        string text = MetronomeValue.Text;
        return string.IsNullOrEmpty(text) ? "60" : text;
    }

    /// <summary>Answers the metronome value as a number.</summary>
    /// <returns>The number, or 60 when it does not read as one.</returns>
    private int MetronomeValueNumber()
        => int.TryParse(
            MetronomeValueText(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
            ? value
            : 60;

    /// <summary>Names a key in the current pitch language.</summary>
    /// <param name="index">The key's index.</param>
    /// <returns>The name.</returns>
    private string KeyNameAt(int index)
    {
        IReadOnlyList<string> names = KeyNamesFor(_pitchLanguage);
        return index >= 0 && index < names.Count ? names[index] : string.Empty;
    }

    // ------------------------------------------------------------------ data

    /// <summary>Gets the key names of one pitch language.</summary>
    /// <param name="language">The language, empty for the default.</param>
    /// <returns>The seventeen names.</returns>
    public static IReadOnlyList<string> KeyNamesFor(string language)
        => KeyNames.TryGetValue(
            string.IsNullOrEmpty(language) ? "nederlands" : language,
            out IReadOnlyList<string> names)
            ? names
            : KeyNames["nederlands"];

    /// <summary>Gets the pitch languages there are key names for.</summary>
    public static IReadOnlyList<string> PitchLanguages { get; } =
        new[]
        {
            "catalan", "deutsch", "english", "espanol", "italiano",
            "nederlands", "norsk", "portugues", "suomi", "svenska", "vlaams",
        };

    /// <summary>Gets the key names, by pitch language.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> KeyNames { get; } =
        BuildKeyNames();

    /// <summary>Gets the note and alteration each key stands for.</summary>
    public static IReadOnlyList<(int Note, int Alter)> Keys { get; } = new[]
    {
        (0, 0), (0, 1),
        (1, -1), (1, 0), (1, 1),
        (2, -1), (2, 0),
        (3, 0), (3, 1),
        (4, -1), (4, 0), (4, 1),
        (5, -1), (5, 0), (5, 1),
        (6, -1), (6, 0),
    };

    /// <summary>Gets the modes a key signature may be in.</summary>
    public static IReadOnlyList<(string Name, Func<string> Title)> Modes { get; } = new (string, Func<string>)[]
    {
        ("major", () => I18n.Get("Major")),
        ("minor", () => I18n.Get("Minor")),
        ("ionian", () => I18n.Get("Ionian")),
        ("dorian", () => I18n.Get("Dorian")),
        ("phrygian", () => I18n.Get("Phrygian")),
        ("lydian", () => I18n.Get("Lydian")),
        ("mixolydian", () => I18n.Get("Mixolydian")),
        ("aeolian", () => I18n.Get("Aeolian")),
        ("locrian", () => I18n.Get("Locrian")),
    };

    /// <summary>Gets the time signatures offered in the list.</summary>
    public static IReadOnlyList<string> TimeSignaturePresets { get; } = new[]
    {
        "(4/4)", "(2/2)",
        "2/4", "3/4", "4/4", "5/4", "6/4", "7/4",
        "2/2", "3/2", "4/2",
        "3/8", "5/8", "6/8", "7/8", "8/8", "9/8", "12/8",
        "3/16", "6/16", "12/16",
        "3+2/8", "3/4+3/8",
    };

    /// <summary>Gets the durations a pickup or a metronome mark may have.</summary>
    public static IReadOnlyList<string> Durations { get; } = new[]
    {
        "16", "16.", "8", "8.", "4", "4.", "2", "2.", "1", "1.",
    };

    /// <summary>Gets the same durations as MIDI moment fractions.</summary>
    public static IReadOnlyList<(int Base, int Multiplier)> MidiDurations { get; } = new[]
    {
        (16, 1), (32, 3), (8, 1), (16, 3), (4, 1),
        (8, 3), (2, 1), (4, 3), (1, 1), (2, 3),
    };

    /// <summary>Gets the same durations as <c>\partial</c> arguments.</summary>
    public static IReadOnlyList<(int Duration, int Dots)> PartialDurations { get; } = new[]
    {
        (4, 0), (4, 1), (3, 0), (3, 1), (2, 0),
        (2, 1), (1, 0), (1, 1), (0, 0), (0, 1),
    };

    /// <summary>Gets the metronome values the list offers.</summary>
    /// <remarks>Upstream's own steps: every 2 up to 60, then 3, 4, 6 and 8 as
    /// the numbers get bigger and the difference matters less.</remarks>
    public static IReadOnlyList<int> MetronomeValues { get; } = BuildMetronomeValues();

    /// <summary>Builds the metronome value list.</summary>
    /// <returns>The values.</returns>
    private static IReadOnlyList<int> BuildMetronomeValues()
    {
        List<int> values = new List<int>();
        int start = 40;
        foreach ((int end, int step) in new[] { (60, 2), (72, 3), (120, 4), (144, 6), (210, 8) })
        {
            for (int value = start; value < end; value += step) { values.Add(value); }

            start = end;
        }

        return values;
    }

    /// <summary>Builds the key-name table.</summary>
    /// <returns>The table.</returns>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildKeyNames()
    {
        Dictionary<string, IReadOnlyList<string>> names =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["nederlands"] = new[]
                {
                    "C", "Cis",
                    "Des", "D", "Dis",
                    "Es", "E",
                    "F", "Fis",
                    "Ges", "G", "Gis",
                    "As", "A", "Ais",
                    "Bes", "B",
                },
                ["english"] = new[]
                {
                    "C", "C#",
                    "Db", "D", "D#",
                    "Eb", "E",
                    "F", "F#",
                    "Gb", "G", "G#",
                    "Ab", "A", "A#",
                    "Bb", "B",
                },
                ["deutsch"] = new[]
                {
                    "C", "Cis",
                    "Des", "D", "Dis",
                    "Es", "E",
                    "F", "Fis",
                    "Ges", "G", "Gis",
                    "As", "A", "Ais",
                    "B", "H",
                },
                ["norsk"] = new[]
                {
                    "C", "Ciss",
                    "Dess", "D", "Diss",
                    "Ess", "E",
                    "F", "Fiss",
                    "Gess", "G", "Giss",
                    "Ass", "A", "Aiss",
                    "B", "H",
                },
                ["italiano"] = new[]
                {
                    "Do", "Do diesis",
                    "Re bemolle", "Re", "Re diesis",
                    "Mi bemolle", "Mi",
                    "Fa", "Fa diesis",
                    "Sol bemolle", "Sol", "Sol diesis",
                    "La bemolle", "La", "La diesis",
                    "Si bemolle", "Si",
                },
                ["espanol"] = new[]
                {
                    "Do", "Do sostenido",
                    "Re bemol", "Re", "Re sostenido",
                    "Mi bemol", "Mi",
                    "Fa", "Fa sostenido",
                    "Sol bemol", "Sol", "Sol sostenido",
                    "La bemol", "La", "La sostenido",
                    "Si bemol", "Si",
                },
                ["vlaams"] = new[]
                {
                    "Do", "Do kruis",
                    "Re mol", "Re", "Re kruis",
                    "Mi mol", "Mi",
                    "Fa", "Fa kruis",
                    "Sol mol", "Sol", "Sol kruis",
                    "La mol", "La", "La kruis",
                    "Si mol", "Si",
                },
            };

        names["svenska"] = names["norsk"];
        names["suomi"] = names["deutsch"];
        names["catalan"] = names["italiano"];
        names["portugues"] = names["espanol"];
        return names;
    }
}
