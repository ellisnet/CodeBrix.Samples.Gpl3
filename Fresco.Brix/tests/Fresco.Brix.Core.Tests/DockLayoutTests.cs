// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The window arrangement that survives a quit: what is written, what comes
/// back, and what a store with nothing in it answers.
/// </summary>
public class DockLayoutTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "frescobrix-docklayout-" + Guid.NewGuid().ToString("N"));

    private readonly SettingsStore _settings;

    /// <summary>Creates the fixture with a store of its own.</summary>
    public DockLayoutTests()
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

    private static DockLayout ThreePanelsInTwoAreas() => new DockLayout
    {
        Panels = new List<DockPanelState>
        {
            new DockPanelState
            {
                Name = "musicview", Area = DockArea.Right, IsActive = true,
            },
            new DockPanelState
            {
                Name = "docbrowser", Area = DockArea.Right, IsActive = false,
            },
            new DockPanelState
            {
                Name = "logtool", Area = DockArea.Bottom, IsActive = true,
            },
        },
        Sizes = new ShellLayout
        {
            OuterSidePercent = 31d,
            OuterStackPercent = 69d,
            OuterUpperPercent = 72d,
            OuterLowerPercent = 28d,
            InnerSidePercent = 24d,
            InnerStackPercent = 76d,
        },
    };

    // -------------------------------------------------------------- the keys

    [Fact]
    public void the_keys_are_the_ones_upstream_writes()
    {
        //Assert
        //mainwindow.py:406-409 — settings.beginGroup('mainwindow') then
        //setValue("size", ...) and setValue('state', ...).
        DockLayout.StateKey.Should().Be("mainwindow/state");
        DockLayout.SizeKey.Should().Be("mainwindow/size");
    }

    // ---------------------------------------------------------- round tripping

    [Fact]
    public void an_arrangement_comes_back_exactly_as_it_was_written()
    {
        //Arrange
        DockLayout written = ThreePanelsInTwoAreas();

        //Act
        written.Save(_settings);
        DockLayout read = DockLayout.Load(_settings);

        //Assert
        read.Panels.Count.Should().Be(3);
        read.Panels.Select(p => p.Name).Should()
            .Equal(new[] { "musicview", "docbrowser", "logtool" });
        read.Panels.Select(p => p.Area).Should()
            .Equal(new[] { DockArea.Right, DockArea.Right, DockArea.Bottom });
        read.Panels.Select(p => p.IsActive).Should()
            .Equal(new[] { true, false, true });
        read.Sizes.OuterSidePercent.Should().Be(31d);
        read.Sizes.OuterStackPercent.Should().Be(69d);
        read.Sizes.OuterUpperPercent.Should().Be(72d);
        read.Sizes.OuterLowerPercent.Should().Be(28d);
        read.Sizes.InnerSidePercent.Should().Be(24d);
        read.Sizes.InnerStackPercent.Should().Be(76d);
        read.Sizes.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void an_arrangement_that_recorded_no_dividers_opens_at_the_defaults()
    {
        //Arrange
        DockLayout written = new DockLayout
        {
            Panels =
            {
                new DockPanelState
                {
                    Name = "musicview", Area = DockArea.Right, IsActive = true,
                },
            },
        };

        //Act
        written.Save(_settings);
        DockLayout read = DockLayout.Load(_settings);

        //Assert
        read.Sizes.Should().NotBeNull();
        read.Sizes.OuterSidePercent.Should().Be(ShellLayout.DefaultOuterSidePercent);
        read.Sizes.InnerSidePercent.Should().Be(ShellLayout.DefaultInnerSidePercent);
        read.Sizes.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void an_arrangement_written_by_the_shell_that_came_before_yields_defaults()
    {
        //Arrange
        //The shell that came before this one stored two lists of splitter
        //weights whose length followed how many areas were on screen. Exactly that payload, written into the
        //store the way the shell that came before this one wrote it.
        _settings.Set(
            DockLayout.StateKey,
            new
            {
                Panels = new[]
                {
                    new { Name = "logtool", Area = DockArea.Bottom, IsActive = true },
                },
                MiddleSizes = new[] { 1.0, 3.0, 1.0 },
                OuterSizes = new[] { 4.0, 1.0 },
            });

        //Act
        DockLayout read = DockLayout.Load(_settings);

        //Assert
        //It is read for its panels and nothing else: there is no migration, so
        //the dividers open where the shell says they should rather than being
        //given weights written for a shell with different panes.
        read.Should().NotBeNull();
        read.Panels.Count.Should().Be(1);
        read.ActiveIn(DockArea.Bottom).Should().Be("logtool");
        read.Sizes.IsUsable.Should().BeTrue();
        read.Sizes.Matches(new ShellLayout()).Should().BeTrue();
    }

    [Fact]
    public void an_arrangement_whose_dividers_make_no_sense_is_not_applied()
    {
        //Arrange
        DockLayout written = new DockLayout
        {
            Panels =
            {
                new DockPanelState
                {
                    Name = "logtool", Area = DockArea.Bottom, IsActive = true,
                },
            },
            Sizes = new ShellLayout { OuterSidePercent = 0d, OuterStackPercent = 100d },
        };

        //Act
        written.Save(_settings);
        DockLayout read = DockLayout.Load(_settings);

        //Assert
        //A share of zero says "that strip is shut", which is the panel toggles'
        //business; the shell asks this before it hands the shares over.
        read.Panels.Count.Should().Be(1);
        read.Sizes.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void the_tab_order_within_an_area_is_the_order_it_was_written_in()
    {
        //Arrange
        ThreePanelsInTwoAreas().Save(_settings);

        //Act
        IReadOnlyList<DockPanelState> right
            = DockLayout.Load(_settings).PanelsIn(DockArea.Right);

        //Assert
        right.Select(p => p.Name).Should().Equal(new[] { "musicview", "docbrowser" });
    }

    [Fact]
    public void each_area_remembers_which_of_its_tabs_was_showing()
    {
        //Arrange
        ThreePanelsInTwoAreas().Save(_settings);
        DockLayout layout = DockLayout.Load(_settings);

        //Assert
        layout.ActiveIn(DockArea.Right).Should().Be("musicview");
        layout.ActiveIn(DockArea.Bottom).Should().Be("logtool");
        layout.ActiveIn(DockArea.Left).Should().BeNull();
    }

    [Fact]
    public void an_area_with_no_panel_in_it_names_none()
    {
        //Arrange
        DockLayout layout = ThreePanelsInTwoAreas();

        //Assert
        layout.PanelsIn(DockArea.Left).Count.Should().Be(0);
        layout.ActiveIn(DockArea.Left).Should().BeNull();
    }

    [Fact]
    public void an_area_that_recorded_no_active_tab_names_none()
    {
        //Arrange
        DockLayout layout = new DockLayout
        {
            Panels = new List<DockPanelState>
            {
                new DockPanelState
                {
                    Name = "outline", Area = DockArea.Left, IsActive = false,
                },
            },
        };

        //Assert
        layout.ActiveIn(DockArea.Left).Should().BeNull();
    }

    // ------------------------------------------------------- nothing recorded

    [Fact]
    public void a_store_that_has_never_been_written_answers_an_empty_arrangement()
    {
        //Act
        DockLayout layout = DockLayout.Load(_settings);

        //Assert
        //A first launch must leave the window at the head's own defaults.
        layout.Should().NotBeNull();
        layout.IsEmpty.Should().Be(true);
        layout.Panels.Count.Should().Be(0);
    }

    [Fact]
    public void an_arrangement_with_no_panel_open_is_empty()
    {
        //Arrange
        DockLayout layout = new DockLayout
        {
            Sizes = new ShellLayout { OuterSidePercent = 31d, OuterStackPercent = 69d },
        };

        //Assert
        layout.IsEmpty.Should().Be(true);
    }

    [Fact]
    public void an_arrangement_with_a_panel_open_is_not_empty()
    {
        //Assert
        ThreePanelsInTwoAreas().IsEmpty.Should().Be(false);
    }

    [Fact]
    public void there_is_no_store_to_read_or_write()
    {
        //Act
        DockLayout layout = DockLayout.Load(null);
        Action save = () => ThreePanelsInTwoAreas().Save(null);

        //Assert
        layout.IsEmpty.Should().Be(true);
        save.Should().NotThrow();
    }

    // ----------------------------------------------------------- window size

    [Fact]
    public void the_window_size_comes_back_as_it_was_written()
    {
        //Act
        DockLayout.SaveWindowSize(_settings, 1100, 700);
        (int Width, int Height) size = DockLayout.LoadWindowSize(_settings);

        //Assert
        size.Width.Should().Be(1100);
        size.Height.Should().Be(700);
    }

    [Fact]
    public void the_size_a_launch_reads_is_the_size_the_next_launch_writes()
    {
        //Arrange
        //What makes the window come back the same size is that the quantity
        //stored is the quantity the head is handed to open the window with, so
        //that reading it and writing it again changes nothing. The window class
        //holds up its end by saving AppWindow.Size, which is exactly what
        //AppWindow.Resize consumes; this is the store's end of the same round
        //trip, which has to survive being replayed launch after launch.
        DockLayout.SaveWindowSize(_settings, 1260, 790);

        //Act
        (int Width, int Height) first = DockLayout.LoadWindowSize(_settings);
        DockLayout.SaveWindowSize(_settings, first.Width, first.Height);
        (int Width, int Height) second = DockLayout.LoadWindowSize(_settings);
        DockLayout.SaveWindowSize(_settings, second.Width, second.Height);
        (int Width, int Height) third = DockLayout.LoadWindowSize(_settings);

        //Assert
        first.Should().Be((1260, 790));
        second.Should().Be(first);
        third.Should().Be(first);
    }

    [Fact]
    public void a_size_that_was_never_written_is_no_size_at_all()
    {
        //Act
        (int Width, int Height) size = DockLayout.LoadWindowSize(_settings);

        //Assert
        size.Width.Should().Be(0);
        size.Height.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 700)]
    [InlineData(1100, 0)]
    [InlineData(-5, -5)]
    public void a_window_that_has_no_real_size_yet_writes_nothing(int width, int height)
    {
        //Arrange
        DockLayout.SaveWindowSize(_settings, 1100, 700);

        //Act
        DockLayout.SaveWindowSize(_settings, width, height);

        //Assert
        //The good value stands: a window the head has not laid out yet must not
        //be able to overwrite the size the user left.
        DockLayout.LoadWindowSize(_settings).Width.Should().Be(1100);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1100")]
    [InlineData("1100 700 40")]
    [InlineData("wide tall")]
    [InlineData("0 700")]
    [InlineData("1100 -700")]
    public void a_stored_size_that_makes_no_sense_is_no_size_at_all(string stored)
    {
        //Arrange
        _settings.SetString(DockLayout.SizeKey, stored);

        //Act
        (int Width, int Height) size = DockLayout.LoadWindowSize(_settings);

        //Assert
        size.Width.Should().Be(0);
        size.Height.Should().Be(0);
    }

    [Fact]
    public void the_size_is_written_in_the_invariant_culture()
    {
        //Act
        DockLayout.SaveWindowSize(_settings, 1920, 1080);

        //Assert
        _settings.GetString(DockLayout.SizeKey).Should().Be("1920 1080");
    }

    [Fact]
    public void a_maximize_does_not_disturb_what_is_recorded()
    {
        //Arrange
        //This case used to fence DockShell.RememberShowingTabs, which
        //existed because a maximize HID every other panel and the tab each area
        //had up was lost with them. A maximize collapses the other panes now
        //and hides nothing, so there is nothing to remember and nothing to fold
        //back in: what a quit from Music > Maximize records is simply what is
        //open, which is what this asserts.
        DockLayout recorded = new DockLayout
        {
            Panels =
            {
                new DockPanelState
                {
                    Name = "musicview", Area = DockArea.Right, IsActive = true,
                },
                new DockPanelState
                {
                    Name = "logtool", Area = DockArea.Bottom, IsActive = true,
                },
                new DockPanelState
                {
                    Name = "snippettool", Area = DockArea.Left, IsActive = true,
                },
            },
        };

        //Act
        recorded.Save(_settings);
        DockLayout read = DockLayout.Load(_settings);

        //Assert
        //Every area still names the tab it had up, the maximized panel's own
        //area included — nothing was hidden, so nothing had to be restored to
        //the record before it was written.
        read.ActiveIn(DockArea.Right).Should().Be("musicview");
        read.ActiveIn(DockArea.Bottom).Should().Be("logtool");
        read.ActiveIn(DockArea.Left).Should().Be("snippettool");
        read.Panels.Count.Should().Be(3);
    }
}
