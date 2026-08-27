// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Fresco.Brix.Services;

/// <summary>
/// The application's single settings store — a key/value table in
/// <c>settings.sqlite</c> under the per-user application data directory,
/// the house options.sqlite pattern. Every preference the app persists goes
/// through here (the W12 preferences dialog reads and writes the same store).
/// </summary>
public sealed class SettingsStore : IDisposable
{
    private readonly SqliteDatabase _database;

    /// <summary>Opens (creating if needed) the default per-user store.</summary>
    public SettingsStore()
        : this(DefaultPath())
    {
    }

    /// <summary>Opens (creating if needed) a store at a specific path — the
    /// seam tests use.</summary>
    /// <param name="path">The database file path.</param>
    public SettingsStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        _database = new SqliteDatabase(path);
        _database.SafeOpen();
        _database.ExecuteNonQuery(
            "CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT);");
    }

    /// <summary>Gets the default store path:
    /// <c>&lt;ApplicationData&gt;/Fresco.Brix/settings.sqlite</c>.</summary>
    /// <returns>The path.</returns>
    public static string DefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Fresco.Brix",
            "settings.sqlite");

    /// <summary>Reads a string setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">The value when unset.</param>
    /// <returns>The value.</returns>
    public string GetString(string key, string defaultValue = null)
    {
        var value = _database.ExecuteScalar(
            "SELECT value FROM settings WHERE key = '" + Escape(key) + "';");
        return value == null || value is DBNull ? defaultValue : value.ToString();
    }

    /// <summary>Writes a string setting (null removes it).</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove.</param>
    public void SetString(string key, string value)
    {
        if (value == null)
        {
            _database.ExecuteNonQuery(
                "DELETE FROM settings WHERE key = '" + Escape(key) + "';");
            return;
        }

        _database.ExecuteNonQuery(
            "INSERT INTO settings (key, value) VALUES ('" + Escape(key) + "', '"
            + Escape(value) + "') ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
    }

    /// <summary>Reads a boolean setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">The value when unset.</param>
    /// <returns>The value.</returns>
    public bool GetBool(string key, bool defaultValue = false)
    {
        var text = GetString(key);
        return text == null ? defaultValue : text == "1" || text == "true";
    }

    /// <summary>Writes a boolean setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value.</param>
    public void SetBool(string key, bool value) => SetString(key, value ? "1" : "0");

    /// <summary>Reads an integer setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">The value when unset.</param>
    /// <returns>The value.</returns>
    public int GetInt(string key, int defaultValue = 0)
    {
        var text = GetString(key);
        return text != null
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    /// <summary>Writes an integer setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value.</param>
    public void SetInt(string key, int value)
        => SetString(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Lists the keys that start with a prefix, in key order — the store's
    /// answer to QSettings' <c>allKeys()</c> within a group.
    /// </summary>
    /// <param name="prefix">The key prefix, e.g. <c>shortcuts/default/main/</c>.</param>
    /// <returns>The matching keys.</returns>
    public IReadOnlyList<string> KeysWithPrefix(string prefix)
    {
        List<string> keys = new List<string>();
        using var command = _database.CreateCommand(
            "SELECT key FROM settings WHERE key LIKE '"
            + Escape(prefix ?? string.Empty) + "%' ESCAPE '\\' ORDER BY key;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    /// <summary>Removes a setting.</summary>
    /// <param name="key">The setting key.</param>
    public void Remove(string key) => SetString(key, null);

    /// <summary>Removes every setting whose key starts with a prefix.</summary>
    /// <param name="prefix">The key prefix.</param>
    public void RemoveWithPrefix(string prefix)
        => _database.ExecuteNonQuery(
            "DELETE FROM settings WHERE key LIKE '"
            + Escape(prefix ?? string.Empty) + "%' ESCAPE '\\';");

    /// <summary>Reads a floating-point setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">The value when unset.</param>
    /// <returns>The value.</returns>
    public double GetDouble(string key, double defaultValue = 0.0)
    {
        var text = GetString(key);
        return text != null
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    /// <summary>Writes a floating-point setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value.</param>
    public void SetDouble(string key, double value)
        => SetString(key, value.ToString("R", CultureInfo.InvariantCulture));

    /// <summary>Closes the store.</summary>
    public void Dispose() => _database.Dispose();

    private static string Escape(string text)
        => (text ?? string.Empty).Replace("'", "''");
}
