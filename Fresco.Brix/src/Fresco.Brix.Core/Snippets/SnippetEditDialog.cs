// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using Fresco.Brix.Widgets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/edit.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The dialog that writes a snippet: its title and its text.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: a remark saying "the shortcut lives on the panel's own
/// command here". Upstream puts a <c>Shortcut:</c> row IN this dialog, beside
/// the title, and it is here now — with upstream's three other pieces: the
/// Restore Defaults button a BUILT-IN snippet gets, the refusal to save an
/// empty snippet, and the save-or-discard question when the dialog is
/// cancelled after an edit.
/// </para>
/// <para>
/// ⚠ Upstream's text area is syntax-highlighted and switches its highlighter
/// to Python for a Python snippet. There is no Python mode to switch to
/// (FR5.3 / FD10) and this is a plain text box; the variable lines and
/// expansions are what it shows.
/// </para>
/// </remarks>
public static class SnippetEditDialog
{
    /// <summary>Puts the editor in front of the user.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="library">The snippet library.</param>
    /// <param name="name">The snippet to edit, or null for a new one.</param>
    /// <param name="text">The text a new snippet starts with, or null.</param>
    /// <param name="editorFont">The monospace font the text area uses.</param>
    /// <param name="shortcuts">The snippet shortcut collection, or null when
    /// the caller has none — the Shortcut row is then left out.</param>
    /// <param name="actions">The action manager, so a shortcut already in use
    /// elsewhere can be named.</param>
    /// <returns>The name it was saved under, or null when cancelled.</returns>
    public static async Task<string> ShowAsync(
        XamlRoot xamlRoot,
        SnippetLibrary library,
        string name,
        string text = null,
        FontFamily editorFont = null,
        SnippetShortcuts shortcuts = null,
        ActionCollectionManager actions = null)
    {
        TextBox title = new TextBox
        {
            Text = name == null ? string.Empty : library.Title(name, fallback: false),
            MinWidth = 420,
        };
        TextBox body = new TextBox
        {
            Text = text ?? (name == null ? string.Empty : library.Text(name)),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 260,
            MinWidth = 420,
        };
        if (editorFont != null) { body.FontFamily = editorFont; }

        //Upstream's Shortcut: row — a button showing the current shortcut, or
        //_("None"), which opens the same editor the Shortcuts preferences page
        //uses. Its tooltip is upstream's own.
        List<KeySequence> chosen = new List<KeySequence>(
            name == null || shortcuts == null
                ? Array.Empty<KeySequence>()
                : shortcuts.Shortcuts(name));
        Button shortcutButton = new Button();
        void ShowShortcut() => shortcutButton.Content = chosen.Count == 0
            ? I18n.Get("None")
            : chosen[0].ToString() + (chosen.Count > 1 ? "..." : string.Empty);

        ShowShortcut();
        ToolTipService.SetToolTip(
            shortcutButton, I18n.Get("Click to change the keyboard shortcut."));
        shortcutButton.Click += async (_, _) =>
        {
            ShortcutEditDialog editor = new ShortcutEditDialog(
                sequence => actions?.FindShortcutConflict(sequence, shortcuts, name));
            IReadOnlyList<KeySequence> answer = await editor.EditAsync(
                xamlRoot,
                string.IsNullOrWhiteSpace(title.Text)
                    ? I18n.Get("Untitled")
                    : title.Text,
                chosen,
                name == null || shortcuts == null
                    ? Array.Empty<KeySequence>()
                    : shortcuts.DefaultShortcuts(name));
            if (answer == null) { return; }

            chosen.Clear();
            chosen.AddRange(answer);
            ShowShortcut();
        };

        StackPanel panel = new StackPanel { Spacing = 6, MinWidth = 420 };
        panel.Children.Add(new TextBlock { Text = I18n.Get("Title:") });
        panel.Children.Add(title);
        if (shortcuts != null)
        {
            panel.Children.Add(new TextBlock { Text = I18n.Get("Shortcut:") });
            panel.Children.Add(shortcutButton);
        }

        panel.Children.Add(new TextBlock { Text = I18n.Get("&Text:") });
        panel.Children.Add(body);
        panel.Children.Add(new TextBlock
        {
            //The variable lines are the snippet's own contract; naming them
            //here is what upstream's What's This does for the same dialog.
            Text = I18n.Get(
                "A snippet may begin with lines like \"-*- menu; indent: no;\" "
                + "that declare where it appears and how it behaves."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        //Upstream's `userguide.addButton(b, "snippet_editor")'.
        panel.Children.Add(UserGuide.GuideHelp.ButtonRow("snippet_editor"));

        //Upstream gives a BUILT-IN snippet a Restore Defaults button, which
        //puts back its shipped text, title and shortcut. A ContentDialog holds
        //three buttons (board trap 50) and OK/Cancel take two, so this is the
        //third — and a snippet the user wrote has no defaults to restore, so it
        //appears only where upstream's does.
        bool builtin = name != null && BuiltinSnippets.ByName.ContainsKey(name);
        ContentDialog dialog = new ContentDialog
        {
            Title = name == null
                ? I18n.Get("Add Snippet")
                : I18n.Get("Edit Snippet"),
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = StandardButtons.Ok,
            SecondaryButtonText = builtin ? StandardButtons.RestoreDefaults : null,
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        string startingText = body.Text;
        string startingTitle = title.Text;

        dialog.SecondaryButtonClick += (_, args) =>
        {
            //Restoring does not close the dialog: upstream's button is an
            //ordinary one on the button box, and the user can still cancel.
            args.Cancel = true;
            if (!BuiltinSnippets.ByName.TryGetValue(name, out var shipped)) { return; }

            body.Text = shipped.Text;
            title.Text = shipped.Title ?? string.Empty;
            chosen.Clear();
            if (shortcuts != null) { chosen.AddRange(shortcuts.DefaultShortcuts(name)); }

            ShowShortcut();
        };

        while (true)
        {
            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                //Upstream refuses to save an empty snippet and says so.
                if (string.IsNullOrEmpty(body.Text))
                {
                    await Shell.InputDialogs.AlertAsync(
                        xamlRoot,
                        I18n.Get("Empty Snippet"),
                        I18n.Get("A snippet can't be empty."));
                    continue;
                }

                break;
            }

            //Cancelled. Upstream asks before losing an edit, with Save,
            //Discard and Cancel.
            bool modified = !string.Equals(body.Text, startingText, StringComparison.Ordinal)
                || !string.Equals(title.Text, startingTitle, StringComparison.Ordinal);
            if (!modified) { return null; }

            SaveDiscard answer = await AskSaveDiscardAsync(
                xamlRoot,
                dialog.Title as string,
                I18n.Get("The snippet has been modified.\n"
                    + "Do you want to save your changes or discard them?"));
            if (answer == SaveDiscard.Cancel) { continue; }

            if (answer == SaveDiscard.Discard) { return null; }

            if (string.IsNullOrEmpty(body.Text))
            {
                await Shell.InputDialogs.AlertAsync(
                    xamlRoot,
                    I18n.Get("Empty Snippet"),
                    I18n.Get("A snippet can't be empty."));
                continue;
            }

            break;
        }

        string saved = library.Save(
            name,
            body.Text,
            string.IsNullOrWhiteSpace(title.Text) ? null : title.Text);
        shortcuts?.SetShortcuts(saved, chosen);
        return saved;
    }

    /// <summary>What the save-or-discard question was answered with.</summary>
    private enum SaveDiscard
    {
        /// <summary>Throw the edit away.</summary>
        Discard,

        /// <summary>Keep it.</summary>
        Save,

        /// <summary>Go back to the editor.</summary>
        Cancel,
    }

    /// <summary>Asks whether to save, discard or go back.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="title">The editor's own title, which upstream reuses.</param>
    /// <param name="message">The question.</param>
    /// <returns>The answer.</returns>
    private static async Task<SaveDiscard> AskSaveDiscardAsync(
        XamlRoot xamlRoot, string title, string message)
    {
        ContentDialog dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = StandardButtons.Save,
            SecondaryButtonText = StandardButtons.Discard,
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => SaveDiscard.Save,
            ContentDialogResult.Secondary => SaveDiscard.Discard,
            _ => SaveDiscard.Cancel,
        };
    }
}
