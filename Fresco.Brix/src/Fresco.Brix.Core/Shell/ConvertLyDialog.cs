// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.ConvertLy;
using Fresco.Brix.Services;
using Fresco.Brix.Tools;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/convert_ly.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What the user decided in the convert-ly dialog.</summary>
public sealed class ConvertLyOutcome
{
    /// <summary>Gets or sets the converted document.</summary>
    public string Text { get; set; }

    /// <summary>Gets or sets whether to write the messages into the document.</summary>
    public bool CopyMessages { get; set; }

    /// <summary>Gets or sets the messages the rules produced.</summary>
    public IReadOnlyList<string> Messages { get; set; }
}

/// <summary>
/// Updates an old document to the syntax this engine reads, showing what will
/// change before anything is touched.
/// </summary>
/// <remarks>
/// Upstream's dialog has the same shape: the two versions at the top, three
/// views of the result (the rules' messages, a side-by-side comparison and a
/// unified diff), a box that copies the messages into the document, and Run
/// Again / Save as file beside OK and Cancel.
/// <para>
/// Two things differ, both ruled. There is no engine CHOOSER — FR5.1 compiles
/// one engine in, so the To version starts at what this engine reads and the
/// user may still type another. And the two diff views are drawn as ordinary
/// controls rather than as HTML in a browser, because FR8 puts no WebView in
/// this application.
/// </para>
/// </remarks>
public static class ConvertLyDialog
{
    /// <summary>The settings key the message-copying box remembers itself in.</summary>
    public const string CopyMessagesKey = "convert_ly/copy_messages";

    private static readonly Color AddedColor = Color.FromArgb(255, 0, 120, 0);
    private static readonly Color RemovedColor = Color.FromArgb(255, 176, 0, 0);
    private static readonly Color HunkColor = Color.FromArgb(255, 0, 90, 160);

    /// <summary>Shows the dialog for a document.</summary>
    /// <param name="xamlRoot">The root to attach the dialog to.</param>
    /// <param name="text">The document as it stands.</param>
    /// <param name="settings">The store the checkbox remembers itself in.</param>
    /// <returns>What to do, or null when the user cancelled.</returns>
    public static async Task<ConvertLyOutcome> ShowAsync(
        XamlRoot xamlRoot, string text, SettingsStore settings)
    {
        string engineVersion = Fresco.Brix.Engrave.LilyPortEngine.CompatibleWithVersion;

        bool hasDeclared = DocumentConverter.TryReadDeclaredVersion(
            text, out ConversionVersion declared, out bool malformed);

        TextBox fromBox = new TextBox
        {
            Text = hasDeclared ? declared.ToString() : string.Empty,
            MinWidth = 120,
        };
        TextBox toBox = new TextBox { Text = engineVersion, MinWidth = 120 };

        TextBlock reason = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = hasDeclared
                ? I18n.Get("(this is the version of the document)")
                : malformed
                    ? I18n.Get("(the document's version could not be read)")
                    : I18n.Get("(the document does not declare a version)"),
        };

        //Trap 40: the theme's tab control paints nothing on the Skia heads, so the
        //three views are a row of buttons over one visible page at a time.
        StackPanel messagesPage = new StackPanel { Spacing = 2 };
        StackPanel changesPage = new StackPanel { Spacing = 0 };
        StackPanel diffPage = new StackPanel { Spacing = 0 };

        ScrollViewer messagesView = Scrolling(messagesPage);
        ScrollViewer changesView = Scrolling(changesPage);
        ScrollViewer diffView = Scrolling(diffPage);
        changesView.Visibility = Visibility.Collapsed;
        diffView.Visibility = Visibility.Collapsed;

        //Trap 18: the action texts keep upstream's & markers verbatim, because the
        //msgid a translation is keyed by includes them, and they are stripped at the
        //point of DISPLAY. A button caption is a point of display.
        Button messagesTab = new Button
            { Content = MenuBuilder.Display(I18n.Get("&Messages")) };
        Button changesTab = new Button
            { Content = MenuBuilder.Display(I18n.Get("&Changes")) };
        Button diffTab = new Button
            { Content = MenuBuilder.Display(I18n.Get("&Diff")) };

        void Show(ScrollViewer chosen)
        {
            messagesView.Visibility = chosen == messagesView
                ? Visibility.Visible : Visibility.Collapsed;
            changesView.Visibility = chosen == changesView
                ? Visibility.Visible : Visibility.Collapsed;
            diffView.Visibility = chosen == diffView
                ? Visibility.Visible : Visibility.Collapsed;
        }

        messagesTab.Click += (_, _) => Show(messagesView);
        changesTab.Click += (_, _) => Show(changesView);
        diffTab.Click += (_, _) => Show(diffView);

        CheckBox copyCheck = new CheckBox
        {
            Content = I18n.Get("Save convert-ly messages in document"),
            IsChecked = settings?.GetBool(CopyMessagesKey, true) ?? true,
        };

        Button runAgain = new Button { Content = I18n.Get("Run Again") };
        TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap };

        ConversionResult result = null;

        void Run()
        {
            messagesPage.Children.Clear();
            changesPage.Children.Clear();
            diffPage.Children.Clear();

            if (!ConversionVersion.TryParse(fromBox.Text, out ConversionVersion from))
            {
                summary.Text = I18n.Get(
                    "Please give the version to convert from, as in 2.14.2.");
                result = null;
                return;
            }

            ConversionVersion to = ConversionVersion.TryParse(
                toBox.Text, out ConversionVersion parsedTo)
                ? parsedTo
                : DocumentConverter.LatestVersion;

            result = DocumentConverter.Convert(text, from, to);

            foreach (string message in result.Messages)
            {
                messagesPage.Children.Add(new TextBlock
                {
                    Text = message.TrimEnd('\n'),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = MonospaceFont(),
                });
            }

            if (result.Messages.Count == 0)
            {
                messagesPage.Children.Add(new TextBlock
                {
                    Text = I18n.Get("(no messages)"),
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            IReadOnlyList<DiffRow> rows = TextDiff.Compare(text, result.Text);
            foreach (DiffRow row in rows)
            {
                changesPage.Children.Add(SideBySide(row));
            }

            foreach (DiffRow row in TextDiff.Unified(
                text, result.Text,
                I18n.Get("Current Document"), I18n.Get("Converted Document")))
            {
                diffPage.Children.Add(UnifiedLine(row));
            }

            int changed = TextDiff.ChangeCount(rows);
            summary.Text = result.Changed
                ? string.Format(
                    I18n.Get("{0} rules ran; {1} lines change; the document becomes version {2}."),
                    result.AppliedRules.Count, changed,
                    result.StampedVersion?.ToString() ?? to.ToString())
                : string.Format(
                    I18n.Get("{0} rules ran and the document is unchanged."),
                    result.AppliedRules.Count);

            if (result.Errors > 0)
            {
                summary.Text += " " + I18n.Get(
                    "A rule stopped early; everything before it has been applied.");
            }
        }

        runAgain.Click += (_, _) => Run();
        Run();

        Grid versions = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        versions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        versions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        versions.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        versions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        versions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock fromLabel = new TextBlock
            { Text = I18n.Get("From version:"), VerticalAlignment = VerticalAlignment.Center };
        TextBlock toLabel = new TextBlock
            { Text = I18n.Get("To version:"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(fromLabel, 0);
        Grid.SetColumn(fromLabel, 0);
        Grid.SetRow(fromBox, 0);
        Grid.SetColumn(fromBox, 1);
        Grid.SetRow(reason, 0);
        Grid.SetColumn(reason, 2);
        Grid.SetRow(toLabel, 1);
        Grid.SetColumn(toLabel, 0);
        Grid.SetRow(toBox, 1);
        Grid.SetColumn(toBox, 1);
        versions.Children.Add(fromLabel);
        versions.Children.Add(fromBox);
        versions.Children.Add(reason);
        versions.Children.Add(toLabel);
        versions.Children.Add(toBox);

        StackPanel tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        tabs.Children.Add(messagesTab);
        tabs.Children.Add(changesTab);
        tabs.Children.Add(diffTab);
        tabs.Children.Add(new TextBlock { Width = 24 });
        tabs.Children.Add(runAgain);

        //Trap 43's neighbour: overriding ContentDialogMaxWidth lets the dialog grow,
        //but nothing inside it ASKS for the room, so it comes up at its minimum. The
        //diff is the reason the dialog is wide, so the pages carry the demand.
        Grid pages = new Grid { Height = 420, MinWidth = 1000 };
        pages.Children.Add(messagesView);
        pages.Children.Add(changesView);
        pages.Children.Add(diffView);

        StackPanel content = new StackPanel { Spacing = 10, MinWidth = 1000 };
        content.Children.Add(versions);
        content.Children.Add(tabs);
        content.Children.Add(pages);
        content.Children.Add(summary);
        content.Children.Add(copyCheck);

        ContentDialog dialog = new ContentDialog
        {
            Title = I18n.Get("Update with convert-ly"),
            Content = content,
            PrimaryButtonText = I18n.Get("OK"),
            CloseButtonText = I18n.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        //Trap 43: the width comes from the RESOURCE, not from MaxWidth.
        dialog.Resources["ContentDialogMaxWidth"] = 1200.0;
        dialog.Resources["ContentDialogMaxHeight"] = 900.0;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || result == null)
        {
            return null;
        }

        bool copy = copyCheck.IsChecked == true;
        settings?.SetBool(CopyMessagesKey, copy);

        return new ConvertLyOutcome
        {
            Text = result.Text,
            CopyMessages = copy,
            Messages = result.Messages,
        };
    }

    /// <summary>Wraps a page in a scroller sized by its host.</summary>
    /// <param name="content">The page.</param>
    /// <returns>The scroller.</returns>
    private static ScrollViewer Scrolling(UIElement content)
        => new ScrollViewer
        {
            Content = content,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

    /// <summary>One row of the side-by-side comparison.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The control.</returns>
    private static UIElement SideBySide(DiffRow row)
    {
        Grid grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });

        TextBlock left = Line(
            row.Left, row.Kind == DiffKind.Removed ? RemovedColor : (Color?)null);
        TextBlock right = Line(
            row.Right, row.Kind == DiffKind.Added ? AddedColor : (Color?)null);
        TextBlock leftNumber = Number(row.LeftNumber);
        TextBlock rightNumber = Number(row.RightNumber);

        Grid.SetColumn(leftNumber, 0);
        Grid.SetColumn(left, 1);
        Grid.SetColumn(rightNumber, 2);
        Grid.SetColumn(right, 3);
        grid.Children.Add(leftNumber);
        grid.Children.Add(left);
        grid.Children.Add(rightNumber);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>One line of the unified diff.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The control.</returns>
    private static UIElement UnifiedLine(DiffRow row)
    {
        string text;
        Color? colour;
        if (row.Kind == DiffKind.Added)
        {
            text = (row.Right.StartsWith("+++", StringComparison.Ordinal) ? string.Empty : "+")
                + row.Right;
            colour = AddedColor;
        }
        else if (row.Kind == DiffKind.Removed)
        {
            text = (row.Left.StartsWith("---", StringComparison.Ordinal) ? string.Empty : "-")
                + row.Left;
            colour = RemovedColor;
        }
        else if (row.Left.StartsWith("@@", StringComparison.Ordinal))
        {
            text = row.Left;
            colour = HunkColor;
        }
        else
        {
            text = " " + row.Left;
            colour = null;
        }

        return Line(text, colour);
    }

    /// <summary>A line of monospaced text, optionally coloured.</summary>
    /// <param name="text">The text.</param>
    /// <param name="colour">The colour, or null for the theme's own.</param>
    /// <returns>The control.</returns>
    private static TextBlock Line(string text, Color? colour)
    {
        TextBlock block = new TextBlock
        {
            Text = text ?? string.Empty,
            FontFamily = MonospaceFont(),
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
        };

        if (colour != null)
        {
            block.Foreground = new SolidColorBrush(colour.Value);
            block.FontWeight = FontWeights.SemiBold;
        }

        return block;
    }

    /// <summary>A line number, or nothing for a line that is not there.</summary>
    /// <param name="number">The number, or 0.</param>
    /// <returns>The control.</returns>
    private static TextBlock Number(int number)
        => new TextBlock
        {
            Text = number == 0 ? string.Empty : number.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            FontFamily = MonospaceFont(),
            Opacity = 0.55,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
        };

    private static FontFamily MonospaceFont()
        => Application.Current?.Resources
            .TryGetValue("RobotoMonoFont", out object font) == true
            ? font as FontFamily
            : null;
}
