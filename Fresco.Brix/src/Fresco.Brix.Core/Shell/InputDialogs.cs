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
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/inputdialog.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The small "ask the user one thing" dialogs: a line of text, a yes/no
/// question, or a colour.
/// </summary>
/// <remarks>
/// <para>
/// <c>getColor</c> landed at W12A with all three of its callers — the
/// fonts-and-colors preferences page, the object editor and the
/// <c>color_dialog</c> editor command.
/// </para>
/// <para>
/// //was previously: a note saying <c>getText</c>'s help-button argument
/// "belongs to the user guide (W12B) and is not taken yet". W12B shipped the
/// guide; <c>helpPage</c> is that argument and its callers pass upstream's own
/// page names. The completer argument is the <c>completions</c> list.
/// </para>
/// </remarks>
public static class InputDialogs
{
    /// <summary>
    /// The colour the picker opens on when the caller names none — upstream's
    /// module-level <c>_savedColor</c>, which starts white and then remembers
    /// the last colour chosen.
    /// </summary>
    private static Color _savedColor = Color.FromArgb(255, 255, 255, 255);

    /// <summary>
    /// The swatches the picker offers, so a colour can be chosen without
    /// dragging anything.
    /// </summary>
    /// <remarks>Qt's own basic-colour grid is 48 entries in six columns; these
    /// are its first two rows of hues at full and half value, which is what a
    /// user reaches for when they want "a red" rather than a precise one.</remarks>
    private static readonly uint[] BasicColors =
    {
        0xFFFFFF, 0xC0C0C0, 0x808080, 0x404040, 0x000000, 0xFFFFC0,
        0xFF0000, 0xFF8000, 0xFFFF00, 0x80FF00, 0x00FF00, 0x00FF80,
        0x00FFFF, 0x0080FF, 0x0000FF, 0x8000FF, 0xFF00FF, 0xFF0080,
        0x800000, 0x804000, 0x808000, 0x408000, 0x008000, 0x008040,
        0x008080, 0x004080, 0x000080, 0x400080, 0x800080, 0x800040,
    };

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
    /// <param name="helpPage">The user guide page a Help button opens, or null
    /// for no Help button — upstream's <c>help</c> argument.</param>
    /// <returns>The text, or null when the user cancelled.</returns>
    public static async Task<string> GetTextAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        string text = "",
        string pattern = null,
        Func<string, bool> validate = null,
        IReadOnlyList<string> completions = null,
        string helpPage = null)
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
            if (helpPage != null)
            {
                panel.Children.Add(UserGuide.GuideHelp.ButtonRow(helpPage));
            }

            ContentDialog suggestDialog = MakeDialog(xamlRoot, title, panel);
            return await suggestDialog.ShowAsync() == ContentDialogResult.Primary
                && IsValid(suggest.Text, expression, validate)
                    ? suggest.Text
                    : null;
        }

        panel.Children.Add(box);

        //Upstream's `help' argument, which its `getText' turns into
        //`userguide.addButton(dlg.buttonBox(), help)'. A ContentDialog's three
        //buttons are spent (board trap 50), so the button goes in the content.
        if (helpPage != null)
        {
            panel.Children.Add(UserGuide.GuideHelp.ButtonRow(helpPage));
        }

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

    /// <summary>Asks the user for a whole number in a range.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The question.</param>
    /// <param name="value">What the box starts out holding.</param>
    /// <param name="minimum">The smallest answer.</param>
    /// <param name="maximum">The largest answer.</param>
    /// <returns>The number, or null when the user cancelled.</returns>
    /// <remarks>
    /// Upstream's <c>QInputDialog</c> in <c>IntInput</c> mode, which
    /// <c>MainWindow.gotoLine</c> is the only user of. Upstream shows it as a
    /// POPUP positioned at the caret; a <c>ContentDialog</c> is the platform's
    /// only modal, so this one is centred like every other dialog here — the
    /// question, the range and the answer are upstream's.
    /// </remarks>
    public static async Task<int?> GetIntegerAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        int value,
        int minimum,
        int maximum)
    {
        Fresco.Brix.Preferences.NumberEntry entry
            = new Fresco.Brix.Preferences.NumberEntry(minimum, maximum);
        entry.SetValueQuietly(value);

        StackPanel panel = new StackPanel { Spacing = 8, MinWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = message ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(entry);

        ContentDialog dialog = MakeDialog(xamlRoot, title, panel);
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? entry.Value
            : null;
    }

    /// <summary>Tells the user something and waits for OK.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">What to say.</param>
    /// <returns>The task.</returns>
    /// <remarks>Upstream's <c>QMessageBox.critical</c> /
    /// <c>QMessageBox.information</c>: ONE button, because there is nothing to
    /// decide. //was previously: these messages went through
    /// <see cref="ConfirmAsync"/>, which offers a Cancel the caller then
    /// discarded.</remarks>
    public static async Task AlertAsync(
        XamlRoot xamlRoot, string title, string message)
    {
        ContentDialog dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = StandardButtons.Ok,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        await dialog.ShowAsync();
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
            PrimaryButtonText = yesText ?? StandardButtons.Ok,
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Asks the user for a colour.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="title">The dialog title, or null for "Select Color".</param>
    /// <param name="color">The colour to open on, or null for the last one
    /// chosen.</param>
    /// <param name="alpha">Whether the opacity channel can be set.</param>
    /// <returns>The colour, or null when the user cancelled.</returns>
    /// <remarks>
    /// <para>
    /// Upstream hands the job to <c>QColorDialog</c>. There is no such control
    /// in the platform — and the one colour part it does carry,
    /// <c>ColorPickerSlider</c>, is a <c>Slider</c>, every part of which paints
    /// nothing on the Skia heads (board trap 53). So the picker is built from
    /// the pieces that do paint: the application's own drawn
    /// <see cref="TrackBar"/> for the channels, a grid of swatch buttons for
    /// the common choices, and a <c>#rrggbb</c> box for an exact value.
    /// </para>
    /// <para>
    /// Upstream's two behaviours are kept: the dialog opens on the LAST colour
    /// chosen when the caller names none, and cancelling changes nothing.
    /// </para>
    /// </remarks>
    public static async Task<Color?> GetColorAsync(
        XamlRoot xamlRoot,
        string title = null,
        Color? color = null,
        bool alpha = false)
    {
        Color current = color ?? _savedColor;
        bool writing = false;

        Border preview = new Border
        {
            Height = 40,
            MinWidth = 320,
            BorderThickness = new Thickness(1),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Color.FromArgb(255, 0x60, 0x60, 0x60)),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(current),
        };

        TextBox hex = new TextBox { Width = 110, Text = Editor.TextFormat.FormatColor(current) };

        TrackBar red = Channel(current.R);
        TrackBar green = Channel(current.G);
        TrackBar blue = Channel(current.B);
        TrackBar opacity = Channel(current.A);

        void Show()
        {
            preview.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(current);
            writing = true;
            red.SetValueQuietly(current.R);
            green.SetValueQuietly(current.G);
            blue.SetValueQuietly(current.B);
            opacity.SetValueQuietly(current.A);
            string text = Editor.TextFormat.FormatColor(current);
            if (!string.Equals(hex.Text, text, StringComparison.OrdinalIgnoreCase))
            {
                hex.Text = text;
            }

            writing = false;
        }

        void FromBars()
        {
            //⚠ An interlock that writes back into the controls it is driven by
            //re-enters itself (board trap 44); the flag is what stops it.
            if (writing) { return; }

            current = Color.FromArgb(
                alpha ? (byte)opacity.Value : (byte)255,
                (byte)red.Value,
                (byte)green.Value,
                (byte)blue.Value);
            Show();
        }

        red.Moved += (_, _) => FromBars();
        green.Moved += (_, _) => FromBars();
        blue.Moved += (_, _) => FromBars();
        opacity.Moved += (_, _) => FromBars();
        red.ValueChanged += (_, _) => FromBars();
        green.ValueChanged += (_, _) => FromBars();
        blue.ValueChanged += (_, _) => FromBars();
        opacity.ValueChanged += (_, _) => FromBars();

        hex.TextChanged += (_, _) =>
        {
            if (writing) { return; }

            Color? parsed = Editor.TextFormat.ParseColor(hex.Text.Trim());
            if (parsed == null) { return; }

            current = Color.FromArgb(
                alpha ? current.A : (byte)255, parsed.Value.R, parsed.Value.G, parsed.Value.B);
            Show();
        };

        StackPanel panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(preview);
        panel.Children.Add(Swatches(picked =>
        {
            current = Color.FromArgb(
                alpha ? current.A : (byte)255, picked.R, picked.G, picked.B);
            Show();
        }));

        Grid rows = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        void Row(int index, string label, TrackBar bar)
        {
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock caption = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(caption, index);
            rows.Children.Add(caption);
            Grid.SetRow(bar, index);
            Grid.SetColumn(bar, 1);
            rows.Children.Add(bar);
        }

        Row(0, I18n.Get("Red:"), red);
        Row(1, I18n.Get("Green:"), green);
        Row(2, I18n.Get("Blue:"), blue);
        if (alpha) { Row(3, I18n.Get("Opacity:"), opacity); }

        panel.Children.Add(rows);

        StackPanel hexRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        hexRow.Children.Add(new TextBlock
        {
            Text = I18n.Get("HTML:"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        hexRow.Children.Add(hex);
        panel.Children.Add(hexRow);

        ContentDialog dialog = MakeDialog(
            xamlRoot, title ?? I18n.Get("Select Color"), panel);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) { return null; }

        _savedColor = current;
        return current;
    }

    /// <summary>Builds one 0–255 channel bar.</summary>
    /// <param name="value">Its initial value.</param>
    /// <returns>The bar.</returns>
    private static TrackBar Channel(byte value)
        => new TrackBar
        {
            Minimum = 0,
            Maximum = 255,
            Value = value,
            IsTracking = true,
            MinWidth = 220,
        };

    /// <summary>Builds the grid of ready-made colours.</summary>
    /// <param name="pick">What choosing one does.</param>
    /// <returns>The grid.</returns>
    private static UIElement Swatches(Action<Color> pick)
    {
        Grid grid = new Grid { ColumnSpacing = 2, RowSpacing = 2 };
        for (int column = 0; column < 6; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (int index = 0; index < BasicColors.Length; index++)
        {
            int row = index / 6;
            if (index % 6 == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            uint rgb = BasicColors[index];
            Color color = Color.FromArgb(
                255, (byte)(rgb >> 16), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            Button swatch = new Button
            {
                Width = 26,
                Height = 18,
                Padding = new Thickness(0),
                Content = new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(color),
                    Width = 20,
                    Height = 12,
                },
            };
            swatch.Click += (_, _) => pick(color);
            Grid.SetRow(swatch, row);
            Grid.SetColumn(swatch, index % 6);
            grid.Children.Add(swatch);
        }

        return grid;
    }

    private static ContentDialog MakeDialog(
        XamlRoot xamlRoot, string title, UIElement content)
        => new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = StandardButtons.Ok,
            CloseButtonText = StandardButtons.Cancel,
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
