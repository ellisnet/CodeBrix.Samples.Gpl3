// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fresco.Brix.Sessions; //was previously: frescobaldi/sessions/manager.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Switching between named sessions: saving the one being left, opening the
/// documents of the one being entered, and putting the right one in front.
/// </summary>
public sealed class SessionManager
{
    private readonly SessionStore _store;
    private readonly DocumentManager _documents;

    /// <summary>Creates the manager.</summary>
    /// <param name="store">The stored sessions.</param>
    /// <param name="documents">The open documents.</param>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public SessionManager(
        SessionStore store,
        DocumentManager documents,
        SettingsStore settings = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        Actions = new SessionActions(settings);
        Actions.SessionSave.AsyncHandler = SaveAsync;
        Actions.SessionNew.AsyncHandler = NewAsync;
        Actions.SessionNone.AsyncHandler = NoSessionAsync;
        _store.CurrentSessionChanged += (_, _) => UpdateActions();
        UpdateActions();
    }

    /// <summary>Raised when a session is about to be written.</summary>
    /// <remarks>Upstream's <c>saveSessionData</c>: the tools that keep their
    /// own per-session state hang off it.</remarks>
    public event EventHandler<SessionEventArgs> SavingSession;

    /// <summary>Gets the session commands.</summary>
    public SessionActions Actions { get; }

    /// <summary>Gets the stored sessions.</summary>
    public SessionStore Store => _store;

    /// <summary>Gets or sets how to ask the user for a session name.</summary>
    public Func<string, Task<string>> AskForNameAsync { get; set; }

    /// <summary>
    /// Gets or sets how to close everything that is open, answering false when
    /// the user backs out.
    /// </summary>
    public Func<Task<bool>> CloseAllAsync { get; set; }

    /// <summary>Gets or sets how to open a file.</summary>
    public Func<string, Task<bool>> OpenPathAsync { get; set; }

    /// <summary>Makes a new session and saves the open documents into it.</summary>
    /// <returns>The task.</returns>
    public async Task NewAsync()
    {
        Func<string, Task<string>> ask = AskForNameAsync;
        if (ask == null) { return; }

        string name = await ask(null);
        if (string.IsNullOrEmpty(name)) { return; }

        _store.SetCurrentSession(name);
        SaveCurrent();
    }

    /// <summary>Saves the current session, or makes one when there is none.</summary>
    /// <returns>The task.</returns>
    public Task SaveAsync()
    {
        if (_store.CurrentSession == null) { return NewAsync(); }

        SaveCurrent();
        return Task.CompletedTask;
    }

    /// <summary>Leaves the current session.</summary>
    /// <returns>The task.</returns>
    public Task NoSessionAsync()
    {
        if (_store.CurrentSession != null)
        {
            SaveCurrentIfDesired();
            _store.SetCurrentSession(null);
        }

        UpdateActions();
        return Task.CompletedTask;
    }

    /// <summary>Switches to a session.</summary>
    /// <param name="name">The session name.</param>
    /// <returns>Whether the switch happened.</returns>
    public async Task<bool> StartSessionAsync(string name)
    {
        if (string.Equals(name, _store.CurrentSession, StringComparison.Ordinal))
        {
            return true;
        }

        SaveCurrentIfDesired();

        //Everything open belongs to the session being left; the user gets the
        //chance to keep unsaved work before it goes.
        Func<Task<bool>> closeAll = CloseAllAsync;
        if (closeAll != null && !await closeAll()) { return false; }

        await LoadSessionAsync(name);
        return true;
    }

    /// <summary>Opens a session's documents and puts the right one in front.</summary>
    /// <param name="name">The session name.</param>
    /// <returns>The document to make current, or null.</returns>
    public async Task<EditorDocument> LoadSessionAsync(string name)
    {
        SessionData data = _store.Read(name);
        _store.SetCurrentSession(name);
        if (data == null) { return null; }

        DocumentInfo.SessionIncludePath = data.IncludePath;

        List<EditorDocument> opened = new List<EditorDocument>();
        Func<string, Task<bool>> open = OpenPathAsync;
        foreach (var path in data.Paths)
        {
            if (open != null && await open(path))
            {
                EditorDocument document = _documents.FindDocument(path);
                if (document != null) { opened.Add(document); }
            }
        }

        if (opened.Count == 0) { return null; }

        int active = data.ActiveIndex;
        if (active < 0 || active >= opened.Count) { active = 0; }

        _documents.CurrentDocument = opened[active];
        return opened[active];
    }

    /// <summary>Writes the open documents into the current session.</summary>
    public void SaveCurrent()
    {
        string name = _store.CurrentSession;
        if (name == null) { return; }

        //Only documents with a file can be reopened; an untitled one has
        //nothing to remember.
        List<EditorDocument> saved = _documents.Documents
            .Where(d => d.Path != null).ToList();
        SessionData data = _store.Read(name) ?? new SessionData();
        data.Paths = saved.Select(d => d.Path).ToList();
        data.ActiveIndex = saved.IndexOf(_documents.CurrentDocument);
        _store.Write(name, data);
        SavingSession?.Invoke(this, new SessionEventArgs(name));
    }

    /// <summary>Saves the current session if it asked to be saved.</summary>
    public void SaveCurrentIfDesired()
    {
        string name = _store.CurrentSession;
        if (name == null) { return; }

        if (_store.Read(name)?.AutoSave ?? true) { SaveCurrent(); }
    }

    /// <summary>Turns the session commands on and off.</summary>
    public void UpdateActions()
        => Actions.SessionNone.IsChecked = _store.CurrentSession == null;
}

/// <summary>Names a session.</summary>
public sealed class SessionEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="name">The session name.</param>
    public SessionEventArgs(string name) => Name = name;

    /// <summary>Gets the session name.</summary>
    public string Name { get; }
}

/// <summary>The Session menu's commands.</summary>
public sealed class SessionActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "session";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public SessionActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the "start a new session" command.</summary>
    public AppAction SessionNew { get; private set; }

    /// <summary>Gets the "save this session" command.</summary>
    public AppAction SessionSave { get; private set; }

    /// <summary>Gets the "manage sessions" command.</summary>
    public AppAction SessionManage { get; private set; }

    /// <summary>Gets the "work outside any session" toggle.</summary>
    public AppAction SessionNone { get; private set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Sessions");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        SessionNew = Add("session_new").WithIcon("document-new");
        SessionSave = Add("session_save").WithIcon("document-save");
        SessionManage = Add("session_manage").WithIcon("view-choose");
        SessionNone = Add("session_none").AsToggle(true);
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        SessionNew.Text = I18n.Get("New Session", "&New...");
        SessionSave.Text = I18n.Get("&Save");
        SessionManage.Text = I18n.Get("&Manage...");
        SessionNone.Text = I18n.Get("No Session");
    }
}
