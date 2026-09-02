// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.AppSettings;
using System;
using System.Globalization;

namespace Fresco.Brix.Services;

/// <summary>
/// The application's single settings store — a THIN FACADE over the
/// <c>CodeBrix.Platform.AppSettings</c> add-in's <see cref="AppSettingsStore"/>.
/// Every preference the application persists goes through here, and the add-in
/// owns the file: one portable <c>settings.sqlite</c> under
/// <c>&lt;ApplicationData&gt;/CodeBrix/Fresco.Brix/settings/</c>, with a
/// timestamped automatic backup and retention pruning on every start,
/// quarantine of a corrupt database and restore from the newest good backup,
/// and silent first-run creation.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: a hand-rolled <c>settings</c> key/value table opened
/// directly on <c>CodeBrix.Sqlite</c>'s <c>SqliteDatabase</c>, in
/// <c>&lt;ApplicationData&gt;/Fresco.Brix/settings.sqlite</c>. The cut-over
/// (board W13 item 9, ruled by Jeremy on 2026-09-01) carries NO data across:
/// the old file is left exactly where it is, orphaned, and the application
/// takes its first-run path with defaults.
/// </para>
/// <para>
/// The scalar accessors keep the store's historical TEXT encoding — a flag is
/// <c>"1"</c>/<c>"0"</c>, a number its invariant-culture text — because every
/// one of the hundred-odd call sites was written against it, and because the
/// add-in's typed <c>Get&lt;T&gt;</c> answers the DEFAULT (silently) when the
/// stored JSON is not of the type asked for, so a key written one way and read
/// another would quietly lose its value. What is typed JSON is the key
/// FAMILIES: the add-in has no prefix-scan API by design, so each family that
/// used to be a subtree of keys is now ONE key holding a list or a dictionary,
/// read and written through <see cref="Get{T}"/> and <see cref="Set{T}"/>.
/// See <c>Commands/ActionCollection</c>, <c>Snippets/SnippetLibrary</c>,
/// <c>Sessions/SessionStore</c>, <c>Documents/MetaInfo</c>,
/// <c>Widgets/SchemeSelector</c>, <c>Editor/TextFormats</c> and
/// <c>Services/RecentFiles</c>.
/// </para>
/// </remarks>
public sealed class SettingsStore : IDisposable
{
    /// <summary>The application name the settings store is registered under.</summary>
    public const string AppName = "Fresco.Brix";

    private static readonly object InitializeLock = new object();

    private readonly AppSettingsStore _store;
    private readonly bool _ownsStore;

    static SettingsStore()
    {
        //The add-in's own logging service writes to the console BY DEFAULT, and
        //that write bypasses the logging the application configures. Fresco.Brix
        //filters the whole "CodeBrix.Platform" category to Warning
        //(App.InitializeLogging), so the add-in's informational lines — "Settings
        //auto-backup created: …" on every start — are not wanted on the console
        //either. Forwarding to the ambient logger is left ON: the application's
        //own filters then decide, which is the point of having them. This runs
        //before ANY store is opened, including the single-instance check that
        //reads one setting before the application has built its container.
        AppSettingLoggingService.ConsoleOutput = false;
    }

    /// <summary>
    /// Opens (creating if needed) the store in the add-in's default per-user
    /// location. Every caller in the running application shares ONE store: the
    /// dependency-injection singleton and the single-instance check that runs
    /// before it both land here, so the add-in's start-up backup and pruning
    /// pass happens once per process rather than once per opener.
    /// </summary>
    public SettingsStore()
    {
        lock (InitializeLock)
        {
            if (!AppSettingsService.IsInitialized)
            {
                AppSettingsService.Initialize(AppName);
            }
        }

        _store = AppSettingsService.Store;
        _ownsStore = false;
    }

    /// <summary>
    /// Opens (creating if needed) a store of its own in a directory of its own
    /// — the seam tests use, so that no test ever touches the real store.
    /// </summary>
    /// <param name="directoryPath">The folder the <c>settings.sqlite</c> and
    /// its backups live in.</param>
    /// <remarks>//was previously: this took the database FILE's path; the
    /// add-in locates the file itself inside a folder it owns.</remarks>
    public SettingsStore(string directoryPath)
    {
        _store = new AppSettingsStore(AppName, directoryPath);
        _ownsStore = true;
    }

    /// <summary>Gets the folder the store lives in.</summary>
    public string DirectoryPath => _store.DirectoryPath;

    /// <summary>Gets the database file's full path.</summary>
    public string DatabaseFilePath => _store.DatabaseFilePath;

    /// <summary>Answers whether the store was created empty on this open.</summary>
    public bool WasCreatedFresh => _store.WasCreatedFresh;

    /// <summary>Gets the default store folder:
    /// <c>&lt;ApplicationData&gt;/CodeBrix/Fresco.Brix/settings</c>.</summary>
    /// <returns>The folder.</returns>
    public static string DefaultDirectory()
        => AppSettingsStore.GetDefaultDirectory(AppName);

    /// <summary>Gets the default store path:
    /// <c>&lt;ApplicationData&gt;/CodeBrix/Fresco.Brix/settings/settings.sqlite</c>.</summary>
    /// <returns>The path.</returns>
    /// <remarks>//was previously:
    /// <c>&lt;ApplicationData&gt;/Fresco.Brix/settings.sqlite</c>, which the
    /// facade wrote itself. That file is NOT read, moved or deleted.</remarks>
    public static string DefaultPath()
        => System.IO.Path.Combine(DefaultDirectory(), AppSettingsStore.SettingsFileName);

    /// <summary>Answers whether anything is stored under a key.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns>Whether a value is stored.</returns>
    public bool HasValue(string key)
        => !string.IsNullOrEmpty(key) && _store.HasValue(key);

    /// <summary>Reads a string setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">The value when unset.</param>
    /// <returns>The value.</returns>
    public string GetString(string key, string defaultValue = null)
    {
        if (string.IsNullOrEmpty(key)) { return defaultValue; }

        return _store.Get(key, defaultValue);
    }

    /// <summary>Writes a string setting (null removes it).</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove.</param>
    public void SetString(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) { return; }

        _store.Set(key, value);
    }

    /// <summary>Reads a boolean setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">The value when unset.</param>
    /// <returns>The value.</returns>
    public bool GetBool(string key, bool defaultValue = false)
    {
        string text = GetString(key);
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
        string text = GetString(key);
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

    /// <summary>Reads a floating-point setting.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">The value when unset.</param>
    /// <returns>The value.</returns>
    public double GetDouble(string key, double defaultValue = 0.0)
    {
        string text = GetString(key);
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

    /// <summary>
    /// Reads a whole key FAMILY back — a list or a dictionary the add-in
    /// stored as JSON under one key.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="key">The family key.</param>
    /// <returns>The value, or <see langword="null"/> (or the type's default)
    /// when nothing is stored.</returns>
    /// <remarks>The add-in answers the type's default rather than throwing when
    /// what is stored is not of the type asked for, so a family key is never
    /// shared with a scalar one.</remarks>
    public T Get<T>(string key)
    {
        if (string.IsNullOrEmpty(key)) { return default; }

        return _store.Get<T>(key);
    }

    /// <summary>Reads a whole key family back, with a fallback.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="key">The family key.</param>
    /// <param name="defaultValue">The value when nothing is stored.</param>
    /// <returns>The value.</returns>
    public T Get<T>(string key, T defaultValue)
    {
        if (string.IsNullOrEmpty(key)) { return defaultValue; }

        return _store.Get(key, defaultValue);
    }

    /// <summary>
    /// Writes a whole key family — a list or a dictionary the add-in serialises
    /// to JSON under one key. A null value removes the key.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="key">The family key.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove it.</param>
    public void Set<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key)) { return; }

        _store.Set(key, value);
    }

    /// <summary>Removes a setting.</summary>
    /// <param name="key">The setting key.</param>
    public void Remove(string key) => SetString(key, null);

    /// <summary>
    /// Closes the store — but only when this facade opened one of its own. The
    /// shared default store belongs to the add-in's static service and outlives
    /// every facade over it, which is what lets the single-instance check open
    /// the settings before the application does and close them again without
    /// taking the application's store with it.
    /// </summary>
    public void Dispose()
    {
        if (_ownsStore) { _store.Dispose(); }
    }
}
