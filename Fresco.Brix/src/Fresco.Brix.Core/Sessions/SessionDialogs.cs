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

namespace Fresco.Brix.Sessions; //was previously: frescobaldi/sessions/dialog.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The two session dialogs: the one that edits a session's properties, and the
/// one that lists them all.
/// </summary>
/// <remarks>
/// Upstream's editor also carries the per-session LilyPond version chooser,
/// which FR5.1 removes. What is left is the name, the base directory, the
/// include path and the save-on-exit switch.
/// </remarks>
public static class SessionDialogs
{
    /// <summary>Edits one session's properties.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="store">The stored sessions.</param>
    /// <param name="name">The session to edit, or null for a new one.</param>
    /// <returns>The name it was saved under, or null when cancelled.</returns>
    public static async Task<string> EditAsync(
        XamlRoot xamlRoot, SessionStore store, string name)
    {
        SessionData data = name == null ? new SessionData() : store.Read(name);
        data ??= new SessionData();

        TextBox nameBox = new TextBox { Text = name ?? string.Empty, MinWidth = 360 };
        CheckBox autoSave = new CheckBox
        {
            Content = I18n.Get("Always save the list of documents in this session"),
            IsChecked = data.AutoSave,
        };
        TextBox baseDirectory = new TextBox
        {
            Text = data.BaseDirectory ?? string.Empty,
            MinWidth = 360,
        };
        TextBox includePath = new TextBox
        {
            Text = string.Join("\n", data.IncludePath),
            AcceptsReturn = true,
            Height = 90,
            MinWidth = 360,
        };

        StackPanel panel = new StackPanel { Spacing = 6, MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = I18n.Get("Please enter a name for the session:"),
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(autoSave);
        panel.Children.Add(new TextBlock { Text = I18n.Get("Base directory:") });
        panel.Children.Add(baseDirectory);
        panel.Children.Add(new TextBlock
        {
            //The marker is stripped at the point of display, never out of the
            //string: the msgid a translation is keyed by carries it.
            Text = MenuBuilder.Display(I18n.Get("&Search path:")),
        });
        panel.Children.Add(includePath);

        ContentDialog dialog = new ContentDialog
        {
            Title = name == null
                ? I18n.Get("dialog title", "New Session")
                : I18n.Format(
                    I18n.Get("dialog title", "Edit session: {name}"),
                    ("name", name)),
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = I18n.Get("OK"),
            CloseButtonText = I18n.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        void Check() => dialog.IsPrimaryButtonEnabled
            = nameBox.Text.Trim().Length > 0;

        nameBox.TextChanged += (_, _) => Check();
        Check();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) { return null; }

        string chosen = nameBox.Text.Trim();
        if (name != null
            && !string.Equals(chosen, name, StringComparison.Ordinal))
        {
            store.Rename(name, chosen);
        }

        SessionData saved = store.Read(chosen) ?? new SessionData();
        saved.AutoSave = autoSave.IsChecked == true;
        saved.BaseDirectory = baseDirectory.Text.Trim().Length == 0
            ? null
            : baseDirectory.Text.Trim();
        saved.IncludePath = includePath.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
        if (name == null) { saved.Paths = Array.Empty<string>(); }

        store.Write(chosen, saved);
        return chosen;
    }

    /// <summary>Lists the sessions and lets the user manage them.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="store">The stored sessions.</param>
    /// <returns>The task.</returns>
    public static async Task ManageAsync(XamlRoot xamlRoot, SessionStore store)
    {
        ListView list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            Height = 240,
            MinWidth = 320,
        };

        void Fill()
        {
            object selected = list.SelectedItem;
            list.ItemsSource = store.SessionNames().ToList();
            list.SelectedItem = selected;
        }

        Fill();

        Button add = new Button { Content = MenuBuilder.Display(I18n.Get("&New...")) };
        Button edit = new Button { Content = MenuBuilder.Display(I18n.Get("&Edit...")) };
        Button remove = new Button
        {
            Content = MenuBuilder.Display(I18n.Get("&Remove")),
        };

        StackPanel buttons = new StackPanel { Spacing = 6, Width = 120 };
        buttons.Children.Add(add);
        buttons.Children.Add(edit);
        buttons.Children.Add(remove);

        Grid content = new Grid { ColumnSpacing = 8, MinWidth = 460 };
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(list, 0);
        Grid.SetColumn(buttons, 1);
        content.Children.Add(list);
        content.Children.Add(buttons);

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("dialog title", "Manage Sessions"),
            Content = content,
            CloseButtonText = I18n.Get("Close"),
            XamlRoot = xamlRoot,
        };

        add.Click += async (_, _) =>
        {
            if (await EditAsync(xamlRoot, store, null) != null) { Fill(); }
        };
        edit.Click += async (_, _) =>
        {
            if (list.SelectedItem is string chosen
                && await EditAsync(xamlRoot, store, chosen) != null)
            {
                Fill();
            }
        };
        remove.Click += (_, _) =>
        {
            if (list.SelectedItem is not string chosen) { return; }

            store.Delete(chosen);
            Fill();
        };

        await dialog.ShowAsync();
    }
}
