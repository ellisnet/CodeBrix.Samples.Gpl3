// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.ScoreWizard;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Fresco.Brix.Shell;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Draws a list of <see cref="PartSetting"/>s as the controls they describe.
/// </summary>
/// <remarks>
/// Upstream's part types build their own widgets; here they describe them and
/// this draws them, so that one reading of the description keeps the Score
/// Wizard's three pages and every part type's own panel looking alike.
/// </remarks>
public static class SettingsEditor
{
    /// <summary>Draws a list of settings.</summary>
    /// <param name="settings">The settings, in display order.</param>
    /// <returns>The controls.</returns>
    public static UIElement Build(IEnumerable<PartSetting> settings)
    {
        StackPanel panel = new StackPanel { Spacing = 6 };
        foreach (PartSetting setting in settings)
        {
            UIElement element = BuildOne(setting);
            if (element != null) { panel.Children.Add(element); }
        }

        return panel;
    }

    /// <summary>Draws a titled group of settings.</summary>
    /// <param name="title">The group's title.</param>
    /// <param name="settings">The settings inside it.</param>
    /// <returns>The group.</returns>
    public static UIElement Group(string title, IEnumerable<PartSetting> settings)
        => Wrap(title, Build(settings));

    /// <summary>Draws a group whose own tick turns its contents on and off.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The group.</returns>
    public static UIElement Group(GroupSetting group)
    {
        UIElement content = Build(group.Children);
        if (!group.IsCheckable) { return Wrap(group.LabelText(), content); }

        CheckBox tick = new CheckBox
        {
            Content = group.LabelText(),
            IsChecked = group.IsChecked,
            FontWeight = FontWeights.SemiBold,
        };
        string toolTip = group.ToolTipText();
        if (!string.IsNullOrEmpty(toolTip)) { ToolTipService.SetToolTip(tick, toolTip); }

        content.Visibility = group.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        tick.Checked += (_, _) =>
        {
            group.IsChecked = true;
            content.Visibility = Visibility.Visible;
        };
        tick.Unchecked += (_, _) =>
        {
            group.IsChecked = false;
            content.Visibility = Visibility.Collapsed;
        };

        StackPanel panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(tick);
        panel.Children.Add(content);
        return Wrap(null, panel);
    }

    /// <summary>Puts a box with an optional title around something.</summary>
    /// <param name="title">The title, or null for none.</param>
    /// <param name="content">What goes inside.</param>
    /// <returns>The box.</returns>
    public static UIElement Wrap(string title, UIElement content)
    {
        StackPanel panel = new StackPanel { Spacing = 6 };
        if (!string.IsNullOrEmpty(title))
        {
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
            });
        }

        panel.Children.Add(content);

        return new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current
                .Resources["TextControlBorderBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = panel,
        };
    }

    /// <summary>Draws one setting.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The control, or null when there is nothing to draw.</returns>
    private static UIElement BuildOne(PartSetting setting)
    {
        switch (setting)
        {
            case NoticeSetting notice:
                return new TextBlock
                {
                    Text = notice.LabelText(),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8,
                };
            case BoolSetting boolean:
                return BuildCheckBox(boolean);
            case GroupSetting group:
                return Group(group);
            case NumberSetting number:
                return Labelled(setting, BuildNumberBox(number));
            case TextSetting text:
                return Labelled(setting, BuildTextBox(text));
            case ChoiceSetting choice:
                return Labelled(setting, BuildComboBox(choice));
            default:
                return null;
        }
    }

    /// <summary>Draws a check box.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The box.</returns>
    private static UIElement BuildCheckBox(BoolSetting setting)
    {
        CheckBox box = new CheckBox
        {
            Content = setting.LabelText(),
            IsChecked = setting.Value,
            IsEnabled = setting.IsEnabled,
        };
        Explain(box, setting);
        box.Checked += (_, _) => setting.Value = true;
        box.Unchecked += (_, _) => setting.Value = false;
        setting.Changed += (_, _) =>
        {
            box.IsChecked = setting.Value;
            box.IsEnabled = setting.IsEnabled;
        };
        return box;
    }

    /// <summary>Draws a number entry.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The entry.</returns>
    /// <remarks>A plain text box rather than a spin control: the theme's own
    /// number box is one more control that has to be proved on every head, and
    /// what these settings need is a small number typed or nudged.</remarks>
    private static UIElement BuildNumberBox(NumberSetting setting)
    {
        TextBox box = new TextBox
        {
            Text = setting.Value.ToString(CultureInfo.InvariantCulture),
            Width = 56,
            IsEnabled = setting.IsEnabled,
            TextAlignment = TextAlignment.Right,
        };
        bool writing = false;

        box.TextChanged += (_, _) =>
        {
            if (writing) { return; }

            if (int.TryParse(
                box.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
            {
                setting.Value = value;
            }
        };
        box.LostFocus += (_, _) =>
        {
            writing = true;
            box.Text = setting.Value.ToString(CultureInfo.InvariantCulture);
            writing = false;
        };

        Button less = new Button { Content = "−", Padding = new Thickness(6, 2, 6, 2) };
        Button more = new Button { Content = "+", Padding = new Thickness(6, 2, 6, 2) };
        less.Click += (_, _) => setting.Value--;
        more.Click += (_, _) => setting.Value++;

        setting.Changed += (_, _) =>
        {
            writing = true;
            string text = setting.Value.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(box.Text, text, StringComparison.Ordinal)) { box.Text = text; }

            box.IsEnabled = setting.IsEnabled;
            less.IsEnabled = setting.IsEnabled;
            more.IsEnabled = setting.IsEnabled;
            writing = false;
        };

        StackPanel row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        row.Children.Add(box);
        row.Children.Add(less);
        row.Children.Add(more);
        return row;
    }

    /// <summary>Draws a text entry.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The entry.</returns>
    private static UIElement BuildTextBox(TextSetting setting)
    {
        TextBox box = new TextBox
        {
            Text = setting.Value,
            IsEnabled = setting.IsEnabled,
            PlaceholderText = setting.PlaceholderText?.Invoke() ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        box.TextChanged += (_, _) => setting.Value = box.Text;
        setting.Changed += (_, _) =>
        {
            if (!string.Equals(box.Text, setting.Value, StringComparison.Ordinal))
            {
                box.Text = setting.Value;
            }

            box.IsEnabled = setting.IsEnabled;
        };
        return box;
    }

    /// <summary>Draws a list to pick from.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The list, with a text entry beside it when the setting takes
    /// a value of the user's own.</returns>
    /// <remarks>
    /// An editable combo box would be the one control for this, and it is what
    /// upstream uses; the platform's own is not proved on the Skia heads, and
    /// a list the user cannot type into would lose the time signatures and
    /// voicings that are not on it. A list that FILLS a text entry beside it
    /// says the same thing with two controls that both paint.
    /// </remarks>
    private static UIElement BuildComboBox(ChoiceSetting setting)
    {
        ComboBox list = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = setting.IsEnabled,
        };
        foreach (ChoiceItem item in setting.Items)
        {
            ComboBoxItem row = new ComboBoxItem { Content = item.LabelText() };
            string toolTip = item.ToolTip?.Invoke();
            if (!string.IsNullOrEmpty(toolTip)) { ToolTipService.SetToolTip(row, toolTip); }

            list.Items.Add(row);
        }

        list.SelectedIndex = setting.SelectedIndex;

        TextBox typed = null;
        if (setting.IsEditable)
        {
            typed = new TextBox { Text = setting.Text, Width = 96, IsEnabled = setting.IsEnabled };
            typed.TextChanged += (_, _) => setting.SetText(typed.Text);
        }

        bool writing = false;
        list.SelectionChanged += (_, _) =>
        {
            if (writing) { return; }

            setting.SelectedIndex = list.SelectedIndex;
        };

        setting.Changed += (_, _) =>
        {
            writing = true;
            if (list.SelectedIndex != setting.SelectedIndex)
            {
                list.SelectedIndex = setting.SelectedIndex;
            }

            //Re-read the rows: a key list changes what it says when the pitch
            //name language changes, without changing what is chosen.
            for (int index = 0; index < setting.Items.Count && index < list.Items.Count; index++)
            {
                if (list.Items[index] is ComboBoxItem row)
                {
                    row.Content = setting.Items[index].LabelText();
                }
            }

            list.IsEnabled = setting.IsEnabled;
            if (typed != null)
            {
                if (!string.Equals(typed.Text, setting.Text, StringComparison.Ordinal))
                {
                    typed.Text = setting.Text;
                }

                typed.IsEnabled = setting.IsEnabled;
            }

            writing = false;
        };

        if (typed == null) { return list; }

        Grid row2 = new Grid { ColumnSpacing = 4 };
        row2.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row2.Children.Add(list);
        Grid.SetColumn(typed, 1);
        row2.Children.Add(typed);
        return row2;
    }

    /// <summary>Puts a setting's label in front of its control.</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="control">The control.</param>
    /// <returns>The pair.</returns>
    private static UIElement Labelled(PartSetting setting, UIElement control)
    {
        string label = setting.LabelText();
        if (string.IsNullOrEmpty(label)) { return control; }

        Grid row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        TextBlock text = new TextBlock
        {
            Text = MenuBuilder.Display(label),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Explain(text, setting);
        row.Children.Add(text);

        Grid.SetColumn(control, 1);
        row.Children.Add(control);

        setting.Changed += (_, _) => text.Opacity = setting.IsEnabled ? 1.0 : 0.5;
        text.Opacity = setting.IsEnabled ? 1.0 : 0.5;
        return row;
    }

    /// <summary>Hangs a setting's tooltip on a control.</summary>
    /// <param name="element">The control.</param>
    /// <param name="setting">The setting.</param>
    private static void Explain(DependencyObject element, PartSetting setting)
    {
        string toolTip = setting.ToolTipText();
        if (!string.IsNullOrEmpty(toolTip)) { ToolTipService.SetToolTip(element, toolTip); }
    }
}
