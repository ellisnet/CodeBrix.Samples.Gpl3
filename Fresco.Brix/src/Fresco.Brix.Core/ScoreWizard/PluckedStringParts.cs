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
using System.Linq;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/plucked_strings.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A fretted instrument, which can be written on a normal staff, as tablature,
/// or as both at once.
/// </summary>
public abstract class TablaturePart : PartType
{
    private string _clef;
    private bool _clefSet;
    private (int Octave, int Note, int Alter)? _transposition;
    private bool _transpositionSet;

    /// <summary>Initializes the part and its settings.</summary>
    protected TablaturePart()
    {
        StaffType = Add(new ChoiceSetting(
            "staffType",
            new[]
            {
                new ChoiceItem(() => I18n.Get("Normal staff")),
                new ChoiceItem(() => I18n.Get("Tablature")),

                //L10N: Both a Normal and a Tablature staff
                new ChoiceItem(() => I18n.Get("Both")),
            })
        {
            Label = () => I18n.Get("Staff type:"),
        });

        if (Tunings.Count > 0) { CreateTuningSettings(); }

        if (MidiInstruments.Count > 0)
        {
            MidiInstrumentSelection = Add(new ChoiceSetting(
                "midiInstrumentSelection",
                MidiInstruments.Select(instrument => new ChoiceItem(instrument)),
                IndexOfMidiInstrument())
            {
                Label = () => I18n.Get("MIDI instrument:"),
            });
        }

        if (Tunings.Count > 0) { TabEnableChanged(); }
    }

    /// <summary>Gets which kind of staff the part is written on.</summary>
    public ChoiceSetting StaffType { get; }

    /// <summary>Gets the tuning, or null when the instrument has no list.</summary>
    public ChoiceSetting Tuning { get; private set; }

    /// <summary>Gets the typed-in tuning, or null.</summary>
    public TextSetting CustomTuning { get; private set; }

    /// <summary>Gets the MIDI instrument choice, or null.</summary>
    public ChoiceSetting MidiInstrumentSelection { get; private set; }

    /// <summary>Gets the octave the music stub starts in.</summary>
    protected virtual int Octave => 0;

    /// <summary>Gets or sets the clef, or null for the default.</summary>
    protected string Clef
    {
        get => _clefSet ? _clef : DefaultClef;
        set
        {
            _clef = value;
            _clefSet = true;
        }
    }

    /// <summary>Gets or sets the sounding pitch of a written <c>c'</c>.</summary>
    protected (int Octave, int Note, int Alter)? Transposition
    {
        get => _transpositionSet ? _transposition : DefaultTransposition;
        set
        {
            _transposition = value;
            _transpositionSet = true;
        }
    }

    /// <summary>Gets the type's own clef.</summary>
    protected virtual string DefaultClef => null;

    /// <summary>Gets the type's own transposition.</summary>
    protected virtual (int Octave, int Note, int Alter)? DefaultTransposition => null;

    /// <summary>Gets the tunings offered, name first, label second.</summary>
    protected virtual IReadOnlyList<(string Name, Func<string> Title)> Tunings
        => Array.Empty<(string, Func<string>)>();

    /// <summary>Gets the <c>tablatureFormat</c> to set, or the empty string.</summary>
    protected virtual string TabFormat => string.Empty;

    /// <summary>Gets the MIDI instrument used when there is no choice.</summary>
    protected virtual string MidiInstrument => string.Empty;

    /// <summary>Gets the MIDI instruments offered, if any.</summary>
    protected virtual IReadOnlyList<string> MidiInstruments => Array.Empty<string>();

    /// <summary>Answers how many voices the part has.</summary>
    /// <returns>The count.</returns>
    /// <remarks>Overridden by the parts that let the user choose.</remarks>
    protected virtual int VoiceCount() => 1;

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        //First the assignments for the voices we want.
        int numVoices = VoiceCount();
        string[] voices;
        int[] order;
        switch (numVoices)
        {
            case 1:
                voices = new[] { LyUtil.MkId(data.Name()) };
                order = new[] { 1 };
                break;
            case 2:
                order = new[] { 1, 2 };
                voices = new[] { "upper", "lower" };
                break;
            case 3:
                order = new[] { 1, 3, 2 };
                voices = new[] { "upper", "middle", "lower" };
                break;
            default:
                order = new[] { 1, 2, 3, 4 };
                voices = order
                    .Select(i => LyUtil.MkId(data.Name(), "voice") + LyUtil.Int2Text(i))
                    .ToArray();
                break;
        }

        Dom.Assignment[] assignments = voices
            .Select(name => data.AssignMusic(name, Octave))
            .ToArray();

        int staffType = StaffType.SelectedIndex;
        Dom.Staff staff = null;
        Dom.TabStaff tabStaff = null;

        if (staffType is 0 or 2)
        {
            staff = new Dom.Staff();
            Dom.Container sequence = new Dom.Seqr(staff);
            if (!string.IsNullOrEmpty(Clef)) { new Dom.Clef(Clef, sequence); }

            if (Transposition != null)
            {
                sequence = builder.SetStaffTransposition(sequence, Transposition.Value);
            }

            Dom.Simr music = new Dom.Simr(sequence);
            foreach (Dom.Assignment assignment in assignments.Take(assignments.Length - 1))
            {
                new Dom.Identifier(assignment.Name, music);
                new Dom.VoiceSeparator(music);
            }

            new Dom.Identifier(assignments[^1].Name, music);
            builder.SetMidiInstrument(
                staff,
                MidiInstrumentSelection != null
                    ? MidiInstrumentSelection.Text
                    : MidiInstrument);
        }

        if (staffType is 1 or 2)
        {
            tabStaff = new Dom.TabStaff();
            if (!string.IsNullOrEmpty(TabFormat))
            {
                tabStaff.GetWith()["tablatureFormat"] = new Dom.Scheme(TabFormat);
            }

            SetTunings(tabStaff);
            builder.SetMidiInstrument(
                tabStaff,
                MidiInstrumentSelection != null
                    ? MidiInstrumentSelection.Text
                    : MidiInstrument);

            Dom.Simr music = new Dom.Simr(tabStaff);
            if (numVoices == 1)
            {
                new Dom.Identifier(assignments[0].Name, music);
            }
            else
            {
                for (int index = 0; index < assignments.Length; index++)
                {
                    Dom.Seq sequence = new Dom.Seq(new Dom.TabVoice(parent: music));
                    new Dom.Text("\\voice" + LyUtil.Int2Text(order[index]), sequence);
                    new Dom.Identifier(assignments[index].Name, sequence);
                }
            }
        }

        Dom.ContextType part;
        if (staffType == 0)
        {
            part = staff;
        }
        else if (staffType == 1)
        {
            part = tabStaff;
        }
        else
        {
            Dom.StaffGroup group = new Dom.StaffGroup();
            Dom.Sim both = new Dom.Sim(group);
            both.Append(staff);
            both.Append(tabStaff);
            group.GetWith()["systemStartDelimiter"] = new Dom.Scheme("'SystemStartSquare");
            part = group;
        }

        builder.SetInstrumentNamesFromPart(part, this, data);
        data.Nodes.Add(part);
    }

    /// <summary>Writes the string tunings into a tablature staff.</summary>
    /// <param name="tab">The staff.</param>
    protected virtual void SetTunings(Dom.ContextType tab)
    {
        if (Tunings.Count == 0) { return; }

        int index = Tuning.SelectedIndex;
        if (index == 0) { return; }

        Dom.LyNode value = index > Tunings.Count
            ? new Dom.Text("\\stringTuning <" + CustomTuning.Value + ">")
            : new Dom.Scheme(Tunings[index - 1].Name);
        tab.GetWith()["stringTunings"] = value;
    }

    /// <summary>Builds the tuning settings.</summary>
    /// <remarks>Overridden by the banjo, which has a string count too.</remarks>
    protected virtual void CreateTuningSettings()
    {
        List<ChoiceItem> tunings = new List<ChoiceItem>
        {
            new ChoiceItem(() => I18n.Get("Default")),
        };
        tunings.AddRange(Tunings.Select(t => new ChoiceItem(t.Title, t.Name)));
        tunings.Add(new ChoiceItem(() => I18n.Get("Custom tuning")));

        Tuning = Add(new ChoiceSetting("tuning", tunings, 1)
        {
            Label = () => I18n.Get("Tuning:"),
        });
        CustomTuning = Add(new TextSetting("customTuning")
        {
            IsEnabled = false,
            PlaceholderText = () => I18n.Get("Custom tuning..."),
            ToolTip = () => I18n.Get(
                "Select custom tuning in the combobox and "
                + "enter a custom tuning here, e.g. <code>e, a d g b e'</code>. "
                + "Use absolute note names in the same language as you want to use "
                + "in your document (by default: \"nederlands\")."),
        });
    }

    /// <inheritdoc/>
    protected override void SettingChanged()
    {
        if (Tunings.Count > 0) { TabEnableChanged(); }
    }

    /// <summary>
    /// Turns the tuning settings on and off: a tuning is only meaningful on a
    /// tablature staff, and a typed-in one only when Custom tuning is chosen.
    /// </summary>
    protected virtual void TabEnableChanged()
    {
        bool tablature = StaffType.SelectedIndex > 0;
        Tuning.IsEnabled = tablature;
        CustomTuning.IsEnabled = tablature && Tuning.SelectedIndex > Tunings.Count;
    }

    /// <summary>Answers which MIDI instrument starts out chosen.</summary>
    /// <returns>The index, or 0.</returns>
    private int IndexOfMidiInstrument()
    {
        for (int index = 0; index < MidiInstruments.Count; index++)
        {
            if (string.Equals(
                MidiInstruments[index], MidiInstrument, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }
}

/// <summary>The mandolin.</summary>
public sealed class Mandolin : TablaturePart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Mandolin");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Mandolin", "Mdl.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "acoustic guitar (steel)";

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, Func<string> Title)> Tunings => new[]
    {
        ("mandolin-tuning", (Func<string>)(() => I18n.Get("Mandolin tuning"))),
    };
}

/// <summary>The ukulele.</summary>
public sealed class Ukulele : TablaturePart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Ukulele");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Ukulele", "Uk.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "acoustic guitar (nylon)";

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, Func<string> Title)> Tunings => new[]
    {
        ("ukulele-tuning", (Func<string>)(() => I18n.Get("Ukulele tuning"))),
        ("ukulele-d-tuning", () => I18n.Get("Ukulele D-tuning")),
        ("tenor-ukulele-tuning", () => I18n.Get("Tenor Ukulele tuning")),
        ("baritone-ukulele-tuning", () => I18n.Get("Baritone Ukulele tuning")),
    };
}

/// <summary>The banjo, which may have four strings instead of five.</summary>
public sealed class Banjo : TablaturePart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Banjo");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Banjo", "Bj.");

    /// <summary>Gets whether the banjo has four strings rather than five.</summary>
    public BoolSetting FourStrings { get; private set; }

    /// <inheritdoc/>
    protected override string MidiInstrument => "banjo";

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? DefaultTransposition
        => (-1, 0, 0);

    /// <inheritdoc/>
    protected override string TabFormat => "fret-number-tablature-format-banjo";

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, Func<string> Title)> Tunings => new[]
    {
        ("banjo-open-g-tuning", (Func<string>)(() => I18n.Get("Open G-tuning (aDGBD)"))),
        ("banjo-c-tuning", () => I18n.Get("C-tuning (gCGBD)")),
        ("banjo-modal-tuning", () => I18n.Get("Modal tuning (gDGCD)")),
        ("banjo-open-d-tuning", () => I18n.Get("Open D-tuning (aDF#AD)")),
        ("banjo-open-dm-tuning", () => I18n.Get("Open Dm-tuning (aDFAD)")),
    };

    /// <inheritdoc/>
    protected override void CreateTuningSettings()
    {
        base.CreateTuningSettings();
        FourStrings = Add(new BoolSetting("fourStrings")
        {
            Label = () => I18n.Get("Four strings (instead of five)"),
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14).
    /// <para>
    /// Upstream is <c>self.tunings[i]</c>, indexed by the COMBO's index — but
    /// the combo's first row is "Default", so everywhere else in this class the
    /// list is indexed <c>[i - 1]</c>. The consequence in Frescobaldi 4.0.7 is
    /// that a four-string banjo is tuned one row FURTHER DOWN the list than the
    /// tuning the user picked (choose "C-tuning (gCGBD)" and the document says
    /// <c>banjo-modal-tuning</c>), and that picking "Default" writes a tuning
    /// where the same method's own rule for "Default" is to write none.
    /// </para>
    /// <para>
    /// This writes the tuning the user actually picked, and lets "Default" mean
    /// what it means on every other row: nothing written, so the engine's own
    /// default applies. The parity fixture keeps upstream's answer — the oracle
    /// goes on telling the truth about Frescobaldi — and the divergence is
    /// declared in the parity test's known-divergence table.
    /// </para>
    /// </remarks>
    protected override void SetTunings(Dom.ContextType tab)
    {
        int index = Tuning.SelectedIndex;
        if (index <= 0 || index > Tunings.Count || !FourStrings.Value)
        {
            base.SetTunings(tab);
            return;
        }

        tab.GetWith()["stringTunings"] = new Dom.Scheme(
            "(four-string-banjo " + Tunings[index - 1].Name + ")");
    }

    /// <inheritdoc/>
    protected override void TabEnableChanged()
    {
        base.TabEnableChanged();
        FourStrings.IsEnabled = Tuning.SelectedIndex <= Tunings.Count;
    }
}

/// <summary>The classical guitar.</summary>
public class Guitar : TablaturePart
{
    /// <summary>Initializes the part and its extra settings.</summary>
    public Guitar()
    {
        Voices = Add(new NumberSetting("voices", 1, 4, 1)
        {
            Label = () => I18n.Get("Voices:"),
        });

        //The octave clef is the classical guitar's convention, and less common
        //in the styles the derived types are meant for.
        OctaveClef = Add(new BoolSetting("octaveClef", GetType() == typeof(Guitar))
        {
            Label = () => I18n.Get("Include octave indication"),
            ToolTip = () => I18n.Get(
                "Use an octave (treble_8) clef to make transposition explicit "
                + "as preferred by some style guides."),
        });
    }

    /// <summary>Gets how many voices the part has.</summary>
    public NumberSetting Voices { get; }

    /// <summary>Gets whether the staff carries an octave clef.</summary>
    public BoolSetting OctaveClef { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Guitar");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Guitar", "Gt.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "acoustic guitar (nylon)";

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? DefaultTransposition
        => (-1, 0, 0);   //but see Build()

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, Func<string> Title)> Tunings => new[]
    {
        ("guitar-tuning", (Func<string>)(() => I18n.Get("Guitar tuning"))),
        ("guitar-seven-string-tuning", () => I18n.Get("Guitar seven-string tuning")),
        ("guitar-drop-d-tuning", () => I18n.Get("Guitar drop-D tuning")),
        ("guitar-drop-c-tuning", () => I18n.Get("Guitar drop-C tuning")),
        ("guitar-open-g-tuning", () => I18n.Get("Open G-tuning")),
        ("guitar-open-d-tuning", () => I18n.Get("Guitar open D tuning")),
        ("guitar-dadgad-tuning", () => I18n.Get("Guitar d-a-d-g-a-d tuning")),
        ("guitar-lute-tuning", () => I18n.Get("Lute tuning")),
        ("guitar-asus4-tuning", () => I18n.Get("Guitar A-sus4 tuning")),
    };

    /// <inheritdoc/>
    protected override int VoiceCount() => Voices.Value;

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        if (OctaveClef.Value)
        {
            Clef = "treble_8";
            Transposition = null;
        }
        else
        {
            Clef = null;
            Transposition = (-1, 0, 0);
        }

        base.Build(data, builder);
    }
}

/// <summary>The steel-string acoustic guitar.</summary>
public sealed class AcousticGuitar : Guitar
{
    /// <inheritdoc/>
    public override string Title(Translator translate)
        => translate(null, "Acoustic guitar");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Acoustic guitar", "A.Gt.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "acoustic guitar (steel)";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "acoustic guitar (nylon)",
        "acoustic guitar (steel)",
    };
}

/// <summary>The electric guitar.</summary>
public sealed class ElectricGuitar : Guitar
{
    /// <inheritdoc/>
    public override string Title(Translator translate)
        => translate(null, "Electric guitar");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Electric guitar", "E.Gt.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "electric guitar (clean)";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "electric guitar (jazz)",
        "electric guitar (clean)",
        "electric guitar (muted)",
        "overdriven guitar",
        "distorted guitar",
    };
}

/// <summary>The acoustic bass guitar.</summary>
public class AcousticBass : TablaturePart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Acoustic bass");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Acoustic bass", "A.Bs.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "acoustic bass";

    /// <inheritdoc/>
    protected override string DefaultClef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? DefaultTransposition
        => (-1, 0, 0);

    /// <inheritdoc/>
    protected override IReadOnlyList<(string Name, Func<string> Title)> Tunings => new[]
    {
        ("bass-tuning", (Func<string>)(() => I18n.Get("Bass tuning"))),
        ("bass-four-string-tuning", () => I18n.Get("Four-string bass tuning")),
        ("bass-drop-d-tuning", () => I18n.Get("Bass drop-D tuning")),
        ("bass-five-string-tuning", () => I18n.Get("Five-string bass tuning")),
        ("bass-six-string-tuning", () => I18n.Get("Six-string bass tuning")),
    };
}

/// <summary>The electric bass guitar.</summary>
public sealed class ElectricBass : AcousticBass
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Electric bass");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Electric bass", "E.Bs.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "electric bass (finger)";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "electric bass (finger)",
        "electric bass (pick)",
        "fretless bass",
        "slap bass 1",
        "slap bass 2",
    };
}

/// <summary>The harp: two staves, neither of them optional.</summary>
public sealed class Harp : PianoStaffPart
{
    /// <summary>Initializes the part and re-words its voice counts.</summary>
    public Harp()
    {
        UpperVoices.Label = () => I18n.Get("Upper staff:");
        LowerVoices.Label = () => I18n.Get("Lower staff:");
    }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Harp");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Harp", "Hp.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "orchestral harp";

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        Dom.PianoStaff piano = new Dom.PianoStaff();
        builder.SetInstrumentNamesFromPart(piano, this, data);
        Dom.Sim staves = new Dom.Sim(piano);
        BuildStaff(data, builder, "upper", 1, UpperVoices.Value, staves);
        BuildStaff(data, builder, "lower", 0, LowerVoices.Value, staves, "bass");
        data.Nodes.Add(piano);
    }
}
