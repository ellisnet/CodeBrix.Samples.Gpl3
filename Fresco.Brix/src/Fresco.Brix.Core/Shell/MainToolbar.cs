// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.MusicView;
using Fresco.Brix.Preferences;
using Fresco.Brix.Services;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.System;
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/mainwindow.py (createToolBars)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The window's toolbars: the Main Toolbar and the Music View Toolbar, side by
/// side on one row under the menu bar.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: nothing. The window had no toolbar at all, which is why
/// <c>engrave_runner</c> — an action that exists only for one — was unreachable
/// (audit A GAP-24). Ruling FR16 (Jeremy, 2026-09-02) brings both of upstream's
/// bars into v1.
/// </para>
/// <para>
/// Upstream calls <c>addToolBar</c> twice, so Qt lays the two bars out side by
/// side in the same top area. A CodeBrix.Platform window has no toolbar area,
/// so the two runs of controls are drawn on one row here, with a wider gap
/// between them than the gap a separator makes inside a bar.
/// </para>
/// <para>
/// ⚠ The buttons are DRAWN, not <c>CommandBar</c>/<c>AppBarButton</c>: this
/// platform build ships neither (its FluentTheme descriptors stub
/// <c>Is_Microsoft_UI_Xaml_Controls_Primitives_CommandBarTemplateSettings_Available</c>
/// to false), and board traps 20/40/53 are the standing account of what happens
/// when a themed control has no template on the Skia heads. The pattern is the
/// one the dock panels' own toolbars already use (trap 57), including the
/// hidden-scrollbar <see cref="ScrollViewer"/> that keeps the last button
/// reachable in a narrow window.
/// </para>
/// </remarks>
public sealed class MainToolbar : Grid
{
    private const string ArrowGlyph = "▾";

    /// <summary>How much of a disabled button's icon is drawn.</summary>
    private const double DisabledOpacity = 0.4;

    private readonly MainActions _main;
    private readonly BrowserActions _browser;
    private readonly ScoreWizardActions _scoreWizard;
    private readonly EngraveActions _engrave;
    private readonly MusicViewActions _music;
    private readonly SnippetLibrary _snippets;
    private readonly SnippetToolActions _snippetActions;
    private readonly Action<string> _applySnippet;
    private readonly RecentFiles _recentFiles;
    private readonly Action<string> _openRecent;
    private readonly SettingsStore _settings;

    private readonly StackPanel _bar = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 2,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private ComboBox _scoreChooser;
    private ComboBox _zoomChooser;
    private TextBox _pager;
    private MusicViewPanel _musicView;
    private IReadOnlyList<ZoomEntry> _zoomEntries = Array.Empty<ZoomEntry>();
    private bool _writingChooser;
    private bool _writingZoom;
    private bool _writingPager;
    private bool _built;

    /// <summary>Creates the toolbars.</summary>
    /// <param name="main">The window's own commands.</param>
    /// <param name="browser">The back/forward commands.</param>
    /// <param name="scoreWizard">The Score Wizard's commands.</param>
    /// <param name="engrave">The engraving commands.</param>
    /// <param name="music">The Music View's commands.</param>
    /// <param name="snippets">The snippet library, for the template menu.</param>
    /// <param name="snippetActions">The Snippets panel's commands.</param>
    /// <param name="applySnippet">What picking a template does.</param>
    /// <param name="recentFiles">The recently opened files.</param>
    /// <param name="openRecent">What picking a recent file does.</param>
    /// <param name="settings">The store the pull-down preference lives in.</param>
    public MainToolbar(
        MainActions main,
        BrowserActions browser,
        ScoreWizardActions scoreWizard,
        EngraveActions engrave,
        MusicViewActions music,
        SnippetLibrary snippets,
        SnippetToolActions snippetActions,
        Action<string> applySnippet,
        RecentFiles recentFiles,
        Action<string> openRecent,
        SettingsStore settings)
    {
        _main = main;
        _browser = browser;
        _scoreWizard = scoreWizard;
        _engrave = engrave;
        _music = music;
        _snippets = snippets;
        _snippetActions = snippetActions;
        _applySnippet = applySnippet;
        _recentFiles = recentFiles;
        _openRecent = openRecent;
        _settings = settings;

        //The same translucent wash the dock panels' toolbars carry, so the row
        //reads as chrome under the menu bar rather than as part of the page.
        Background = new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0));
        Padding = new Thickness(4, 2, 4, 2);

        Children.Add(new ScrollViewer
        {
            Content = _bar,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        //The icons follow the platform's theme, the way upstream's follow Qt's
        //palette. The subscription is made on Loaded, because ActualTheme is
        //not resolved before the control is in a tree.
        Loaded += (_, _) =>
        {
            if (_built) { return; }

            //Upstream re-picks its icon theme on Qt's ApplicationPaletteChange
            //(icons/change_theme_eventhandler.py); this is the same moment.
            IconTheme.Follow(this, _ => Rebuild());
            Rebuild();
        };
    }

    /// <summary>
    /// Gets or sets the Music View panel the second bar drives, or null before
    /// there is one.
    /// </summary>
    public MusicViewPanel MusicView
    {
        get => _musicView;
        set
        {
            if (_musicView == value) { return; }

            if (_musicView != null)
            {
                _musicView.ScoresChanged -= OnScoresChanged;
                _musicView.ViewStateChanged -= OnViewStateChanged;
            }

            _musicView = value;
            if (_musicView != null)
            {
                _musicView.ScoresChanged += OnScoresChanged;
                _musicView.ViewStateChanged += OnViewStateChanged;
            }

            OnScoresChanged(this, EventArgs.Empty);
            OnViewStateChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>Rebuilds both bars from the current preference and theme.</summary>
    /// <remarks>
    /// Upstream's <c>mainwindow.settingsChanged</c> hangs or unhangs the three
    /// pull-down menus the moment <c>verbose_toolbuttons</c> changes; the
    /// window calls this from the same place, and a theme change comes through
    /// here too because the icons are rendered, not swapped.
    /// </remarks>
    public void SettingsChanged() => Rebuild();

    /// <summary>Answers whether the Shift key is held down.</summary>
    /// <returns>Whether it is.</returns>
    /// <remarks>
    /// Board trap 38: a modifier is read from the keyboard source rather than
    /// from an event's arguments, because ALT reads as SHIFT in the editor's
    /// key arguments on the Skia heads. Upstream reads
    /// <c>QApplication.keyboardModifiers()</c> at the same moment, inside
    /// <c>engrave.engraveRunner</c>.
    /// </remarks>
    public static bool ShiftHeld()
    {
        try
        {
            return (Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(VirtualKey.Shift)
                & Windows.UI.Core.CoreVirtualKeyStates.Down)
                == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }
        catch (Exception)
        {
            //A head with no keyboard source to ask cannot know, and "not held"
            //is the answer that runs a preview rather than opening a dialog.
            return false;
        }
    }

    /// <summary>Answers whether the pull-down menus are wanted.</summary>
    /// <param name="settings">The store, or null for the default.</param>
    /// <returns>Whether they are.</returns>
    /// <remarks>Upstream's
    /// <c>QSettings().value("verbose_toolbuttons", False, bool)</c>.</remarks>
    public static bool VerboseToolButtons(SettingsStore settings)
        => settings?.GetBool(GeneralValues.VerboseToolButtonsKey, false) ?? false;

    private void Rebuild()
    {
        _built = true;

        while (_bar.Children.Count > 0)
        {
            _bar.Children.RemoveAt(_bar.Children.Count - 1);
        }

        _scoreChooser = null;
        _zoomChooser = null;
        _pager = null;

        bool verbose = VerboseToolButtons(_settings);
        foreach (ToolbarEntry entry in ToolbarLayout.Main(
            _main, _browser, _scoreWizard, _engrave, verbose))
        {
            Add(entry, ToolbarLayout.MainTitle());
        }

        IReadOnlyList<ToolbarEntry> musicEntries = ToolbarLayout.Music(_music);
        if (musicEntries.Count > 0)
        {
            //The gap BETWEEN the two bars, which is what Qt draws when two
            //toolbars share a row.
            _bar.Children.Add(new Border
            {
                Width = 1,
                Margin = new Thickness(10, 3, 10, 3),
                Background = new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80)),
            });
        }

        foreach (ToolbarEntry entry in musicEntries)
        {
            Add(entry, ToolbarLayout.MusicTitle());
        }

        OnScoresChanged(this, EventArgs.Empty);
        OnViewStateChanged(this, EventArgs.Empty);
    }

    private void Add(ToolbarEntry entry, string barTitle)
    {
        switch (entry.Kind)
        {
            case ToolbarEntryKind.Separator:
                _bar.Children.Add(new Border
                {
                    Width = 1,
                    Margin = new Thickness(4, 4, 4, 4),
                    Background = new SolidColorBrush(
                        Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
                });
                return;

            case ToolbarEntryKind.Widget:
                UIElement control = ControlFor(entry, barTitle);
                if (control != null) { _bar.Children.Add(control); }

                return;

            default:
                if (entry.Action == null) { return; }

                _bar.Children.Add(ButtonFor(entry, barTitle));
                return;
        }
    }

    private UIElement ButtonFor(ToolbarEntry entry, string barTitle)
    {
        AppAction action = entry.Action;
        ButtonBase button = action.IsCheckable
            ? new ToggleButton { IsChecked = action.IsChecked }
            : new Button();

        button.Padding = new Thickness(5, 3, 5, 3);
        button.MinWidth = 0;
        button.IsEnabled = action.IsEnabled;
        button.Content = ContentFor(action);
        Describe(button, action, barTitle);

        if (button is ToggleButton toggle)
        {
            toggle.Click += (_, _) =>
            {
                //A ToggleButton has already flipped itself by the time it is
                //clicked; AppAction.Trigger flips the action. Setting the
                //action's state from the button first keeps the two from
                //cancelling each other out.
                action.IsChecked = toggle.IsChecked != true;
                action.Trigger();
            };
        }
        else
        {
            button.Click += (_, _) => action.Trigger();
        }

        void Update()
        {
            button.IsEnabled = action.IsEnabled;
            button.Content = ContentFor(action);
            Describe(button, action, barTitle);
            if (button is ToggleButton box) { box.IsChecked = action.IsChecked; }
        }

        Update();

        //Board trap 41's neighbour: a toolbar button is never removed from the
        //tree while the bar lives, so one subscription is enough — but it is
        //dropped when the bar is rebuilt, which is why Rebuild makes new ones.
        action.PropertyChanged += (_, _) => Update();

        if (entry.Menu == ToolbarMenu.None) { return button; }

        //Qt's MenuButtonPopup: the button IS the action, and a small arrow
        //beside it opens the menu.
        Button arrow = new Button
        {
            Content = new TextBlock { Text = ArrowGlyph, FontSize = 10 },
            Padding = new Thickness(2, 3, 2, 3),
            MinWidth = 0,
        };
        AutomationProperties.SetName(
            arrow, MenuBuilder.Display(action.Text) + " " + ArrowGlyph);

        MenuFlyout flyout = new MenuFlyout();
        flyout.Opening += (_, _) => FillMenu(flyout, entry.Menu);
        arrow.Flyout = flyout;

        //Qt disables a tool button whole — arrow, menu and all — when its
        //action is disabled, so the arrow follows the same state the button
        //does.
        arrow.IsEnabled = action.IsEnabled;
        action.PropertyChanged += (_, _) => arrow.IsEnabled = action.IsEnabled;

        StackPanel pair = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
        };
        pair.Children.Add(button);
        pair.Children.Add(arrow);
        return pair;
    }

    private void FillMenu(MenuFlyout flyout, ToolbarMenu menu)
    {
        switch (menu)
        {
            case ToolbarMenu.RecentFiles:
                MenuBuilder.FillRecent(flyout.Items, _recentFiles, _openRecent);
                break;

            case ToolbarMenu.EngraveModes:
                //Upstream hangs exactly these two off the runner's button
                //(mainwindow.createToolBars): publish and custom. Preview is
                //what the button itself does.
                while (flyout.Items.Count > 0)
                {
                    flyout.Items.RemoveAt(flyout.Items.Count - 1);
                }

                if (_engrave != null)
                {
                    flyout.Items.Add(MenuBuilder.ItemFor(_engrave.EngravePublish));
                    flyout.Items.Add(MenuBuilder.ItemFor(_engrave.EngraveCustom));
                }

                break;

            case ToolbarMenu.Templates:
                MenuBuilder.FillTemplates(
                    flyout.Items, _snippets, _snippetActions, _applySnippet,
                    _scoreWizard);
                break;

            case ToolbarMenu.Save:
                MenuBuilder.FillSave(flyout.Items, _main, _snippetActions);
                break;

            case ToolbarMenu.Close:
                MenuBuilder.FillClose(flyout.Items, _main);
                break;
        }
    }

    private UIElement ContentFor(AppAction action)
    {
        UIElement content = string.IsNullOrEmpty(action.IconName)
            ? null
            : IconTheme.Image(ActualTheme, action.IconName);

        //No icon of that name in the shipped sets — the button says what it
        //does instead, in the short form Qt would put under an icon. Emptying
        //assets/icons/ leaves a working, if wordy, pair of toolbars.
        content ??= new TextBlock
        {
            Text = MenuBuilder.Display(action.IconText),
            VerticalAlignment = VerticalAlignment.Center,
        };

        //was previously: the content was returned at full opacity whatever the
        //command's state, so a DISABLED toolbar button looked exactly like an
        //enabled one — Go to previous position was greyed out on the View menu
        //and bright on the bar at the same moment. The button itself is
        //disabled either way; this is the affordance Qt draws for free by
        //rendering a disabled action's icon in a greyed mode.
        content.Opacity = action.IsEnabled ? 1.0 : DisabledOpacity;
        return content;
    }

    /// <summary>Answers the tool tip a toolbar button carries.</summary>
    /// <param name="action">The command the button fires.</param>
    /// <returns>The tip.</returns>
    /// <remarks>
    /// Qt's own shape for a toolbar button: what the command is, then its
    /// shortcut in parentheses. An action that sets a tool tip of its own says
    /// that instead of its menu text — which is how the engrave button
    /// promises "Engrave (preview; press Shift for custom)" and then, while a
    /// job runs, "Abort engraving job". The accelerator marker is stripped at
    /// DISPLAY (board trap 18), never out of the msgid.
    /// </remarks>
    public static string ToolTipFor(AppAction action)
    {
        if (action == null) { return string.Empty; }

        string text = string.IsNullOrEmpty(action.ToolTip)
            ? MenuBuilder.Display(action.Text)
            : MenuBuilder.Display(action.ToolTip);
        return action.Shortcuts.Count > 0
            ? text + " (" + action.Shortcuts[0] + ")"
            : text;
    }

    private static void Describe(
        DependencyObject button, AppAction action, string barTitle)
    {
        ToolTipService.SetToolTip(button, ToolTipFor(action));
        AutomationProperties.SetName(button, MenuBuilder.Display(action.Text));
        AutomationProperties.SetHelpText(button, barTitle);
    }

    private UIElement ControlFor(ToolbarEntry entry, string barTitle)
    {
        switch (entry.Widget)
        {
            case ToolbarWidget.DocumentChooser:
                return BuildScoreChooser(entry.Action, barTitle);

            case ToolbarWidget.ZoomChooser:
                return BuildZoomChooser(barTitle);

            case ToolbarWidget.Pager:
                return BuildPager(barTitle);

            default:
                return null;
        }
    }

    private UIElement BuildScoreChooser(AppAction action, string barTitle)
    {
        _scoreChooser = new ComboBox { MinWidth = 150, IsEnabled = false };
        _scoreChooser.SelectionChanged += (_, _) =>
        {
            if (_writingChooser) { return; }

            _musicView?.SelectScore(_scoreChooser.SelectedIndex);
        };

        if (action != null)
        {
            AutomationProperties.SetName(
                _scoreChooser, MenuBuilder.Display(action.Text));
            AutomationProperties.SetHelpText(_scoreChooser, barTitle);
            ToolTipService.SetToolTip(_scoreChooser, action.ToolTip);

            //Upstream's music_document_select IS the combo (a ComboBoxAction),
            //and its Ctrl+Shift+O drops the list open, because that is all a
            //chooser can do from the keyboard — ComboBoxAction.showPopup.
            action.Handler = () =>
            {
                _scoreChooser.Focus(FocusState.Programmatic);
                _scoreChooser.IsDropDownOpen = true;
            };
        }

        return _scoreChooser;
    }

    private UIElement BuildZoomChooser(string barTitle)
    {
        _zoomEntries = ZoomLevels.Entries();
        _zoomChooser = new ComboBox { MinWidth = 84 };
        foreach (ZoomEntry entry in _zoomEntries)
        {
            _zoomChooser.Items.Add(entry.Caption);
        }

        _zoomChooser.SelectionChanged += (_, _) =>
        {
            if (_writingZoom || _musicView == null) { return; }

            //An index past the declared entries is the transient row ShowZoom
            //adds for a zoom the list does not carry — it already IS the
            //current zoom, so choosing it changes nothing.
            int index = _zoomChooser.SelectedIndex;
            if (index < 0 || index >= _zoomEntries.Count) { return; }

            ZoomEntry chosen = _zoomEntries[index];
            if (chosen.Mode is { } mode) { _musicView.ApplyViewMode(mode); }
            else if (chosen.Factor is { } factor)
            {
                _musicView.ApplyZoomFactor(factor);
            }
        };

        if (_music?.MusicZoomCombo != null)
        {
            AutomationProperties.SetName(
                _zoomChooser, MenuBuilder.Display(_music.MusicZoomCombo.Text));
            AutomationProperties.SetHelpText(_zoomChooser, barTitle);
            ToolTipService.SetToolTip(
                _zoomChooser, MenuBuilder.Display(_music.MusicZoomCombo.Text));
            _music.MusicZoomCombo.Handler = () =>
            {
                _zoomChooser.Focus(FocusState.Programmatic);
                _zoomChooser.IsDropDownOpen = true;
            };
        }

        return _zoomChooser;
    }

    private UIElement BuildPager(string barTitle)
    {
        //Upstream's pager is a QSpinBox with NoButtons: a field holding the
        //page number inside the format string's own prefix and suffix, driven
        //by typing or by the up and down keys.
        _pager = new TextBox
        {
            MinWidth = 84,
            Width = 96,
            TextAlignment = TextAlignment.Center,
            IsEnabled = false,
        };
        AutomationProperties.SetName(
            _pager, MenuBuilder.Display(I18n.Get("{num} of {total}")));
        AutomationProperties.SetHelpText(_pager, barTitle);

        _pager.KeyDown += (_, args) =>
        {
            if (_musicView == null) { return; }

            if (args.Key == VirtualKey.Enter)
            {
                Commit();
                args.Handled = true;
            }
            else if (args.Key == VirtualKey.Up)
            {
                _musicView.GoToPage(_musicView.CurrentPageNumber + 1);
                args.Handled = true;
            }
            else if (args.Key == VirtualKey.Down)
            {
                _musicView.GoToPage(_musicView.CurrentPageNumber - 1);
                args.Handled = true;
            }
        };
        _pager.LostFocus += (_, _) => Commit();

        void Commit()
        {
            if (_writingPager || _musicView == null) { return; }

            int page = PagerDisplay.Parse(_pager.Text, _musicView.PageCount);
            if (page > 0) { _musicView.GoToPage(page); }

            ShowPage();
        }

        return _pager;
    }

    private void OnScoresChanged(object sender, EventArgs e)
    {
        if (_scoreChooser == null) { return; }

        IReadOnlyList<string> names = _musicView?.ScoreNames()
            ?? Array.Empty<string>();
        _writingChooser = true;
        try
        {
            _scoreChooser.Items.Clear();
            foreach (string name in names) { _scoreChooser.Items.Add(name); }

            _scoreChooser.IsEnabled = names.Count > 0;
            _scoreChooser.SelectedIndex = names.Count > 0
                ? Math.Clamp(_musicView.CurrentScoreIndex, 0, names.Count - 1)
                : -1;
        }
        finally
        {
            _writingChooser = false;
        }

        ShowPage();
    }

    private void OnViewStateChanged(object sender, EventArgs e)
    {
        ShowPage();
        ShowZoom();
    }

    private void ShowPage()
    {
        if (_pager == null) { return; }

        int total = _musicView?.PageCount ?? 0;
        int number = _musicView?.CurrentPageNumber ?? 0;
        _writingPager = true;
        try
        {
            _pager.Text = PagerDisplay.Format(number, total);
            _pager.IsEnabled = total > 0;
        }
        finally
        {
            _writingPager = false;
        }
    }

    private void ShowZoom()
    {
        if (_zoomChooser == null || _zoomEntries.Count == 0) { return; }

        _writingZoom = true;
        try
        {
            //Drop whatever transient row the last call added.
            while (_zoomChooser.Items.Count > _zoomEntries.Count)
            {
                _zoomChooser.Items.RemoveAt(_zoomChooser.Items.Count - 1);
            }

            if (_musicView == null)
            {
                _zoomChooser.SelectedIndex = -1;
                return;
            }

            int index = ZoomLevels.IndexFor(
                _zoomEntries, _musicView.CurrentViewMode, _musicView.ZoomFactor);

            //⚠ A SMALL DELIBERATE DIFFERENCE OF MECHANISM, said out loud.
            //Upstream's combo is editable with a READ-ONLY line edit, so when
            //the view is at a zoom its list does not carry — after a
            //Ctrl+scroll, or after Zoom In from 200% — Qt simply writes the
            //percentage into the box (_adjustComboBox's setEditText branch)
            //without adding a row. A CodeBrix.Platform ComboBox shows one of
            //its items or nothing, and making it editable would mean a text
            //box a user could type into, which upstream's is not. So the
            //percentage is shown as a row of its own, added while it is
            //current and removed the moment it stops being: the box reads
            //"240%" as upstream's does, at the cost of one extra line in the
            //open list.
            //was previously: SelectedIndex = -1, which left the box EMPTY and
            //the user with no way to read the current zoom at all.
            if (index < 0)
            {
                _zoomChooser.Items.Add(
                    ZoomLevels.CaptionFor(_musicView.ZoomFactor));
                index = _zoomChooser.Items.Count - 1;
            }

            _zoomChooser.SelectedIndex = index;
        }
        finally
        {
            _writingZoom = false;
        }
    }
}
