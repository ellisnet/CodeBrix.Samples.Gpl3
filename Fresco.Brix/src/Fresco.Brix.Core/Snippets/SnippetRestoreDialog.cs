// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/restore.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The dialog that brings back built-in snippets the user changed or deleted:
/// two groups of tick boxes and one button.
/// </summary>
/// <remarks>
/// //was previously: nothing. "Restore &amp;Built-in Snippets..." — a caption
/// whose ellipsis promises a dialog — silently rewrote EVERY built-in over
/// whatever the user had made of it, with no list, no choice and no
/// confirmation. Upstream lists the ones that are actually deleted or changed
/// and restores only what is ticked.
/// </remarks>
public static class SnippetRestoreDialog
{
    /// <summary>Puts the dialog in front of the user.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="library">The snippet library.</param>
    /// <param name="shortcuts">The snippet shortcut collection, or null.</param>
    /// <returns>Whether anything was restored.</returns>
    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot, SnippetLibrary library, SnippetShortcuts shortcuts)
    {
        if (library == null) { return false; }

        SnippetCheckGroup deleted = new SnippetCheckGroup(I18n.Get("Deleted Snippets"));
        SnippetCheckGroup changed = new SnippetCheckGroup(I18n.Get("Changed Snippets"));

        //Upstream sorts the built-ins by TITLE and puts each in the group it
        //belongs to; a built-in that is untouched appears in neither.
        HashSet<string> available = new HashSet<string>(
            library.Names(), StringComparer.Ordinal);
        foreach (var snippet in BuiltinSnippets.All
            .OrderBy(s => library.Title(s.Name), StringComparer.CurrentCulture))
        {
            if (!available.Contains(snippet.Name))
            {
                deleted.Add(snippet.Name, library.Title(snippet.Name));
                continue;
            }

            if (library.IsOriginal(snippet.Name)) { continue; }

            changed.Add(snippet.Name, library.Title(snippet.Name));
        }

        StackPanel panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = I18n.Get(
                "This dialog allows you to recover built-in snippets that have "
                + "been changed or deleted. Check the snippets you want to "
                + "recover and click the button \"Restore Checked Snippets.\""),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(deleted.Panel);
        panel.Children.Add(changed.Panel);

        //Upstream's `userguide.addButton(self.buttonBox(), "snippets")'.
        panel.Children.Add(UserGuide.GuideHelp.ButtonRow("snippets"));

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("dialog title", "Restore Built-in Snippets"),
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 420,
            },
            PrimaryButtonText = I18n.Get("Restore Checked Snippets"),
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        //Upstream's `checkOkButton': nothing ticked, nothing to do.
        void Recheck() => dialog.IsPrimaryButtonEnabled
            = deleted.Checked().Count > 0 || changed.Checked().Count > 0;

        deleted.Changed += (_, _) => Recheck();
        changed.Changed += (_, _) => Recheck();
        Recheck();
        Shell.DialogSizing.Clamp(dialog, 640, 560);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) { return false; }

        List<string> names = new List<string>(deleted.Checked());
        names.AddRange(changed.Checked());
        foreach (var name in names)
        {
            //Upstream restores the SHORTCUT as well as the text, and saves the
            //snippet with a null text and title — which is how the library is
            //told to forget the user's override.
            shortcuts?.RestoreDefaultShortcuts(name);
            library.Save(name, null, null);
        }

        return names.Count > 0;
    }
}
