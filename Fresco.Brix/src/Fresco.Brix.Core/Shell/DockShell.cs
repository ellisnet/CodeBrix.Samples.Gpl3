// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.Toolkit;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Shell; //was previously: the QMainWindow dock arrangement Frescobaldi builds

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The window's working area: the editor in the middle, with tool panels
/// docked left, right and along the bottom, each area a tab strip of whichever
/// panels are currently shown, and draggable dividers between them.
/// <para>
/// An area with no visible panel takes no space at all, so a user who opens no
/// tools sees nothing but the editor.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// The working area is two nested CodeBrix.Platform <c>TriPaneView</c> controls
/// rather than the nested splitters it used to be. A pane control offers three
/// regions and the window needs four, so the OUTER control keeps the right
/// strip in its side pane, the bottom strip in its lower pane, and the whole of
/// the rest of the window in its upper pane — where the INNER control keeps the
/// left strip in its side pane and the editor in its upper pane. The inner
/// control's lower pane is not one of the window's regions; it is held shut for
/// the life of the window, which is why the inner stack divider is never drawn.
/// Three dividers are on screen, exactly as before: the outer side divider
/// between the editor block and the right strip, the outer stack divider
/// between the editor block and the bottom strip, and the inner side divider
/// between the left strip and the editor.
/// </para>
/// <para>
/// ⚠ DECLARED DIVERGENCE, on the LEFT side only. Frescobaldi is a
/// <c>QMainWindow</c> whose four dock areas own the corners the Qt way, so
/// upstream's Log stops at the left dock and at the right dock, and neither the
/// Quick Insert panel nor the Music View is ever run under. Here the Music View
/// gets that treatment exactly — the right strip runs the full height of the
/// window and the bottom strip stops dead at the outer side divider — but the
/// bottom strip DOES run under the left strip, because the bottom strip and the
/// left strip belong to different controls and only one of the two can own that
/// corner. The right-hand corner is the one worth having: the Music View is the
/// panel a user works beside all day, where every panel of the left strip is
/// hidden until it is asked for. The shell that came before this one ran the
/// bottom strip under BOTH docks, so this is half of an old divergence retired
/// rather than a new one taken on.
/// </para>
/// <para>
/// A strip is created once and then stays in its pane for the life of the
/// window. Showing and hiding an area is the pane opening and shutting, not the
/// strip being taken out of a container and put back: the tab strip and every
/// panel widget in it stay in the visual tree, keeping their scroll positions
/// and their selections, and nothing has to be re-parented.
/// </para>
/// </remarks>
public sealed class DockShell
{
    //The narrowest the editor is ever laid out at. It is the outer control's
    //StackMinLength when the left strip is shut, and part of it when the strip
    //is open — see ApplyPanes.
    private const double EditorMinLength = 200d;

    //The editor's lion's share, the floors under a drag, and the scroll policy
    //are all set here rather than after the fact: a percent written in an
    //object initializer is already in force on the first frame drawn, so
    //nothing has to wait for the window to load. The three vertical scroll
    //settings are what make the panes FILL: every pane of a pane control sits
    //in a scroll viewer, and a scroll viewer measures its content with
    //unbounded height unless its vertical scroll bar is Disabled — with the
    //default the editor, the log, the tree views and every FillGrid panel would
    //come out a few pixels tall. The outer upper pane's setting is the one that
    //keeps the INNER control from being measured unbounded.
    private readonly TriPaneView _inner = new TriPaneView
    {
        SidePanePlacement = TriPaneViewSidePanePlacement.Left,
        SidePanePercent = ShellLayout.DefaultInnerSidePercent,
        StackPercent = ShellLayout.DefaultInnerStackPercent,
        UpperPanePercent = 100d,

        //Not a region of the window. Held at zero for the life of the window,
        //which with RestoreGripMode.Never means the inner stack divider is
        //never drawn and no grip is ever offered for it.
        LowerPanePercent = 0d,
        SidePaneMinLength = 200d,
        StackMinLength = 200d,
        SidePaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        UpperPaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        LowerPaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsDragToMinimizeEnabled = false,
        RestoreGripMode = TriPaneViewRestoreGripMode.Never,
    };

    private readonly TriPaneView _outer = new TriPaneView
    {
        SidePanePlacement = TriPaneViewSidePanePlacement.Right,
        SidePanePercent = ShellLayout.DefaultOuterSidePercent,
        StackPercent = ShellLayout.DefaultOuterStackPercent,
        UpperPanePercent = ShellLayout.DefaultOuterUpperPercent,
        LowerPanePercent = ShellLayout.DefaultOuterLowerPercent,
        SidePaneMinLength = 200d,
        StackMinLength = EditorMinLength,
        UpperPaneMinLength = 200d,
        LowerPaneMinLength = 80d,
        SidePaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        UpperPaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        LowerPaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsDragToMinimizeEnabled = false,
        RestoreGripMode = TriPaneViewRestoreGripMode.Never,
    };

    //The shares the user last had, kept here rather than read off the controls
    //at the last moment: a shut strip's own share reads as zero, and writing
    //THAT out would bring the strip back with no width at all on the next
    //launch. An axis is recorded only while both of its panes are open, so a
    //strip that is shut keeps the width it had when it was last on screen.
    private readonly ShellLayout _sizes = new ShellLayout();

    private readonly Dictionary<DockArea, TabView> _areas
        = new Dictionary<DockArea, TabView>();
    private readonly List<Panel> _panels = new List<Panel>();
    private UIElement _center;
    private Panel _maximized;

    /// <summary>Creates the shell.</summary>
    public DockShell()
    {
        _outer.UpperPane = _inner;

        //Every strip starts shut, because no panel is visible yet. Minimizing
        //rather than writing a zero percent is what gives each one a snapshot
        //of the weight it should come back at; a pane that was simply set to
        //zero has nothing to go back to and reopens at the control's own class
        //default instead of the share this shell chose.
        _outer.MinimizeSidePane();
        _outer.MinimizeLowerPane();
        _inner.MinimizeSidePane();

        _outer.DividerDragCompleted += (_, _) => OnDividerDragCompleted();
        _inner.DividerDragCompleted += (_, _) => OnDividerDragCompleted();
    }

    /// <summary>Raised when the user has finished dragging a divider.</summary>
    /// <remarks>The window writes the arrangement out on this as well as on the
    /// way out, so a divider the user moved is where they left it on the next
    /// launch. A gesture that moved nothing does not raise it.</remarks>
    public event EventHandler LayoutChanged;

    /// <summary>Gets the element the window puts in its content host.</summary>
    /// <remarks>A pane control is sealed, so this shell OWNS one rather than
    /// being one.</remarks>
    public UIElement Root => _outer;

    /// <summary>Gets or sets what sits in the middle — the editor area.</summary>
    public UIElement Center
    {
        get => _center;
        set
        {
            _center = value;
            _inner.UpperPane = _center;
        }
    }

    /// <summary>Gets the panels the shell knows about.</summary>
    public IReadOnlyList<Panel> Panels => _panels;

    /// <summary>Gets the panel filling the window, or null.</summary>
    public Panel MaximizedPanel => _maximized;

    /// <summary>Adds a panel and watches it for show/hide.</summary>
    /// <param name="panel">The panel.</param>
    public void AddPanel(Panel panel)
    {
        if (panel == null) { throw new ArgumentNullException(nameof(panel)); }

        _panels.Add(panel);
        panel.VisibilityChanged += (_, _) =>
        {
            //Closing the maximized panel puts the window back rather than
            //leaving it empty.
            if (ReferenceEquals(panel, _maximized) && !panel.IsVisible)
            {
                RestoreFromMaximized();
            }

            Refresh(panel);
        };
        panel.Activated += (_, _) => BringToFront(panel);
        if (panel.IsVisible)
        {
            Refresh(panel);
        }
    }

    /// <summary>Brings a panel to the front of its area, showing it first.</summary>
    /// <param name="panel">The panel.</param>
    public void BringToFront(Panel panel)
    {
        if (panel == null) { return; }

        if (!panel.IsVisible)
        {
            panel.IsVisible = true;
        }

        if (!_areas.TryGetValue(panel.Area, out var view)) { return; }

        TabViewItem tab = view.TabItems.OfType<TabViewItem>()
            .FirstOrDefault(t => ReferenceEquals(t.Tag, panel));
        if (tab != null)
        {
            view.SelectedItem = tab;
        }
    }

    /// <summary>
    /// Gives one panel the whole window, collapsing every other pane until
    /// <see cref="RestoreFromMaximized"/> opens them again.
    /// </summary>
    /// <param name="panel">The panel to maximize.</param>
    /// <remarks>
    /// <para>
    /// ⚠ MECHANISM DIVERGENCE, declared here. Upstream's
    /// <c>panel.Panel.maximize</c> is two lines — <c>setFloating(True)</c> then
    /// <c>showMaximized()</c> — because a Qt dock widget can leave the window
    /// and become a top-level one; it is then restored by re-docking it, so
    /// upstream's action has nothing to undo. This shell is "a modest app-level
    /// dock shell" with no floating dock widgets at all, so the same INTENT —
    /// give this panel the whole screen area — is carried out inside the
    /// window, and because there is no float to re-dock, invoking the command
    /// again is what puts the layout back. What the user sees is what upstream's
    /// user sees: the Music View filling everything.
    /// </para>
    /// <para>
    /// Nothing is hidden to make room. The OTHER PANES are collapsed and every
    /// panel stays exactly as it was — visible, in its own tab, with its Tools
    /// menu still checked, and with its scroll position and its selection
    /// intact, because no widget ever leaves the visual tree. That is the one
    /// visible difference from the shell that came before this one, which hid
    /// every other panel and pulled the editor out of its container, and lost
    /// their state doing it.
    /// </para>
    /// </remarks>
    public void MaximizePanel(Panel panel)
    {
        if (panel == null) { return; }

        panel.IsVisible = true;
        BringToFront(panel);

        _maximized = panel;
        ApplyPanes(ShellLayout.PanesForMaximized(ShellLayout.RegionOf(panel.Area)));
    }

    /// <summary>Puts the layout back the way it was before a maximize.</summary>
    /// <remarks>Pane by pane, and never through the control's own "restore
    /// everything": the inner control's lower pane is not one of the window's
    /// regions, and a pane held at zero beside a pane that is not reads as
    /// minimized, so restoring everything would open a fourth region this
    /// window does not have. Nothing has to be re-shown or re-raised, because
    /// nothing was hidden.</remarks>
    public void RestoreFromMaximized()
    {
        if (_maximized == null) { return; }

        Panel wasMaximized = _maximized;
        _maximized = null;
        ApplyPanes(WantedPanes());

        //Only a panel that is still open is raised. Closing the maximized panel
        //is one of the two ways out of a maximize, and raising it here would
        //open again the very panel the user has just closed.
        if (wasMaximized.IsVisible) { BringToFront(wasMaximized); }
    }

    /// <summary>
    /// Reads the arrangement out of the shell, so it can be put back after a
    /// relaunch.
    /// </summary>
    /// <returns>What is open, where, in what tab order, and how big.</returns>
    /// <remarks>Upstream's <c>QMainWindow.saveState()</c>, called from
    /// <c>mainwindow.py</c>'s <c>writeSettings</c> — see
    /// <see cref="DockLayout"/> for the declared difference of mechanism.
    /// A maximize disturbs nothing that is recorded here: every panel is still
    /// open, in its own area, with its own tab up, so quitting from
    /// Music &gt; Maximize records the user's own arrangement without having to
    /// reconstruct it.</remarks>
    public DockLayout CaptureLayout()
    {
        DockLayout layout = new DockLayout();
        foreach (var pair in _areas)
        {
            TabView view = pair.Value;
            foreach (var tab in view.TabItems.OfType<TabViewItem>())
            {
                if (tab.Tag is not Panel panel) { continue; }

                layout.Panels.Add(new DockPanelState
                {
                    Name = panel.Name,
                    Area = pair.Key,
                    IsActive = ReferenceEquals(view.SelectedItem, tab),
                });
            }
        }

        RecordSizes();
        layout.Sizes.CopyFrom(_sizes);
        return layout;
    }

    /// <summary>Puts a remembered arrangement back.</summary>
    /// <param name="layout">The arrangement, or null.</param>
    /// <remarks>
    /// <para>
    /// Upstream's <c>restoreState()</c>, called from <c>readSettings</c> as the
    /// window is built. Called ONCE, while the window is being built and
    /// before the user can have moved anything: it opens the panels in the
    /// stored tab order, raises the one that was showing in each area, and only
    /// THEN gives the dividers their shares, because opening an area moves them.
    /// A stored panel this build no longer offers is skipped rather than
    /// refusing the whole arrangement.
    /// </para>
    /// <para>
    /// The arrangement the <see cref="SplitContainer"/> shell that came before
    /// this one wrote held two lists of weights whose length followed how many
    /// areas were on screen.
    /// There is no honest way to replay such a list into a pane control that
    /// has no list to put it in, so — the ruling already recorded for the
    /// settings store itself — there is NO migration: an arrangement written by
    /// that shell carries no usable shares, and the window opens at the shares
    /// chosen here. The panels it recorded, and the window size, are read as
    /// they always were.
    /// </para>
    /// </remarks>
    public void ApplyLayout(DockLayout layout)
    {
        if (layout == null || layout.IsEmpty) { return; }

        foreach (var state in layout.Panels)
        {
            Panel panel = _panels.FirstOrDefault(
                p => string.Equals(p.Name, state.Name, StringComparison.Ordinal));
            if (panel == null) { continue; }

            panel.Area = state.Area;
            panel.IsVisible = true;
        }

        foreach (DockArea area in _areas.Keys.ToList())
        {
            string active = layout.ActiveIn(area);
            if (active == null) { continue; }

            Panel panel = _panels.FirstOrDefault(
                p => string.Equals(p.Name, active, StringComparison.Ordinal));
            if (panel != null) { BringToFront(panel); }
        }

        if (layout.Sizes == null || !layout.Sizes.IsUsable) { return; }

        _sizes.CopyFrom(layout.Sizes);
        ApplySizes();
    }

    /// <summary>Gives both controls the shares the shell is holding.</summary>
    /// <remarks>A pane that is shut is opened, given its share and shut again,
    /// which is how the share reaches the control's own snapshot: the strip
    /// then comes back at the width the user left it at rather than at this
    /// shell's default the first time they open it. None of that is visible —
    /// this runs while the window is still being built.</remarks>
    private void ApplySizes()
    {
        ApplySidePercents(_outer, _sizes.OuterSidePercent, _sizes.OuterStackPercent);
        ApplyStackPercents(_outer, _sizes.OuterUpperPercent, _sizes.OuterLowerPercent);
        ApplySidePercents(_inner, _sizes.InnerSidePercent, _sizes.InnerStackPercent);
    }

    private static void ApplySidePercents(TriPaneView panes, double side, double stack)
    {
        bool wasMinimized = panes.IsSidePaneMinimized;
        if (wasMinimized) { panes.RestoreSidePane(); }

        panes.SidePanePercent = side;
        panes.StackPercent = stack;
        if (wasMinimized) { panes.MinimizeSidePane(); }
    }

    private static void ApplyStackPercents(TriPaneView panes, double upper, double lower)
    {
        bool wasMinimized = panes.IsLowerPaneMinimized;
        if (wasMinimized) { panes.RestoreLowerPane(); }

        panes.UpperPanePercent = upper;
        panes.LowerPanePercent = lower;
        if (wasMinimized) { panes.MinimizeLowerPane(); }
    }

    /// <summary>Takes down the shares of every divider that is on screen.</summary>
    /// <remarks>An axis with a shut pane reads as a zero and a hundred, which
    /// says nothing about where the user put the divider, so only an axis with
    /// both of its panes open is recorded. Everything else keeps the share it
    /// last had.</remarks>
    private void RecordSizes()
    {
        if (!_outer.IsSidePaneMinimized && _outer.StackPercent > 0d)
        {
            _sizes.OuterSidePercent = _outer.SidePanePercent;
            _sizes.OuterStackPercent = _outer.StackPercent;
        }

        if (!_outer.IsUpperPaneMinimized && !_outer.IsLowerPaneMinimized)
        {
            _sizes.OuterUpperPercent = _outer.UpperPanePercent;
            _sizes.OuterLowerPercent = _outer.LowerPanePercent;
        }

        if (!_inner.IsSidePaneMinimized && _inner.StackPercent > 0d)
        {
            _sizes.InnerSidePercent = _inner.SidePanePercent;
            _sizes.InnerStackPercent = _inner.StackPercent;
        }
    }

    /// <summary>Announces a divider the user actually moved.</summary>
    /// <remarks>A gesture that changed nothing — a press and release on a
    /// divider already sitting against its floor — announces itself the same
    /// way a real drag does, so the shares are compared and an announcement
    /// that says nothing new is dropped rather than sending the window off to
    /// write a file.</remarks>
    private void OnDividerDragCompleted()
    {
        ShellLayout before = new ShellLayout();
        before.CopyFrom(_sizes);
        RecordSizes();
        if (_sizes.Matches(before)) { return; }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Which panes the panels that are open ask for.</summary>
    /// <returns>The five panes.</returns>
    private ShellPanes WantedPanes()
        => ShellLayout.PanesFor(
            AreaHasATab(DockArea.Left),
            AreaHasATab(DockArea.Right),
            AreaHasATab(DockArea.Bottom));

    private bool AreaHasATab(DockArea area)
        => _areas.TryGetValue(area, out var view) && view.TabItems.Count > 0;

    /// <summary>Opens and shuts the panes so the window looks like this.</summary>
    /// <param name="wanted">Which panes are to be open.</param>
    /// <remarks>Everything that is to be open is opened BEFORE anything is
    /// shut, on both controls: a control refuses a request that would leave it
    /// with nothing open at all, and doing it in the other order would walk
    /// into that refusal on the way through. Opening a pane that is already
    /// open, and shutting one that is already shut, are both nothing at all —
    /// in particular a second shut does not overwrite the snapshot the first
    /// one took.</remarks>
    private void ApplyPanes(ShellPanes wanted)
    {
        if (wanted == null) { return; }

        //A pane control narrower than the sum of its own floors gives its side
        //pane the floor and its stack whatever is left, which can be nothing at
        //all: measured on X11, dragging the outer side divider down to the
        //editor block's 200 left the left strip at its own 200 and the EDITOR
        //at zero width, with the inner side divider gone with it. So the outer
        //stack's floor is the room the inner control actually needs — its side
        //pane's floor, its divider, and the editor's — whenever the left strip
        //is open, and just the editor's when it is not.
        _outer.StackMinLength = wanted.LeftStripIsOpen
            ? _inner.SidePaneMinLength + _inner.DividerThickness + EditorMinLength
            : EditorMinLength;

        //The inner control is only touched while it is on screen. When the
        //editor block is collapsed the left strip and the editor keep whatever
        //arrangement they had, and it is there again the moment the block
        //reopens.
        if (wanted.EditorBlockIsOpen)
        {
            _outer.RestoreUpperPane();
            if (wanted.EditorIsOpen) { _inner.RestoreUpperPane(); }

            if (wanted.LeftStripIsOpen) { _inner.RestoreSidePane(); }

            if (!wanted.EditorIsOpen) { _inner.MinimizeUpperPane(); }

            if (!wanted.LeftStripIsOpen) { _inner.MinimizeSidePane(); }
        }

        if (wanted.RightStripIsOpen) { _outer.RestoreSidePane(); }

        if (wanted.BottomStripIsOpen) { _outer.RestoreLowerPane(); }

        if (!wanted.EditorBlockIsOpen) { _outer.MinimizeUpperPane(); }

        if (!wanted.RightStripIsOpen) { _outer.MinimizeSidePane(); }

        if (!wanted.BottomStripIsOpen) { _outer.MinimizeLowerPane(); }
    }

    private void Refresh(Panel panel)
    {
        TabView view = AreaView(panel.Area);
        TabViewItem existing = view.TabItems.OfType<TabViewItem>()
            .FirstOrDefault(t => ReferenceEquals(t.Tag, panel));

        if (panel.IsVisible && existing == null)
        {
            TabViewItem tab = new TabViewItem
            {
                //A fresh TextBlock rather than the bare string: a string header
                //is realised through a recycled ContentPresenter, and a tab that
                //is removed and re-added then lands its header's presenter on a
                //control inside the NEXT tab's content (seen as the Layout
                //Control panel's last checkbox taking the tab's own title).
                Header = new TextBlock { Text = panel.Title },
                Tag = panel,
                //The tab is the panel's only close affordance; closing it is
                //the same as unchecking the panel's action.
                IsClosable = true,
                Content = panel.Widget(),
            };
            view.TabItems.Add(tab);
            view.SelectedItem = tab;
        }
        else if (!panel.IsVisible && existing != null)
        {
            //THE CONTENT GOES FIRST. A panel's widget is built once and kept
            //(Panel.Widget), so the SAME element is handed to whichever tab is
            //holding it — and an element has one parent. Leaving it attached to
            //a tab that is being thrown away and then giving it to a new one
            //left the old presenter still claiming it, which showed up as the
            //panel's LAST control taking the tab header's text. Clearing it
            //here is what makes hiding and showing a panel repeatable.
            existing.Content = null;
            view.TabItems.Remove(existing);
        }

        //A maximize is the one arrangement the panels do not get a say in: it
        //is undone by the command that made it, or by closing the panel that
        //asked for it.
        if (_maximized == null) { ApplyPanes(WantedPanes()); }
    }

    private TabView AreaView(DockArea area)
    {
        if (_areas.TryGetValue(area, out var existing)) { return existing; }

        TabView view = new TabView
        {
            IsAddTabButtonVisible = false,
            CanDragTabs = false,
            CanReorderTabs = false,
            TabWidthMode = TabViewWidthMode.SizeToContent,
        };
        view.TabCloseRequested += (_, e) =>
        {
            if (e.Tab?.Tag is Panel panel)
            {
                panel.IsVisible = false;
            }
        };

        _areas[area] = view;

        //Each strip goes into its pane once and stays there. From here on it is
        //the PANE that opens and shuts.
        switch (area)
        {
            case DockArea.Left:
                _inner.SidePane = view;
                break;
            case DockArea.Right:
                _outer.SidePane = view;
                break;
            default:
                _outer.LowerPane = view;
                break;
        }

        return view;
    }
}
