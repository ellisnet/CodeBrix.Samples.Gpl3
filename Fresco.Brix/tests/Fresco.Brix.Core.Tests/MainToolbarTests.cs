// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.CommandBar;
using Fresco.Brix.Commands;
using Fresco.Brix.Engrave;
using Fresco.Brix.MusicView;
using Fresco.Brix.Preferences;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
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
        string newDocument = ToolbarLayout.ToolTipFor(main.FileNew);
        string runner = ToolbarLayout.ToolTipFor(engrave.EngraveRunner);

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

/// <summary>
/// What the builder makes of each layout entry: the two bars, the elements on
/// them, and the state that has to keep following the commands.
/// </summary>
/// <remarks>
/// <para>
/// These cases build real CodeBrix.Platform elements in a process with no
/// window, which the module initializers in this project make possible; the
/// collection they belong to is what keeps that work off every other test
/// thread.
/// </para>
/// <para>
/// The bars are built by <see cref="MainToolbar.SettingsChanged"/> rather than
/// by being loaded into a tree, because that is the same call the window makes
/// when the pull-down preference changes and it is the only route that does not
/// need a window.
/// </para>
/// </remarks>
[Collection(XamlTestCollection.Name)]
public class MainToolbarBuilderTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "frescobrix-builder-" + Guid.NewGuid().ToString("N"));

    private readonly SettingsStore _settings;
    private MainActions _main;
    private BrowserActions _browser;
    private ScoreWizardActions _scoreWizard;
    private EngraveActions _engrave;
    private MusicViewActions _music;

    /// <summary>Creates the fixture with a store of its own.</summary>
    public MainToolbarBuilderTests()
    {
        Directory.CreateDirectory(_folder);
        _settings = new SettingsStore(_folder);
    }

    /// <summary>Removes the scratch store.</summary>
    public void Dispose()
    {
        _settings?.Dispose();
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void the_tray_holds_the_two_bars_upstream_names()
    {
        //Arrange, Act
        MainToolbar toolbar = Build();

        //Assert — upstream adds two QToolBars to the same area, and Qt names
        //each of them; the tray is that area.
        IReadOnlyList<ToolBar> bars = Bars(toolbar);
        bars.Count.Should().Be(2);
        bars[0].Title.Should().Be(ToolbarLayout.MainTitle());
        bars[1].Title.Should().Be(ToolbarLayout.MusicTitle());
    }

    [Fact]
    public void the_main_bar_holds_one_item_per_layout_entry()
    {
        //Arrange
        MainToolbar toolbar = Build();
        IReadOnlyList<ToolbarEntry> entries = ToolbarLayout.Main(
            _main, _browser, _scoreWizard, _engrave, verboseToolButtons: false);

        //Act
        IReadOnlyList<UIElement> items = Items(Bars(toolbar)[0]);

        //Assert — the order model and the bar say the same thing, entry for
        //entry: a separator entry is a separator, an action entry is a button.
        items.Count.Should().Be(entries.Count);
        for (int index = 0; index < entries.Count; index++)
        {
            if (entries[index].Kind == ToolbarEntryKind.Separator)
            {
                items[index].Should().BeOfType<ToolBarSeparator>();
                continue;
            }

            ToolButton button = items[index].Should().BeAssignableTo<ToolButton>().Subject;
            button.Command.Should().BeSameAs(entries[index].Action);
        }
    }

    [Fact]
    public void the_music_bar_holds_one_item_per_layout_entry()
    {
        //Arrange
        MainToolbar toolbar = Build();
        IReadOnlyList<ToolbarEntry> entries = ToolbarLayout.Music(_music);

        //Act
        IReadOnlyList<UIElement> items = Items(Bars(toolbar)[1]);

        //Assert — the three entries that are not buttons are the controls
        //upstream puts on the bar: a score chooser, a zoom chooser and a pager.
        items.Count.Should().Be(entries.Count);
        items.OfType<ComboBox>().Count().Should().Be(2);
        items.OfType<TextBox>().Count().Should().Be(1);
        items.OfType<ToolBarSeparator>().Count()
            .Should().Be(entries.Count(e => e.Kind == ToolbarEntryKind.Separator));
    }

    [Fact]
    public void every_button_carries_the_icon_its_action_names()
    {
        //Arrange
        MainToolbar toolbar = Build();

        //Act
        IReadOnlyList<ToolButton> buttons = Buttons(toolbar);

        //Assert — a name no set ships hands back nothing, and nothing is what
        //makes a button show its text instead of a blank square. Every name on
        //either bar ships in both sets, so every button has artwork.
        buttons.Should().NotBeEmpty();
        foreach (ToolButton button in buttons)
        {
            AppAction action = (AppAction)button.Command;
            button.Icon.Should().BeSameAs(IconTheme.Source(action.IconName));
            button.Icon.Should().NotBeNull();
        }
    }

    [Fact]
    public void the_open_button_is_a_drop_down_with_the_recent_files_flyout()
    {
        //Arrange
        MainToolbar toolbar = Build();

        //Act
        ToolDropDownButton open = (ToolDropDownButton)ButtonFor(toolbar, "file_open");

        //Assert — Qt's MenuButtonPopup: the main part opens a file, the arrow
        //part shows the recent ones. Upstream hangs this menu on the Open
        //button whatever verbose_toolbuttons says.
        open.PopupMode.Should().Be(PopupMode.MenuButton);
        open.Flyout.Should().BeOfType<MenuFlyout>();
        open.Command.Should().BeSameAs(_main.FileOpen);
    }

    [Fact]
    public void the_preference_changes_which_buttons_are_drop_downs_without_a_restart()
    {
        //Arrange — the same toolbar throughout, as the window keeps.
        MainToolbar toolbar = Build();
        IReadOnlyList<string> quietDropDowns = DropDownNames(toolbar);

        //Act — upstream's mainwindow.settingsChanged, which the window calls
        //from the preferences page.
        GeneralValues values = new GeneralValues();
        values.Load(_settings);
        values.VerboseToolButtons = true;
        values.Save(_settings);
        toolbar.SettingsChanged();

        //Assert
        quietDropDowns.Should().BeEquivalentTo(new[] { "file_open", "engrave_runner" });
        DropDownNames(toolbar).Should().BeEquivalentTo(
            new[] { "file_new", "file_open", "file_save", "file_close", "engrave_runner" });
    }

    [Fact]
    public void the_magnifier_is_a_toggle_and_a_click_flips_the_action_once()
    {
        //Arrange — the one checkable action on either bar.
        MainToolbar toolbar = Build();
        ToolToggleButton magnifier =
            (ToolToggleButton)ButtonFor(toolbar, "music_magnifier");
        bool before = _music.MusicMagnifier.IsChecked;
        int triggered = 0;
        _music.MusicMagnifier.Triggered += (_, _) => triggered++;

        //Act — the same code path a pointer takes.
        new ToolToggleButtonAutomationPeer(magnifier).Toggle();

        //Assert — the toggle flips its own state and the command flips the
        //action's, one flip each. Writing the action from the button as well
        //would cancel the two out and leave the magnifier where it was.
        triggered.Should().Be(1);
        _music.MusicMagnifier.IsChecked.Should().Be(!before);
        magnifier.IsChecked.Should().Be(_music.MusicMagnifier.IsChecked);
    }

    [Fact]
    public void a_disabled_action_disables_its_button()
    {
        //Arrange
        MainToolbar toolbar = Build();
        ToolButton back = ButtonFor(toolbar, "go_back");

        //Act — nothing sets IsEnabled on the button; it follows the command's
        //CanExecute, which AppAction answers from IsEnabled.
        _browser.GoBack.IsEnabled = false;
        bool disabled = back.IsEnabled;
        _browser.GoBack.IsEnabled = true;

        //Assert
        disabled.Should().BeFalse();
        back.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void a_buttons_composed_tooltip_is_the_one_fresco_writes()
    {
        //Arrange
        MainToolbar toolbar = Build();

        //Act
        ToolButton newDocument = ButtonFor(toolbar, "file_new");

        //Assert — the add-in composes it from the two strings the builder hands
        //over, and lands on the wording MainToolbarTests pins.
        newDocument.ComposedToolTipText.Should().Be("New Document (Ctrl+N)");
        newDocument.ComposedToolTipText
            .Should().Be(ToolbarLayout.ToolTipFor(_main.FileNew));
        ToolTipService.GetToolTip(newDocument).Should().Be("New Document (Ctrl+N)");
    }

    [Fact]
    public void the_engrave_buttons_icon_and_tooltip_follow_the_job()
    {
        //Arrange — engrave_runner is the one action whose tool tip is not its
        //label, so the application writes it and the add-in leaves it alone.
        MainToolbar toolbar = Build();
        ToolButton runner = ButtonFor(toolbar, "engrave_runner");
        object promise = ToolTipService.GetToolTip(runner);
        object idle = runner.Icon;

        //Act — what Engraver.UpdateActions does when a job starts.
        _engrave.EngraveRunner.IconName = "lilypond-stop";
        _engrave.EngraveRunner.ToolTip = "Abort engraving job";

        //Assert
        promise.Should().Be("Engrave (preview; Shift-click for custom)");
        idle.Should().BeSameAs(IconTheme.Source("lilypond-run"));
        runner.Icon.Should().BeSameAs(IconTheme.Source("lilypond-stop"));
        ToolTipService.GetToolTip(runner).Should().Be("Abort engraving job");
    }

    [Fact]
    public void the_bars_carry_the_presentation_upstream_asks_for()
    {
        //Arrange
        MainToolbar toolbar = Build();

        //Act — the three settings are inherited attached properties, set once
        //on the tray.
        double iconSize = ToolBarProperties.GetIconSize(toolbar);
        LabelMode labelMode = ToolBarProperties.GetLabelMode(toolbar);
        bool showToolTips = ToolBarProperties.GetShowToolTips(toolbar);

        //Assert — Qt's ToolButtonIconOnly at the size IconTheme names, with a
        //tool tip on every button.
        iconSize.Should().Be(IconTheme.ToolbarIconSize);
        labelMode.Should().Be(LabelMode.IconOnly);
        showToolTips.Should().BeTrue();
        ToolBarProperties.GetIconSize(Bars(toolbar)[0]).Should().Be(24d);
    }

    [Fact]
    public void a_narrow_bar_pushes_its_trailing_items_behind_the_chevron()
    {
        //Arrange — the behaviour that replaces the hand-built row's hidden
        //horizontal scrollbar.
        TestHost.EnsureReady();
        MainToolbar toolbar = Build();
        ToolBar mainBar = Bars(toolbar)[0];

        //Act
        mainBar.Measure(new Size(1200d, 100d));
        bool fitsWide = mainBar.HasOverflowItems;
        mainBar.Measure(new Size(90d, 100d));

        //Assert
        fitsWide.Should().BeFalse();
        mainBar.HasOverflowItems.Should().BeTrue();
        mainBar.OverflowItems.Should().NotBeEmpty();
    }

    /// <summary>Builds a toolbar over this fixture's own action collections.</summary>
    /// <returns>The toolbar, with both bars already built.</returns>
    private MainToolbar Build()
    {
        _main = new MainActions(_settings);
        _browser = new BrowserActions(_settings);
        _scoreWizard = new ScoreWizardActions(_settings);
        _engrave = new EngraveActions(_settings);
        _music = new MusicViewActions(_settings);

        MainToolbar toolbar = new MainToolbar(
            _main,
            _browser,
            _scoreWizard,
            _engrave,
            _music,
            new SnippetLibrary(_settings),
            new SnippetToolActions(_settings),
            _ => { },
            new RecentFiles(_settings),
            _ => { },
            _settings);

        //The window builds the bars when the tray enters the tree; nothing
        //enters a tree here, so the same rebuild is asked for directly.
        toolbar.SettingsChanged();
        return toolbar;
    }

    /// <summary>Answers the bars on a tray.</summary>
    /// <param name="toolbar">The toolbar.</param>
    /// <returns>The bars, in tray order.</returns>
    private static IReadOnlyList<ToolBar> Bars(MainToolbar toolbar)
        => toolbar.Children.OfType<ToolBar>().ToList();

    /// <summary>Answers the elements on a bar.</summary>
    /// <param name="bar">The bar.</param>
    /// <returns>The elements, in bar order.</returns>
    private static IReadOnlyList<UIElement> Items(ToolBar bar)
        => bar.Items.Cast<UIElement>().ToList();

    /// <summary>Answers every button on either bar.</summary>
    /// <param name="toolbar">The toolbar.</param>
    /// <returns>The buttons.</returns>
    private static IReadOnlyList<ToolButton> Buttons(MainToolbar toolbar)
        => Bars(toolbar).SelectMany(Items).OfType<ToolButton>().ToList();

    /// <summary>Answers the button a named command sits behind.</summary>
    /// <param name="toolbar">The toolbar.</param>
    /// <param name="name">The command's name.</param>
    /// <returns>The button.</returns>
    private static ToolButton ButtonFor(MainToolbar toolbar, string name)
        => Buttons(toolbar).Single(b => ((AppAction)b.Command).Name == name);

    /// <summary>Answers which commands are behind a drop-down button.</summary>
    /// <param name="toolbar">The toolbar.</param>
    /// <returns>The command names.</returns>
    private static IReadOnlyList<string> DropDownNames(MainToolbar toolbar)
        => Buttons(toolbar)
            .OfType<ToolDropDownButton>()
            .Select(b => ((AppAction)b.Command).Name)
            .ToList();
}
