// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/hyphendialog.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One hyphenation dictionary as the dialog lists it.</summary>
public sealed class HyphenLanguage
{
    /// <summary>Creates the entry.</summary>
    /// <param name="name">The language's name in the user's own language.</param>
    /// <param name="code">The language code, such as <c>nl_NL</c>.</param>
    /// <param name="fileName">The dictionary file.</param>
    public HyphenLanguage(string name, string code, string fileName)
    {
        Name = name;
        Code = code;
        FileName = fileName;
    }

    /// <summary>Gets the language's name.</summary>
    public string Name { get; }

    /// <summary>Gets the language code.</summary>
    public string Code { get; }

    /// <summary>Gets the dictionary file.</summary>
    public string FileName { get; }

    /// <summary>Gets the row as the list shows it.</summary>
    public string Label => $"{Name}  ({Code})";

    /// <inheritdoc/>
    public override string ToString() => Label;
}

/// <summary>
/// Asks the user which language to hyphenate in, and hands back a hyphenator
/// for it.
/// </summary>
/// <remarks>
/// The list is whatever <see cref="HyphenDictionaries"/> found, sorted by the
/// language's name in the user's own language. The row that starts out chosen
/// is the one used last, or failing that the application's own language —
/// upstream's three-step preference, in upstream's order.
/// </remarks>
public static class HyphenDialog
{
    /// <summary>Gets the languages there are dictionaries for.</summary>
    /// <param name="settings">The store the search paths live in.</param>
    /// <returns>The languages, sorted by name.</returns>
    public static IReadOnlyList<HyphenLanguage> Languages(SettingsStore settings)
        => HyphenDictionaries.FindDictionaries(settings)
            .Select(pair => new HyphenLanguage(
                LanguageName(pair.Key), pair.Key, pair.Value))
            .OrderBy(l => l.Name, StringComparer.CurrentCulture)
            .ThenBy(l => l.Code, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Answers which row should start out chosen.</summary>
    /// <param name="languages">The languages on offer.</param>
    /// <param name="settings">The store the last-used language lives in.</param>
    /// <returns>The index, or 0 when nothing matches.</returns>
    /// <remarks>Upstream tries the last used language, then the application's
    /// own, then that one's base — <c>pt_BR</c> settling for <c>pt</c>.</remarks>
    public static int PreselectedIndex(
        IReadOnlyList<HyphenLanguage> languages, SettingsStore settings)
    {
        if (languages == null || languages.Count == 0) { return -1; }

        foreach (var wanted in Preferences(settings))
        {
            for (int index = 0; index < languages.Count; index++)
            {
                if (string.Equals(
                    languages[index].Code, wanted, StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return 0;
    }

    /// <summary>Shows the dialog and answers the hyphenator chosen.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="settings">The store the last-used language lives in.</param>
    /// <returns>The hyphenator, or null when the user cancelled or there is
    /// nothing installed.</returns>
    public static async Task<Hyphenator> ChooseAsync(
        XamlRoot xamlRoot, SettingsStore settings)
    {
        IReadOnlyList<HyphenLanguage> languages = Languages(settings);

        ListView list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = languages.Select(l => l.Label).ToList(),
            Height = 260,
            MinWidth = 320,
        };
        list.SelectedIndex = PreselectedIndex(languages, settings);

        StackPanel panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(new TextBlock
        {
            Text = languages.Count == 0
                ? I18n.Get("No hyphenation dictionaries were found.")
                : I18n.Get("Please select a language:"),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(list);

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("dialog title", "Hyphenate Lyrics Text"),
            Content = panel,
            PrimaryButtonText = I18n.Get("OK"),
            CloseButtonText = I18n.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = languages.Count > 0,
            XamlRoot = xamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || languages.Count == 0)
        {
            return null;
        }

        int chosen = list.SelectedIndex;
        if (chosen < 0 || chosen >= languages.Count) { return null; }

        HyphenLanguage language = languages[chosen];
        settings?.SetString(HyphenDictionaries.LastUsedKey, language.Code);
        return new Hyphenator(language.FileName);
    }

    /// <summary>The language codes to try, in order.</summary>
    /// <param name="settings">The store.</param>
    /// <returns>The codes.</returns>
    private static IEnumerable<string> Preferences(SettingsStore settings)
    {
        string lastUsed = settings?.GetString(HyphenDictionaries.LastUsedKey);
        if (!string.IsNullOrEmpty(lastUsed)) { yield return lastUsed; }

        string current = CultureInfo.CurrentUICulture.Name.Replace('-', '_');
        if (!string.IsNullOrEmpty(current))
        {
            yield return current;
            yield return current.Split('_')[0];
        }
    }

    /// <summary>Names a language in the user's own language.</summary>
    /// <param name="code">The code, such as <c>nl_NL</c>.</param>
    /// <returns>The name, or the code when it names no language.</returns>
    /// <remarks>Upstream reads this out of its own <c>language_names</c>
    /// tables, which are 3,573 lines of generated CLDR data and arrive with
    /// W-I18N. The same data is already on this machine, inside the ICU the
    /// platform carries, so it is asked for here — and when W-I18N brings the
    /// ported tables, this is the one place that changes.</remarks>
    private static string LanguageName(string code)
    {
        if (string.IsNullOrEmpty(code)) { return string.Empty; }

        try
        {
            return CultureInfo.GetCultureInfo(code.Replace('_', '-')).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }
}
