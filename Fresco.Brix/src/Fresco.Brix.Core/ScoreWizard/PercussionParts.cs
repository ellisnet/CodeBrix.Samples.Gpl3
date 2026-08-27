// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Services;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/percussion.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>A percussion instrument that plays pitches.</summary>
public abstract class PitchedPercussionPart : SingleVoicePart
{
}

/// <summary>The timpani.</summary>
public sealed class Timpani : PitchedPercussionPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Timpani");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Timpani", "Tmp.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "timpani";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -1;
}

/// <summary>The xylophone.</summary>
public sealed class Xylophone : PitchedPercussionPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Xylophone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Xylophone", "Xyl.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "xylophone";

    /// <inheritdoc/>
    protected override int Octave => 2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (1, 0, 0);
}

/// <summary>The marimba: two staves, the lower one optional.</summary>
public class Marimba : PianoStaffPart
{
    /// <inheritdoc/>
    public Marimba()
    {
        UpperVoices.Label = () => I18n.Get("Upper staff:");
        LowerVoices.Label = () => I18n.Get("Lower staff:");
        LowerVoices.ToolTip = () => I18n.Get(
            "Set the number of voices to 0 to disable the second staff.");
    }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Marimba");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Marimba", "Mar.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "marimba";

    /// <inheritdoc/>
    protected override int MinLowerVoices => 0;
}

/// <summary>The vibraphone.</summary>
public sealed class Vibraphone : Marimba
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Vibraphone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Vibraphone", "Vib.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "vibraphone";
}

/// <summary>The tubular bells.</summary>
public sealed class TubularBells : PitchedPercussionPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Tubular bells");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Tubular bells", "Tub.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "tubular bells";
}

/// <summary>The steelpan.</summary>
public sealed class Steelpan : PitchedPercussionPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Steelpan");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Steelpan", "Pan");

    /// <inheritdoc/>
    protected override string MidiInstrument => "steel drums";
}

/// <summary>The hammered dulcimer.</summary>
public sealed class Dulcimer : PitchedPercussionPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Dulcimer");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Dulcimer", "Dul.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "dulcimer";
}

/// <summary>The glockenspiel.</summary>
public sealed class Glockenspiel : PitchedPercussionPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Glockenspiel");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Glockenspiel", "Gls.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "glockenspiel";

    /// <inheritdoc/>
    protected override int Octave => 3;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (2, 0, 0);
}

/// <summary>The carillon: a manual staff and a pedal staff.</summary>
public sealed class Carillon : PianoStaffPart
{
    /// <inheritdoc/>
    public Carillon()
    {
        UpperVoices.Label = () => I18n.Get("Manual staff:");
        LowerVoices.Label = () => I18n.Get("Pedal staff:");
    }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Carillon");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Carillon", "Car.");

    //Upstream's own note: anyone knows better?
    /// <inheritdoc/>
    protected override string MidiInstrument => "tubular bells";

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        Dom.PianoStaff piano = new Dom.PianoStaff();
        builder.SetInstrumentNamesFromPart(piano, this, data);
        Dom.Sim staves = new Dom.Sim(piano);
        BuildStaff(data, builder, "manual", 1, UpperVoices.Value, staves);
        if (DynamicsStaff.Value) { BuildDynamicsStaff(data, staves); }

        BuildStaff(data, builder, "pedal", 0, LowerVoices.Value, staves, "bass");
        data.Nodes.Add(piano);
    }
}

/// <summary>A drum kit.</summary>
public sealed class Drums : PartType
{
    /// <summary>Initializes the part and its settings.</summary>
    public Drums()
    {
        Voices = Add(new NumberSetting("voices", 1, 4, 1)
        {
            Label = () => I18n.Get("Voices:"),
        });
        DrumStyle = Add(new ChoiceSetting(
            "drumStyle",
            new[]
            {
                new ChoiceItem(() => I18n.Get("Drums (5 lines, default)")),
                new ChoiceItem(() => I18n.Get("Timbales-style (2 lines)")),
                new ChoiceItem(() => I18n.Get("Congas-style (2 lines)")),
                new ChoiceItem(() => I18n.Get("Bongos-style (2 lines)")),
                new ChoiceItem(() => I18n.Get("Percussion-style (1 line)")),
            })
        {
            Label = () => I18n.Get("Style:"),
        });
        DrumStems = Add(new BoolSetting("drumStems")
        {
            Label = () => I18n.Get("Remove stems"),
            ToolTip = () => I18n.Get("Remove the stems from the drum notes."),
        });
    }

    /// <summary>Gets how many voices the staff has.</summary>
    public NumberSetting Voices { get; }

    /// <summary>Gets which drum-style table the staff uses.</summary>
    public ChoiceSetting DrumStyle { get; }

    /// <summary>Gets whether the notes are written without stems.</summary>
    public BoolSetting DrumStems { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Drums");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Drums", "Dr.");

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        Dom.DrumStaff staff = new Dom.DrumStaff();
        Dom.Simr music = new Dom.Simr(staff);
        if (Voices.Value > 1)
        {
            for (int voice = 1; voice <= Voices.Value; voice++)
            {
                Dom.Seq sequence = new Dom.Seq(new Dom.DrumVoice(parent: music));
                new Dom.Text("\\voice" + LyUtil.Int2Text(voice), sequence);
                Dom.Assignment assignment =
                    AssignDrums(data, "drum" + LyUtil.Int2Text(voice));
                new Dom.Identifier(assignment.Name, sequence);
            }
        }
        else
        {
            new Dom.Identifier(AssignDrums(data, "drum").Name, music);
        }

        builder.SetInstrumentNamesFromPart(staff, this, data);

        int style = DrumStyle.SelectedIndex;
        if (style > 0)
        {
            string[] tables = { "drums", "timbales", "congas", "bongos", "percussion" };
            int[] lines = { 5, 2, 2, 2, 1 };
            staff.GetWith()["drumStyleTable"] = new Dom.Scheme(tables[style] + "-style");
            new Dom.Line(
                "\\override StaffSymbol.line-count = #" + lines[style], staff.GetWith());
        }

        if (DrumStems.Value)
        {
            new Dom.Line("\\override Stem.stencil = ##f", staff.GetWith());
            new Dom.Line(
                "\\override Stem.length = #3  % " + I18n.Get("keep some distance"),
                staff.GetWith());
        }

        data.Nodes.Add(staff);
    }

    /// <summary>Makes an empty <c>\drummode</c> assignment.</summary>
    /// <param name="data">What the part is building into.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The assignment.</returns>
    private static Dom.Assignment AssignDrums(PartData data, string name = null)
    {
        Dom.Assignment assignment = data.Assign(name);
        Dom.DrumMode mode = new Dom.DrumMode(assignment);
        new Dom.Identifier(data.GlobalName, mode);
        new Dom.LineComment(I18n.Get("Drums follow here."), mode);
        new Dom.BlankLine(mode);
        return assignment;
    }
}
