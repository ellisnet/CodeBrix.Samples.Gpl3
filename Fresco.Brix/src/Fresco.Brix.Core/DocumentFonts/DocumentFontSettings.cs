// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.IO;

namespace Fresco.Brix.DocumentFonts; //was previously: frescobaldi/fonts/__init__.py + fonts/preview.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Where the document-font feature keeps its settings, and the two folders the
/// Paths preferences page configures.
/// </summary>
/// <remarks>
/// Every key is upstream's own: the dialog's own state lives under
/// <c>document-fonts-dialog</c> and the two folders under
/// <c>music-fonts</c>, so a Frescobaldi settings file and a Fresco.Brix one
/// still mean the same thing.
/// </remarks>
public static class DocumentFontSettings
{
    /// <summary>The group the dialog's own state lives under.</summary>
    public const string DialogGroup = "document-fonts-dialog/";

    /// <summary>The key naming the folder music fonts are installed FROM.</summary>
    /// <remarks>Upstream's <c>music-fonts/font-repo</c>.</remarks>
    public const string FontRepoKey = "music-fonts/font-repo";

    /// <summary>The key naming the folder sample engravings are kept in.</summary>
    /// <remarks>Upstream's <c>music-fonts/font-cache</c>.</remarks>
    public const string FontCacheKey = "music-fonts/font-cache";

    /// <summary>The key deciding whether the repository is installed on opening.</summary>
    /// <remarks>Upstream's <c>music-fonts/auto-install</c>, default true.</remarks>
    public const string AutoInstallKey = "music-fonts/auto-install";

    /// <summary>Names one of the dialog's own settings.</summary>
    /// <param name="name">The name inside the group.</param>
    /// <returns>The full key.</returns>
    public static string Key(string name) => DialogGroup + name;

    /// <summary>
    /// Answers the music-font repository the preferences name, or null.
    /// </summary>
    /// <param name="settings">The store.</param>
    /// <returns>The repository, or null when none is configured.</returns>
    /// <remarks>Upstream's <c>set_music_fonts_repo()</c>, which is called at
    /// import and again on every settings change. Here the caller asks when it
    /// needs one, which is the same answer without the module-level cache.</remarks>
    public static MusicFontRepo MusicFontsRepo(SettingsStore settings)
    {
        string root = settings?.GetString(FontRepoKey, string.Empty);
        return string.IsNullOrEmpty(root) ? null : new MusicFontRepo(root);
    }

    /// <summary>
    /// Answers where sample engravings are kept between runs.
    /// </summary>
    /// <param name="settings">The store.</param>
    /// <returns>The configured folder, or the default one under the temporary
    /// directory.</returns>
    /// <remarks>Upstream's <c>get_persistent_cache_dir()</c>: the preference if
    /// set, otherwise <c>&lt;temp&gt;/&lt;appname&gt;-music-font-samples</c>,
    /// "which will be purged upon computer shutdown".</remarks>
    public static string PersistentCacheDirectory(SettingsStore settings)
    {
        string configured = settings?.GetString(FontCacheKey, string.Empty);
        return string.IsNullOrEmpty(configured)
            ? Path.Combine(Path.GetTempPath(), AppInfo.Name + "-music-font-samples")
            : configured;
    }

    /// <summary>Answers whether the repository is installed when the dialog opens.</summary>
    /// <param name="settings">The store.</param>
    /// <returns>Whether it is.</returns>
    public static bool AutoInstall(SettingsStore settings)
        => settings?.GetBool(AutoInstallKey, true) ?? true;
}
