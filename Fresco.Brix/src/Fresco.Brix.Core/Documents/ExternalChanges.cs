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
using System.Threading;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/externalchanges/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Watches the open documents for changes made by other programs — an
/// overwrite, a move, a delete — and puts the "Modified Files" window in front
/// of the user when one really happened.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DocumentWatchService"/> does the watching; this decides whether a
/// touched file really CHANGED, and waits half a second first, because a file
/// that is being written is very likely still being written.
/// </para>
/// <para>
/// Upstream is a module with global state; here it is a service the window
/// owns, and the window supplies <see cref="Display"/> — the only part that
/// needs a window at all.
/// </para>
/// </remarks>
public sealed class ExternalChanges : IDisposable
{
    /// <summary>The setting that turns the watching off.</summary>
    /// <remarks>Upstream's own key, and its own default of <c>true</c>.</remarks>
    public const string EnabledKey = "externalchanges/enabled";

    /// <summary>
    /// How long to wait after a change before looking, in milliseconds.
    /// </summary>
    /// <remarks>Upstream's own 500: "a file could probably still be
    /// changing".</remarks>
    public const int SettleMilliseconds = 500;

    private readonly DocumentManager _documents;
    private readonly DocumentWatchService _watcher;
    private readonly SettingsStore _settings;
    private readonly Timer _timer;

    private bool _connected;

    /// <summary>Creates the service.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="watcher">The file-system watcher.</param>
    /// <param name="settings">The settings store, or null for the defaults.</param>
    public ExternalChanges(
        DocumentManager documents,
        DocumentWatchService watcher,
        SettingsStore settings = null)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _settings = settings;
        //⚠ Board trap 22 again: the timer fires on a pool thread, and what it
        //leads to is a window. The hop is made HERE rather than left to the
        //caller, because Display is the only thing on the other side of it.
        _timer = new Timer(
            _ => Post(CheckChangedDocuments), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Gets or sets what shows the window; null means nothing is shown, which
    /// is the state the tests run in.
    /// </summary>
    /// <remarks>Upstream's <c>display()</c>, which imports its widget module
    /// on demand.</remarks>
    public Action<IReadOnlyList<EditorDocument>> Display { get; set; }

    /// <summary>
    /// Gets or sets how to get onto the window's thread; null runs the check
    /// where the timer fired, which is what the tests want.
    /// </summary>
    public Action<Action> ToUiThread { get; set; }

    /// <summary>Gets whether watching is enabled.</summary>
    /// <remarks>Upstream's <c>enabled()</c>.</remarks>
    public bool Enabled => _settings?.GetBool(EnabledKey, true) ?? true;

    /// <summary>Turns watching on or off and remembers the choice.</summary>
    /// <param name="enable">Whether to watch.</param>
    /// <remarks>Upstream's <c>setEnabled()</c>: it writes the setting only when
    /// the answer really changed, and then calls <see cref="Setup"/>.</remarks>
    public void SetEnabled(bool enable)
    {
        if (Enabled == enable) { return; }

        _settings?.SetBool(EnabledKey, enable);
        Setup();
    }

    /// <summary>Starts or stops the watching according to the setting.</summary>
    /// <remarks>Upstream's <c>setup()</c>.</remarks>
    public void Setup()
    {
        if (Enabled)
        {
            if (!_connected)
            {
                _watcher.DocumentChangedOnDisk += OnDocumentChanged;
                _connected = true;
            }

            _watcher.Start();
            return;
        }

        if (_connected)
        {
            _watcher.DocumentChangedOnDisk -= OnDocumentChanged;
            _connected = false;
        }

        _watcher.Stop();
    }

    /// <summary>Gets the documents that REALLY changed.</summary>
    /// <returns>The documents.</returns>
    /// <remarks>
    /// Upstream's <c>changedDocuments()</c>: a document with no unsaved edits
    /// whose file is byte-for-byte what the document holds has not changed,
    /// however many times the file system said it was written — which is what
    /// keeps a save made by another copy of the same text quiet.
    /// </remarks>
    public IReadOnlyList<EditorDocument> ChangedDocuments()
    {
        foreach (var watcher in DocumentWatcher.Instances())
        {
            EditorDocument document = watcher.Document;
            if (document == null || !watcher.Changed || document.IsModified) { continue; }

            string fileName = document.Path;
            if (string.IsNullOrEmpty(fileName)) { continue; }

            try
            {
                if (File.ReadAllBytes(fileName).SequenceEqual(document.EncodedText()))
                {
                    watcher.Changed = false;
                }
            }
            catch (IOException)
            {
                //Unreadable — which is itself a change worth reporting, so the
                //flag stays as it is.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return DocumentWatcher.Instances()
            .Where(w => w.Changed && w.Document != null)
            .Select(w => w.Document)
            .Where(d => _documents.Documents.Contains(d))
            .ToList();
    }

    /// <summary>Shows the window even when nothing has changed.</summary>
    /// <remarks>Upstream's <c>displayChangedDocuments()</c> — what File &gt;
    /// Check for External Changes does.</remarks>
    public void DisplayChangedDocuments() => Display?.Invoke(ChangedDocuments());

    /// <summary>Shows the window when something HAS changed.</summary>
    /// <remarks>Upstream's <c>checkChangedDocuments()</c>.</remarks>
    public void CheckChangedDocuments()
    {
        IReadOnlyList<EditorDocument> documents = ChangedDocuments();
        if (documents.Count > 0) { Display?.Invoke(documents); }
    }

    /// <summary>Stops the timer and the watching.</summary>
    public void Dispose()
    {
        _timer.Dispose();
        if (_connected)
        {
            _watcher.DocumentChangedOnDisk -= OnDocumentChanged;
            _connected = false;
        }
    }

    private void OnDocumentChanged(object sender, DocumentEventArgs e)
        => _timer.Change(SettleMilliseconds, Timeout.Infinite);

    private void Post(Action work)
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
