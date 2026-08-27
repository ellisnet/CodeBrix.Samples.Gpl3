// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System;
using System.IO;
using System.Threading;

namespace Fresco.Brix.Engrave; //was previously: frescobaldi/engrave/autocompile.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Keeps the engraved score up to date while the user types, without ever
/// being asked to.
/// </summary>
/// <remarks>
/// <para>
/// The rules that make this bearable rather than maddening are all upstream's,
/// and all of them matter: it waits until the user pauses; it only runs when
/// the document is COMPLETE (its braces balance and it has something to
/// output), so a half-typed expression never produces a page of errors; it
/// compares a hash of the document's TOKENS, so reformatting or a comment
/// change does not trigger a run; and it never overtakes a job the user asked
/// for — it waits for that one to finish instead.
/// </para>
/// <para>
/// Its jobs are marked hidden, which is what keeps the log from popping up and
/// the engrave button from turning into a stop button.
/// </para>
/// </remarks>
public sealed class AutoCompiler : IDisposable
{
    /// <summary>How long after the last change a run is considered.</summary>
    public const int DelayMilliseconds = 750;

    private readonly Engraver _engraver;
    private readonly DocumentManager _documents;
    private readonly Timer _timer;
    private readonly Action<Action> _toUiThread;
    private EditorDocument _watched;
    private bool _enabled;

    /// <summary>Creates the automatic engraver.</summary>
    /// <param name="engraver">The engraving service.</param>
    /// <param name="documents">The open documents.</param>
    /// <param name="toUiThread">How to get back onto the UI thread, or null to
    /// run the check where the timer fires (the tests' path).</param>
    public AutoCompiler(
        Engraver engraver,
        DocumentManager documents,
        Action<Action> toUiThread = null)
    {
        _engraver = engraver ?? throw new ArgumentNullException(nameof(engraver));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _toUiThread = toUiThread ?? (work => work());
        _timer = new Timer(_ => _toUiThread(Tick), null, Timeout.Infinite, Timeout.Infinite);

        _engraver.Actions.EngraveAutoCompile.Triggered
            += (_, _) => IsEnabled = _engraver.Actions.EngraveAutoCompile.IsChecked;
        IsEnabled = _engraver.Actions.EngraveAutoCompile.IsChecked;
    }

    /// <summary>Gets or sets whether automatic engraving is on.</summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) { return; }

            _enabled = value;
            if (value)
            {
                _documents.CurrentDocumentChanged += OnCurrentDocumentChanged;
                _documents.DocumentUrlChanged += OnDocumentTouched;
                Watch(_documents.CurrentDocument);
                StartTimer();
            }
            else
            {
                _documents.CurrentDocumentChanged -= OnCurrentDocumentChanged;
                _documents.DocumentUrlChanged -= OnDocumentTouched;
                Watch(null);
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    /// <summary>Asks for a run to be considered a moment from now.</summary>
    public void StartTimer()
        => _timer.Change(DelayMilliseconds, Timeout.Infinite);

    /// <summary>Considers a run right now — the seam the tests drive.</summary>
    public void Tick()
    {
        if (!_enabled) { return; }

        EditorDocument document = _engraver.Document();
        if (document == null) { return; }

        EngraveJob running = JobManager.JobFor(document);
        if (running is { IsRunning: true })
        {
            //A job the user asked for is running. Come back when it is done
            //rather than queueing behind it.
            void Resume(object sender, bool success)
            {
                running.Done -= Resume;
                _toUiThread(StartTimer);
            }

            running.Done += Resume;
            return;
        }

        AutoCompileState state = AutoCompileState.For(document);
        bool mayCompile = state.MayCompile();
        if (!mayCompile)
        {
            //The engraved document may not be the current one. If the current
            //one is saved and unmodified, it is safe to consider IT instead —
            //upstream's fallback, and what makes autocompile work while the
            //user reads a part of a score whose master is sticky.
            EditorDocument current = _documents.CurrentDocument;
            if (current != null && current != document
                && !current.IsModified && current.Path != null)
            {
                state = AutoCompileState.For(current);
                mayCompile = state.MayCompile();
                if (mayCompile) { state.JobStarted(); }
            }
        }

        if (!mayCompile) { return; }

        PreviewJob job = new PreviewJob(_engraver.Engine, document);
        JobAttributes.For(job).Hidden = true;
        _engraver.RunJob(job, document);
    }

    /// <summary>Stops watching and releases the timer.</summary>
    public void Dispose()
    {
        Watch(null);
        _timer.Dispose();
    }

    private void OnCurrentDocumentChanged(object sender, DocumentEventArgs e)
    {
        Watch(e.Document);
        if (_enabled) { StartTimer(); }
    }

    private void OnDocumentTouched(object sender, DocumentEventArgs e) => StartTimer();

    private void Watch(EditorDocument document)
    {
        if (_watched == document) { return; }

        if (_watched != null)
        {
            _watched.ContentsChanged -= OnDocumentChanged;
            _watched.Loaded -= OnDocumentChanged;
            _watched.Saved -= OnDocumentChanged;
        }

        _watched = document;

        if (_watched != null)
        {
            _watched.ContentsChanged += OnDocumentChanged;
            _watched.Loaded += OnDocumentChanged;
            _watched.Saved += OnDocumentChanged;
        }
    }

    private void OnDocumentChanged(object sender, EventArgs e) => StartTimer();
}

/// <summary>
/// What the automatic engraver remembers about one document: whether it has
/// changed since the last look, and what its tokens hashed to then.
/// </summary>
public sealed class AutoCompileState : Plugin<EditorDocument, AutoCompileState>
{
    private bool _dirty;
    private int? _hash;

    private AutoCompileState(EditorDocument document)
        : base(document)
    {
        document.ContentsChanged += (_, _) => OnContentsChanged();
        document.Loaded += (_, _) => Initialize();
        document.Saved += (_, _) => OnSaved();
        JobManager.For(document).JobStarted += (_, _) => JobStarted();
        Initialize();
    }

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets the state for a document, creating it on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The state.</returns>
    public static AutoCompileState For(EditorDocument document)
        => Instance(document, owner => new AutoCompileState(owner));

    /// <summary>Answers whether an automatic run is worth starting.</summary>
    /// <returns>Whether to run.</returns>
    /// <remarks>Asking is not free of consequence: a "no" clears the dirty
    /// flag, so a document that is not worth engraving is not asked about
    /// again until it changes.</remarks>
    public bool MayCompile()
    {
        if (!_dirty) { return false; }

        EditorDocument document = Document;
        if (document == null) { return false; }

        DocumentInfo info = DocumentInfo.For(document);
        string path = document.Path;
        bool eligible = info.Mode() == "lilypond"
            && (path == null
                || path.EndsWith(".ly", StringComparison.OrdinalIgnoreCase))
            && info.DocInfo().Complete()
            && info.Music().HasOutput();

        if (eligible)
        {
            int hash = info.DocInfo().TokenHash();
            if (hash != _hash)
            {
                _hash = hash;

                //An empty document hashes to the empty hash; engraving that
                //produces nothing and would simply run forever on every keystroke.
                if (hash != EmptyHash) { return true; }
            }
        }

        _dirty = false;
        return false;
    }

    /// <summary>Notes that a job has started, so its result counts as current.</summary>
    public void JobStarted()
    {
        if (!_dirty) { return; }

        _dirty = false;
        EditorDocument document = Document;
        if (document != null)
        {
            _hash = DocumentInfo.For(document).DocInfo().TokenHash();
        }
    }

    /// <summary>
    /// The hash of a document with no tokens at all.
    /// </summary>
    /// <remarks>Upstream compares against <c>hash(tuple())</c> for the same
    /// reason: an empty document is "complete" and would otherwise be engraved
    /// over and over. Computed rather than written down, so it stays right if
    /// the hash ever changes.</remarks>
    private static readonly int EmptyHash =
        new LyDocInfo(new Fresco.Brix.Ly.Document(string.Empty), null).TokenHash();

    private void Initialize()
    {
        EditorDocument document = Document;
        if (document == null) { return; }

        if (document.IsModified)
        {
            _dirty = true;
        }
        else if (document.Path == null)
        {
            _dirty = false;
        }
        else
        {
            //A saved document whose output is already on disk is not dirty.
            _dirty = ResultFiles.For(document).Files(".svg*").Count == 0;
        }

        _hash = _dirty ? null : DocumentInfo.For(document).DocInfo().TokenHash();
    }

    private void OnContentsChanged()
    {
        EditorDocument document = Document;
        if (document == null) { return; }

        //⚠ THE MODIFIED FLAG IS NOT TRUE YET WHILE THIS EVENT RUNS. The editor
        //marks the document modified when the change GROUP closes, which is
        //after the contents-changed notification; upstream's Qt document sets
        //it during, which is why upstream can simply ask. What IS already true
        //here is that the change is on the undo stack — which is the same
        //question upstream is really asking with
        //"isModified() or isRedoAvailable()": did the user do this, or is this
        //a document being filled in from a template.
        if (document.IsModified || document.Document.UndoStack.CanUndo)
        {
            _dirty = true;
        }
    }

    private void OnSaved()
    {
        //Saving is a moment worth engraving at, even if the tokens did not
        //change in a way the hash would notice.
        _dirty = true;
        _hash = null;
    }
}
