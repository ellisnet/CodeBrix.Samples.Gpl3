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

namespace Fresco.Brix.Manuscripts; //was previously: frescobaldi/viewers/__init__.py (class ViewdocChooserAction)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One manuscript the viewer has open: a PDF file, and whether it is
/// still there.</summary>
/// <remarks>
/// Upstream's "viewdoc" is a loaded <c>qpageview.Document</c> carrying a
/// <c>filename()</c> and an <c>ispresent</c> flag that is set when the file was
/// found and cleared when it was not. Here the entry is the FILE, and what the
/// rasteriser made of it hangs off it once the panel has shown it — which is
/// what lets the list, and every rule over it, be tested with no window and no
/// PDF library.
/// </remarks>
public sealed class ManuscriptEntry
{
    /// <summary>Creates an entry over a file.</summary>
    /// <param name="path">The PDF's path.</param>
    /// <param name="isPresent">Whether the file was found.</param>
    public ManuscriptEntry(string path, bool isPresent = true)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Name = System.IO.Path.GetFileName(path);
        IsPresent = isPresent;
    }

    /// <summary>Gets the PDF's path.</summary>
    public string Path { get; }

    /// <summary>Gets the file's own name, which is what the chooser shows.</summary>
    /// <remarks>Upstream's list model displays <c>os.path.basename</c> and puts
    /// the whole path in the tool tip.</remarks>
    public string Name { get; }

    /// <summary>Gets or sets whether the file was found on disk.</summary>
    public bool IsPresent { get; set; }

    /// <summary>Gets or sets the opened PDF, or null until it has been shown.</summary>
    public PdfManuscript Opened { get; set; }

    /// <inheritdoc/>
    public override string ToString() => Path;
}

/// <summary>Names the manuscripts a session asked for that are not there.</summary>
public sealed class MissingManuscriptsEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="paths">The files that are missing.</param>
    public MissingManuscriptsEventArgs(IReadOnlyList<string> paths)
        => Paths = paths ?? Array.Empty<string>();

    /// <summary>Gets the files that are missing.</summary>
    public IReadOnlyList<string> Paths { get; }
}

/// <summary>
/// The manuscripts the viewer has open, and every rule about opening, choosing
/// and closing them.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>ViewdocChooserAction</c> is a <c>QWidgetAction</c> that IS a
/// combo box AND owns this list AND follows the main window's current document.
/// The three are separated here: this class is the list and its rules, with no
/// control and no window, so the behaviours the guide promises — "new files are
/// added to the list of open manuscripts", Close, Close other, Close all — are
/// provable without a host.
/// </para>
/// <para>
/// The Manuscript Viewer's own subclass overrides <c>slotEditdocChanged</c> and
/// <c>slotEditdocUpdated</c> to do NOTHING ("when we have a tie between
/// documents and manuscripts something will have to be done here"), so the two
/// halves of upstream's class that follow the editor are simply not here: a
/// manuscript list is the user's, and nothing the editor does disturbs it.
/// </para>
/// </remarks>
public sealed class ManuscriptList
{
    private readonly List<ManuscriptEntry> _entries = new List<ManuscriptEntry>();

    private int _currentIndex = -1;

    /// <summary>Raised when the set of open manuscripts changed.</summary>
    /// <remarks>Upstream's <c>viewdocsChanged</c>.</remarks>
    public event EventHandler Changed;

    /// <summary>Raised when a different manuscript became the current one.</summary>
    /// <remarks>Upstream's <c>currentViewdocChanged</c>.</remarks>
    public event EventHandler CurrentChanged;

    /// <summary>Raised when a manuscript a session asked for is not there.</summary>
    /// <remarks>Upstream's <c>viewdocsMissing</c>.</remarks>
    public event EventHandler<MissingManuscriptsEventArgs> Missing;

    /// <summary>Gets the open manuscripts, in the order they were opened.</summary>
    public IReadOnlyList<ManuscriptEntry> Entries => _entries;

    /// <summary>Gets how many are open.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets which one is shown, or -1 when none is.</summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>Gets the manuscript being shown, or null.</summary>
    public ManuscriptEntry Current
        => _currentIndex >= 0 && _currentIndex < _entries.Count
            ? _entries[_currentIndex]
            : null;

    /// <summary>Gets the open manuscripts' paths, in order.</summary>
    /// <returns>The paths.</returns>
    public IReadOnlyList<string> Paths()
        => _entries.Select(entry => entry.Path).ToList();

    /// <summary>Answers whether a file is already open.</summary>
    /// <param name="path">The path.</param>
    /// <returns>Whether it is.</returns>
    public bool Contains(string path)
        => _entries.Any(entry => string.Equals(entry.Path, path, StringComparison.Ordinal));

    /// <summary>
    /// Opens files, skipping any that are already open, and brings the LAST of
    /// them to the front.
    /// </summary>
    /// <param name="paths">The files.</param>
    /// <param name="sort">Whether to sort the whole list by file name after.</param>
    /// <returns>The entries that were added.</returns>
    /// <remarks>
    /// Upstream's <c>loadFiles</c> then <c>loadViewdocs</c>: a file already in
    /// the list is not opened twice, each new one records whether it exists,
    /// and <c>files[-1]</c> — the last one chosen — is made active.
    /// </remarks>
    public IReadOnlyList<ManuscriptEntry> Load(IEnumerable<string> paths, bool sort = false)
    {
        List<string> wanted = (paths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrEmpty(path))
            .ToList();
        if (wanted.Count == 0) { return Array.Empty<ManuscriptEntry>(); }

        List<ManuscriptEntry> added = new List<ManuscriptEntry>();
        foreach (string path in wanted)
        {
            if (Contains(path)) { continue; }

            added.Add(new ManuscriptEntry(path, File.Exists(path)));
        }

        return Load(added, wanted[^1], sort);
    }

    /// <summary>Adds entries and makes one of them active.</summary>
    /// <param name="entries">The entries to add.</param>
    /// <param name="activePath">Which file to bring to the front, or null.</param>
    /// <param name="sort">Whether to sort by file name after.</param>
    /// <returns>The entries that were added.</returns>
    /// <remarks>Upstream's <c>loadViewdocs</c>, which is also how a session
    /// restores: the entries are made from what was stored, the one marked
    /// active is chosen, and the list is refreshed once at the end.</remarks>
    public IReadOnlyList<ManuscriptEntry> Load(
        IReadOnlyList<ManuscriptEntry> entries, string activePath = null, bool sort = false)
    {
        List<ManuscriptEntry> added = (entries ?? Array.Empty<ManuscriptEntry>())
            .Where(entry => entry != null && !Contains(entry.Path))
            .ToList();
        _entries.AddRange(added);

        //"will automatically 'pass' if empty" — upstream sets the active
        //document WITHOUT refreshing, so the refresh below happens once.
        SetActive(activePath, update: false);
        if (sort) { Sort(update: false); }

        Update();
        return added;
    }

    /// <summary>Closes one manuscript.</summary>
    /// <param name="entry">The manuscript, or null for nothing.</param>
    /// <remarks>Upstream's <c>removeViewdoc</c>. The index is NOT moved, so the
    /// manuscript that takes the closed one's place becomes current — and when
    /// the last one is closed the index falls back to the first
    /// (<c>updateViewdoc</c>'s clamp).</remarks>
    public void Remove(ManuscriptEntry entry)
    {
        if (entry == null || !_entries.Remove(entry)) { return; }

        entry.Opened?.Dispose();
        entry.Opened = null;
        Update();
    }

    /// <summary>Closes every manuscript but one.</summary>
    /// <param name="keep">The one to keep, or null to close them all.</param>
    /// <remarks>Upstream's <c>removeOtherViewdocs</c>.</remarks>
    public void RemoveOthers(ManuscriptEntry keep)
    {
        foreach (ManuscriptEntry entry in _entries)
        {
            if (entry == keep) { continue; }

            entry.Opened?.Dispose();
            entry.Opened = null;
        }

        _entries.Clear();
        if (keep != null) { _entries.Add(keep); }

        Update();
    }

    /// <summary>Closes every manuscript.</summary>
    /// <param name="update">Whether to announce the change.</param>
    /// <remarks>Upstream's <c>removeAllViewdocs</c>, whose <c>update</c>
    /// argument exists for exactly one caller: restoring a session, which
    /// empties the list and refills it in one step.</remarks>
    public void RemoveAll(bool update = true)
    {
        foreach (ManuscriptEntry entry in _entries)
        {
            entry.Opened?.Dispose();
            entry.Opened = null;
        }

        _entries.Clear();
        if (update) { Update(); } else { _currentIndex = -1; }
    }

    /// <summary>Brings a file to the front, if it is open.</summary>
    /// <param name="path">The file.</param>
    /// <param name="update">Whether to announce the change.</param>
    /// <returns>Whether the file was in the list.</returns>
    /// <remarks>Upstream's <c>setActiveViewdoc</c>.</remarks>
    public bool SetActive(string path, bool update = true)
    {
        if (string.IsNullOrEmpty(path)) { return false; }

        int index = _entries.FindIndex(
            entry => string.Equals(entry.Path, path, StringComparison.Ordinal));
        if (index < 0) { return false; }

        _currentIndex = index;
        if (update) { Update(); }

        return true;
    }

    /// <summary>Shows one of the open manuscripts.</summary>
    /// <param name="index">Which one.</param>
    /// <remarks>Upstream's <c>setCurrentIndex</c>: an empty list ignores it, and
    /// choosing one always announces, because the chooser and the view both
    /// follow that announcement.</remarks>
    public void SetCurrentIndex(int index)
    {
        if (_entries.Count == 0) { return; }

        if (index < 0 || index >= _entries.Count) { return; }

        _currentIndex = index;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sorts the open manuscripts by file name.</summary>
    /// <param name="update">Whether to announce the change.</param>
    /// <remarks>Upstream's <c>sortViewdocs</c>: "sort the open manuscripts
    /// alphabetically", by <c>os.path.basename</c>.</remarks>
    public void Sort(bool update = true)
    {
        ManuscriptEntry current = Current;
        _entries.Sort((left, right) => string.Compare(
            left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        if (current != null) { _currentIndex = _entries.IndexOf(current); }

        if (update) { Update(); }
    }

    /// <summary>Says which manuscripts are missing, if any are.</summary>
    /// <remarks>Upstream's <c>checkMissingFiles</c>, called after a session has
    /// been restored.</remarks>
    public void CheckMissingFiles()
    {
        List<string> missing = _entries
            .Where(entry => !entry.IsPresent)
            .Select(entry => entry.Path)
            .ToList();
        if (missing.Count > 0)
        {
            Missing?.Invoke(this, new MissingManuscriptsEventArgs(missing));
        }
    }

    /// <summary>Refreshes the list and announces both changes.</summary>
    /// <remarks>Upstream's <c>updateViewdoc</c>, whose clamp is the rule that
    /// decides which manuscript is current after one is closed.</remarks>
    private void Update()
    {
        //⚠ THE CLAMP HAPPENS BEFORE EITHER ANNOUNCEMENT, and that ordering is
        //load-bearing.
        //was previously: `Changed' was raised FIRST and `_currentIndex' was
        //assigned after it. Every listener therefore read a STALE index — and
        //the chooser, which writes it straight onto a ComboBox, wrote an index
        //that no longer existed. Closing the other manuscripts (three open,
        //the second showing, index 1; one left, so index 1 is out of range)
        //left the chooser blank and the list's own index out of range, so the
        //panel believed nothing was current while the page was still on
        //screen — the context menu's Reload entry, which appears only when
        //something IS current, vanished. Found on X11 at board wave W15 and
        //fixed here, at the one place the rule lives.
        int index = _entries.Count == 0
            ? -1
            : _currentIndex < 0 || _currentIndex >= _entries.Count ? 0 : _currentIndex;
        _currentIndex = index;

        Changed?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
