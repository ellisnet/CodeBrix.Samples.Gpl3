// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.System;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>Shortcuts: reading, writing and comparing them.</summary>
public class KeySequenceTests
{
    [Theory]
    [InlineData("Ctrl+N")]
    [InlineData("Ctrl+Shift+S")]
    [InlineData("Ctrl+Alt+G")]
    [InlineData("F11")]
    [InlineData("Shift+F3")]
    [InlineData("Ctrl+,")]
    [InlineData("Alt+Right")]
    [InlineData("Ctrl+Shift+Tab")]
    public void a_shortcut_survives_a_round_trip(string text)
    {
        //Arrange, Act
        KeySequence shortcut = KeySequence.Parse(text);

        //Assert
        shortcut.Should().NotBeNull();
        shortcut.ToString().Should().Be(text);
    }

    [Fact]
    public void the_modifier_names_are_read_case_insensitively()
    {
        //Arrange, Act
        KeySequence shortcut = KeySequence.Parse("control+SHIFT+f");

        //Assert
        shortcut.Should().Be(KeySequence.Parse("Ctrl+Shift+F"));
    }

    [Fact]
    public void an_unknown_key_reads_as_nothing()
    {
        //Arrange, Act
        KeySequence shortcut = KeySequence.Parse("Ctrl+Nonesuch");

        //Assert
        shortcut.Should().BeNull();
    }

    [Fact]
    public void two_shortcuts_with_the_same_key_and_modifiers_are_equal()
    {
        //Arrange
        KeySequence first = new KeySequence(VirtualKey.S, VirtualKeyModifiers.Control);
        KeySequence second = KeySequence.Parse("Ctrl+S");

        //Act, Assert
        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void redo_carries_both_of_qts_bindings()
    {
        //Arrange, Act
        IReadOnlyList<KeySequence> redo = StandardKeys.Redo;

        //Assert
        redo.Select(k => k.ToString()).Should()
            .BeEquivalentTo(new[] { "Ctrl+Shift+Z", "Ctrl+Y" });
    }
}

/// <summary>A command's state, and what invoking it does.</summary>
public class AppActionTests
{
    [Fact]
    public void triggering_runs_the_handler()
    {
        //Arrange
        int runs = 0;
        AppAction action = new AppAction("test").Does(() => runs++);

        //Act
        action.Trigger();

        //Assert
        runs.Should().Be(1);
    }

    [Fact]
    public void a_disabled_command_does_nothing()
    {
        //Arrange
        int runs = 0;
        AppAction action = new AppAction("test").Does(() => runs++);
        action.IsEnabled = false;

        //Act
        action.Trigger();

        //Assert
        runs.Should().Be(0);
    }

    [Fact]
    public void a_toggle_flips_when_triggered()
    {
        //Arrange
        AppAction action = new AppAction("test").AsToggle();

        //Act
        action.Trigger();

        //Assert
        action.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void the_icon_text_falls_back_to_the_full_text()
    {
        //Arrange
        AppAction action = new AppAction("test") { Text = "&Save Document" };

        //Act, Assert
        action.IconText.Should().Be("&Save Document");
        action.IconText = "Save";
        action.IconText.Should().Be("Save");
    }

    [Fact]
    public void changing_the_enabled_state_announces_it()
    {
        //Arrange
        AppAction action = new AppAction("test");
        int announcements = 0;
        action.CanExecuteChanged += (_, _) => announcements++;

        //Act
        action.IsEnabled = false;
        action.IsEnabled = false;

        //Assert — only the real change is announced.
        announcements.Should().Be(1);
    }
}

/// <summary>A test collection with two commands and a known default.</summary>
internal sealed class TestActions : ActionCollection
{
    public TestActions(SettingsStore settings)
        : base("test", settings)
        => Initialize();

    public AppAction First { get; private set; }

    public AppAction Second { get; private set; }

    protected override void CreateActions()
    {
        First = Add("first").WithShortcut("Ctrl+1");
        Second = Add("second");
    }

    public override void TranslateUI()
    {
        First.Text = "&First";
        Second.Text = "&Second";
    }
}

/// <summary>How a collection remembers a user's shortcut changes.</summary>
public class ActionCollectionTests : IDisposable
{
    private readonly string _path;
    private readonly SettingsStore _settings;

    public ActionCollectionTests()
    {
        _path = Path.Combine(Path.GetTempPath(),
            "frescobrix-tests-" + Guid.NewGuid().ToString("N"), "settings.sqlite");
        _settings = new SettingsStore(_path);
    }

    public void Dispose()
    {
        _settings.Dispose();
        try
        {
            Directory.Delete(Path.GetDirectoryName(_path), recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void a_command_starts_with_the_shortcut_it_was_created_with()
    {
        //Arrange, Act
        TestActions actions = new TestActions(_settings);

        //Assert
        actions.First.Shortcuts.Single().ToString().Should().Be("Ctrl+1");
        actions.DefaultShortcuts("first").Single().ToString().Should().Be("Ctrl+1");
    }

    [Fact]
    public void a_changed_shortcut_is_remembered_across_a_reload()
    {
        //Arrange
        TestActions actions = new TestActions(_settings);

        //Act
        actions.SetShortcuts("first", new[] { KeySequence.Parse("Ctrl+9") });
        TestActions reloaded = new TestActions(_settings);

        //Assert
        reloaded.First.Shortcuts.Single().ToString().Should().Be("Ctrl+9");
    }

    [Fact]
    public void setting_a_shortcut_back_to_its_default_forgets_the_override()
    {
        //Arrange
        TestActions actions = new TestActions(_settings);
        actions.SetShortcuts("first", new[] { KeySequence.Parse("Ctrl+9") });

        //Act
        actions.SetShortcuts("first", new[] { KeySequence.Parse("Ctrl+1") });

        //Assert — nothing is stored, so a future change of default reaches
        //a user who never really customised this command.
        _settings.KeysWithPrefix("shortcuts/default/test/").Should().BeEmpty();
    }

    [Fact]
    public void removing_a_default_shortcut_is_remembered_as_a_deliberate_choice()
    {
        //Arrange
        TestActions actions = new TestActions(_settings);

        //Act
        actions.SetShortcuts("first", Array.Empty<KeySequence>());
        TestActions reloaded = new TestActions(_settings);

        //Assert
        reloaded.First.Shortcuts.Should().BeEmpty();
    }

    [Fact]
    public void restoring_the_default_puts_the_original_shortcut_back()
    {
        //Arrange
        TestActions actions = new TestActions(_settings);
        actions.SetShortcuts("first", new[] { KeySequence.Parse("Ctrl+9") });

        //Act
        actions.RestoreDefaultShortcuts("first");

        //Assert
        actions.First.Shortcuts.Single().ToString().Should().Be("Ctrl+1");
        _settings.KeysWithPrefix("shortcuts/default/test/").Should().BeEmpty();
    }

    [Fact]
    public void a_stored_shortcut_for_a_vanished_command_is_dropped()
    {
        //Arrange
        _settings.SetString("shortcuts/default/test/gone", "Ctrl+8");

        //Act
        _ = new TestActions(_settings);

        //Assert
        _settings.GetString("shortcuts/default/test/gone").Should().BeNull();
    }

    [Fact]
    public void a_shortcut_already_taken_is_reported_as_a_conflict()
    {
        //Arrange
        TestActions actions = new TestActions(_settings);
        ActionCollectionManager manager = new ActionCollectionManager();
        manager.Add(actions);

        //Act
        string conflict = manager.FindShortcutConflict(KeySequence.Parse("Ctrl+1"));

        //Assert — the accelerator marker is stripped for display.
        conflict.Should().Be("First");
    }

    [Fact]
    public void the_command_being_edited_does_not_conflict_with_itself()
    {
        //Arrange
        TestActions actions = new TestActions(_settings);
        ActionCollectionManager manager = new ActionCollectionManager();
        manager.Add(actions);

        //Act
        string conflict = manager.FindShortcutConflict(
            KeySequence.Parse("Ctrl+1"), actions, "first");

        //Assert
        conflict.Should().BeNull();
    }

    [Fact]
    public void freeing_a_shortcut_takes_it_off_the_command_that_had_it()
    {
        //Arrange
        TestActions actions = new TestActions(_settings);
        ActionCollectionManager manager = new ActionCollectionManager();
        manager.Add(actions);

        //Act
        manager.RemoveShortcuts(new[] { KeySequence.Parse("Ctrl+1") });

        //Assert
        actions.First.Shortcuts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("&Save", "Save")]
    [InlineData("Save && Close", "Save & Close")]
    [InlineData("No marker", "No marker")]
    [InlineData("", "")]
    public void accelerator_markers_are_stripped_for_display(string text, string expected)
    {
        //Arrange, Act
        string plain = ActionCollectionManager.RemoveAccelerator(text);

        //Assert
        plain.Should().Be(expected);
    }

    [Fact]
    public void the_windows_commands_carry_the_shortcuts_frescobaldi_gives_them()
    {
        //Arrange, Act
        MainActions actions = new MainActions(_settings);

        //Assert — a spot check across the standard-key and explicit paths.
        actions.FileSave.Shortcuts.Single().ToString().Should().Be("Ctrl+S");
        actions.FileSaveAs.Shortcuts.Single().ToString().Should().Be("Ctrl+Shift+S");
        actions.EditPreferences.Shortcuts.Single().ToString().Should().Be("Ctrl+,");
        actions.ViewGotoLine.Shortcuts.Single().ToString().Should().Be("Ctrl+Alt+G");
        actions.WindowFullscreen.Shortcuts.Select(s => s.ToString()).Should()
            .BeEquivalentTo(new[] { "Ctrl+Shift+F", "F11" });
    }

    [Fact]
    public void no_two_of_the_windows_own_commands_share_a_shortcut()
    {
        //Arrange
        ActionCollectionManager manager = new ActionCollectionManager();
        manager.Add(new MainActions(_settings));
        manager.Add(new ViewActions(_settings));

        //Act
        List<string> duplicates = manager.AllShortcuts()
            .GroupBy(s => s.Shortcut.ToString(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key + ": "
                + string.Join(", ", g.Select(x => x.Action.Name)))
            .ToList();

        //Assert
        duplicates.Should().BeEmpty();
    }
}
