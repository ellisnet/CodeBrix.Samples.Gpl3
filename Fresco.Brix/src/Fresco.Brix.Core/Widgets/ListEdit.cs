// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Widgets; //was previously: frescobaldi/widgets/listedit.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The list of strings behind a <see cref="ListEdit"/>: what Add, Edit, Remove
/// and the two move buttons actually do.
/// </summary>
/// <remarks>
/// Upstream keeps this state in the <c>QListWidget</c> itself. Splitting it out
/// is what lets the behaviour be tested without a window, and it is the same
/// separation the preference pages use between their values and their controls.
/// </remarks>
public sealed class ListEditModel
{
    private readonly List<string> _items = new List<string>();

    /// <summary>Gets the items, in order.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Gets or sets which item is current; -1 for none.</summary>
    public int CurrentIndex
    {
        get;
        set => field = value < 0 || value >= _items.Count ? -1 : value;
    } = -1;

    /// <summary>Gets the current item, or null.</summary>
    public string Current
        => CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;

    /// <summary>Gets whether Edit and Remove apply.</summary>
    public bool HasSelection => CurrentIndex >= 0;

    /// <summary>Replaces every item.</summary>
    /// <param name="items">The new items; null empties the list.</param>
    public void SetItems(IEnumerable<string> items)
    {
        _items.Clear();
        if (items != null) { _items.AddRange(items.Where(i => i != null)); }

        CurrentIndex = _items.Count > 0 ? 0 : -1;
    }

    /// <summary>Adds an item at the end and makes it current.</summary>
    /// <param name="item">The item.</param>
    public void Add(string item)
    {
        if (item == null) { return; }

        _items.Add(item);
        CurrentIndex = _items.Count - 1;
    }

    /// <summary>Replaces the current item.</summary>
    /// <param name="item">The new text.</param>
    public void ReplaceCurrent(string item)
    {
        if (item == null || CurrentIndex < 0) { return; }

        _items[CurrentIndex] = item;
    }

    /// <summary>Removes the current item.</summary>
    public void RemoveCurrent()
    {
        if (CurrentIndex < 0) { return; }

        int index = CurrentIndex;
        _items.RemoveAt(index);
        CurrentIndex = _items.Count == 0 ? -1 : Math.Min(index, _items.Count - 1);
    }

    /// <summary>Moves the current item, keeping it current.</summary>
    /// <param name="offset">-1 for up, 1 for down.</param>
    /// <returns>Whether anything moved.</returns>
    /// <remarks>Upstream gets this from the list box's own internal-move drag
    /// mode, which the outline patterns turn on; buttons say the same thing
    /// without a drag.</remarks>
    public bool Move(int offset)
    {
        int from = CurrentIndex;
        int to = from + offset;
        if (from < 0 || to < 0 || to >= _items.Count) { return false; }

        (_items[from], _items[to]) = (_items[to], _items[from]);
        CurrentIndex = to;
        return true;
    }
}

/// <summary>
/// A list the user edits: a row of items with Add, Edit and Remove beside it.
/// What "edit an item" means is the caller's — a folder picker, a text dialog,
/// whatever the list holds.
/// </summary>
/// <remarks>
/// Upstream subclasses <c>ListEdit</c> and overrides <c>openEditor</c>; here
/// the same choice is a delegate, because a subclass of a platform control
/// carries a template with it and a delegate does not.
/// </remarks>
public sealed class ListEdit : Grid
{
    private readonly ListView _list = new ListView
    {
        SelectionMode = ListViewSelectionMode.Single,
        MinHeight = 120,
    };

    private readonly ObservableCollection<string> _rows = new ObservableCollection<string>();
    private readonly Button _add = new Button();
    private readonly Button _edit = new Button();
    private readonly Button _remove = new Button();
    //Hidden until a caller asks for them: upstream turns internal-move
    //dragging on for the outline patterns and leaves it off everywhere else.
    private readonly Button _up = new Button
    {
        Content = "▲",
        Visibility = Visibility.Collapsed,
    };

    private readonly Button _down = new Button
    {
        Content = "▼",
        Visibility = Visibility.Collapsed,
    };

    private bool _updating;

    /// <summary>Creates the list editor.</summary>
    public ListEdit()
    {
        ColumnSpacing = 4;
        RowSpacing = 2;
        ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int row = 0; row < 6; row++)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        _list.ItemsSource = _rows;
        SetRowSpan(_list, 6);
        Children.Add(_list);

        void Place(Button button, int row)
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            SetColumn(button, 1);
            SetRow(button, row);
            Children.Add(button);
        }

        Place(_add, 0);
        Place(_edit, 1);
        Place(_remove, 2);
        Place(_up, 3);
        Place(_down, 4);

        _add.Content = MenuBuilder.Display(I18n.Get("&Add..."));
        _edit.Content = MenuBuilder.Display(I18n.Get("&Edit..."));
        _remove.Content = MenuBuilder.Display(I18n.Get("&Remove"));
        ToolTipService.SetToolTip(_up, I18n.Get("Move up"));
        ToolTipService.SetToolTip(_down, I18n.Get("Move down"));

        _add.Click += async (_, _) => await AddAsync();
        _edit.Click += async (_, _) => await EditAsync();
        _remove.Click += (_, _) => RemoveCurrent();
        _up.Click += (_, _) => MoveCurrent(-1);
        _down.Click += (_, _) => MoveCurrent(1);

        _list.SelectionChanged += (_, _) =>
        {
            if (_updating) { return; }

            Model.CurrentIndex = _list.SelectedIndex;
            UpdateSelection();
        };
        _list.DoubleTapped += async (_, _) => await EditAsync();

        UpdateSelection();
    }

    /// <summary>Raised whenever the list changed.</summary>
    public event EventHandler Changed;

    /// <summary>Gets the list's contents and the operations on them.</summary>
    public ListEditModel Model { get; } = new ListEditModel();

    /// <summary>
    /// Gets or sets how an item is edited: given the item's current text (empty
    /// for a new one), it answers the new text or null when the user cancelled.
    /// </summary>
    /// <remarks>Upstream's <c>openEditor</c>.</remarks>
    public Func<string, Task<string>> OpenEditorAsync { get; set; }

    /// <summary>Gets or sets whether the move buttons are shown.</summary>
    /// <remarks>Upstream turns internal-move dragging on only for the outline
    /// patterns, whose ORDER matters; the paths lists do not use it.</remarks>
    public bool CanReorder
    {
        get;
        set
        {
            field = value;
            _up.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _down.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Gets or sets the list's contents.</summary>
    public IReadOnlyList<string> Value
    {
        get => Model.Items;
        set
        {
            Model.SetItems(value);
            Repopulate();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Adds a button of the caller's own beside the three.</summary>
    /// <param name="button">The button.</param>
    /// <remarks>Upstream's outline group reaches into the widget's layout to
    /// put its Default button there; this is the same thing, said out loud.</remarks>
    public void AddButton(Button button)
    {
        if (button == null) { return; }

        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        SetColumn(button, 1);
        SetRow(button, 5);
        Children.Add(button);
    }

    private async Task AddAsync()
    {
        Func<string, Task<string>> editor = OpenEditorAsync;
        if (editor == null) { return; }

        string text = await editor(string.Empty);
        if (text == null) { return; }

        Model.Add(text);
        Repopulate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task EditAsync()
    {
        Func<string, Task<string>> editor = OpenEditorAsync;
        if (editor == null || !Model.HasSelection) { return; }

        string text = await editor(Model.Current);
        if (text == null) { return; }

        Model.ReplaceCurrent(text);
        Repopulate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveCurrent()
    {
        if (!Model.HasSelection) { return; }

        Model.RemoveCurrent();
        Repopulate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void MoveCurrent(int offset)
    {
        if (!Model.Move(offset)) { return; }

        Repopulate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Repopulate()
    {
        _updating = true;
        _rows.Clear();
        foreach (var item in Model.Items) { _rows.Add(item); }

        _list.SelectedIndex = Model.CurrentIndex;
        _updating = false;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        bool selected = Model.HasSelection;
        _edit.IsEnabled = selected;
        _remove.IsEnabled = selected;
        _up.IsEnabled = selected && Model.CurrentIndex > 0;
        _down.IsEnabled = selected && Model.CurrentIndex < Model.Items.Count - 1;
    }
}
