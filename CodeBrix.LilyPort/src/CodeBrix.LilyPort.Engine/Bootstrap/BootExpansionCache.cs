// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Caching;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// Process-wide management of the boot expansion cache. Macro-expanding the vendored
/// Scheme layer is ~99% of a cold boot (measured: ~25 s of ~26 s); replaying recorded
/// Tree-IL is ~50 ms. This class decides whether a saved recording is valid for the
/// CURRENT world, hands each new interpreter its own private cache instance, and saves
/// a fresh recording after the first live boot.
/// </summary>
/// <remarks>
/// <para>
/// The validity key is a SHA-256 over: a format marker; the MVIDs of the
/// CodeBrix.LilyScheme and CodeBrix.LilyPort.Engine assemblies (deterministic builds
/// make an MVID a content identity of its compilation, so any real change to either
/// binary invalidates the cache and a no-change rebuild does not); and the name and
/// content of every embedded <c>.scm</c> resource in both assemblies. Any mismatch is
/// simply a miss — the boot loads live, exactly as before the cache existed, and
/// re-records.
/// </para>
/// <para>
/// Each interpreter must get its OWN deserialized instance (recorded Tree-IL holds
/// quoted constants that become live mutable data), so the process memo keeps the
/// serialized BYTES and <see cref="Acquire"/> deserializes per call — ~75 ms against
/// the ~25 s it replaces.
/// </para>
/// </remarks>
public static class BootExpansionCache
{
    /// <summary>
    /// The environment variable that disables the cache when set to <c>0</c> — for
    /// A/B comparison against live boots. Any other value (or none) leaves it enabled.
    /// </summary>
    public const string EnabledVariable = "LILYPORT_EXPANSION_CACHE";

    /// <summary>
    /// The environment variable overriding the cache directory. Unset, the per-user
    /// cache location for the platform is used: <c>$XDG_CACHE_HOME/CodeBrix.LilyScheme</c>
    /// when XDG_CACHE_HOME is set; otherwise <c>%LOCALAPPDATA%\CodeBrix.LilyScheme</c> on
    /// Windows, <c>~/Library/Caches/CodeBrix.LilyScheme</c> on macOS, and
    /// <c>~/.cache/CodeBrix.LilyScheme</c> on Linux.
    /// </summary>
    public const string DirectoryVariable = "LILYPORT_EXPANSION_CACHE_DIR";

    private const int PrunedGenerationsKept = 3;

    private static readonly object Sync = new object();
    private static string _key;
    private static byte[] _bytes;
    private static bool _probedDisk;

    /// <summary>Gets a value indicating whether the cache is enabled for this process.</summary>
    public static bool Enabled
        => !string.Equals(
            Environment.GetEnvironmentVariable(EnabledVariable), "0", StringComparison.Ordinal);

    /// <summary>Gets the resolved cache directory.</summary>
    public static string CacheDirectory
    {
        get
        {
            string overridden = Environment.GetEnvironmentVariable(DirectoryVariable);
            if (!string.IsNullOrEmpty(overridden))
            {
                return overridden;
            }

            string xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrEmpty(xdg))
            {
                return Path.Combine(xdg, "CodeBrix.LilyScheme");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CodeBrix.LilyScheme");
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(home, "Library", "Caches", "CodeBrix.LilyScheme");
            }

            return Path.Combine(home, ".cache", "CodeBrix.LilyScheme");
        }
    }

    /// <summary>Gets the cache file path for the current world key.</summary>
    public static string CacheFilePath
        => Path.Combine(CacheDirectory, "boot-" + Key.Substring(0, 16) + ".lsxc");

    private static string Key
    {
        get
        {
            lock (Sync)
            {
                if (_key == null)
                {
                    _key = ComputeKey();
                }

                return _key;
            }
        }
    }

    /// <summary>
    /// Hands out a cache instance for one new interpreter: a replayable instance
    /// deserialized from the saved recording when one matches the current world, an
    /// empty recording instance otherwise, or null when the cache is disabled.
    /// </summary>
    /// <returns>The instance to assign to <see cref="Interpreter.ExpansionCache"/>.</returns>
    public static ExpansionCache Acquire()
    {
        if (!Enabled)
        {
            return null;
        }

        lock (Sync)
        {
            if (!_probedDisk)
            {
                _probedDisk = true;
                try
                {
                    string path = CacheFilePath;
                    if (File.Exists(path))
                    {
                        _bytes = File.ReadAllBytes(path);
                    }
                }
                catch (Exception)
                {
                    // Unreadable is a miss, never a failure.
                    _bytes = null;
                }
            }

            if (_bytes != null)
            {
                try
                {
                    using (MemoryStream stream = new MemoryStream(_bytes, false))
                    {
                        ExpansionCache cache = ExpansionCacheFile.Read(stream, Key);
                        if (cache != null)
                        {
                            return cache;
                        }
                    }
                }
                catch (Exception)
                {
                    // Corrupt content falls through to a fresh recording.
                }

                _bytes = null;
            }

            return new ExpansionCache();
        }
    }

    /// <summary>
    /// Saves an interpreter's cache when it recorded anything new, refreshing the
    /// process memo so later interpreters in this process replay it. Failures are
    /// swallowed: the cache must never be able to fail a boot.
    /// </summary>
    /// <param name="interpreter">The freshly-booted interpreter.</param>
    public static void SaveIfDirty(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            return;
        }

        ExpansionCache cache = interpreter.ExpansionCache;
        if (cache == null || !cache.IsDirty || !Enabled)
        {
            return;
        }

        lock (Sync)
        {
            try
            {
                string path = CacheFilePath;
                ExpansionCacheFile.WriteFile(cache, path, Key);
                _bytes = File.ReadAllBytes(path);
                _probedDisk = true;
                Prune(path);
            }
            catch (Exception)
            {
                // A boot that cannot save its recording still booted.
            }
        }
    }

    /// <summary>
    /// Forgets the process memo, so the next <see cref="Acquire"/> probes the disk
    /// again. For tests that point <see cref="DirectoryVariable"/> at a fresh directory
    /// mid-process.
    /// </summary>
    public static void ResetProcessMemo()
    {
        lock (Sync)
        {
            _bytes = null;
            _probedDisk = false;
        }
    }

    private static void Prune(string keep)
    {
        try
        {
            List<FileInfo> generations = new DirectoryInfo(CacheDirectory)
                .GetFiles("boot-*.lsxc")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            int kept = 0;
            foreach (FileInfo file in generations)
            {
                if (string.Equals(file.FullName, keep, StringComparison.Ordinal) || kept < PrunedGenerationsKept)
                {
                    kept++;
                    continue;
                }

                try
                {
                    file.Delete();
                }
                catch (Exception)
                {
                    // A file another process holds open just survives until next time.
                }
            }
        }
        catch (Exception)
        {
            // Pruning is best-effort housekeeping.
        }
    }

    private static string ComputeKey()
    {
        using (IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            AppendString(sha, "lilyport-expansion-cache-v1");

            Assembly lilyScheme = typeof(Interpreter).Assembly;
            Assembly engine = typeof(BootExpansionCache).Assembly;
            AppendString(sha, lilyScheme.ManifestModule.ModuleVersionId.ToString("N"));
            AppendString(sha, engine.ManifestModule.ModuleVersionId.ToString("N"));

            foreach (Assembly assembly in new[] { lilyScheme, engine })
            {
                IEnumerable<string> names = assembly.GetManifestResourceNames()
                    .Where(n => n.EndsWith(".scm", StringComparison.Ordinal))
                    .OrderBy(n => n, StringComparer.Ordinal);
                foreach (string name in names)
                {
                    AppendString(sha, name);
                    using (Stream stream = assembly.GetManifestResourceStream(name))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            sha.AppendData(buffer, 0, read);
                        }
                    }
                }
            }

            return Convert.ToHexStringLower(sha.GetHashAndReset());
        }
    }

    private static void AppendString(IncrementalHash sha, string value)
    {
        sha.AppendData(Encoding.UTF8.GetBytes(value));
        sha.AppendData(new byte[] { 0 });
    }
}
