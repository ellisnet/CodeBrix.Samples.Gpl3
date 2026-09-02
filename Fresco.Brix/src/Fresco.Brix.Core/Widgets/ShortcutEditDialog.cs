// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace Fresco.Brix.Widgets; //was previously: frescobaldi/widgets/shortcuteditdialog.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Views and edits the keyboard shortcuts of one command: keep the default,
/// have none, or record up to four of your own — with a warning under any that
/// another command has already taken.
/// </summary>
/// <remarks>
/// //was previously: three <c>QRadioButton</c>s. Nothing in this application
/// uses a radio button and none is proved to paint on the Skia heads (the same
/// caution that drew <see cref="TrackBar"/> by hand rather than templating a
/// <c>Slider</c> — board trap 53), so the three-way choice is a
/// <c>ComboBox</c>, which every other choice in the application already is.
/// </remarks>
public sealed class ShortcutEditDialog
{
    /// <summary>How many shortcuts one command may carry.</summary>
    /// <remarks>Upstream's own four: a primary and three alternatives.</remarks>
    public const int MaximumShortcuts = 4;

    private readonly ComboBox _choice = new ComboBox
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly List<KeySequenceWidget> _entries = new List<KeySequenceWidget>();
    private readonly List<TextBlock> _conflicts = new List<TextBlock>();
    private readonly StackPanel _custom = new StackPanel { Spacing = 4 };
    private readonly TextBlock _heading = new TextBlock { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _defaultConflict = new TextBlock
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC0, 0x30, 0x30)),
        Visibility = Visibility.Collapsed,
    };

    private IReadOnlyList<KeySequence> _defaults = Array.Empty<KeySequence>();

    /// <summary>Creates the dialog.</summary>
    /// <param name="findConflict">Answers the name of the command a proposed
    /// shortcut collides with, or null when it is free.</param>
    public ShortcutEditDialog(Func<KeySequence, string> findConflict = null)
    {
        FindConflict = findConflict;

        _choice.Items.Add(new ComboBoxItem());
        _choice.Items.Add(new ComboBoxItem
        {
            Content = MenuBuilder.Display(I18n.Get("&No shortcut")),
        });
        _choice.Items.Add(new ComboBoxItem
        {
            Content = MenuBuilder.Display(I18n.Get("Use a &custom shortcut:")),
        });

        for (int number = 0; number < MaximumShortcuts; number++)
        {
            Grid row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });

            TextBlock label = new TextBlock
            {
                Text = number == 0
                    ? I18n.Get("Primary shortcut:")
                    : I18n.Format(
                        I18n.Get("Alternative #{num}:"), ("num", number)),

                //Each row is its own Grid, so the label column stays a fixed
                //width for the editors to line up; a longer translation wraps
                //rather than being cut off (board rule 7).
                TextWrapping = TextWrapping.Wrap,
                HorizontalTextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(label);

            KeySequenceWidget entry = new KeySequenceWidget(number);
            Grid.SetColumn(entry, 1);
            row.Children.Add(entry);
            _entries.Add(entry);

            TextBlock conflict = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC0, 0x30, 0x30)),
                Visibility = Visibility.Collapsed,
                HorizontalTextAlignment = TextAlignment.Center,
            };
            _conflicts.Add(conflict);

            entry.ShortcutChanged += (_, index) =>
            {
                CheckConflict(index);

                //Recording a key means the user wants a custom shortcut, which
                //is what upstream's own handler concludes.
                _choice.SelectedIndex = 2;
            };

            _custom.Children.Add(row);
            _custom.Children.Add(conflict);
        }

        _choice.SelectionChanged += (_, _) => ChoiceChanged();
    }

    /// <summary>
    /// Gets or sets how a collision is looked up: given a shortcut, the name of
    /// the command already using it, or null.
    /// </summary>
    public Func<KeySequence, string> FindConflict { get; set; }

    /// <summary>
    /// Edits one command's shortcuts, and answers the list the user settled on
    /// — or null when they cancelled.
    /// </summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="title">The command's name, as the user reads it.</param>
    /// <param name="shortcuts">The shortcuts it has now.</param>
    /// <param name="defaults">The shortcuts it was built with; empty when it
    /// has none, which hides the "use the default" choice.</param>
    /// <returns>The new shortcuts, or null.</returns>
    public async Task<IReadOnlyList<KeySequence>> EditAsync(
        XamlRoot xamlRoot,
        string title,
        IReadOnlyList<KeySequence> shortcuts,
        IReadOnlyList<KeySequence> defaults)
    {
        _defaults = defaults ?? Array.Empty<KeySequence>();
        shortcuts ??= Array.Empty<KeySequence>();

        _heading.Text = I18n.Format(
            I18n.Get("Here you can edit the shortcuts for {name}"), ("name", title));

        string defaultText = _defaults.Count > 0
            ? string.Join("; ", _defaults.Select(s => s.ToString()))
            : I18n.Get("no keyboard shortcut", "none");
        ((ComboBoxItem)_choice.Items[0]).Content = MenuBuilder.Display(I18n.Format(
            I18n.Get("Use &default shortcut ({name})"), ("name", defaultText)));
        ((ComboBoxItem)_choice.Items[0]).IsEnabled = _defaults.Count > 0;

        for (int number = 0; number < MaximumShortcuts; number++)
        {
            _entries[number].Shortcut = number < shortcuts.Count ? shortcuts[number] : null;
            _conflicts[number].Visibility = Visibility.Collapsed;
        }

        _choice.SelectedIndex = _defaults.Count > 0 && SameShortcuts(shortcuts, _defaults)
            ? 0
            : shortcuts.Count > 0 ? 2 : 1;

        for (int number = 0; number < shortcuts.Count && number < MaximumShortcuts; number++)
        {
            CheckConflict(number);
        }

        StackPanel panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(_heading);
        panel.Children.Add(_choice);
        panel.Children.Add(_defaultConflict);
        panel.Children.Add(_custom);

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("window title", "Edit Shortcut"),
            Content = panel,
            PrimaryButtonText = StandardButtons.Ok,
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        //⚠ The dialog's width is the resource, not MaxWidth (board trap 43).
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) { return null; }

        return _choice.SelectedIndex switch
        {
            0 => _defaults,
            1 => Array.Empty<KeySequence>(),
            _ => _entries.Select(e => e.Shortcut).Where(s => s != null).ToArray(),
        };
    }

    private static bool SameShortcuts(
        IReadOnlyList<KeySequence> left, IReadOnlyList<KeySequence> right)
        => left.Count == right.Count
            && left.Zip(right, (a, b) => a.Equals(b)).All(same => same);

    private void ChoiceChanged()
    {
        _custom.Visibility = _choice.SelectedIndex == 2
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_choice.SelectedIndex != 0 || _defaults.Count == 0)
        {
            _defaultConflict.Visibility = Visibility.Collapsed;
            return;
        }

        Func<KeySequence, string> find = FindConflict;
        if (find == null) { return; }

        List<string> conflicting = _defaults
            .Select(find)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        if (conflicting.Count == 0)
        {
            _defaultConflict.Visibility = Visibility.Collapsed;
            return;
        }

        _defaultConflict.Text = I18n.Format(
            I18n.Get("Conflict with: {name}"), ("name", string.Join(", ", conflicting)));
        _defaultConflict.Visibility = Visibility.Visible;
    }

    private void CheckConflict(int number)
    {
        Func<KeySequence, string> find = FindConflict;
        if (find == null || number < 0 || number >= _entries.Count) { return; }

        KeySequence shortcut = _entries[number].Shortcut;
        string name = shortcut == null ? null : find(shortcut);
        if (string.IsNullOrEmpty(name))
        {
            _conflicts[number].Visibility = Visibility.Collapsed;
            return;
        }

        _conflicts[number].Text = I18n.Format(
            I18n.Get("Conflict with: {name}"), ("name", name));
        _conflicts[number].Visibility = Visibility.Visible;
    }
}
