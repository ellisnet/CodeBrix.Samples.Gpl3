// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/panelmanager.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The tool panels the window offers, in the groups the Tools menu lists them
/// under.
/// </summary>
public sealed class PanelManager
{
    /// <summary>The Tools menu's groups, in upstream's order.</summary>
    public static readonly IReadOnlyList<string> GroupNames = new[]
    {
        "viewers", "coding", "structure", "midi",
    };

    private readonly List<Panel> _panels = new List<Panel>();
    private readonly Dictionary<string, List<Panel>> _groups
        = new Dictionary<string, List<Panel>>(StringComparer.Ordinal);
    private readonly DockShell _shell;

    /// <summary>Creates the manager over a shell.</summary>
    /// <param name="shell">The window's dock shell.</param>
    /// <param name="settings">The store panel shortcuts live in.</param>
    public PanelManager(DockShell shell, SettingsStore settings = null)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Actions = new PanelActions(this, settings);
        foreach (var name in GroupNames)
        {
            _groups[name] = new List<Panel>();
        }
    }

    /// <summary>Gets the panels, in registration order.</summary>
    public IReadOnlyList<Panel> Panels => _panels;

    /// <summary>Gets the collection holding the panels' toggle actions.</summary>
    public PanelActions Actions { get; }

    /// <summary>Registers a panel and puts it on the shell.</summary>
    /// <param name="panel">The panel.</param>
    /// <param name="group">Which Tools submenu it belongs under, or null for
    /// the menu's top level.</param>
    /// <returns>The panel, for chaining.</returns>
    public Panel AddPanel(Panel panel, string group = null)
    {
        if (panel == null) { throw new ArgumentNullException(nameof(panel)); }

        _panels.Add(panel);
        if (group != null && _groups.TryGetValue(group, out var list))
        {
            list.Add(panel);
        }

        panel.TranslateUI();
        _shell.AddPanel(panel);
        Actions.Register(panel);
        return panel;
    }

    /// <summary>Gets the panels in a Tools submenu.</summary>
    /// <param name="group">The group name.</param>
    /// <returns>The panels.</returns>
    public IReadOnlyList<Panel> PanelsInGroup(string group)
        => _groups.TryGetValue(group ?? string.Empty, out var list)
            ? list
            : Array.Empty<Panel>();

    /// <summary>Gets the panels that belong to no submenu.</summary>
    /// <returns>The panels.</returns>
    public IReadOnlyList<Panel> UngroupedPanels()
        => _panels.Where(p => !_groups.Values.Any(g => g.Contains(p))).ToList();

    /// <summary>Finds a panel by name, or null.</summary>
    /// <param name="name">The panel name.</param>
    /// <returns>The panel, or null.</returns>
    public Panel PanelByName(string name)
        => _panels.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>Gets the visible panels docked against an edge.</summary>
    /// <param name="area">The edge.</param>
    /// <returns>The panels.</returns>
    public IReadOnlyList<Panel> PanelsAt(DockArea area)
        => _panels.Where(p => p.Area == area && p.IsVisible).ToList();

    /// <summary>Re-translates every panel.</summary>
    public void TranslateUI()
    {
        foreach (var panel in _panels)
        {
            panel.TranslateUI();
        }

        Actions.TranslateUI();
    }
}

/// <summary>
/// The keyboard shortcuts that show and hide the tool panels — a collection of
/// its own so a user can rebind them like any other command.
/// </summary>
public sealed class PanelActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "panels";

    private readonly PanelManager _manager;

    /// <summary>Creates the collection.</summary>
    /// <param name="manager">The panel manager.</param>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public PanelActions(PanelManager manager, SettingsStore settings = null)
        : base(CollectionName, settings)
    {
        _manager = manager;
        Initialize();
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Tool Panels");

    /// <summary>Adds a panel's toggle action to the collection.</summary>
    /// <param name="panel">The panel.</param>
    /// <remarks>Panels register as they are created, which is after the
    /// collection exists — so unlike upstream's fixed collections this one
    /// grows, and reloads the stored shortcuts each time it does.</remarks>
    internal void Register(Panel panel)
    {
        Add(panel.ToggleAction);
        Load(false);
    }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        //The actions belong to the panels; they arrive through Register.
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        foreach (var panel in _manager?.Panels ?? Array.Empty<Panel>())
        {
            panel.ToggleAction.Text = panel.Title;
        }
    }
}
