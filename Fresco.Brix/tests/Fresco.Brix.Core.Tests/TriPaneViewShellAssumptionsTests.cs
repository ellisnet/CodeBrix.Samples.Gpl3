// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.Toolkit;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// Everything the window's working area assumes about the pane control it is
/// built from, asserted against the control itself.
/// </summary>
/// <remarks>
/// <para>
/// The shell is two nested pane controls, the OUTER one holding the right strip
/// in its side pane, the bottom strip in its lower pane and the INNER one in
/// its upper pane; the inner one holds the left strip in its side pane and the
/// editor in its upper pane, and its lower pane is not a region of the window
/// at all. Both controls are built here exactly as
/// <c>DockShell</c> builds them, so a change in the control that would break
/// the shell fails here rather than on screen.
/// </para>
/// <para>
/// Nothing here draws. The control's default style never resolves in a process
/// with no application, so only the state the control deliberately keeps
/// outside its template — the shares, the minimized flags, and the refusals —
/// is asserted; divider visibility, chevrons, cursors and pixel sizes belong to
/// the X11 battery.
/// </para>
/// </remarks>
[Collection(XamlTestCollection.Name)]
public class TriPaneViewShellAssumptionsTests
{
    private static TriPaneView Outer() => new TriPaneView
    {
        SidePanePlacement = TriPaneViewSidePanePlacement.Right,
        SidePanePercent = 25d,
        StackPercent = 75d,
        UpperPanePercent = 80d,
        LowerPanePercent = 20d,
        SidePaneMinLength = 200d,
        StackMinLength = 200d,
        UpperPaneMinLength = 200d,
        LowerPaneMinLength = 80d,
        IsDragToMinimizeEnabled = false,
        RestoreGripMode = TriPaneViewRestoreGripMode.Never,
    };

    private static TriPaneView Inner() => new TriPaneView
    {
        SidePanePlacement = TriPaneViewSidePanePlacement.Left,
        SidePanePercent = 20d,
        StackPercent = 80d,
        UpperPanePercent = 100d,
        LowerPanePercent = 0d,
        SidePaneMinLength = 200d,
        StackMinLength = 200d,
        IsDragToMinimizeEnabled = false,
        RestoreGripMode = TriPaneViewRestoreGripMode.Never,
    };

    [Fact]
    public void a_pane_control_is_built_with_no_window_at_all()
    {
        //Arrange & Act
        TriPaneView panes = new TriPaneView();

        //Assert
        panes.Should().NotBeNull();
        panes.SidePanePlacement.Should().Be(TriPaneViewSidePanePlacement.Left);
        panes.RestoreGripMode.Should().Be(TriPaneViewRestoreGripMode.Auto);
        panes.IsDragToMinimizeEnabled.Should().BeFalse();
    }

    [Fact]
    public void the_object_initializer_route_survives_construction()
    {
        //Arrange & Act
        TriPaneView panes = Outer();

        //Assert
        //Everything the shell sets before the control is anywhere near a window.
        panes.SidePanePlacement.Should().Be(TriPaneViewSidePanePlacement.Right);
        panes.SidePanePercent.Should().Be(25d);
        panes.StackPercent.Should().Be(75d);
        panes.UpperPanePercent.Should().Be(80d);
        panes.LowerPanePercent.Should().Be(20d);
        panes.SidePaneMinLength.Should().Be(200d);
        panes.LowerPaneMinLength.Should().Be(80d);
        panes.RestoreGripMode.Should().Be(TriPaneViewRestoreGripMode.Never);
    }

    [Fact]
    public void a_pane_control_is_accepted_as_another_ones_pane()
    {
        //Arrange
        TriPaneView inner = Inner();

        //Act
        TriPaneView outer = new TriPaneView { UpperPane = inner };

        //Assert
        outer.UpperPane.Should().BeSameAs(inner);
    }

    [Fact]
    public void minimizing_a_pane_zeroes_its_share_and_sets_its_flag()
    {
        //Arrange
        TriPaneView outer = Outer();

        //Act
        outer.MinimizeLowerPane();

        //Assert
        outer.IsLowerPaneMinimized.Should().BeTrue();
        outer.LowerPanePercent.Should().Be(0d);
        outer.IsUpperPaneMinimized.Should().BeFalse();
        outer.IsSidePaneMinimized.Should().BeFalse();
    }

    [Fact]
    public void restoring_a_pane_returns_the_share_it_was_minimized_at()
    {
        //Arrange
        TriPaneView outer = Outer();
        outer.MinimizeLowerPane();

        //Act
        outer.RestoreLowerPane();

        //Assert
        //Not the control's own default of fifty: the shell's twenty, which is
        //why every strip that starts shut is MINIMIZED rather than set to zero.
        outer.IsLowerPaneMinimized.Should().BeFalse();
        outer.LowerPanePercent.Should().Be(20d);
    }

    [Fact]
    public void minimizing_a_pane_that_is_already_shut_keeps_its_snapshot()
    {
        //Arrange
        TriPaneView outer = Outer();
        outer.MinimizeLowerPane();

        //Act
        //The shell asks for the arrangement it wants as a whole rather than
        //working out what has changed, so it shuts panes that are already shut.
        outer.MinimizeLowerPane();
        outer.RestoreLowerPane();

        //Assert
        outer.LowerPanePercent.Should().Be(20d);
    }

    [Fact]
    public void restoring_a_pane_that_is_already_open_changes_nothing()
    {
        //Arrange
        TriPaneView outer = Outer();

        //Act
        outer.RestoreSidePane();

        //Assert
        outer.SidePanePercent.Should().Be(25d);
        outer.StackPercent.Should().Be(75d);
        outer.IsSidePaneMinimized.Should().BeFalse();
    }

    [Fact]
    public void minimizing_the_last_open_pane_is_refused()
    {
        //Arrange
        TriPaneView outer = Outer();
        outer.MinimizeLowerPane();
        outer.MinimizeUpperPane();

        //Act
        outer.MinimizeSidePane();

        //Assert
        //Which is why the shell opens every pane that is to be open before it
        //shuts any that is to be shut.
        outer.IsSidePaneMinimized.Should().BeFalse();
        outer.SidePanePercent.Should().Be(25d);
    }

    [Fact]
    public void maximizing_the_right_strip_collapses_the_outer_stack()
    {
        //Arrange
        TriPaneView outer = Outer();

        //Act
        outer.MinimizeUpperPane();
        outer.MinimizeLowerPane();

        //Assert
        outer.StackPercent.Should().Be(0d);
        outer.SidePanePercent.Should().Be(25d);
        outer.IsSidePaneMinimized.Should().BeFalse();
    }

    [Fact]
    public void the_right_strip_gives_the_window_back_pane_by_pane()
    {
        //Arrange
        TriPaneView outer = Outer();
        outer.MinimizeUpperPane();
        outer.MinimizeLowerPane();

        //Act
        outer.RestoreUpperPane();
        outer.RestoreLowerPane();

        //Assert
        outer.StackPercent.Should().Be(75d);
        outer.UpperPanePercent.Should().Be(80d);
        outer.LowerPanePercent.Should().Be(20d);
        outer.SidePanePercent.Should().Be(25d);
    }

    [Fact]
    public void maximizing_the_bottom_strip_leaves_it_holding_the_outer_control()
    {
        //Arrange
        TriPaneView outer = Outer();

        //Act
        outer.MinimizeUpperPane();
        outer.MinimizeSidePane();

        //Assert
        outer.IsUpperPaneMinimized.Should().BeTrue();
        outer.IsSidePaneMinimized.Should().BeTrue();
        outer.IsLowerPaneMinimized.Should().BeFalse();
        outer.LowerPanePercent.Should().Be(20d);
    }

    [Fact]
    public void maximizing_the_left_strip_leaves_it_holding_the_inner_control()
    {
        //Arrange
        //The inner control's lower pane is not a region of the window and is
        //held at zero for the life of the window, so minimizing the upper pane
        //leaves the side pane alone with the control. Assume nothing here: this
        //is the one case where a pane is shut from code while its partner is
        //already at zero.
        TriPaneView inner = Inner();

        //Act
        inner.MinimizeUpperPane();

        //Assert
        inner.IsUpperPaneMinimized.Should().BeTrue();
        inner.IsSidePaneMinimized.Should().BeFalse();
        inner.SidePanePercent.Should().Be(20d);
        inner.StackPercent.Should().Be(0d);
    }

    [Fact]
    public void the_left_strip_gives_the_editor_back_at_the_share_it_had()
    {
        //Arrange
        TriPaneView inner = Inner();
        inner.MinimizeUpperPane();

        //Act
        inner.RestoreUpperPane();

        //Assert
        inner.IsUpperPaneMinimized.Should().BeFalse();
        inner.UpperPanePercent.Should().Be(100d);
        inner.StackPercent.Should().Be(80d);
        inner.SidePanePercent.Should().Be(20d);
    }

    [Fact]
    public void maximizing_the_editor_leaves_it_holding_the_inner_control()
    {
        //Arrange
        TriPaneView inner = Inner();

        //Act
        inner.MinimizeSidePane();

        //Assert
        inner.IsSidePaneMinimized.Should().BeTrue();
        inner.SidePanePercent.Should().Be(0d);
        inner.IsUpperPaneMinimized.Should().BeFalse();
        inner.UpperPanePercent.Should().Be(100d);
    }

    [Fact]
    public void restore_all_opens_a_lower_pane_that_was_meant_to_stay_shut()
    {
        //Arrange
        //The inner control's lower pane is not one of the window's regions.
        TriPaneView inner = Inner();
        inner.MinimizeUpperPane();

        //Act
        //The obvious way to undo a maximize, and the wrong one here.
        inner.RestoreAll();

        //Assert
        //A pane held at zero beside a pane that is not reads as minimized, so
        //restoring everything gives it the control's own default of fifty and
        //the window grows a region it does not have. The shell restores pane by
        //pane for exactly this reason.
        inner.LowerPanePercent.Should().Be(50d);
        inner.IsLowerPaneMinimized.Should().BeFalse();
    }

    [Fact]
    public void restoring_pane_by_pane_leaves_the_unused_lower_pane_shut()
    {
        //Arrange
        TriPaneView inner = Inner();
        inner.MinimizeUpperPane();

        //Act
        inner.RestoreUpperPane();

        //Assert
        inner.LowerPanePercent.Should().Be(0d);
        inner.IsLowerPaneMinimized.Should().BeTrue();
        inner.UpperPanePercent.Should().Be(100d);
        inner.StackPercent.Should().Be(80d);
    }
}
