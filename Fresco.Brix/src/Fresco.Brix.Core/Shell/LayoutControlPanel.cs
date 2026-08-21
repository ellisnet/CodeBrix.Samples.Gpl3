// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/layoutcontrol/widget.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Layout Control Options panel: which of the layout-control formatters a
/// layout-control engrave switches on.
/// </summary>
/// <remarks>
/// The state is remembered between sessions, exactly as upstream remembers it,
/// so a user debugging a spacing problem does not re-tick seven boxes every
/// time they reopen the application.
/// </remarks>
public sealed class LayoutControlPanel : Panel
{
    private readonly SettingsStore _settings;
    private readonly EngraveActions _actions;
    private readonly Dictionary<string, CheckBox> _checkBoxes
        = new Dictionary<string, CheckBox>(StringComparer.Ordinal);

    private CheckBox _verbose;
    private CheckBox _pointAndClick;
    private CheckBox _customFile;
    private TextBox _customFileName;

    /// <summary>Creates the panel.</summary>
    /// <param name="actions">The engrave commands, for the run button.</param>
    /// <param name="settings">The settings store, or null.</param>
    public LayoutControlPanel(EngraveActions actions, SettingsStore settings = null)
        : base("layoutcontrol", DockArea.Left)
    {
        _actions = actions;
        _settings = settings;
        ToggleAction.WithShortcut("Meta+Alt+C");
    }

    /// <summary>Raised when any option changes.</summary>
    public event EventHandler OptionsChanged;

    /// <inheritdoc/>
    public override string Title => I18n.Get("Layout Control Options");

    /// <inheritdoc/>
    public override void TranslateUI()
        => ToggleAction.Text = I18n.Get("Layout &Control Options");

    /// <summary>Gets the options a layout-control run is configured with.</summary>
    /// <returns>The tokens.</returns>
    public IReadOnlyList<string> PreviewOptions()
        => LayoutControl.PreviewOptions(
            SelectedModes(),
            _pointAndClick?.IsChecked ?? true,
            _customFile?.IsChecked == true ? _customFileName?.Text : null,
            _verbose?.IsChecked ?? false);

    /// <summary>Writes the panel's state to the settings store.</summary>
    public void SaveSettings()
    {
        if (_settings == null) { return; }

        foreach (var mode in LayoutControl.ModeList)
        {
            _settings.SetBool(
                LayoutControl.SettingsPrefix + mode,
                _checkBoxes.TryGetValue(mode, out var box) && box.IsChecked == true);
        }

        _settings.SetBool(
            LayoutControl.SettingsPrefix + "verbose", _verbose?.IsChecked == true);
        _settings.SetBool(
            LayoutControl.SettingsPrefix + "point-and-click",
            _pointAndClick?.IsChecked != false);
        _settings.SetBool(
            LayoutControl.SettingsPrefix + "custom-file", _customFile?.IsChecked == true);
        _settings.SetString(
            LayoutControl.SettingsPrefix + "custom-filename",
            _customFileName?.Text ?? string.Empty);
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        StackPanel panel = new StackPanel { Spacing = 2, Padding = new Thickness(6) };

        if (_actions != null)
        {
            Button run = new Button
            {
                Content = MenuBuilder.Display(_actions.EngraveDebug.Text),
                Margin = new Thickness(0, 0, 0, 6),
            };
            run.Click += (_, _) => _actions.EngraveDebug.Trigger();
            panel.Children.Add(run);
        }

        _verbose = AddCheckBox(
            panel, I18n.Get("Verbose output"), I18n.Get("Run LilyPort with verbose output"));
        _pointAndClick = AddCheckBox(
            panel,
            I18n.Get("Point-and-Click"),
            I18n.Get("Run LilyPort in preview mode (with Point and Click)"));

        foreach (var mode in LayoutControl.ModeList)
        {
            _checkBoxes[mode] = AddCheckBox(
                panel, LayoutControl.Label(mode), LayoutControl.ToolTip(mode));
        }

        _customFile = AddCheckBox(
            panel,
            I18n.Get("Include Custom File:"),
            I18n.Get("Include a custom file with definitions\n"
                + "for additional Layout Control Modes"));

        _customFileName = new TextBox { IsEnabled = false };
        ToolTipService.SetToolTip(_customFileName, I18n.Get("Filename to be included"));
        _customFileName.TextChanged += (_, _) => Changed();
        panel.Children.Add(_customFileName);

        _customFile.Checked += (_, _) => _customFileName.IsEnabled = true;
        _customFile.Unchecked += (_, _) => _customFileName.IsEnabled = false;

        LoadSettings();
        return panel;
    }

    private IEnumerable<string> SelectedModes()
        => LayoutControl.ModeList.Where(
            mode => _checkBoxes.TryGetValue(mode, out var box) && box.IsChecked == true);

    private CheckBox AddCheckBox(StackPanel panel, string text, string toolTip)
    {
        CheckBox box = new CheckBox { Content = text };
        ToolTipService.SetToolTip(box, toolTip);
        box.Checked += (_, _) => Changed();
        box.Unchecked += (_, _) => Changed();
        panel.Children.Add(box);
        return box;
    }

    private void Changed() => OptionsChanged?.Invoke(this, EventArgs.Empty);

    private void LoadSettings()
    {
        if (_settings == null)
        {
            _pointAndClick.IsChecked = true;
            return;
        }

        foreach (var mode in LayoutControl.ModeList)
        {
            _checkBoxes[mode].IsChecked
                = _settings.GetBool(LayoutControl.SettingsPrefix + mode);
        }

        _verbose.IsChecked = _settings.GetBool(LayoutControl.SettingsPrefix + "verbose");
        _pointAndClick.IsChecked = _settings.GetBool(
            LayoutControl.SettingsPrefix + "point-and-click", true);
        _customFile.IsChecked = _settings.GetBool(
            LayoutControl.SettingsPrefix + "custom-file");
        _customFileName.Text = _settings.GetString(
            LayoutControl.SettingsPrefix + "custom-filename", string.Empty);
        _customFileName.IsEnabled = _customFile.IsChecked == true;
    }
}
