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
    /// <summary>
    /// The settings key the per-document entries live under — ONE key holding
    /// every remembered document as a JSON object, by its flattened path.
    /// </summary>
    /// <remarks>//was previously: <c>metainfo/</c>, a PREFIX with a key per
    /// document per value under it, which the flat store this replaced could
    /// enumerate to prune. The settings add-in has no prefix-scan API by design
    /// (board W13 item 9, route (a)). The key is NOT <c>metainfo</c>, because
    /// that name is already the user's "remember meta info" flag.</remarks>
    public const string SettingsKey = "metainfo/documents";

    private const string EnabledKey = "metainfo";

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
        Dictionary<string, string> stored = enabled && _path != null
            ? Entry(ReadStored(_settings), DocumentKey)?.Values
            : null;

        foreach (var pair in Defaults)
        {
            _values[pair.Key] = stored != null
                && stored.TryGetValue(pair.Key, out var value)
                    ? value
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

        Dictionary<string, StoredMetaInfo> stored = ReadStored(_settings);
        StoredMetaInfo entry = Entry(stored, DocumentKey);
        if (entry == null)
        {
            entry = new StoredMetaInfo();
            stored[DocumentKey] = entry;
        }

        entry.Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        entry.Values ??= new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in Defaults)
        {
            string value = Get(pair.Key);
            //A value equal to its default is never stored, so a later change of
            //default reaches a user who never touched it.
            if (string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                entry.Values.Remove(pair.Key);
            }
            else
            {
                entry.Values[pair.Key] = value;
            }
        }

        WriteStored(_settings, stored);
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
        if (settings == null) { return 0; }

        long cutoff = DateTimeOffset.UtcNow
            .Subtract(maximumAge ?? TimeSpan.FromDays(31)).ToUnixTimeSeconds();
        Dictionary<string, StoredMetaInfo> stored = ReadStored(settings);
        int pruned = 0;

        //Each entry is one document, and carries the stamp its last save wrote.
        foreach (var pair in stored.ToList())
        {
            if (pair.Value != null && pair.Value.Time >= cutoff) { continue; }

            stored.Remove(pair.Key);
            pruned++;
        }

        if (pruned > 0) { WriteStored(settings, stored); }

        return pruned;
    }

    /// <summary>
    /// The name this document's entry is held under inside the family.
    /// </summary>
    /// <remarks>Upstream flattens the path into a settings group name by
    /// replacing the separators; the same flattening is kept so that a path
    /// reads the way upstream's group did.</remarks>
    private string DocumentKey
        => (_path ?? string.Empty).Replace('\\', '_').Replace('/', '_');

    private static StoredMetaInfo Entry(
        Dictionary<string, StoredMetaInfo> stored, string documentKey)
        => stored.TryGetValue(documentKey, out var entry) ? entry : null;

    private static Dictionary<string, StoredMetaInfo> ReadStored(SettingsStore settings)
        => settings?.Get<Dictionary<string, StoredMetaInfo>>(SettingsKey)
            ?? new Dictionary<string, StoredMetaInfo>(StringComparer.Ordinal);

    private static void WriteStored(
        SettingsStore settings, Dictionary<string, StoredMetaInfo> stored)
    {
        if (settings == null) { return; }

        if (stored.Count == 0)
        {
            settings.Remove(SettingsKey);
        }
        else
        {
            settings.Set(SettingsKey, stored);
        }
    }
}

/// <summary>
/// What the application remembers about ONE document, as the settings store
/// holds it.
/// </summary>
public sealed class StoredMetaInfo
{
    /// <summary>Gets or sets when the entry was last written, as a Unix
    /// timestamp in seconds — what <see cref="MetaInfo.Prune"/> reads.</summary>
    public long Time { get; set; }

    /// <summary>Gets or sets the values that differ from their defaults.</summary>
    public Dictionary<string, string> Values { get; set; }
}
