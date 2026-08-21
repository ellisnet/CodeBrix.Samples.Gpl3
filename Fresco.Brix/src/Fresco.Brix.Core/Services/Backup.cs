// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;

namespace Fresco.Brix.Services; //was previously: frescobaldi/backup.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Keeps a copy of a file before it is overwritten, so a bad save is
/// recoverable. The copy is removed again after a successful save unless the
/// user asked to keep it.
/// </summary>
public sealed class Backup
{
    /// <summary>The setting deciding whether backups are kept after a save.</summary>
    public const string KeepSettingKey = "backup_keep";

    /// <summary>The setting holding the backup naming scheme.</summary>
    public const string SchemeSettingKey = "backup_scheme";

    /// <summary>The default scheme: the file name with a tilde appended.</summary>
    public const string DefaultScheme = "FILE~";

    private readonly SettingsStore _settings;

    /// <summary>Creates the backup helper.</summary>
    /// <param name="settings">The store the scheme and keep-flag live in, or
    /// null to use the defaults.</param>
    public Backup(SettingsStore settings = null) => _settings = settings;

    /// <summary>
    /// Gets the naming scheme, a string containing <c>FILE</c> where the file
    /// name goes.
    /// </summary>
    /// <returns>The scheme.</returns>
    /// <remarks>A stored scheme that does not name the file, or is nothing but
    /// the file, is ignored — upstream asserts on it, which would be a crash
    /// in front of the user for a setting they could have typed.</remarks>
    public string Scheme()
    {
        string scheme = _settings?.GetString(SchemeSettingKey, DefaultScheme)
            ?? DefaultScheme;
        return scheme.Contains("FILE", StringComparison.Ordinal) && scheme != "FILE"
            ? scheme
            : DefaultScheme;
    }

    /// <summary>Gets the backup path for a file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The backup path.</returns>
    public string BackupName(string path)
        => Scheme().Replace("FILE", path, StringComparison.Ordinal);

    /// <summary>Copies a file to its backup path.</summary>
    /// <param name="path">The file to back up.</param>
    /// <returns>Whether the copy succeeded.</returns>
    public bool Create(string path)
    {
        if (string.IsNullOrEmpty(path)) { return false; }

        try
        {
            File.Copy(path, BackupName(path), overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes a file's backup, unless the user configured backups to be kept.
    /// </summary>
    /// <param name="path">The file whose backup to remove.</param>
    public void Remove(string path)
    {
        if (string.IsNullOrEmpty(path)) { return; }
        if (_settings?.GetBool(KeepSettingKey) == true) { return; }

        try
        {
            File.Delete(BackupName(path));
        }
        catch (IOException)
        {
            //A backup that cannot be removed is not worth interrupting a save.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
