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
using System.Linq;
using Xunit;
using CodeBrix.LilyPort;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The recent-documents list against upstream's rules (recentfiles.py): adding
/// an entry moves it to the front, the list is capped at ten, entries that no
/// longer resolve are dropped when the list is read back, and the whole thing
/// survives the settings store.
/// </summary>
public class RecentFilesTests : IDisposable
{
    private readonly string _directory;
    private readonly SettingsStore _settings;

    /// <summary>Creates the fixture over a scratch directory and store.</summary>
    public RecentFilesTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _settings = new SettingsStore(_directory);
    }

    /// <summary>Closes the store and removes the scratch directory.</summary>
    public void Dispose()
    {
        _settings.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string MakeFile(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"\n");
        return path;
    }

    [Fact]
    public void the_newest_entry_comes_first()
    {
        //Arrange
        var recent = new RecentFiles(_settings);
        var first = MakeFile("one.ly");
        var second = MakeFile("two.ly");

        //Act
        recent.Add(first);
        recent.Add(second);

        //Assert
        recent.Paths().Should().Equal(new[] { second, first });
    }

    [Fact]
    public void re_adding_an_entry_moves_it_to_the_front_without_duplicating()
    {
        //Arrange
        var recent = new RecentFiles(_settings);
        var first = MakeFile("one.ly");
        var second = MakeFile("two.ly");
        recent.Add(first);
        recent.Add(second);

        //Act
        recent.Add(first);

        //Assert
        recent.Paths().Should().Equal(new[] { first, second });
    }

    [Fact]
    public void the_list_stops_at_ten_entries()
    {
        //Arrange
        var recent = new RecentFiles(_settings);
        var paths = Enumerable.Range(0, 13)
            .Select(i => MakeFile("score" + i + ".ly")).ToList();

        //Act
        foreach (var path in paths)
        {
            recent.Add(path);
        }

        //Assert
        recent.Paths().Count.Should().Be(RecentFiles.MaxLength);
        recent.Paths()[0].Should().Be(paths[12]);
        recent.Paths().Should().NotContain(paths[0]);
    }

    [Fact]
    public void the_list_survives_the_settings_store()
    {
        //Arrange
        var path = MakeFile("kept.ly");
        new RecentFiles(_settings).Add(path);

        //Act
        var reloaded = new RecentFiles(_settings).Paths();

        //Assert
        reloaded.Should().Equal(new[] { path });
    }

    [Fact]
    public void an_entry_that_no_longer_resolves_is_dropped_on_load()
    {
        //Arrange
        var kept = MakeFile("kept.ly");
        var vanishing = MakeFile("gone.ly");
        var recent = new RecentFiles(_settings);
        recent.Add(kept);
        recent.Add(vanishing);

        //Act
        File.Delete(vanishing);
        var reloaded = new RecentFiles(_settings).Paths();

        //Assert
        reloaded.Should().Equal(new[] { kept });
    }

    [Fact]
    public void removing_an_entry_takes_it_out_of_the_store()
    {
        //Arrange
        var first = MakeFile("one.ly");
        var second = MakeFile("two.ly");
        var recent = new RecentFiles(_settings);
        recent.Add(first);
        recent.Add(second);

        //Act
        recent.Remove(second);

        //Assert
        recent.Paths().Should().Equal(new[] { first });
        new RecentFiles(_settings).Paths().Should().Equal(new[] { first });
    }

    [Fact]
    public void a_relative_path_is_stored_as_a_full_path()
    {
        //Arrange
        var recent = new RecentFiles(_settings);
        MakeFile("relative.ly");
        var relative = Path.Combine(_directory, ".", "relative.ly");

        //Act
        recent.Add(relative);

        //Assert
        recent.Paths()[0].Should().Be(Path.Combine(_directory, "relative.ly"));
    }
}
