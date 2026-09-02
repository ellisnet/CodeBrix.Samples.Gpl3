// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Import;
using Fresco.Brix.Services;
using Fresco.Brix.Widgets;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/file_import/toly_dialog.py + musicxml.py + midi.py + abc.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The options dialog one import puts in front of the user: the converter's own
/// switches on one tab, and what to do with the result on the other.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>ToLyDialog</c>, which is one dialog with a per-format widget
/// on its first tab — the three subclasses differ only in that widget, in the
/// caption of the OK button and in which user-guide page the Help button opens.
/// The same is true here, so there is one class rather than three.
/// </para>
/// <para>
/// TWO THINGS UPSTREAM HAS ARE GONE, both under rulings. The version chooser
/// and its "LilyPond version:" label are dropped by FR5.1 — there is one
/// engine, compiled in, and nothing to choose between. And there is no command
/// line, because ruling FD1 replaces the subprocess with
/// <c>CodeBrix.LilyPort.Importers</c> in this process; the user guide's claim
/// that this dialog carries an editable command-line box has been out of date
/// upstream for years and is corrected in this port's pages.
/// </para>
/// <para>
/// Board trap 40: the theme's tab control paints nothing on the Skia heads, so
/// the two tabs are a row of buttons over one visible page at a time — the same
/// arrangement the convert-ly and Document Fonts dialogs use. Board trap 50: a
/// <c>ContentDialog</c> carries three buttons, and OK and Cancel have spent
/// two, so Help goes inside the content, which is what
/// <see cref="WidgetDialog.HelpPage"/> already does.
/// </para>
/// </remarks>
public static class ImportDialog
{
    /// <summary>Shows the options dialog for one file.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="format">Which converter is being configured.</param>
    /// <param name="settings">The store the boxes remember themselves in.</param>
    /// <returns>What the user chose, or null when they cancelled.</returns>
    public static async Task<ImportSettings> ShowAsync(
        XamlRoot xamlRoot, ImportFormat format, SettingsStore settings)
    {
        ImportSettings chosen = ImportSettings.Load(format, settings);

        //--- the converter's own tab.
        StackPanel importPage = new StackPanel { Spacing = 6 };
        IReadOnlyList<string> texts = chosen.CheckTexts();
        List<CheckBox> importBoxes = new List<CheckBox>();
        for (int index = 0; index < texts.Count; index++)
        {
            CheckBox box = new CheckBox
            {
                Content = texts[index],
                IsChecked = chosen.GetCheck(index),
            };
            importBoxes.Add(box);
            importPage.Children.Add(box);
        }

        ComboBox languages = null;
        if (chosen is MusicXmlImportSettings musicXml)
        {
            //Upstream's `impExtra': a label and the language list, under the
            //boxes. The first entry is the converter's own default, which is
            //why every index here is one more than the list's.
            importPage.Children.Add(new TextBlock
            {
                Text = I18n.Get("Language for pitch names"),
                Margin = new Thickness(0, 8, 0, 0),
            });

            languages = new ComboBox { MinWidth = 200 };
            languages.Items.Add(I18n.Get("Default"));
            foreach (string language in MusicXmlImportSettings.Languages)
            {
                languages.Items.Add(language);
            }

            languages.SelectedIndex = musicXml.LanguageIndex;
            importPage.Children.Add(languages);
        }

        //--- the "After Import" tab.
        StackPanel postPage = new StackPanel { Spacing = 6 };
        IReadOnlyList<string> postTexts = PostImportSettings.Texts();
        List<CheckBox> postBoxes = new List<CheckBox>();
        for (int index = 0; index < postTexts.Count; index++)
        {
            CheckBox box = new CheckBox
            {
                Content = postTexts[index],
                IsChecked = chosen.Post[index],
            };
            postBoxes.Add(box);
            postPage.Children.Add(box);
        }

        //--- the tab row (trap 40).
        Grid pages = new Grid { MinHeight = 180, MinWidth = 380 };
        pages.Children.Add(importPage);
        pages.Children.Add(postPage);
        postPage.Visibility = Visibility.Collapsed;

        Button importTab = new Button
        {
            Content = ImportFormats.ConverterName(format),
            Padding = new Thickness(10, 4, 10, 4),
        };
        Button postTab = new Button
        {
            Content = MenuBuilder.Display(I18n.Get("After Import")),
            Padding = new Thickness(10, 4, 10, 4),
        };

        void Show(bool first)
        {
            importPage.Visibility = first ? Visibility.Visible : Visibility.Collapsed;
            postPage.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
            importTab.FontWeight = first ? FontWeights.SemiBold : FontWeights.Normal;
            postTab.FontWeight = first ? FontWeights.Normal : FontWeights.SemiBold;
        }

        importTab.Click += (_, _) => Show(true);
        postTab.Click += (_, _) => Show(false);
        Show(true);

        StackPanel tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        tabs.Children.Add(importTab);
        tabs.Children.Add(postTab);

        StackPanel content = new StackPanel { Spacing = 8, MinWidth = 380 };
        content.Children.Add(tabs);
        content.Children.Add(pages);

        WidgetDialog dialog = new WidgetDialog(TitleFor(format))
        {
            AcceptText = AcceptTextFor(format),
            RejectText = StandardButtons.Cancel,
            HelpPage = ImportFormats.HelpPage(format),
        };
        dialog.SetMainElement(content);

        if (!await dialog.ShowAsync(xamlRoot)) { return null; }

        for (int index = 0; index < importBoxes.Count; index++)
        {
            chosen.SetCheck(index, importBoxes[index].IsChecked == true);
        }

        for (int index = 0; index < postBoxes.Count; index++)
        {
            chosen.Post.Set(index, postBoxes[index].IsChecked == true);
        }

        if (chosen is MusicXmlImportSettings settingsWithLanguage && languages != null)
        {
            settingsWithLanguage.LanguageIndex = languages.SelectedIndex;
        }

        return chosen;
    }

    /// <summary>The caption of the button that starts the conversion.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The caption.</returns>
    /// <remarks>Three msgids with the converter's name written into each, which
    /// is how upstream spells them; a message built from a placeholder would be
    /// a msgid no catalog has (standing rule 7).</remarks>
    public static string AcceptTextFor(ImportFormat format)
        => format switch
        {
            ImportFormat.MusicXml => I18n.Get("Run musicxml2ly"),
            ImportFormat.Midi => I18n.Get("Run midi2ly"),
            _ => I18n.Get("Run abc2ly"),
        };

    /// <summary>The dialog's title for a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The title.</returns>
    /// <remarks>
    /// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Only the MusicXML
    /// dialog sets a window title upstream; <c>midi.py</c> and <c>abc.py</c>
    /// never call <c>setWindowTitle</c>, so their dialogs come up carrying
    /// whatever the window manager makes of a title-less <c>QDialog</c>. Two of
    /// three sibling dialogs having no title is an oversight rather than a
    /// design, and a titleless <c>ContentDialog</c> here would draw a blank
    /// band, so the two are given titles built the way the third's is. The
    /// fixture records upstream's own answers and
    /// <c>FileImportParityTests.the_declared_title_differences_are_these_and_no_others</c>
    /// declares exactly these two; the wave's STATUS file writes it up as a
    /// Frescobaldi bug report.
    /// </remarks>
    public static string TitleFor(ImportFormat format)
        => format switch
        {
            //Upstream's own msgid, from `musicxml.py''s translateUI.
            ImportFormat.MusicXml => I18n.Get("Import Music XML"),

            //New msgids, in upstream's own shape (see the remarks above).
            ImportFormat.Midi => I18n.Get("Import Midi"),
            _ => I18n.Get("Import abc"),
        };
}
