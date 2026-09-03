// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/mainwindow.py readSettings/writeSettings

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One tool panel's place in a remembered window arrangement.</summary>
public sealed class DockPanelState
{
    /// <summary>Gets or sets the panel's stable name (e.g. <c>musicview</c>).</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets which edge it was docked against.</summary>
    public DockArea Area { get; set; }

    /// <summary>Gets or sets whether it was the tab showing in that area.</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// The window arrangement that outlives a quit: which tool panels were open,
/// in which area, in what tab order, which one was showing in each area, and
/// how the dividers between the areas were set.
/// </summary>
/// <remarks>
/// <para>
/// Frescobaldi writes this on the way out and reads it on the way in —
/// <c>mainwindow.py:403-411</c> <c>writeSettings()</c> stores
/// <c>mainwindow/size</c>, <c>mainwindow/state</c>, <c>mainwindow/tabbar</c>
/// and <c>mainwindow/maximized</c>, and <c>mainwindow.py:391-401</c>
/// <c>readSettings()</c> puts them back with <c>resize()</c> and
/// <c>restoreState()</c>; <c>closeEvent</c> (line 344) is what calls the
/// writer, for the last window only.
/// </para>
/// <para>
/// ⚠ MECHANISM DIVERGENCE, declared here. Upstream's <c>mainwindow/state</c> is
/// the opaque byte array <c>QMainWindow.saveState()</c> produces — a Qt private
/// format describing dock widgets, their areas, their tabification and the
/// toolbars. There is no such call to port: this shell is the repository's own
/// (<see cref="DockShell"/> over <see cref="SplitContainer"/>), so the KEY is
/// upstream's and what it holds says the same things in this shell's own terms.
/// The sizes are the dividers' relative weights rather than pixels, which is
/// what <see cref="SplitContainer"/> works in and what makes the arrangement
/// come back the same on a screen of another size.
/// </para>
/// </remarks>
public sealed class DockLayout
{
    /// <summary>The key the arrangement is stored under.</summary>
    public const string StateKey = "mainwindow/state";

    /// <summary>The key the window size is stored under.</summary>
    public const string SizeKey = "mainwindow/size";

    /// <summary>Gets or sets the open panels, in the tab order they had.</summary>
    public List<DockPanelState> Panels { get; set; } = new List<DockPanelState>();

    /// <summary>
    /// Gets or sets the middle row's divider weights — left area, editor,
    /// right area, for whichever of those were on screen.
    /// </summary>
    public List<double> MiddleSizes { get; set; } = new List<double>();

    /// <summary>
    /// Gets or sets the outer column's divider weights — the middle row and
    /// the bottom area, for whichever were on screen.
    /// </summary>
    public List<double> OuterSizes { get; set; } = new List<double>();

    /// <summary>Gets whether nothing was recorded.</summary>
    /// <remarks>A first launch has nothing stored, and an arrangement with no
    /// panel open records none: both leave the window at its defaults rather
    /// than being applied. Not stored — it is a reading of what is.</remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Panels.Count == 0;

    /// <summary>Gets the panels recorded against one edge, in tab order.</summary>
    /// <param name="area">The edge.</param>
    /// <returns>The panels.</returns>
    public IReadOnlyList<DockPanelState> PanelsIn(DockArea area)
        => Panels.Where(p => p != null && p.Area == area).ToList();

    /// <summary>Gets the name of the panel that was showing in an area.</summary>
    /// <param name="area">The edge.</param>
    /// <returns>The panel name, or <see langword="null"/> when the area
    /// recorded none.</returns>
    /// <remarks>Only one tab can be up in an area, so the FIRST one marked
    /// wins — a stored arrangement that somehow marks two is read the way the
    /// tab strip would have drawn it.</remarks>
    public string ActiveIn(DockArea area)
        => PanelsIn(area).FirstOrDefault(p => p.IsActive)?.Name;

    /// <summary>Writes the arrangement to the settings store.</summary>
    /// <param name="settings">The store, or null to do nothing.</param>
    public void Save(SettingsStore settings) => settings?.Set(StateKey, this);

    /// <summary>Reads the arrangement back.</summary>
    /// <param name="settings">The store, or null.</param>
    /// <returns>The arrangement, empty when nothing is stored.</returns>
    public static DockLayout Load(SettingsStore settings)
        => settings?.Get<DockLayout>(StateKey) ?? new DockLayout();

    /// <summary>Writes the window's size.</summary>
    /// <param name="settings">The store, or null to do nothing.</param>
    /// <param name="width">The width in device-independent pixels.</param>
    /// <param name="height">The height.</param>
    /// <remarks>Nothing is written for a size that is not a real one, so a
    /// window the head has not laid out yet cannot overwrite a good value.
    /// Upstream stores a <c>QSize</c>; this stores the same two numbers.</remarks>
    public static void SaveWindowSize(SettingsStore settings, int width, int height)
    {
        if (settings == null || width <= 0 || height <= 0) { return; }

        settings.SetString(
            SizeKey,
            width.ToString(CultureInfo.InvariantCulture)
                + " "
                + height.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Reads the window's size back.</summary>
    /// <param name="settings">The store, or null.</param>
    /// <returns>The size, or <c>(0, 0)</c> when nothing usable is stored.</returns>
    public static (int Width, int Height) LoadWindowSize(SettingsStore settings)
    {
        string stored = settings?.GetString(SizeKey);
        if (string.IsNullOrWhiteSpace(stored)) { return (0, 0); }

        string[] parts = stored.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(
                parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(
                parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            || width <= 0
            || height <= 0)
        {
            return (0, 0);
        }

        return (width, height);
    }
}
