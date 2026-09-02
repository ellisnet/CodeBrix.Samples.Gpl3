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

namespace Fresco.Brix.Snippets;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A group of snippets the user ticks, with a heading that ticks or unticks
/// the whole group.
/// </summary>
/// <remarks>
/// Upstream's two snippet choosers — <c>restore.py</c> and
/// <c>import_export.py</c> — are both a <c>QTreeWidget</c> two levels deep in
/// which every item is checkable and a parent's check state is pushed down to
/// its children. A <c>TreeView</c> node's content renders as its type name
/// unless it is given an item template (board trap 40's neighbour), and a
/// checkbox inside a template is another thing to prove on six heads; this is
/// the same two levels drawn as indented check boxes, which paint everywhere.
/// </remarks>
public sealed class SnippetCheckGroup
{
    private readonly List<(string Name, CheckBox Box)> _rows
        = new List<(string, CheckBox)>();

    private bool _pushing;

    /// <summary>Creates a group.</summary>
    /// <param name="title">The heading, without its count.</param>
    /// <param name="checkable">Whether its rows can be ticked at all —
    /// upstream's "Unchanged Snippets" group is shown and cannot be.</param>
    public SnippetCheckGroup(string title, bool checkable = true)
    {
        Title = title;
        Checkable = checkable;
        Heading = new CheckBox { Content = title, IsEnabled = false };
        Panel = new StackPanel { Spacing = 2 };
        Panel.Children.Add(Heading);

        Heading.Checked += (_, _) => Push(true);
        Heading.Unchecked += (_, _) => Push(false);
    }

    /// <summary>Raised when any row's tick changed.</summary>
    public event EventHandler Changed;

    /// <summary>Gets the heading, without its count.</summary>
    public string Title { get; }

    /// <summary>Gets whether the group's rows can be ticked.</summary>
    public bool Checkable { get; }

    /// <summary>Gets the heading's own box.</summary>
    public CheckBox Heading { get; }

    /// <summary>Gets the element to put in a dialog.</summary>
    public StackPanel Panel { get; }

    /// <summary>Gets how many rows the group holds.</summary>
    public int Count => _rows.Count;

    /// <summary>Adds a row.</summary>
    /// <param name="name">What the row stands for.</param>
    /// <param name="title">What it says.</param>
    /// <param name="isChecked">Whether it starts ticked.</param>
    public void Add(string name, string title, bool isChecked = false)
    {
        CheckBox box = new CheckBox
        {
            Content = title,
            IsChecked = isChecked,
            IsEnabled = Checkable,
            Margin = new Thickness(24, 0, 0, 0),
        };
        box.Checked += (_, _) => Announce();
        box.Unchecked += (_, _) => Announce();
        _rows.Add((name, box));
        Panel.Children.Add(box);
        Heading.IsEnabled = Checkable;
    }

    /// <summary>Puts the count in the heading, as upstream does.</summary>
    public void ShowCount()
        => Heading.Content = Title + " (" + _rows.Count.ToString(
            System.Globalization.CultureInfo.InvariantCulture) + ")";

    /// <summary>Ticks every row.</summary>
    public void CheckAll()
    {
        Heading.IsChecked = true;
        Push(true);
    }

    /// <summary>Gets the names of the ticked rows.</summary>
    /// <returns>The names.</returns>
    public IReadOnlyList<string> Checked()
    {
        List<string> names = new List<string>();
        foreach (var (name, box) in _rows)
        {
            if (box.IsChecked == true) { names.Add(name); }
        }

        return names;
    }

    private void Push(bool value)
    {
        if (_pushing) { return; }

        _pushing = true;
        try
        {
            foreach (var (_, box) in _rows) { box.IsChecked = value; }
        }
        finally
        {
            _pushing = false;
        }

        Announce();
    }

    private void Announce() => Changed?.Invoke(this, EventArgs.Empty);
}
