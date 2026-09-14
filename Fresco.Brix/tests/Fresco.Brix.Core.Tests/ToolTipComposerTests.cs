// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.CommandBar;
using Fresco.Brix.Commands;
using Fresco.Brix.Engrave;
using Fresco.Brix.MusicView;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Fresco.Brix.Tools;
using Microsoft.UI.Xaml.Controls;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The tooltip the CommandBar add-in composes, against the wording Fresco.Brix
/// writes today: what a button has to be told, and what the application keeps
/// saying for itself.
/// </summary>
/// <remarks>
/// <para>
/// Upstream Frescobaldi's shape is "what the command does, then its shortcut in
/// parentheses", which is also the add-in's shape. The two agree only if the
/// button is handed the DISPLAY text (the accelerator marker already stripped,
/// board trap 18) and Fresco's own spelling of the shortcut; the add-in would
/// otherwise spell the keystroke in the framework's order rather than in Qt's.
/// </para>
/// <para>
/// One action does not fit the shape at all: <c>engrave_runner</c> carries a
/// tooltip that is not its label, and swaps it while a job runs. The application
/// sets that one itself, and the add-in leaves an application-set tooltip alone.
/// </para>
/// </remarks>
[Collection(XamlTestCollection.Name)]
public class ToolTipComposerTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "frescobrix-tooltip-" + Guid.NewGuid().ToString("N"));

    private readonly SettingsStore _settings;

    /// <summary>Creates the fixture with a store of its own.</summary>
    public ToolTipComposerTests()
    {
        Directory.CreateDirectory(_folder);
        _settings = new SettingsStore(_folder);
    }

    /// <summary>Removes the scratch store.</summary>
    public void Dispose()
    {
        _settings.Dispose();
        try { Directory.Delete(_folder, true); } catch (IOException) { }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void the_composer_writes_every_tooltip_fresco_writes_today()
    {
        //Arrange — every action on the two window toolbars, less the one that
        //carries a tooltip of its own.
        Dictionary<string, string> today = new Dictionary<string, string>();
        Dictionary<string, string> composed = new Dictionary<string, string>();

        foreach (AppAction action in ToolbarActions().Where(a => string.IsNullOrEmpty(a.ToolTip)))
        {
            today[action.Name] = ToolbarLayout.ToolTipFor(action);

            //Act — exactly what the button is handed: the display text, and
            //Fresco's own spelling of the shortcut through ToolButton.Shortcut,
            //which the add-in prefers over anything it could derive.
            composed[action.Name] = ToolTipComposer.Compose(
                MenuBuilder.Display(action.Text), ShortcutOf(action), null);
        }

        //Assert
        today.Count.Should().BeGreaterThan(10);
        composed.Should().BeEquivalentTo(today);
    }

    [Fact]
    public void a_button_told_the_text_and_the_shortcut_says_what_fresco_says()
    {
        //Arrange
        MainActions main = new MainActions(_settings);
        ToolButton button = new ToolButton
        {
            Text = MenuBuilder.Display(main.FileNew.Text),
            Shortcut = ShortcutOf(main.FileNew),
        };

        //Act
        string composed = button.ComposedToolTipText;

        //Assert — the string MainToolbarTests pins.
        composed.Should().Be("New Document (Ctrl+N)");
        composed.Should().Be(ToolbarLayout.ToolTipFor(main.FileNew));
    }

    [Fact]
    public void a_button_with_no_shortcut_says_only_what_it_does()
    {
        //Arrange
        MusicViewActions music = new MusicViewActions(_settings);
        AppAction clear = music.MusicClear;

        //Act
        ToolButton button = new ToolButton
        {
            Text = MenuBuilder.Display(clear.Text),
            Shortcut = ShortcutOf(clear),
        };

        //Assert — no keystroke, no parentheses, on both routes.
        button.ComposedToolTipText.Should().Be(ToolbarLayout.ToolTipFor(clear));
        button.ComposedToolTipText.Should().NotContain("(");
    }

    [Fact]
    public void the_composed_tooltip_reaches_the_button_itself()
    {
        //Arrange
        ToolButton button = new ToolButton { Text = "Open Document", Shortcut = "Ctrl+O" };

        //Act
        object installed = ToolTipService.GetToolTip(button);

        //Assert — the button installs what it composed, with no bar and no
        //window anywhere.
        installed.Should().Be("Open Document (Ctrl+O)");
    }

    [Fact]
    public void the_accessible_name_names_the_bar_the_button_sits_in()
    {
        //Arrange
        TestHost.EnsureReady();
        ToolBar bar = new ToolBar { Title = ToolbarLayout.MainTitle() };
        ToolButton button = new ToolButton { Text = "New Document", Shortcut = "Ctrl+N" };
        bar.Items.Add(button);

        //Act — the bar has to lay out before the button has a parent to walk up
        //from.
        bar.Measure(new Windows.Foundation.Size(800d, 100d));
        string name = button.AccessibleName;

        //Assert — an accepted change: today the application splits this across
        //AutomationProperties Name and HelpText.
        name.Should().Be("New Document (Ctrl+N), Main Toolbar");
    }

    [Fact]
    public void a_tooltip_the_application_set_first_is_left_alone()
    {
        //Arrange — the engrave button's promise, which is not its label.
        EngraveActions engrave = new EngraveActions(_settings);
        string promise = ToolbarLayout.ToolTipFor(engrave.EngraveRunner);
        ToolButton button = new ToolButton();
        ToolTipService.SetToolTip(button, promise);

        //Act — everything the builder would go on to set.
        button.Text = MenuBuilder.Display(engrave.EngraveRunner.Text);
        button.Shortcut = ShortcutOf(engrave.EngraveRunner);
        button.Icon = IconTheme.Source("lilypond-run");

        //Assert
        promise.Should().Be("Engrave (preview; Shift-click for custom)");
        ToolTipService.GetToolTip(button).Should().Be(promise);
    }

    [Fact]
    public void a_tooltip_the_application_sets_afterwards_wins_and_stays()
    {
        //Arrange — the other order: the button composes first, then a running
        //job replaces the wording.
        ToolButton button = new ToolButton { Text = "Engrave", Shortcut = null };
        ToolTipService.GetToolTip(button).Should().Be("Engrave");

        //Act
        ToolTipService.SetToolTip(button, "Abort engraving job");
        button.Icon = IconTheme.Source("lilypond-stop");
        button.Text = "Engrave";

        //Assert — the application's wording survives the later property writes,
        //so the engrave button can swap its tooltip while a job runs.
        ToolTipService.GetToolTip(button).Should().Be("Abort engraving job");
    }

    /// <summary>Answers Fresco's own spelling of an action's first shortcut.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The shortcut text, or null when the action has none.</returns>
    private static string ShortcutOf(AppAction action)
        => action.Shortcuts.Count > 0 ? action.Shortcuts[0].ToString() : null;

    /// <summary>Answers every action the two window toolbars put on a button.</summary>
    /// <returns>The actions, in bar order.</returns>
    private IEnumerable<AppAction> ToolbarActions()
    {
        MainActions main = new MainActions(_settings);
        EngraveActions engrave = new EngraveActions(_settings);
        MusicViewActions music = new MusicViewActions(_settings);

        IEnumerable<ToolbarEntry> entries = ToolbarLayout
            .Main(
                main,
                new BrowserActions(_settings),
                new ScoreWizardActions(_settings),
                engrave,
                verboseToolButtons: false)
            .Concat(ToolbarLayout.Music(music));

        return entries.Where(e => e.Action != null).Select(e => e.Action);
    }
}
