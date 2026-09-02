// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documentation;
using Fresco.Brix.Editor;
using Fresco.Brix.Midi;
using Fresco.Brix.Preferences;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// What the Preferences dialog actually does: every page's values reach the
/// settings store under upstream's own keys and come back unchanged.
/// </summary>
/// <remarks>
/// The pages themselves are controls and are verified on X11; what is tested
/// here is the half that has no window in it — the load/save round trip, which
/// is the whole contract a preferences page has.
/// </remarks>
public class PreferencesTests : IDisposable
{
    private readonly string _directory;

    /// <summary>Creates the fixture over a scratch database file.</summary>
    public PreferencesTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-prefs-" + Guid.NewGuid().ToString("N"));
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
    }

    private string StorePath => _directory;

    // ------------------------------------------------------------ general page

    [Fact]
    public void general_values_round_trip_through_the_store()
    {
        //Arrange
        GeneralValues written = new GeneralValues
        {
            Language = "de",
            NewDocument = GeneralValues.NewDocumentKind.Template,
            NewDocumentTemplate = "blues",
            StripTrailingWhitespace = true,
            KeepBackup = true,
            RememberMetaInfo = false,
            FormatOnSave = true,
            BaseDirectory = "/home/scores",
            UsesFileNameTemplate = true,
            FileNameTemplate = "{title}",
            SessionStartup = GeneralValues.SessionStartupKind.Custom,
            CustomSession = "Bach",
            ExperimentalFeatures = true,
        };

        //Act
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        GeneralValues read = new GeneralValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        read.Language.Should().Be("de");
        read.NewDocument.Should().Be(GeneralValues.NewDocumentKind.Template);
        read.NewDocumentTemplate.Should().Be("blues");
        read.StripTrailingWhitespace.Should().Be(true);
        read.KeepBackup.Should().Be(true);
        read.RememberMetaInfo.Should().Be(false);
        read.FormatOnSave.Should().Be(true);
        read.BaseDirectory.Should().Be("/home/scores");
        read.UsesFileNameTemplate.Should().Be(true);
        read.FileNameTemplate.Should().Be("{title}");
        read.SessionStartup.Should().Be(GeneralValues.SessionStartupKind.Custom);
        read.CustomSession.Should().Be("Bach");
        read.ExperimentalFeatures.Should().Be(true);
    }

    [Fact]
    public void general_values_are_stored_under_upstreams_own_keys()
    {
        //Arrange
        GeneralValues values = new GeneralValues
        {
            NewDocument = GeneralValues.NewDocumentKind.Version,
            StripTrailingWhitespace = true,
            ExperimentalFeatures = true,
        };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);

        //Assert
        store.GetString("new_document").Should().Be("version");
        store.GetBool("strip_trailing_whitespace").Should().Be(true);
        //⚠ Upstream spells this one with a hyphen; the spelling is kept.
        store.GetBool("experimental-features").Should().Be(true);
        store.GetString("session/startup").Should().Be("none");
    }

    [Fact]
    public void general_values_start_at_upstreams_own_defaults()
    {
        //Arrange
        GeneralValues values = new GeneralValues();

        //Act
        using var store = new SettingsStore(StorePath);
        values.Load(store);

        //Assert
        values.NewDocument.Should().Be(GeneralValues.NewDocumentKind.Empty);
        values.RememberMetaInfo.Should().Be(true);
        values.KeepBackup.Should().Be(false);
        values.FileNameTemplate.Should().Be(GeneralValues.DefaultFileNameTemplate);
        values.SessionStartup.Should().Be(GeneralValues.SessionStartupKind.None);
    }

    // ------------------------------------------------------------- editor page

    [Fact]
    public void editor_values_round_trip_through_the_store()
    {
        //Arrange
        EditorValues written = new EditorValues
        {
            WrapLines = true,
            ContextLines = 7,
            MatchHighlightSeconds = 0,
            TabWidth = 4,
            IndentSpaces = 3,
            DocumentSpaces = 0,
            SmartHome = false,
            SmartStartEnd = false,
            KeepCursorInLine = true,
            NumberLines = true,
            InlineStyleCopy = false,
            InlineStyleExport = true,
            CopyHtmlAsPlainText = true,
            CopyDocumentBodyOnly = true,
            WrapTag = "div",
            WrapAttribute = "class",
            WrapAttributeName = "score",
            QuotesLanguage = "custom",
            PrimaryLeft = "«",
            PrimaryRight = "»",
            SecondaryLeft = "‹",
            SecondaryRight = "›",
        };

        //Act
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        EditorValues read = new EditorValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        read.WrapLines.Should().Be(true);
        read.ContextLines.Should().Be(7);
        read.MatchHighlightSeconds.Should().Be(0);
        read.TabWidth.Should().Be(4);
        read.IndentSpaces.Should().Be(3);
        read.DocumentSpaces.Should().Be(0);
        read.SmartHome.Should().Be(false);
        read.SmartStartEnd.Should().Be(false);
        read.KeepCursorInLine.Should().Be(true);
        read.NumberLines.Should().Be(true);
        read.InlineStyleCopy.Should().Be(false);
        read.InlineStyleExport.Should().Be(true);
        read.CopyHtmlAsPlainText.Should().Be(true);
        read.CopyDocumentBodyOnly.Should().Be(true);
        read.WrapTag.Should().Be("div");
        read.WrapAttribute.Should().Be("class");
        read.WrapAttributeName.Should().Be("score");
        read.QuotesLanguage.Should().Be("custom");
        read.PrimaryLeft.Should().Be("«");
        read.SecondaryRight.Should().Be("›");
    }

    [Fact]
    public void an_indent_width_of_zero_reaches_the_indenter_as_tabs()
    {
        //Arrange
        EditorValues values = new EditorValues { IndentSpaces = 0, DocumentSpaces = 0 };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);
        IndentPreferences preferences = IndentPreferences.Read(store);

        //Assert
        preferences.IndentTabs.Should().Be(true);
        preferences.DocumentTabs.Should().Be(true);
    }

    [Fact]
    public void the_editor_page_writes_the_indent_keys_the_indenter_reads()
    {
        //Arrange
        EditorValues values = new EditorValues
        {
            TabWidth = 6,
            IndentSpaces = 4,
            DocumentSpaces = 2,
        };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);
        IndentPreferences preferences = IndentPreferences.Read(store);

        //Assert
        preferences.TabWidth.Should().Be(6);
        preferences.IndentWidth.Should().Be(4);
        preferences.DocumentTabWidth.Should().Be(2);
    }

    // --------------------------------------------------------------- midi page

    [Fact]
    public void midi_values_round_trip_through_the_store()
    {
        //Arrange
        MidiValues written = new MidiValues
        {
            InstrumentPath = "/music/banks/piano.sf2",
            VolumePercent = 160,
        };

        //Act
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        MidiValues read = new MidiValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        read.InstrumentPath.Should().Be("/music/banks/piano.sf2");
        read.VolumePercent.Should().Be(160);
    }

    [Fact]
    public void midi_values_use_the_players_own_settings_keys()
    {
        //Arrange
        MidiValues values = new MidiValues { InstrumentPath = "/a.sf2", VolumePercent = 50 };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);

        //Assert
        store.GetString(SoundFonts.InstrumentSettingKey).Should().Be("/a.sf2");
        store.GetInt(MidiPlayerService.VolumeSettingKey).Should().Be(50);
    }

    [Fact]
    public void a_volume_beyond_the_range_is_clamped()
    {
        //Arrange
        MidiValues values = new MidiValues { VolumePercent = 5000 };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);
        MidiValues read = new MidiValues();
        read.Load(store);

        //Assert
        read.VolumePercent.Should().Be(200);
    }

    // ------------------------------------------------------ documentation page

    [Fact]
    public void documentation_values_round_trip_through_the_store()
    {
        //Arrange
        DocumentationValues written = new DocumentationValues
        {
            Manual = ManualCatalog.NotationName,
            ShowContents = false,
        };

        //Act
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        DocumentationValues read = new DocumentationValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        read.Manual.Should().Be(ManualCatalog.NotationName);
        read.ShowContents.Should().Be(false);
    }

    [Fact]
    public void a_manual_that_is_not_in_the_catalog_falls_back_to_the_default()
    {
        //Arrange
        DocumentationValues values = new DocumentationValues { Manual = "no-such-manual" };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);
        DocumentationValues read = new DocumentationValues();
        read.Load(store);

        //Assert
        read.Manual.Should().Be(ManualCatalog.DefaultName);
    }

    // -------------------------------------------------------------- paths page

    [Fact]
    public void hyphenation_paths_round_trip_through_the_store()
    {
        //Arrange
        PathValues written = new PathValues
        {
            HyphenationPaths = new[] { "/opt/dicts", "/home/me/dicts" },
        };

        //Act
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        PathValues read = new PathValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        read.HyphenationPaths.Count.Should().Be(2);
        read.HyphenationPaths[0].Should().Be("/opt/dicts");
        read.HyphenationPaths[1].Should().Be("/home/me/dicts");
    }

    [Fact]
    public void the_built_in_path_list_is_forgotten_rather_than_written()
    {
        //Arrange
        PathValues values = new PathValues
        {
            HyphenationPaths = HyphenDictionaries.DefaultPaths,
        };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);

        //Assert
        store.GetString(HyphenDictionaries.PathsKey).Should().BeNull();
    }

    // ------------------------------------------------------------ helpers page

    [Fact]
    public void helper_commands_round_trip_through_the_store()
    {
        //Arrange
        HelperValues written = new HelperValues();
        written.SetCommand("pdf", "okular $f");
        written.SetCommand("shell", "xterm");

        //Act
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        HelperValues read = new HelperValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        read.Command("pdf").Should().Be("okular $f");
        read.Command("shell").Should().Be("xterm");
        read.Command("browser").Should().Be(string.Empty);
    }

    [Fact]
    public void a_helper_command_reaches_the_service_that_runs_it()
    {
        //Arrange
        HelperValues values = new HelperValues();
        values.SetCommand("pdf", "okular \"my viewer\" $f");

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);
        HelperApplications helpers = new HelperApplications(store);
        IReadOnlyList<string> command = helpers.Command("pdf");

        //Assert
        command.Count.Should().Be(3);
        command[0].Should().Be("okular");
        command[1].Should().Be("my viewer");
        command[2].Should().Be("$f");
    }

    [Fact]
    public void the_helper_page_offers_upstreams_types_without_the_git_row()
    {
        //Arrange
        IReadOnlyList<string> types = HelperValues.Types;

        //Assert
        types.Count.Should().Be(8);
        types[0].Should().Be("pdf");
        types.Contains("git").Should().Be(false);
        types.Contains("shell").Should().Be(true);
    }

    // ------------------------------------------------- fonts and colors page

    [Fact]
    public void a_colour_scheme_round_trips_through_the_store()
    {
        //Arrange
        using var store = new SettingsStore(StorePath);
        TextFormatData written = new TextFormatData("default", store);
        written.SetBaseColor(
            "background", Windows.UI.Color.FromArgb(255, 0x20, 0x21, 0x22));
        written.FontSize = 17;
        written.FontFamily = "RobotoFont";
        TextFormat comment = written.DefaultStyle("comment");
        comment.IsItalic = true;
        comment.Foreground = Windows.UI.Color.FromArgb(255, 0x11, 0x22, 0x33);

        //Act
        written.Save(store);
        TextFormatData read = new TextFormatData("default", store);

        //Assert
        read.BaseColor("background").R.Should().Be((byte)0x20);
        read.FontSize.Should().Be(17.0);
        read.FontFamily.Should().Be("RobotoFont");
        read.DefaultStyle("comment").IsItalic.Should().Be(true);
        read.DefaultStyle("comment").Foreground.Value.B.Should().Be((byte)0x33);
    }

    [Fact]
    public void a_second_scheme_keeps_its_own_colours()
    {
        //Arrange
        using var store = new SettingsStore(StorePath);
        TextFormatData dark = new TextFormatData("user1", store);
        dark.SetBaseColor("text", Windows.UI.Color.FromArgb(255, 255, 255, 255));
        dark.Save(store);

        //Act
        TextFormatData light = new TextFormatData("default", store);
        TextFormatData reopened = new TextFormatData("user1", store);

        //Assert
        light.BaseColor("text").R.Should().Be((byte)0);
        reopened.BaseColor("text").R.Should().Be((byte)255);
    }

    [Fact]
    public void the_scheme_in_force_is_read_from_upstreams_own_key()
    {
        //Arrange
        using var store = new SettingsStore(StorePath);

        //Act
        string before = TextFormatData.CurrentScheme(store);
        store.SetString(TextFormatData.SchemeSettingKey, "user2");
        string after = TextFormatData.CurrentScheme(store);

        //Assert
        before.Should().Be("default");
        after.Should().Be("user2");
    }

    [Fact]
    public void the_export_scheme_falls_back_to_the_editors_own()
    {
        //Arrange
        using var store = new SettingsStore(StorePath);
        store.SetString(TextFormatData.SchemeSettingKey, "user1");

        //Act
        string printer = TextFormatData.PrinterScheme(store);

        //Assert
        printer.Should().Be("user1");
    }

    [Theory]
    [InlineData("#ffffff", 255, 255, 255)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#123456", 0x12, 0x34, 0x56)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    public void a_colour_reads_back_as_it_was_written(
        string text, int red, int green, int blue)
    {
        //Act
        Windows.UI.Color? parsed = TextFormat.ParseColor(text);

        //Assert
        parsed.HasValue.Should().Be(true);
        parsed.Value.R.Should().Be((byte)red);
        parsed.Value.G.Should().Be((byte)green);
        parsed.Value.B.Should().Be((byte)blue);
    }

    [Fact]
    public void a_colour_written_out_parses_back_to_itself()
    {
        //Arrange
        Windows.UI.Color color = Windows.UI.Color.FromArgb(255, 0x30, 0x8C, 0xC6);

        //Act
        string text = TextFormat.FormatColor(color);
        Windows.UI.Color? parsed = TextFormat.ParseColor(text);

        //Assert
        text.Should().Be("#308cc6");
        parsed.Value.G.Should().Be((byte)0x8C);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    public void a_colour_that_does_not_read_answers_nothing(string text)
    {
        //Act
        Windows.UI.Color? parsed = TextFormat.ParseColor(text);

        //Assert
        parsed.HasValue.Should().Be(false);
    }

    // --------------------------------------------------------------- tools page

    [Fact]
    public void tools_values_start_at_upstreams_own_defaults()
    {
        //Arrange
        ToolsValues values = new ToolsValues();

        //Act
        using var store = new SettingsStore(StorePath);
        values.Load(store);

        //Assert
        values.ShowLogOnStart.Should().Be(true);
        values.RawLogView.Should().Be(true);
        values.HideAutomaticEngraves.Should().Be(false);
        values.GroupDocumentsByFolder.Should().Be(false);
        values.OutlinePatterns.Count.Should().Be(
            Fresco.Brix.Documents.DocumentStructure.DefaultPatterns.Count);
        values.OutlineCommentPatterns.Count.Should().Be(
            Fresco.Brix.Documents.DocumentStructure.DefaultCommentPatterns.Count);
    }

    [Fact]
    public void tools_values_round_trip_through_the_store()
    {
        //Arrange
        ToolsValues written = new ToolsValues
        {
            ShowLogOnStart = false,
            RawLogView = false,
            HideAutomaticEngraves = true,
            GroupDocumentsByFolder = true,
            OutlinePatterns = new[] { @"^\\score", @"^\\book" },
            OutlineCommentPatterns = new[] { @"\bTODO\b" },
        };

        //Act
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        ToolsValues read = new ToolsValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        read.ShowLogOnStart.Should().Be(false);
        read.RawLogView.Should().Be(false);
        read.HideAutomaticEngraves.Should().Be(true);
        read.GroupDocumentsByFolder.Should().Be(true);
        read.OutlinePatterns.Count.Should().Be(2);
        read.OutlinePatterns[0].Should().Be(@"^\\score");
        read.OutlinePatterns[1].Should().Be(@"^\\book");
        read.OutlineCommentPatterns.Count.Should().Be(1);
        read.OutlineCommentPatterns[0].Should().Be(@"\bTODO\b");
    }

    [Fact]
    public void the_running_engine_rows_round_trip_through_the_store()
    {
        //Arrange — the surviving half of upstream's retired LilyPond page.
        ToolsValues written = new ToolsValues
        {
            SaveDocumentOnRun = true,
            DeleteIntermediateFiles = false,
            EmbedSourceCode = true,
            IncludePath = new[] { "/home/scores/lib", "/usr/share/ly" },
        };

        //Act
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        ToolsValues read = new ToolsValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        read.SaveDocumentOnRun.Should().Be(true);
        read.DeleteIntermediateFiles.Should().Be(false);
        read.EmbedSourceCode.Should().Be(true);
        read.IncludePath.Count.Should().Be(2);
        read.IncludePath[0].Should().Be("/home/scores/lib");
        read.IncludePath[1].Should().Be("/usr/share/ly");
    }

    [Fact]
    public void the_running_engine_rows_keep_upstreams_own_key_spellings()
    {
        //Arrange
        ToolsValues values = new ToolsValues
        {
            SaveDocumentOnRun = true,
            DeleteIntermediateFiles = false,
            EmbedSourceCode = true,
            IncludePath = new[] { "/home/scores/lib" },
        };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);

        //Assert — a Frescobaldi settings file keeps working, so the keys stay
        //upstream's own even though the page they live on is a different one.
        store.GetBool("lilypond_settings/save_on_run").Should().Be(true);
        store.GetBool("lilypond_settings/delete_intermediate_files", true)
            .Should().Be(false);
        store.GetBool("lilypond_settings/embed_source_code").Should().Be(true);
        store.GetString("lilypond_settings/include_path")
            .Should().Be("/home/scores/lib");
    }

    [Fact]
    public void the_music_font_auto_install_row_round_trips_and_defaults_on()
    {
        //Arrange — upstream's `music-fonts/auto-install' defaults TRUE and is
        //written unconditionally, unlike the two paths beside it.
        PathValues fresh = new PathValues();

        //Act
        using (var store = new SettingsStore(StorePath)) { fresh.Load(store); }

        PathValues written = new PathValues { AutoInstallMusicFonts = false };
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        PathValues read = new PathValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        fresh.AutoInstallMusicFonts.Should().Be(true);
        read.AutoInstallMusicFonts.Should().Be(false);
        using var check = new SettingsStore(StorePath);
        check.GetBool("music-fonts/auto-install", true).Should().Be(false);
    }

    [Fact]
    public void the_tab_close_button_row_round_trips_and_defaults_on()
    {
        //Arrange
        GeneralValues fresh = new GeneralValues();

        //Act
        using (var store = new SettingsStore(StorePath)) { fresh.Load(store); }

        GeneralValues written = new GeneralValues { TabsClosable = false };
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        GeneralValues read = new GeneralValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert — upstream's `tabs_closable', default on (tabbar.py reads it).
        fresh.TabsClosable.Should().Be(true);
        read.TabsClosable.Should().Be(false);
        using var check = new SettingsStore(StorePath);
        check.GetBool(GeneralValues.TabsClosableKey, true).Should().Be(false);
    }

    [Fact]
    public void tools_values_are_stored_under_upstreams_own_keys()
    {
        //Arrange
        ToolsValues values = new ToolsValues
        {
            ShowLogOnStart = false,
            RawLogView = false,
            HideAutomaticEngraves = true,
            GroupDocumentsByFolder = true,
        };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);

        //Assert
        store.GetBool("log/show_on_start", true).Should().Be(false);
        store.GetBool("log/rawview", true).Should().Be(false);
        store.GetBool("log/hide_auto_engrave").Should().Be(true);
        store.GetBool("document_list/group_by_folder").Should().Be(true);
    }

    [Fact]
    public void the_built_in_outline_patterns_are_forgotten_rather_than_written()
    {
        //Arrange
        ToolsValues values = new ToolsValues
        {
            OutlinePatterns = Fresco.Brix.Documents.DocumentStructure.DefaultPatterns,
            OutlineCommentPatterns =
                Fresco.Brix.Documents.DocumentStructure.DefaultCommentPatterns,
        };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);

        //Assert
        store.GetString("documentstructure/outline_patterns").Should().BeNull();
        store.GetString("documentstructure/outline_patterns_comments").Should().BeNull();
    }

    [Fact]
    public void changed_outline_patterns_reach_the_document_structure()
    {
        //Arrange
        ToolsValues values = new ToolsValues
        {
            OutlinePatterns = new[] { @"^\\header", @"^\\layout" },
        };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);
        IReadOnlyList<string> patterns =
            Fresco.Brix.Documents.DocumentStructure.Patterns(false, store);

        //Assert
        patterns.Count.Should().Be(2);
        patterns[0].Should().Be(@"^\\header");
        patterns[1].Should().Be(@"^\\layout");
    }

    // ---------------------------------------------------------- music view page

    [Fact]
    public void music_view_values_start_at_the_views_own_defaults()
    {
        //Arrange
        MusicViewValues values = new MusicViewValues();

        //Act
        using var store = new SettingsStore(StorePath);
        values.Load(store);

        //Assert
        values.OnlyNewerFiles.Should().Be(true);
        values.ViewMode.Should().Be(MusicViewValues.DefaultViewMode);
        values.ScalePercent.Should().Be(100);
        values.PageLayout.Should().Be(MusicViewValues.DefaultPageLayout);
        values.Orientation.Should().Be(MusicViewValues.DefaultOrientation);
        values.ContinuousScrolling.Should().Be(true);
        values.PageShadow.Should().Be(true);
    }

    [Fact]
    public void music_view_values_round_trip_through_the_store()
    {
        //Arrange
        MusicViewValues written = new MusicViewValues
        {
            OnlyNewerFiles = false,
            ViewMode = "fixed",
            ScalePercent = 175,
            PageLayout = "double_left",
            Orientation = "horizontal",
            ContinuousScrolling = false,
            PageShadow = false,
        };

        //Act
        using (var store = new SettingsStore(StorePath)) { written.Save(store); }

        MusicViewValues read = new MusicViewValues();
        using (var store = new SettingsStore(StorePath)) { read.Load(store); }

        //Assert
        read.OnlyNewerFiles.Should().Be(false);
        read.ViewMode.Should().Be("fixed");
        read.ScalePercent.Should().Be(175);
        read.PageLayout.Should().Be("double_left");
        read.Orientation.Should().Be("horizontal");
        read.ContinuousScrolling.Should().Be(false);
        read.PageShadow.Should().Be(false);
    }

    [Fact]
    public void music_view_values_use_the_keys_the_view_already_reads()
    {
        //Arrange
        MusicViewValues values = new MusicViewValues
        {
            OnlyNewerFiles = false,
            ViewMode = "fitboth",
            ScalePercent = 50,
            PageLayout = "raster",
            Orientation = "horizontal",
            ContinuousScrolling = false,
            PageShadow = false,
        };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);

        //Assert
        store.GetBool("musicview/newer_files_only", true).Should().Be(false);
        store.GetString("musicview/viewmode").Should().Be("fitboth");
        store.GetDouble("musicview/zoom", 1.0).Should().Be(0.5);
        store.GetString("musicview/layout").Should().Be("raster");
        store.GetString("musicview/orientation").Should().Be("horizontal");
        store.GetBool("musicview/continuous", true).Should().Be(false);
        store.GetBool("musicview/shadow", true).Should().Be(false);
    }

    [Fact]
    public void a_fixed_scale_beyond_the_range_is_clamped()
    {
        //Arrange
        MusicViewValues values = new MusicViewValues { ScalePercent = 5000 };

        //Act
        using var store = new SettingsStore(StorePath);
        values.Save(store);
        MusicViewValues read = new MusicViewValues();
        read.Load(store);

        //Assert
        read.ScalePercent.Should().Be(MusicViewValues.MaximumScalePercent);
    }

    [Fact]
    public void a_scaling_the_view_does_not_know_falls_back_to_its_own()
    {
        //Arrange
        using var store = new SettingsStore(StorePath);
        store.SetString("musicview/viewmode", "no-such-mode");
        store.SetString("musicview/layout", "no-such-layout");

        //Act
        MusicViewValues values = new MusicViewValues();
        values.Load(store);

        //Assert
        values.ViewMode.Should().Be(MusicViewValues.DefaultViewMode);
        values.PageLayout.Should().Be(MusicViewValues.DefaultPageLayout);
    }
}
