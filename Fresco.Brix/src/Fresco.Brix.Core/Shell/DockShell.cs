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
    /// </remarks>
    public void MaximizePanel(Panel panel)
    {
        if (panel == null) { return; }

        if (_maximized != null) { RestoreFromMaximized(); }

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
    public void RestoreFromMaximized()
    {
        if (_maximized == null) { return; }

        _maximized = null;
        if (_center != null && _middleRow.IndexOf(_center) < 0)
        {
            _middleRow.InsertPane(LeftAreaIsShown ? 1 : 0, _center);
        }

        foreach (var other in _hiddenByMaximize) { other.IsVisible = true; }

        _hiddenByMaximize.Clear();
        RebalanceMiddle();
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
