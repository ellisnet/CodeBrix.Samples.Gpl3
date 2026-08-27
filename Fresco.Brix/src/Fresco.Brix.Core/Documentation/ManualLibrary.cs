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
using System.Threading.Tasks;

namespace Fresco.Brix.Documentation; //was previously: frescobaldi/lilydoc/manager.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The manuals this installation has: where they are, which of them are there,
/// and the open ones.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>lilydoc.manager</c> searches <c>/usr</c>, <c>/usr/local</c>,
/// <c>/usr/share/doc</c> and the user's own configured paths for an installed
/// LilyPond documentation tree, adds remote URLs for the stable and development
/// releases, sorts them local-before-remote and by version, and then has to
/// ASK each one over the network what version it is. Every line of that exists
/// because the documentation belongs to a LilyPond installation that may or may
/// not be there and may or may not match.
/// </para>
/// <para>
/// Here the manuals are ASSETS. Ruling FR5.1 leaves exactly one engine, so
/// there is exactly one documentation set and it is the right one by
/// construction; ruling FR8 makes them PDFs beside the application. What
/// survives of upstream's module is the one question a reader can still get a
/// wrong answer to — is it installed? — because the folder is deliberately
/// removable (see <c>assets/docs/README.txt</c>).
/// </para>
/// </remarks>
public sealed class ManualLibrary : IDisposable
{
    /// <summary>The folder the manuals live in, beside the application.</summary>
    public const string AssetsFolderName = "docs";

    private readonly Dictionary<string, PdfManual> _open
        = new Dictionary<string, PdfManual>(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ManualOutlineEntry>> _outlines
        = new Dictionary<string, IReadOnlyList<ManualOutlineEntry>>(StringComparer.Ordinal);

    private bool _disposed;

    /// <summary>Creates a library over the application's own assets folder.</summary>
    public ManualLibrary()
        : this(DefaultDirectory())
    {
    }

    /// <summary>Creates a library over a folder.</summary>
    /// <param name="directory">Where the PDFs are.</param>
    public ManualLibrary(string directory) => Directory = directory;

    /// <summary>Gets the folder the manuals are read from.</summary>
    public string Directory { get; }

    /// <summary>Gets the manuals that are actually installed, in catalog order.</summary>
    public IReadOnlyList<ManualDefinition> Installed
        => ManualCatalog.All.Where(IsInstalled).ToList();

    /// <summary>Gets whether any manual at all is installed.</summary>
    public bool Any => ManualCatalog.All.Any(IsInstalled);

    /// <summary>The folder the manuals are installed in beside the application.</summary>
    /// <returns>The path.</returns>
    /// <remarks>
    /// The same shape as every other asset set the application carries — the
    /// hyphenation dictionaries, the layout-control formatters and the
    /// SoundFont are all found this way — because a Content item is copied
    /// beside the assembly on every head.
    /// </remarks>
    public static string DefaultDirectory()
        => Path.Combine(AppContext.BaseDirectory, "assets", AssetsFolderName);

    /// <summary>Gets a manual's file path, whether or not it exists.</summary>
    /// <param name="manual">The manual.</param>
    /// <returns>The path, or null when no manual was given.</returns>
    public string PathOf(ManualDefinition manual)
        => manual == null || string.IsNullOrEmpty(Directory)
            ? null
            : Path.Combine(Directory, manual.FileName);

    /// <summary>Gets whether a manual's PDF is present.</summary>
    /// <param name="manual">The manual.</param>
    /// <returns>True when the file is there.</returns>
    public bool IsInstalled(ManualDefinition manual)
    {
        string path = PathOf(manual);
        return !string.IsNullOrEmpty(path) && File.Exists(path);
    }

    /// <summary>Opens a manual, or returns the already-open one.</summary>
    /// <param name="manual">The manual.</param>
    /// <returns>The open manual, or null when it is not installed.</returns>
    /// <remarks>An open manual is KEPT: it holds an indexed outline that cost
    /// half a second to read and a cache of rendered pages, and a reader moves
    /// between manuals.</remarks>
    public async Task<PdfManual> OpenAsync(ManualDefinition manual)
    {
        if (_disposed || manual == null) { return null; }

        if (_open.TryGetValue(manual.Name, out PdfManual already)) { return already; }

        PdfManual opened = await PdfManual.OpenAsync(manual, PathOf(manual)).ConfigureAwait(true);
        if (opened == null) { return null; }

        //Another caller may have opened the same manual while this one awaited.
        if (_open.TryGetValue(manual.Name, out already))
        {
            opened.Dispose();
            return already;
        }

        _open[manual.Name] = opened;
        _outlines[manual.Name] = opened.Outline;
        return opened;
    }

    /// <summary>Gets a manual's table of contents, reading it if need be.</summary>
    /// <param name="manual">The manual.</param>
    /// <returns>The entries; empty when the manual is not installed.</returns>
    /// <remarks>
    /// Deliberately separate from <see cref="OpenAsync"/>: contextual help
    /// searches the contents of manuals the reader has not opened, and reading
    /// an outline costs a fraction of what opening a manual with its rasteriser
    /// does. An outline read this way is kept and reused when the manual IS
    /// opened.
    /// </remarks>
    public IReadOnlyList<ManualOutlineEntry> OutlineOf(ManualDefinition manual)
    {
        if (_disposed || manual == null) { return Array.Empty<ManualOutlineEntry>(); }

        if (_outlines.TryGetValue(manual.Name, out var already)) { return already; }

        var outline = ManualOutline.Read(PathOf(manual));
        _outlines[manual.Name] = outline;
        return outline;
    }

    /// <summary>Closes every open manual.</summary>
    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        foreach (var manual in _open.Values) { manual.Dispose(); }

        _open.Clear();
        _outlines.Clear();
    }
}
