// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fresco.Brix.Services; //was previously: frescobaldi/recentfiles.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The recently-opened documents list. Upstream keeps QUrls in QSettings and
/// drops the ones that are no longer readable when it loads; this keeps local
/// paths in the <see cref="SettingsStore"/> and does the same readability
/// filtering, with the same ten-item limit and the same
/// move-to-front-on-add semantics.
/// </summary>
/// <remarks>The list is ONE JSON-valued key — the settings add-in serialises a
/// <see cref="List{T}"/> natively. //was previously: the same key holding the
/// paths joined by newlines, which the flat store this replaced could only
/// hold as one string.</remarks>
public sealed class RecentFiles
{
    /// <summary>The settings key the list is stored under.</summary>
    public const string SettingKey = "recent_files";

    /// <summary>The maximum number of items remembered.</summary>
    public const int MaxLength = 10; //was previously: MAXLEN

    private readonly SettingsStore _settings;
    private List<string> _paths;

    /// <summary>Creates the list over a settings store.</summary>
    /// <param name="settings">The store the list is persisted in.</param>
    public RecentFiles(SettingsStore settings)
        => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>Gets the remembered paths, most recent first.</summary>
    /// <returns>The paths.</returns>
    public IReadOnlyList<string> Paths()
    {
        Load();
        return _paths;
    }

    /// <summary>Moves a path to the front of the list, adding it when new.</summary>
    /// <param name="path">The document path.</param>
    public void Add(string path)
    {
        if (string.IsNullOrEmpty(path)) { return; }

        Load();
        var full = Normalize(path);
        _paths.RemoveAll(p => string.Equals(p, full, StringComparison.Ordinal));
        _paths.Insert(0, full);
        Trim();
        Save();
    }

    /// <summary>Drops a path from the list.</summary>
    /// <param name="path">The document path.</param>
    public void Remove(string path)
    {
        if (string.IsNullOrEmpty(path)) { return; }

        Load();
        var full = Normalize(path);
        if (_paths.RemoveAll(p => string.Equals(p, full, StringComparison.Ordinal)) > 0)
        {
            Save();
        }
    }

    /// <summary>Forgets the in-memory copy, so the next read reloads it — the
    /// seam the tests use to prove the list survived the store.</summary>
    public void Invalidate() => _paths = null;

    private void Load()
    {
        if (_paths != null) { return; }

        //Upstream drops entries it can no longer read; a stored path that has
        //since been deleted or turned unreadable never reaches the menu.
        _paths = (_settings.Get<List<string>>(SettingKey) ?? new List<string>())
            .Where(p => !string.IsNullOrEmpty(p) && IsReadable(p))
            .ToList();
        Trim();
    }

    private void Save()
    {
        if (_paths.Count == 0) { _settings.Remove(SettingKey); } else { _settings.Set(SettingKey, _paths); }
    }

    private void Trim()
    {
        if (_paths.Count > MaxLength)
        {
            _paths.RemoveRange(MaxLength, _paths.Count - MaxLength);
        }
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static bool IsReadable(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
