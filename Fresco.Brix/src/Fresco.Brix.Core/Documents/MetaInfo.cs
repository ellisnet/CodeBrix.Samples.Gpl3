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

namespace Fresco.Brix.Documents; //was previously: frescobaldi/metainfo.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What the application remembers ABOUT a document rather than in it: where
/// the cursor was, whether folding was on, which tools were open — keyed by
/// the document's path, so reopening a file puts the user back where they
/// left off.
/// <para>
/// Values are declared once with <see cref="Define"/> so a value the user has
/// never changed costs nothing to store, and so an old key can be dropped
/// simply by no longer declaring it.
/// </para>
/// </summary>
public sealed class MetaInfo
{
    private const string Prefix = "metainfo/";
    private const string EnabledKey = "metainfo";
    private const string TimeName = "time";

    private static readonly Dictionary<string, string> Defaults
        = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly SettingsStore _settings;
    private readonly Dictionary<string, string> _values
        = new Dictionary<string, string>(StringComparer.Ordinal);
    private string _path;

    /// <summary>Creates the meta-info for a document.</summary>
    /// <param name="settings">The store to remember values in.</param>
    /// <param name="path">The document's path, or null while it is nameless.</param>
    public MetaInfo(SettingsStore settings, string path = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _path = path;
        Load();
    }

    /// <summary>
    /// Declares a value and its default. Anything read or written must be
    /// declared first; a value equal to its default is never stored.
    /// </summary>
    /// <param name="name">The value name.</param>
    /// <param name="defaultValue">The default, as text.</param>
    public static void Define(string name, string defaultValue)
        => Defaults[name] = defaultValue ?? string.Empty;

    /// <summary>Gets the declared value names.</summary>
    public static IEnumerable<string> DefinedNames => Defaults.Keys;

    /// <summary>Gets or sets the document's path; changing it reloads.</summary>
    public string Path
    {
        get => _path;
        set
        {
            if (string.Equals(_path, value, StringComparison.Ordinal)) { return; }

            _path = value;
            Load();
        }
    }

    /// <summary>Reads a remembered value.</summary>
    /// <param name="name">The declared name.</param>
    /// <returns>The value, or its default.</returns>
    public string Get(string name)
        => _values.TryGetValue(name, out var value)
            ? value
            : Defaults.TryGetValue(name, out var fallback) ? fallback : null;

    /// <summary>Reads a remembered flag.</summary>
    /// <param name="name">The declared name.</param>
    /// <returns>The value.</returns>
    public bool GetBool(string name)
    {
        string value = Get(name);
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads a remembered number.</summary>
    /// <param name="name">The declared name.</param>
    /// <returns>The value, or 0 when it does not read as a number.</returns>
    public int GetInt(string name)
        => int.TryParse(Get(name), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;

    /// <summary>Sets a value in memory; <see cref="Save"/> writes it out.</summary>
    /// <param name="name">The declared name.</param>
    /// <param name="value">The value.</param>
    public void Set(string name, string value) => _values[name] = value ?? string.Empty;

    /// <summary>Sets a flag.</summary>
    /// <param name="name">The declared name.</param>
    /// <param name="value">The value.</param>
    public void SetBool(string name, bool value) => Set(name, value ? "1" : "0");

    /// <summary>Sets a number.</summary>
    /// <param name="name">The declared name.</param>
    /// <param name="value">The value.</param>
    public void SetInt(string name, int value)
        => Set(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Reads the remembered values back for the current path.</summary>
    public void Load()
    {
        _values.Clear();
        bool enabled = _settings.GetBool(EnabledKey, true);
        foreach (var pair in Defaults)
        {
            _values[pair.Key] = enabled && _path != null
                ? _settings.GetString(Key(pair.Key), pair.Value)
                : pair.Value;
        }
    }

    /// <summary>
    /// Writes the values that differ from their defaults, and stamps the entry
    /// so <see cref="Prune"/> can tell how old it is.
    /// </summary>
    public void Save()
    {
        if (_path == null || !_settings.GetBool(EnabledKey, true)) { return; }

        _settings.SetString(Key(TimeName),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture));
        foreach (var pair in Defaults)
        {
            string value = Get(pair.Key);
            if (string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                _settings.Remove(Key(pair.Key));
            }
            else
            {
                _settings.SetString(Key(pair.Key), value);
            }
        }
    }

    /// <summary>
    /// Forgets the entries for documents not seen for a month, so the store
    /// does not grow without bound.
    /// </summary>
    /// <param name="settings">The store.</param>
    /// <param name="maximumAge">How long an entry may go untouched; a month
    /// by default, as upstream.</param>
    /// <returns>How many document entries were dropped.</returns>
    public static int Prune(SettingsStore settings, TimeSpan? maximumAge = null)
    {
        long cutoff = DateTimeOffset.UtcNow
            .Subtract(maximumAge ?? TimeSpan.FromDays(31)).ToUnixTimeSeconds();
        int pruned = 0;

        //Every entry ends '<group>/time'; the group is one document.
        foreach (var key in settings.KeysWithPrefix(Prefix)
            .Where(k => k.EndsWith("/" + TimeName, StringComparison.Ordinal))
            .ToList())
        {
            string group = key.Substring(0, key.Length - TimeName.Length);
            if (long.TryParse(settings.GetString(key), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var stamp)
                && stamp >= cutoff)
            {
                continue;
            }

            settings.RemoveWithPrefix(group);
            pruned++;
        }

        return pruned;
    }

    /// <summary>The settings key one value is stored under.</summary>
    /// <param name="name">The value name.</param>
    /// <returns>The key.</returns>
    /// <remarks>Upstream flattens the path into a settings group name by
    /// replacing the separators; the same flattening keeps a path from
    /// splitting our own key structure.</remarks>
    private string Key(string name)
        => Prefix + (_path ?? string.Empty).Replace('\\', '_').Replace('/', '_')
            + "/" + name;
}
