// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.ScoreWizard;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The Score Wizard against Frescobaldi's OWN score wizard:
/// <c>fixtures/scorewiz/scorewiz.json</c> holds the LilyPond text that
/// <c>scorewiz/build.py</c> and every <c>scorewiz/parts/*.py</c> produced when
/// they were run over these scenarios (regenerate with
/// <c>tools/scorewizprobe/gen-scorewiz-fixtures.py</c>). Nothing here is
/// recorded from the port's own output.
/// </summary>
/// <remarks>
/// The part types ARE Qt widgets upstream, so there is no pure half to lift
/// out by AST the way board trap 21 does: the probe shims PyQt6 instead and
/// runs the real code. A scenario names its parts by upstream's class name and
/// its settings by upstream's widget attribute name, which are exactly the
/// port's type names and setting keys — so a fixture replays here directly.
/// </remarks>
public class ScoreWizardParityTests
{
    /// <summary>
    /// The scenarios where the port DELIBERATELY does not match upstream,
    /// because upstream is wrong there (ruling FR14).
    /// </summary>
    /// <remarks>
    /// The fixture is left exactly as Frescobaldi produced it, so the oracle
    /// goes on telling the truth about upstream; this table says what the port
    /// writes instead, and why. Each entry is a scenario name, the text
    /// upstream wrote, and the text this port writes in its place.
    /// <para>
    /// An entry that no longer applies FAILS the test rather than passing
    /// quietly, so a regenerated fixture in which upstream has fixed its bug
    /// says so out loud.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyList<(string Scenario, string Upstream, string Ours, string Why)>
        KnownDivergences = new[]
        {
            ("banjo-four-strings",
             "(four-string-banjo banjo-modal-tuning)",
             "(four-string-banjo banjo-c-tuning)",
             "Banjo.setTunings indexes its tuning list by the COMBO's index, "
             + "where the rest of the class subtracts the \"Default\" row first, "
             + "so upstream tunes a four-string banjo one row further down the "
             + "list than the tuning the user picked."),
        };

    /// <summary>Gets the scenario names, one test case each.</summary>
    public static IEnumerable<object[]> Scenarios
        => Fixtures().EnumerateArray()
            .Select(scenario => new object[] { scenario.GetProperty("name").GetString() });

    /// <summary>
    /// Builds each scenario and compares the LilyPond text character for
    /// character with what Frescobaldi wrote.
    /// </summary>
    /// <param name="name">The scenario.</param>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void builder_writes_the_same_document_as_frescobaldi(string name)
    {
        JsonElement scenario = Scenario(name);
        ScoreWizardModel model = ModelFor(scenario);

        ScoreBuilder builder = new ScoreBuilder(model);
        Dom.Document document = builder.Document();
        if (!model.GeneralPreferences.RelativePitch.Value)
        {
            RemoveRelativePitches(document);
        }

        builder.Text(document)
            .Should().Be(Expected(name, scenario.GetProperty("text").GetString()));
    }

    /// <summary>
    /// Fills each scenario with example music and compares that text too.
    /// </summary>
    /// <param name="name">The scenario.</param>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void preview_fills_in_the_same_example_music(string name)
    {
        JsonElement scenario = Scenario(name);
        ScoreWizardModel model = ModelFor(scenario);

        ScoreBuilder builder = new ScoreBuilder(model);
        Dom.Document document = builder.Document();
        ScorePreview.Examplify(document);

        builder.Text(document)
            .Should().Be(Expected(name, scenario.GetProperty("previewText").GetString()));
    }

    /// <summary>The catalogue lists the same parts, in the same order.</summary>
    [Fact]
    public void registry_lists_the_same_parts_as_frescobaldi()
    {
        using JsonDocument fixture = JsonDocument.Parse(
            File.ReadAllText(FixturePath("parts.json")));
        JsonElement categories = fixture.RootElement;

        PartRegistry.Categories.Count.Should().Be(categories.GetArrayLength());
        int categoryIndex = 0;
        foreach (JsonElement category in categories.EnumerateArray())
        {
            PartCategory ours = PartRegistry.Categories[categoryIndex++];
            ours.Title().Should().Be(category.GetProperty("title").GetString());

            JsonElement items = category.GetProperty("items");
            ours.Items.Count.Should().Be(items.GetArrayLength());
            int itemIndex = 0;
            foreach (JsonElement item in items.EnumerateArray())
            {
                PartEntry entry = ours.Items[itemIndex++];
                PartBase part = entry.Create();
                entry.Name.Should().Be(item.GetProperty("name").GetString());
                part.Title().Should().Be(item.GetProperty("title").GetString());
                (part.Short() ?? string.Empty)
                    .Should().Be(item.GetProperty("short").GetString());
            }
        }
    }

    /// <summary>
    /// A synth staff with several voices and no name is upstream's own crash;
    /// here it names the voices after the part.
    /// </summary>
    [Fact]
    public void an_unnamed_staff_with_several_voices_names_its_voices_after_the_part()
    {
        //Arrange
        ScoreWizardModel model = new ScoreWizardModel();
        SynthBass part = new SynthBass();
        part.UpperVoices.Value = 0;
        part.LowerVoices.Value = 2;
        model.Root.Add(part);

        //Act
        string text = new ScoreBuilder(model).Text();

        //Assert
        text.Should().Contain("synthBassOne = \\relative");
        text.Should().Contain("synthBassTwo = \\relative");
    }

    /// <summary>
    /// Answers what the PORT should write, given what upstream wrote.
    /// </summary>
    /// <param name="name">The scenario.</param>
    /// <param name="upstream">The text Frescobaldi produced.</param>
    /// <returns>The text to expect, with any declared divergence applied.</returns>
    private static string Expected(string name, string upstream)
    {
        string expected = upstream;
        foreach (var divergence in KnownDivergences)
        {
            if (!string.Equals(divergence.Scenario, name, StringComparison.Ordinal))
            {
                continue;
            }

            //A divergence that no longer applies is a failure, not a pass: it
            //means the fixture changed under it — most happily, because
            //upstream fixed the bug and the divergence can go.
            upstream.Should().Contain(
                divergence.Upstream,
                "the fixture should still hold what upstream wrote (" + divergence.Why + ")");
            expected = expected.Replace(
                divergence.Upstream, divergence.Ours, StringComparison.Ordinal);
        }

        return expected;
    }

    /// <summary>Every declared divergence names a scenario that exists.</summary>
    [Fact]
    public void every_declared_divergence_names_a_real_scenario()
    {
        foreach (var divergence in KnownDivergences)
        {
            Fixtures().EnumerateArray()
                .Any(scenario =>
                    scenario.GetProperty("name").GetString() == divergence.Scenario)
                .Should().BeTrue();
        }
    }

    /// <summary>Reads the fixture file.</summary>
    /// <returns>The scenarios.</returns>
    private static JsonElement Fixtures()
    {
        JsonDocument fixture = JsonDocument.Parse(
            File.ReadAllText(FixturePath("scorewiz.json")));
        return fixture.RootElement;
    }

    /// <summary>Answers a fixture's path.</summary>
    /// <param name="name">The file.</param>
    /// <returns>The path.</returns>
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "scorewiz", name);

    /// <summary>Finds one scenario by name.</summary>
    /// <param name="name">The scenario.</param>
    /// <returns>The scenario.</returns>
    private static JsonElement Scenario(string name)
        => Fixtures().EnumerateArray().First(
            scenario => scenario.GetProperty("name").GetString() == name);

    /// <summary>Builds the model a scenario describes.</summary>
    /// <param name="scenario">The scenario.</param>
    /// <returns>The model.</returns>
    private static ScoreWizardModel ModelFor(JsonElement scenario)
    {
        ScoreWizardModel model = new ScoreWizardModel
        {
            Version = scenario.GetProperty("version").GetString(),
        };

        foreach (JsonProperty header in scenario.GetProperty("header").EnumerateObject())
        {
            model.SetHeader(header.Name, header.Value.GetString());
        }

        AddParts(model.Root, scenario.GetProperty("parts"));

        string language = scenario.GetProperty("pitchLanguage").GetString();
        if (!string.IsNullOrEmpty(language))
        {
            ChoiceSetting pitchLanguage = model.EnginePreferences.PitchLanguage;
            for (int index = 0; index < pitchLanguage.Items.Count; index++)
            {
                if ((pitchLanguage.Items[index].Tag as string) == language)
                {
                    pitchLanguage.SelectedIndex = index;
                    break;
                }
            }
        }

        foreach (JsonProperty setting in scenario.GetProperty("settings").EnumerateObject())
        {
            PartSetting target = model.FindSetting(setting.Name);
            target.Should().NotBeNull();
            Apply(target, setting.Value);
        }

        return model;
    }

    /// <summary>Adds the parts a scenario describes to a tree row.</summary>
    /// <param name="parent">The row.</param>
    /// <param name="parts">The described parts.</param>
    private static void AddParts(PartTreeItem parent, JsonElement parts)
    {
        foreach (JsonElement entry in parts.EnumerateArray())
        {
            string name = entry.GetProperty("part").GetString();
            PartBase part = PartRegistry.Create(name);
            part.Should().NotBeNull();
            PartTreeItem item = parent.Add(part);

            if (entry.TryGetProperty("settings", out JsonElement settings))
            {
                foreach (JsonProperty setting in settings.EnumerateObject())
                {
                    Apply(part, setting.Name, setting.Value);
                }
            }

            if (entry.TryGetProperty("children", out JsonElement children))
            {
                AddParts(item, children);
            }
        }
    }

    /// <summary>Applies one setting to a part, by upstream's widget name.</summary>
    /// <param name="part">The part.</param>
    /// <param name="key">The widget name.</param>
    /// <param name="value">The value.</param>
    private static void Apply(PartBase part, string key, JsonElement value)
    {
        //Upstream's two book-output radio buttons are one either-or setting
        //here, so a fixture that ticks one of them picks its row.
        if (part is Book book
            && (key == "bookOutputFileName" || key == "bookOutputSuffix"))
        {
            if (value.GetBoolean())
            {
                book.OutputMode.SelectedIndex = key == "bookOutputFileName" ? 0 : 1;
            }

            return;
        }

        PartSetting target = ScoreWizardModel.Find(part.Settings, key);
        target.Should().NotBeNull();
        Apply(target, value);
    }

    /// <summary>Applies one value to one setting.</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="value">The value.</param>
    private static void Apply(PartSetting setting, JsonElement value)
    {
        switch (setting)
        {
            case BoolSetting boolean:
                boolean.Value = value.GetBoolean();
                return;
            case GroupSetting group:
                group.IsChecked = value.GetBoolean();
                return;
            case NumberSetting number:
                number.Value = value.GetInt32();
                return;
            case TextSetting text:
                text.Value = value.GetString();
                return;
            case ChoiceSetting choice when value.ValueKind == JsonValueKind.Number:
                choice.SelectedIndex = value.GetInt32();
                return;
            case ChoiceSetting choice:
                choice.SetText(value.GetString());
                return;
            default:
                throw new InvalidOperationException(
                    "cannot set " + setting.Key + " from " + value.ValueKind);
        }
    }

    /// <summary>
    /// Drops the pitch out of every <c>\relative</c>, which is what the wizard
    /// does when the user does not want one written.
    /// </summary>
    /// <param name="document">The document.</param>
    private static void RemoveRelativePitches(Dom.Document document)
    {
        foreach (Dom.Relative relative in document.Find<Dom.Relative>().ToList())
        {
            foreach (Dom.Pitch pitch in relative.Find<Dom.Pitch>(1).ToList())
            {
                relative.Remove(pitch);
            }
        }
    }
}
