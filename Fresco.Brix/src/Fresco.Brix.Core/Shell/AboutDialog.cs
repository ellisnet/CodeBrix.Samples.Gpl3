// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/about.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The About window: what the application is, who it credits, and what it is
/// built on.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's three tabs are kept — About, Credits, Version — as three panels
/// with a row of buttons over them, because the theme's tab controls paint
/// nothing on the Skia heads (board trap 40).
/// </para>
/// <para>
/// ⚠ RULING FR9: Frescobaldi is openly credited here as the application
/// Fresco.Brix is modelled on, and Fresco.Brix never presents AS Frescobaldi.
/// ⚠ RULING FR13: this is one of the FEW places allowed to name LilyPond —
/// About may state the engine's lineage, where the chrome may not.
/// </para>
/// <para>
/// //was previously: an HTML page in a <c>QLabel</c> and two
/// <c>QTextBrowser</c>s, one of which renders the user guide's <c>credits</c>
/// page. There is no web view anywhere in this application (FR8), and the user
/// guide arrives at W12B; the credits are written out here and the seam for the
/// guide's own page is marked below.
/// </para>
/// </remarks>
public static class AboutDialog
{
    /// <summary>The years the application has been worked on.</summary>
    public const string CopyrightYears = "2026";

    /// <summary>Puts the About window in front of the user.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="renderPage">How a user-guide page is drawn, or null to
    /// write the credits out here.</param>
    /// <returns>The running task.</returns>
    public static async Task ShowAsync(
        XamlRoot xamlRoot, Func<string, UIElement> renderPage = null)
    {
        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Format(
                I18n.Get("About {appname}"), ("appname", AppInfo.AppName)),
            Content = BuildContent(renderPage),
            PrimaryButtonText = StandardButtons.Ok,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        //⚠ The width is the resource, not MaxWidth (board trap 43).
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;
        dialog.Resources["ContentDialogMaxHeight"] = 620.0;

        await dialog.ShowAsync();
    }

    private static UIElement BuildContent(Func<string, UIElement> renderPage)
    {
        (string Title, Func<UIElement> Build)[] panels =
        {
            (I18n.Get("About"), BuildAbout),
            (I18n.Get("Credits"), () => BuildCredits(renderPage)),
            (I18n.Get("Version"), BuildVersion),
        };

        Grid root = new Grid { RowSpacing = 8, Width = 620, Height = 460 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        StackPanel tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        Grid pages = new Grid();
        List<Button> buttons = new List<Button>();

        for (int index = 0; index < panels.Length; index++)
        {
            UIElement page = panels[index].Build();
            page.Visibility = Visibility.Collapsed;
            pages.Children.Add(page);

            Button tab = new Button
            {
                Content = panels[index].Title,
                Padding = new Thickness(12, 4, 12, 4),
            };
            int wanted = index;
            tab.Click += (_, _) => Show(pages, buttons, wanted);
            buttons.Add(tab);
            tabs.Children.Add(tab);
        }

        root.Children.Add(tabs);
        Grid.SetRow(pages, 1);
        root.Children.Add(pages);

        Show(pages, buttons, 0);
        return root;
    }

    private static void Show(Grid pages, IReadOnlyList<Button> tabs, int index)
    {
        for (int page = 0; page < pages.Children.Count; page++)
        {
            pages.Children[page].Visibility = page == index
                ? Visibility.Visible
                : Visibility.Collapsed;
            tabs[page].FontWeight = page == index
                ? FontWeights.SemiBold
                : FontWeights.Normal;
        }
    }

    private static UIElement BuildAbout()
    {
        StackPanel panel = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        panel.Children.Add(Centered(AppInfo.AppName, 28, FontWeights.Bold));
        panel.Children.Add(Centered(
            I18n.Format(I18n.Get("Version {version}"), ("version", AppInfo.Version)),
            16,
            FontWeights.SemiBold));
        //was previously: _("A LilyPond Music Editor"). The engine the user
        //drives is LilyPort (FR13); the lineage is stated two lines down, which
        //is where About is allowed to say it.
        panel.Children.Add(Centered(I18n.Get("A LilyPort Music Editor"), 0, FontWeights.Normal));
        panel.Children.Add(Centered(
            I18n.Format(
                I18n.Get("Copyright (c) {year} by {author}"),
                ("year", CopyrightYears),
                ("author", AppInfo.Maintainer)),
            0,
            FontWeights.Normal));
        panel.Children.Add(Centered(
            I18n.Format(I18n.Get("Licensed under the {gpl}."), ("gpl", "GNU GPL v3")),
            0,
            FontWeights.Normal));

        panel.Children.Add(new Border { Height = 8 });

        //⚠ FR9's credit, and FR13's one permitted statement of the lineage.
        panel.Children.Add(Wrapped(I18n.Format(
            I18n.Get(
                "{appname} is modelled on {inspiration}, the LilyPond music "
                + "editor by Wilbert Berendsen and contributors, and is deeply "
                + "indebted to it. It is not {inspiration}, and neither the "
                + "{inspiration} project nor its authors endorse it."),
            ("appname", AppInfo.AppName),
            ("inspiration", AppInfo.InspiredBy))));

        panel.Children.Add(Wrapped(I18n.Format(
            I18n.Get(
                "Scores are engraved inside {appname} by CodeBrix.LilyPort, a "
                + "managed port of LilyPond {version}. No LilyPond installation "
                + "is used or required."),
            ("appname", AppInfo.AppName),
            ("version", Engrave.LilyPortEngine.CompatibleWithVersion))));

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private static UIElement BuildCredits(Func<string, UIElement> renderPage)
    {
        //Upstream's own arrangement: this panel IS the user guide's `credits'
        //page (`about.py': `userguide.page.Page('credits').body()' in a text
        //browser). Drawn here rather than parsed as HTML, ruling FR8.
        UIElement page = renderPage?.Invoke("credits");
        if (page != null)
        {
            return new ScrollViewer
            {
                Content = page,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
        }

        //...and what it says when the guide's page folder was emptied. The
        //assets are droppable, and the credit ruling FR9 requires is not.
        StackPanel panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Wrapped(I18n.Format(
            I18n.Get("{appname} is written and maintained by {author}."),
            ("appname", AppInfo.AppName),
            ("author", AppInfo.Maintainer))));

        panel.Children.Add(Heading(I18n.Get("Based on the work of")));
        foreach (var line in new[]
        {
            "Frescobaldi — Wilbert Berendsen and contributors (GPL-2.0-or-later)",
            "python-ly — Wilbert Berendsen and contributors (GPL-3.0-or-later)",
            "qpageview — Wilbert Berendsen and contributors (GPL-3.0-or-later)",
            "LilyPond — the LilyPond development team (GPL-3.0-or-later)",
        })
        {
            //Project names and their authors are DATA, not messages.
            panel.Children.Add(Wrapped(line));
        }

        panel.Children.Add(Heading(I18n.Get("Built with")));
        foreach (var line in new[]
        {
            "CodeBrix.Platform — the user interface foundation",
            "CodeBrix.LilyPort — the engraving engine",
            "CodeBrix.LilyScheme — the Scheme interpreter the engine runs on",
            "CodeBrix.Audio — SoundFont synthesis for MIDI playback",
            "CodeBrix.PdfDocuments — PDF writing and reading",
            //was previously: "CodeBrix.Sqlite — the settings store". The store
            //is the AppSettings add-in now; Sqlite is the database beneath it.
            "CodeBrix.Platform.AppSettings — the settings store",
            "CodeBrix.Sqlite — the database it keeps them in",
        })
        {
            panel.Children.Add(Wrapped(line));
        }

        panel.Children.Add(Wrapped(I18n.Get(
            "The full list of third-party works, with each one's licence, is in "
            + "THIRD-PARTY-NOTICES.txt beside the program.")));

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private static UIElement BuildVersion()
    {
        Grid grid = new Grid { ColumnSpacing = 12, RowSpacing = 2 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        int row = 0;
        foreach (var (name, version) in DebugInfo.VersionInfoNamed())
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock label = new TextBlock { Text = name + ":" };
            Grid.SetRow(label, row);
            grid.Children.Add(label);

            TextBlock value = new TextBlock
            {
                Text = version,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            row++;
        }

        return new ScrollViewer
        {
            Content = grid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private static TextBlock Centered(
        string text, double size, Windows.UI.Text.FontWeight weight)
    {
        TextBlock block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = weight,
        };
        if (size > 0) { block.FontSize = size; }

        return block;
    }

    private static TextBlock Wrapped(string text)
        => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Heading(string text)
        => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
        };
}
