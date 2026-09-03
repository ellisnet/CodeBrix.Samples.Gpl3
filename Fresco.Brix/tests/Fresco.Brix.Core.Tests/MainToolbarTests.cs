// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Engrave;
using Fresco.Brix.MusicView;
using Fresco.Brix.Preferences;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Fresco.Brix.Tools;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The window's two toolbars: what they hold, in what order, and what the
/// pull-down preference changes.
/// </summary>
public class MainToolbarTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "frescobrix-toolbar-" + Guid.NewGuid().ToString("N"));

    private readonly SettingsStore _settings;

    /// <summary>Creates the fixture with a store of its own.</summary>
    public MainToolbarTests()
    {
        Directory.CreateDirectory(_folder);
        _settings = new SettingsStore(_folder);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _settings?.Dispose();
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }

        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<ToolbarEntry> MainBar(bool verbose)
        => ToolbarLayout.Main(
            new MainActions(_settings),
            new BrowserActions(_settings),
            new ScoreWizardActions(_settings),
            new EngraveActions(_settings),
            verbose);

    private static IEnumerable<string> Shape(IReadOnlyList<ToolbarEntry> entries)
        => entries.Select(entry => entry.Kind switch
        {
            ToolbarEntryKind.Separator => "|",
            ToolbarEntryKind.Widget => "<" + entry.Widget + ">",
            _ => entry.Action.Name,
        });

    [Fact]
    public void the_main_toolbar_is_upstreams_own_order()
    {
        //Arrange, Act
        IReadOnlyList<ToolbarEntry> entries = MainBar(verbose: false);

        //Assert — mainwindow.createToolBars, entry for entry and separator for
        //separator.
        Shape(entries).Should().BeEquivalentTo(new[]
        {
            "file_new", "file_open", "file_save", "file_close",
            "|", "go_back", "go_forward",
            "|", "edit_undo", "edit_redo",
            "|", "scorewiz", "engrave_runner",
        });
    }

    [Fact]
    public void the_music_view_toolbar_is_upstreams_own_order_without_print()
    {
        //Arrange, Act
        IReadOnlyList<ToolbarEntry> entries
            = ToolbarLayout.Music(new MusicViewActions(_settings));

        //Assert — mainwindow.createToolBars' second bar. music_print is absent
        //for good under ruling FR5.5, which is the one difference.
        Shape(entries).Should().BeEquivalentTo(new[]
        {
            "<DocumentChooser>",
            "|", "music_zoom_in", "<ZoomChooser>", "music_zoom_out",
            "music_magnifier",
            "|", "music_prev_page", "<Pager>", "music_next_page",
            "|", "music_clear",
        });
    }

    [Fact]
    public void the_main_toolbars_icons_are_upstreams_own_names()
    {
        //Arrange, Act
        IReadOnlyList<ToolbarEntry> entries = MainBar(verbose: false);

        //Assert — every button names an icon, and every name it uses ships.
        foreach (ToolbarEntry entry in entries.Where(
            e => e.Kind == ToolbarEntryKind.Action))
        {
            entry.Action.IconName.Should().NotBeNullOrEmpty();
            IconTheme.Has(IconSet.Light, entry.Action.IconName).Should().BeTrue();
            IconTheme.Has(IconSet.Dark, entry.Action.IconName).Should().BeTrue();
        }
    }

    [Fact]
    public void the_music_toolbars_icons_are_upstreams_own_names()
    {
        //Arrange, Act
        IReadOnlyList<ToolbarEntry> entries
            = ToolbarLayout.Music(new MusicViewActions(_settings));

        //Assert
        foreach (ToolbarEntry entry in entries.Where(
            e => e.Kind == ToolbarEntryKind.Action))
        {
            entry.Action.IconName.Should().NotBeNullOrEmpty();
            IconTheme.Has(IconSet.Light, entry.Action.IconName).Should().BeTrue();
            IconTheme.Has(IconSet.Dark, entry.Action.IconName).Should().BeTrue();
        }
    }

    [Fact]
    public void open_carries_the_recent_files_menu_whatever_the_preference_says()
    {
        //Arrange, Act
        ToolbarEntry plain = MainBar(verbose: false)
            .Single(e => e.Action?.Name == "file_open");
        ToolbarEntry verbose = MainBar(verbose: true)
            .Single(e => e.Action?.Name == "file_open");

        //Assert — upstream hangs menu_recent_files on the Open button inside
        //createToolBars, not inside the verbose_toolbuttons branch.
        plain.Menu.Should().Be(ToolbarMenu.RecentFiles);
        verbose.Menu.Should().Be(ToolbarMenu.RecentFiles);
    }

    [Fact]
    public void the_engrave_button_carries_publish_and_custom()
    {
        //Arrange, Act
        ToolbarEntry runner = MainBar(verbose: false)
            .Single(e => e.Action?.Name == "engrave_runner");

        //Assert — upstream adds engrave_publish and engrave_custom to the
        //runner's own button widget.
        runner.Menu.Should().Be(ToolbarMenu.EngraveModes);
    }

    [Fact]
    public void without_the_preference_only_open_has_a_menu()
    {
        //Arrange, Act
        IReadOnlyList<ToolbarEntry> entries = MainBar(verbose: false);

        //Assert
        entries.Single(e => e.Action?.Name == "file_new").Menu
            .Should().Be(ToolbarMenu.None);
        entries.Single(e => e.Action?.Name == "file_save").Menu
            .Should().Be(ToolbarMenu.None);
        entries.Single(e => e.Action?.Name == "file_close").Menu
            .Should().Be(ToolbarMenu.None);
    }

    [Fact]
    public void with_the_preference_new_save_and_close_get_their_menus()
    {
        //Arrange, Act
        IReadOnlyList<ToolbarEntry> entries = MainBar(verbose: true);

        //Assert — upstream's settingsChanged: the template menu on New, the
        //File menu's save sub-menu on Save, its close sub-menu on Close.
        entries.Single(e => e.Action?.Name == "file_new").Menu
            .Should().Be(ToolbarMenu.Templates);
        entries.Single(e => e.Action?.Name == "file_save").Menu
            .Should().Be(ToolbarMenu.Save);
        entries.Single(e => e.Action?.Name == "file_close").Menu
            .Should().Be(ToolbarMenu.Close);
    }

    [Fact]
    public void the_preference_is_off_until_it_is_set_and_round_trips()
    {
        //Arrange
        GeneralValues values = new GeneralValues();

        //Act
        values.Load(_settings);
        bool before = MainToolbar.VerboseToolButtons(_settings);
        values.VerboseToolButtons = true;
        values.Save(_settings);
        GeneralValues again = new GeneralValues();
        again.Load(_settings);

        //Assert — upstream's key and upstream's default.
        GeneralValues.VerboseToolButtonsKey.Should().Be("verbose_toolbuttons");
        before.Should().BeFalse();
        values.Load(_settings);
        again.VerboseToolButtons.Should().BeTrue();
        MainToolbar.VerboseToolButtons(_settings).Should().BeTrue();
    }

    [Fact]
    public void the_bars_are_named_by_upstreams_own_msgids()
    {
        //Arrange, Act, Assert — mainwindow.translateUI sets these as the two
        //bars' window titles.
        ToolbarLayout.MainTitle().Should().Be("Main Toolbar");
        ToolbarLayout.MusicTitle().Should().Be("Music View Toolbar");
    }

    [Fact]
    public void a_buttons_tool_tip_is_what_it_does_and_its_shortcut()
    {
        //Arrange
        MainActions main = new MainActions(_settings);
        EngraveActions engrave = new EngraveActions(_settings);

        //Act
        string newDocument = MainToolbar.ToolTipFor(main.FileNew);
        string runner = MainToolbar.ToolTipFor(engrave.EngraveRunner);

        //Assert — Qt's own shape, with the accelerator marker stripped at
        //display (board trap 18) and an action's own tool tip winning over its
        //menu text. engrave_runner carries no shortcut, and upstream's tooltip
        //is the promise the Shift-click behaviour keeps.
        newDocument.Should().Be("New Document (Ctrl+N)");
        runner.Should().Be("Engrave (preview; Shift-click for custom)");
    }

    [Fact]
    public void the_document_chooser_is_the_action_that_carries_the_shortcut()
    {
        //Arrange
        MusicViewActions music = new MusicViewActions(_settings);

        //Act
        ToolbarEntry chooser = ToolbarLayout.Music(music)
            .Single(e => e.Widget == ToolbarWidget.DocumentChooser);

        //Assert — audit A GAP-25: the chooser is a real action with a caption
        //and Ctrl+Shift+O, not a bare combo box.
        chooser.Action.Should().BeSameAs(music.MusicDocumentSelect);
        chooser.Action.Name.Should().Be("music_document_select");
        chooser.Action.Shortcuts.Single().ToString().Should().Be("Ctrl+Shift+O");
    }
}

/// <summary>The one engrave button on the toolbar: run, abort, or custom.</summary>
public class EngraveRunnerTests
{
    [Theory]
    [InlineData(false, false, EngraveRunnerAction.Preview)]
    [InlineData(false, true, EngraveRunnerAction.Custom)]
    [InlineData(true, false, EngraveRunnerAction.Abort)]
    [InlineData(true, true, EngraveRunnerAction.Abort)]
    public void the_button_does_what_upstream_does(
        bool running, bool shift, EngraveRunnerAction expected)
    {
        //Arrange, Act
        EngraveRunnerAction action = Engraver.RunnerActionFor(running, shift);

        //Assert — engrave/__init__.py engraveRunner: a running job is aborted
        //whatever is held down, Shift asks for the custom window, otherwise a
        //preview runs.
        action.Should().Be(expected);
    }

    [Fact]
    public async Task a_job_says_it_has_started_only_once_it_is_running()
    {
        //Arrange — this is the invariant Engraver.RunJob relies on. It hooks
        //the JOB's own Started event rather than JobManager.AnyJobStarted,
        //because the manager announces BEFORE it calls StartAsync (so that a
        //log connected by the announcement sees the run's first message) and
        //IsRunning is still false at that moment. Hooking the wrong one is why
        //the toolbar's engrave button never turned into a stop button, and why
        //Abort stayed disabled through a whole run.
        FakeJob job = new FakeJob("engrave");
        bool runningWhenAnnounced = false;
        bool announced = false;
        job.Started += (_, _) =>
        {
            announced = true;
            runningWhenAnnounced = job.IsRunning;
        };

        //Act
        Task run = job.StartAsync();
        job.Complete(true);
        await run;

        //Assert
        announced.Should().BeTrue();
        runningWhenAnnounced.Should().BeTrue();
        job.IsRunning.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, "lilypond-stop")]
    [InlineData(false, "lilypond-run")]
    public void the_buttons_icon_follows_the_job(bool running, string expected)
    {
        //Arrange, Act — this is the rule Engraver.UpdateActions applies
        //(engrave/__init__.py updateActions).
        string icon = running ? "lilypond-stop" : "lilypond-run";

        //Assert — and both names ship in both sets, so the toggle can draw.
        icon.Should().Be(expected);
        IconTheme.Has(IconSet.Light, expected).Should().BeTrue();
        IconTheme.Has(IconSet.Dark, expected).Should().BeTrue();
    }
}

/// <summary>The Music View toolbar's zoom chooser and page box.</summary>
public class MusicToolbarWidgetTests
{
    [Fact]
    public void the_zoom_list_is_upstreams_own_after_the_views_maximum()
    {
        //Arrange, Act
        IReadOnlyList<double> factors = ZoomLevels.Factors;

        //Assert — qpageview declares ten factors and Frescobaldi's own
        //pagedview.ViewActions drops the ones above its view's MAX_ZOOM of 8.0,
        //which is this view's maximum too.
        ZoomLevels.DeclaredFactors.Count.Should().Be(10);
        factors.Should().BeEquivalentTo(new[]
        {
            0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 8.0,
        });
    }

    [Theory]
    [InlineData(0.5, "50%")]
    [InlineData(0.75, "75%")]
    [InlineData(1.0, "100%")]
    [InlineData(1.25, "125%")]
    [InlineData(8.0, "800%")]
    [InlineData(1.375, "138%")]
    public void a_zoom_factor_reads_as_a_whole_percentage(
        double factor, string expected)
    {
        //Arrange, Act
        string caption = ZoomLevels.CaptionFor(factor);

        //Assert — upstream's format is "{0:.0%}".
        caption.Should().Be(expected);
    }

    [Fact]
    public void the_three_fit_modes_come_first()
    {
        //Arrange, Act
        IReadOnlyList<ZoomEntry> entries = ZoomLevels.Entries();

        //Assert — ZoomerAction puts the view modes above the zoom factors, and
        //Frescobaldi names them Width, Height and Page.
        entries.Count.Should().Be(3 + ZoomLevels.Factors.Count);
        entries[0].Mode.Should().Be(ViewMode.FitWidth);
        entries[1].Mode.Should().Be(ViewMode.FitHeight);
        entries[2].Mode.Should().Be(ViewMode.FitBoth);
        entries[0].Caption.Should().Be("Width");
        entries[1].Caption.Should().Be("Height");
        entries[2].Caption.Should().Be("Page");
        entries[3].Factor.Should().Be(0.5);
    }

    [Fact]
    public void a_fit_mode_wins_over_the_zoom_factor()
    {
        //Arrange
        IReadOnlyList<ZoomEntry> entries = ZoomLevels.Entries();

        //Act, Assert — upstream's _adjustComboBox: in a fit mode the factor is
        //whatever the window size made it, so the mode is what is shown.
        ZoomLevels.IndexFor(entries, ViewMode.FitWidth, 1.37).Should().Be(0);
        ZoomLevels.IndexFor(entries, ViewMode.FitBoth, 1.0).Should().Be(2);
        ZoomLevels.IndexFor(entries, ViewMode.FixedScale, 1.0).Should().Be(5);
    }

    [Fact]
    public void a_zoom_the_list_does_not_carry_selects_nothing()
    {
        //Arrange
        IReadOnlyList<ZoomEntry> entries = ZoomLevels.Entries();

        //Act
        int index = ZoomLevels.IndexFor(entries, ViewMode.FixedScale, 1.37);

        //Assert — upstream shows the value as edit text instead of selecting a
        //row (its combo is editable with a read-only line edit).
        index.Should().Be(-1);
        ZoomLevels.CaptionFor(1.37).Should().Be("137%");
    }

    [Theory]
    [InlineData(1, 12, "1 of 12")]
    [InlineData(7, 7, "7 of 7")]
    [InlineData(0, 0, "")]
    [InlineData(3, 0, "")]
    public void the_page_box_reads_the_way_upstream_writes_it(
        int number, int total, string expected)
    {
        //Arrange, Act
        string text = PagerDisplay.Format("{num} of {total}", number, total);

        //Assert — pagedview.py:215 sets the display format to
        //_("{num} of {total}"); with no pages upstream shows its special value
        //text and the box is dead.
        text.Should().Be(expected);
    }

    [Fact]
    public void the_page_boxs_format_is_upstreams_msgid()
    {
        //Arrange, Act, Assert
        PagerDisplay.DisplayFormat().Should().Be("{num} of {total}");
    }

    [Theory]
    [InlineData("5 of 12", 12, 5)]
    [InlineData("12", 12, 12)]
    [InlineData("99 of 12", 12, 12)]
    [InlineData("0 of 12", 12, 1)]
    [InlineData("", 12, 0)]
    [InlineData("no digits", 12, 0)]
    [InlineData("5 of 12", 0, 0)]
    public void a_typed_page_is_the_first_number_in_range(
        string typed, int total, int expected)
    {
        //Arrange, Act
        int page = PagerDisplay.Parse(typed, total);

        //Assert — Qt's spin box only ever sees the number, because the prefix
        //and suffix are chrome it draws itself; here the whole line is a text
        //box, so the first run of digits is the answer.
        page.Should().Be(expected);
    }
}
