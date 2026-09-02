// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The per-document watchers and the "files changed on disk" service share
/// process-wide state — <c>DocumentWatcher</c> is a plugin whose instances are
/// walked as a set — so, as with the MIDI note state (board trap 55), the tests
/// over them do not run beside each other.
/// </summary>
[CollectionDefinition(DocumentWatchCollection.Name, DisableParallelization = true)]
public class DocumentWatchCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "document watching";
}

/// <summary>
/// The document watcher against upstream's <c>documentwatcher.py</c>: the flag
/// that says a change was seen, what counts as deleted, which files are
/// watched as documents come and go, and standing aside for our own saves.
/// </summary>
[Collection(DocumentWatchCollection.Name)]
public class DocumentWatcherTests : IDisposable
{
    private readonly string _directory;
    private readonly DocumentManager _documents = new DocumentManager();
    private readonly DocumentWatchService _service;

    /// <summary>Creates the fixture over a scratch directory.</summary>
    public DocumentWatcherTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DocumentWatcher.Reset();
        _service = new DocumentWatchService(_documents);
    }

    /// <summary>Stops the watcher and removes the scratch directory.</summary>
    public void Dispose()
    {
        _service.Dispose();
        DocumentWatcher.Reset();
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void a_new_watcher_has_seen_nothing()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4");

        //Act
        DocumentWatcher watcher = DocumentWatcher.For(document);

        //Assert
        watcher.Changed.Should().BeFalse();
        watcher.IsDeleted().Should().BeFalse();
    }

    [Fact]
    public void a_file_is_deleted_only_when_a_change_was_seen_and_it_is_gone()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4");
        DocumentWatcher watcher = DocumentWatcher.For(document);

        //Act
        File.Delete(document.Path);

        //Assert — upstream asks for the flag FIRST; a file that vanished
        //without the watcher noticing is not "deleted" yet.
        watcher.IsDeleted().Should().BeFalse();
        watcher.Changed = true;
        watcher.IsDeleted().Should().BeTrue();
    }

    [Fact]
    public void a_document_with_no_file_is_never_deleted()
    {
        //Arrange
        EditorDocument document = _documents.CreateDocument();
        DocumentWatcher watcher = DocumentWatcher.For(document);

        //Act
        watcher.Changed = true;

        //Assert
        watcher.IsDeleted().Should().BeFalse();
    }

    [Fact]
    public void starting_watches_every_open_document()
    {
        //Arrange
        EditorDocument first = Open("a.ly", "c4");
        EditorDocument second = Open("b.ly", "d4");

        //Act
        _service.Start();

        //Assert
        _service.IsRunning.Should().BeTrue();
        _service.Files().Should().Contain(first.Path);
        _service.Files().Should().Contain(second.Path);
    }

    [Fact]
    public void a_document_opened_afterwards_is_watched_too()
    {
        //Arrange
        _service.Start();

        //Act
        EditorDocument document = Open("later.ly", "e4");

        //Assert
        _service.Files().Should().Contain(document.Path);
    }

    [Fact]
    public void a_document_that_closes_is_no_longer_watched()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4");
        _service.Start();

        //Act
        _documents.CloseDocument(document);

        //Assert
        _service.Files().Should().NotContain(document.Path);
    }

    [Fact]
    public void stopping_lets_every_file_go()
    {
        //Arrange
        Open("a.ly", "c4");
        _service.Start();

        //Act
        _service.Stop();

        //Assert
        _service.IsRunning.Should().BeFalse();
        _service.Files().Should().BeEmpty();
    }

    [Fact]
    public void a_change_is_announced_once_until_something_clears_the_flag()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4");
        _service.Start();
        List<EditorDocument> announced = new List<EditorDocument>();
        _service.DocumentChangedOnDisk += (_, e) => announced.Add(e.Document);

        //Act — the platform's own event is simulated, because a real inotify
        //notification arrives when the operating system feels like it and a
        //test that waits for one is a test that sometimes fails.
        _service.FileChanged(document.Path);
        _service.FileChanged(document.Path);

        //Assert
        announced.Should().ContainSingle();
        DocumentWatcher.For(document).Changed.Should().BeTrue();
    }

    [Fact]
    public void a_file_nobody_has_open_is_not_announced()
    {
        //Arrange
        Open("a.ly", "c4");
        _service.Start();
        List<EditorDocument> announced = new List<EditorDocument>();
        _service.DocumentChangedOnDisk += (_, e) => announced.Add(e.Document);

        //Act
        _service.FileChanged(Path.Combine(_directory, "somebody-elses.ly"));

        //Assert
        announced.Should().BeEmpty();
    }

    [Fact]
    public void the_announcement_is_made_on_the_windows_own_thread()
    {
        //Arrange — board trap 22: the platform raises its events on its own
        //thread, and what they lead to is a window.
        EditorDocument document = Open("a.ly", "c4");
        _service.Start();
        List<Action> posted = new List<Action>();
        _service.ToUiThread = work => posted.Add(work);
        int announced = 0;
        _service.DocumentChangedOnDisk += (_, _) => announced++;

        //Act
        _service.FileChanged(document.Path);

        //Assert
        announced.Should().Be(0);
        posted.Should().ContainSingle();
        posted[0]();
        announced.Should().Be(1);
    }

    [Fact]
    public void loading_a_document_clears_the_flag()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4");
        DocumentWatcher.For(document).Changed = true;

        //Act
        File.WriteAllText(document.Path, "d4");
        document.Load(keepUndo: true);

        //Assert
        DocumentWatcher.For(document).Changed.Should().BeFalse();
    }

    [Fact]
    public void saving_a_document_clears_the_flag()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4");
        DocumentWatcher.For(document).Changed = true;

        //Act
        document.Save();

        //Assert
        DocumentWatcher.For(document).Changed.Should().BeFalse();
    }

    [Fact]
    public void the_watcher_stands_aside_for_our_own_save_and_takes_the_file_up_again()
    {
        //Arrange — upstream's whileSaving() context manager.
        EditorDocument document = Open("a.ly", "c4");
        _service.Start();
        int watchedDuringSave = -1;
        document.Saving += (_, _) => watchedDuringSave = _service.Files().Count;

        //Act
        document.Save();

        //Assert
        watchedDuringSave.Should().Be(0);
        _service.Files().Should().Contain(document.Path);
    }

    [Fact]
    public void the_resumption_runs_even_when_the_write_fails()
    {
        //Arrange — the `finally' half of upstream's context manager. A
        //DIRECTORY standing where the file should be makes the write throw.
        EditorDocument document = Open("a.ly", "c4");
        string blocked = Path.Combine(_directory, "blocked.ly");
        Directory.CreateDirectory(blocked);

        bool resumed = false;
        document.Saving += (_, e) => e.ResumeAfterSave(() => resumed = true);

        //Act
        try
        {
            document.Save(blocked);
        }
        catch (Exception)
        {
            //Which exception the platform raises for "that is a directory" is
            //not the point; that the resumption ran anyway is.
        }

        //Assert
        resumed.Should().BeTrue();
    }

    private EditorDocument Open(string name, string text)
        => _documents.OpenDocument(WriteFile(Path.Combine(_directory, name), text));

    private static string WriteFile(string path, string text)
    {
        File.WriteAllText(path, text);
        return path;
    }
}
