// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.DocumentFonts;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The font-command composer against Frescobaldi's OWN
/// <c>fonts/fontcommand.py</c>: <c>fixtures/fonts/fontcommand.json</c> holds
/// the commands upstream's widget generated for these 28 scenarios
/// (regenerate with <c>tools/fontprobe/gen-font-fixtures.py</c>). Nothing here
/// is recorded from the port's own output.
/// </summary>
/// <remarks>
/// The two approaches are checked differently and deliberately:
/// <list type="bullet">
/// <item>the openLilyLib command is upstream's CHARACTER FOR CHARACTER, because
/// its syntax belongs to the <c>notation-fonts</c> package and nothing about it
/// changed when LilyPond dropped <c>set-global-fonts</c>;</item>
/// <item>the plain LilyPond command is the wave's one declared FR14 divergence,
/// so the fixture is left exactly as Frescobaldi produced it and
/// <see cref="Translate"/> says what this port writes instead, and why.</item>
/// </list>
/// </remarks>
public class FontCommandTests : IDisposable
{
    private readonly string _directory;

    /// <summary>Creates the fixture over a scratch database file.</summary>
    public FontCommandTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-fontcmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Removes the scratch directory.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private string StorePath => _directory;

    /// <summary>
    /// What the port writes in place of upstream's <c>set-global-fonts</c>
    /// form, and why.
    /// </summary>
    /// <remarks>
    /// ⚠ FR14. Frescobaldi 4.0.7 generates
    /// <c>#(define fonts (set-global-fonts …))</c>, and
    /// <c>set-global-fonts</c> was removed from LilyPond at 2.25.4 — so the
    /// command it writes is an unbound variable against the engine this
    /// application engraves with. The replacement is the four paper variables
    /// <c>paper-defaults-init.ly</c> itself sets.
    /// <para>
    /// This is expressed as a TRANSLATION of the fixture rather than as a
    /// second recorded expectation, so upstream's own logic — which of the five
    /// families the tick boxes include, and whether the <c>\paper</c> wrapper
    /// is written — stays the oracle. Only the SYNTAX is ours.
    /// </para>
    /// <para>
    /// A fixture that stops containing <c>set-global-fonts</c> FAILS the test
    /// rather than passing quietly, so a regenerated fixture in which upstream
    /// has fixed its bug says so out loud.
    /// </para>
    /// </remarks>
    public const string DivergenceReason =
        "Frescobaldi 4.0.7's Document Fonts dialog writes "
        + "#(define fonts (set-global-fonts ...)), which LilyPond removed at "
        + "2.25.4; the port writes property-defaults.fonts.{music,serif,sans,"
        + "typewriter} instead, and drops #:brace (the engine derives the brace "
        + "font from the music family's own name) and #:factor (an argument of "
        + "set-global-fonts, with no counterpart).";

    /// <summary>Gets the scenario names, one test case each.</summary>
    public static IEnumerable<object[]> Scenarios
        => Fixture().EnumerateArray()
            .Select(scenario => new object[]
            {
                scenario.GetProperty("name").GetString(),
            });

    /// <summary>
    /// The openLilyLib command matches Frescobaldi's character for character.
    /// </summary>
    /// <param name="name">The scenario.</param>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void openlilylib_command_matches_frescobaldi(string name)
    {
        //Arrange
        JsonElement scenario = Scenario(name);
        (FontSelection fonts, FontCommandOptions options) = StateFor(scenario);

        //Act
        FontCommandText generated = FontCommand.GenerateOll(fonts, options);

        //Assert
        generated.Command.Should().Be(scenario.GetProperty("ollCommand").GetString());
        generated.Full.Should().Be(scenario.GetProperty("ollFullCommand").GetString());
    }

    /// <summary>
    /// The plain LilyPond command is upstream's, translated into the syntax
    /// that still exists.
    /// </summary>
    /// <param name="name">The scenario.</param>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void lilypond_command_is_frescobaldis_in_the_long_form(string name)
    {
        //Arrange
        JsonElement scenario = Scenario(name);
        (FontSelection fonts, FontCommandOptions options) = StateFor(scenario);
        string upstream = scenario.GetProperty("lilyCommand").GetString();

        //Act
        FontCommandText generated = FontCommand.GenerateLily(fonts, options);

        //Assert
        generated.Command.Should().Be(Translate(upstream));
        generated.Full.Should().Be(
            Translate(scenario.GetProperty("lilyFullCommand").GetString()));
    }

    /// <summary>
    /// Every recorded scenario still holds the dead syntax the divergence is
    /// about.
    /// </summary>
    [Fact]
    public void the_fixture_still_records_upstreams_dead_syntax()
    {
        //Arrange
        //Act
        List<string> commands = Fixture().EnumerateArray()
            .Select(scenario => scenario.GetProperty("lilyCommand").GetString())
            .ToList();

        //Assert
        commands.Should().NotBeEmpty();
        foreach (string command in commands)
        {
            command.Should().Contain("set-global-fonts", DivergenceReason);
        }
    }

    /// <summary>The port never writes the short form (board trap 67).</summary>
    [Fact]
    public void the_short_font_name_is_never_written()
    {
        //Arrange
        FontSelection fonts = new FontSelection();
        FontCommandOptions options = new FontCommandOptions
        {
            SetMusic = true,
            SetRoman = true,
            SetSans = true,
            SetTypewriter = true,
        };

        //Act
        string command = FontCommand.GenerateLily(fonts, options).Command;

        //Assert
        //The short `fonts.serif = ' form is a convert-ly way-station and a
        //silent no-op; every assignment has to carry the long prefix.
        foreach (string line in command.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.Contains('=')) { continue; }

            trimmed.Should().StartWith("property-defaults.fonts.");
        }

        command.Should().Contain("property-defaults.fonts.serif = \"TeXGyre Schola\"");
        command.Should().NotContain("set-global-fonts");
        command.Should().NotContain("property-defaults.fonts.roman");
        command.Should().NotContain("property-defaults.fonts.brace");
    }

    /// <summary>
    /// The typewriter tick is remembered, which upstream's own code loses.
    /// </summary>
    /// <remarks>⚠ FR14: <c>fontcommand.py</c> reads and writes
    /// <c>set-roman</c> twice and never touches <c>set-typewriter</c>.</remarks>
    [Fact]
    public void every_tick_survives_a_round_trip_through_the_settings()
    {
        //Arrange
        using Services.SettingsStore settings = new Services.SettingsStore(StorePath);
        FontCommandOptions saved = new FontCommandOptions
        {
            SetMusic = false,
            SetRoman = true,
            SetSans = false,
            SetTypewriter = true,
            SetPaperBlock = false,
            Approach = FontCommandApproach.OpenLilyLib,
            LoadOll = false,
            LoadPackage = false,
            FontExtensions = true,
            StyleType = 2,
            FontStylesheet = "jazz.ily",
        };

        //Act
        saved.Save(settings);
        FontCommandOptions loaded = new FontCommandOptions();
        loaded.Load(settings);

        //Assert
        loaded.SetMusic.Should().BeFalse();
        loaded.SetRoman.Should().BeTrue();
        loaded.SetSans.Should().BeFalse();
        loaded.SetTypewriter.Should().BeTrue();
        loaded.SetPaperBlock.Should().BeFalse();
        loaded.Approach.Should().Be(FontCommandApproach.OpenLilyLib);
        loaded.LoadOll.Should().BeFalse();
        loaded.LoadPackage.Should().BeFalse();
        loaded.FontExtensions.Should().BeTrue();
        loaded.StyleType.Should().Be(2);
        loaded.FontStylesheet.Should().Be("jazz.ily");
    }

    /// <summary>The five chosen fonts round-trip, and Restore puts them back.</summary>
    [Fact]
    public void the_chosen_fonts_round_trip_and_restore()
    {
        //Arrange
        using Services.SettingsStore settings = new Services.SettingsStore(StorePath);
        FontSelection fonts = new FontSelection();

        //Act
        fonts["music"] = "lilyjazz";
        fonts["roman"] = "C059";
        fonts.Save(settings);

        FontSelection loaded = new FontSelection();
        loaded.Load(settings);

        //Assert
        loaded["music"].Should().Be("lilyjazz");
        loaded["roman"].Should().Be("C059");
        loaded["sans"].Should().Be("TeXGyre Heros");

        loaded.Restore();
        foreach (string family in FontSelection.Families)
        {
            loaded[family].Should().Be(FontSelection.Defaults[family]);
        }
    }

    /// <summary>
    /// Translates upstream's <c>set-global-fonts</c> command into the form the
    /// port writes, so upstream's own filtering logic stays the oracle.
    /// </summary>
    /// <param name="upstream">What Frescobaldi wrote.</param>
    /// <returns>What this port writes in its place.</returns>
    private static string Translate(string upstream)
    {
        upstream.Should().Contain("set-global-fonts", DivergenceReason);

        //`roman' became `serif' in the same release that removed
        //set-global-fonts; `brace' and `factor' have no counterpart at all.
        Dictionary<string, string> properties = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["music"] = "music",
            ["roman"] = "serif",
            ["sans"] = "sans",
            ["typewriter"] = "typewriter",
        };

        List<string> definitions = new List<string>();
        foreach (string line in upstream.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("#:", StringComparison.Ordinal)) { continue; }

            int space = trimmed.IndexOf(' ');
            if (space < 0) { continue; }

            string keyword = trimmed.Substring(2, space - 2);
            if (!properties.TryGetValue(keyword, out string property)) { continue; }

            definitions.Add(
                "  property-defaults.fonts." + property + " = "
                + trimmed.Substring(space + 1));
        }

        string command = string.Join("\n", definitions);
        bool wrapped = upstream.StartsWith("\\paper {", StringComparison.Ordinal);
        return wrapped ? "\\paper {\n" + command + "\n}" : command;
    }

    /// <summary>Rebuilds a scenario's state from the fixture.</summary>
    /// <param name="scenario">The scenario.</param>
    /// <returns>The fonts and the options.</returns>
    private static (FontSelection, FontCommandOptions) StateFor(JsonElement scenario)
    {
        FontSelection fonts = new FontSelection();
        JsonElement chosen = scenario.GetProperty("fonts");
        foreach (string family in FontSelection.Families)
        {
            fonts[family] = chosen.GetProperty(family).GetString();
        }

        JsonElement recorded = scenario.GetProperty("options");
        FontCommandOptions options = new FontCommandOptions
        {
            SetMusic = recorded.GetProperty("set-music").GetBoolean(),
            SetRoman = recorded.GetProperty("set-roman").GetBoolean(),
            SetSans = recorded.GetProperty("set-sans").GetBoolean(),
            SetTypewriter = recorded.GetProperty("set-typewriter").GetBoolean(),
            SetPaperBlock = recorded.GetProperty("set-paper-block").GetBoolean(),
            Approach = recorded.GetProperty("approach-index").GetInt32() == 1
                ? FontCommandApproach.OpenLilyLib
                : FontCommandApproach.Lily,
            LoadOll = recorded.GetProperty("load-oll").GetBoolean(),
            LoadPackage = recorded.GetProperty("load-package").GetBoolean(),
            FontExtensions = recorded.GetProperty("font-extensions").GetBoolean(),
            StyleType = recorded.GetProperty("style-type").GetInt32(),
            FontStylesheet = recorded.GetProperty("font-stylesheet").GetString(),
        };

        return (fonts, options);
    }

    /// <summary>Reads one scenario.</summary>
    /// <param name="name">The scenario.</param>
    /// <returns>The scenario.</returns>
    private static JsonElement Scenario(string name)
        => Fixture().EnumerateArray()
            .First(scenario => scenario.GetProperty("name").GetString() == name);

    /// <summary>Reads the fixture file.</summary>
    /// <returns>The scenarios.</returns>
    private static JsonElement Fixture()
    {
        JsonDocument fixture = JsonDocument.Parse(
            File.ReadAllText(FontFixtures.Path("fontcommand.json")));
        return fixture.RootElement;
    }
}

/// <summary>Where the document-font fixtures live.</summary>
internal static class FontFixtures
{
    /// <summary>Answers a fixture's path.</summary>
    /// <param name="name">The file.</param>
    /// <returns>The path.</returns>
    internal static string Path(string name)
        => System.IO.Path.Combine(
            AppContext.BaseDirectory, "fixtures", "fonts", name);
}
