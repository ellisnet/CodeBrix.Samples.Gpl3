// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Shell;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// How the window's working area shares its room out: which panes the panels
/// that are open ask for, which panes a maximize asks for, and what the
/// dividers open at.
/// </summary>
public class ShellLayoutTests
{
    // ----------------------------------------------------- the editor's share

    [Fact]
    public void the_editor_keeps_the_lions_share_of_the_width()
    {
        //Arrange
        ShellLayout layout = new ShellLayout();

        //Assert
        //The right strip takes about a quarter of the window and the editor
        //block the rest — the 3:1 the shell used to recompute every time a
        //panel came or went. The LEFT strip is the deliberate exception: it
        //opens wider than that, because Quick Insert does not read at a
        //quarter. The editor still keeps the larger share of its own block.
        layout.OuterStackPercent.Should().BeGreaterThan(layout.OuterSidePercent * 2d);
        layout.InnerStackPercent.Should().BeGreaterThan(layout.InnerSidePercent);
    }

    [Fact]
    public void the_left_strip_opens_wide_enough_to_read()
    {
        //Arrange
        ShellLayout layout = new ShellLayout();

        //Assert
        //Measured on X11 at the default window size with the Music View open,
        //which is the arrangement the left strip is narrowest in: two fifths of
        //the editor block is 380 pixels, where Quick Insert's four-tab row ends
        //whole and its Direction list, check box and Remove button all read in
        //full. A fifth put the strip on its own 200-pixel floor with the tab
        //row cut after "Dynamics"; a third still clipped the last tab. The
        //floor is unchanged — this moves where the divider OPENS, not how far
        //it can be dragged.
        layout.InnerSidePercent.Should().Be(ShellLayout.DefaultInnerSidePercent);
        layout.InnerSidePercent.Should().Be(40d);
        layout.InnerStackPercent.Should().Be(60d);
        (layout.InnerSidePercent + layout.InnerStackPercent).Should().Be(100d);
    }

    [Fact]
    public void the_editor_keeps_the_lions_share_of_the_height()
    {
        //Arrange
        ShellLayout layout = new ShellLayout();

        //Assert
        //The bottom strip takes about a fifth, which is the old 4:1.
        layout.OuterUpperPercent.Should().Be(80d);
        layout.OuterLowerPercent.Should().Be(20d);
    }

    [Fact]
    public void every_divider_opens_somewhere_usable()
    {
        //Arrange & Act
        ShellLayout layout = new ShellLayout();

        //Assert
        layout.IsUsable.Should().BeTrue();
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-10d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void a_share_that_is_not_a_width_makes_the_whole_arrangement_unusable(double share)
    {
        //Arrange
        ShellLayout layout = new ShellLayout { InnerSidePercent = share };

        //Assert
        //Zero is not a stored width, it is a shut strip — and which strips are
        //shut is the panel toggles' business, not the stored arrangement's.
        layout.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void an_arrangement_written_before_there_were_shares_is_unusable()
    {
        //Arrange
        //Every share left at zero is what deserializing an old payload gives,
        //because the old payload names none of them.
        ShellLayout layout = new ShellLayout
        {
            OuterSidePercent = 0d,
            OuterStackPercent = 0d,
            OuterUpperPercent = 0d,
            OuterLowerPercent = 0d,
            InnerSidePercent = 0d,
            InnerStackPercent = 0d,
        };

        //Assert
        layout.IsUsable.Should().BeFalse();
    }

    // ------------------------------------------------------- copying and comparing

    [Fact]
    public void one_arrangement_takes_anothers_shares()
    {
        //Arrange
        ShellLayout source = new ShellLayout
        {
            OuterSidePercent = 31d,
            OuterStackPercent = 69d,
            OuterUpperPercent = 72d,
            OuterLowerPercent = 28d,
            InnerSidePercent = 24d,
            InnerStackPercent = 76d,
        };
        ShellLayout target = new ShellLayout();

        //Act
        target.CopyFrom(source);

        //Assert
        target.Matches(source).Should().BeTrue();
        target.OuterSidePercent.Should().Be(31d);
        target.InnerStackPercent.Should().Be(76d);
    }

    [Fact]
    public void copying_from_nothing_leaves_the_shares_alone()
    {
        //Arrange
        ShellLayout layout = new ShellLayout { OuterSidePercent = 31d };

        //Act
        layout.CopyFrom(null);

        //Assert
        layout.OuterSidePercent.Should().Be(31d);
    }

    [Fact]
    public void a_gesture_that_moved_a_divider_does_not_match_what_came_before()
    {
        //Arrange
        ShellLayout before = new ShellLayout();
        ShellLayout after = new ShellLayout { OuterSidePercent = 31d, OuterStackPercent = 69d };

        //Assert
        //A gesture that moved nothing announces itself the same way a real drag
        //does, so this reading is what stops the window writing a file for it.
        after.Matches(before).Should().BeFalse();
        before.Matches(new ShellLayout()).Should().BeTrue();
        before.Matches(null).Should().BeFalse();
    }

    // ------------------------------------------------------------- the regions

    [Theory]
    [InlineData(DockArea.Left, ShellRegion.Left)]
    [InlineData(DockArea.Right, ShellRegion.Right)]
    [InlineData(DockArea.Bottom, ShellRegion.Bottom)]
    public void every_dock_area_is_a_region(DockArea area, ShellRegion expected)
    {
        //Assert
        ShellLayout.RegionOf(area).Should().Be(expected);
    }

    // ----------------------------------------- which panes the panels ask for

    [Fact]
    public void a_strip_with_a_panel_in_it_asks_for_its_pane()
    {
        //Act
        ShellPanes panes = ShellLayout.PanesFor(
            leftHasPanel: true, rightHasPanel: true, bottomHasPanel: true);

        //Assert
        panes.LeftStripIsOpen.Should().BeTrue();
        panes.RightStripIsOpen.Should().BeTrue();
        panes.BottomStripIsOpen.Should().BeTrue();
        panes.EditorIsOpen.Should().BeTrue();
        panes.EditorBlockIsOpen.Should().BeTrue();
    }

    [Fact]
    public void a_strip_with_nothing_in_it_takes_no_room()
    {
        //Act
        ShellPanes panes = ShellLayout.PanesFor(
            leftHasPanel: false, rightHasPanel: true, bottomHasPanel: false);

        //Assert
        panes.LeftStripIsOpen.Should().BeFalse();
        panes.BottomStripIsOpen.Should().BeFalse();
        panes.RightStripIsOpen.Should().BeTrue();
    }

    [Fact]
    public void closing_every_tool_leaves_the_editor_with_the_window()
    {
        //Act
        ShellPanes panes = ShellLayout.PanesFor(
            leftHasPanel: false, rightHasPanel: false, bottomHasPanel: false);

        //Assert
        //The last open region is never minimized: the editor is always one of
        //them, so neither control is ever asked to shut its last open pane.
        panes.EditorIsOpen.Should().BeTrue();
        panes.EditorBlockIsOpen.Should().BeTrue();
        panes.OuterKeepsAPaneOpen.Should().BeTrue();
        panes.InnerKeepsAPaneOpen.Should().BeTrue();
    }

    [Fact]
    public void re_showing_a_panel_asks_for_the_pane_again()
    {
        //Arrange
        ShellPanes shut = ShellLayout.PanesFor(
            leftHasPanel: false, rightHasPanel: true, bottomHasPanel: true);

        //Act
        ShellPanes shown = ShellLayout.PanesFor(
            leftHasPanel: true, rightHasPanel: true, bottomHasPanel: true);

        //Assert
        //Which share it comes back at is the control's own business: it
        //snapshots the share the strip had when it was shut, so nothing here
        //has to remember it.
        shut.LeftStripIsOpen.Should().BeFalse();
        shown.LeftStripIsOpen.Should().BeTrue();
        shown.RightStripIsOpen.Should().BeTrue();
        shown.BottomStripIsOpen.Should().BeTrue();
    }

    // ------------------------------------------------- which panes a maximize asks for

    [Fact]
    public void maximizing_the_right_strip_shuts_the_editor_block()
    {
        //Act
        ShellPanes panes = ShellLayout.PanesForMaximized(ShellRegion.Right);

        //Assert
        panes.RightStripIsOpen.Should().BeTrue();
        panes.EditorBlockIsOpen.Should().BeFalse();
        panes.BottomStripIsOpen.Should().BeFalse();
        panes.OuterKeepsAPaneOpen.Should().BeTrue();
    }

    [Fact]
    public void maximizing_the_bottom_strip_shuts_the_editor_block()
    {
        //Act
        ShellPanes panes = ShellLayout.PanesForMaximized(ShellRegion.Bottom);

        //Assert
        panes.BottomStripIsOpen.Should().BeTrue();
        panes.EditorBlockIsOpen.Should().BeFalse();
        panes.RightStripIsOpen.Should().BeFalse();
        panes.OuterKeepsAPaneOpen.Should().BeTrue();
    }

    [Fact]
    public void maximizing_the_left_strip_keeps_the_editor_block_around_it()
    {
        //Act
        ShellPanes panes = ShellLayout.PanesForMaximized(ShellRegion.Left);

        //Assert
        //The left strip is a pane of the INNER control, so the pane that holds
        //that control has to stay open for it to be seen at all.
        panes.LeftStripIsOpen.Should().BeTrue();
        panes.EditorBlockIsOpen.Should().BeTrue();
        panes.EditorIsOpen.Should().BeFalse();
        panes.RightStripIsOpen.Should().BeFalse();
        panes.BottomStripIsOpen.Should().BeFalse();
        panes.InnerKeepsAPaneOpen.Should().BeTrue();
    }

    [Fact]
    public void maximizing_the_editor_is_the_same_picture_as_closing_every_tool()
    {
        //Act
        ShellPanes maximized = ShellLayout.PanesForMaximized(ShellRegion.Editor);
        ShellPanes nothingOpen = ShellLayout.PanesFor(
            leftHasPanel: false, rightHasPanel: false, bottomHasPanel: false);

        //Assert
        //Which is what makes the editor's case reachable without a command of
        //its own: close every tool panel and the editor has the window.
        maximized.EditorIsOpen.Should().Be(nothingOpen.EditorIsOpen);
        maximized.EditorBlockIsOpen.Should().Be(nothingOpen.EditorBlockIsOpen);
        maximized.LeftStripIsOpen.Should().Be(nothingOpen.LeftStripIsOpen);
        maximized.RightStripIsOpen.Should().Be(nothingOpen.RightStripIsOpen);
        maximized.BottomStripIsOpen.Should().Be(nothingOpen.BottomStripIsOpen);
    }

    [Theory]
    [InlineData(ShellRegion.Left)]
    [InlineData(ShellRegion.Right)]
    [InlineData(ShellRegion.Bottom)]
    [InlineData(ShellRegion.Editor)]
    public void a_maximize_never_asks_a_control_to_shut_its_last_pane(ShellRegion region)
    {
        //Act
        ShellPanes panes = ShellLayout.PanesForMaximized(region);

        //Assert
        panes.OuterKeepsAPaneOpen.Should().BeTrue();
        if (panes.EditorBlockIsOpen) { panes.InnerKeepsAPaneOpen.Should().BeTrue(); }
    }

    [Theory]
    [InlineData(ShellRegion.Left)]
    [InlineData(ShellRegion.Right)]
    [InlineData(ShellRegion.Bottom)]
    [InlineData(ShellRegion.Editor)]
    public void a_maximize_leaves_exactly_one_region_open(ShellRegion region)
    {
        //Act
        ShellPanes panes = ShellLayout.PanesForMaximized(region);

        //Assert
        int open = 0;
        if (panes.RightStripIsOpen) { open++; }

        if (panes.BottomStripIsOpen) { open++; }

        if (panes.EditorBlockIsOpen && panes.LeftStripIsOpen) { open++; }

        if (panes.EditorBlockIsOpen && panes.EditorIsOpen) { open++; }

        open.Should().Be(1);
    }
}
