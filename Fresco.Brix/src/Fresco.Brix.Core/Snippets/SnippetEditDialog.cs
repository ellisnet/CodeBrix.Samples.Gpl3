// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;

namespace Fresco.Brix.Snippets; //was previously: frescobaldi/snippet/edit.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The dialog that writes a snippet: its title and its text.
/// </summary>
/// <remarks>
/// Upstream's editor also carries a shortcut button and a syntax-highlighted
/// text area whose highlighter switches to Python for a Python snippet. The
/// shortcut lives on the panel's own command here, and there is no Python mode
/// to switch to (FR5.3); the variable lines and expansions are what the text
/// area shows.
/// </remarks>
public static class SnippetEditDialog
{
    /// <summary>Puts the editor in front of the user.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="library">The snippet library.</param>
    /// <param name="name">The snippet to edit, or null for a new one.</param>
    /// <param name="text">The text a new snippet starts with, or null.</param>
    /// <param name="editorFont">The monospace font the text area uses.</param>
    /// <returns>The name it was saved under, or null when cancelled.</returns>
    public static async Task<string> ShowAsync(
        XamlRoot xamlRoot,
        SnippetLibrary library,
        string name,
        string text = null,
        FontFamily editorFont = null)
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

        StackPanel panel = new StackPanel { Spacing = 6, MinWidth = 420 };
        panel.Children.Add(new TextBlock { Text = I18n.Get("&Title:") });
        panel.Children.Add(title);
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

        ContentDialog dialog = new ContentDialog
        {
            Title = name == null
                ? I18n.Get("Add Snippet")
                : I18n.Get("Edit Snippet"),
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = I18n.Get("OK"),
            CloseButtonText = I18n.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) { return null; }

        return library.Save(
            name,
            body.Text,
            string.IsNullOrWhiteSpace(title.Text) ? null : title.Text);
    }
}
