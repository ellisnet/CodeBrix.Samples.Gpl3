// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Fresco.Brix.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/pitch/dialog.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The key and the mode the user chose.</summary>
public sealed class ModeShiftChoice
{
    /// <summary>Creates the choice.</summary>
    /// <param name="key">The key, as the user typed it.</param>
    /// <param name="mode">The mode's name.</param>
    public ModeShiftChoice(string key, string mode)
    {
        Key = key;
        Mode = mode;
    }

    /// <summary>Gets the key.</summary>
    public string Key { get; }

    /// <summary>Gets the mode's name.</summary>
    public string Mode { get; }
}

/// <summary>
/// Asks for a key and a mode, and remembers both for next time.
/// </summary>
/// <remarks>
/// The OK button — which upstream labels "shift pitches" rather than OK — is
/// off until the key box holds something that reads as a pitch in the
/// document's own pitch-name language, which is upstream's validator.
/// </remarks>
public static class ModeShiftDialog
{
    /// <summary>The settings key the last key typed is remembered under.</summary>
    public const string KeySettingName = "mode_shift/key";

    /// <summary>The settings key the last mode chosen is remembered under.</summary>
    public const string ModeSettingName = "mode_shift/mode";

    /// <summary>Shows the dialog.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="settings">The store the last choice lives in.</param>
    /// <param name="language">The document's pitch-name language, which the
    /// key is read in.</param>
    /// <returns>The choice, or null when the user cancelled.</returns>
    public static async Task<ModeShiftChoice> ShowAsync(
        XamlRoot xamlRoot, SettingsStore settings, string language)
    {
        TextBox keyBox = new TextBox
        {
            Text = settings?.GetString(KeySettingName, string.Empty) ?? string.Empty,
            MinWidth = 200,
        };

        ComboBox modeBox = new ComboBox
        {
            ItemsSource = PitchModes.Names.ToList(),
            MinWidth = 200,
        };
        int stored = settings?.GetInt(ModeSettingName) ?? 0;
        modeBox.SelectedIndex = stored >= 0 && stored < PitchModes.Names.Count
            ? stored
            : 0;

        Grid grid = new Grid { ColumnSpacing = 8, RowSpacing = 8, MinWidth = 320 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock keyLabel = new TextBlock
        {
            Text = I18n.Get("Key:"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock modeLabel = new TextBlock
        {
            Text = I18n.Get("Mode:"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(keyLabel, 0);
        Grid.SetColumn(keyLabel, 0);
        Grid.SetRow(keyBox, 0);
        Grid.SetColumn(keyBox, 1);
        Grid.SetRow(modeLabel, 1);
        Grid.SetColumn(modeLabel, 0);
        Grid.SetRow(modeBox, 1);
        Grid.SetColumn(modeBox, 1);
        grid.Children.Add(keyLabel);
        grid.Children.Add(keyBox);
        grid.Children.Add(modeLabel);
        grid.Children.Add(modeBox);

        //Upstream's `userguide.addButton(self.buttons, "mode_shift")'. A
        //ContentDialog's three buttons are spent (board trap 50), so the Help
        //button goes inside the content.
        StackPanel content = new StackPanel { Spacing = 8 };
        content.Children.Add(grid);
        content.Children.Add(UserGuide.GuideHelp.ButtonRow("mode_shift"));

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("Mode Shift"),
            Content = content,
            PrimaryButtonText = I18n.Get("shift pitches"),
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        void Check() => dialog.IsPrimaryButtonEnabled
            = PitchTools.IsModeShiftKey(keyBox.Text, language);

        keyBox.TextChanged += (_, _) => Check();
        Check();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) { return null; }

        string mode = modeBox.SelectedItem as string ?? PitchModes.Names[0];
        if (settings != null)
        {
            settings.SetString(KeySettingName, keyBox.Text);
            settings.SetInt(ModeSettingName, Math.Max(0, modeBox.SelectedIndex));
        }

        return new ModeShiftChoice(keyBox.Text, mode);
    }
}
