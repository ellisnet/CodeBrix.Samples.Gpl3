// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Pitching;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/pitch/pitch.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What changing a document's pitch-name language did.</summary>
public enum LanguageChange
{
    /// <summary>The pitches were translated and the document's own
    /// <c>\language</c> or <c>\include</c> command was updated with them.</summary>
    Changed,

    /// <summary>The whole document was translated and a language command was
    /// written into it, because it had none.</summary>
    CommandInserted,

    /// <summary>Only a selection was translated, and it carried no language
    /// command — so the user has to add one by hand.</summary>
    CommandNeeded,

    /// <summary>Nothing was translated: the music has quarter tones and the
    /// wanted language cannot spell them.</summary>
    NotAvailable,
}

/// <summary>
/// The modes the Mode Shift command offers, each a scale as a list of
/// (step, alteration) pairs.
/// </summary>
/// <remarks>
/// Upstream keeps this table in its mode-shift DIALOG. Here it is beside the
/// rest of the pitch logic, so the scales can be tested and the transposer
/// built without a window; the dialog reads <see cref="Names"/> for its list.
/// The table itself is upstream's, entry for entry.
/// </remarks>
public static class PitchModes //was previously: frescobaldi/pitch/dialog.py
{
    private static readonly Dictionary<string, (int Step, Fraction Alter)[]> Modes
        = new Dictionary<string, (int, Fraction)[]>(StringComparer.Ordinal)
        {
            ["Major"] = new[]
            {
                (0, F(0)), (1, F(1)), (2, F(2)), (3, F(5, 2)), (4, F(7, 2)),
                (5, F(9, 2)), (6, F(11, 2)),
            },
            ["Minor (harmonic)"] = new[]
            {
                (0, F(0)), (1, F(1)), (2, F(3, 2)), (3, F(5, 2)), (4, F(7, 2)),
                (5, F(4)), (6, F(11, 2)),
            },
            ["Minor (natural)"] = new[]
            {
                (0, F(0)), (1, F(1)), (2, F(3, 2)), (3, F(5, 2)), (4, F(7, 2)),
                (5, F(4)), (6, F(5)),
            },
            ["Dorian"] = new[]
            {
                (0, F(0)), (1, F(1)), (2, F(3, 2)), (3, F(5, 2)), (4, F(7, 2)),
                (5, F(9, 2)), (6, F(5)),
            },
            ["Phrygian"] = new[]
            {
                (0, F(0)), (1, F(1, 2)), (2, F(3, 2)), (3, F(5, 2)), (4, F(7, 2)),
                (5, F(4)), (6, F(5)),
            },
            ["Lydian"] = new[]
            {
                (0, F(0)), (1, F(1)), (2, F(2)), (3, F(3)), (4, F(7, 2)),
                (5, F(9, 2)), (6, F(11, 2)),
            },
            ["Mixolydian"] = new[]
            {
                (0, F(0)), (1, F(1)), (2, F(2)), (3, F(5, 2)), (4, F(7, 2)),
                (5, F(9, 2)), (6, F(5)),
            },
            ["Locrian"] = new[]
            {
                (0, F(0)), (1, F(1, 2)), (2, F(3, 2)), (3, F(5, 2)), (4, F(3)),
                (5, F(4)), (6, F(5)),
            },
            ["Phrygian dominant"] = new[]
            {
                (0, F(0)), (1, F(1, 2)), (2, F(2)), (3, F(5, 2)), (4, F(7, 2)),
                (5, F(4)), (6, F(5)),
            },
            ["Hungarian minor"] = new[]
            {
                (0, F(0)), (1, F(1)), (2, F(3, 2)), (3, F(3)), (4, F(7, 2)),
                (5, F(4)), (6, F(11, 2)),
            },
            ["Double harmonic major"] = new[]
            {
                (0, F(0)), (1, F(1, 2)), (2, F(2)), (3, F(5, 2)), (4, F(7, 2)),
                (5, F(4)), (6, F(11, 2)),
            },
            ["Persian"] = new[]
            {
                (0, F(0)), (1, F(1, 2)), (2, F(2)), (3, F(5, 2)), (4, F(3)),
                (5, F(4)), (6, F(11, 2)),
            },
            ["Diminished (octatonic)"] = new[]
            {
                (0, F(0)), (1, F(1)), (2, F(3, 2)), (3, F(5, 2)), (4, F(3)),
                (5, F(4)), (5, F(9, 2)), (6, F(11, 2)),
            },
            ["Whole tone (hexatonic)"] = new[]
            {
                (0, F(0)), (1, F(1)), (2, F(2)), (3, F(3)), (4, F(4)), (6, F(5)),
            },
            ["Yo (pentatonic)"] = new[]
            {
                (0, F(0)), (1, F(1)), (3, F(5, 2)), (4, F(7, 2)), (6, F(5)),
            },
        };

    /// <summary>Gets the mode names, in the order the dialog lists them.</summary>
    /// <remarks>Upstream sorts them, so the list starts at Diminished and ends
    /// at Yo rather than starting at Major.</remarks>
    public static IReadOnlyList<string> Names { get; }
        = Modes.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>Gets a mode's scale, or null when the name is not one.</summary>
    /// <param name="name">The mode name.</param>
    /// <returns>The scale.</returns>
    public static IReadOnlyList<(int Step, Fraction Alter)> Scale(string name)
        => name != null && Modes.TryGetValue(name, out var scale) ? scale : null;

    private static Fraction F(long numerator, long denominator = 1)
        => new Fraction(numerator, denominator);
}

/// <summary>
/// The commands that change the pitches of the music: the pitch-name language,
/// relative and absolute, and the four transpositions.
/// </summary>
/// <remarks>
/// Every one of them works over a whole document unless there is a selection,
/// which is what <c>select_all</c> means where upstream builds its cursor.
/// The reading and validating half is here rather than in the dialogs, so a
/// test can ask what "d f" means in <c>nederlands</c> without a window.
/// </remarks>
public static class PitchTools
{
    //Upstream's readpitches: a run of letters, then any number of octave marks.
    private static readonly Regex PitchText = new Regex(
        @"([a-z]+)([,']*)", RegexOptions.Compiled);

    /// <summary>The default pitch-name language, as upstream picks it.</summary>
    public const string DefaultLanguage = "nederlands";

    /// <summary>The settings key the first-pitch-absolute preference lives under.</summary>
    public const string FirstPitchAbsoluteKey = "pitch-menu/relative-first-pitch-absolute";

    /// <summary>The settings key the write-startpitch preference lives under.</summary>
    public const string WriteStartPitchKey = "pitch-menu/relative-write-startpitch";

    /// <summary>Gets the pitch-name language a document is written in.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The language; <c>nederlands</c> when the document says nothing.</returns>
    public static string LanguageOf(EditorDocument document)
    {
        if (document == null) { return DefaultLanguage; }

        string language = DocumentInfo.For(document).DocInfo().Language();
        return string.IsNullOrEmpty(language) ? DefaultLanguage : language;
    }

    /// <summary>
    /// Answers whether the first pitch of a <c>\relative</c> expression with no
    /// start pitch counts as absolute.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="preferenceChecked">Whether the user ticked the menu
    /// entry that always assumes it.</param>
    /// <returns>Whether it does.</returns>
    /// <remarks>Upstream's <c>get_absolute</c>: the preference forces it, and
    /// otherwise the document's own version decides, because LilyPond changed
    /// this at 2.18.</remarks>
    public static bool FirstPitchAbsolute(
        EditorDocument document, bool preferenceChecked)
    {
        if (preferenceChecked) { return true; }

        if (document == null) { return false; }

        int[] version = DocumentInfo.For(document).DocInfo().Version();
        return version is { Length: > 0 }
            && (version[0] > 2
                || (version[0] == 2 && version.Length > 1 && version[1] >= 18));
    }

    /// <summary>Reads the pitches a user typed into a dialog.</summary>
    /// <param name="text">What they typed.</param>
    /// <param name="language">The pitch-name language to read in.</param>
    /// <returns>The pitches; the ones that are not pitch names are skipped.</returns>
    public static IReadOnlyList<Pitch> ReadPitches(string text, string language)
    {
        List<Pitch> pitches = new List<Pitch>();
        if (string.IsNullOrEmpty(text)) { return pitches; }

        PitchReader reader = Pitches.PitchReaderFor(language ?? DefaultLanguage);
        foreach (Match match in PitchText.Matches(text))
        {
            if (!reader.TryRead(match.Groups[1].Value, out int note, out Fraction alter))
            {
                continue;
            }

            pitches.Add(new Pitch(
                note, alter, Pitches.OctaveToNum(match.Groups[2].Value)));
        }

        return pitches;
    }

    /// <summary>Answers whether the text names exactly two pitches.</summary>
    /// <param name="text">The text.</param>
    /// <param name="language">The pitch-name language.</param>
    /// <returns>Whether it does.</returns>
    public static bool IsTransposeInput(string text, string language)
        => ReadPitches(text, language).Count == 2;

    /// <summary>Builds the transposer for a "from to" pair of pitches.</summary>
    /// <param name="text">The text the user typed.</param>
    /// <param name="language">The pitch-name language.</param>
    /// <returns>The transposer, or null when the text is not two pitches.</returns>
    public static Transposer TransposerFor(string text, string language)
    {
        IReadOnlyList<Pitch> pitches = ReadPitches(text, language);
        return pitches.Count == 2 ? new Transposer(pitches[0], pitches[1]) : null;
    }

    /// <summary>
    /// Answers whether the text is a number of steps followed by a key.
    /// </summary>
    /// <param name="text">The text, such as <c>5 F</c>.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsModalTransposeInput(string text)
        => ModalTransposerFor(text) != null;

    /// <summary>Builds the modal transposer for "steps key".</summary>
    /// <param name="text">The text the user typed, such as <c>5 F</c>.</param>
    /// <returns>The transposer, or null when the text is not that.</returns>
    public static ModalTransposer ModalTransposerFor(string text)
    {
        string[] words = (text ?? string.Empty).Split(
            (char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 2) { return null; }

        if (!int.TryParse(
                words[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                out int steps))
        {
            return null;
        }

        try
        {
            return new ModalTransposer(steps, ModalTransposer.GetKeyIndex(words[1]));
        }
        catch (ArgumentException)
        {
            //Upstream's ValueError: the second word is not a key name.
            return null;
        }
    }

    /// <summary>Answers whether the text names exactly one key pitch.</summary>
    /// <param name="text">The text.</param>
    /// <param name="language">The pitch-name language.</param>
    /// <returns>Whether it does.</returns>
    /// <remarks>Upstream lower-cases the text first, so <c>Bes</c> is a key.</remarks>
    public static bool IsModeShiftKey(string text, string language)
        => ReadPitches((text ?? string.Empty).ToLowerInvariant(), language).Count == 1;

    /// <summary>Builds the mode shifter for a key and a mode.</summary>
    /// <param name="key">The key the user typed.</param>
    /// <param name="modeName">The mode they chose.</param>
    /// <param name="language">The pitch-name language.</param>
    /// <returns>The shifter, or null when the key or the mode is not one.</returns>
    public static ModeShifter ModeShifterFor(
        string key, string modeName, string language)
    {
        IReadOnlyList<Pitch> pitches
            = ReadPitches((key ?? string.Empty).ToLowerInvariant(), language);
        IReadOnlyList<(int Step, Fraction Alter)> scale = PitchModes.Scale(modeName);
        return pitches.Count == 1 && scale != null
            ? new ModeShifter(pitches[0], scale)
            : null;
    }

    /// <summary>Changes the pitch-name language of a document or a selection.</summary>
    /// <param name="document">The document.</param>
    /// <param name="language">The language to write the pitches in.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="end">Where it ends; equal to <paramref name="start"/> for
    /// no selection.</param>
    /// <returns>What happened, which is what the window reports.</returns>
    public static LanguageChange ChangeLanguage(
        EditorDocument document, string language, int start, int end)
    {
        if (document == null) { return LanguageChange.NotAvailable; }

        Cursor cursor = CursorFor(document, start, end);
        bool hasSelection = end > start;

        try
        {
            if (Translating.Translate(cursor, language)) { return LanguageChange.Changed; }
        }
        catch (PitchNameNotAvailableException)
        {
            return LanguageChange.NotAvailable;
        }

        if (hasSelection)
        {
            //The pitches changed, but the command that says which language
            //they are in is somewhere the selection did not reach.
            return LanguageChange.CommandNeeded;
        }

        Translating.InsertLanguage(
            cursor.Document,
            language,
            DocumentInfo.For(document).DocInfo().Version());
        return LanguageChange.CommandInserted;
    }

    /// <summary>Converts relative pitches to absolute.</summary>
    /// <param name="document">The document.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="firstPitchAbsolute">Whether a start-pitch-less
    /// <c>\relative</c> begins at f rather than c'.</param>
    public static void RelativeToAbsolute(
        EditorDocument document, int start, int end, bool firstPitchAbsolute)
    {
        if (document == null) { return; }

        Rel2Abs.Convert(
            CursorFor(document, start, end),
            firstPitchAbsolute: firstPitchAbsolute);
    }

    /// <summary>Converts absolute pitches to relative.</summary>
    /// <param name="document">The document.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="writeStartPitch">Whether to write a start pitch after
    /// <c>\relative</c>.</param>
    /// <param name="firstPitchAbsolute">Whether a start-pitch-less
    /// <c>\relative</c> begins at f rather than c'.</param>
    public static void AbsoluteToRelative(
        EditorDocument document,
        int start,
        int end,
        bool writeStartPitch,
        bool firstPitchAbsolute)
    {
        if (document == null) { return; }

        Abs2Rel.Convert(
            CursorFor(document, start, end),
            startPitch: writeStartPitch,
            firstPitchAbsolute: firstPitchAbsolute);
    }

    /// <summary>Transposes the music with a transposer.</summary>
    /// <param name="document">The document.</param>
    /// <param name="transposer">The transposer.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="firstPitchAbsolute">Whether a start-pitch-less
    /// <c>\relative</c> begins at f rather than c'.</param>
    /// <returns>Null when it worked, or the pitch-name language that cannot
    /// spell what the transposition produced.</returns>
    public static string Transpose(
        EditorDocument document,
        TransposerBase transposer,
        int start,
        int end,
        bool firstPitchAbsolute = false)
    {
        if (document == null || transposer == null) { return null; }

        try
        {
            Transposing.Transpose(
                CursorFor(document, start, end),
                transposer,
                relativeFirstPitchAbsolute: firstPitchAbsolute);
            return null;
        }
        catch (PitchNameNotAvailableException error)
        {
            return error.Language;
        }
    }

    /// <summary>
    /// Makes the cursor a pitch command works over: the selection when there
    /// is one, the whole document when there is not.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <returns>The cursor.</returns>
    /// <remarks>Upstream's <c>lydocument.cursor(cursor, select_all=True)</c>:
    /// the pitch tools always have something to work on.</remarks>
    private static Cursor CursorFor(EditorDocument document, int start, int end)
    {
        AteLyDocument text = DocumentEditorState.For(document).LyDocument;
        return end > start
            ? new Cursor(text, start, end)
            : new Cursor(text, 0, document.Text.Length);
    }
}

/// <summary>The Pitch menu's commands.</summary>
public sealed class PitchActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "pitch";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public PitchActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the pitch-name language command, whose menu lists the
    /// languages.</summary>
    public AppAction PitchLanguage { get; private set; }

    /// <summary>Gets the relative-to-absolute command.</summary>
    public AppAction PitchRel2Abs { get; private set; }

    /// <summary>Gets the absolute-to-relative command.</summary>
    public AppAction PitchAbs2Rel { get; private set; }

    /// <summary>Gets the transpose command.</summary>
    public AppAction PitchTranspose { get; private set; }

    /// <summary>Gets the modal transpose command.</summary>
    public AppAction PitchModalTranspose { get; private set; }

    /// <summary>Gets the mode shift command.</summary>
    public AppAction PitchModeShift { get; private set; }

    /// <summary>Gets the simplify-accidentals command.</summary>
    public AppAction PitchSimplify { get; private set; }

    /// <summary>Gets the "assume the first pitch is absolute" preference.</summary>
    public AppAction PitchRelativeAssumeFirstPitchAbsolute { get; private set; }

    /// <summary>Gets the "write \relative with a start pitch" preference.</summary>
    public AppAction PitchRelativeWriteStartPitch { get; private set; }

    /// <summary>Gets the pitch-name languages, in the order the menu lists them.</summary>
    /// <remarks>Upstream sorts <c>ly.pitch.pitchInfo</c>'s keys, so the list
    /// runs catalan … vlaams rather than in the table's own order.</remarks>
    public static IReadOnlyList<string> Languages { get; }
        = Pitches.Languages.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <inheritdoc/>
    public override string Title => I18n.Get("Pitch");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        PitchLanguage = Add("pitch_language").WithIcon("tools-pitch-language");
        PitchRel2Abs = Add("pitch_rel2abs");
        PitchAbs2Rel = Add("pitch_abs2rel");
        PitchTranspose = Add("pitch_transpose").WithIcon("tools-transpose");
        PitchModalTranspose = Add("pitch_modal_transpose").WithIcon("tools-transpose");
        PitchModeShift = Add("pitch_mode_shift").WithIcon("tools-transpose");
        PitchSimplify = Add("pitch_simplify").WithIcon("tools-transpose");
        PitchRelativeAssumeFirstPitchAbsolute
            = Add("pitch_relative_assume_first_pitch_absolute").AsToggle();
        PitchRelativeWriteStartPitch
            = Add("pitch_relative_write_startpitch").AsToggle(true);
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        PitchLanguage.Text = I18n.Get("Pitch Name &Language");
        //was previously: "Change the LilyPond language used for pitch names
        //in this document or in the selection." FR13: a tooltip is chrome, and
        //no chrome names LilyPond. W-I18N: a Fresco.Brix-original msgid, for
        //the harvest tool's renamed-string table.
        PitchLanguage.ToolTip = I18n.Get(
            "Change the LilyPort language used for pitch names "
            + "in this document or in the selection.");
        PitchRel2Abs.Text = I18n.Get("Convert Relative to &Absolute");
        PitchRel2Abs.ToolTip = I18n.Get(
            "Converts the notes in the document or selection from relative to "
            + "absolute pitch.");
        PitchAbs2Rel.Text = I18n.Get("Convert Absolute to &Relative");
        PitchAbs2Rel.ToolTip = I18n.Get(
            "Converts the notes in the document or selection from absolute to "
            + "relative pitch.");
        PitchTranspose.Text = I18n.Get("&Transpose...");
        PitchTranspose.ToolTip = I18n.Get(
            "Transposes all notes in the document or selection.");
        PitchModalTranspose.Text = I18n.Get("&Modal Transpose...");
        PitchModalTranspose.ToolTip = I18n.Get(
            "Transposes all notes in the document or selection within a given mode.");
        PitchModeShift.Text = I18n.Get("Mode shift...");
        PitchModeShift.ToolTip = I18n.Get(
            "Transforms all notes in the document or selection to an optional mode.");
        PitchSimplify.Text = I18n.Get("Simplify Accidentals");
        PitchSimplify.ToolTip = I18n.Get(
            "Replaces notes with accidentals as much as possible with natural neighbors.");
        PitchRelativeAssumeFirstPitchAbsolute.Text
            = I18n.Get("First pitch in \\relative {...} is absolute");
        //was previously: "... Otherwise, Frescobaldi only assumes this when the
        //LilyPond version is >= 2.18." Two names in one sentence that this
        //application may not present as its own (standing rule 5) or name at
        //all (FR13); the version meant is the DOCUMENT's, which is also what
        //the code reads. W-I18N: a Fresco.Brix-original msgid.
        //(2.18 stays a literal: it is the LilyPond release that changed this
        //rule, a boundary in the GRAMMAR like ly.pitch's own 2.13.38 test —
        //not the engine version FR13 keeps in one declaration.)
        PitchRelativeAssumeFirstPitchAbsolute.ToolTip = I18n.Get(
            "If checked, always assume that the first pitch of a \\relative {...}\n"
            + "expression without startpitch is absolute. Otherwise, Fresco.Brix\n"
            + "only assumes this when the document's version is >= 2.18.");
        PitchRelativeWriteStartPitch.Text
            = I18n.Get("Write \\relative with startpitch");
        PitchRelativeWriteStartPitch.ToolTip = I18n.Get(
            "If checked, when converting absolute music to relative, a startpitch\n"
            + "is added. Otherwise, no starting pitch is written.");
    }
}
