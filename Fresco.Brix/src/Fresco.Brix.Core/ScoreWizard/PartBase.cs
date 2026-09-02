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
using System.Collections.ObjectModel;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/_base.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A part type: one row a user can put in the score, and the LilyPond it
/// writes when the wizard builds the document.
/// </summary>
/// <remarks>
/// The class NAME matters: it becomes the music variable's identifier in the
/// finished document (<c>TrumpetBb</c> writes <c>trumpetBb = \relative …</c>),
/// so every one of these carries upstream's own class name exactly.
/// </remarks>
public abstract class PartBase
{
    private readonly List<PartSetting> _settings = new List<PartSetting>();
    private bool _inSettingChanged;

    /// <summary>Raised when any of the part's settings changed.</summary>
    public event EventHandler SettingsChanged;

    /// <summary>Gets the name the music variables are built from.</summary>
    public virtual string TypeName => GetType().Name;

    /// <summary>Gets the settings, in the order they are shown.</summary>
    public IReadOnlyList<PartSetting> Settings
        => new ReadOnlyCollection<PartSetting>(_settings);

    /// <summary>Answers the part's name.</summary>
    /// <param name="translate">The translator to name it with.</param>
    /// <returns>The name.</returns>
    public abstract string Title(Translator translate);

    /// <summary>Answers the part's abbreviated name, or null.</summary>
    /// <param name="translate">The translator to name it with.</param>
    /// <returns>The abbreviation.</returns>
    public virtual string Short(Translator translate) => null;

    /// <summary>Answers the part's name in the interface language.</summary>
    /// <returns>The name.</returns>
    public string Title() => Title(I18n.Current);

    /// <summary>Answers the part's abbreviation in the interface language.</summary>
    /// <returns>The abbreviation, or null.</returns>
    public string Short() => Short(I18n.Current);

    /// <summary>Answers whether this part may contain another one.</summary>
    /// <param name="part">The part that would go inside.</param>
    /// <returns>Whether it may.</returns>
    public virtual bool Accepts(PartBase part) => false;

    /// <summary>Writes what this part adds to the score.</summary>
    /// <param name="data">What the part is building into.</param>
    /// <param name="builder">The builder, read for the score-wide settings.</param>
    public virtual void Build(PartData data, ScoreBuilder builder)
        => data.Nodes.Add(new Dom.Comment("Part " + TypeName));

    /// <summary>Registers a setting, in display order.</summary>
    /// <typeparam name="T">The setting's type.</typeparam>
    /// <param name="setting">The setting.</param>
    /// <returns>The setting, so a field can be assigned from the call.</returns>
    protected T Add<T>(T setting)
        where T : PartSetting
    {
        _settings.Add(setting);
        setting.Changed += OnSettingChanged;
        return setting;
    }

    /// <summary>Registers a setting straight after another one.</summary>
    /// <typeparam name="T">The setting's type.</typeparam>
    /// <param name="anchor">The setting it goes after.</param>
    /// <param name="setting">The setting.</param>
    /// <returns>The setting, so a field can be assigned from the call.</returns>
    /// <remarks>A subclass's settings would otherwise all land at the end;
    /// upstream reaches into the grid layout to the same effect.</remarks>
    protected T AddAfter<T>(PartSetting anchor, T setting)
        where T : PartSetting
    {
        int index = _settings.IndexOf(anchor);
        if (index < 0) { return Add(setting); }

        _settings.Insert(index + 1, setting);
        setting.Changed += OnSettingChanged;
        return setting;
    }

    /// <summary>Called after any setting changed.</summary>
    /// <remarks>Overridden by the parts whose settings switch each other on
    /// and off — upstream's <c>slotTabEnable</c> and friends.</remarks>
    protected virtual void SettingChanged()
    {
    }

    /// <summary>Passes a setting change on.</summary>
    /// <param name="sender">The setting.</param>
    /// <param name="e">Nothing.</param>
    private void OnSettingChanged(object sender, EventArgs e)
    {
        //An interlock changes settings, which announces them again: run it
        //once and let the changes it makes be reported, not re-interlocked.
        if (!_inSettingChanged)
        {
            _inSettingChanged = true;
            try { SettingChanged(); } finally { _inSettingChanged = false; }
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A part that cannot contain other parts.</summary>
public abstract class PartType : PartBase
{
}

/// <summary>A part type that can contain others, such as a staff group.</summary>
public abstract class ContainerPart : PartBase
{
    /// <inheritdoc/>
    public override bool Accepts(PartBase part) => true;

    /// <summary>Answers the node this container's children are built into.</summary>
    /// <param name="node">The node the container itself was added to.</param>
    /// <returns>The node to go on with.</returns>
    public virtual Dom.LyNode MakeNode(Dom.LyNode node) => node;
}

/// <summary>
/// A container that is a group of its own — a book, a book part or a score.
/// </summary>
/// <remarks>Groups stack horizontally (each is its own block of the document);
/// everything else stacks vertically inside one score.</remarks>
public abstract class GroupPart : ContainerPart
{
}

/// <summary>A part that is one staff with one voice on it.</summary>
public abstract class SingleVoicePart : PartType
{
    /// <summary>Gets the MIDI instrument, or the empty string for none.</summary>
    protected virtual string MidiInstrument => string.Empty;

    /// <summary>Gets the clef, or null for the default treble.</summary>
    protected virtual string Clef => null;

    /// <summary>Gets the octave the music stub starts in.</summary>
    protected virtual int Octave => 1;

    /// <summary>
    /// Gets the sounding pitch of a written <c>c'</c>, or null when the
    /// instrument does not transpose.
    /// </summary>
    protected virtual (int Octave, int Note, int Alter)? Transposition => null;

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        Dom.Assignment assignment = data.AssignMusic(null, Octave);
        Dom.Staff staff = new Dom.Staff();
        builder.SetInstrumentNamesFromPart(staff, this, data);
        if (!string.IsNullOrEmpty(MidiInstrument))
        {
            builder.SetMidiInstrument(staff, MidiInstrument);
        }

        Dom.Container sequence = new Dom.Seqr(staff);
        if (!string.IsNullOrEmpty(Clef)) { new Dom.Clef(Clef, sequence); }

        if (Transposition != null)
        {
            sequence = builder.SetStaffTransposition(sequence, Transposition.Value);
        }

        new Dom.Identifier(assignment.Name, sequence);
        data.Nodes.Add(staff);
    }
}

/// <summary>A part that is a two-staff piano-style system.</summary>
public abstract class PianoStaffPart : PartType
{
    /// <summary>Initializes the part and its settings.</summary>
    protected PianoStaffPart()
    {
        Notice = Add(new NoticeSetting(
            "label",
            () => I18n.Get("Adjust how many separate voices you want on each staff.")
                + " ("
                + I18n.Get(
                    "This is primarily useful when you write polyphonic music "
                    + "like a fugue.")
                + ")"));
        UpperVoices = Add(new NumberSetting(
            "upperVoices", MinUpperVoices, MaxUpperVoices, DefaultUpperVoices)
        {
            Label = () => I18n.Get("Right hand:"),
        });

        //Upstream's own maximum for the lower staff is maxUpperVoices; every
        //part type sets the two the same, so it has never shown.
        LowerVoices = Add(new NumberSetting(
            "lowerVoices", MinLowerVoices, MaxUpperVoices, DefaultLowerVoices)
        {
            Label = () => I18n.Get("Left hand:"),
        });
        DynamicsStaff = Add(new BoolSetting("dynamicsStaff", true)
        {
            Label = () => I18n.Get("Center dynamics between staffs"),
        });

        if (MidiInstruments.Count > 0)
        {
            MidiInstrumentSelection = Add(new ChoiceSetting(
                "midiInstrumentSelection",
                CreateMidiInstrumentItems(),
                IndexOfMidiInstrument())
            {
                Label = () => I18n.Get("MIDI instrument:"),
            });
        }

        VoiceCountChanged();
    }

    /// <summary>Gets the explanatory paragraph above the settings.</summary>
    public NoticeSetting Notice { get; }

    /// <summary>Gets how many voices the upper staff has.</summary>
    public NumberSetting UpperVoices { get; }

    /// <summary>Gets how many voices the lower staff has.</summary>
    public NumberSetting LowerVoices { get; }

    /// <summary>Gets whether dynamics are centred between the staves.</summary>
    public BoolSetting DynamicsStaff { get; }

    /// <summary>Gets the MIDI instrument choice, or null when there is none.</summary>
    public ChoiceSetting MidiInstrumentSelection { get; }

    /// <summary>Gets the MIDI instrument used when there is no choice.</summary>
    protected virtual string MidiInstrument => string.Empty;

    /// <summary>Gets the MIDI instruments offered, if any.</summary>
    protected virtual IReadOnlyList<string> MidiInstruments => Array.Empty<string>();

    /// <summary>Gets the octave the right-hand stub starts in.</summary>
    protected virtual int Octave => 1;

    /// <summary>Gets the sounding pitch of a written <c>c'</c>, or null.</summary>
    protected virtual (int Octave, int Note, int Alter)? Transposition => null;

    /// <summary>Gets the fewest voices the upper staff may have.</summary>
    protected virtual int MinUpperVoices => 1;

    /// <summary>Gets the most voices the upper staff may have.</summary>
    protected virtual int MaxUpperVoices => 4;

    /// <summary>Gets how many voices the upper staff starts with.</summary>
    protected virtual int DefaultUpperVoices => 1;

    /// <summary>Gets the fewest voices the lower staff may have.</summary>
    protected virtual int MinLowerVoices => 1;

    /// <summary>Gets how many voices the lower staff starts with.</summary>
    protected virtual int DefaultLowerVoices => 1;

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        int upperCount = UpperVoices.Value;
        int lowerCount = LowerVoices.Value;
        Dom.LyNode part = null;
        if (upperCount > 0 && lowerCount > 0)
        {
            Dom.PianoStaff piano = new Dom.PianoStaff();
            part = piano;
            Dom.Sim staves = new Dom.Sim(piano);
            BuildStaff(data, builder, "right", Octave, upperCount, staves);
            if (DynamicsStaff.Value)
            {
                //Both staves have to be there for dynamics to sit between them.
                BuildDynamicsStaff(data, staves);
            }

            BuildStaff(data, builder, "left", Octave - 1, lowerCount, staves, "bass");
        }
        else if (upperCount > 0)
        {
            part = BuildStaff(data, builder, null, Octave, upperCount);
        }
        else if (lowerCount > 0)
        {
            part = BuildStaff(data, builder, null, Octave - 1, lowerCount, null, "bass");
        }

        if (part == null) { return; }

        builder.SetInstrumentNamesFromPart(part, this, data);
        data.Nodes.Add(part);
    }

    /// <summary>Builds one staff with the wanted number of voices.</summary>
    /// <param name="data">What the part is building into.</param>
    /// <param name="builder">The builder.</param>
    /// <param name="name">The staff's name, or null for an unnamed one.</param>
    /// <param name="octave">The octave its stubs start in.</param>
    /// <param name="numVoices">How many voices it has.</param>
    /// <param name="node">The node to add the staff to, or null.</param>
    /// <param name="clef">The clef, or null for the default.</param>
    /// <returns>The staff.</returns>
    protected Dom.Staff BuildStaff(
        PartData data,
        ScoreBuilder builder,
        string name,
        int octave,
        int numVoices = 1,
        Dom.Container node = null,
        string clef = null)
    {
        Dom.Staff staff = new Dom.Staff(name, parent: node);
        if (MidiInstrumentSelection != null)
        {
            string midiInstrument = MidiInstrumentSelection.Text;
            if (string.Equals(midiInstrument, "percussive organ", StringComparison.Ordinal)
                && !string.Equals(name, "right", StringComparison.Ordinal))
            {
                //The Hammond B3 this MIDI instrument stands for only has
                //percussion on its upper manual.
                midiInstrument = "drawbar organ";
            }

            builder.SetMidiInstrument(staff, midiInstrument);
        }
        else
        {
            builder.SetMidiInstrument(staff, MidiInstrument);
        }

        Dom.Container music = new Dom.Seqr(staff);
        if (!string.IsNullOrEmpty(clef)) { new Dom.Clef(clef, music); }

        if (Transposition != null)
        {
            music = builder.SetStaffTransposition(music, Transposition.Value);
        }

        if (numVoices == 1)
        {
            new Dom.Identifier(data.AssignMusic(name, octave).Name, music);
            return staff;
        }

        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14).
        //Upstream is `name + ly.util.int2text(i)`, and both of its single-staff
        //branches call this with name=None — so an unnamed staff with SEVERAL
        //voices is `None + str`, a TypeError. The score wizard reaches it:
        //a synth part may set either staff to zero voices, so a SynthBass with
        //0 upper and 2 lower voices crashes Frescobaldi 4.0.7.
        //
        //What name=None MEANS is already settled in this same method: the
        //single-voice branch passes it to assignMusic, which falls back to
        //mkid(data.name()) — the part's own name. The multi-voice branch simply
        //fails to honour it, so that fallback is what is used here, and the
        //voices are numbered the way this class numbers its NAMED ones
        //(rightOne, rightTwo → synthBassOne, synthBassTwo). Every output that
        //does not crash upstream is unchanged.
        //⚠ The tablature parts spell their unnamed voices differently —
        //mkid(data.name(), "voice") + int2text(i), so guitarVoiceOne — so if
        //upstream ever fixes this and picks THAT convention, the identifier
        //here should follow it. Nothing will catch that automatically: the
        //scenario is absent from the parity fixtures because upstream crashes
        //on it, and a crash is not an answer to record.
        string stem = name ?? LyUtil.MkId(data.Name());
        music = new Dom.Sim(music);
        for (int voice = 1; voice < numVoices; voice++)
        {
            new Dom.Identifier(
                data.AssignMusic(stem + LyUtil.Int2Text(voice), octave).Name, music);
            new Dom.VoiceSeparator(music);
        }

        new Dom.Identifier(
            data.AssignMusic(stem + LyUtil.Int2Text(numVoices), octave).Name, music);
        return staff;
    }

    /// <summary>Builds the staff that carries the dynamics.</summary>
    /// <param name="data">What the part is building into.</param>
    /// <param name="pianoSim">The system the staves are in.</param>
    protected void BuildDynamicsStaff(PartData data, Dom.Container pianoSim)
    {
        Dom.Dynamics dynamicsStaff = new Dom.Dynamics(parent: pianoSim);
        Dom.Assignment assignment = data.Assign("dynamics");
        new Dom.Identifier(assignment.Name, dynamicsStaff);
        Dom.Seq stub = new Dom.Seq(assignment);
        new Dom.Identifier(data.GlobalName, stub) { After = 1 };
        new Dom.LineComment(I18n.Get("Dynamics follow here."), stub);
        new Dom.BlankLine(stub);
    }

    /// <inheritdoc/>
    protected override void SettingChanged() => VoiceCountChanged();

    /// <summary>Answers the MIDI instruments as list rows.</summary>
    /// <returns>The rows.</returns>
    protected IEnumerable<ChoiceItem> CreateMidiInstrumentItems()
    {
        foreach (string instrument in MidiInstruments)
        {
            yield return new ChoiceItem(instrument);
        }
    }

    /// <summary>Answers which MIDI instrument starts out chosen.</summary>
    /// <returns>The index, or 0.</returns>
    protected int IndexOfMidiInstrument()
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

    /// <summary>Keeps at least one staff present and the dynamics tick honest.</summary>
    private void VoiceCountChanged()
    {
        if (UpperVoices == null || LowerVoices == null) { return; }

        UpperVoices.Minimum = LowerVoices.Value > 0 ? MinUpperVoices : 1;
        LowerVoices.Minimum = UpperVoices.Value > 0 ? MinLowerVoices : 1;
        DynamicsStaff.IsEnabled = UpperVoices.Value > 0 && LowerVoices.Value > 0;
    }
}

/// <summary>
/// The chord-names staff a part can have above it, and the fret diagrams that
/// can go with it.
/// </summary>
/// <remarks>
/// //was previously: the <c>_base.ChordNames</c> MIXIN, which upstream stirs
/// into both the Chord names part and the Lead sheet. C# has no multiple
/// inheritance, so the two settings and the code that writes them live here and
/// both parts own one.
/// </remarks>
public sealed class ChordNamesSupport
{
    /// <summary>Initializes the settings.</summary>
    public ChordNamesSupport()
    {
        ChordStyle = new ChoiceSetting(
            "chordStyle",
            new[]
            {
                new ChoiceItem(() => I18n.Get("Default")),
                new ChoiceItem(() => I18n.Get("German")),
                new ChoiceItem(() => I18n.Get("Semi-German")),
                new ChoiceItem(() => I18n.Get("Italian")),
                new ChoiceItem(() => I18n.Get("French")),
            })
        {
            Label = () => I18n.Get("Chord style:"),
        };
        GuitarFrets = new BoolSetting("guitarFrets")
        {
            Label = () => I18n.Get("Guitar fret diagrams"),
            //was previously: "…(LilyPond 2.12 and above)." Two reasons to
            //change it: a tooltip is chrome and FR13 names tooltips, and under
            //FR5.1 there is ONE engine, so a version caveat describes a choice
            //the user does not have — the feature is simply there.
            ToolTip = () => I18n.Get(
                "Show predefined guitar fret diagrams below the chord names."),
        };
    }

    /// <summary>Gets which naming style the chords are written in.</summary>
    public ChoiceSetting ChordStyle { get; }

    /// <summary>Gets whether fret diagrams are shown too.</summary>
    public BoolSetting GuitarFrets { get; }

    /// <summary>Writes the chord names staff.</summary>
    /// <param name="data">What the part is building into.</param>
    /// <param name="builder">The builder.</param>
    public void Build(PartData data, ScoreBuilder builder)
    {
        Dom.ChordNames chordNames = new Dom.ChordNames();
        Dom.Assignment assignment = data.Assign("chordNames");
        new Dom.Identifier(assignment.Name, chordNames);
        Dom.ChordMode mode = new Dom.ChordMode(assignment);
        new Dom.Identifier(data.GlobalName, mode) { After = 1 };
        int style = ChordStyle.SelectedIndex;
        if (style > 0)
        {
            string[] names = { "german", "semiGerman", "italian", "french" };
            new Dom.Line("\\" + names[style - 1] + "Chords", mode);
        }

        new Dom.LineComment(I18n.Get("Chords follow here."), mode);
        new Dom.BlankLine(mode);
        data.Nodes.Add(chordNames);

        if (!GuitarFrets.Value) { return; }

        Dom.FretBoards fretBoards = new Dom.FretBoards();
        new Dom.Identifier(assignment.Name, fretBoards);
        data.Nodes.Add(fretBoards);
        data.Includes.Add("predefined-guitar-fretboards.ly");
    }
}
