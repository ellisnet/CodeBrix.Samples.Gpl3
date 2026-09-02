// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Lex;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicTree = Fresco.Brix.Ly.Music.Document;

namespace Fresco.Brix.Documents; //was previously: frescobaldi/documentinfo.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Everything the application works out about a document open in the editor:
/// its mode, its version, the files it includes, the file an engrave run is
/// performed on, and the base names that run is expected to write.
/// </summary>
/// <remarks>
/// <para>
/// Upstream computes this on a background thread, because tokenizing a large
/// document on every keystroke made the editor lag. Here the tokenization is
/// ALREADY shared and incremental — <c>DocumentEditorState</c> holds the one
/// highlighter every view and tool reads — so the expensive half upstream was
/// moving off the UI thread does not happen twice. What is left is the music
/// tree, which is rebuilt lazily on first request after a change, and the
/// staleness rule is upstream's: a change marks the answers stale, and the
/// next request recomputes.
/// </para>
/// <para>
/// The version-chooser half of upstream's class is gone with FR5.1 — there is
/// one engine, compiled in — so there is no <c>lilypondinfo()</c> here.
/// </para>
/// </remarks>
public sealed class DocumentInfo : Plugin<EditorDocument, DocumentInfo>
{
    private LyDocInfo _docInfo;
    private MusicTree _music;
    private bool _stale;

    private DocumentInfo(EditorDocument document)
        : base(document)
    {
        document.ContentsChanged += (_, _) => _stale = true;
        document.Loaded += (_, _) => Reset();
        document.Closed += (_, _) => Reset();
    }

    /// <summary>Raised when the cached answers have been recomputed.</summary>
    public event EventHandler ContentsChanged;

    /// <summary>Gets the document.</summary>
    public EditorDocument Document => Owner;

    /// <summary>
    /// Gets or sets the CURRENT SESSION's own include directories.
    /// </summary>
    /// <remarks>//was previously: <c>GlobalIncludePath</c>, which was the only
    /// one there was — the session's — and so was named for the job it was
    /// standing in for. The application-wide list is
    /// <see cref="ApplicationIncludePath"/>, and
    /// <see cref="IncludePath"/> puts the two together.</remarks>
    public static IReadOnlyList<string> SessionIncludePath { get; set; }
        = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the APPLICATION-WIDE include directories — the preferences'
    /// own list, which every session inherits.
    /// </summary>
    /// <remarks>Upstream's <c>lilypond_settings/include_path</c>, read in
    /// <c>documentinfo.includepath()</c>.</remarks>
    public static IReadOnlyList<string> ApplicationIncludePath { get; set; }
        = Array.Empty<string>();

    /// <summary>Gets the information for a document, creating it on first use.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The information.</returns>
    public static DocumentInfo For(EditorDocument document)
        => Instance(document, owner => new DocumentInfo(owner));

    /// <summary>Gets the document's information, recomputing it when stale.</summary>
    /// <returns>The information.</returns>
    public LyDocInfo DocInfo()
    {
        Refresh();
        return _docInfo;
    }

    /// <summary>Gets the document's music tree, recomputing it when stale.</summary>
    /// <returns>The music tree.</returns>
    public MusicTree Music()
    {
        Refresh();
        _music.IncludePath = IncludePath().ToList();
        return _music;
    }

    /// <summary>
    /// Gets the document's mode: the <c>mode</c> variable when it declares one,
    /// and otherwise a guess from the contents.
    /// </summary>
    /// <param name="guess">Whether to guess when nothing is declared.</param>
    /// <returns>The mode, or null when nothing is declared and guessing is off.</returns>
    public string Mode(bool guess = true)
    {
        EditorDocument document = Document;
        string declared = document == null
            ? null
            : DocumentVariables.Get(document.Text, "mode");
        if (Modes.Exists(declared)) { return declared; }

        return guess ? DocInfo().Mode() : null;
    }

    /// <summary>Gets the directories <c>\include</c> is searched in.</summary>
    /// <returns>The directories.</returns>
    /// <remarks>
    /// Upstream's <c>includepath()</c>: the application-wide list, with the
    /// session's own PREPENDED to it. (Upstream can also REPLACE the global
    /// list with the session's, behind that session's <c>repl-paths</c> flag;
    /// this port's session editor has no such flag — see the session editor's
    /// own note — so the prepending case, which is upstream's default, is the
    /// only one.)
    /// </remarks>
    public IReadOnlyList<string> IncludePath()
    {
        IReadOnlyList<string> session = SessionIncludePath ?? Array.Empty<string>();
        IReadOnlyList<string> application
            = ApplicationIncludePath ?? Array.Empty<string>();

        if (session.Count == 0) { return application; }

        if (application.Count == 0) { return session; }

        List<string> all = new List<string>(session.Count + application.Count);
        all.AddRange(session);
        all.AddRange(application);
        return all;
    }

    /// <summary>
    /// Works out the file an engrave run is performed on, and the include path
    /// that run needs.
    /// </summary>
    /// <param name="create">Whether to WRITE the scratch copy now — pass true
    /// only when a run is actually about to start.</param>
    /// <returns>The file and the include path.</returns>
    /// <remarks>
    /// A saved, unmodified document is engraved where it lives. Anything else
    /// is engraved from a scratch copy, and then the document's own directory
    /// goes on the FRONT of the include path so its relative includes still
    /// resolve.
    /// </remarks>
    public (string FileName, IReadOnlyList<string> IncludePath) JobInfo(bool create = false)
    {
        List<string> includePath = new List<string>(IncludePath());
        EditorDocument document = Document;
        string fileName = document?.Path;

        if (document != null && (string.IsNullOrEmpty(fileName) || document.IsModified))
        {
            ScratchDir scratch = ScratchDir.For(document);
            if (create) { scratch.SaveDocument(); }

            if (!string.IsNullOrEmpty(fileName))
            {
                includePath.Insert(0, Path.GetDirectoryName(fileName));
            }

            string scratchPath = scratch.Path();
            if (create || (scratchPath != null && File.Exists(scratchPath)))
            {
                fileName = scratchPath;
            }
        }

        return (fileName, includePath);
    }

    /// <summary>Gets the files this document includes, recursively.</summary>
    /// <returns>The files; the document's own is not among them.</returns>
    public IReadOnlyCollection<string> IncludeFiles()
        => LyFileInfo.IncludeFiles(DocInfo(), IncludePath());

    /// <summary>
    /// Gets the base names a run of this document is expected to write.
    /// </summary>
    /// <returns>The base names, without extension.</returns>
    /// <remarks>An <c>output</c> document variable overrides the lot: it names
    /// the base names outright, comma-separated.</remarks>
    public IReadOnlyList<string> BaseNames()
    {
        EditorDocument document = Document;
        if (document == null) { return Array.Empty<string>(); }

        string fileName = JobInfo().FileName;
        string output = DocumentVariables.Get(document.Text, "output");
        if (!string.IsNullOrEmpty(output))
        {
            string directory = Path.GetDirectoryName(fileName) ?? string.Empty;
            return output.Split(',')
                .Select(name => Path.Combine(directory, name.Trim()))
                .ToList();
        }

        //Only LilyPond documents name output; the other modes upstream lists
        //(html, texinfo, latex, docbook) all fall through to nothing there too.
        return Mode() == "lilypond"
            ? LyFileInfo.BaseNames(DocInfo(), IncludeFiles(), fileName)
            : Array.Empty<string>();
    }

    /// <summary>
    /// Gets the paths the document includes DIRECTLY, without searching the
    /// include path.
    /// </summary>
    /// <returns>The paths, empty when the document has no file of its own.</returns>
    public IReadOnlyList<string> ChildPaths()
    {
        EditorDocument document = Document;
        if (document?.Path == null) { return Array.Empty<string>(); }

        string directory = Path.GetDirectoryName(document.Path);
        return DocInfo().IncludeArgs()
            .Select(argument => Path.Combine(directory, argument))
            .ToList();
    }

    private void Reset()
    {
        _docInfo = null;
        _music = null;
        _stale = false;
    }

    private void Refresh()
    {
        if (_docInfo != null && _music != null && !_stale) { return; }

        EditorDocument document = Document;
        if (document == null) { return; }

        DocumentEditorState state = DocumentEditorState.For(document);
        _docInfo = new LyDocInfo(
            state.LyDocument, DocumentVariables.Read(document.Text));
        _music = new MusicTree(state.LyDocument);
        _stale = false;
        ContentsChanged?.Invoke(this, EventArgs.Empty);
    }
}
