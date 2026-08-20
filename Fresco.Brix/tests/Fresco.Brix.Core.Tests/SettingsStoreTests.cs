// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.IO;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The application settings store: values survive a reopen of the same
/// database file, typed accessors round-trip, and a null write removes the
/// key.
/// </summary>
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

    private string Path_ => System.IO.Path.Combine(_directory, "settings.sqlite");

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
}
