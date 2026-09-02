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
using System.IO;
using System.Linq;
using System.Text.Json;
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

        //Upstream's `userguide.addButton(b, "sessions")'.
        panel.Children.Add(UserGuide.GuideHelp.ButtonRow("sessions"));

        ContentDialog dialog = new ContentDialog
        {
            Title = name == null
                ? I18n.Get("Edit new session")
                : I18n.Format(
                    I18n.Get("Edit session: {name}"),
                    ("name", name)),
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = StandardButtons.Ok,
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        void Check() => dialog.IsPrimaryButtonEnabled
            = nameBox.Text.Trim().Length > 0;

        nameBox.TextChanged += (_, _) => Check();
        Check();

        //was previously: the dialog closed on OK and whatever was typed was
        //written, so a name that already existed SILENTLY overwrote another
        //session. Upstream refuses to close until the name is acceptable
        //(SessionEditor.done -> validate), and this is that loop: the dialog is
        //re-shown with everything the user typed still in it.
        string chosen;
        while (true)
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) { return null; }

            chosen = nameBox.Text.Trim();
            nameBox.Text = chosen;

            if (chosen.Length == 0)
            {
                await Shell.InputDialogs.AlertAsync(
                    xamlRoot,
                    I18n.Get("Warning"),
                    I18n.Get("Please enter a session name."));
                if (name != null) { nameBox.Text = name; }

                continue;
            }

            //Upstream's reserved name: `-' is what the "no session" entry is
            //stored as, so a session may not be called it.
            if (string.Equals(chosen, "-", StringComparison.Ordinal))
            {
                await Shell.InputDialogs.AlertAsync(
                    xamlRoot,
                    I18n.Get("Warning"),
                    I18n.Format(
                        I18n.Get("Please do not use the name '{name}'."),
                        ("name", "-")));
                continue;
            }

            if (!string.Equals(chosen, name, StringComparison.Ordinal)
                && store.SessionNames().Contains(chosen))
            {
                bool overwrite = await Shell.InputDialogs.ConfirmAsync(
                    xamlRoot,
                    I18n.Get("Warning"),
                    I18n.Format(
                        I18n.Get("Another session with the name {name} already "
                            + "exists.\n\nDo you want to overwrite it?"),
                        ("name", chosen)),
                    StandardButtons.Overwrite);
                if (!overwrite) { continue; }
            }

            break;
        }

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
    /// <param name="pickImportPathAsync">Asks the user which file to import a
    /// session from, or null when the head has no file dialog.</param>
    /// <param name="pickExportPathAsync">Asks where to export a session to,
    /// given a suggested file name, or null.</param>
    /// <param name="activateAsync">Switches to a session, or null.</param>
    /// <returns>The task.</returns>
    /// <remarks>//was previously: New, Edit, Remove and Close only. Upstream's
    /// <c>SessionManagerDialog</c> also carries Import, Export and Activate
    /// beside them, and a Help button on its button box.</remarks>
    public static async Task ManageAsync(
        XamlRoot xamlRoot,
        SessionStore store,
        Func<Task<string>> pickImportPathAsync = null,
        Func<string, Task<string>> pickExportPathAsync = null,
        Func<string, Task> activateAsync = null)
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

        Button add = new Button { Content = MenuBuilder.Display(I18n.Get("New Session", "&New...")) };
        Button edit = new Button { Content = MenuBuilder.Display(I18n.Get("&Edit...")) };
        Button remove = new Button
        {
            Content = MenuBuilder.Display(I18n.Get("&Remove")),
        };

        Button import = new Button
        {
            Content = MenuBuilder.Display(I18n.Get("&Import...")),
            IsEnabled = pickImportPathAsync != null,
        };
        ToolTipService.SetToolTip(
            import, I18n.Get("Opens a dialog to import a session from a file."));
        Button export = new Button
        {
            Content = MenuBuilder.Display(I18n.Get("E&xport...")),
            IsEnabled = false,
        };
        ToolTipService.SetToolTip(
            export, I18n.Get("Opens a dialog to export a session to a file."));
        Button activate = new Button
        {
            Content = MenuBuilder.Display(I18n.Get("&Activate")),
            IsEnabled = false,
        };
        ToolTipService.SetToolTip(
            activate, I18n.Get("Switches to the selected session."));

        StackPanel buttons = new StackPanel { Spacing = 6, Width = 120 };
        buttons.Children.Add(add);
        buttons.Children.Add(edit);
        buttons.Children.Add(remove);
        buttons.Children.Add(import);
        buttons.Children.Add(export);
        buttons.Children.Add(activate);
        buttons.Children.Add(UserGuide.GuideHelp.Button("sessions"));

        //Upstream's `enableButtons': Export and Activate need a selection.
        void EnableButtons()
        {
            bool selected = list.SelectedItem is string;
            export.IsEnabled = selected && pickExportPathAsync != null;
            activate.IsEnabled = selected && activateAsync != null;
        }

        list.SelectionChanged += (_, _) => EnableButtons();
        EnableButtons();

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
            Title = I18n.Get("Manage Sessions"),
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
            EnableButtons();
        };
        import.Click += async (_, _) =>
        {
            if (pickImportPathAsync == null) { return; }

            string path = await pickImportPathAsync();
            if (string.IsNullOrEmpty(path)) { return; }

            try
            {
                StoredSessionFile file = JsonSerializer.Deserialize<StoredSessionFile>(
                    File.ReadAllText(path));
                if (file?.Name == null) { return; }

                store.Write(file.Name, file.ToData());
                Fill();
                list.SelectedItem = file.Name;
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or JsonException)
            {
                await Shell.InputDialogs.AlertAsync(
                    xamlRoot,
                    I18n.Get("Error"),
                    I18n.Format(I18n.Get("Could not read from: {url}"), ("url", path))
                        + "\n\n" + error.Message);
            }
        };
        export.Click += async (_, _) =>
        {
            if (pickExportPathAsync == null
                || list.SelectedItem is not string chosen)
            {
                return;
            }

            string path = await pickExportPathAsync(chosen + ".json");
            if (string.IsNullOrEmpty(path)) { return; }

            try
            {
                File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(
                        StoredSessionFile.From(chosen, store.Read(chosen)),
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException or JsonException)
            {
                await Shell.InputDialogs.AlertAsync(
                    xamlRoot,
                    I18n.Get("Error"),
                    I18n.Format(I18n.Get("Could not write to: {url}"), ("url", path))
                        + "\n\n" + error.Message);
            }
        };
        activate.Click += async (_, _) =>
        {
            if (activateAsync == null || list.SelectedItem is not string chosen)
            {
                return;
            }

            //Upstream closes the manager and switches; the switch opens and
            //closes documents, so the dialog goes first.
            dialog.Hide();
            await activateAsync(chosen);
        };

        await dialog.ShowAsync();
    }
}

/// <summary>
/// One session as an exported FILE — upstream's json dictionary, which is a
/// session's settings group with its name added.
/// </summary>
/// <remarks>Upstream's <c>SessionList.exportItem</c> dumps every key of the
/// session's settings group plus the name; the keys here are this port's own
/// <see cref="SessionData"/>, so an exported file is readable by this
/// application and not by Frescobaldi. That is a divergence of FORMAT and not
/// of behaviour: the two applications store a session's URLs differently
/// (upstream writes QUrl strings), so a shared format would be a lie.</remarks>
public sealed class StoredSessionFile
{
    /// <summary>Gets or sets the session's name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the documents it holds.</summary>
    public List<string> Urls { get; set; }

    /// <summary>Gets or sets which of them was in front.</summary>
    public int ActiveIndex { get; set; }

    /// <summary>Gets or sets whether the document list is always saved.</summary>
    public bool AutoSave { get; set; }

    /// <summary>Gets or sets the session's base directory.</summary>
    public string BaseDirectory { get; set; }

    /// <summary>Gets or sets the session's own include path.</summary>
    public List<string> IncludePath { get; set; }

    /// <summary>Makes an exportable record of a session.</summary>
    /// <param name="name">The session name.</param>
    /// <param name="data">Its data, or null.</param>
    /// <returns>The record.</returns>
    public static StoredSessionFile From(string name, SessionData data)
    {
        data ??= new SessionData();
        return new StoredSessionFile
        {
            Name = name,
            Urls = new List<string>(data.Paths ?? Array.Empty<string>()),
            ActiveIndex = data.ActiveIndex,
            AutoSave = data.AutoSave,
            BaseDirectory = data.BaseDirectory,
            IncludePath = new List<string>(data.IncludePath ?? Array.Empty<string>()),
        };
    }

    /// <summary>Reads the record back as session data.</summary>
    /// <returns>The data.</returns>
    public SessionData ToData()
        => new SessionData
        {
            Paths = Urls ?? new List<string>(),
            ActiveIndex = ActiveIndex,
            AutoSave = AutoSave,
            BaseDirectory = BaseDirectory,
            IncludePath = IncludePath ?? new List<string>(),
        };
}
