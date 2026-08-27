// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace Fresco.Brix.Midi; //was previously: frescobaldi/miditool/midifiles.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The MIDI files one source document has produced, and which of them the
/// player is on.
/// </summary>
/// <remarks>
/// The list is thrown away — not rebuilt — whenever the document is reloaded or
/// a run finishes, and read again on the next request. Upstream does the same,
/// and for the same reason: a run that is still going has not written its
/// output yet, so the answer is only worth having when somebody asks for it.
/// </remarks>
public sealed class MidiFiles : Plugin<EditorDocument, MidiFiles>
{
    private readonly List<string> _files = new List<string>();
    private readonly List<MidiSong> _songs = new List<MidiSong>();
    private bool _read;

    private MidiFiles(EditorDocument document)
        : base(document)
    {
        document.Loaded += (_, _) => Invalidate();
        JobManager.For(document).JobFinished += (_, _) => Invalidate();
    }

    /// <summary>Gets or sets which file the player is on.</summary>
    public int Current { get; set; }

    /// <summary>Gets the source document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Gets whether the document has produced any MIDI at all.</summary>
    public bool Any => Files.Count > 0;

    /// <summary>Gets the MIDI files, reading them on the first request.</summary>
    public IReadOnlyList<string> Files
    {
        get
        {
            if (!_read) { Update(); }

            return _files;
        }
    }

    /// <summary>Gets the files for a document, creating the list on first use.</summary>
    /// <param name="document">The source document.</param>
    /// <returns>The list.</returns>
    public static MidiFiles For(EditorDocument document)
        => Instance(document, owner => new MidiFiles(owner));

    /// <summary>Forgets the list, so the next request reads the disk again.</summary>
    public void Invalidate() => _read = false;

    /// <summary>Re-reads the document's MIDI output.</summary>
    /// <returns>Whether any file was found.</returns>
    public bool Update()
    {
        _read = true;
        _files.Clear();
        _songs.Clear();

        EditorDocument document = Document;
        if (document == null) { return false; }

        //".mid*" catches both .midi (what the engine writes) and the .mid a
        //file from elsewhere may use — upstream's own glob.
        foreach (string file in ResultFiles.For(document).Files(".mid*"))
        {
            _files.Add(file);
            _songs.Add(null);
        }

        if (_files.Count > 0 && Current >= _files.Count)
        {
            Current = _files.Count - 1;
        }

        return _files.Count > 0;
    }

    /// <summary>Gets a file's display name — its name without its directory.</summary>
    /// <param name="index">Which file.</param>
    /// <returns>The name, or an empty string when the index is out of range.</returns>
    public string DisplayName(int index)
        => index >= 0 && index < Files.Count
            ? Path.GetFileName(Files[index])
            : string.Empty;

    /// <summary>Gets a file as a song, loading it on the first request.</summary>
    /// <param name="index">Which file.</param>
    /// <returns>The song, or null when the index is out of range or the file
    /// cannot be read as MIDI.</returns>
    /// <remarks>
    /// Upstream lets a malformed file raise out of the panel. Here it answers
    /// null instead: the panel is showing whatever a run left on disk, a
    /// half-written file is a state a run can genuinely be caught in, and the
    /// window has no business closing over it.
    /// </remarks>
    public MidiSong Song(int index)
    {
        if (index < 0 || index >= Files.Count) { return null; }

        if (_songs[index] == null)
        {
            try { _songs[index] = MidiSong.Load(_files[index]); }
            catch (IOException) { return null; }
            catch (FormatException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        return _songs[index];
    }
}
