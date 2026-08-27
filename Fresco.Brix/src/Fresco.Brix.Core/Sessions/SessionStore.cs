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
/// Upstream stores each session under a generated group (<c>session1</c>,
/// <c>session2</c>, …) whose <c>name</c> value holds the name the user typed,
/// so that renaming a session does not have to move its settings. The same
/// shape is kept here, which is also what makes a session name able to hold a
/// <c>/</c> (the grouping the menu shows) without colliding with the settings
/// store's own key separator.
/// </remarks>
public sealed class SessionStore
{
    /// <summary>The settings prefix sessions live under.</summary>
    public const string SettingsPrefix = "sessions/";

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
        => Groups()
            .Select(g => _settings.GetString(g + "/name", string.Empty))
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, NaturalComparer.Instance)
            .ToList();

    /// <summary>Answers whether a session exists.</summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether it does.</returns>
    public bool Exists(string name) => GroupOf(name) != null;

    /// <summary>Reads a session.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The session, or null when it does not exist.</returns>
    public SessionData Read(string name)
    {
        string group = GroupOf(name);
        if (group == null || _settings == null) { return null; }

        return new SessionData
        {
            Paths = Decode(_settings.GetString(group + "/urls", string.Empty)),
            ActiveIndex = _settings.GetInt(group + "/active", -1),
            AutoSave = _settings.GetBool(group + "/autosave", true),
            BaseDirectory = NullIfEmpty(
                _settings.GetString(group + "/basedir", string.Empty)),
            IncludePath = Decode(
                _settings.GetString(group + "/includepath", string.Empty)),
        };
    }

    /// <summary>Writes a session, creating it when it is new.</summary>
    /// <param name="name">The name.</param>
    /// <param name="data">What to remember.</param>
    public void Write(string name, SessionData data)
    {
        string group = GroupOf(name) ?? CreateGroup(name);
        if (group == null || _settings == null) { return; }

        _settings.SetString(group + "/urls", Encode(data.Paths));
        if (data.ActiveIndex >= 0)
        {
            _settings.SetInt(group + "/active", data.ActiveIndex);
        }
        else
        {
            _settings.Remove(group + "/active");
        }

        _settings.SetBool(group + "/autosave", data.AutoSave);
        if (string.IsNullOrEmpty(data.BaseDirectory))
        {
            _settings.Remove(group + "/basedir");
        }
        else
        {
            _settings.SetString(group + "/basedir", data.BaseDirectory);
        }

        _settings.SetString(group + "/includepath", Encode(data.IncludePath));
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes a session.</summary>
    /// <param name="name">The name.</param>
    public void Delete(string name)
    {
        string group = GroupOf(name);
        if (group == null || _settings == null) { return; }

        foreach (var key in _settings.KeysWithPrefix(group + "/").ToList())
        {
            _settings.Remove(key);
        }

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
        string group = GroupOf(oldName);
        if (group == null || _settings == null) { return; }

        _settings.SetString(group + "/name", newName);
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
        if (!string.IsNullOrEmpty(name) && GroupOf(name) == null)
        {
            CreateGroup(name);
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

    private IReadOnlyList<string> Groups()
    {
        if (_settings == null) { return Array.Empty<string>(); }

        HashSet<string> groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in _settings.KeysWithPrefix(SettingsPrefix))
        {
            string rest = key.Substring(SettingsPrefix.Length);
            int slash = rest.IndexOf('/');
            if (slash > 0) { groups.Add(SettingsPrefix + rest.Substring(0, slash)); }
        }

        return groups.OrderBy(g => g, NaturalComparer.Instance).ToList();
    }

    private string GroupOf(string name)
    {
        if (_settings == null || string.IsNullOrEmpty(name)) { return null; }

        return Groups().FirstOrDefault(g => string.Equals(
            _settings.GetString(g + "/name", string.Empty), name, StringComparison.Ordinal));
    }

    private string CreateGroup(string name)
    {
        if (_settings == null) { return null; }

        HashSet<string> taken = new HashSet<string>(Groups(), StringComparer.Ordinal);
        for (int count = 1; ; count++)
        {
            string group = SettingsPrefix + "session"
                + count.ToString(CultureInfo.InvariantCulture);
            if (taken.Contains(group)) { continue; }

            _settings.SetString(group + "/name", name);
            return group;
        }
    }

    /// <summary>Encodes a list of paths as one setting value.</summary>
    /// <param name="values">The paths.</param>
    /// <returns>The encoded value.</returns>
    /// <remarks>A newline cannot appear in a path on any platform this runs
    /// on, which is what makes it usable as the separator.</remarks>
    public static string Encode(IEnumerable<string> values)
        => string.Join("\n", values ?? Array.Empty<string>());

    /// <summary>Decodes what <see cref="Encode"/> wrote.</summary>
    /// <param name="text">The encoded value.</param>
    /// <returns>The paths.</returns>
    public static IReadOnlyList<string> Decode(string text)
        => string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string NullIfEmpty(string text)
        => string.IsNullOrEmpty(text) ? null : text;
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
