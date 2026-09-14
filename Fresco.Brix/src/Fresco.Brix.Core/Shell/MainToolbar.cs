// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.CommandBar;
using Fresco.Brix.Commands;
using Fresco.Brix.MusicView;
using Fresco.Brix.Preferences;
using Fresco.Brix.Services;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.System;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/mainwindow.py (createToolBars)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The window's toolbars: the Main Toolbar and the Music View Toolbar, side by
/// side on one row under the menu bar.
/// </summary>
/// <remarks>
/// <para>
/// Upstream calls <c>addToolBar</c> twice, so Qt lays the two bars out side by
/// side in the same top area. This class IS that area: it is one
/// <see cref="ToolBarTray"/> from the CodeBrix.Platform CommandBar add-in
/// holding two real <see cref="ToolBar"/>s, which lays them side by side on one
/// row and wraps the second to a row of its own when the window is too narrow
/// to hold both. The gap the tray leaves between two bars is what Qt draws
/// between two docked toolbars.
/// </para>
/// <para>
/// The window had no toolbar at all before ruling FR16 (Jeremy, 2026-09-02),
/// which is why <c>engrave_runner</c> — an action that exists only for one —
/// was unreachable (audit A GAP-24). The first build of this class drew the
/// buttons by hand, because this platform build ships no
/// <c>CommandBar</c>/<c>AppBarButton</c>; the add-in is the platform's answer to
/// exactly that, and it brings the overflow chevron, keyboard navigation along
/// a bar, access keys and automation peers that the hand-built row had to do
/// without.
/// </para>
/// <para>
/// <see cref="ToolbarLayout"/> still says WHAT is on each bar and in what
/// order, as data, so the order stays assertable without a window; this class
/// only turns each entry into an element.
/// </para>
/// </remarks>
public sealed class MainToolbar : ToolBarTray
{
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

    private ComboBox _scoreChooser;
    private ComboBox _zoomChooser;
    private TextBox _pager;
    private MusicViewPanel _musicView;
    private FrameworkElement _widthSource;
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

        //The four presentation settings are INHERITED attached properties, so
        //setting them on the tray settles both bars and every button on them.
        //Upstream's toolbars are Qt's ToolButtonIconOnly at the icon size
        //IconTheme already names, and Qt writes a tool tip for every button.
        ToolBarProperties.SetIconSize(this, IconTheme.ToolbarIconSize);
        ToolBarProperties.SetLabelMode(this, LabelMode.IconOnly);
        ToolBarProperties.SetShowToolTips(this, true);

        //The bars are built once the tray is in a tree, because a button's icon
        //resolves against the theme the tree says it is in. A theme change no
        //longer rebuilds anything: an icon element re-renders itself when the
        //theme or the display scale changes, which is also what keeps it
        //pixel-exact at a fractional scale.
        Loaded += (_, _) =>
        {
            FollowHostWidth();
            if (_built) { return; }

            Rebuild();
        };

        Unloaded += (_, _) => StopFollowingHostWidth();
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

    /// <summary>Rebuilds both bars from the current preference.</summary>
    /// <remarks>
    /// Upstream's <c>mainwindow.settingsChanged</c> hangs or unhangs the three
    /// pull-down menus the moment <c>verbose_toolbuttons</c> changes; the
    /// window calls this from the same place.
    /// </remarks>
    public void SettingsChanged() => Rebuild();

    /// <summary>Answers whether the pull-down menus are wanted.</summary>
    /// <param name="settings">The store, or null for the default.</param>
    /// <returns>Whether they are.</returns>
    /// <remarks>Upstream's
    /// <c>QSettings().value("verbose_toolbuttons", False, bool)</c>.</remarks>
    public static bool VerboseToolButtons(SettingsStore settings)
        => settings?.GetBool(GeneralValues.VerboseToolButtonsKey, false) ?? false;

    /// <summary>Caps the tray at the width its host was arranged to.</summary>
    /// <remarks>
    /// A <see cref="ContentControl"/> measures its content with an UNBOUNDED
    /// width, so a tray hosted in one is offered infinite room: it never wraps
    /// its second bar to a second row, and no bar ever moves its trailing items
    /// behind the overflow chevron. The ARRANGE pass is right — the host reports
    /// the window's own width — so the host's arranged width is handed back to
    /// the tray as a maximum and the next measure is bounded by it. This is a
    /// CodeBrix.Platform defect worked around here; it is in the package fixlist
    /// of 2026-09-13.
    /// </remarks>
    private void FollowHostWidth()
    {
        if (_widthSource != null) { return; }

        FrameworkElement nearest = null;
        DependencyObject parent = VisualTreeHelper.GetParent(this);
        while (parent != null)
        {
            if (parent is ContentControl host)
            {
                _widthSource = host;
                break;
            }

            nearest ??= parent as FrameworkElement;
            parent = VisualTreeHelper.GetParent(parent);
        }

        _widthSource ??= nearest;
        if (_widthSource == null) { return; }

        _widthSource.SizeChanged += OnHostSizeChanged;
        ApplyHostWidth(_widthSource.ActualWidth);
    }

    private void StopFollowingHostWidth()
    {
        if (_widthSource == null) { return; }

        _widthSource.SizeChanged -= OnHostSizeChanged;
        _widthSource = null;
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyHostWidth(e.NewSize.Width);

    private void ApplyHostWidth(double width)
    {
        if (width > 0 && !double.IsInfinity(width)) { MaxWidth = width; }
    }

    private void Rebuild()
    {
        _built = true;

        while (Children.Count > 0)
        {
            Children.RemoveAt(Children.Count - 1);
        }

        _scoreChooser = null;
        _zoomChooser = null;
        _pager = null;

        ToolBar mainBar = new ToolBar { Title = ToolbarLayout.MainTitle() };
        bool verbose = VerboseToolButtons(_settings);
        foreach (ToolbarEntry entry in ToolbarLayout.Main(
            _main, _browser, _scoreWizard, _engrave, verbose))
        {
            Add(mainBar, entry);
        }

        if (mainBar.Items.Count > 0) { Children.Add(mainBar); }

        IReadOnlyList<ToolbarEntry> musicEntries = ToolbarLayout.Music(_music);
        if (musicEntries.Count > 0)
        {
            ToolBar musicBar = new ToolBar { Title = ToolbarLayout.MusicTitle() };
            foreach (ToolbarEntry entry in musicEntries)
            {
                Add(musicBar, entry);
            }

            Children.Add(musicBar);
        }

        OnScoresChanged(this, EventArgs.Empty);
        OnViewStateChanged(this, EventArgs.Empty);
    }

    private void Add(ToolBar bar, ToolbarEntry entry)
    {
        switch (entry.Kind)
        {
            case ToolbarEntryKind.Separator:
                bar.Items.Add(new ToolBarSeparator());
                return;

            case ToolbarEntryKind.Widget:
                UIElement control = ControlFor(entry, bar.Title);
                if (control != null) { bar.Items.Add(control); }

                return;

            default:
                if (entry.Action == null) { return; }

                bar.Items.Add(ButtonFor(entry));
                return;
        }
    }

    private ToolButton ButtonFor(ToolbarEntry entry)
    {
        AppAction action = entry.Action;
        ToolButton button;
        if (entry.Menu != ToolbarMenu.None)
        {
            //Qt's MenuButtonPopup, as one button: the main part IS the action
            //and the arrow part opens the menu. Nothing on either bar is a
            //checkable action AND a menu button, so a menu wins the choice.
            MenuFlyout flyout = new MenuFlyout();
            flyout.Opening += (_, _) => FillMenu(flyout, entry.Menu);
            button = new ToolDropDownButton
            {
                PopupMode = PopupMode.MenuButton,
                Flyout = flyout,
            };
        }
        else if (action.IsCheckable)
        {
            button = new ToolToggleButton { IsChecked = action.IsChecked };
        }
        else
        {
            button = new ToolButton();
        }

        //IsEnabled is NOT set here. The button follows the command's
        //CanExecute, which AppAction answers from IsEnabled and re-raises on
        //every change; an explicit IsEnabled would outrank the command for good.
        button.Command = action;

        //A tool tip the action writes for itself is not the button's label, so
        //the application sets it: the add-in leaves a tool tip it did not
        //compose alone. That is how the engrave button promises a preview and
        //then, while a job runs, offers to abort it. Once the application has
        //set one, that button never composes another — so the flag stays on and
        //the application keeps saying the whole tip. Every other button says
        //nothing at all here and the add-in composes "Text (Shortcut)", which
        //is the same string ToolbarLayout.ToolTipFor writes.
        bool applicationOwnsToolTip = false;

        void Update()
        {
            button.Text = MenuBuilder.Display(action.Text);
            button.Shortcut = action.Shortcuts.Count > 0
                ? action.Shortcuts[0].ToString()
                : null;

            //No icon of that name in the shipped sets hands back nothing at
            //all, which is what makes the add-in show the button's text rather
            //than draw a blank square. Emptying assets/icons/ leaves a working,
            //if wordy, pair of toolbars.
            button.Icon = IconTheme.Source(action.IconName);

            if (applicationOwnsToolTip || !string.IsNullOrEmpty(action.ToolTip))
            {
                applicationOwnsToolTip = true;
                ToolTipService.SetToolTip(button, ToolbarLayout.ToolTipFor(action));
            }

            //A click flips the toggle's own state and, separately, runs the
            //command, which flips the action's — one flip each, ending in step.
            //The action is never written from here, which is what keeps the two
            //from cancelling each other out.
            if (button is ToolToggleButton box) { box.IsChecked = action.IsChecked; }
        }

        Update();

        //A toolbar button is never removed from the tree while its bar lives,
        //so one subscription is enough — but it is dropped when the bars are
        //rebuilt, which is why Rebuild makes new ones.
        action.PropertyChanged += (_, _) => Update();
        return button;
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
        _scoreChooser = new ComboBox
        {
            MinWidth = 150,
            IsEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
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
        _zoomChooser = new ComboBox
        {
            MinWidth = 84,
            VerticalAlignment = VerticalAlignment.Center,
        };
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
            VerticalAlignment = VerticalAlignment.Center,
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
            //open list. Leaving SelectedIndex at -1 instead would leave the box
            //EMPTY and the user with no way to read the current zoom at all.
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
