// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The "files changed on disk" service against upstream's
/// <c>externalchanges/__init__.py</c>: which documents really changed, the
/// preference, and the reload/save halves of its window.
/// </summary>
[Collection(DocumentWatchCollection.Name)]
public class ExternalChangesTests : IDisposable
{
    private readonly string _directory;
    private readonly DocumentManager _documents = new DocumentManager();
    private readonly DocumentWatchService _watcher;
    private readonly ExternalChanges _service;
    private readonly SettingsStore _settings;

    /// <summary>Creates the fixture over a scratch directory and store.</summary>
    public ExternalChangesTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DocumentWatcher.Reset();
        _settings = new SettingsStore(_directory);
        _watcher = new DocumentWatchService(_documents);
        _service = new ExternalChanges(_documents, _watcher, _settings);
    }

    /// <summary>Closes everything and removes the scratch directory.</summary>
    public void Dispose()
    {
        _service.Dispose();
        _watcher.Dispose();
        _settings.Dispose();
        DocumentWatcher.Reset();
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void watching_is_enabled_by_default()
    {
        //Assert — upstream's QSettings().value("externalchanges/enabled", True, bool).
        _service.Enabled.Should().BeTrue();
    }

    [Fact]
    public void turning_watching_off_writes_the_setting_and_stops_the_watcher()
    {
        //Arrange
        _service.Setup();
        _watcher.IsRunning.Should().BeTrue();

        //Act
        _service.SetEnabled(false);

        //Assert
        _service.Enabled.Should().BeFalse();
        _settings.GetBool(ExternalChanges.EnabledKey, true).Should().BeFalse();
        _watcher.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void turning_watching_back_on_starts_it_again()
    {
        //Arrange
        _service.SetEnabled(false);

        //Act
        _service.SetEnabled(true);

        //Assert
        _watcher.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void a_file_that_is_byte_for_byte_what_the_document_holds_did_not_change()
    {
        //Arrange — upstream's own rule: the file system says a file was
        //written; the bytes say nothing happened.
        EditorDocument document = Open("a.ly", "c4 d e f\n");
        _service.Setup();
        _watcher.FileChanged(document.Path);
        DocumentWatcher.For(document).Changed.Should().BeTrue();

        //Act
        IReadOnlyList<EditorDocument> changed = _service.ChangedDocuments();

        //Assert
        changed.Should().BeEmpty();
        DocumentWatcher.For(document).Changed.Should().BeFalse();
    }

    [Fact]
    public void a_file_whose_contents_differ_really_changed()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4 d e f\n");
        _service.Setup();
        File.WriteAllText(document.Path, "g4 a b c\n");

        //Act
        _watcher.FileChanged(document.Path);
        IReadOnlyList<EditorDocument> changed = _service.ChangedDocuments();

        //Assert
        changed.Should().ContainSingle();
        changed[0].Should().BeSameAs(document);
    }

    [Fact]
    public void a_document_with_unsaved_edits_is_reported_without_reading_the_file()
    {
        //Arrange — upstream skips the comparison for a modified document: the
        //user has changes of their own, so the file differing is the whole
        //point.
        EditorDocument document = Open("a.ly", "c4\n");
        document.Document.Text = "c4 d\n";
        document.IsModified.Should().BeTrue();
        _service.Setup();

        //Act
        _watcher.FileChanged(document.Path);
        IReadOnlyList<EditorDocument> changed = _service.ChangedDocuments();

        //Assert
        changed.Should().ContainSingle();
    }

    [Fact]
    public void a_deleted_file_is_reported_and_is_known_to_be_deleted()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4\n");
        _service.Setup();
        File.Delete(document.Path);

        //Act
        _watcher.FileChanged(document.Path);
        IReadOnlyList<EditorDocument> changed = _service.ChangedDocuments();

        //Assert
        changed.Should().ContainSingle();
        DocumentWatcher.For(document).IsDeleted().Should().BeTrue();
    }

    [Fact]
    public void the_window_is_shown_only_when_something_changed()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4\n");
        _service.Setup();
        List<IReadOnlyList<EditorDocument>> shown
            = new List<IReadOnlyList<EditorDocument>>();
        _service.Display = documents => shown.Add(documents);

        //Act
        _service.CheckChangedDocuments();
        File.WriteAllText(document.Path, "d4\n");
        _watcher.FileChanged(document.Path);
        _service.CheckChangedDocuments();

        //Assert
        shown.Should().ContainSingle();
        shown[0].Should().ContainSingle();
    }

    [Fact]
    public void the_command_shows_the_window_even_with_nothing_to_report()
    {
        //Arrange — upstream's displayChangedDocuments(), which File > Check for
        //External Changes calls.
        Open("a.ly", "c4\n");
        _service.Setup();
        List<IReadOnlyList<EditorDocument>> shown
            = new List<IReadOnlyList<EditorDocument>>();
        _service.Display = documents => shown.Add(documents);

        //Act
        _service.DisplayChangedDocuments();

        //Assert
        shown.Should().ContainSingle();
        shown[0].Should().BeEmpty();
    }

    [Fact]
    public void reloading_takes_the_file_and_leaves_the_previous_text_one_undo_away()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4\n");
        File.WriteAllText(document.Path, "g4\n");

        //Act
        IReadOnlyList<(EditorDocument Document, string Reason)> failures
            = ChangedDocumentsDialog.ReloadDocuments(new[] { document });

        //Assert
        failures.Should().BeEmpty();
        document.Text.Should().Be("g4\n");
        document.Document.UndoStack.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void reloading_a_file_that_is_gone_is_reported_rather_than_thrown()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4\n");
        File.Delete(document.Path);

        //Act
        IReadOnlyList<(EditorDocument Document, string Reason)> failures
            = ChangedDocumentsDialog.ReloadDocuments(new[] { document });

        //Assert
        failures.Should().ContainSingle();
        failures[0].Document.Should().BeSameAs(document);
        failures[0].Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void saving_writes_the_document_over_the_other_programs_changes()
    {
        //Arrange
        EditorDocument document = Open("a.ly", "c4\n");
        File.WriteAllText(document.Path, "somebody else\n");

        //Act
        IReadOnlyList<(EditorDocument Document, string Reason)> failures
            = ChangedDocumentsDialog.SaveDocuments(new[] { document });

        //Assert
        failures.Should().BeEmpty();
        File.ReadAllText(document.Path).Should().Be("c4\n");
    }

    [Fact]
    public void the_home_folder_is_shortened_the_way_upstream_shortens_it()
    {
        //Arrange — the list groups by folder, and upstream homifies the
        //folder's name before showing it.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        //Assert
        PathUtil.Homify(home).Should().Be("~");
        PathUtil.Homify(Path.Combine(home, "scores")).Should().Be(
            "~" + Path.DirectorySeparatorChar + "scores");
        PathUtil.Homify("/etc").Should().Be("/etc");
        PathUtil.Homify(null).Should().BeNull();
    }

    private EditorDocument Open(string name, string text)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return _documents.OpenDocument(path);
    }
}
