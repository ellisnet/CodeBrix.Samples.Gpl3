// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fresco.Brix.Sessions; //was previously: frescobaldi/sessions/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What a named session remembers.</summary>
public sealed class SessionData
{
    /// <summary>Gets or sets the files it holds.</summary>
    public IReadOnlyList<string> Paths { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets which of them was in front, or -1.</summary>
    public int ActiveIndex { get; set; } = -1;

    /// <summary>Gets or sets whether it saves itself when it is left.</summary>
    public bool AutoSave { get; set; } = true;

    /// <summary>Gets or sets the folder new documents start in, or null.</summary>
    public string BaseDirectory { get; set; }

    /// <summary>Gets or sets the extra <c>\include</c> directories.</summary>
    public IReadOnlyList<string> IncludePath { get; set; } = Array.Empty<string>();
}

/// <summary>What the application does with the session it was started in.</summary>
public enum SessionStartup
{
    /// <summary>Start with no session.</summary>
    None,

    /// <summary>Start in whichever session was last used.</summary>
    LastUsed,

    /// <summary>Start in one particular session.</summary>
    Custom,
}

/// <summary>
/// Named sessions: a set of open documents with the settings that go with
/// them, remembered under a name the user chooses.
/// </summary>
/// <remarks>
/// <para>
/// Upstream stores each session under a generated group (<c>session1</c>,
/// <c>session2</c>, …) whose <c>name</c> value holds the name the user typed,
/// so that renaming a session does not have to move its settings. The same
/// shape is kept here, which is also what makes a session name able to hold a
/// <c>/</c> (the grouping the menu shows) without colliding with the settings
/// store's own key separator.
/// </para>
/// <para>
/// //was previously: those groups were settings-key PREFIXES —
/// <c>sessions/session1/urls</c> and the rest — which the flat store this
/// replaced could enumerate. The settings add-in has no prefix-scan API by
/// design (board W13 item 9, route (a)), so every session is one entry in one
/// JSON object under <see cref="SettingsKey"/>, and the group name is now that
/// entry's name rather than a key prefix.
/// </para>
/// </remarks>
public sealed class SessionStore
{
    /// <summary>The settings key sessions live under.</summary>
    public const string SettingsKey = "sessions";

    /// <summary>The setting holding what to do at startup.</summary>
    public const string StartupKey = "session/startup";

    /// <summary>The setting holding the last-used session's name.</summary>
    public const string LastUsedKey = "session/lastused";

    /// <summary>The setting holding the chosen startup session's name.</summary>
    public const string CustomKey = "session/custom";

    private readonly SettingsStore _settings;
    private string _current;

    /// <summary>Creates the store.</summary>
    /// <param name="settings">The settings store, or null.</param>
    public SessionStore(SettingsStore settings = null) => _settings = settings;

    /// <summary>Raised when the current session changes.</summary>
    public event EventHandler CurrentSessionChanged;

    /// <summary>Raised when a session is written, renamed or deleted.</summary>
    /// <remarks>The Session menu follows this. Upstream rebuilds its menu each
    /// time it is opened; a menu bar item here has no such moment, and being
    /// told when the list changes is the better answer anyway.</remarks>
    public event EventHandler SessionsChanged;

    /// <summary>Gets the session in force, or null for none.</summary>
    public string CurrentSession => _current;

    /// <summary>Gets or sets what to do at startup.</summary>
    public SessionStartup Startup
    {
        get => (_settings?.GetString(StartupKey, "none")) switch
        {
            "lastused" => SessionStartup.LastUsed,
            "custom" => SessionStartup.Custom,
            _ => SessionStartup.None,
        };

        set => _settings?.SetString(StartupKey, value switch
        {
            SessionStartup.LastUsed => "lastused",
            SessionStartup.Custom => "custom",
            _ => "none",
        });
    }

    /// <summary>Gets the session names, naturally sorted.</summary>
    /// <returns>The names.</returns>
    public IReadOnlyList<string> SessionNames()
        => ReadStored().Values
            .Select(s => s?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, NaturalComparer.Instance)
            .ToList();

    /// <summary>Answers whether a session exists.</summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether it does.</returns>
    public bool Exists(string name) => GroupOf(ReadStored(), name) != null;

    /// <summary>Reads a session.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The session, or null when it does not exist.</returns>
    public SessionData Read(string name)
    {
        if (_settings == null) { return null; }

        Dictionary<string, StoredSession> stored = ReadStored();
        string group = GroupOf(stored, name);
        if (group == null) { return null; }

        StoredSession session = stored[group];
        return new SessionData
        {
            Paths = session.Urls ?? (IReadOnlyList<string>)Array.Empty<string>(),
            ActiveIndex = session.ActiveIndex,
            AutoSave = session.AutoSave,
            BaseDirectory = NullIfEmpty(session.BaseDirectory),
            IncludePath = session.IncludePath ?? (IReadOnlyList<string>)Array.Empty<string>(),
        };
    }

    /// <summary>Writes a session, creating it when it is new.</summary>
    /// <param name="name">The name.</param>
    /// <param name="data">What to remember.</param>
    public void Write(string name, SessionData data)
    {
        if (_settings == null) { return; }

        Dictionary<string, StoredSession> stored = ReadStored();
        string group = GroupOf(stored, name) ?? CreateGroup(stored, name);
        if (group == null) { return; }

        StoredSession session = stored[group];
        session.Urls = new List<string>(data.Paths ?? Array.Empty<string>());
        session.ActiveIndex = data.ActiveIndex >= 0 ? data.ActiveIndex : -1;
        session.AutoSave = data.AutoSave;
        session.BaseDirectory = NullIfEmpty(data.BaseDirectory);
        session.IncludePath = new List<string>(data.IncludePath ?? Array.Empty<string>());

        WriteStored(stored);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes a session.</summary>
    /// <param name="name">The name.</param>
    public void Delete(string name)
    {
        if (_settings == null) { return; }

        Dictionary<string, StoredSession> stored = ReadStored();
        string group = GroupOf(stored, name);
        if (group == null) { return; }

        stored.Remove(group);
        WriteStored(stored);

        if (string.Equals(name, _current, StringComparison.Ordinal))
        {
            SetCurrentSession(null);
        }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Renames a session, keeping everything it holds.</summary>
    /// <param name="oldName">The name it has.</param>
    /// <param name="newName">The name it should have.</param>
    public void Rename(string oldName, string newName)
    {
        if (_settings == null) { return; }

        Dictionary<string, StoredSession> stored = ReadStored();
        string group = GroupOf(stored, oldName);
        if (group == null) { return; }

        stored[group].Name = newName;
        WriteStored(stored);
        if (string.Equals(oldName, _current, StringComparison.Ordinal))
        {
            SetCurrentSession(newName);
        }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets the session in force.</summary>
    /// <param name="name">The name, or null for none.</param>
    public void SetCurrentSession(string name)
    {
        if (string.Equals(name, _current, StringComparison.Ordinal)) { return; }

        //Selecting a session that does not exist yet writes its group, so the
        //name is remembered even before anything is saved into it.
        if (!string.IsNullOrEmpty(name) && _settings != null)
        {
            Dictionary<string, StoredSession> stored = ReadStored();
            if (GroupOf(stored, name) == null)
            {
                CreateGroup(stored, name);
                WriteStored(stored);
            }
        }

        _current = name;
        CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remembers which session was last used.</summary>
    public void SaveLastUsed()
        => _settings?.SetString(LastUsedKey, _current ?? string.Empty);

    /// <summary>
    /// Gets the session that should be opened at startup, or null.
    /// </summary>
    /// <returns>The name, or null.</returns>
    public string DefaultSessionName()
    {
        string name = Startup switch
        {
            SessionStartup.LastUsed => _settings?.GetString(LastUsedKey, string.Empty),
            SessionStartup.Custom => _settings?.GetString(CustomKey, string.Empty),
            _ => null,
        };

        if (Startup == SessionStartup.Custom
            && !string.IsNullOrEmpty(name) && !Exists(name))
        {
            //A chosen session that has since been deleted goes back to none,
            //rather than the application starting with an error.
            Startup = SessionStartup.None;
            return null;
        }

        return string.IsNullOrEmpty(name) || !Exists(name) ? null : name;
    }

    private Dictionary<string, StoredSession> ReadStored()
        => _settings?.Get<Dictionary<string, StoredSession>>(SettingsKey)
            ?? new Dictionary<string, StoredSession>(StringComparer.Ordinal);

    private void WriteStored(Dictionary<string, StoredSession> stored)
    {
        if (_settings == null) { return; }

        if (stored.Count == 0)
        {
            _settings.Remove(SettingsKey);
        }
        else
        {
            _settings.Set(SettingsKey, stored);
        }
    }

    private static string GroupOf(
        Dictionary<string, StoredSession> stored, string name)
    {
        if (string.IsNullOrEmpty(name)) { return null; }

        return stored
            .Where(pair => string.Equals(
                pair.Value?.Name ?? string.Empty, name, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .OrderBy(g => g, NaturalComparer.Instance)
            .FirstOrDefault();
    }

    private static string CreateGroup(
        Dictionary<string, StoredSession> stored, string name)
    {
        for (int count = 1; ; count++)
        {
            string group = "session" + count.ToString(CultureInfo.InvariantCulture);
            if (stored.ContainsKey(group)) { continue; }

            stored[group] = new StoredSession { Name = name };
            return group;
        }
    }

    private static string NullIfEmpty(string text)
        => string.IsNullOrEmpty(text) ? null : text;
}

/// <summary>
/// One named session as the settings store holds it.
/// </summary>
/// <remarks>//was previously: six settings keys under a
/// <c>sessions/&lt;group&gt;/</c> prefix (<c>name</c>, <c>urls</c>,
/// <c>active</c>, <c>autosave</c>, <c>basedir</c>, <c>includepath</c>), with
/// the two lists joined by newlines because the flat store held only text. The
/// settings add-in serialises a list natively.</remarks>
public sealed class StoredSession
{
    /// <summary>Gets or sets the name the user gave the session.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the files it holds.</summary>
    public List<string> Urls { get; set; }

    /// <summary>Gets or sets which of them was in front, or -1.</summary>
    public int ActiveIndex { get; set; } = -1;

    /// <summary>Gets or sets whether it saves itself when it is left.</summary>
    public bool AutoSave { get; set; } = true;

    /// <summary>Gets or sets the folder new documents start in, or null.</summary>
    public string BaseDirectory { get; set; }

    /// <summary>Gets or sets the extra <c>\include</c> directories.</summary>
    public List<string> IncludePath { get; set; }
}

/// <summary>
/// Sorts names the way a person reads them, so <c>session2</c> comes before
/// <c>session10</c>.
/// </summary>
/// <remarks>Upstream's <c>util.naturalsort</c>.</remarks>
public sealed class NaturalComparer : IComparer<string>
{
    /// <summary>The one instance.</summary>
    public static readonly NaturalComparer Instance = new NaturalComparer();

    private NaturalComparer()
    {
    }

    /// <inheritdoc/>
    public int Compare(string left, string right)
    {
        if (left == null) { return right == null ? 0 : -1; }

        if (right == null) { return 1; }

        int i = 0;
        int j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                int startLeft = i;
                int startRight = j;
                while (i < left.Length && char.IsDigit(left[i])) { i++; }

                while (j < right.Length && char.IsDigit(right[j])) { j++; }

                long numberLeft = long.Parse(
                    left.Substring(startLeft, i - startLeft),
                    CultureInfo.InvariantCulture);
                long numberRight = long.Parse(
                    right.Substring(startRight, j - startRight),
                    CultureInfo.InvariantCulture);
                if (numberLeft != numberRight)
                {
                    return numberLeft.CompareTo(numberRight);
                }

                continue;
            }

            int order = left[i].CompareTo(right[j]);
            if (order != 0) { return order; }

            i++;
            j++;
        }

        return (left.Length - i).CompareTo(right.Length - j);
    }
}
