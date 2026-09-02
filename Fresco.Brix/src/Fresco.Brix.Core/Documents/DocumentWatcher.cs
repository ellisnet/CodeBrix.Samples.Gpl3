// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/documentwatcher.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Remembers whether a change was seen on disk for one document.
/// </summary>
/// <remarks>
/// Upstream's <c>DocumentWatcher</c> plugin, and the same rule: loading,
/// saving or renaming a document clears the flag.
/// </remarks>
public sealed class DocumentWatcher : Plugin<EditorDocument, DocumentWatcher>
{
    private DocumentWatcher(EditorDocument document)
        : base(document)
    {
    }

    /// <summary>Gets or sets whether a change has been seen on disk.</summary>
    public bool Changed { get; set; }

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the watcher for a document, creating it on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The watcher.</returns>
    public static DocumentWatcher For(EditorDocument document)
        => Instance(document, owner => new DocumentWatcher(owner));

    /// <summary>Gets the watchers that exist, for the documents still open.</summary>
    /// <returns>The watchers.</returns>
    /// <remarks>Upstream's <c>DocumentWatcher.instances()</c>.</remarks>
    public static IReadOnlyList<DocumentWatcher> Instances()
        => LiveInstances().Where(w => w.Owner != null).ToList();

    /// <summary>Forgets every watcher — the seam the tests reset with.</summary>
    internal static void Reset() => ClearInstances();

    /// <summary>
    /// Answers whether something changed, the document has a file, and that
    /// file is no longer there.
    /// </summary>
    /// <returns>Whether the file was deleted.</returns>
    public bool IsDeleted()
    {
        if (!Changed) { return false; }

        string fileName = Document?.Path;
        return !string.IsNullOrEmpty(fileName) && !File.Exists(fileName);
    }
}

/// <summary>
/// Watches the files of the open documents and says when one of them changes
/// under the application's feet.
/// </summary>
/// <remarks>
/// <para>
/// Upstream keeps ONE <c>QFileSystemWatcher</c> in a module-level global and
/// hands it individual file names. .NET's <see cref="FileSystemWatcher"/>
/// watches a DIRECTORY, so this keeps one watcher per directory and filters by
/// the file names it was asked for — which is the same set of files, said the
/// way the platform says it.
/// </para>
/// <para>
/// ⚠ The platform raises its events on ITS OWN thread (board trap 22). Set
/// <see cref="ToUiThread"/> and every announcement is made on the window's
/// thread instead; a test that leaves it null gets the announcement where the
/// change was seen, which is what makes the service testable without a window.
/// </para>
/// </remarks>
public sealed class DocumentWatchService : IDisposable
{
    private readonly DocumentManager _documents;
    private readonly Dictionary<string, FileSystemWatcher> _watchers;
    private readonly HashSet<string> _files;
    private readonly object _gate = new object();

    private bool _running;

    /// <summary>Creates the service over the open documents.</summary>
    /// <param name="documents">The open documents.</param>
    public DocumentWatchService(DocumentManager documents)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));

        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        _watchers = new Dictionary<string, FileSystemWatcher>(comparer);
        _files = new HashSet<string>(comparer);

        //Upstream's "always-on connections", made when the module is imported:
        //loading, saving or renaming a document means the application knows
        //what is on disk, whatever the watcher saw before.
        _documents.DocumentLoaded += (_, e) => Unchange(e.Document);
        _documents.DocumentSaved += (_, e) => Unchange(e.Document);
        _documents.DocumentUrlChanged += (_, e) => Unchange(e.Document);
    }

    /// <summary>Raised once when a document is changed on disk.</summary>
    /// <remarks>Upstream's <c>documentChangedOnDisk</c> signal.</remarks>
    public event EventHandler<DocumentEventArgs> DocumentChangedOnDisk;

    /// <summary>
    /// Gets or sets how to get onto the window's thread; null announces the
    /// change where it was seen.
    /// </summary>
    public Action<Action> ToUiThread { get; set; }

    /// <summary>Gets whether the watcher is running.</summary>
    public bool IsRunning => _running;

    /// <summary>Gets the files being watched.</summary>
    /// <returns>The files.</returns>
    /// <remarks>Upstream's <c>watcher.files()</c>.</remarks>
    public IReadOnlyList<string> Files()
    {
        lock (_gate) { return _files.ToList(); }
    }

    /// <summary>Starts watching the open documents.</summary>
    /// <remarks>Upstream's <c>start()</c>.</remarks>
    public void Start()
    {
        if (_running) { return; }

        _running = true;
        _documents.DocumentLoaded += OnDocumentLoaded;
        _documents.DocumentUrlChanged += OnDocumentUrlChanged;
        _documents.DocumentClosed += OnDocumentClosed;
        _documents.DocumentSaving += OnDocumentSaving;

        foreach (var document in _documents.Documents)
        {
            AddPath(document.Path);
        }
    }

    /// <summary>Stops watching.</summary>
    /// <remarks>Upstream's <c>stop()</c>.</remarks>
    public void Stop()
    {
        if (!_running) { return; }

        _running = false;
        _documents.DocumentLoaded -= OnDocumentLoaded;
        _documents.DocumentUrlChanged -= OnDocumentUrlChanged;
        _documents.DocumentClosed -= OnDocumentClosed;
        _documents.DocumentSaving -= OnDocumentSaving;

        lock (_gate)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            _files.Clear();
        }
    }

    /// <summary>Marks a document as not changed any more.</summary>
    /// <param name="document">The document.</param>
    /// <remarks>Upstream's <c>unchange()</c>.</remarks>
    public static void Unchange(EditorDocument document)
    {
        if (document != null) { DocumentWatcher.For(document).Changed = false; }
    }

    /// <summary>Adds a file to the watch set.</summary>
    /// <param name="path">The file, or null for a document with no file yet.</param>
    /// <remarks>Upstream's <c>addUrl()</c>.</remarks>
    public void AddPath(string path)
    {
        if (string.IsNullOrEmpty(path)) { return; }

        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) { return; }

        lock (_gate)
        {
            if (!_files.Add(full)) { return; }

            if (_watchers.ContainsKey(directory)) { return; }

            FileSystemWatcher watcher = new FileSystemWatcher(directory)
            {
                //Everything a rewrite, a truncation, a delete or a rename can
                //look like from outside; upstream's QFileSystemWatcher reports
                //the lot as one "the file changed".
                NotifyFilter = NotifyFilters.LastWrite
                    | NotifyFilters.FileName
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
            };
            watcher.Changed += OnFileSystemEvent;
            watcher.Created += OnFileSystemEvent;
            watcher.Deleted += OnFileSystemEvent;
            watcher.Renamed += OnFileRenamed;
            watcher.EnableRaisingEvents = true;
            _watchers[directory] = watcher;
        }
    }

    /// <summary>Removes a file from the watch set.</summary>
    /// <param name="path">The file.</param>
    /// <remarks>Upstream's <c>removeUrl()</c>.</remarks>
    public void RemovePath(string path)
    {
        if (string.IsNullOrEmpty(path)) { return; }

        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full);

        lock (_gate)
        {
            if (!_files.Remove(full)) { return; }

            //The directory's watcher goes when the last file in it does.
            if (string.IsNullOrEmpty(directory)
                || _files.Any(f => string.Equals(
                    Path.GetDirectoryName(f), directory,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal)))
            {
                return;
            }

            if (_watchers.Remove(directory, out FileSystemWatcher watcher))
            {
                watcher.Dispose();
            }
        }
    }

    /// <summary>
    /// Reports that a file changed on disk, announcing it once per document.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <remarks>Upstream's <c>fileChanged()</c>: the flag is what makes it
    /// once — a file rewritten ten times raises this once until something
    /// clears the flag.</remarks>
    public void FileChanged(string path)
    {
        if (string.IsNullOrEmpty(path)) { return; }

        lock (_gate)
        {
            if (!_files.Contains(Path.GetFullPath(path))) { return; }
        }

        EditorDocument document = _documents.FindDocument(path);
        if (document == null) { return; }

        DocumentWatcher watcher = DocumentWatcher.For(document);
        if (watcher.Changed) { return; }

        watcher.Changed = true;
        Announce(() => DocumentChangedOnDisk?.Invoke(
            this, new DocumentEventArgs(document)));
    }

    /// <summary>Stops watching and releases the platform watchers.</summary>
    public void Dispose() => Stop();

    private void OnDocumentLoaded(object sender, DocumentEventArgs e)
        => AddPath(e.Document?.Path);

    private void OnDocumentUrlChanged(object sender, DocumentEventArgs e)
    {
        //Upstream compares against the OLD url and keeps watching it when some
        //other document still points at it. The document manager announces the
        //document rather than the pair, so the same question is asked of the
        //watch set: a path no open document has any more is dropped.
        foreach (var watched in Files())
        {
            if (_documents.FindDocument(watched) == null) { RemovePath(watched); }
        }

        AddPath(e.Document?.Path);
    }

    private void OnDocumentClosed(object sender, DocumentEventArgs e)
    {
        string path = e.Document?.Path;
        if (string.IsNullOrEmpty(path)) { return; }

        //Upstream keeps the path when ANOTHER open document has it.
        if (_documents.FindDocument(path) != null) { return; }

        RemovePath(path);
    }

    private void OnDocumentSaving(object sender, DocumentSavingEventArgs e)
    {
        //Upstream's whileSaving(): stand aside for our own write, and take the
        //file up again afterwards whether it succeeded or threw.
        string path = e.Document?.Path;
        if (string.IsNullOrEmpty(path)) { return; }

        RemovePath(path);
        e.ResumeAfterSave(() => AddPath(path));
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        => FileChanged(e.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        //A rename touches both names: the file that was moved away, and
        //whatever has taken its place.
        FileChanged(e.OldFullPath);
        FileChanged(e.FullPath);
    }

    private void Announce(Action work)
    {
        Action<Action> post = ToUiThread;
        if (post == null)
        {
            work();
            return;
        }

        post(work);
    }
}
