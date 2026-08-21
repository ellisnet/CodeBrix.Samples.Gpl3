// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Services; //was previously: frescobaldi/completionmodel.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The things a user has typed into a text box before, remembered so the box
/// can offer them again: search terms, session names, variable names.
/// </summary>
/// <remarks>
/// One list per settings key, kept for the life of the process and written out
/// only when something has been added — which is upstream's arrangement, and
/// the reason the settings file is not rewritten on every keystroke.
/// </remarks>
public sealed class CompletionHistory
{
    private static readonly ConcurrentDictionary<string, CompletionHistory> Lists
        = new ConcurrentDictionary<string, CompletionHistory>(StringComparer.Ordinal);

    private readonly SettingsStore _settings;
    private readonly SortedSet<string> _strings
        = new SortedSet<string>(StringComparer.CurrentCulture);
    private bool _changed;

    private CompletionHistory(string key, SettingsStore settings)
    {
        Key = key;
        _settings = settings;
        Load();
    }

    /// <summary>Gets the settings key the list lives under.</summary>
    public string Key { get; }

    /// <summary>Gets the remembered strings, in order.</summary>
    public IReadOnlyList<string> Strings => _strings.ToList();

    /// <summary>Gets the list for a settings key, making it on first use.</summary>
    /// <param name="key">The settings key.</param>
    /// <param name="settings">The settings store, or null.</param>
    /// <returns>The list.</returns>
    public static CompletionHistory For(string key, SettingsStore settings = null)
        => Lists.GetOrAdd(key, k => new CompletionHistory(k, settings));

    /// <summary>Writes out every list that has changed.</summary>
    /// <remarks>Called as the application closes; upstream registers each
    /// model's save with <c>atexit</c> for the same reason.</remarks>
    public static void SaveAll()
    {
        foreach (var list in Lists.Values)
        {
            list.Save();
        }
    }

    /// <summary>Remembers a string.</summary>
    /// <param name="text">The string; blank text is ignored.</param>
    public void Add(string text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) { return; }

        if (_strings.Add(text)) { _changed = true; }
    }

    /// <summary>Reads the list back from the settings.</summary>
    public void Load()
    {
        _strings.Clear();
        string stored = _settings?.GetString(Key, string.Empty);
        if (!string.IsNullOrEmpty(stored))
        {
            foreach (var value in stored.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                _strings.Add(value);
            }
        }

        _changed = false;
    }

    /// <summary>Writes the list out, if anything was added.</summary>
    public void Save()
    {
        if (!_changed || _settings == null) { return; }

        _settings.SetString(Key, string.Join("\n", _strings));
        _changed = false;
    }
}
