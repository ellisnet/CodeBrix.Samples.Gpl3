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
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/inputdialog.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The small "ask the user one thing" dialogs: a line of text, or a yes/no
/// question.
/// </summary>
/// <remarks>
/// Upstream's <c>getColor</c> is not here: the only callers are the
/// fonts-and-colors preferences page (W12) and the object editor, and a colour
/// picker is a platform widget rather than a ported one. Its help-button and
/// completer arguments belong to the user guide (W12) and the persistent
/// line-edit completions (<c>completionmodel</c>), both of which arrive with
/// their own callers.
/// </remarks>
public static class InputDialogs
{
    /// <summary>Asks the user for a line of text.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="title">The dialog title, without the application name.</param>
    /// <param name="message">The question.</param>
    /// <param name="text">What the box starts out holding.</param>
    /// <param name="pattern">A regular expression the whole answer must match,
    /// or null for no validation.</param>
    /// <param name="validate">A predicate the answer must satisfy, used when
    /// there is no pattern.</param>
    /// <param name="completions">Words the box offers as the user types, or
    /// null for none.</param>
    /// <returns>The text, or null when the user cancelled.</returns>
    public static async Task<string> GetTextAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        string text = "",
        string pattern = null,
        Func<string, bool> validate = null,
        IReadOnlyList<string> completions = null)
    {
        Regex expression = pattern == null
            ? null
            : new Regex("^(?:" + pattern + ")$");

        TextBox box = new TextBox
        {
            Text = text ?? string.Empty,
            AcceptsReturn = false,
            MinWidth = 320,
        };

        StackPanel panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(new TextBlock
        {
            Text = message ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
        });

        if (completions is { Count: > 0 })
        {
            //An AutoSuggestBox is the platform's completer; its Text is what
            //the dialog reads, so the two paths end the same way.
            AutoSuggestBox suggest = new AutoSuggestBox
            {
                Text = text ?? string.Empty,
                MinWidth = 320,
            };
            suggest.TextChanged += (sender, args) =>
            {
                if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    return;
                }

                List<string> matches = new List<string>();
                foreach (var candidate in completions)
                {
                    if (candidate.StartsWith(
                            suggest.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(candidate);
                    }
                }

                sender.ItemsSource = matches;
            };
            suggest.SuggestionChosen += (_, args)
                => suggest.Text = args.SelectedItem as string ?? suggest.Text;
            panel.Children.Add(suggest);

            ContentDialog suggestDialog = MakeDialog(xamlRoot, title, panel);
            return await suggestDialog.ShowAsync() == ContentDialogResult.Primary
                && IsValid(suggest.Text, expression, validate)
                    ? suggest.Text
                    : null;
        }

        panel.Children.Add(box);
        ContentDialog dialog = MakeDialog(xamlRoot, title, panel);

        //The OK button follows the validity of what is typed, which is what
        //upstream's validator does to its button box.
        void Check() => dialog.IsPrimaryButtonEnabled
            = IsValid(box.Text, expression, validate);

        box.TextChanged += (_, _) => Check();
        Check();

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? box.Text
            : null;
    }

    /// <summary>Asks the user a yes/no question.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The question.</param>
    /// <param name="yesText">The affirmative button's text, or null for
    /// "OK".</param>
    /// <returns>Whether the user agreed.</returns>
    public static async Task<bool> ConfirmAsync(
        XamlRoot xamlRoot, string title, string message, string yesText = null)
    {
        ContentDialog dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = yesText ?? I18n.Get("OK"),
            CloseButtonText = I18n.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static ContentDialog MakeDialog(
        XamlRoot xamlRoot, string title, UIElement content)
        => new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = I18n.Get("OK"),
            CloseButtonText = I18n.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

    private static bool IsValid(
        string text, Regex expression, Func<string, bool> validate)
    {
        if (string.IsNullOrEmpty(text)) { return false; }

        if (expression != null) { return expression.IsMatch(text); }

        return validate == null || validate(text);
    }
}
