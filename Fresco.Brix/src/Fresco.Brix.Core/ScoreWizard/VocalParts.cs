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
using System.Text;
using System.Text.RegularExpressions;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/vocal.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The stanza count, the ambitus, and the lyric assignments every vocal part
/// needs.
/// </summary>
/// <remarks>
/// //was previously: the <c>VocalPart</c> base class. The solo voices also
/// need <see cref="SingleVoicePart"/>'s staff building, and C# has no multiple
/// inheritance, so what upstream inherits twice is owned once here.
/// </remarks>
public sealed class VocalSupport
{
    /// <summary>Initializes the settings.</summary>
    public VocalSupport()
    {
        Stanzas = new NumberSetting("stanzas", 1, 99, 1)
        {
            Label = () => I18n.Get("Stanzas:"),
            ToolTip = () => I18n.Get("The number of stanzas."),
        };
        Ambitus = new BoolSetting("ambitus")
        {
            Label = () => I18n.Get("Ambitus"),
            ToolTip = () => I18n.Get(
                "Show the pitch range of the voice at the beginning of the staff."),
        };
    }

    /// <summary>Gets how many stanzas the part has.</summary>
    public NumberSetting Stanzas { get; }

    /// <summary>Gets whether the voice's range is shown.</summary>
    public BoolSetting Ambitus { get; }

    /// <summary>Makes an empty lyrics assignment.</summary>
    /// <param name="data">What the part is building into.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="verse">The stanza number, or 0 for the only one.</param>
    /// <returns>The assignment.</returns>
    public static Dom.Assignment AssignLyrics(PartData data, string name, int verse = 0)
    {
        Dom.LyricMode mode = new Dom.LyricMode();
        if (verse > 0)
        {
            name += LyUtil.Int2Text(verse);
            new Dom.Line(
                string.Create(CultureInfo.InvariantCulture, $"\\set stanza = \"{verse}.\""),
                mode);
        }

        Dom.Assignment assignment = data.Assign(name);
        assignment.Append(mode);
        new Dom.LineComment(I18n.Get("Lyrics follow here."), mode);
        new Dom.BlankLine(mode);
        return assignment;
    }

    /// <summary>Adds the stanzas to a voice with <c>\addlyrics</c>.</summary>
    /// <param name="data">What the part is building into.</param>
    /// <param name="node">The voice or staff.</param>
    public void AddStanzas(PartData data, Dom.Container node)
    {
        if (Stanzas.Value == 1)
        {
            new Dom.Identifier(
                AssignLyrics(data, "verse").Name, new Dom.AddLyrics(node));
            return;
        }

        for (int verse = 1; verse <= Stanzas.Value; verse++)
        {
            new Dom.Identifier(
                AssignLyrics(data, "verse", verse).Name, new Dom.AddLyrics(node));
        }
    }
}

/// <summary>A solo voice: one staff, its own lyrics beneath it.</summary>
public abstract class VocalSoloPart : SingleVoicePart
{
    /// <summary>Initializes the part and its settings.</summary>
    protected VocalSoloPart()
    {
        Vocal = new VocalSupport();
        Add(Vocal.Stanzas);
        Add(Vocal.Ambitus);
    }

    /// <summary>Gets the stanza and ambitus settings.</summary>
    public VocalSupport Vocal { get; }

    /// <summary>Gets the octave this voice's music starts in.</summary>
    /// <remarks>The choir part reads this off the four solo voices rather than
    /// carrying a second table of its own, which is upstream's own arrangement
    /// through <c>voice2Voice</c>.</remarks>
    public int VoiceOctave => Octave;

    /// <inheritdoc/>
    protected override string MidiInstrument => "choir aahs";

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        base.Build(data, builder);

        //The music stub is the { } inside the last \relative; the \dynamicUp
        //goes straight after the \global that opens it.
        Dom.Assignment assignment = data.Assignments[^1];
        Dom.Container stub = (Dom.Container)((Dom.Container)assignment[0])[^1];
        stub.Insert(1, new Dom.Line("\\dynamicUp"));

        Dom.Staff staff = (Dom.Staff)data.Nodes[^1];

        //A staff that gets lyrics keeps its brackets: \addlyrics attaches to
        //the music before it, and a bare identifier would take them instead.
        ((Dom.Enclosed)staff[^1]).MayRemoveBrackets = false;
        Vocal.AddStanzas(data, staff);
        if (Vocal.Ambitus.Value)
        {
            new Dom.Line("\\consists \"Ambitus_engraver\"", staff.GetWith());
        }
    }
}

/// <summary>The soprano.</summary>
public sealed class SopranoVoice : VocalSoloPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Soprano");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Soprano", "S.");
}

/// <summary>The mezzo-soprano.</summary>
public sealed class MezzoSopranoVoice : VocalSoloPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Mezzo-soprano");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Mezzo-soprano", "Ms.");
}

/// <summary>The alto.</summary>
public sealed class AltoVoice : VocalSoloPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Alto");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Alto", "A.");

    /// <inheritdoc/>
    protected override int Octave => 0;
}

/// <summary>The tenor.</summary>
public sealed class TenorVoice : VocalSoloPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Tenor");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Tenor", "T.");

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override string Clef => "treble_8";
}

/// <summary>The bass.</summary>
public sealed class BassVoice : VocalSoloPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Bass");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Bass", "B.");

    /// <inheritdoc/>
    protected override int Octave => -1;

    /// <inheritdoc/>
    protected override string Clef => "bass";
}

/// <summary>A melody staff with chord names above and lyrics below.</summary>
public sealed class LeadSheet : PartType
{
    /// <summary>Initializes the part and its settings.</summary>
    public LeadSheet()
    {
        Add(new NoticeSetting(
            "label",
            () => I18n.Get(
                "The Lead Sheet provides a staff with chord names above "
                + "and lyrics below it. A second staff is optional.")));

        Chords = Add(new GroupSetting("chords", isCheckable: true, isChecked: true)
        {
            Label = () => I18n.Get("Chord names"),
        });
        ChordNames = new ChordNamesSupport();
        Chords.Add(ChordNames.ChordStyle);
        Chords.Add(ChordNames.GuitarFrets);

        Accompaniment = Add(new BoolSetting("accomp")
        {
            Label = () => I18n.Get("Add accompaniment staff"),
            ToolTip = () => I18n.Get(
                "Adds an accompaniment staff and also puts an accompaniment "
                + "voice in the upper staff."),
        });

        Vocal = new VocalSupport();
        Add(Vocal.Stanzas);
        Add(Vocal.Ambitus);
    }

    /// <summary>Gets the group holding the chord-name settings.</summary>
    public GroupSetting Chords { get; }

    /// <summary>Gets the chord-name settings.</summary>
    public ChordNamesSupport ChordNames { get; }

    /// <summary>Gets whether an accompaniment staff is added.</summary>
    public BoolSetting Accompaniment { get; }

    /// <summary>Gets the stanza and ambitus settings.</summary>
    public VocalSupport Vocal { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Lead sheet");

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        if (Chords.IsChecked) { ChordNames.Build(data, builder); }

        Dom.ContextType part;
        if (Accompaniment.Value)
        {
            //TODO (upstream's own): instrument names? a different MIDI
            //instrument for the voice and the accompaniment?
            Dom.ChoirStaff choirStaff = new Dom.ChoirStaff();
            part = choirStaff;
            Dom.Sim staves = new Dom.Sim(choirStaff);
            Dom.Sim melody = new Dom.Sim(new Dom.Staff(parent: staves));
            Dom.Voice upperVoice = new Dom.Voice(parent: melody);
            Dom.Seq upper = new Dom.Seq(upperVoice);
            new Dom.Text("\\voiceOne", upper);
            new Dom.Identifier(data.AssignMusic("melody", 1).Name, upper);

            Dom.Seq second = new Dom.Seq(new Dom.Voice(parent: melody));
            new Dom.Text("\\voiceTwo", second);
            new Dom.Identifier(data.AssignMusic("accRight", 0).Name, second);

            Dom.Staff accompanimentStaff = new Dom.Staff(parent: staves);
            Dom.Seq accompaniment = new Dom.Seq(accompanimentStaff);
            new Dom.Clef("bass", accompaniment);
            new Dom.Identifier(data.AssignMusic("accLeft", -1).Name, accompaniment);

            if (Vocal.Ambitus.Value)
            {
                //\addlyrics cannot be used when the voice has a \with { }
                //section, because that creates a nested Voice context. So when
                //the ambitus engraver goes on the Voice, the Lyrics contexts
                //are put inside the ChoirStaff by hand instead.
                upperVoice.ContextId = new Dom.Reference("melody");
                new Dom.Line("\\consists \"Ambitus_engraver\"", upperVoice.GetWith());
                int count = Vocal.Stanzas.Value;
                if (count == 1)
                {
                    Dom.Lyrics lyrics = new Dom.Lyrics();
                    staves.InsertBefore(accompanimentStaff, lyrics);
                    new Dom.Identifier(
                        VocalSupport.AssignLyrics(data, "verse").Name,
                        new Dom.LyricsTo(upperVoice.ContextId, lyrics));
                }
                else
                {
                    for (int verse = 1; verse <= count; verse++)
                    {
                        Dom.Lyrics lyrics = new Dom.Lyrics();
                        staves.InsertBefore(accompanimentStaff, lyrics);
                        new Dom.Identifier(
                            VocalSupport.AssignLyrics(data, "verse", verse).Name,
                            new Dom.LyricsTo(upperVoice.ContextId, lyrics));
                    }
                }
            }
            else
            {
                Vocal.AddStanzas(data, upperVoice);
            }
        }
        else
        {
            Dom.Assignment melody = data.AssignMusic("melody", 1);
            Dom.Staff staff = new Dom.Staff();
            part = staff;
            new Dom.Identifier(melody.Name, new Dom.Seq(staff));
            Vocal.AddStanzas(data, staff);
            if (Vocal.Ambitus.Value)
            {
                new Dom.Line("\\consists \"Ambitus_engraver\"", staff.GetWith());
            }
        }

        data.Nodes.Add(part);
    }
}

/// <summary>A choir: any voicing of sopranos, altos, tenors and basses.</summary>
public sealed class Choir : PartType
{
    private static readonly string[] VoiceLetters = { "S", "A", "T", "B" };

    private static readonly Dictionary<char, string> VoiceIds =
        new Dictionary<char, string>
        {
            ['S'] = "soprano",
            ['A'] = "alto",
            ['T'] = "tenor",
            ['B'] = "bass",
        };

    private static readonly Dictionary<char, string> VoiceMidi =
        new Dictionary<char, string>
        {
            ['S'] = "soprano sax",
            ['A'] = "soprano sax",
            ['T'] = "tenor sax",
            ['B'] = "tenor sax",
        };

    /// <summary>Initializes the part and its settings.</summary>
    public Choir()
    {
        Add(new NoticeSetting(
            "label",
            () => I18n.Get(
                "Please select the voices for the choir. "
                + "Use the letters S, A, T, or B. A hyphen denotes a new staff.")
                + " ("
                + I18n.Get("Hint: For a double choir you can use two choir parts.")
                + ")"));

        Voicing = Add(new ChoiceSetting(
            "voicing",
            new[]
            {
                "SA-TB", "S-A-T-B",
                "SA", "S-A", "SS-A", "S-S-A",
                "TB", "T-B", "TT-B", "T-T-B",
                "SS-A-T-B", "S-A-TT-B", "SS-A-TT-B",
                "S-S-A-T-T-B", "S-S-A-A-T-T-B-B",
            }.Select(voicing => new ChoiceItem(voicing)),
            isEditable: true)
        {
            Label = () => I18n.Get("Voicing:"),
        });

        Vocal = new VocalSupport();
        Add(Vocal.Stanzas);

        Lyrics = Add(new ChoiceSetting(
            "lyrics",
            new[]
            {
                new ChoiceItem(
                    () => I18n.Get("All voices same lyrics"),
                    null,
                    () => I18n.Get(
                        "A set of the same lyrics is placed between all staves.")),
                new ChoiceItem(
                    () => I18n.Get("Every voice same lyrics"),
                    null,
                    () => I18n.Get(
                        "Every voice gets its own lyrics, using the same text as the"
                        + " other voices.")),
                new ChoiceItem(
                    () => I18n.Get("Every voice different lyrics"),
                    null,
                    () => I18n.Get("Every voice gets a different set of lyrics.")),
                new ChoiceItem(
                    () => I18n.Get("Distribute stanzas"),
                    null,
                    () => I18n.Get(
                        "One set of stanzas is distributed across the staves.")),
            })
        {
            Label = () => I18n.Get("Lyrics:"),
        });

        Add(Vocal.Ambitus);

        PianoReduction = Add(new BoolSetting("pianoReduction")
        {
            Label = () => I18n.Get("Piano reduction"),
            ToolTip = () => I18n.Get("Adds an automatically generated piano reduction."),
        });
        RehearsalMidi = Add(new BoolSetting("rehearsalMidi")
        {
            Label = () => I18n.Get("Rehearsal MIDI files"),
            ToolTip = () => I18n.Get(
                "Creates a rehearsal MIDI file for every voice, "
                + "even if no MIDI output is generated for the main score."),
        });
    }

    /// <summary>Gets which voices are on which staves.</summary>
    public ChoiceSetting Voicing { get; }

    /// <summary>Gets the stanza and ambitus settings.</summary>
    public VocalSupport Vocal { get; }

    /// <summary>Gets how the lyrics are laid out.</summary>
    public ChoiceSetting Lyrics { get; }

    /// <summary>Gets whether a piano reduction is added.</summary>
    public BoolSetting PianoReduction { get; }

    /// <summary>Gets whether per-voice rehearsal MIDI files are made.</summary>
    public BoolSetting RehearsalMidi { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Choir");

    /// <summary>Gets the solo voice a letter stands for.</summary>
    /// <param name="letter">One of S, A, T or B.</param>
    /// <returns>The voice.</returns>
    public static VocalSoloPart VoiceFor(char letter) => letter switch
    {
        'S' => new SopranoVoice(),
        'A' => new AltoVoice(),
        'T' => new TenorVoice(),
        _ => new BassVoice(),
    };

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        //Normalize the voicing: upper case, only SATB and hyphens, no doubled
        //hyphens and none at either end.
        string staves = (Voicing.Text ?? string.Empty).ToUpperInvariant();
        staves = Regex.Replace(staves, "[^SATB-]+", string.Empty);
        staves = Regex.Replace(staves, "-+", "-").Trim('-');
        if (staves.Length == 0) { return; }

        string[] splitStaves = staves.Split('-');
        int numStaves = splitStaves.Length;
        Dictionary<string, int> staffCIDs =
            new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<char, int> voiceCounter = new Dictionary<char, int>();
        int maxNumVoices = splitStaves.Max(s => s.Length);
        int numStanzas = Vocal.Stanzas.Value;
        Dictionary<int, List<(Dom.Container Node, string Name)>> lyrics =
            new Dictionary<int, List<(Dom.Container, string)>>();
        Dictionary<char, List<object>> pianoReduction =
            new Dictionary<char, List<object>>();
        List<(char Voice, int Num, object Reference, string LyricName)> rehearsalMidis =
            new List<(char, int, object, string)>();

        Dom.ChoirStaff choirStaff = new Dom.ChoirStaff();
        Dom.Container choir = new Dom.Sim(choirStaff);
        data.Nodes.Add(choirStaff);

        //Name the choir itself only when there are several of them and this one
        //is more than a single staff.
        if (numStaves > 1 && data.Num > 0)
        {
            builder.SetInstrumentNames(
                choirStaff,
                builder.InstrumentName(t => t(null, "Choir"), data.Num),
                builder.InstrumentName(
                    t => t("abbreviation for Choir", "Ch."), data.Num));
        }

        int lyricsMode = Lyrics.SelectedIndex;
        bool lyrEachSame = lyricsMode == 1;
        bool lyrEachDiff = lyricsMode == 2;
        bool lyrSpread = lyricsMode == 3;
        bool lyrEach = lyrEachSame || lyrEachDiff;

        //Stanza 0 means "do not print a stanza number".
        List<int> allStanzas = numStanzas == 1
            ? new List<int> { 0 }
            : Enumerable.Range(1, numStanzas).ToList();

        List<List<int>> stanzaGroups = StanzaGroups(
            allStanzas, numStanzas, numStaves, lyrSpread);

        void SetStaffAffinity(Dom.ContextType context, string affinity)
        {
            if (!builder.LyVersionAtLeast(2, 13, 4)) { return; }

            new Dom.Line(
                "\\override VerticalAxisGroup.staff-affinity = #" + affinity,
                context.GetWith());
        }

        string columnCommand = builder.LyVersionAtLeast(2, 11, 57)
            ? "center-column"
            : "center-align";

        Dom.Markup MakeColumnMarkup(IEnumerable<string> names)
        {
            Dom.Markup markup = new Dom.Markup();
            Dom.MarkupEnclosed column = new Dom.MarkupEnclosed(columnCommand, markup);
            foreach (string name in names) { new Dom.QuotedString(name, column); }

            return markup;
        }

        int stavesLeft = numStaves;
        for (int staffIndex = 0; staffIndex < numStaves; staffIndex++)
        {
            List<int> stanzas = stanzaGroups[staffIndex];
            stavesLeft--;
            string staffVoicing = splitStaves[staffIndex];
            int numVoices = staffVoicing.Length;

            //Sort the letters into SATB order.
            staffVoicing = string.Concat(VoiceLetters.Select(
                letter => new string(letter[0], staffVoicing.Count(c => c == letter[0]))));

            Dom.Staff staff = new Dom.Staff(parent: choir);
            builder.SetMidiInstrument(staff, "choir aahs");

            //Each voice is a letter and a number: 0 when it occurs once, and
            //1, 2, … when there are several of the same kind (Soprano I and II).
            List<(char Voice, int Num)> voices = new List<(char, int)>();
            foreach (char voice in staffVoicing)
            {
                if (staves.Count(c => c == voice) > 1)
                {
                    voiceCounter[voice] =
                        (voiceCounter.TryGetValue(voice, out int count) ? count : 0) + 1;
                }

                voices.Add((voice, voiceCounter.TryGetValue(voice, out int num) ? num : 0));
            }

            if (numVoices == 1)
            {
                VocalSoloPart solo = VoiceFor(voices[0].Voice);
                builder.SetInstrumentNames(
                    staff,
                    builder.InstrumentName(solo.Title, voices[0].Num),
                    builder.InstrumentName(solo.Short, voices[0].Num));
            }
            else
            {
                //Stack the names in a markup column, long and short alike.
                builder.SetInstrumentNames(
                    staff,
                    MakeColumnMarkup(voices.Select(
                        v => builder.InstrumentName(VoiceFor(v.Voice).Title, v.Num))),
                    MakeColumnMarkup(voices.Select(
                        v => builder.InstrumentName(VoiceFor(v.Voice).Short, v.Num))));
            }

            //If EVERY staff has one voice, \addlyrics is used, and the braces
            //have to stay.
            Dom.Container staffMusic = lyrEach && maxNumVoices == 1
                ? new Dom.Seq(staff)
                : numVoices == 1 ? new Dom.Seqr(staff) : new Dom.Simr(staff);

            if (staffVoicing.Contains('B'))
            {
                new Dom.Clef("bass", staffMusic);
            }
            else if (staffVoicing.Contains('T'))
            {
                new Dom.Clef("treble_8", staffMusic);
            }

            int[] order = numVoices switch
            {
                1 => new[] { 0 },
                2 => new[] { 1, 2 },
                _ when staffVoicing is "SSA" or "TTB" => new[] { 1, 3, 2 },
                _ when staffVoicing is "SAA" or "TBB" => new[] { 1, 2, 4 },
                _ when staffVoicing is "SSAA" or "TTBB" => new[] { 1, 3, 2, 4 },
                _ => Enumerable.Range(1, numVoices).ToArray(),
            };

            //What this staff would be called if something has to refer to it.
            staffCIDs[staffVoicing] =
                (staffCIDs.TryGetValue(staffVoicing, out int seen) ? seen : 0) + 1;
            Dom.Reference cid = new Dom.Reference(
                staffVoicing.ToLowerInvariant()
                + (staffCIDs[staffVoicing] > 1
                    ? staffCIDs[staffVoicing].ToString(CultureInfo.InvariantCulture)
                    : string.Empty));

            for (int index = 0; index < voices.Count; index++)
            {
                (char voice, int num) = voices[index];
                int voiceNum = order[index];
                string name = VoiceIds[voice];
                if (num > 0) { name += LyUtil.Int2Text(num); }

                Dom.Assignment music = data.AssignMusic(
                    name, VoiceFor(voice).VoiceOctave);
                string lyrName = lyrEachDiff ? name + "Verse" : "verse";
                Dom.Voice voiceContext = null;

                if (lyrEach && maxNumVoices == 1)
                {
                    foreach (int verse in stanzas)
                    {
                        Add(lyrics, verse, (new Dom.AddLyrics(staff), lyrName));
                    }

                    new Dom.Identifier(music.Name, staffMusic);
                }
                else
                {
                    string voiceName = VoiceIds[voice]
                        + (num > 0 ? num.ToString(CultureInfo.InvariantCulture) : string.Empty);
                    voiceContext = new Dom.Voice(voiceName, parent: staffMusic);
                    Dom.Seqr voiceMusic = new Dom.Seqr(voiceContext);
                    if (voiceNum > 0)
                    {
                        new Dom.Text("\\voice" + LyUtil.Int2Text(voiceNum), voiceMusic);
                    }

                    new Dom.Identifier(music.Name, voiceMusic);

                    if (stanzas.Count > 0
                        && (lyrEach
                            || (voiceNum <= 1 && (stavesLeft > 0 || numStaves == 1))))
                    {
                        //Lyrics above the staff need the staff to have a name,
                        //so that alignAboveContext can point at it.
                        bool above = lyrEach && (voiceNum & 1) == 1;
                        if (above && staff.ContextId == null) { staff.ContextId = cid; }

                        foreach (int verse in stanzas)
                        {
                            Dom.Lyrics lyricsContext = new Dom.Lyrics(parent: choir);
                            if (above)
                            {
                                //A quoted string over the REFERENCE, not over
                                //its name: the name can still be prefixed
                                //before the document is printed.
                                lyricsContext.GetWith()["alignAboveContext"] =
                                    new Dom.QuotedString(cid);
                                SetStaffAffinity(lyricsContext, "DOWN");
                            }
                            else if (!lyrEach && stavesLeft > 0)
                            {
                                SetStaffAffinity(lyricsContext, "CENTER");
                            }

                            Add(
                                lyrics,
                                verse,
                                (new Dom.LyricsTo(voiceName, lyricsContext), lyrName));
                        }
                    }
                }

                if (Vocal.Ambitus.Value)
                {
                    Dom.With ambitusContext =
                        (numVoices == 1 ? (Dom.ContextType)staff : voiceContext).GetWith();
                    new Dom.Line("\\consists \"Ambitus_engraver\"", ambitusContext);
                    if (voiceNum > 1)
                    {
                        new Dom.Line(
                            "\\override Ambitus.X-offset = #"
                            + ((voiceNum - 1) * 2.0).ToString(
                                "0.0######", CultureInfo.InvariantCulture),
                            ambitusContext);
                    }
                }

                if (!pianoReduction.TryGetValue(voice, out List<object> reduction))
                {
                    reduction = new List<object>();
                    pianoReduction[voice] = reduction;
                }

                reduction.Add(music.Name);
                rehearsalMidis.Add((voice, num, music.Name, lyrName));
            }
        }

        //Assign the lyrics after the notes, so their definitions come second.
        Dictionary<(string Name, int Verse), object> references =
            new Dictionary<(string, int), object>();
        foreach (int verse in allStanzas)
        {
            if (!lyrics.TryGetValue(
                verse, out List<(Dom.Container Node, string Name)> entries))
            {
                continue;
            }

            foreach ((Dom.Container node, string name) in entries)
            {
                if (!references.TryGetValue((name, verse), out object reference))
                {
                    reference = VocalSupport.AssignLyrics(data, name, verse).Name;
                    references[(name, verse)] = reference;
                }

                new Dom.Identifier(reference, node);
            }
        }

        if (PianoReduction.Value) { BuildPianoReduction(data, builder, pianoReduction); }

        if (RehearsalMidi.Value)
        {
            BuildRehearsalMidi(data, builder, rehearsalMidis, references, allStanzas);
        }
    }

    /// <summary>Adds an entry to a stanza's list.</summary>
    /// <param name="lyrics">The lists by stanza.</param>
    /// <param name="verse">The stanza.</param>
    /// <param name="entry">The entry.</param>
    private static void Add(
        Dictionary<int, List<(Dom.Container Node, string Name)>> lyrics,
        int verse,
        (Dom.Container Node, string Name) entry)
    {
        if (!lyrics.TryGetValue(verse, out List<(Dom.Container, string)> entries))
        {
            entries = new List<(Dom.Container, string)>();
            lyrics[verse] = entries;
        }

        entries.Add(entry);
    }

    /// <summary>Answers which stanzas go with which staff.</summary>
    /// <param name="allStanzas">Every stanza.</param>
    /// <param name="numStanzas">How many there are.</param>
    /// <param name="numStaves">How many staves there are.</param>
    /// <param name="spread">Whether the stanzas are spread across the staves.</param>
    /// <returns>One list per staff.</returns>
    private static List<List<int>> StanzaGroups(
        List<int> allStanzas, int numStanzas, int numStaves, bool spread)
    {
        List<List<int>> groups = new List<List<int>>();
        if (!spread || numStanzas <= 1 || numStaves <= 2)
        {
            for (int index = 0; index < numStaves; index++)
            {
                groups.Add(allStanzas);
            }

            return groups;
        }

        int spaces = numStaves - 1;
        int total = Math.Max(numStanzas, spaces);
        int count = total / spaces;
        int rest = total % spaces;

        int source = 0;
        List<int> Take(int howMany)
        {
            List<int> taken = new List<int>();
            for (int index = 0; index < howMany; index++)
            {
                taken.Add(allStanzas[source % allStanzas.Count]);
                source++;
            }

            return taken;
        }

        for (int index = 0; index < rest; index++) { groups.Add(Take(count + 1)); }

        for (int index = 0; index < numStaves - rest; index++) { groups.Add(Take(count)); }

        return groups;
    }

    /// <summary>Builds the automatically generated piano reduction.</summary>
    /// <param name="data">What the part is building into.</param>
    /// <param name="builder">The builder.</param>
    /// <param name="voices">The music references, by voice letter.</param>
    private static void BuildPianoReduction(
        PartData data, ScoreBuilder builder, Dictionary<char, List<object>> voices)
    {
        List<object> Of(char letter)
            => voices.TryGetValue(letter, out List<object> found)
                ? found
                : new List<object>();

        Dom.Assignment assignment = data.Assign("pianoReduction");
        data.Nodes.Add(new Dom.Identifier(assignment.Name));
        Dom.PianoStaff piano = new Dom.PianoStaff(parent: assignment);

        Dom.Sim staves = new Dom.Sim(piano);
        Dom.Staff rightStaff = new Dom.Staff(parent: staves);
        Dom.Staff leftStaff = new Dom.Staff(parent: staves);
        Dom.Seq right = new Dom.Seq(rightStaff);
        Dom.Seq left = new Dom.Seq(leftStaff);

        List<object> upper = Of('S').Concat(Of('A')).ToList();
        List<object> lower = Of('T').Concat(Of('B')).ToList();

        int preferUpper = 1;
        if (upper.Count == 0)
        {
            //A male choir.
            upper = Of('T');
            lower = Of('B');
            new Dom.Clef("treble_8", right);
            new Dom.Clef("bass", left);
            preferUpper = 0;
        }
        else if (lower.Count == 0)
        {
            //A female choir.
            upper = Of('S');
            lower = Of('A');
        }
        else
        {
            new Dom.Clef("bass", left);
        }

        //Without this the accidentals get confusing.
        new Dom.Line("#(set-accidental-style 'piano)", right);
        new Dom.Line("#(set-accidental-style 'piano)", left);

        //Move voices across if they are unevenly spread.
        if (Math.Abs(upper.Count - lower.Count) > 1)
        {
            List<object> all = upper.Concat(lower).ToList();
            int half = (all.Count + preferUpper) / 2;
            upper = all.Take(half).ToList();
            lower = all.Skip(half).ToList();
        }

        foreach ((Dom.Container hand, List<object> parts) in new[]
        {
            ((Dom.Container)new Dom.Simr(right), upper),
            (new Dom.Simr(left), lower),
        })
        {
            if (parts.Count == 0) { continue; }

            foreach (object reference in parts.Take(parts.Count - 1))
            {
                new Dom.Identifier(reference, hand);
                new Dom.VoiceSeparator(hand) { After = 1 };
            }

            new Dom.Identifier(parts[^1], hand);
        }

        //Make the piano part a little smaller.
        new Dom.Line("fontSize = #-1", piano.GetWith());
        new Dom.Line(
            "\\override StaffSymbol.staff-space = #(magstep -1)", piano.GetWith());

        //Marks and metronome marks are nice to have on it.
        new Dom.Line("\\consists \"Mark_engraver\"", rightStaff.GetWith());
        new Dom.Line("\\consists \"Metronome_mark_engraver\"", rightStaff.GetWith());

        //Keep the reduction out of the MIDI output.
        if (builder.Midi)
        {
            new Dom.Line("\\remove \"Staff_performer\"", rightStaff.GetWith());
            new Dom.Line("\\remove \"Staff_performer\"", leftStaff.GetWith());
        }
    }

    /// <summary>Builds one rehearsal MIDI file per voice.</summary>
    /// <param name="data">What the part is building into.</param>
    /// <param name="builder">The builder.</param>
    /// <param name="rehearsalMidis">The voices to make files for.</param>
    /// <param name="references">The lyric assignments, by name and stanza.</param>
    /// <param name="allStanzas">Every stanza.</param>
    private static void BuildRehearsalMidi(
        PartData data,
        ScoreBuilder builder,
        List<(char Voice, int Num, object Reference, string LyricName)> rehearsalMidis,
        Dictionary<(string Name, int Verse), object> references,
        List<int> allStanzas)
    {
        Dom.Assignment assignment = data.Assign("rehearsalMidi");
        object rehearsalMidi = assignment.Name;

        Dom.SchemeList function = new Dom.SchemeList(assignment)
        {
            Pre = "#\n(",       //upstream's own hack, and the reason Pre is settable
        };
        new Dom.Text("define-music-function", function);
        new Dom.Line(
            "(parser location name midiInstrument lyrics) "
            + "(string? string? ly:music?)",
            function);
        Dom.Container choir = new Dom.Sim(
            new Dom.Command("unfoldRepeats", new Dom.SchemeLily(function)));

        data.AfterBlocks.Add(new Dom.Comment(I18n.Get("Rehearsal MIDI files:")));

        foreach ((char voice, int num, object reference, string lyricName) in rehearsalMidis)
        {
            string name = VoiceIds[voice]
                + (num > 0 ? num.ToString(CultureInfo.InvariantCulture) : string.Empty);
            Dom.Seq sequence = new Dom.Seq(
                new Dom.Voice(name, parent: new Dom.Staff(name, parent: choir)));
            if (!builder.LyVersionAtLeast(2, 18, 0))
            {
                new Dom.Text("<>\\f", sequence);    //one dynamic, or it stays silent
            }

            new Dom.Identifier(reference, sequence);

            Dom.Book book = new Dom.Book();

            string suffix = data.Num > 0
                ? string.Create(CultureInfo.InvariantCulture, $"choir{data.Num}-{name}")
                : name;
            if (!builder.LyVersionAtLeast(2, 12, 0))
            {
                data.AfterBlocks.Add(new Dom.Line(
                    "#(define output-suffix \"" + suffix + "\")"));
            }
            else
            {
                new Dom.Line("\\bookOutputSuffix \"" + suffix + "\"", book);
            }

            data.AfterBlocks.Add(book);
            data.AfterBlocks.Add(new Dom.BlankLine());
            Dom.Score score = new Dom.Score(book);

            //TODO (upstream's own): make configurable.
            string midiInstrument = VoiceMidi[voice];

            Dom.Command command = new Dom.Command(rehearsalMidi, score);
            new Dom.QuotedString(name, command);
            new Dom.QuotedString(midiInstrument, command);
            new Dom.Identifier(references[(lyricName, allStanzas[0])], command);
            new Dom.Midi(score);
        }

        new Dom.Text("\\context Staff = $name", choir);
        Dom.Seq settings = new Dom.Seq(choir);
        new Dom.Line("\\set Score.midiMinimumVolume = #0.5", settings);
        new Dom.Line("\\set Score.midiMaximumVolume = #0.5", settings);
        new Dom.Line(
            "\\set Score.tempoWholesPerMinute = #"
            + data.ScoreProperties.SchemeMidiTempo(),
            settings);
        new Dom.Line("\\set Staff.midiMinimumVolume = #0.8", settings);
        new Dom.Line("\\set Staff.midiMaximumVolume = #1.0", settings);
        new Dom.Line("\\set Staff.midiInstrument = $midiInstrument", settings);
        Dom.Lyrics lyricsContext = new Dom.Lyrics(parent: choir);
        lyricsContext.GetWith()["alignBelowContext"] = new Dom.Text("$name");
        new Dom.Text("\\lyricsto $name $lyrics", lyricsContext);
    }
}
