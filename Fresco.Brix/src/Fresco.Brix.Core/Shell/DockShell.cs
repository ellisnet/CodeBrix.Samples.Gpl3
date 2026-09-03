// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

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
public sealed class DockShell : SplitContainer
{
    private readonly SplitContainer _middleRow = new SplitContainer
    {
        Orientation = Orientation.Horizontal,
    };

    private readonly Dictionary<DockArea, TabView> _areas
        = new Dictionary<DockArea, TabView>();
    private readonly List<Panel> _panels = new List<Panel>();
    private readonly List<Panel> _hiddenByMaximize = new List<Panel>();

    //Which tab was UP in each OTHER area when a maximize started. Upstream
    //never disturbs the other panels at all — it floats the one being
    //maximized — so putting them back has to put their own tab back with them.
    private readonly DockLayout _showingBeforeMaximize = new DockLayout();
    private UIElement _center;
    private Panel _maximized;

    /// <summary>Creates the shell.</summary>
    public DockShell()
    {
        Orientation = Orientation.Vertical;
        AddPane(_middleRow);
    }

    /// <summary>Gets or sets what sits in the middle — the editor area.</summary>
    public UIElement Center
    {
        get => _center;
        set
        {
            if (_center != null)
            {
                _middleRow.RemovePane(_center);
            }

            _center = value;
            if (_center != null)
            {
                //The centre always goes between the left and right areas.
                _middleRow.InsertPane(LeftAreaIsShown ? 1 : 0, _center);
                RebalanceMiddle();
            }
        }
    }

    /// <summary>Gets the panels the shell knows about.</summary>
    public IReadOnlyList<Panel> Panels => _panels;

    private bool LeftAreaIsShown
        => _areas.TryGetValue(DockArea.Left, out var view)
            && _middleRow.IndexOf(view) >= 0;

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

    /// <summary>Gets the panel filling the window, or null.</summary>
    public Panel MaximizedPanel => _maximized;

    /// <summary>
    /// Gives one panel the whole window, hiding the editor area and every
    /// other panel until <see cref="RestoreFromMaximized"/> puts them back.
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
    /// Because the other panels ARE disturbed here where upstream's are not,
    /// the tab that was showing in each other area is remembered before they
    /// are hidden and raised again by <see cref="RestoreFromMaximized"/>:
    /// showing a panel makes it its area's current tab, so without that the
    /// LAST one re-shown in an area would be left showing instead of the user's
    /// own. The maximized panel keeps its own area, being what the user was
    /// looking at.
    /// </para>
    /// </remarks>
    public void MaximizePanel(Panel panel)
    {
        if (panel == null) { return; }

        if (_maximized != null) { RestoreFromMaximized(); }

        RememberShowingTabs(panel);

        panel.IsVisible = true;
        BringToFront(panel);

        _hiddenByMaximize.Clear();
        foreach (var other in _panels)
        {
            if (ReferenceEquals(other, panel) || !other.IsVisible) { continue; }

            _hiddenByMaximize.Add(other);
            other.IsVisible = false;
        }

        if (_center != null && _middleRow.IndexOf(_center) >= 0)
        {
            _middleRow.RemovePane(_center);
        }

        _maximized = panel;
        RebalanceMiddle();
    }

    /// <summary>Puts the layout back the way it was before a maximize.</summary>
    /// <remarks>Every area gets the tab it was showing back — the other areas
    /// the one they had (<see cref="RememberShowingTabs"/>), and the maximized
    /// panel's own area the maximized panel, which is what the user has been
    /// looking at and what upstream's re-docked floating panel is.</remarks>
    public void RestoreFromMaximized()
    {
        if (_maximized == null) { return; }

        Panel wasMaximized = _maximized;
        _maximized = null;
        if (_center != null && _middleRow.IndexOf(_center) < 0)
        {
            _middleRow.InsertPane(LeftAreaIsShown ? 1 : 0, _center);
        }

        foreach (var other in _hiddenByMaximize) { other.IsVisible = true; }

        _hiddenByMaximize.Clear();
        RaiseRememberedTabs();
        BringToFront(wasMaximized);
        RebalanceMiddle();
    }

    /// <summary>Records the tab showing in every area but one panel's own.</summary>
    /// <param name="maximizing">The panel about to fill the window.</param>
    private void RememberShowingTabs(Panel maximizing)
    {
        _showingBeforeMaximize.Panels.Clear();
        foreach (var pair in _areas)
        {
            if (maximizing != null && pair.Key == maximizing.Area) { continue; }

            if (pair.Value.SelectedItem is not TabViewItem selected
                || selected.Tag is not Panel showing)
            {
                continue;
            }

            _showingBeforeMaximize.Panels.Add(new DockPanelState
            {
                Name = showing.Name,
                Area = pair.Key,
                IsActive = true,
            });
        }
    }

    /// <summary>Puts each remembered tab back up, then forgets them.</summary>
    private void RaiseRememberedTabs()
    {
        foreach (DockArea area in _areas.Keys.ToList())
        {
            string showing = _showingBeforeMaximize.ActiveIn(area);
            if (showing == null) { continue; }

            Panel panel = _panels.FirstOrDefault(
                p => string.Equals(p.Name, showing, StringComparison.Ordinal));
            if (panel != null && panel.IsVisible) { BringToFront(panel); }
        }

        _showingBeforeMaximize.Panels.Clear();
    }

    /// <summary>
    /// Reads the arrangement out of the shell, so it can be put back after a
    /// relaunch.
    /// </summary>
    /// <returns>What is open, where, in what tab order, and how big.</returns>
    /// <remarks>Upstream's <c>QMainWindow.saveState()</c>, called from
    /// <c>mainwindow.py</c>'s <c>writeSettings</c> — see
    /// <see cref="DockLayout"/> for the declared difference of mechanism.
    /// A maximized panel is NOT what gets recorded: the arrangement it
    /// interrupted is, so quitting from Music &gt; Maximize brings the
    /// user's own layout back rather than the full-window one.</remarks>
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

        //A maximized panel has emptied every other area, so the weights on
        //screen are not the ones worth keeping.
        foreach (var panel in _hiddenByMaximize)
        {
            if (layout.Panels.Any(
                p => string.Equals(p.Name, panel.Name, StringComparison.Ordinal)))
            {
                continue;
            }

            layout.Panels.Add(new DockPanelState
            {
                Name = panel.Name,
                Area = panel.Area,
                //was previously: always false, which lost the tab the user had
                //up in that area whenever the quit came from Music > Maximize.
                IsActive = string.Equals(
                    _showingBeforeMaximize.ActiveIn(panel.Area),
                    panel.Name,
                    StringComparison.Ordinal),
            });
        }

        layout.MiddleSizes = _middleRow.Sizes().ToList();
        layout.OuterSizes = Sizes().ToList();
        return layout;
    }

    /// <summary>Puts a remembered arrangement back.</summary>
    /// <param name="layout">The arrangement, or null.</param>
    /// <remarks>
    /// Upstream's <c>restoreState()</c>, called from <c>readSettings</c> as the
    /// window is built. Called ONCE, while the window is being built and
    /// before the user can have moved anything: it opens the panels in the
    /// stored tab order, raises the one that was showing in each area, and only
    /// THEN sets the divider weights, because opening an area rebalances them
    /// (<see cref="RebalanceMiddle"/>). A stored panel this build no longer
    /// offers is skipped rather than refusing the whole arrangement.
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

        _middleRow.SetSizes(layout.MiddleSizes);
        SetSizes(layout.OuterSizes);
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
            //here is what makes hiding and showing a panel repeatable, and the
            //Music View's Maximize does exactly that to every other panel.
            existing.Content = null;
            view.TabItems.Remove(existing);
        }

        ShowOrHideArea(panel.Area, view);
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
        return view;
    }

    private void ShowOrHideArea(DockArea area, TabView view)
    {
        bool wanted = view.TabItems.Count > 0;
        SplitContainer host = area == DockArea.Bottom ? this : _middleRow;
        bool shown = host.IndexOf(view) >= 0;

        if (wanted == shown) { return; }

        if (!wanted)
        {
            host.RemovePane(view);
            RebalanceMiddle();
            return;
        }

        switch (area)
        {
            case DockArea.Left:
                host.InsertPane(0, view);
                break;
            case DockArea.Right:
                host.AddPane(view);
                break;
            default:
                //The bottom area is the second pane of the outer, vertical
                //container, under the whole middle row.
                host.AddPane(view);
                break;
        }

        RebalanceMiddle();
    }

    /// <summary>
    /// Gives the editor the lion's share: a docked tool area takes about a
    /// quarter of the width, and the bottom area about a fifth of the height.
    /// </summary>
    private void RebalanceMiddle()
    {
        List<double> middle = new List<double>();
        foreach (var pane in _middleRow.Panes)
        {
            middle.Add(ReferenceEquals(pane, _center) ? 3.0 : 1.0);
        }

        _middleRow.SetSizes(middle);

        List<double> outer = new List<double>();
        foreach (var pane in Panes)
        {
            outer.Add(ReferenceEquals(pane, _middleRow) ? 4.0 : 1.0);
        }

        SetSizes(outer);
    }
}
