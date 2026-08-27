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

namespace Fresco.Brix.Services; //was previously: frescobaldi/hyphendialog.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Finds the hyphenation dictionaries this machine offers: the ones the
/// application brings with it, and any a word processor has already installed.
/// </summary>
/// <remarks>
/// <para>
/// This is the finding half of upstream's hyphenation dialog —
/// <c>default_paths</c>, <c>directories()</c> and <c>findDicts()</c> — kept
/// apart from the dialog because it is where the preferences page that edits
/// the path list will reach (W12), and because it can then be tested without
/// a window.
/// </para>
/// <para>
/// The bundled folder is searched LAST and therefore WINS, which is upstream's
/// order: a dictionary shipped with the application is known to be the one it
/// was tested against. Everything else is a bonus — a machine with
/// <c>/usr/share/hyphen</c> populated simply offers more languages.
/// </para>
/// <para>
/// ⚠ This is not a system-font-style fallback (standing rule 6). Nothing here
/// is a substitute for something the application failed to bring: the bundled
/// set is always present and always complete. The extra directories only ever
/// ADD languages, and a machine with none of them behaves exactly like the
/// machine the application was built on.
/// </para>
/// </remarks>
public static class HyphenDictionaries
{
    /// <summary>The settings key the searched paths are remembered under.</summary>
    public const string PathsKey = "hyphenation/paths";

    /// <summary>The settings key the last language used is remembered under.</summary>
    public const string LastUsedKey = "hyphenation/lastused";

    /// <summary>The relative paths searched under each prefix by default.</summary>
    /// <remarks>Upstream's <c>default_paths</c>, verbatim: the places the
    /// word processors of the last twenty years have put these files.</remarks>
    public static readonly IReadOnlyList<string> DefaultPaths = new[]
    {
        "share/hyphen",
        "share/myspell",
        "share/myspell/dicts",
        "share/dict/ooo",
        "share/apps/koffice/hyphdicts",
        "lib/scribus/dicts",
        "share/scribus/dicts",
        "share/scribus-ng/dicts",
        "share/hunspell",
    };

    /// <summary>The prefixes a relative path is looked for under.</summary>
    private static readonly string[] Prefixes = { "/usr/", "/usr/local/" };

    /// <summary>
    /// Gets the folder the application's own dictionaries were installed in.
    /// </summary>
    /// <remarks>Upstream's <c>hyphdicts.path</c>. The files sit beside the
    /// program, as the layout-control formatters do.</remarks>
    public static string BundledDirectory
        => Path.Combine(AppContext.BaseDirectory, "assets", "hyphdicts");

    /// <summary>Gets the directories that will be searched, in order.</summary>
    /// <param name="settings">The store the path list is read from, or null
    /// for the defaults.</param>
    /// <returns>The directories that exist.</returns>
    public static IReadOnlyList<string> Directories(SettingsStore settings = null)
    {
        IReadOnlyList<string> paths = ConfiguredPaths(settings);
        List<string> found = new List<string>();

        foreach (var path in paths)
        {
            if (Path.IsPathRooted(path))
            {
                found.Add(path);
                continue;
            }

            foreach (var prefix in Prefixes)
            {
                found.Add(Path.Combine(prefix, path));
            }
        }

        found.Add(BundledDirectory);
        return found.Where(SafelyExists).ToArray();
    }

    /// <summary>Gets the paths that will be searched, before existence.</summary>
    /// <param name="settings">The store, or null for the defaults.</param>
    /// <returns>The configured relative or absolute paths.</returns>
    public static IReadOnlyList<string> ConfiguredPaths(SettingsStore settings = null)
    {
        string stored = settings?.GetString(PathsKey);
        if (string.IsNullOrEmpty(stored)) { return DefaultPaths; }

        string[] paths = stored.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return paths.Length == 0 ? DefaultPaths : paths;
    }

    /// <summary>Remembers the paths to search.</summary>
    /// <param name="settings">The store.</param>
    /// <param name="paths">The paths, or null to go back to the defaults.</param>
    public static void SetConfiguredPaths(
        SettingsStore settings, IReadOnlyList<string> paths)
    {
        if (settings == null) { return; }

        if (paths == null || paths.Count == 0)
        {
            settings.Remove(PathsKey);
            return;
        }

        settings.SetString(PathsKey, string.Join("\n", paths));
    }

    /// <summary>
    /// Finds every dictionary, by the language code in its file name.
    /// </summary>
    /// <param name="settings">The store the path list is read from, or null
    /// for the defaults.</param>
    /// <returns>The dictionaries, language code to file.</returns>
    /// <remarks><c>hyph_nl_NL.dic</c> is the language <c>nl_NL</c>. A later
    /// directory wins, which puts the application's own set on top.</remarks>
    public static IReadOnlyDictionary<string, string> FindDictionaries(
        SettingsStore settings = null)
    {
        Dictionary<string, string> found
            = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var directory in Directories(settings))
        {
            foreach (var file in SafeFiles(directory))
            {
                string name = Path.GetFileNameWithoutExtension(file);

                //"hyph_" is five characters; what follows is the language.
                if (name.Length <= 5) { continue; }

                found[name.Substring(5)] = file;
            }
        }

        return found;
    }

    private static bool SafelyExists(string directory)
    {
        try
        {
            return Directory.Exists(directory);
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

    private static IEnumerable<string> SafeFiles(string directory)
    {
        try
        {
            string[] files = Directory.GetFiles(directory, "hyph_*.dic");
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
