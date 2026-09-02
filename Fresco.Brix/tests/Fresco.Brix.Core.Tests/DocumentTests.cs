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
using System.Text;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>A scratch directory that cleans itself up.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "frescobrix-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name, string contents = "")
    {
        string path = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>One document: its file, its name, its encoding and its state.</summary>
public class EditorDocumentTests
{
    [Fact]
    public void a_nameless_document_is_called_untitled()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();

        //Act
        EditorDocument document = documents.CreateDocument();

        //Assert
        document.DocumentName().Should().Be("Untitled");
    }

    [Fact]
    public void a_second_nameless_document_is_numbered()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        documents.CreateDocument();

        //Act
        EditorDocument second = documents.CreateDocument();

        //Assert
        second.DocumentName().Should().Be("Untitled (2)");
    }

    [Fact]
    public void a_saved_document_is_called_by_its_file_name()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "c d e\n");

        //Act
        EditorDocument document = EditorDocument.NewFromPath(path);

        //Assert
        document.DocumentName().Should().Be("score.ly");
    }

    [Fact]
    public void loading_normalizes_line_endings()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "one\r\ntwo\rthree\n");

        //Act
        EditorDocument document = EditorDocument.NewFromPath(path);

        //Assert
        document.Text.Should().Be("one\ntwo\nthree\n");
    }

    [Fact]
    public void a_freshly_loaded_document_is_unmodified()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "c d e\n");

        //Act
        EditorDocument document = EditorDocument.NewFromPath(path);

        //Assert
        document.IsModified.Should().BeFalse();
    }

    [Fact]
    public void editing_marks_the_document_modified_and_saving_clears_it()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "c d e\n");
        EditorDocument document = EditorDocument.NewFromPath(path);

        //Act
        document.Document.Insert(0, "% edited\n");

        //Assert
        document.IsModified.Should().BeTrue();

        //Act
        document.Save();

        //Assert
        document.IsModified.Should().BeFalse();
        File.ReadAllText(path).Should().StartWith("% edited");
    }

    [Fact]
    public void saving_under_a_new_name_changes_the_documents_file()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        EditorDocument document = new EditorDocument();
        document.Document.Text = "c d e\n";
        string path = Path.Combine(folder.Path, "new.ly");

        //Act
        document.Save(path);

        //Assert
        document.Path.Should().Be(path);
        document.DocumentName().Should().Be("new.ly");
    }

    [Fact]
    public void a_documents_coding_variable_decides_the_encoding_it_is_written_in()
    {
        //Arrange
        EditorDocument document = new EditorDocument();
        document.Document.Text = "% -*- coding: latin1; -*-\n% café\n";

        //Act
        Encoding encoding = document.ResolvedEncoding();

        //Assert
        encoding.WebName.Should().Be("iso-8859-1");
    }

    [Fact]
    public void an_unknown_coding_variable_does_not_stop_a_save()
    {
        //Arrange
        EditorDocument document = new EditorDocument();
        document.Document.Text = "% -*- coding: nonesuch; -*-\nc\n";

        //Act
        Encoding encoding = document.ResolvedEncoding();

        //Assert
        encoding.WebName.Should().Be("utf-8");
    }

    [Fact]
    public void a_file_written_with_a_byte_order_mark_reads_back_without_one()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = Path.Combine(folder.Path, "bom.ly");
        File.WriteAllText(path, "c d e\n", new UTF8Encoding(true));

        //Act
        string text = EditorDocument.LoadData(path);

        //Assert
        text.Should().Be("c d e\n");
    }

    [Fact]
    public void a_file_that_is_not_valid_utf8_still_opens()
    {
        //Arrange — a lone 0xE9, which is latin-1's é and invalid UTF-8.
        using TempFolder folder = new TempFolder();
        string path = Path.Combine(folder.Path, "latin.ly");
        File.WriteAllBytes(path, new byte[] { (byte)'c', 0xE9, (byte)'\n' });

        //Act
        string text = EditorDocument.LoadData(path);

        //Assert
        text.Should().Be("cé\n");
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 6)]
    [InlineData(2, 3, 8)]
    [InlineData(0, 0, 0)]
    [InlineData(99, 1, 19)]
    public void a_line_and_column_resolve_to_an_offset(int line, int column, int expected)
    {
        //Arrange
        EditorDocument document = new EditorDocument();
        document.Document.Text = "c d e\nf g a\nb c' d'";

        //Act
        int offset = document.OffsetAtPosition(line, column);

        //Assert
        offset.Should().Be(expected);
    }

    [Fact]
    public void a_column_past_the_end_of_a_line_lands_at_the_lines_end()
    {
        //Arrange
        EditorDocument document = new EditorDocument();
        document.Document.Text = "c d e\nf g a\n";

        //Act
        int offset = document.OffsetAtPosition(1, 99);

        //Assert
        offset.Should().Be(5);
    }
}

/// <summary>The open documents and the announcements about them.</summary>
public class DocumentManagerTests
{
    [Fact]
    public void opening_the_same_file_twice_answers_the_same_document()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "c d e\n");
        DocumentManager documents = new DocumentManager();

        //Act
        EditorDocument first = documents.OpenDocument(path);
        EditorDocument second = documents.OpenDocument(path);

        //Assert
        second.Should().BeSameAs(first);
        documents.Documents.Count.Should().Be(1);
    }

    [Fact]
    public void the_first_document_becomes_the_current_one()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();

        //Act
        EditorDocument document = documents.CreateDocument();

        //Assert
        documents.CurrentDocument.Should().BeSameAs(document);
    }

    [Fact]
    public void closing_the_current_document_raises_the_one_that_took_its_place()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument first = documents.CreateDocument();
        EditorDocument second = documents.CreateDocument();
        EditorDocument third = documents.CreateDocument();
        documents.CurrentDocument = second;

        //Act
        documents.CloseDocument(second);

        //Assert
        documents.CurrentDocument.Should().BeSameAs(third);
        documents.Documents.Should().BeEquivalentTo(new[] { first, third });
    }

    [Fact]
    public void closing_the_last_document_leaves_no_current_one()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument only = documents.CreateDocument();

        //Act
        documents.CloseDocument(only);

        //Assert
        documents.CurrentDocument.Should().BeNull();
    }

    [Fact]
    public void closing_a_document_announces_it()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        int announcements = 0;
        documents.DocumentClosed += (_, _) => announcements++;

        //Act
        documents.CloseDocument(document);

        //Assert
        announcements.Should().Be(1);
    }

    [Fact]
    public void saving_a_nameless_document_under_a_name_retires_its_number()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();

        //Act
        document.Save(Path.Combine(folder.Path, "named.ly"));

        //Assert
        document.Number.Should().Be(0);

        //Act — the next nameless document takes number 1 again.
        EditorDocument next = documents.CreateDocument();

        //Assert
        next.DocumentName().Should().Be("Untitled");
    }

    [Fact]
    public void a_modification_is_announced()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        int announcements = 0;
        documents.DocumentModificationChanged += (_, _) => announcements++;

        //Act
        document.Document.Insert(0, "c");

        //Assert
        announcements.Should().BeGreaterThan(0);
    }

    [Fact]
    public void dragging_a_tab_reorders_the_documents()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument first = documents.CreateDocument();
        EditorDocument second = documents.CreateDocument();

        //Act
        documents.MoveDocument(0, 1);

        //Assert
        documents.Documents.ToList().Should().BeEquivalentTo(new[] { second, first });
    }
}

/// <summary>
/// The per-document state every view of a document shares, and the ORDERING
/// trap that decided whether it had a settings store at all.
/// </summary>
public class DocumentEditorStateTests : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly SettingsStore _settings;

    /// <summary>Creates the fixture.</summary>
    public DocumentEditorStateTests()
        => _settings = new SettingsStore(_folder.Path);

    /// <summary>Puts the ambient store back and cleans up.</summary>
    public void Dispose()
    {
        DocumentEditorState.DefaultSettings = null;
        _settings.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public void the_first_caller_decides_and_a_storeless_one_used_to_win()
    {
        //Arrange — this is the defect, pinned: a state is made ONCE, by
        //whichever caller asks first, and most callers want only the token
        //cache and pass no store.
        DocumentEditorState.DefaultSettings = null;
        EditorDocument document = new EditorDocument();

        //Act
        DocumentEditorState first = DocumentEditorState.For(document);
        DocumentEditorState later = DocumentEditorState.For(document, _settings);

        //Assert — the same object, and it has no meta-info, so the caret was
        //remembered in memory and written nowhere.
        ReferenceEquals(first, later).Should().Be(true);
        later.MetaInfo.Should().BeNull();
    }

    [Fact]
    public void the_ambient_store_makes_the_answer_the_same_whoever_asks_first()
    {
        //Arrange — what the application sets once, before any document exists.
        DocumentEditorState.DefaultSettings = _settings;
        EditorDocument document = new EditorDocument();

        //Act — the storeless caller wins the race again.
        DocumentEditorState state = DocumentEditorState.For(document);

        //Assert
        state.MetaInfo.Should().NotBeNull();
        state.Settings.Should().BeSameAs(_settings);
    }

    [Fact]
    public void a_named_store_still_wins_over_the_ambient_one()
    {
        //Arrange
        using TempFolder other = new TempFolder();
        using SettingsStore named = new SettingsStore(other.Path);
        DocumentEditorState.DefaultSettings = _settings;
        EditorDocument document = new EditorDocument();

        //Act
        DocumentEditorState state = DocumentEditorState.For(document, named);

        //Assert
        state.Settings.Should().BeSameAs(named);
    }
}

/// <summary>What the app remembers about a document between sessions.</summary>
public class MetaInfoTests : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();
    private readonly SettingsStore _settings;

    public MetaInfoTests()
        => _settings = new SettingsStore(_folder.Path);

    public void Dispose()
    {
        _settings.Dispose();
        _folder.Dispose();
    }

    /// <summary>What the one meta-info settings key holds.</summary>
    private Dictionary<string, StoredMetaInfo> StoredMetaInfoEntries()
        => _settings.Get<Dictionary<string, StoredMetaInfo>>(MetaInfo.SettingsKey)
            ?? new Dictionary<string, StoredMetaInfo>(StringComparer.Ordinal);

    [Fact]
    public void a_declared_value_starts_at_its_default()
    {
        //Arrange
        MetaInfo.Define("test_position", "0");

        //Act
        MetaInfo info = new MetaInfo(_settings, "/tmp/score.ly");

        //Assert
        info.Get("test_position").Should().Be("0");
    }

    [Fact]
    public void a_saved_value_comes_back_for_the_same_document()
    {
        //Arrange
        MetaInfo.Define("test_position", "0");
        MetaInfo info = new MetaInfo(_settings, "/tmp/score.ly");
        info.SetInt("test_position", 42);

        //Act
        info.Save();
        MetaInfo reopened = new MetaInfo(_settings, "/tmp/score.ly");

        //Assert
        reopened.GetInt("test_position").Should().Be(42);
    }

    [Fact]
    public void another_document_gets_its_own_values()
    {
        //Arrange
        MetaInfo.Define("test_position", "0");
        MetaInfo info = new MetaInfo(_settings, "/tmp/one.ly");
        info.SetInt("test_position", 42);
        info.Save();

        //Act
        MetaInfo other = new MetaInfo(_settings, "/tmp/two.ly");

        //Assert
        other.GetInt("test_position").Should().Be(0);
    }

    [Fact]
    public void a_value_left_at_its_default_is_not_stored()
    {
        //Arrange
        MetaInfo.Define("test_folding", "0");
        MetaInfo info = new MetaInfo(_settings, "/tmp/score.ly");

        //Act
        info.Save();

        //Assert — only the timestamp is written.
        //was previously: KeysWithPrefix("metainfo/") — every document's entry
        //is now one entry in ONE key (board W13 item 9, route (a)).
        StoredMetaInfoEntries().Values
            .Where(e => e.Values != null && e.Values.ContainsKey("test_folding"))
            .Should().BeEmpty();
    }

    [Fact]
    public void turning_the_memory_off_gives_every_document_the_defaults()
    {
        //Arrange
        MetaInfo.Define("test_position", "0");
        MetaInfo info = new MetaInfo(_settings, "/tmp/score.ly");
        info.SetInt("test_position", 42);
        info.Save();

        //Act
        _settings.SetBool("metainfo", false);
        MetaInfo reopened = new MetaInfo(_settings, "/tmp/score.ly");

        //Assert
        reopened.GetInt("test_position").Should().Be(0);
    }

    [Fact]
    public void an_entry_untouched_for_a_month_is_pruned()
    {
        //Arrange
        MetaInfo.Define("test_position", "0");
        MetaInfo info = new MetaInfo(_settings, "/tmp/old.ly");
        info.SetInt("test_position", 42);
        info.Save();

        //Backdate the stamp by two months.
        Dictionary<string, StoredMetaInfo> stored = StoredMetaInfoEntries();
        stored.Values.First().Time
            = DateTimeOffset.UtcNow.AddDays(-62).ToUnixTimeSeconds();
        _settings.Set(MetaInfo.SettingsKey, stored);

        //Act
        int pruned = MetaInfo.Prune(_settings);

        //Assert
        pruned.Should().Be(1);
        StoredMetaInfoEntries().Should().BeEmpty();
    }

    [Fact]
    public void a_recent_entry_survives_pruning()
    {
        //Arrange
        MetaInfo.Define("test_position", "0");
        MetaInfo info = new MetaInfo(_settings, "/tmp/fresh.ly");
        info.SetInt("test_position", 7);
        info.Save();

        //Act
        int pruned = MetaInfo.Prune(_settings);

        //Assert
        pruned.Should().Be(0);
        new MetaInfo(_settings, "/tmp/fresh.ly").GetInt("test_position").Should().Be(7);
    }
}

/// <summary>Keeping a copy of a file before overwriting it.</summary>
public class BackupTests : IDisposable
{
    private readonly TempFolder _folder = new TempFolder();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void the_default_backup_name_is_the_file_with_a_tilde()
    {
        //Arrange
        Backup backup = new Backup();

        //Act
        string name = backup.BackupName("/tmp/score.ly");

        //Assert
        name.Should().Be("/tmp/score.ly~");
    }

    [Fact]
    public void a_backup_is_a_copy_of_what_was_there()
    {
        //Arrange
        string path = _folder.File("score.ly", "original\n");
        Backup backup = new Backup();

        //Act
        bool made = backup.Create(path);

        //Assert
        made.Should().BeTrue();
        File.ReadAllText(path + "~").Should().Be("original\n");
    }

    [Fact]
    public void the_backup_is_removed_again_after_a_good_save()
    {
        //Arrange
        string path = _folder.File("score.ly", "original\n");
        Backup backup = new Backup();
        backup.Create(path);

        //Act
        backup.Remove(path);

        //Assert
        File.Exists(path + "~").Should().BeFalse();
    }

    [Fact]
    public void backups_are_kept_when_the_user_asks_for_that()
    {
        //Arrange
        using SettingsStore settings
            = new SettingsStore(_folder.Path);
        settings.SetBool(Backup.KeepSettingKey, true);
        string path = _folder.File("score.ly", "original\n");
        Backup backup = new Backup(settings);
        backup.Create(path);

        //Act
        backup.Remove(path);

        //Assert
        File.Exists(path + "~").Should().BeTrue();
    }

    [Fact]
    public void backing_up_a_file_that_is_not_there_simply_fails()
    {
        //Arrange
        Backup backup = new Backup();

        //Act
        bool made = backup.Create(Path.Combine(_folder.Path, "missing.ly"));

        //Assert
        made.Should().BeFalse();
    }

    [Fact]
    public void a_scheme_that_does_not_name_the_file_is_ignored()
    {
        //Arrange
        using SettingsStore settings
            = new SettingsStore(_folder.Path);
        settings.SetString(Backup.SchemeSettingKey, "backup");
        Backup backup = new Backup(settings);

        //Act
        string name = backup.BackupName("/tmp/score.ly");

        //Assert
        name.Should().Be("/tmp/score.ly~");
    }

    [Fact]
    public void a_custom_scheme_is_honoured()
    {
        //Arrange
        using SettingsStore settings
            = new SettingsStore(_folder.Path);
        settings.SetString(Backup.SchemeSettingKey, "FILE.bak");
        Backup backup = new Backup(settings);

        //Act
        string name = backup.BackupName("/tmp/score.ly");

        //Assert
        name.Should().Be("/tmp/score.ly.bak");
    }
}
