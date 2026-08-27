// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System.Collections.Generic;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/keyboard.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>A keyboard instrument.</summary>
public abstract class KeyboardPart : PianoStaffPart
{
}

/// <summary>The piano.</summary>
public sealed class Piano : KeyboardPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Piano");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Piano", "Pno.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "acoustic grand";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "acoustic grand",
        "bright acoustic",
        "electric grand",
        "honky-tonk",
    };
}

/// <summary>The electric piano.</summary>
public sealed class ElectricPiano : KeyboardPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate)
        => translate(null, "Electric piano");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Electric piano", "E.Pno.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "electric piano 1";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "electric piano 1",
        "electric piano 2",
    };
}

/// <summary>The harpsichord.</summary>
public sealed class Harpsichord : KeyboardPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Harpsichord");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Harpsichord", "Hs.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "harpsichord";
}

/// <summary>The clavichord.</summary>
public sealed class Clavichord : KeyboardPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Clavichord");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Clavichord", "Clv.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "clav";
}

/// <summary>The organ: two manual staves and a pedal staff.</summary>
public sealed class Organ : KeyboardPart
{
    /// <summary>Initializes the part and its pedal setting.</summary>
    public Organ()
        => PedalVoices = AddAfter(LowerVoices, new NumberSetting("pedalVoices", 0, 4, 1)
        {
            Label = () => I18n.Get("Pedal:"),
            ToolTip = () => I18n.Get("Set to 0 to disable the pedal altogether."),
        });

    /// <summary>Gets how many voices the pedal staff has, 0 for none.</summary>
    public NumberSetting PedalVoices { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Organ");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Organ", "Org.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "church organ";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "drawbar organ",
        "percussive organ",
        "rock organ",
        "church organ",
        "reed organ",
    };

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        base.Build(data, builder);
        if (PedalVoices.Value > 0)
        {
            data.Nodes.Add(BuildStaff(
                data, builder, "pedal", -1, PedalVoices.Value, clef: "bass"));
        }
    }
}

/// <summary>The celesta.</summary>
public sealed class Celesta : KeyboardPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Celesta");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Celesta", "Cel.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "celesta";

    /// <inheritdoc/>
    protected override int Octave => 2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (1, 0, 0);
}

/// <summary>
/// A synthesizer part: like a piano staff, except that either staff may be
/// turned off entirely, leaving one staff for a monophonic line.
/// </summary>
public abstract class SynthPart : KeyboardPart
{
    /// <summary>Initializes the part and re-words its voice-count tooltips.</summary>
    protected SynthPart()
    {
        UpperVoices.ToolTip = () => I18n.Get(
            "Set to 0 to disable the right-hand part altogether.");
        LowerVoices.ToolTip = () => I18n.Get(
            "Set to 0 to disable the left-hand part altogether.");
    }

    /// <summary>Gets every synth instrument the family offers.</summary>
    /// <remarks>A derived type narrows this to the ones that suit it.</remarks>
    protected override IReadOnlyList<string> MidiInstruments => AllSynthInstruments;

    /// <inheritdoc/>
    protected override int MinUpperVoices => 0;

    /// <inheritdoc/>
    protected override int MinLowerVoices => 0;

    /// <summary>The whole synth family, as upstream lists it.</summary>
    private static readonly IReadOnlyList<string> AllSynthInstruments = new[]
    {
        "synth bass 1",
        "synth bass 2",
        "synthstrings 1",
        "synthstrings 2",
        "synth voice",
        "synthbrass 1",
        "synthbrass 2",
        "lead 1 (square)",
        "lead 2 (sawtooth)",
        "lead 3 (calliope)",
        "lead 4 (chiff)",
        "lead 5 (charang)",
        "lead 6 (voice)",
        "lead 7 (fifths)",
        "lead 8 (bass+lead)",
        "pad 1 (new age)",
        "pad 2 (warm)",
        "pad 3 (polysynth)",
        "pad 4 (choir)",
        "pad 5 (bowed)",
        "pad 6 (metallic)",
        "pad 7 (halo)",
        "pad 8 (sweep)",
        "fx 1 (rain)",
        "fx 2 (soundtrack)",
        "fx 3 (crystal)",
        "fx 4 (atmosphere)",
        "fx 5 (brightness)",
        "fx 6 (goblins)",
        "fx 7 (echoes)",
        "fx 8 (sci-fi)",
    };
}

/// <summary>A synth lead: one treble line by default.</summary>
public sealed class SynthLead : SynthPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Synth lead");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Synth lead", "Syn.Ld.");

    /// <inheritdoc/>
    protected override int DefaultLowerVoices => 0;

    /// <inheritdoc/>
    protected override string MidiInstrument => "lead 1 (square)";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "lead 1 (square)",
        "lead 2 (sawtooth)",
        "lead 3 (calliope)",
        "lead 4 (chiff)",
        "lead 5 (charang)",
        "lead 6 (voice)",
        "lead 7 (fifths)",
        "lead 8 (bass+lead)",
    };
}

/// <summary>A synth pad.</summary>
public sealed class SynthPad : SynthPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Synth pad");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Synth pad", "Syn.Pad");

    /// <inheritdoc/>
    protected override string MidiInstrument => "pad 2 (warm)";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "pad 1 (new age)",
        "pad 2 (warm)",
        "pad 3 (polysynth)",
        "pad 4 (choir)",
        "pad 5 (bowed)",
        "pad 6 (metallic)",
        "pad 7 (halo)",
        "pad 8 (sweep)",
    };
}

/// <summary>A synth bass: one bass line by default.</summary>
public sealed class SynthBass : SynthPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Synth bass");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Synth bass", "Syn.Bs.");

    /// <inheritdoc/>
    protected override int DefaultUpperVoices => 0;

    /// <inheritdoc/>
    protected override string MidiInstrument => "synth bass 1";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "synth bass 1",
        "synth bass 2",
    };
}

/// <summary>Synth strings.</summary>
public sealed class SynthStrings : SynthPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Synth strings");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Synth strings", "Syn.Str.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "synthstrings 1";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "synthstrings 1",
        "synthstrings 2",
    };
}

/// <summary>Synth brass.</summary>
public sealed class SynthBrass : SynthPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Synth brass");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Synth brass", "Syn.Br.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "synthbrass 1";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "synthbrass 1",
        "synthbrass 2",
    };
}

/// <summary>Synth effects.</summary>
public sealed class SynthFx : SynthPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Synth effects");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Synth effects", "Syn.Fx.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "fx 3 (crystal)";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> MidiInstruments => new[]
    {
        "fx 1 (rain)",
        "fx 2 (soundtrack)",
        "fx 3 (crystal)",
        "fx 4 (atmosphere)",
        "fx 5 (brightness)",
        "fx 6 (goblins)",
        "fx 7 (echoes)",
        "fx 8 (sci-fi)",
    };
}
