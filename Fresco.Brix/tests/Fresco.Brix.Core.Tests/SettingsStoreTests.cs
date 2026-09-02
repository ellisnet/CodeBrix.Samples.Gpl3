// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Editor;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The application settings store — a thin facade over the
/// <c>CodeBrix.Platform.AppSettings</c> add-in: values survive a reopen of the
/// same store, typed accessors round-trip, a null write removes the key, and a
/// key FAMILY is one JSON-valued key holding a list or a dictionary.
/// </summary>
/// <remarks>The add-in's own file lifecycle — backups, retention, corrupt-file
/// recovery, import and export — is the package's and is not re-tested here.
/// Every test opens a store in a scratch folder of its own, so none of them can
/// reach the real one.</remarks>
public class SettingsStoreTests : IDisposable
{
    private readonly string _directory;

    /// <summary>Creates the fixture over a scratch database file.</summary>
    public SettingsStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-tests-" + Guid.NewGuid().ToString("N"));
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

    private string Path_ => _directory;

    [Fact]
    public void a_string_setting_survives_a_reopen()
    {
        //Arrange
        using (var store = new SettingsStore(Path_))
        {
            store.SetString("greeting", "hello");
        }

        //Act
        using var reopened = new SettingsStore(Path_);
        var value = reopened.GetString("greeting");

        //Assert
        value.Should().Be("hello");
    }

    [Fact]
    public void an_unset_key_answers_the_default()
    {
        //Arrange
        using var store = new SettingsStore(Path_);

        //Act
        var value = store.GetString("nothing-here", "fallback");

        //Assert
        value.Should().Be("fallback");
    }

    [Fact]
    public void typed_settings_round_trip()
    {
        //Arrange
        using var store = new SettingsStore(Path_);

        //Act
        store.SetBool("wrap", true);
        store.SetInt("tab-width", 4);

        //Assert
        store.GetBool("wrap").Should().BeTrue();
        store.GetInt("tab-width").Should().Be(4);
        store.GetBool("missing", true).Should().BeTrue();
        store.GetInt("missing", 7).Should().Be(7);
    }

    [Fact]
    public void writing_null_removes_the_key()
    {
        //Arrange
        using var store = new SettingsStore(Path_);
        store.SetString("temporary", "value");

        //Act
        store.SetString("temporary", null);

        //Assert
        store.GetString("temporary").Should().BeNull();
    }

    [Fact]
    public void a_value_with_an_apostrophe_round_trips()
    {
        //Arrange
        using var store = new SettingsStore(Path_);

        //Act
        store.SetString("path", "/home/jeremy/it's here/score.ly");

        //Assert
        store.GetString("path").Should().Be("/home/jeremy/it's here/score.ly");
    }

    // ------------------------------------------------ the add-in underneath

    [Fact]
    public void the_store_is_the_addins_settings_file_in_the_folder_it_was_given()
    {
        //Arrange, Act
        using var store = new SettingsStore(Path_);

        //Assert
        store.DirectoryPath.Should().Be(Path_);
        store.DatabaseFilePath.Should().Be(
            System.IO.Path.Combine(Path_, "settings.sqlite"));
        File.Exists(store.DatabaseFilePath).Should().BeTrue();
        store.WasCreatedFresh.Should().BeTrue();
    }

    [Fact]
    public void the_default_location_is_the_addins_own()
    {
        //Act — a pure path computation; nothing is opened or created.
        //was previously: <ApplicationData>/Fresco.Brix/settings.sqlite, which
        //the store wrote itself. That file is orphaned in place, never read.
        string directory = SettingsStore.DefaultDirectory();

        //Assert
        directory.Should().Be(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodeBrix",
            SettingsStore.AppName,
            "settings"));
        SettingsStore.DefaultPath().Should().Be(
            System.IO.Path.Combine(directory, "settings.sqlite"));
    }

    [Fact]
    public void an_unwritten_key_holds_no_value()
    {
        //Arrange
        using var store = new SettingsStore(Path_);

        //Act
        store.SetString("written", "yes");

        //Assert
        store.HasValue("written").Should().BeTrue();
        store.HasValue("never-written").Should().BeFalse();
        store.HasValue(null).Should().BeFalse();
    }

    [Fact]
    public void a_null_or_empty_key_is_tolerated_rather_than_thrown_at()
    {
        //Arrange — the add-in refuses an empty key; the facade answers the
        //default instead, which is what the store it replaced did.
        using var store = new SettingsStore(Path_);

        //Act
        store.SetString(null, "ignored");
        store.SetString(string.Empty, "ignored");

        //Assert
        store.GetString(null, "fallback").Should().Be("fallback");
        store.GetString(string.Empty, "fallback").Should().Be("fallback");
    }

    // -------------------------------------------------------- key families

    [Fact]
    public void a_list_family_is_one_key_and_survives_a_reopen()
    {
        //Arrange
        using (var store = new SettingsStore(Path_))
        {
            store.Set("family/list", new List<string> { "one", "two" });
        }

        //Act
        using var reopened = new SettingsStore(Path_);

        //Assert
        reopened.Get<List<string>>("family/list").Should().Equal("one", "two");
        reopened.Get<List<string>>("family/missing").Should().BeNull();
    }

    [Fact]
    public void a_dictionary_family_is_one_key_and_survives_a_reopen()
    {
        //Arrange
        using (var store = new SettingsStore(Path_))
        {
            store.Set("family/map", new Dictionary<string, string>
            {
                ["main/file_new"] = "Ctrl+Alt+N",
                ["main/file_open"] = "Ctrl+Alt+O",
            });
        }

        //Act
        using var reopened = new SettingsStore(Path_);
        Dictionary<string, string> read =
            reopened.Get<Dictionary<string, string>>("family/map");

        //Assert
        read.Count.Should().Be(2);
        read["main/file_new"].Should().Be("Ctrl+Alt+N");
    }

    [Fact]
    public void a_family_key_read_as_a_scalar_answers_the_default()
    {
        //Arrange — the add-in answers the DEFAULT rather than throwing when the
        //stored JSON is not of the type asked for. That is exactly why the
        //scalar accessors keep the store's historical text encoding and never
        //share a key with a family.
        using var store = new SettingsStore(Path_);
        store.Set("family/list", new List<string> { "one" });

        //Act, Assert
        store.GetString("family/list", "fallback").Should().Be("fallback");
        store.HasValue("family/list").Should().BeTrue();
    }

    [Fact]
    public void the_recent_files_family_is_one_json_key()
    {
        //Arrange
        using var store = new SettingsStore(Path_);
        string document = System.IO.Path.Combine(Path_, "score.ly");
        File.WriteAllText(document, "{ c }");
        RecentFiles recent = new RecentFiles(store);

        //Act
        recent.Add(document);

        //Assert
        store.Get<List<string>>(RecentFiles.SettingKey).Should().Equal(document);
        new RecentFiles(store).Paths().Should().Equal(document);
    }

    [Fact]
    public void forgetting_a_shortcut_scheme_drops_its_one_key()
    {
        //Arrange
        using var store = new SettingsStore(Path_);
        store.Set(ActionCollection.ShortcutFamilyKey("user1"),
            new Dictionary<string, string> { ["main/file_new"] = "Ctrl+Alt+N" });

        //Act
        ActionCollection.ForgetScheme(store, "user1");

        //Assert
        store.HasValue(ActionCollection.ShortcutFamilyKey("user1")).Should().BeFalse();
    }

    [Fact]
    public void forgetting_a_fonts_and_colours_scheme_drops_its_one_key()
    {
        //Arrange
        using var store = new SettingsStore(Path_);
        TextFormatData data = new TextFormatData("user1", store, "editor");
        data.Save(store);
        store.HasValue(TextFormatData.SchemeKey("editor", "user1")).Should().BeTrue();

        //Act
        TextFormatData.ForgetScheme(store, "editor", "user1");

        //Assert
        store.HasValue(TextFormatData.SchemeKey("editor", "user1")).Should().BeFalse();
    }
}
