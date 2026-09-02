// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Importers;
using Fresco.Brix.Import;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// File &gt; Import against Frescobaldi's OWN <c>file_import/</c> package: the
/// extension table, every dialog's boxes and their defaults, THE MAPPING from
/// those boxes to the converter's options, the settings round trip and
/// <c>util.next_file</c>.
/// </summary>
/// <remarks>
/// <para>
/// The converters themselves are NOT verified here and are not this port's to
/// verify: <c>musicxml2ly</c>, <c>midi2ly</c> and <c>abc2ly</c> are
/// <c>CodeBrix.LilyPort.Importers</c>, checked against the same corpora on the
/// LilyPort board. What is verified is the ADAPTATION LAYER — and in
/// particular the part of it with the inverted senses in it, where a box
/// reading "Import beaming" that is NOT ticked is what adds
/// <c>--no-beaming</c>.
/// </para>
/// <para>
/// The oracle is <c>tools/importprobe/gen-import-fixtures.py</c>, which runs
/// upstream's own <c>configure_job()</c> under
/// <c>tools/scorewizprobe/qtshim.py</c> (board trap 46) with a recording stand-in
/// for <c>job.Job</c>, and records the argument list every combination of boxes
/// produces. The comparison here builds this port's options object for the same
/// combination and renders it back into upstream's argument list, in upstream's
/// own order — one line per member, so a reader can check the equivalence by
/// eye.
/// </para>
/// </remarks>
public class FileImportParityTests
{
    /// <summary>
    /// The only differences from upstream this port declares, so a silent drift
    /// fails rather than passes.
    /// </summary>
    /// <remarks>⚠ RULING FR14. Upstream gives its MusicXML dialog a window
    /// title and its MIDI and ABC dialogs none — <c>midi.py</c> and
    /// <c>abc.py</c> never call <c>setWindowTitle</c>. Two of three sibling
    /// dialogs coming up untitled is an oversight, and this platform's dialog
    /// would draw a blank title band, so the two are given titles in upstream's
    /// own shape.</remarks>
    private static readonly IReadOnlyDictionary<string, string> DeclaredTitleDifferences
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["midi"] = "Import Midi",
            ["abc"] = "Import abc",
        };

    /// <summary>Upstream's extension table is this port's.</summary>
    [Fact]
    public void the_extension_table_is_upstreams()
    {
        //Arrange
        JsonElement targets = ImportFixtures.Root.GetProperty("targets");
        Dictionary<string, string> expected = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (JsonProperty entry in targets.EnumerateObject())
        {
            expected[entry.Name] = entry.Value.GetString();
        }

        //Act
        Dictionary<string, string> actual = ImportFormats.Targets.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                ImportFormat.MusicXml => ".musicxml",
                ImportFormat.Midi => ".midi",
                _ => ".abc",
            },
            StringComparer.Ordinal);

        //Assert — upstream maps an extension to the MODULE that handles it, and
        //the three module names are `.musicxml', `.midi' and `.abc'.
        actual.Should().Equal(expected);
    }

    /// <summary>
    /// A file is importable exactly when upstream says it is — which is
    /// case-sensitively, and never for a name that is nothing but a suffix.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="expected">Whether upstream imports it.</param>
    [Theory]
    [MemberData(nameof(ImportableNames))]
    public void a_file_is_importable_when_upstream_says_so(string name, bool expected)
    {
        //Act
        bool actual = ImportFormats.IsImportable(name);

        //Assert
        actual.Should().Be(expected, name);
    }

    /// <summary>
    /// <c>util.next_file</c> answers what upstream's own answers, oddities and
    /// all.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="expected">Upstream's answer.</param>
    [Theory]
    [MemberData(nameof(NextFileNames))]
    public void next_file_answers_what_upstream_answers(string name, string expected)
    {
        //Act
        string actual = PathUtil.NextFile(name);

        //Assert
        actual.Should().Be(expected, name);
    }

    /// <summary>Every dialog's boxes are upstream's, in upstream's order.</summary>
    /// <param name="format">The dialog.</param>
    [Theory]
    [MemberData(nameof(Formats))]
    public void the_boxes_are_upstreams(string format)
    {
        //Arrange
        JsonElement fixture = ImportFixtures.Dialog(format);
        ImportSettings settings = ImportSettings.For(FormatOf(format));

        //Act & Assert
        settings.CheckKeys.Should().Equal(ImportFixtures.Strings(fixture, "import_checks"));
        settings.CheckTexts().Should().Equal(ImportFixtures.Strings(fixture, "import_texts"));
        settings.CheckDefaults.Should().Equal(ImportFixtures.Bools(fixture, "import_defaults"));
        ImportFormats.ConverterName(FormatOf(format)).Should()
            .Be(fixture.GetProperty("imp_program").GetString());
        ImportFormats.HelpPage(FormatOf(format)).Should()
            .Be(fixture.GetProperty("userguide_page").GetString());
        ImportDialog.AcceptTextFor(FormatOf(format)).Should()
            .Be(fixture.GetProperty("ok_text").GetString());
    }

    /// <summary>
    /// The "After Import" tab is upstream's four boxes, in upstream's order,
    /// with upstream's defaults.
    /// </summary>
    /// <param name="format">The dialog.</param>
    [Theory]
    [MemberData(nameof(Formats))]
    public void the_after_import_tab_is_upstreams(string format)
    {
        //Arrange
        JsonElement fixture = ImportFixtures.Dialog(format);

        //Act & Assert
        PostImportSettings.Keys.Should().Equal(ImportFixtures.Strings(fixture, "post_checks"));
        PostImportSettings.Texts().Should().Equal(ImportFixtures.Strings(fixture, "post_texts"));
        PostImportSettings.Defaults.Should()
            .Equal(ImportFixtures.Bools(fixture, "post_defaults"));
    }

    /// <summary>
    /// ⚠ THE MAPPING. For every combination of boxes upstream was driven
    /// through, this port's options object says exactly what upstream's command
    /// line said.
    /// </summary>
    /// <param name="format">The dialog.</param>
    /// <param name="index">Which recorded combination.</param>
    [Theory]
    [MemberData(nameof(MappingCases))]
    public void the_options_say_what_upstreams_command_line_said(string format, int index)
    {
        //Arrange
        JsonElement fixture = ImportFixtures.Dialog(format);
        JsonElement recorded = fixture.GetProperty("cases")[index];
        ImportFormat which = FormatOf(format);
        ImportSettings settings = ImportSettings.For(which);

        IReadOnlyList<bool> checks = ImportFixtures.Bools(recorded, "checks");
        for (int box = 0; box < checks.Count; box++)
        {
            settings.SetCheck(box, checks[box]);
        }

        int language = recorded.GetProperty("language_index").GetInt32();
        if (settings is MusicXmlImportSettings musicXml && language >= 0)
        {
            musicXml.LanguageIndex = language;
        }

        //Act
        IReadOnlyList<string> arguments = ArgumentsOf(settings.ToOptions("song.xml"));

        //Assert
        arguments.Should().Equal(ImportFixtures.Strings(recorded, "arguments"));
    }

    /// <summary>
    /// <c>get_post_settings</c> answers the four boxes in upstream's order.
    /// </summary>
    /// <param name="format">The dialog.</param>
    /// <param name="index">Which recorded combination.</param>
    [Theory]
    [MemberData(nameof(PostCases))]
    public void the_post_settings_are_read_in_upstreams_order(string format, int index)
    {
        //Arrange
        JsonElement recorded = ImportFixtures.Dialog(format).GetProperty("post_cases")[index];
        PostImportSettings post = new PostImportSettings();
        IReadOnlyList<bool> checks = ImportFixtures.Bools(recorded, "checks");
        for (int box = 0; box < checks.Count; box++)
        {
            post.Set(box, checks[box]);
        }

        //Act
        IReadOnlyList<bool> values = post.Values;

        //Assert
        values.Should().Equal(ImportFixtures.Bools(recorded, "settings"));
    }

    /// <summary>
    /// The settings are written under upstream's own keys and read back the way
    /// upstream reads them.
    /// </summary>
    /// <param name="format">The dialog.</param>
    [Theory]
    [MemberData(nameof(Formats))]
    public void the_settings_round_trip_the_way_upstreams_do(string format)
    {
        //Arrange — the probe ticked every box the other way from its default,
        //ticked all four post boxes, and chose the LAST language.
        JsonElement fixture = ImportFixtures.Dialog(format);
        ImportFormat which = FormatOf(format);
        ImportSettings settings = ImportSettings.For(which);
        for (int box = 0; box < settings.CheckKeys.Count; box++)
        {
            settings.SetCheck(box, !settings.CheckDefaults[box]);
        }

        for (int box = 0; box < PostImportSettings.Keys.Count; box++)
        {
            settings.Post.Set(box, true);
        }

        if (settings is MusicXmlImportSettings musicXml)
        {
            musicXml.LanguageIndex = MusicXmlImportSettings.Languages.Count;
        }

        string directory = Directory.CreateTempSubdirectory("frescoimport").FullName;
        using SettingsStore store
            = new SettingsStore(directory);

        //Act
        settings.Save(store);
        ImportSettings reloaded = ImportSettings.Load(which, store);

        //Assert — the keys upstream writes, with the values it writes.
        foreach (JsonProperty saved in fixture.GetProperty("saved_settings").EnumerateObject())
        {
            if (saved.Value.ValueKind == JsonValueKind.String)
            {
                store.GetString(saved.Name).Should().Be(saved.Value.GetString(), saved.Name);
            }
            else
            {
                store.GetBool(saved.Name, !saved.Value.GetBoolean()).Should()
                    .Be(saved.Value.GetBoolean(), saved.Name);
            }
        }

        //...and what a fresh dialog then shows.
        IReadOnlyList<bool> expectedChecks
            = ImportFixtures.Bools(fixture, "reloaded_import_checks");
        for (int box = 0; box < expectedChecks.Count; box++)
        {
            reloaded.GetCheck(box).Should().Be(expectedChecks[box]);
        }

        IReadOnlyList<bool> expectedPost
            = ImportFixtures.Bools(fixture, "reloaded_post_checks");
        reloaded.Post.Values.Should().Equal(expectedPost);

        if (reloaded is MusicXmlImportSettings reloadedMusicXml)
        {
            reloadedMusicXml.LanguageIndex.Should()
                .Be(fixture.GetProperty("reloaded_language_index").GetInt32());
        }
    }

    /// <summary>The pitch-name language list is upstream's, in its order.</summary>
    [Fact]
    public void the_language_list_is_upstreams()
    {
        //Act & Assert
        MusicXmlImportSettings.Languages.Should()
            .Equal(ImportFixtures.Strings(ImportFixtures.Root, "languages"));
    }

    /// <summary>
    /// The dialog titles are upstream's except for the two this port declares.
    /// </summary>
    /// <param name="format">The dialog.</param>
    [Theory]
    [MemberData(nameof(Formats))]
    public void the_declared_title_differences_are_these_and_no_others(string format)
    {
        //Arrange
        string upstream = ImportFixtures.Dialog(format).GetProperty("window_title")
            .GetString();

        //Act
        string ours = ImportDialog.TitleFor(FormatOf(format));

        //Assert
        if (DeclaredTitleDifferences.TryGetValue(format, out string declared))
        {
            //⚠ FR14: upstream has no title here at all.
            upstream.Should().BeEmpty();
            ours.Should().Be(declared);
        }
        else
        {
            ours.Should().Be(upstream);
        }
    }

    /// <summary>The three dialogs.</summary>
    /// <returns>Their names in the fixture.</returns>
    public static TheoryData<string> Formats()
        => new TheoryData<string> { "musicxml", "midi", "abc" };

    /// <summary>Every recorded mapping case.</summary>
    /// <returns>The dialog and the case index.</returns>
    public static TheoryData<string, int> MappingCases()
        => CasesOf("cases");

    /// <summary>Every recorded "After Import" case.</summary>
    /// <returns>The dialog and the case index.</returns>
    public static TheoryData<string, int> PostCases()
        => CasesOf("post_cases");

    /// <summary>The names upstream was asked about.</summary>
    /// <returns>The name and its answer.</returns>
    public static TheoryData<string, bool> ImportableNames()
    {
        TheoryData<string, bool> data = new TheoryData<string, bool>();
        foreach (JsonProperty entry
            in ImportFixtures.Root.GetProperty("importable").EnumerateObject())
        {
            data.Add(entry.Name, entry.Value.GetBoolean());
        }

        return data;
    }

    /// <summary>The names <c>next_file</c> was asked about.</summary>
    /// <returns>The name and upstream's answer.</returns>
    public static TheoryData<string, string> NextFileNames()
    {
        TheoryData<string, string> data = new TheoryData<string, string>();
        foreach (JsonProperty entry
            in ImportFixtures.Root.GetProperty("next_file").EnumerateObject())
        {
            data.Add(entry.Name, entry.Value.GetString());
        }

        return data;
    }

    /// <summary>
    /// Renders an options object back into the argument list upstream's
    /// <c>configure_job</c> would have built, in upstream's own order.
    /// </summary>
    /// <param name="options">The options object.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// ⚠ THIS IS THE EQUIVALENCE BEING ASSERTED, written out one member at a
    /// time so it can be read against upstream's three <c>configure_job</c>
    /// bodies and against <c>musicxml2ly --help</c>. It lives in the test
    /// because the application never builds a command line.
    /// </remarks>
    private static IReadOnlyList<string> ArgumentsOf(object options)
    {
        List<string> arguments = new List<string>();
        switch (options)
        {
            case MusicXmlImportOptions musicXml:
                if (musicXml.PitchMode == MusicXmlPitchMode.Absolute)
                {
                    arguments.Add("-a");
                }

                if (musicXml.NoArticulationDirections) { arguments.Add("--nd"); }

                if (musicXml.NoRestPositions) { arguments.Add("--nrp"); }

                if (musicXml.NoPageLayout) { arguments.Add("--npl"); }

                if (musicXml.NoBeaming) { arguments.Add("--no-beaming"); }

                if (musicXml.Midi) { arguments.Add("-m"); }

                if (musicXml.Language != null)
                {
                    arguments.Add("--language=" + musicXml.Language);
                }

                break;

            case MidiImportOptions midi:
                if (midi.AbsolutePitches) { arguments.Add("-a"); }

                break;

            case AbcImportOptions abc:
                if (abc.Beams) { arguments.Add("-b"); }

                break;

            default:
                throw new ArgumentException("unknown options object", nameof(options));
        }

        return arguments;
    }

    private static TheoryData<string, int> CasesOf(string property)
    {
        TheoryData<string, int> data = new TheoryData<string, int>();
        foreach (string format in new[] { "musicxml", "midi", "abc" })
        {
            int count = ImportFixtures.Dialog(format).GetProperty(property).GetArrayLength();
            for (int index = 0; index < count; index++) { data.Add(format, index); }
        }

        return data;
    }

    private static ImportFormat FormatOf(string name)
        => name switch
        {
            "musicxml" => ImportFormat.MusicXml,
            "midi" => ImportFormat.Midi,
            _ => ImportFormat.Abc,
        };
}

/// <summary>The recorded answers of Frescobaldi's own <c>file_import</c>.</summary>
internal static class ImportFixtures
{
    private static JsonDocument _fixture;

    /// <summary>Gets the whole fixture.</summary>
    internal static JsonElement Root
        => (_fixture ??= JsonDocument.Parse(File.ReadAllText(
            System.IO.Path.Combine(
                AppContext.BaseDirectory, "fixtures", "import", "file_import.json"))))
            .RootElement;

    /// <summary>Gets one dialog's record.</summary>
    /// <param name="name">The dialog.</param>
    /// <returns>The record.</returns>
    internal static JsonElement Dialog(string name)
        => Root.GetProperty("dialogs").GetProperty(name);

    /// <summary>Reads an array of strings.</summary>
    /// <param name="element">The object holding it.</param>
    /// <param name="property">The property.</param>
    /// <returns>The strings.</returns>
    internal static IReadOnlyList<string> Strings(JsonElement element, string property)
        => element.GetProperty(property).EnumerateArray()
            .Select(item => item.GetString()).ToList();

    /// <summary>Reads an array of booleans.</summary>
    /// <param name="element">The object holding it.</param>
    /// <param name="property">The property.</param>
    /// <returns>The booleans.</returns>
    internal static IReadOnlyList<bool> Bools(JsonElement element, string property)
        => element.GetProperty(property).EnumerateArray()
            .Select(item => item.GetBoolean()).ToList();
}
