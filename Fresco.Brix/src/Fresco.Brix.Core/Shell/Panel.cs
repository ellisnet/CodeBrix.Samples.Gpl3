// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Microsoft.UI.Xaml;
using System;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/panel.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Which edge of the window a tool panel docks against.</summary>
public enum DockArea
{
    /// <summary>The left edge.</summary>
    Left,

    /// <summary>The right edge.</summary>
    Right,

    /// <summary>The bottom edge.</summary>
    Bottom,
}

/// <summary>
/// A tool panel — the Music View, the log, Quick Insert, the document list and
/// the rest — docked against an edge of the window and shown or hidden by its
/// own action on the Tools menu.
/// <para>
/// A panel's contents are not built until it is first shown. Frescobaldi does
/// this so that starting up does not pay for tools the user never opens, and
/// the same reasoning is stronger here: the Music View pulls in the SVG
/// renderer and the log pulls in the engrave service.
/// </para>
/// </summary>
public abstract class Panel
{
    private UIElement _widget;

    /// <summary>Creates a panel.</summary>
    /// <param name="name">The stable name it is stored and looked up under.</param>
    /// <param name="area">Which edge it docks against.</param>
    protected Panel(string name, DockArea area)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Area = area;
        ToggleAction = new AppAction("panel_" + name).AsToggle();
        ToggleAction.Triggered += (_, _) => IsVisible = ToggleAction.IsChecked;
    }

    /// <summary>Raised when the panel is shown or hidden.</summary>
    public event EventHandler VisibilityChanged;

    /// <summary>Gets the stable name (e.g. <c>musicview</c>).</summary>
    public string Name { get; }

    /// <summary>Gets or sets which edge the panel docks against.</summary>
    public DockArea Area { get; set; }

    /// <summary>Gets the action that shows and hides the panel.</summary>
    public AppAction ToggleAction { get; }

    /// <summary>Gets the panel's title, retranslated with the language.</summary>
    public abstract string Title { get; }

    /// <summary>Gets whether the contents have been built yet.</summary>
    public bool IsInstantiated => _widget != null;

    /// <summary>Gets or sets whether the panel is on screen.</summary>
    public bool IsVisible
    {
        get;
        set
        {
            if (field == value) { return; }

            field = value;
            ToggleAction.IsChecked = value;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the panel's contents, building them on the first request.
    /// </summary>
    /// <returns>The contents.</returns>
    public UIElement Widget() => _widget ??= CreateWidget();

    /// <summary>Shows the panel and brings it to the front of its area.</summary>
    public void Activate()
    {
        IsVisible = true;
        Activated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the panel asks to be brought to the front.</summary>
    public event EventHandler Activated;

    /// <summary>Sets the panel's title and toggle text for the language.</summary>
    public virtual void TranslateUI() => ToggleAction.Text = Title;

    /// <summary>Builds the panel's contents. Called once, lazily.</summary>
    /// <returns>The contents.</returns>
    protected abstract UIElement CreateWidget();
}
