// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: PyQt6 QSplitter, as Frescobaldi's shell uses it

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Lays a row or column of panes out with draggable dividers between them, and
/// can be nested inside another to build any arrangement of panes.
/// <para>
/// Frescobaldi's window is built out of Qt splitters — the editor area splits
/// into views, and the tool panels dock around it. CodeBrix.Platform has no
/// splitter control, so this is the port's own: only what the shell needs
/// (add, insert, remove, index, proportional sizes), not a general-purpose
/// docking library.
/// </para>
/// </summary>
public class SplitContainer : Grid
{
    /// <summary>How wide a divider is.</summary>
    public const double DividerThickness = 6.0;

    private readonly List<UIElement> _panes = new List<UIElement>();
    private readonly List<double> _weights = new List<double>();
    private Orientation _orientation = Orientation.Horizontal;

    /// <summary>Creates an empty container.</summary>
    public SplitContainer()
    {
    }

    /// <summary>
    /// Gets or sets whether the panes sit side by side
    /// (<see cref="Orientation.Horizontal"/>) or stacked
    /// (<see cref="Orientation.Vertical"/>).
    /// </summary>
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value) { return; }

            _orientation = value;
            Rebuild();
        }
    }

    /// <summary>Gets or sets the divider colour.</summary>
    public Brush DividerBrush { get; set; }
        = new SolidColorBrush(Color.FromArgb(255, 0xC8, 0xC8, 0xC8));

    /// <summary>Gets the panes, in order.</summary>
    public IReadOnlyList<UIElement> Panes => _panes;

    /// <summary>Gets how many panes there are.</summary>
    public int Count => _panes.Count;

    /// <summary>Gets a pane.</summary>
    /// <param name="index">The position.</param>
    /// <returns>The pane.</returns>
    public UIElement Pane(int index) => _panes[index];

    /// <summary>Finds a pane's position, or -1.</summary>
    /// <param name="pane">The pane.</param>
    /// <returns>The position, or -1.</returns>
    public int IndexOf(UIElement pane) => _panes.IndexOf(pane);

    /// <summary>Adds a pane at the end.</summary>
    /// <param name="pane">The pane.</param>
    public void AddPane(UIElement pane) => InsertPane(_panes.Count, pane);

    /// <summary>Adds a pane at a position.</summary>
    /// <param name="index">The position.</param>
    /// <param name="pane">The pane.</param>
    public void InsertPane(int index, UIElement pane)
    {
        if (pane == null) { throw new ArgumentNullException(nameof(pane)); }

        index = Math.Clamp(index, 0, _panes.Count);
        _panes.Insert(index, pane);

        //A new pane takes the average of what is already there, so adding one
        //to a container of equal panes keeps them equal.
        _weights.Insert(index, _weights.Count == 0 ? 1.0 : _weights.Average());
        Rebuild();
    }

    /// <summary>Removes a pane.</summary>
    /// <param name="pane">The pane.</param>
    /// <returns>Whether it was there.</returns>
    public bool RemovePane(UIElement pane)
    {
        int index = _panes.IndexOf(pane);
        if (index < 0) { return false; }

        _panes.RemoveAt(index);
        _weights.RemoveAt(index);
        Rebuild();
        return true;
    }

    /// <summary>Gets the panes' relative sizes.</summary>
    /// <returns>The weights, one per pane.</returns>
    public IReadOnlyList<double> Sizes() => _weights.ToList();

    /// <summary>Sets the panes' relative sizes.</summary>
    /// <param name="sizes">The weights; extra or missing entries are ignored.</param>
    public void SetSizes(IReadOnlyList<double> sizes)
    {
        if (sizes == null) { return; }

        for (int i = 0; i < _weights.Count && i < sizes.Count; i++)
        {
            _weights[i] = Math.Max(sizes[i], 0.0001);
        }

        Rebuild();
    }

    /// <summary>
    /// Rebuilds the layout: one definition per pane, sized by its weight, with
    /// a fixed-width divider between neighbours.
    /// </summary>
    protected void Rebuild()
    {
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        if (_panes.Count == 0) { return; }

        bool horizontal = _orientation == Orientation.Horizontal;
        for (int i = 0; i < _panes.Count; i++)
        {
            if (i > 0)
            {
                Define(horizontal, new GridLength(DividerThickness));
            }

            Define(horizontal, new GridLength(_weights[i], GridUnitType.Star));
        }

        for (int i = 0; i < _panes.Count; i++)
        {
            int slot = i * 2;
            if (i > 0)
            {
                Children.Add(CreateDivider(horizontal, slot - 1, i - 1));
            }

            UIElement pane = _panes[i];
            if (horizontal) { SetColumn((FrameworkElement)pane, slot); }
            else { SetRow((FrameworkElement)pane, slot); }

            Children.Add(pane);
        }
    }

    private void Define(bool horizontal, GridLength length)
    {
        if (horizontal)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        }
        else
        {
            RowDefinitions.Add(new RowDefinition { Height = length });
        }
    }

    private UIElement CreateDivider(bool horizontal, int slot, int leftPane)
    {
        //A bare Thumb paints nothing under the theme templates on the Skia
        //heads (the same sharp edge the standalone ScrollBar has), so the
        //divider is a plain Grid with its own background and pointer handling.
        Grid host = new Grid
        {
            Background = DividerBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        bool dragging = false;
        double lastPosition = 0;

        host.PointerPressed += (sender, e) =>
        {
            dragging = true;
            lastPosition = Position(e, horizontal);
            ((Grid)sender).CapturePointer(e.Pointer);
            e.Handled = true;
        };
        host.PointerMoved += (sender, e) =>
        {
            if (!dragging) { return; }

            double position = Position(e, horizontal);
            Resize(horizontal, leftPane, position - lastPosition);
            lastPosition = position;
            e.Handled = true;
        };
        host.PointerReleased += (sender, e) =>
        {
            dragging = false;
            ((Grid)sender).ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        };
        host.PointerCaptureLost += (_, _) => dragging = false;

        if (horizontal) { SetColumn(host, slot); }
        else { SetRow(host, slot); }

        return host;
    }

    private double Position(PointerRoutedEventArgs e, bool horizontal)
    {
        var point = e.GetCurrentPoint(this).Position;
        return horizontal ? point.X : point.Y;
    }

    private void Resize(bool horizontal, int leftPane, double delta)
    {
        if (Math.Abs(delta) < 0.5) { return; }

        int rightPane = leftPane + 1;
        if (rightPane >= _panes.Count) { return; }

        double total = horizontal ? ActualWidth : ActualHeight;
        if (total <= 0) { return; }

        double weightTotal = _weights.Sum();
        double perPixel = weightTotal / Math.Max(total, 1.0);
        double change = delta * perPixel;

        //Neither neighbour may vanish: the divider stops rather than
        //collapsing a pane the user can no longer grab.
        double minimum = weightTotal * 0.02;
        double left = _weights[leftPane] + change;
        double right = _weights[rightPane] - change;
        if (left < minimum || right < minimum) { return; }

        _weights[leftPane] = left;
        _weights[rightPane] = right;

        bool useColumns = horizontal;
        int leftSlot = leftPane * 2;
        int rightSlot = rightPane * 2;
        if (useColumns)
        {
            ColumnDefinitions[leftSlot].Width = new GridLength(left, GridUnitType.Star);
            ColumnDefinitions[rightSlot].Width = new GridLength(right, GridUnitType.Star);
        }
        else
        {
            RowDefinitions[leftSlot].Height = new GridLength(left, GridUnitType.Star);
            RowDefinitions[rightSlot].Height = new GridLength(right, GridUnitType.Star);
        }
    }
}
