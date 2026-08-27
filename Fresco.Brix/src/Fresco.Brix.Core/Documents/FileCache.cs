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

namespace Fresco.Brix.Documents; //was previously: frescobaldi/filecache.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Remembers something worked out about a file, and forgets it again the
/// moment the file's modification time moves.
/// </summary>
/// <typeparam name="T">What is remembered about each file.</typeparam>
/// <remarks>
/// The whole point is that the check is on READ, not on write: nothing has to
/// watch the file system, and a file edited by another program simply misses
/// on its next lookup.
/// </remarks>
public class FileCache<T>
{
    private readonly Dictionary<string, (DateTime Time, T Value)> _cache
        = new Dictionary<string, (DateTime, T)>(StringComparer.Ordinal);

    /// <summary>Reads a cached value.</summary>
    /// <param name="fileName">The file.</param>
    /// <param name="value">Receives the value when it is still valid.</param>
    /// <returns>Whether a valid value was found.</returns>
    public virtual bool TryGetValue(string fileName, out T value)
    {
        value = default;
        if (fileName == null || !_cache.TryGetValue(fileName, out var entry))
        {
            return false;
        }

        if (ModifiedTime(fileName) == entry.Time)
        {
            value = entry.Value;
            return true;
        }

        _cache.Remove(fileName);
        return false;
    }

    /// <summary>Remembers a value for a file.</summary>
    /// <param name="fileName">The file.</param>
    /// <param name="value">The value.</param>
    /// <remarks>A file that cannot be stat'ed is simply not cached — upstream
    /// swallows the same error for the same reason.</remarks>
    public virtual void Set(string fileName, T value)
    {
        DateTime? time = ModifiedTime(fileName);
        if (time != null)
        {
            _cache[fileName] = (time.Value, value);
        }
    }

    /// <summary>Forgets one file.</summary>
    /// <param name="fileName">The file.</param>
    public void Remove(string fileName) => _cache.Remove(fileName ?? string.Empty);

    /// <summary>Answers whether a valid value is cached for a file.</summary>
    /// <param name="fileName">The file.</param>
    /// <returns>Whether it is cached.</returns>
    public bool Contains(string fileName) => TryGetValue(fileName, out _);

    /// <summary>Enumerates the file names whose values are still valid.</summary>
    /// <returns>The file names.</returns>
    public IEnumerable<string> FileNames()
        => _cache.Keys.ToList().Where(name => TryGetValue(name, out _));

    /// <summary>Forgets everything.</summary>
    public void Clear() => _cache.Clear();

    /// <summary>Gets the entry store, for the weak-reference variant.</summary>
    /// <returns>The store.</returns>
    private protected Dictionary<string, (DateTime Time, T Value)> Entries => _cache;

    /// <summary>Reads a file's modification time, or null when it has none.</summary>
    /// <param name="fileName">The file.</param>
    /// <returns>The time, or null.</returns>
    private protected static DateTime? ModifiedTime(string fileName)
    {
        try
        {
            return File.Exists(fileName) ? File.GetLastWriteTimeUtc(fileName) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
