// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Engrave;
using Fresco.Brix.ScoreWizard;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The Score Wizard's own behaviour: the settings that switch each other on
/// and off, reading a score back in, and the pieces that are not covered by
/// the parity fixtures because upstream has no equivalent.
/// </summary>
public class ScoreWizardTests
{
    /// <summary>The wizard writes the version the engine is compatible with.</summary>
    [Fact]
    public void a_new_wizard_writes_the_engines_compatible_version()
    {
        //Arrange
        ScoreWizardModel model = new ScoreWizardModel();

        //Act
        model.Root.Add(new Violin());

        //Assert
        model.Version.Should().Be(LilyPortEngine.CompatibleWithVersion);
        new ScoreBuilder(model).Text()
            .Should().StartWith("\\version \"" + LilyPortEngine.CompatibleWithVersion);
    }

    /// <summary>The paper orientation waits for a paper size to be chosen.</summary>
    [Fact]
    public void the_paper_orientation_is_only_offered_with_a_paper_size()
    {
        //Arrange
        GeneralPreferences preferences = new GeneralPreferences();

        //Act
        preferences.Paper.SelectedIndex = 2;

        //Assert
        preferences.PaperOrientation.IsEnabled.Should().BeTrue();
        preferences.Paper.SelectedIndex = 0;
        preferences.PaperOrientation.IsEnabled.Should().BeFalse();
    }

    /// <summary>A piano staff always keeps at least one staff.</summary>
    [Fact]
    public void a_piano_staff_keeps_at_least_one_staff()
    {
        //Arrange
        Piano piano = new Piano();

        //Act
        piano.LowerVoices.Value = 0;

        //Assert
        piano.UpperVoices.Minimum.Should().Be(1);
        piano.LowerVoices.Value.Should().Be(1);
        piano.DynamicsStaff.IsEnabled.Should().BeTrue();
    }

    /// <summary>A synth may lose one of its staves entirely.</summary>
    [Fact]
    public void a_synth_part_may_drop_one_staff()
    {
        //Arrange
        SynthLead synth = new SynthLead();

        //Assert
        synth.LowerVoices.Value.Should().Be(0);
        synth.UpperVoices.Minimum.Should().Be(1);
        synth.DynamicsStaff.IsEnabled.Should().BeFalse();
    }

    /// <summary>A tuning is only offered on a tablature staff.</summary>
    [Fact]
    public void a_tuning_is_only_offered_for_tablature()
    {
        //Arrange
        Guitar guitar = new Guitar();

        //Assert
        guitar.Tuning.IsEnabled.Should().BeFalse();
        guitar.CustomTuning.IsEnabled.Should().BeFalse();

        //Act
        guitar.StaffType.SelectedIndex = 1;

        //Assert
        guitar.Tuning.IsEnabled.Should().BeTrue();
        guitar.CustomTuning.IsEnabled.Should().BeFalse();

        //Act
        guitar.Tuning.SelectedIndex = guitar.Tuning.Items.Count - 1;

        //Assert
        guitar.CustomTuning.IsEnabled.Should().BeTrue();
    }

    /// <summary>
    /// A four-string banjo is tuned to the tuning the user PICKED — the
    /// deliberate divergence from upstream's off-by-one (ruling FR14).
    /// </summary>
    [Fact]
    public void a_four_string_banjo_uses_the_tuning_that_was_picked()
    {
        //Arrange
        ScoreWizardModel model = new ScoreWizardModel();
        Banjo banjo = new Banjo();
        banjo.StaffType.SelectedIndex = 1;              //tablature
        banjo.Tuning.SelectedIndex = 2;                 //C-tuning (gCGBD)
        banjo.FourStrings.Value = true;
        model.Root.Add(banjo);

        //Act
        string text = new ScoreBuilder(model).Text();

        //Assert
        text.Should().Contain("(four-string-banjo banjo-c-tuning)");
    }

    /// <summary>A four-string banjo on the Default tuning writes none.</summary>
    /// <remarks>The other half of the same divergence: "Default" means the same
    /// thing on this row as on every other one.</remarks>
    [Fact]
    public void a_four_string_banjo_on_the_default_tuning_writes_no_tuning()
    {
        //Arrange
        ScoreWizardModel model = new ScoreWizardModel();
        Banjo banjo = new Banjo();
        banjo.StaffType.SelectedIndex = 1;
        banjo.Tuning.SelectedIndex = 0;                 //Default
        banjo.FourStrings.Value = true;
        model.Root.Add(banjo);

        //Act
        string text = new ScoreBuilder(model).Text();

        //Assert
        text.Should().NotContain("stringTunings");
    }

    /// <summary>A five-string banjo is tuned the ordinary way.</summary>
    [Fact]
    public void a_five_string_banjo_uses_the_tuning_that_was_picked()
    {
        //Arrange
        ScoreWizardModel model = new ScoreWizardModel();
        Banjo banjo = new Banjo();
        banjo.StaffType.SelectedIndex = 1;
        banjo.Tuning.SelectedIndex = 2;
        model.Root.Add(banjo);

        //Act
        string text = new ScoreBuilder(model).Text();

        //Assert
        text.Should().Contain("stringTunings = #banjo-c-tuning");
    }

    /// <summary>Only the classical guitar starts with an octave clef.</summary>
    [Fact]
    public void only_the_classical_guitar_starts_with_an_octave_clef()
    {
        new Guitar().OctaveClef.Value.Should().BeTrue();
        new AcousticGuitar().OctaveClef.Value.Should().BeFalse();
        new ElectricGuitar().OctaveClef.Value.Should().BeFalse();
    }

    /// <summary>Every part type can be built without a window.</summary>
    [Fact]
    public void every_part_type_builds_a_score_on_its_own()
    {
        foreach (PartEntry entry in PartRegistry.AllParts())
        {
            //Arrange
            ScoreWizardModel model = new ScoreWizardModel();
            model.Root.Add(entry.Create());

            //Act
            string text = new ScoreBuilder(model).Text();

            //Assert
            text.Should().StartWith("\\version");
        }
    }

    /// <summary>A score the wizard wrote can be read back into it.</summary>
    [Fact]
    public void a_wizard_score_reads_back_into_the_wizard()
    {
        //Arrange
        ScoreWizardModel written = new ScoreWizardModel();
        written.SetHeader("title", "Sonata");
        written.SetHeader("composer", "Anonymous");
        written.ScoreProperties.KeyNote.SelectedIndex = 3;      //D
        written.ScoreProperties.KeyMode.SelectedIndex = 1;      //minor
        written.ScoreProperties.TimeSignature.SetText("3/4");
        written.Root.Add(new Violin());
        written.Root.Add(new Cello());
        string text = new ScoreBuilder(written).Text();

        //Act
        ScoreWizardModel read = new ScoreWizardModel();
        ScoreReader.Read(read, text);

        //Assert
        read.Header("title").Should().Be("Sonata");
        read.Header("composer").Should().Be("Anonymous");
        read.ScoreProperties.KeyNote.SelectedIndex.Should().Be(3);
        read.ScoreProperties.KeyMode.SelectedIndex.Should().Be(1);
        read.ScoreProperties.TimeSignature.Text.Should().Be("3/4");
        read.Root.Children.Select(item => item.Part.TypeName)
            .Should().BeEquivalentTo(new[] { "Violin", "Cello" });
    }

    /// <summary>Reading a score empties whatever was in the wizard.</summary>
    [Fact]
    public void reading_a_score_replaces_what_the_wizard_held()
    {
        //Arrange
        ScoreWizardModel model = new ScoreWizardModel();
        model.SetHeader("title", "Gone");
        model.Root.Add(new Piano());

        //Act
        ScoreReader.Read(model, "\\version \"2.27.2\"\n");

        //Assert
        model.Header("title").Should().BeEmpty();
        model.Root.Children.Count.Should().Be(0);
    }

    /// <summary>The pickup measure is read back out of a partial.</summary>
    [Fact]
    public void a_pickup_measure_reads_back()
    {
        //Arrange
        ScoreWizardModel written = new ScoreWizardModel();
        written.ScoreProperties.Pickup.SelectedIndex = 5;
        written.Root.Add(new Violin());
        written.Root.Add(new Viola());

        //Act
        ScoreWizardModel read = new ScoreWizardModel();
        ScoreReader.Read(read, new ScoreBuilder(written).Text());

        //Assert
        read.ScoreProperties.Pickup.SelectedIndex.Should().Be(5);
    }

    /// <summary>Only containers take other parts, and only the right ones.</summary>
    [Fact]
    public void a_container_only_accepts_what_may_go_inside_it()
    {
        //Arrange
        Book book = new Book();
        BookPart bookPart = new BookPart();
        Score score = new Score();
        StaffGroup group = new StaffGroup();

        //Assert
        book.Accepts(bookPart).Should().BeTrue();
        book.Accepts(new Violin()).Should().BeTrue();
        bookPart.Accepts(book).Should().BeFalse();
        score.Accepts(bookPart).Should().BeFalse();
        score.Accepts(group).Should().BeTrue();
        group.Accepts(score).Should().BeFalse();
        group.Accepts(group).Should().BeTrue();
        new Violin().Accepts(new Viola()).Should().BeFalse();
    }

    /// <summary>A part put inside a staff group is built inside it.</summary>
    [Fact]
    public void a_part_inside_a_staff_group_is_built_inside_it()
    {
        //Arrange
        ScoreWizardModel model = new ScoreWizardModel();
        PartTreeItem group = model.Root.Add(new StaffGroup());
        group.Add(new Violin());
        group.Add(new Viola());

        //Act
        string text = new ScoreBuilder(model).Text();

        //Assert
        text.Should().Contain("\\new StaffGroup");
    }

    /// <summary>The wizard's own preferences survive a settings round trip.</summary>
    [Fact]
    public void preferences_round_trip_through_the_settings_store()
    {
        //Arrange
        string directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-scorewiz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using SettingsStore settings =
            new SettingsStore(Path.Combine(directory, "settings.sqlite"));
        ScoreWizardModel saved = new ScoreWizardModel();
        saved.GeneralPreferences.RemoveTagline.Value = true;
        saved.GeneralPreferences.Paper.SelectedIndex = 3;
        saved.GeneralPreferences.PaperOrientation.SelectedIndex = 1;
        saved.InstrumentNames.OtherSystems.SelectedIndex = 1;
        saved.MidiOutput.SeparateScore.Value = true;
        saved.EnginePreferences.PitchLanguage.SelectedIndex = 3;

        //Act
        saved.Save(settings);
        ScoreWizardModel loaded = new ScoreWizardModel();
        loaded.Load(settings);

        //Assert
        loaded.GeneralPreferences.RemoveTagline.Value.Should().BeTrue();
        loaded.GeneralPreferences.Paper.SelectedIndex.Should().Be(3);
        loaded.GeneralPreferences.PaperOrientation.SelectedIndex.Should().Be(1);
        loaded.InstrumentNames.OtherSystems.SelectedIndex.Should().Be(1);
        loaded.MidiOutput.SeparateScore.Value.Should().BeTrue();
        loaded.EnginePreferences.PitchLanguage.SelectedIndex.Should().Be(3);
        loaded.PitchLanguage.Should().Be(saved.PitchLanguage);
    }

    /// <summary>The pitch language reaches a Score part's own properties.</summary>
    [Fact]
    public void the_pitch_language_reaches_a_scores_own_properties()
    {
        //Arrange
        ScoreWizardModel model = new ScoreWizardModel();
        Score score = new Score();
        model.Root.Add(score);

        //Act
        model.EnginePreferences.PitchLanguage.SelectedIndex =
            model.EnginePreferences.PitchLanguage.Items.Count - 1;

        //Assert
        score.Properties.PitchLanguage.Should().Be(model.PitchLanguage);
    }

    /// <summary>Every part type answers with a name and most with a short one.</summary>
    [Fact]
    public void every_part_type_names_itself()
    {
        foreach (PartEntry entry in PartRegistry.AllParts())
        {
            PartBase part = entry.Create();
            part.Title().Should().NotBeNullOrEmpty();
            part.TypeName.Should().Be(entry.Name);
        }
    }
}
