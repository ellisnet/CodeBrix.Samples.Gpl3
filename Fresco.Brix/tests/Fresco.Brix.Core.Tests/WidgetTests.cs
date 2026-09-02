// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Services;
using Fresco.Brix.Widgets;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.System;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The ported <c>widgets/</c> survivors, in the half of each that has no
/// window in it: the list a <c>ListEdit</c> edits, and the rule a
/// <c>KeySequenceWidget</c> records a keystroke by.
/// </summary>
public class WidgetTests : IDisposable
{
    private readonly string _directory;

    /// <summary>Creates the fixture over a scratch directory.</summary>
    public WidgetTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-widgets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Removes the scratch directory.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ---------------------------------------------------------------- listedit

    [Fact]
    public void a_new_list_has_nothing_selected()
    {
        //Arrange
        ListEditModel model = new ListEditModel();

        //Assert
        model.Items.Count.Should().Be(0);
        model.HasSelection.Should().Be(false);
        model.Current.Should().BeNull();
    }

    [Fact]
    public void setting_the_items_selects_the_first()
    {
        //Arrange
        ListEditModel model = new ListEditModel();

        //Act
        model.SetItems(new[] { "one", "two" });

        //Assert
        model.Items.Count.Should().Be(2);
        model.CurrentIndex.Should().Be(0);
        model.Current.Should().Be("one");
    }

    [Fact]
    public void an_added_item_becomes_the_current_one()
    {
        //Arrange
        ListEditModel model = new ListEditModel();
        model.SetItems(new[] { "one" });

        //Act
        model.Add("two");

        //Assert
        model.CurrentIndex.Should().Be(1);
        model.Current.Should().Be("two");
    }

    [Fact]
    public void editing_replaces_the_current_item_in_place()
    {
        //Arrange
        ListEditModel model = new ListEditModel();
        model.SetItems(new[] { "one", "two", "three" });
        model.CurrentIndex = 1;

        //Act
        model.ReplaceCurrent("TWO");

        //Assert
        model.Items[1].Should().Be("TWO");
        model.Items.Count.Should().Be(3);
    }

    [Fact]
    public void removing_the_last_item_moves_the_selection_back()
    {
        //Arrange
        ListEditModel model = new ListEditModel();
        model.SetItems(new[] { "one", "two" });
        model.CurrentIndex = 1;

        //Act
        model.RemoveCurrent();

        //Assert
        model.Items.Count.Should().Be(1);
        model.CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void removing_the_only_item_leaves_nothing_selected()
    {
        //Arrange
        ListEditModel model = new ListEditModel();
        model.SetItems(new[] { "one" });

        //Act
        model.RemoveCurrent();

        //Assert
        model.HasSelection.Should().Be(false);
    }

    [Fact]
    public void moving_an_item_keeps_it_selected()
    {
        //Arrange
        ListEditModel model = new ListEditModel();
        model.SetItems(new[] { "one", "two", "three" });
        model.CurrentIndex = 2;

        //Act
        bool moved = model.Move(-1);

        //Assert
        moved.Should().Be(true);
        model.Items[1].Should().Be("three");
        model.CurrentIndex.Should().Be(1);
    }

    [Fact]
    public void an_item_cannot_move_off_the_end_of_the_list()
    {
        //Arrange
        ListEditModel model = new ListEditModel();
        model.SetItems(new[] { "one", "two" });
        model.CurrentIndex = 1;

        //Act
        bool moved = model.Move(1);

        //Assert
        moved.Should().Be(false);
        model.Items[1].Should().Be("two");
    }

    // -------------------------------------------------------- keysequencewidget

    [Fact]
    public void a_modifier_on_its_own_is_not_a_shortcut()
    {
        //Act
        KeySequence recorded = KeySequenceWidget.Record(
            VirtualKey.Control, VirtualKeyModifiers.Control);

        //Assert
        recorded.Should().BeNull();
    }

    [Fact]
    public void a_plain_letter_is_not_a_shortcut()
    {
        //Act
        KeySequence recorded = KeySequenceWidget.Record(
            VirtualKey.A, VirtualKeyModifiers.None);

        //Assert
        recorded.Should().BeNull();
    }

    [Fact]
    public void a_plain_letter_is_a_shortcut_when_modifierless_keys_are_allowed()
    {
        //Act
        KeySequence recorded = KeySequenceWidget.Record(
            VirtualKey.A, VirtualKeyModifiers.None, modifierlessAllowed: true);

        //Assert
        recorded.Should().NotBeNull();
        recorded.Key.Should().Be(VirtualKey.A);
    }

    [Fact]
    public void a_function_key_is_a_shortcut_on_its_own()
    {
        //Act
        KeySequence recorded = KeySequenceWidget.Record(
            VirtualKey.F5, VirtualKeyModifiers.None);

        //Assert
        recorded.Should().NotBeNull();
        recorded.ToString().Should().Be("F5");
    }

    [Fact]
    public void a_letter_with_control_is_a_shortcut()
    {
        //Act
        KeySequence recorded = KeySequenceWidget.Record(
            VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift);

        //Assert
        recorded.ToString().Should().Be("Ctrl+Shift+S");
    }

    [Fact]
    public void a_recorded_shortcut_parses_back_to_itself()
    {
        //Arrange
        KeySequence recorded = KeySequenceWidget.Record(
            VirtualKey.Enter, VirtualKeyModifiers.Menu);

        //Act
        KeySequence parsed = KeySequence.Parse(recorded.ToString());

        //Assert
        parsed.Should().NotBeNull();
        parsed.Equals(recorded).Should().Be(true);
    }

    // ------------------------------------------------------ shortcuts per scheme

    /// <summary>Reads one action's stored shortcut out of a scheme's one key.</summary>
    private static string StoredShortcut(
        SettingsStore store, string scheme, string action)
        => (store.Get<Dictionary<string, string>>(
                ActionCollection.ShortcutFamilyKey(scheme))
            ?? new Dictionary<string, string>(StringComparer.Ordinal))
            .TryGetValue(ActionCollection.ShortcutEntryKey("main", action), out var stored)
                ? stored
                : null;

    [Fact]
    public void a_shortcut_key_names_its_scheme_collection_and_action()
    {
        //Act
        //was previously: one key per action —
        //ShortcutKey("user1", "main", "file_new") == "shortcuts/user1/main/file_new".
        //A scheme is now ONE key holding a dictionary, and the last two
        //segments name the entry inside it (board W13 item 9, route (a)).
        string family = ActionCollection.ShortcutFamilyKey("user1");
        string entry = ActionCollection.ShortcutEntryKey("main", "file_new");

        //Assert
        family.Should().Be("shortcuts/user1");
        entry.Should().Be("main/file_new");
    }

    [Fact]
    public void a_scheme_with_nothing_stored_answers_the_defaults()
    {
        //Arrange
        using var store = new SettingsStore(
            _directory);
        MainActions actions = new MainActions(store);

        //Act
        IReadOnlyList<KeySequence> shortcuts =
            actions.ShortcutsInScheme("user1", "file_new");

        //Assert
        actions.UsesDefaultShortcuts("user1", "file_new").Should().Be(true);
        shortcuts.Count.Should().Be(1);
        shortcuts[0].ToString().Should().Be("Ctrl+N");
    }

    [Fact]
    public void a_shortcut_set_in_one_scheme_leaves_the_others_alone()
    {
        //Arrange
        using var store = new SettingsStore(
            _directory);
        MainActions actions = new MainActions(store);

        //Act
        actions.SetShortcutsInScheme(
            "user1", "file_new", new[] { KeySequence.Parse("Ctrl+Alt+N") });

        //Assert
        actions.ShortcutsInScheme("user1", "file_new")[0]
            .ToString().Should().Be("Ctrl+Alt+N");
        actions.ShortcutsInScheme("default", "file_new")[0]
            .ToString().Should().Be("Ctrl+N");
        StoredShortcut(store, "user1", "file_new").Should().Be("Ctrl+Alt+N");
    }

    [Fact]
    public void setting_a_shortcut_back_to_its_default_forgets_the_override()
    {
        //Arrange
        using var store = new SettingsStore(
            _directory);
        MainActions actions = new MainActions(store);
        actions.SetShortcutsInScheme(
            "user1", "file_new", new[] { KeySequence.Parse("Ctrl+Alt+N") });

        //Act
        actions.SetShortcutsInScheme(
            "user1", "file_new", actions.DefaultShortcuts("file_new"));

        //Assert
        StoredShortcut(store, "user1", "file_new").Should().BeNull();
        actions.UsesDefaultShortcuts("user1", "file_new").Should().Be(true);
    }

    [Fact]
    public void every_shortcut_a_stored_scheme_holds_parses_back()
    {
        //Arrange
        using var store = new SettingsStore(
            _directory);
        MainActions actions = new MainActions(store);

        //Act — every default the collection carries, written and read back.
        List<string> broken = new List<string>();
        foreach (var name in actions.Actions.Keys)
        {
            IReadOnlyList<KeySequence> defaults = actions.DefaultShortcuts(name);
            if (defaults.Count == 0) { continue; }

            foreach (var shortcut in defaults)
            {
                if (KeySequence.Parse(shortcut.ToString()) == null)
                {
                    broken.Add(name + ": " + shortcut.ToString());
                }
            }
        }

        //Assert — a shortcut that does not parse is silently DROPPED, which is
        //how a command loses its key without anyone noticing (board trap 37).
        broken.Count.Should().Be(0);
    }
}
