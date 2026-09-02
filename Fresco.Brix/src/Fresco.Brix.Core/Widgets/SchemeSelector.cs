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
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Widgets; //was previously: frescobaldi/widgets/schemeselector.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Picks one of the user's named schemes, and adds, removes and renames them.
/// The Fonts &amp; Colors and Shortcuts preference pages both sit on one.
/// </summary>
/// <remarks>
/// <para>
/// The scheme a user picks is stored under upstream's own keys: a
/// <c>&lt;thing&gt;_scheme</c> value naming the current one, and a
/// <c>&lt;thing&gt;_schemes</c> value holding every scheme's key against the
/// name the user gave it. The built-in scheme's key is always <c>default</c>
/// and it can be neither removed nor renamed; the ones the user makes are
/// <c>user1</c>, <c>user2</c>, … exactly as upstream numbers them.
/// </para>
/// <para>
/// //was previously: <c>&lt;thing&gt;_schemes/&lt;key&gt;</c> — a settings key
/// per scheme, which the flat store this replaced could enumerate by prefix.
/// The settings add-in has no prefix-scan API by design (board W13 item 9,
/// route (a)), so the names are ONE JSON object under the group's own name, and
/// the data a REMOVED scheme leaves behind is dropped by the owner of that data
/// rather than by a prefix delete here.
/// </para>
/// <para>
/// //was previously: upstream's menu also carries Import and Export, which read
/// and write Frescobaldi's own XML theme files through
/// <c>preferences/import_export.py</c>. That file is not ported here: a theme
/// file is an interchange format between two Frescobaldi installations, and
/// Fresco.Brix has none to interchange with. If it is ever wanted, this is the
/// menu it hangs on.
/// </para>
/// </remarks>
public sealed class SchemeSelector : Grid
{
    private readonly ComboBox _list = new ComboBox { MinWidth = 180 };
    private readonly Button _menuButton = new Button();
    private readonly List<(string Key, string Name)> _schemes
        = new List<(string Key, string Name)>();
    private readonly HashSet<string> _toRemove = new HashSet<string>(StringComparer.Ordinal);

    private MenuFlyoutItem _remove;
    private MenuFlyoutItem _rename;
    private bool _updating;

    /// <summary>Creates the selector.</summary>
    public SchemeSelector()
    {
        ColumnSpacing = 6;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBlock label = new TextBlock
        {
            Text = I18n.Get("Scheme:"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(label);

        SetColumn(_list, 1);
        Children.Add(_list);

        _menuButton.Content = MenuBuilder.Display(I18n.Get("&Menu"));
        _menuButton.Flyout = BuildMenu();
        SetColumn(_menuButton, 2);
        Children.Add(_menuButton);

        _list.SelectionChanged += (_, _) =>
        {
            if (_updating) { return; }

            UpdateMenuState();
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised when a different scheme is chosen.</summary>
    public event EventHandler CurrentChanged;

    /// <summary>Raised whenever anything about the schemes changed.</summary>
    public event EventHandler Changed;

    /// <summary>Gets or sets the root the Add and Rename prompts attach to.</summary>
    public XamlRoot DialogRoot { get; set; }

    /// <summary>Gets the keys of the schemes on offer.</summary>
    public IReadOnlyList<string> Schemes => _schemes.Select(s => s.Key).ToArray();

    /// <summary>Gets the key of the scheme in front of the user.</summary>
    public string CurrentSchemeKey
        => _list.SelectedIndex >= 0 && _list.SelectedIndex < _schemes.Count
            ? _schemes[_list.SelectedIndex].Key
            : "default";

    /// <summary>Reads the schemes and the chosen one out of the store.</summary>
    /// <param name="settings">The settings store.</param>
    /// <param name="currentKey">The key naming the chosen scheme, e.g.
    /// <c>editor_scheme</c>.</param>
    /// <param name="namesGroup">The group holding the names, e.g.
    /// <c>editor_schemes</c>.</param>
    public void LoadSettings(SettingsStore settings, string currentKey, string namesGroup)
    {
        //A load forgets the removals that were only pending — upstream's own
        //"don't mark schemes for removal anymore".
        _toRemove.Clear();
        _schemes.Clear();
        _schemes.Add(("default", I18n.Get("Default")));

        if (settings != null)
        {
            List<(string Key, string Name)> user = new List<(string, string)>();
            foreach (var pair in ReadNames(settings, namesGroup))
            {
                string scheme = pair.Key;
                if (string.IsNullOrEmpty(scheme)
                    || string.Equals(scheme, "default", StringComparison.Ordinal))
                {
                    continue;
                }

                user.Add((scheme,
                    string.IsNullOrEmpty(pair.Value) ? scheme : pair.Value));
            }

            //Sorted by the name the user sees, case-insensitively, which is
            //upstream's own ordering.
            foreach (var entry in user.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                _schemes.Add(entry);
            }
        }

        string current = settings?.GetString(currentKey, "default") ?? "default";
        Repopulate(current);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the schemes and the chosen one back.</summary>
    /// <param name="settings">The settings store.</param>
    /// <param name="currentKey">The key naming the chosen scheme.</param>
    /// <param name="namesGroup">The group holding the names.</param>
    /// <param name="forgetScheme">Called with the key of each scheme the user
    /// removed, so the owner of that scheme's data can drop it; null to leave
    /// the data. //was previously: a settings PREFIX this method deleted itself
    /// (<c>shortcuts</c>, <c>fontscolors/editor</c>), which needed the store to
    /// be able to enumerate keys.</param>
    public void SaveSettings(
        SettingsStore settings,
        string currentKey,
        string namesGroup,
        Action<string> forgetScheme = null)
    {
        if (settings == null) { return; }

        Dictionary<string, string> names = ReadNames(settings, namesGroup);
        foreach (var (key, name) in _schemes)
        {
            if (string.Equals(key, "default", StringComparison.Ordinal)) { continue; }

            names[key] = name;
        }

        foreach (var scheme in _toRemove)
        {
            names.Remove(scheme);
            forgetScheme?.Invoke(scheme);
        }

        if (names.Count == 0)
        {
            settings.Remove(namesGroup);
        }
        else
        {
            settings.Set(namesGroup, names);
        }

        settings.SetString(currentKey, CurrentSchemeKey);
        _toRemove.Clear();
    }

    private static Dictionary<string, string> ReadNames(
        SettingsStore settings, string namesGroup)
        => settings?.Get<Dictionary<string, string>>(namesGroup)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private MenuFlyout BuildMenu()
    {
        MenuFlyout menu = new MenuFlyout();

        MenuFlyoutItem add = new MenuFlyoutItem
        {
            Text = MenuBuilder.Display(I18n.Get("&Add...")),
        };
        add.Click += async (_, _) => await AddAsync();
        menu.Items.Add(add);

        _remove = new MenuFlyoutItem { Text = MenuBuilder.Display(I18n.Get("&Remove")) };
        _remove.Click += (_, _) => RemoveCurrent();
        menu.Items.Add(_remove);

        _rename = new MenuFlyoutItem { Text = MenuBuilder.Display(I18n.Get("Re&name...")) };
        _rename.Click += async (_, _) => await RenameAsync();
        menu.Items.Add(_rename);

        //⚠ A flyout item unsubscribes from its action when the menu closes
        //(board trap 41); these handlers are attached to the ITEM's own Click,
        //which survives, and the enabled state is refreshed when the menu is
        //about to be shown as well as on every selection change.
        menu.Opening += (_, _) => UpdateMenuState();
        return menu;
    }

    private void UpdateMenuState()
    {
        bool isDefault = string.Equals(
            CurrentSchemeKey, "default", StringComparison.Ordinal);
        if (_remove != null) { _remove.IsEnabled = !isDefault; }

        if (_rename != null) { _rename.IsEnabled = !isDefault; }
    }

    private async Task AddAsync()
    {
        TextDialog dialog = new TextDialog(
            I18n.Get("Add Scheme"),
            I18n.Get("Please enter a name for the new scheme:"));
        dialog.SetValidateFunction(text => !string.IsNullOrWhiteSpace(text));
        if (!await dialog.ShowAsync(DialogRoot ?? XamlRoot)) { return; }

        string key = NextKey();
        _schemes.Add((key, dialog.Text.Trim()));
        Repopulate(key);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RenameAsync()
    {
        string key = CurrentSchemeKey;
        if (string.Equals(key, "default", StringComparison.Ordinal)) { return; }

        int index = _schemes.FindIndex(s => string.Equals(s.Key, key, StringComparison.Ordinal));
        if (index < 0) { return; }

        TextDialog dialog = new TextDialog(I18n.Get("Rename"), I18n.Get("New name:"))
        {
            Text = _schemes[index].Name,
        };
        dialog.SetValidateFunction(text => !string.IsNullOrWhiteSpace(text));
        if (!await dialog.ShowAsync(DialogRoot ?? XamlRoot)) { return; }

        _schemes[index] = (key, dialog.Text.Trim());
        Repopulate(key);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveCurrent()
    {
        string key = CurrentSchemeKey;
        if (string.Equals(key, "default", StringComparison.Ordinal)) { return; }

        //Marked, not deleted: nothing leaves the settings until the dialog's
        //OK, which is what makes Cancel mean cancel.
        _toRemove.Add(key);
        _schemes.RemoveAll(s => string.Equals(s.Key, key, StringComparison.Ordinal));
        Repopulate("default");
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string NextKey()
    {
        int number = 1;
        string key = "user1";
        while (_schemes.Any(s => string.Equals(s.Key, key, StringComparison.Ordinal))
            || _toRemove.Contains(key))
        {
            number++;
            key = "user" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return key;
    }

    private void Repopulate(string select)
    {
        _updating = true;
        _list.Items.Clear();
        int chosen = 0;
        for (int index = 0; index < _schemes.Count; index++)
        {
            _list.Items.Add(new ComboBoxItem { Content = _schemes[index].Name });
            if (string.Equals(_schemes[index].Key, select, StringComparison.Ordinal))
            {
                chosen = index;
            }
        }

        _list.SelectedIndex = chosen;
        _updating = false;
        UpdateMenuState();
    }
}
