// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Importers;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;

namespace Fresco.Brix.Import; //was previously: frescobaldi/file_import/{toly_dialog,musicxml,midi,abc}.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What the "After Import" tab asks for: the adaptations made to the converted
/// source once it is in a document.
/// </summary>
/// <remarks>
/// Upstream's four <c>postChecks</c>, in its own order — which is the order
/// <c>get_post_settings</c> returns them in and the order
/// <c>FileImport.post_import</c> reads them back in.
/// </remarks>
public sealed class PostImportSettings
{
    /// <summary>The settings key the reformat box remembers itself in.</summary>
    public const string ReformatKey = "reformat";

    /// <summary>The settings key the trim-durations box remembers itself in.</summary>
    public const string TrimDurationsKey = "trim-durations";

    /// <summary>The settings key the remove-scaling box remembers itself in.</summary>
    public const string RemoveScalingKey = "remove-scaling";

    /// <summary>The settings key the engrave box remembers itself in.</summary>
    public const string EngraveDirectlyKey = "engrave-directly";

    /// <summary>The four keys, in upstream's order.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        ReformatKey, TrimDurationsKey, RemoveScalingKey, EngraveDirectlyKey,
    };

    /// <summary>The four defaults, in upstream's order.</summary>
    /// <remarks>Upstream's <c>post_default = [True, False, False, True]</c>.</remarks>
    public static readonly IReadOnlyList<bool> Defaults = new[] { true, false, false, true };

    /// <summary>Gets or sets whether to reformat the imported source.</summary>
    public bool Reformat { get; set; } = true;

    /// <summary>Gets or sets whether to make durations implicit per line.</summary>
    public bool TrimDurations { get; set; }

    /// <summary>Gets or sets whether to remove fraction duration scaling.</summary>
    public bool RemoveScaling { get; set; }

    /// <summary>Gets or sets whether to engrave the result at once.</summary>
    public bool EngraveDirectly { get; set; } = true;

    /// <summary>Gets the four answers, in upstream's order.</summary>
    /// <remarks>Upstream's <c>get_post_settings()</c>.</remarks>
    public IReadOnlyList<bool> Values
        => new[] { Reformat, TrimDurations, RemoveScaling, EngraveDirectly };

    /// <summary>Reads one of the four by position.</summary>
    /// <param name="index">The position, in upstream's order.</param>
    /// <returns>The answer.</returns>
    public bool this[int index] => Values[index];

    /// <summary>Sets one of the four by position.</summary>
    /// <param name="index">The position, in upstream's order.</param>
    /// <param name="value">The answer.</param>
    public void Set(int index, bool value)
    {
        switch (index)
        {
            case 0: Reformat = value; break;
            case 1: TrimDurations = value; break;
            case 2: RemoveScaling = value; break;
            case 3: EngraveDirectly = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>The label each box carries, in upstream's order.</summary>
    /// <returns>The labels.</returns>
    public static IReadOnlyList<string> Texts() => new[]
    {
        I18n.Get("Reformat source"),
        I18n.Get("Trim durations (Make implicit per line)"),
        I18n.Get("Remove fraction duration scaling"),
        I18n.Get("Engrave directly"),
    };
}

/// <summary>
/// What one import dialog holds: the converter's own options as CHECKBOXES,
/// upstream's way round, and the four "After Import" adaptations.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ THE SENSES ARE UPSTREAM'S, IN BOTH DIRECTIONS AND ON PURPOSE. The
/// checkboxes are written the way a user reads them ("Import beaming"), and the
/// options objects are named after the converter's own LONG options with their
/// negative sense kept (<c>NoBeaming</c>) — so the mapping between them inverts
/// five of the six MusicXML boxes, exactly as upstream's <c>configure_job</c>
/// does when it adds <c>--no-beaming</c> for a box that is NOT ticked. Neither
/// side is "helpfully" turned round: a reader of this file can hold upstream's
/// dialog and LilyPond's own <c>--help</c> beside it and check every row.
/// </para>
/// <para>
/// Upstream keeps these in <c>QSettings</c> under a group per format, with the
/// Qt object names as keys; the same group and key names are used here, so the
/// stored settings read the same.
/// </para>
/// </remarks>
public abstract class ImportSettings
{
    /// <summary>Gets the format these settings belong to.</summary>
    public abstract ImportFormat Format { get; }

    /// <summary>Gets the "After Import" answers.</summary>
    public PostImportSettings Post { get; } = new PostImportSettings();

    /// <summary>Gets the settings keys of this format's own boxes, in order.</summary>
    public abstract IReadOnlyList<string> CheckKeys { get; }

    /// <summary>Gets the defaults of this format's own boxes, in order.</summary>
    /// <remarks>Upstream's <c>imp_default</c>.</remarks>
    public abstract IReadOnlyList<bool> CheckDefaults { get; }

    /// <summary>Gets the labels of this format's own boxes, in order.</summary>
    public abstract IReadOnlyList<string> CheckTexts();

    /// <summary>Reads one of this format's boxes by position.</summary>
    /// <param name="index">The position, in upstream's order.</param>
    /// <returns>Whether it is ticked.</returns>
    public abstract bool GetCheck(int index);

    /// <summary>Sets one of this format's boxes by position.</summary>
    /// <param name="index">The position, in upstream's order.</param>
    /// <param name="value">Whether it is ticked.</param>
    public abstract void SetCheck(int index, bool value);

    /// <summary>Makes the settings for a format, at their defaults.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The settings.</returns>
    public static ImportSettings For(ImportFormat format)
        => format switch
        {
            ImportFormat.MusicXml => new MusicXmlImportSettings(),
            ImportFormat.Midi => new MidiImportSettings(),
            _ => new AbcImportSettings(),
        };

    /// <summary>Reads the settings a previous import left behind.</summary>
    /// <param name="format">The format.</param>
    /// <param name="store">The store, or null for the defaults.</param>
    /// <returns>The settings.</returns>
    /// <remarks>Upstream's <c>loadSettings()</c>.</remarks>
    public static ImportSettings Load(ImportFormat format, SettingsStore store)
    {
        ImportSettings settings = For(format);
        settings.Read(store);
        return settings;
    }

    /// <summary>Remembers these settings for the next import.</summary>
    /// <param name="store">The store, or null to remember nothing.</param>
    /// <remarks>Upstream's <c>saveSettings()</c>, which runs when the import
    /// has FINISHED rather than when the dialog is accepted.</remarks>
    public virtual void Save(SettingsStore store)
    {
        if (store == null) { return; }

        string group = ImportFormats.SettingsGroup(Format);
        for (int index = 0; index < CheckKeys.Count; index++)
        {
            store.SetBool(group + "/" + CheckKeys[index], GetCheck(index));
        }

        for (int index = 0; index < PostImportSettings.Keys.Count; index++)
        {
            store.SetBool(group + "/" + PostImportSettings.Keys[index], Post[index]);
        }
    }

    /// <summary>Hands the converter what the boxes say.</summary>
    /// <param name="sourceName">What to call the input in messages.</param>
    /// <returns>The options object, as the importer wants it.</returns>
    public abstract object ToOptions(string sourceName);

    /// <summary>Reads the boxes out of the store.</summary>
    /// <param name="store">The store, or null.</param>
    protected virtual void Read(SettingsStore store)
    {
        string group = ImportFormats.SettingsGroup(Format);
        for (int index = 0; index < CheckKeys.Count; index++)
        {
            SetCheck(
                index,
                store?.GetBool(group + "/" + CheckKeys[index], CheckDefaults[index])
                    ?? CheckDefaults[index]);
        }

        for (int index = 0; index < PostImportSettings.Keys.Count; index++)
        {
            Post.Set(
                index,
                store?.GetBool(
                    group + "/" + PostImportSettings.Keys[index],
                    PostImportSettings.Defaults[index])
                    ?? PostImportSettings.Defaults[index]);
        }
    }
}

/// <summary>The MusicXML dialog's six boxes and its pitch-name language.</summary>
public sealed class MusicXmlImportSettings : ImportSettings
{
    /// <summary>The settings key each box remembers itself in, in order.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "articulation-directions", "rest-positions", "page-layout",
        "import-beaming", "absolute-mode", "comment-out-midi",
    };

    /// <summary>The key the chosen language is remembered in.</summary>
    public const string LanguageKey = "language";

    /// <summary>What the settings hold when no language has been chosen.</summary>
    /// <remarks>Upstream writes the literal string <c>"default"</c>.</remarks>
    public const string DefaultLanguage = "default";

    /// <summary>
    /// The note-name languages <c>musicxml2ly</c> accepts, in upstream's order.
    /// </summary>
    /// <remarks>Upstream's <c>_langlist</c>. The dialog shows a "Default" entry
    /// before them, which is why every index in the settings is one lower than
    /// the one in the list on screen.</remarks>
    public static readonly IReadOnlyList<string> Languages = new[]
    {
        "nederlands", "catalan", "deutsch", "english", "espanol", "italiano",
        "norsk", "portugues", "suomi", "svenska", "vlaams",
    };

    /// <inheritdoc/>
    public override ImportFormat Format => ImportFormat.MusicXml;

    /// <inheritdoc/>
    public override IReadOnlyList<string> CheckKeys => Keys;

    /// <inheritdoc/>
    public override IReadOnlyList<bool> CheckDefaults
        => new[] { false, false, false, false, false, false };

    /// <summary>Gets or sets whether articulation directions are imported.</summary>
    public bool ImportArticulationDirections { get; set; }

    /// <summary>Gets or sets whether exact rest positions are imported.</summary>
    public bool ImportRestPositions { get; set; }

    /// <summary>Gets or sets whether the page layout is imported.</summary>
    public bool ImportPageLayout { get; set; }

    /// <summary>Gets or sets whether the document's beaming is imported.</summary>
    public bool ImportBeaming { get; set; }

    /// <summary>Gets or sets whether pitches are written in absolute mode.</summary>
    public bool AbsoluteMode { get; set; }

    /// <summary>Gets or sets whether the MIDI block is commented out.</summary>
    public bool CommentOutMidi { get; set; }

    /// <summary>
    /// Gets or sets the chosen note-name language, or null for the converter's
    /// own default.
    /// </summary>
    public string Language { get; set; }

    /// <inheritdoc/>
    public override IReadOnlyList<string> CheckTexts() => new[]
    {
        I18n.Get("Import articulation directions"),
        I18n.Get("Import rest positions"),
        I18n.Get("Import page layout"),
        I18n.Get("Import beaming"),
        I18n.Get("Pitches in absolute mode"),
        I18n.Get("Comment out midi block"),
    };

    /// <inheritdoc/>
    public override bool GetCheck(int index)
        => index switch
        {
            0 => ImportArticulationDirections,
            1 => ImportRestPositions,
            2 => ImportPageLayout,
            3 => ImportBeaming,
            4 => AbsoluteMode,
            5 => CommentOutMidi,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

    /// <inheritdoc/>
    public override void SetCheck(int index, bool value)
    {
        switch (index)
        {
            case 0: ImportArticulationDirections = value; break;
            case 1: ImportRestPositions = value; break;
            case 2: ImportPageLayout = value; break;
            case 3: ImportBeaming = value; break;
            case 4: AbsoluteMode = value; break;
            case 5: CommentOutMidi = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>
    /// Gets or sets the language by its position in the dialog's list, where 0
    /// is the "Default" entry.
    /// </summary>
    /// <remarks>Upstream's <c>langCombo.currentIndex()</c>. Kept because the
    /// settings round trip is written in terms of it.</remarks>
    public int LanguageIndex
    {
        get
        {
            int found = Language == null
                ? -1
                : IndexOfLanguage(Language);
            return found + 1;
        }

        set => Language = value > 0 && value <= Languages.Count
            ? Languages[value - 1]
            : null;
    }

    /// <inheritdoc/>
    public override void Save(SettingsStore store)
    {
        base.Save(store);
        store?.SetString(
            ImportFormats.SettingsGroup(Format) + "/" + LanguageKey,
            LanguageIndex == 0 ? DefaultLanguage : Languages[LanguageIndex - 1]);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ FIVE OF THE SIX BOXES INVERT. Upstream adds the converter's
    /// <c>--no-…</c> option when the box saying it WILL be imported is clear,
    /// and the MIDI box is the odd one out again: "Comment out midi block"
    /// ticked means <c>-m</c> is NOT passed.
    /// </remarks>
    public override object ToOptions(string sourceName)
        => new MusicXmlImportOptions
        {
            SourceName = sourceName ?? string.Empty,

            //-a / --absolute, the only positively-sensed box here.
            PitchMode = AbsoluteMode
                ? MusicXmlPitchMode.Absolute
                : MusicXmlPitchMode.Relative,

            //--nd, --nrp, --npl, --no-beaming: added when the box is CLEAR.
            NoArticulationDirections = !ImportArticulationDirections,
            NoRestPositions = !ImportRestPositions,
            NoPageLayout = !ImportPageLayout,
            NoBeaming = !ImportBeaming,

            //-m writes the MIDI block; the box asks for it to be commented out.
            Midi = !CommentOutMidi,

            //--language=…, left unset for the dialog's "Default" entry.
            Language = Language,
        };

    /// <inheritdoc/>
    protected override void Read(SettingsStore store)
    {
        base.Read(store);

        //Upstream looks the stored name up in its list and falls to the
        //"Default" entry when it is not there — which is what the literal
        //`"default"' it writes for that entry does on the way back in.
        string stored = store?.GetString(
            ImportFormats.SettingsGroup(Format) + "/" + LanguageKey, DefaultLanguage)
            ?? DefaultLanguage;
        LanguageIndex = IndexOfLanguage(stored) + 1;
    }

    private static int IndexOfLanguage(string name)
    {
        for (int index = 0; index < Languages.Count; index++)
        {
            if (string.Equals(Languages[index], name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>The MIDI dialog's one box.</summary>
public sealed class MidiImportSettings : ImportSettings
{
    /// <summary>The settings key the box remembers itself in.</summary>
    public static readonly IReadOnlyList<string> Keys = new[] { "absolute-mode" };

    /// <inheritdoc/>
    public override ImportFormat Format => ImportFormat.Midi;

    /// <inheritdoc/>
    public override IReadOnlyList<string> CheckKeys => Keys;

    /// <inheritdoc/>
    public override IReadOnlyList<bool> CheckDefaults => new[] { false };

    /// <summary>Gets or sets whether pitches are written in absolute mode.</summary>
    public bool AbsoluteMode { get; set; }

    /// <inheritdoc/>
    public override IReadOnlyList<string> CheckTexts()
        => new[] { I18n.Get("Pitches in absolute mode") };

    /// <inheritdoc/>
    public override bool GetCheck(int index)
        => index == 0 ? AbsoluteMode : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc/>
    public override void SetCheck(int index, bool value)
    {
        if (index != 0) { throw new ArgumentOutOfRangeException(nameof(index)); }

        AbsoluteMode = value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ ONE BOX, AND UPSTREAM'S OWN CHOICE OF ONE. <c>midi2ly</c> has ten
    /// options; Frescobaldi's dialog offers <c>-a</c> and no other, and this
    /// port offers what the dialog offers. The rest of
    /// <see cref="MidiImportOptions"/> stays at the converter's defaults, which
    /// is what upstream's command line leaves them at.
    /// </remarks>
    public override object ToOptions(string sourceName)
        => new MidiImportOptions
        {
            SourceName = sourceName ?? string.Empty,
            AbsolutePitches = AbsoluteMode,
        };
}

/// <summary>The ABC dialog's one box.</summary>
public sealed class AbcImportSettings : ImportSettings
{
    /// <summary>The settings key the box remembers itself in.</summary>
    public static readonly IReadOnlyList<string> Keys = new[] { "import-beaming" };

    /// <inheritdoc/>
    public override ImportFormat Format => ImportFormat.Abc;

    /// <inheritdoc/>
    public override IReadOnlyList<string> CheckKeys => Keys;

    /// <inheritdoc/>
    /// <remarks>⚠ The one import box in the application that starts TICKED —
    /// upstream's <c>imp_default = [True]</c>.</remarks>
    public override IReadOnlyList<bool> CheckDefaults => new[] { true };

    /// <summary>Gets or sets whether ABC's own beaming is kept.</summary>
    public bool ImportBeaming { get; set; } = true;

    /// <inheritdoc/>
    public override IReadOnlyList<string> CheckTexts()
        => new[] { I18n.Get("Import beaming") };

    /// <inheritdoc/>
    public override bool GetCheck(int index)
        => index == 0 ? ImportBeaming : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc/>
    public override void SetCheck(int index, bool value)
    {
        if (index != 0) { throw new ArgumentOutOfRangeException(nameof(index)); }

        ImportBeaming = value;
    }

    /// <inheritdoc/>
    /// <remarks>The one positively-sensed box of the three dialogs: ticked adds
    /// <c>abc2ly</c>'s <c>-b</c>.</remarks>
    public override object ToOptions(string sourceName)
        => new AbcImportOptions
        {
            SourceName = sourceName ?? string.Empty,
            Beams = ImportBeaming,
        };
}
