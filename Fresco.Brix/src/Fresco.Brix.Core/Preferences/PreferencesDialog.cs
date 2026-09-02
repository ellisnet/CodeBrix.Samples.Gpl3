// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Preferences dialog: a list of pages on the left and the one in front of
/// the user on the right.
/// </summary>
/// <remarks>
/// <para>
/// The page order is upstream's <c>pageorder()</c> with the dead pages taken
/// out: LilyPond paths and versions (FR5.1 — one engine is compiled in),
/// MIDI ports (FR6 — synthesis is in-process), extensions (FR5.3) and the
/// LilyPond-documentation page (FR5.1/FR8 — the manuals are bundled).
/// //was previously: Music View and Tools were missing from the list; they are
/// in upstream's own places in it now, so what remains IS upstream's order.
/// </para>
/// <para>
/// ⚠ Upstream's button box has FIVE buttons: OK, Cancel, Apply, Reset and Help.
/// A <c>ContentDialog</c> carries three (board trap 50), so Reset is a button
/// inside the page area — where it reads as "reset the settings", which is what
/// it does — and Help waits for W12B to give it a user guide to open. Every
/// page RECORDS its help identifier from the start
/// (<see cref="PreferencesPage.Help"/>); nothing resolves them yet, and that is
/// expected.
/// </para>
/// <para>
/// Which page was last looked at is remembered for the run, exactly as
/// upstream's module-level <c>_prefsindex</c> is: it is not written to the
/// settings, so a fresh launch starts on General.
/// </para>
/// </remarks>
public sealed class PreferencesDialog
{
    /// <summary>
    /// The page the dialog opens on, remembered while the application runs.
    /// </summary>
    /// <remarks>Upstream's <c>_prefsindex</c>, with its comment: "global
    /// setting for selected prefs page but not saved on exit".</remarks>
    private static int _lastPage;

    private readonly List<PreferencesPage> _pages = new List<PreferencesPage>();
    private readonly PreferencesContext _context;

    private ListView _list;
    private ContentControl _host;
    private ContentDialog _dialog;
    private Button _reset;

    /// <summary>Creates the dialog over what it configures.</summary>
    /// <param name="context">The settings store and the objects the pages
    /// configure.</param>
    public PreferencesDialog(PreferencesContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        //Upstream's pageorder(), minus the four dead pages.
        _pages.Add(new GeneralPage(context));
        _pages.Add(new MusicViewPage(context));
        _pages.Add(new MidiPage(context));
        _pages.Add(new EditorPage(context));
        _pages.Add(new ToolsPage(context));
        _pages.Add(new PathsPage(context));
        _pages.Add(new DocumentationPage(context));
        _pages.Add(new ShortcutsPage(context));
        _pages.Add(new FontsColorsPage(context));
        _pages.Add(new HelpersPage(context));

        foreach (var page in _pages)
        {
            page.Changed += (_, _) => MarkChanged();
        }
    }

    /// <summary>Gets the pages, in the order the list shows them.</summary>
    public IReadOnlyList<PreferencesPage> Pages => _pages;

    /// <summary>Raised after the settings were written and applied.</summary>
    /// <remarks>Upstream's <c>app.settingsChanged()</c>: the window re-reads
    /// what it needs rather than the dialog reaching into it.</remarks>
    public event EventHandler SettingsChanged;

    /// <summary>Puts the dialog in front of the user.</summary>
    /// <param name="xamlRoot">The root to attach it to.</param>
    /// <param name="help">The page to open on, by its help identifier, or null
    /// for the one last looked at.</param>
    /// <returns>Whether the settings were written.</returns>
    public async Task<bool> ShowAsync(XamlRoot xamlRoot, string help = null)
    {
        foreach (var page in _pages) { page.DialogRoot = xamlRoot; }

        _dialog = new ContentDialog
        {
            Title = I18n.Get("Preferences"),
            Content = BuildContent(),
            PrimaryButtonText = StandardButtons.Ok,
            SecondaryButtonText = StandardButtons.Apply,
            CloseButtonText = StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            IsSecondaryButtonEnabled = false,
            XamlRoot = xamlRoot,
        };

        //⚠ A ContentDialog's width is the ContentDialogMaxWidth RESOURCE, not
        //its MaxWidth (board trap 43); a page list beside a page of settings
        //needs far more than the theme's ~550.
        _dialog.Resources["ContentDialogMaxWidth"] = 1100.0;
        _dialog.Resources["ContentDialogMaxHeight"] = 900.0;

        //Apply writes and keeps the dialog open, which is what it is for.
        _dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            SaveSettings();
        };

        //Upstream gives the Help button the system help key; here the key is
        //read on the dialog, because the button is inside the content.
        _dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.F1)
            {
                ShowHelp();
                args.Handled = true;
            }
        };

        ShowPage(FindPage(help));

        bool accepted = await _dialog.ShowAsync() == ContentDialogResult.Primary;
        _lastPage = _list.SelectedIndex < 0 ? 0 : _list.SelectedIndex;
        if (accepted) { SaveSettings(); }

        _dialog = null;
        return accepted;
    }

    /// <summary>Writes every changed page and announces it.</summary>
    public void SaveSettings()
    {
        foreach (var page in _pages)
        {
            if (!page.IsBuilt || !page.HasChanges) { continue; }

            page.SaveSettings();
            page.HasChanges = false;
        }

        if (_dialog != null) { _dialog.IsSecondaryButtonEnabled = false; }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-reads every built page from the settings.</summary>
    /// <remarks>Upstream's Reset button.</remarks>
    public void LoadSettings()
    {
        foreach (var page in _pages)
        {
            if (!page.IsBuilt) { continue; }

            page.LoadSettings();
            page.HasChanges = false;
        }

        if (_dialog != null) { _dialog.IsSecondaryButtonEnabled = false; }
    }

    /// <summary>Opens the current page's own user-guide page.</summary>
    /// <remarks>Upstream's <c>showHelp</c>.</remarks>
    private void ShowHelp()
    {
        int index = _list?.SelectedIndex ?? -1;
        if (index >= 0 && index < _pages.Count)
        {
            _ = UserGuide.GuideHelp.ShowAsync(_pages[index].Help);
        }
    }

    private int FindPage(string help)
    {
        if (!string.IsNullOrEmpty(help))
        {
            for (int index = 0; index < _pages.Count; index++)
            {
                if (string.Equals(_pages[index].Help, help, StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return _lastPage >= 0 && _lastPage < _pages.Count ? _lastPage : 0;
    }

    private UIElement BuildContent()
    {
        _list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MinWidth = 170,
            ItemTemplate = PageTemplate(),
        };

        List<PageEntry> entries = new List<PageEntry>();
        foreach (var page in _pages) { entries.Add(new PageEntry(page.Title)); }

        _list.ItemsSource = entries;
        _list.SelectionChanged += (_, _) => ShowPage(_list.SelectedIndex);

        _host = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };

        ScrollViewer scroller = new ScrollViewer
        {
            Content = _host,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        //The size a page area is given. A ContentDialog spends its own height
        //on the title and the button row, so a taller split is simply CLIPPED
        //on a window this application's default size — every page scrolls, so
        //the honest answer is a page area that fits.
        Grid split = new Grid { ColumnSpacing = 12, Width = 880, Height = 400 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        split.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        split.Children.Add(_list);
        Grid.SetColumn(scroller, 1);
        split.Children.Add(scroller);

        _reset = new Button { Content = StandardButtons.Reset };
        ToolTipService.SetToolTip(
            _reset, I18n.Get("Discards the changes made in this window."));
        _reset.Click += (_, _) => LoadSettings();

        StackPanel actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        actions.Children.Add(_reset);

        //Upstream's button box carries a Help button that opens the CURRENT
        //page's `help' identifier (`showHelp': `userguide.show(
        //self.pagelist.currentItem().help)'). A ContentDialog's three buttons
        //are spent (board trap 50), so it sits here beside Reset — and the
        //system help key does the same thing, which is what upstream's
        //`setShortcut(QKeySequence.StandardKey.HelpContents)' is for.
        Button help = new Button { Content = I18n.Get("Help") };
        ToolTipService.SetToolTip(
            help, I18n.Get("Opens this page in the user guide (F1)."));
        help.Click += (_, _) => ShowHelp();
        actions.Children.Add(help);

        Grid root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(split);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        return root;
    }

    private static DataTemplate PageTemplate()
    {
        //A ListView row's Content renders as its TYPE NAME without a template.
        string xaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<TextBlock Text=\"{Binding Title}\" Margin=\"2,4,2,4\" />"
            + "</DataTemplate>";
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Count) { return; }

        if (_list.SelectedIndex != index) { _list.SelectedIndex = index; }

        _host.Content = _pages[index].Panel();
    }

    private void MarkChanged()
    {
        if (_dialog != null) { _dialog.IsSecondaryButtonEnabled = true; }
    }

    /// <summary>One entry of the page list.</summary>
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class PageEntry
    {
        /// <summary>Creates an entry.</summary>
        /// <param name="title">The page's title.</param>
        public PageEntry(string title) => Title = title;

        /// <summary>Gets the page's title.</summary>
        public string Title { get; }
    }
}
