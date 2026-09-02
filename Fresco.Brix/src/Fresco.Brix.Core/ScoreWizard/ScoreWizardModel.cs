// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/settings.py + header.py + dialog.py's state

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One row of the score tree the user is assembling.</summary>
public sealed class PartTreeItem
{
    private readonly List<PartTreeItem> _children = new List<PartTreeItem>();

    /// <summary>Initializes the row.</summary>
    /// <param name="part">The part, or null for the invisible root.</param>
    public PartTreeItem(PartBase part = null) => Part = part;

    /// <summary>Gets the part, or null when this is the root.</summary>
    public PartBase Part { get; }

    /// <summary>Gets the row this one sits in, or null.</summary>
    public PartTreeItem Parent { get; private set; }

    /// <summary>Gets the rows inside this one.</summary>
    public IReadOnlyList<PartTreeItem> Children => _children;

    /// <summary>Adds a row inside this one.</summary>
    /// <param name="child">The row.</param>
    /// <param name="index">Where to put it, or -1 for the end.</param>
    /// <returns>The row.</returns>
    public PartTreeItem Add(PartTreeItem child, int index = -1)
    {
        child.Parent?.Remove(child);
        child.Parent = this;
        if (index < 0 || index >= _children.Count)
        {
            _children.Add(child);
        }
        else
        {
            _children.Insert(index, child);
        }

        return child;
    }

    /// <summary>Adds a part inside this row.</summary>
    /// <param name="part">The part.</param>
    /// <returns>The new row.</returns>
    public PartTreeItem Add(PartBase part) => Add(new PartTreeItem(part));

    /// <summary>Removes a row from this one.</summary>
    /// <param name="child">The row.</param>
    public void Remove(PartTreeItem child)
    {
        if (_children.Remove(child)) { child.Parent = null; }
    }

    /// <summary>Empties this row.</summary>
    public void Clear()
    {
        foreach (PartTreeItem child in _children) { child.Parent = null; }

        _children.Clear();
    }

    /// <summary>Moves a row up or down among its siblings.</summary>
    /// <param name="child">The row.</param>
    /// <param name="offset">-1 for up, 1 for down.</param>
    /// <returns>Whether it moved.</returns>
    public bool Move(PartTreeItem child, int offset)
    {
        int index = _children.IndexOf(child);
        int wanted = index + offset;
        if (index < 0 || wanted < 0 || wanted >= _children.Count) { return false; }

        _children.RemoveAt(index);
        _children.Insert(wanted, child);
        return true;
    }

    /// <summary>Walks this row and everything inside it.</summary>
    /// <returns>The rows, this one first.</returns>
    public IEnumerable<PartTreeItem> Descendants()
    {
        yield return this;
        foreach (PartTreeItem child in _children)
        {
            foreach (PartTreeItem item in child.Descendants()) { yield return item; }
        }
    }
}

/// <summary>The wizard's general preferences.</summary>
public sealed class GeneralPreferences
{
    /// <summary>The paper sizes offered, the empty one meaning the default.</summary>
    public static readonly IReadOnlyList<string> PaperSizes = new[]
    {
        string.Empty, "a3", "a4", "a5", "a6", "a7", "legal", "letter", "11x17",
    };

    /// <summary>Initializes the preferences with upstream's defaults.</summary>
    public GeneralPreferences()
    {
        TypographicalQuotes = new BoolSetting("typq", true)
        {
            Label = () => I18n.Get("Use typographical quotes"),
            ToolTip = () => I18n.Get(
                "Replace normal quotes in titles with nice typographical quotes."),
        };
        RelativePitch = new BoolSetting("relpitch", true)
        {
            Label = () => I18n.Get("Use \\relative with pitch"),
            ToolTip = () => I18n.Get(
                "Write a default pitch after the \\relative command."),
        };
        RemoveTagline = new BoolSetting("tagl")
        {
            Label = () => I18n.Get("Remove default tagline"),
            //was previously: "Suppress the default tagline output by
            //LilyPond." A TOOLTIP is chrome, and FR13 names tooltips
            //explicitly; the engine the user drives here is LilyPort, and the
            //sentence says the same thing without naming either. The new msgid
            //is in the harvest tool's renamed-string table.
            ToolTip = () => I18n.Get(
                "Suppress the default tagline in the engraved output."),
        };
        RemoveBarNumbers = new BoolSetting("barnum")
        {
            Label = () => I18n.Get("Remove bar numbers"),
            ToolTip = () => I18n.Get(
                "Suppress the display of measure numbers at the beginning of "
                + "every system."),
        };
        SmartNeutralDirection = new BoolSetting("neutdir")
        {
            Label = () => I18n.Get("Smart neutral stem direction"),
            ToolTip = () => I18n.Get(
                "Use a logical direction (up or down) for stems on the middle "
                + "line of a staff."),
        };
        ShowMetronomeMark = new BoolSetting("metro")
        {
            Label = () => I18n.Get("Show metronome mark"),
            ToolTip = () => I18n.Get(
                "If checked, show the metronome mark at the beginning of the "
                + "score. The MIDI output also uses the metronome setting."),
        };

        List<ChoiceItem> papers = new List<ChoiceItem>
        {
            new ChoiceItem(() => I18n.Get("Default"), string.Empty),
        };
        papers.AddRange(PaperSizes.Skip(1).Select(size => new ChoiceItem(size)));
        Paper = new ChoiceSetting("paper", papers)
        {
            Label = () => I18n.Get("Paper size:"),
        };

        PaperOrientation = new ChoiceSetting(
            "paperOrientation",
            new[]
            {
                new ChoiceItem(
                    () => I18n.Get("Regular"), "regular",
                    () => I18n.Get("Regular portrait orientation")),
                new ChoiceItem(
                    () => I18n.Get("Landscape"), "landscape",
                    () => I18n.Get(
                        "Set paper orientation to landscape while keeping "
                        + "upright printing orientation.")),
                new ChoiceItem(
                    () => I18n.Get("Rotated"), "rotated",
                    () => I18n.Get("Rotate print on regular paper.")),
            })
        {
            Label = () => I18n.Get("Orientation:"),
            IsEnabled = false,
        };

        Paper.Changed += (_, _) => PaperOrientation.IsEnabled = Paper.SelectedIndex > 0;
    }

    /// <summary>Gets whether plain quotes in titles are replaced.</summary>
    public BoolSetting TypographicalQuotes { get; }

    /// <summary>Gets whether <c>\relative</c> is written with a pitch.</summary>
    public BoolSetting RelativePitch { get; }

    /// <summary>Gets whether the default tagline is suppressed.</summary>
    public BoolSetting RemoveTagline { get; }

    /// <summary>Gets whether measure numbers are suppressed.</summary>
    public BoolSetting RemoveBarNumbers { get; }

    /// <summary>Gets whether middle-line stems get a logical direction.</summary>
    public BoolSetting SmartNeutralDirection { get; }

    /// <summary>Gets whether the metronome mark is shown.</summary>
    public BoolSetting ShowMetronomeMark { get; }

    /// <summary>Gets the paper size.</summary>
    public ChoiceSetting Paper { get; }

    /// <summary>Gets the paper orientation.</summary>
    public ChoiceSetting PaperOrientation { get; }

    /// <summary>Gets the settings in the order they are shown.</summary>
    public IReadOnlyList<PartSetting> Settings => new PartSetting[]
    {
        TypographicalQuotes, RelativePitch, RemoveTagline, RemoveBarNumbers,
        SmartNeutralDirection, ShowMetronomeMark, Paper, PaperOrientation,
    };

    /// <summary>Gets the chosen paper size, or the empty string.</summary>
    /// <returns>The size.</returns>
    public string PaperSize()
    {
        int index = Paper.SelectedIndex;
        return index >= 0 && index < PaperSizes.Count ? PaperSizes[index] : string.Empty;
    }

    /// <summary>Reads the preferences from the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Load(SettingsStore settings)
    {
        if (settings == null) { return; }

        const string group = "scorewiz/preferences/";
        TypographicalQuotes.Value = settings.GetBool(group + "typographical_quotes", true);
        RelativePitch.Value = settings.GetBool(group + "relative_pitch", true);
        RemoveTagline.Value = settings.GetBool(group + "remove_tagline", false);
        RemoveBarNumbers.Value = settings.GetBool(group + "remove_barnumbers", false);
        SmartNeutralDirection.Value =
            settings.GetBool(group + "smart_neutral_direction", false);
        ShowMetronomeMark.Value = settings.GetBool(group + "metronome_mark", false);

        string paperSize = settings.GetString(group + "paper_size", string.Empty);
        int index = PaperSizes.ToList().IndexOf(paperSize ?? string.Empty);
        Paper.SelectedIndex = index > 0 ? index : 0;
        PaperOrientation.SelectedIndex = settings.GetInt(group + "paper_rotation", 0);
        PaperOrientation.IsEnabled = Paper.SelectedIndex > 0;
    }

    /// <summary>Writes the preferences to the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { return; }

        const string group = "scorewiz/preferences/";
        settings.SetBool(group + "typographical_quotes", TypographicalQuotes.Value);
        settings.SetBool(group + "relative_pitch", RelativePitch.Value);
        settings.SetBool(group + "remove_tagline", RemoveTagline.Value);
        settings.SetBool(group + "remove_barnumbers", RemoveBarNumbers.Value);
        settings.SetBool(group + "smart_neutral_direction", SmartNeutralDirection.Value);
        settings.SetBool(group + "metronome_mark", ShowMetronomeMark.Value);
        settings.SetString(group + "paper_size", PaperSize());
        settings.SetInt(group + "paper_rotation", Math.Max(0, PaperOrientation.SelectedIndex));
    }
}

/// <summary>Whether and how instrument names are printed.</summary>
public sealed class InstrumentNamesPreferences
{
    private static readonly string[] Allowed = { "long", "short", "none" };

    /// <summary>Initializes the preferences with upstream's defaults.</summary>
    public InstrumentNamesPreferences()
    {
        Group = new GroupSetting("instrumentNames", isCheckable: true, isChecked: true)
        {
            Label = () => I18n.Get("Instrument names"),
        };
        FirstSystem = Group.Add(new ChoiceSetting("firstSystem", LengthItems(), 0)
        {
            Label = () => I18n.Get("First system:"),
            ToolTip = () => I18n.Get(
                "Use long or short instrument names before the first system."),
        });
        OtherSystems = Group.Add(new ChoiceSetting("otherSystems", LengthItems(), 2)
        {
            Label = () => I18n.Get("Other systems:"),
            ToolTip = () => I18n.Get(
                "Use short, long or no instrument names before the next systems."),
        });
        Language = Group.Add(new ChoiceSetting("language", LanguageItems(), 0)
        {
            Label = () => I18n.Get("Language:"),
            ToolTip = () => I18n.Get(
                "Which language to use for the instrument names."),
        });
    }

    /// <summary>Gets the group, whose tick turns the names off entirely.</summary>
    public GroupSetting Group { get; }

    /// <summary>Gets what is printed before the first system.</summary>
    public ChoiceSetting FirstSystem { get; }

    /// <summary>Gets what is printed before the other systems.</summary>
    public ChoiceSetting OtherSystems { get; }

    /// <summary>Gets which language the names are written in.</summary>
    public ChoiceSetting Language { get; }

    /// <summary>Gets whether instrument names are printed at all.</summary>
    public bool IsEnabled => Group.IsChecked;

    /// <summary>Gets the chosen language code, empty for the default.</summary>
    /// <returns>The code.</returns>
    public string LanguageCode() => Language.SelectedTag as string ?? string.Empty;

    /// <summary>Reads the preferences from the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Load(SettingsStore settings)
    {
        if (settings == null) { return; }

        const string group = "scorewiz/instrumentnames/";
        Group.IsChecked = settings.GetBool(group + "enabled", true);
        FirstSystem.SelectedIndex = IndexOf(settings.GetString(group + "first"), 0);
        OtherSystems.SelectedIndex = IndexOf(settings.GetString(group + "other"), 2);

        string language = settings.GetString(group + "language", string.Empty);
        for (int index = 0; index < Language.Items.Count; index++)
        {
            if (string.Equals(
                Language.Items[index].Tag as string, language, StringComparison.Ordinal))
            {
                Language.SelectedIndex = index;
                break;
            }
        }
    }

    /// <summary>Writes the preferences to the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { return; }

        const string group = "scorewiz/instrumentnames/";
        settings.SetBool(group + "enabled", Group.IsChecked);
        settings.SetString(group + "first", Allowed[Math.Max(0, FirstSystem.SelectedIndex)]);
        settings.SetString(group + "other", Allowed[Math.Max(0, OtherSystems.SelectedIndex)]);
        settings.SetString(group + "language", LanguageCode());
    }

    /// <summary>Answers the index of a stored name length.</summary>
    /// <param name="value">The stored value.</param>
    /// <param name="fallback">What to answer when it names nothing.</param>
    /// <returns>The index.</returns>
    private static int IndexOf(string value, int fallback)
    {
        int index = Array.IndexOf(Allowed, value ?? string.Empty);
        return index < 0 ? fallback : index;
    }

    /// <summary>Answers the long/short/none rows.</summary>
    /// <returns>The rows.</returns>
    private static IEnumerable<ChoiceItem> LengthItems() => new[]
    {
        new ChoiceItem(() => I18n.Get("Long"), "long"),
        new ChoiceItem(() => I18n.Get("Short"), "short"),
        new ChoiceItem(() => I18n.Get("None"), null),
    };

    /// <summary>Answers the language rows.</summary>
    /// <returns>The rows.</returns>
    /// <remarks>Upstream lists every language it is translated into; until
    /// W-I18N brings the catalogs there are two honest answers, and a third
    /// would be a list of languages that all produce English.</remarks>
    private static IEnumerable<ChoiceItem> LanguageItems() => new[]
    {
        new ChoiceItem(() => I18n.Get("Default"), string.Empty),
        new ChoiceItem(() => I18n.Get("English (untranslated)"), "C"),
    };
}

/// <summary>Whether the score also produces a MIDI file.</summary>
public sealed class MidiOutputPreferences
{
    /// <summary>Initializes the preferences with upstream's defaults.</summary>
    public MidiOutputPreferences()
    {
        Group = new GroupSetting("midiOutput", isCheckable: true, isChecked: true)
        {
            Label = () => I18n.Get("Create MIDI output"),
            ToolTip = () => I18n.Get("Create a MIDI file in addition to the PDF file."),
        };
        SeparateScore = Group.Add(new BoolSetting("separateScore")
        {
            Label = () => I18n.Get("Place in a separate \\score block"),
            ToolTip = () => I18n.Get("Create a separate \\score block for MIDI output."),
        });
    }

    /// <summary>Gets the group, whose tick turns MIDI output off.</summary>
    public GroupSetting Group { get; }

    /// <summary>Gets whether the MIDI goes in a <c>\score</c> of its own.</summary>
    public BoolSetting SeparateScore { get; }

    /// <summary>Gets whether MIDI output is wanted.</summary>
    public bool IsEnabled => Group.IsChecked;

    /// <summary>Reads the preferences from the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Load(SettingsStore settings)
    {
        if (settings == null) { return; }

        //Upstream keeps these in the general preferences group for backwards
        //compatibility; the port keeps its key layout.
        Group.IsChecked = settings.GetBool("scorewiz/preferences/midi", true);
        SeparateScore.Value = settings.GetBool("scorewiz/preferences/separateMidi", false);
    }

    /// <summary>Writes the preferences to the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { return; }

        settings.SetBool("scorewiz/preferences/midi", Group.IsChecked);
        settings.SetBool("scorewiz/preferences/separateMidi", SeparateScore.Value);
    }
}

/// <summary>The engine-facing preferences: the document's pitch language.</summary>
/// <remarks>
/// //was previously: <c>LilyPondPreferences</c>, whose second half was a
/// version chooser. FR5.1 leaves one engine compiled in, so there is no version
/// to choose: the group shows which one it is (FR13's two version rows) and
/// the document is written for it.
/// </remarks>
public sealed class EnginePreferences
{
    /// <summary>Initializes the preferences.</summary>
    public EnginePreferences()
    {
        List<ChoiceItem> languages = new List<ChoiceItem>
        {
            new ChoiceItem(() => I18n.Get("Default"), string.Empty),
        };
        languages.AddRange(ScoreProperties.PitchLanguages.Select(
            language => new ChoiceItem(
                () => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(language),
                language)));
        PitchLanguage = new ChoiceSetting("pitchLanguage", languages)
        {
            Label = () => I18n.Get("Pitch name language:"),
            //was previously: "The LilyPond language you want to use for the
            //pitch names." FR13: a tooltip is chrome. The thing chosen is a
            //pitch-name language, and the label already says so.
            ToolTip = () => I18n.Get(
                "The language you want to use for the pitch names."),
        };
    }

    /// <summary>Gets which language the pitch names are written in.</summary>
    public ChoiceSetting PitchLanguage { get; }

    /// <summary>Gets the chosen language, or the empty string for the default.</summary>
    /// <returns>The language.</returns>
    public string PitchLanguageCode() => PitchLanguage.SelectedTag as string ?? string.Empty;

    /// <summary>Reads the preferences from the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Load(SettingsStore settings)
    {
        if (settings == null) { return; }

        string language = settings.GetString(
            "scorewiz/lilypond/pitch_language", string.Empty);
        for (int index = 0; index < PitchLanguage.Items.Count; index++)
        {
            if (string.Equals(
                PitchLanguage.Items[index].Tag as string,
                language,
                StringComparison.Ordinal))
            {
                PitchLanguage.SelectedIndex = index;
                return;
            }
        }

        PitchLanguage.SelectedIndex = 0;
    }

    /// <summary>Writes the preferences to the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Save(SettingsStore settings)
        => settings?.SetString("scorewiz/lilypond/pitch_language", PitchLanguageCode());
}

/// <summary>
/// Everything the Score Wizard knows: the titles, the settings and the tree of
/// parts. The builder reads one of these and needs nothing else.
/// </summary>
public sealed class ScoreWizardModel
{
    private readonly Dictionary<string, string> _headers =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Initializes an empty wizard.</summary>
    public ScoreWizardModel()
    {
        Version = LilyPortEngine.CompatibleWithVersion;
        GeneralPreferences.Paper.Changed += (_, _) => { };
        EnginePreferences.PitchLanguage.Changed += (_, _) => ApplyPitchLanguage();
    }

    /// <summary>Gets the header fields, in the order they are shown.</summary>
    public static IReadOnlyList<(string Name, Func<string> Title)> HeaderFields { get; } =
        new (string, Func<string>)[]
        {
            ("dedication", () => I18n.Get("Dedication")),
            ("title", () => I18n.Get("Title")),
            ("subtitle", () => I18n.Get("Subtitle")),
            ("subsubtitle", () => I18n.Get("Subsubtitle")),
            ("instrument", () => I18n.Get("Instrument")),
            ("composer", () => I18n.Get("Composer")),
            ("arranger", () => I18n.Get("Arranger")),
            ("poet", () => I18n.Get("Poet")),
            ("meter", () => I18n.Get("Meter")),
            ("piece", () => I18n.Get("Piece")),
            ("opus", () => I18n.Get("Opus")),
            ("copyright", () => I18n.Get("Copyright")),
            ("tagline", () => I18n.Get("Tagline")),
        };

    /// <summary>Gets the score's own key, time and tempo settings.</summary>
    public ScoreProperties ScoreProperties { get; } = new ScoreProperties();

    /// <summary>Gets the general preferences.</summary>
    public GeneralPreferences GeneralPreferences { get; } = new GeneralPreferences();

    /// <summary>Gets the instrument-name preferences.</summary>
    public InstrumentNamesPreferences InstrumentNames { get; } =
        new InstrumentNamesPreferences();

    /// <summary>Gets the MIDI output preferences.</summary>
    public MidiOutputPreferences MidiOutput { get; } = new MidiOutputPreferences();

    /// <summary>Gets the engine-facing preferences.</summary>
    public EnginePreferences EnginePreferences { get; } = new EnginePreferences();

    /// <summary>Gets the root of the score tree.</summary>
    public PartTreeItem Root { get; } = new PartTreeItem();

    /// <summary>
    /// Gets or sets the LilyPond release the document is written for.
    /// </summary>
    /// <remarks>
    /// This is the one value upstream's version combo box chose between. FR5.1
    /// compiles one engine in, so it comes from
    /// <see cref="LilyPortEngine.CompatibleWithVersion"/> and no C# in this
    /// repository writes the number itself (FR13). It is settable because the
    /// version-conditional code the port carries has to be testable at the
    /// versions that make it branch.
    /// </remarks>
    public string Version { get; set; }

    /// <summary>Gets the pitch language chosen for the document.</summary>
    public string PitchLanguage => EnginePreferences.PitchLanguageCode();

    /// <summary>Gets a header field's text.</summary>
    /// <param name="name">The field.</param>
    /// <returns>The text, or the empty string.</returns>
    public string Header(string name)
        => _headers.TryGetValue(name, out string value) ? value : string.Empty;

    /// <summary>Sets a header field's text.</summary>
    /// <param name="name">The field.</param>
    /// <param name="value">The text.</param>
    public void SetHeader(string name, string value)
        => _headers[name] = value ?? string.Empty;

    /// <summary>Gets the header fields that have something in them.</summary>
    /// <returns>The name and text of each, in display order.</returns>
    public IEnumerable<(string Name, string Value)> Headers()
    {
        foreach ((string name, _) in HeaderFields)
        {
            string text = Header(name).Trim();
            if (text.Length > 0) { yield return (name, text); }
        }
    }

    /// <summary>Empties every header field.</summary>
    public void ClearHeaders() => _headers.Clear();

    /// <summary>
    /// Tells the score properties — the wizard's own and every Score part's —
    /// which language to name keys in.
    /// </summary>
    public void ApplyPitchLanguage()
    {
        string language = PitchLanguage;
        ScoreProperties.PitchLanguage = language;
        foreach (PartTreeItem item in Root.Descendants())
        {
            if (item.Part is Score score) { score.Properties.PitchLanguage = language; }
        }
    }

    /// <summary>Finds a setting by its dotted key, as the fixtures name it.</summary>
    /// <param name="path">The key, e.g. <c>generalPreferences.metro</c>.</param>
    /// <returns>The setting, or null when nothing has that key.</returns>
    public PartSetting FindSetting(string path)
    {
        if (string.IsNullOrEmpty(path)) { return null; }

        string[] steps = path.Split('.');
        IReadOnlyList<PartSetting> settings = steps[0] switch
        {
            "scoreProperties" => ScoreProperties.Settings,
            "generalPreferences" => GeneralPreferences.Settings,
            "instrumentNames" => new PartSetting[] { InstrumentNames.Group },
            "midiOutput" => new PartSetting[] { MidiOutput.Group },
            "lilyPondPreferences" => new PartSetting[]
            {
                EnginePreferences.PitchLanguage,
            },
            _ => null,
        };

        if (settings == null) { return null; }

        if (steps.Length == 1)
        {
            //`instrumentNames` and `midiOutput` name their own group box.
            return settings.Count == 1 && settings[0] is GroupSetting group
                ? group
                : null;
        }

        return Find(settings, steps[1]);
    }

    /// <summary>Finds a setting by key, looking inside groups.</summary>
    /// <param name="settings">Where to look.</param>
    /// <param name="key">The key.</param>
    /// <returns>The setting, or null.</returns>
    public static PartSetting Find(IEnumerable<PartSetting> settings, string key)
    {
        foreach (PartSetting setting in settings)
        {
            if (string.Equals(setting.Key, key, StringComparison.Ordinal))
            {
                return setting;
            }

            if (setting is GroupSetting group)
            {
                PartSetting found = Find(group.Children, key);
                if (found != null) { return found; }
            }
        }

        return null;
    }

    /// <summary>Reads every preference from the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Load(SettingsStore settings)
    {
        GeneralPreferences.Load(settings);
        InstrumentNames.Load(settings);
        MidiOutput.Load(settings);
        EnginePreferences.Load(settings);
        ScoreProperties.MetronomeRound.Value =
            settings?.GetBool("scorewiz/scoreproperties/round_metronome", true) ?? true;
        ApplyPitchLanguage();
    }

    /// <summary>Writes every preference to the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Save(SettingsStore settings)
    {
        GeneralPreferences.Save(settings);
        InstrumentNames.Save(settings);
        MidiOutput.Save(settings);
        EnginePreferences.Save(settings);
        settings?.SetBool(
            "scorewiz/scoreproperties/round_metronome", ScoreProperties.MetronomeRound.Value);
    }
}
