// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fresco.Brix.MusicView; //was previously: frescobaldi/musicview/documents.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The engraved scores one source document has produced, kept so the view can
/// show the same one again after a re-engrave without losing its place.
/// </summary>
/// <remarks>
/// <para>
/// Upstream groups PDF FILES: one run, one file, one entry in the chooser.
/// The engine here writes ONE SVG FILE PER PAGE, so a score is the set of files
/// that share a base name — <c>score.svg</c> alone, or <c>score-1.svg</c>,
/// <c>score-2.svg</c>, … in page order — and the chooser lists base names.
/// The grouping and the page order both come from <see cref="PathUtil"/>, whose
/// natural sort already puts page 2 before page 10.
/// </para>
/// </remarks>
public sealed class ScoreDocuments : Plugin<EditorDocument, ScoreDocuments>
{
    /// <summary>The setting deciding whether stale output is listed at all.</summary>
    public const string NewerFilesOnlySettingKey = "musicview/newer_files_only";

    private List<MusicDocument> _documents;

    private ScoreDocuments(EditorDocument document)
        : base(document)
    {
    }

    /// <summary>Raised when a finished run has produced new output for a document.</summary>
    public static event EventHandler<DocumentEventArgs> ScoreUpdated;

    /// <summary>Gets or sets who answers the score's font families (trap 9).</summary>
    public static IScoreTypefaceResolver Typefaces { get; set; }

    /// <summary>Gets the source document these scores were engraved from.</summary>
    public EditorDocument Document => Owner;

    /// <summary>Returns the group for a source document.</summary>
    /// <param name="document">The source document.</param>
    /// <returns>The group.</returns>
    public static ScoreDocuments For(EditorDocument document)
        => Instance(document, owner => new ScoreDocuments(owner));

    /// <summary>Tells the world a run produced new output.</summary>
    /// <param name="document">The source document.</param>
    public static void RaiseScoreUpdated(EditorDocument document)
        => ScoreUpdated?.Invoke(null, new DocumentEventArgs(document));

    /// <summary>Gets the engraved scores, reading them on the first request.</summary>
    /// <returns>The scores.</returns>
    public IReadOnlyList<MusicDocument> Documents()
    {
        if (_documents == null)
        {
            _documents = new List<MusicDocument>();
            Update();
        }

        return _documents.ToList();
    }

    /// <summary>
    /// Re-reads the source document's SVG output and rebuilds the scores.
    /// </summary>
    /// <param name="newer">
    /// Whether to list only output newer than the source; null takes the
    /// setting.
    /// </param>
    /// <param name="settings">The settings store, or null.</param>
    /// <returns>Whether any score was found.</returns>
    public bool Update(bool? newer = null, SettingsStore settings = null)
    {
        bool onlyNewer = newer ?? settings?.GetBool(NewerFilesOnlySettingKey, true) ?? true;

        ResultFiles results = ResultFiles.For(Document);
        IReadOnlyList<string> files = results.Files(".svg", onlyNewer);
        if (files.Count == 0) { return false; }

        var groups = GroupPages(files);
        var documents = new List<MusicDocument>();
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            //Re-point an EXISTING score at the new files rather than replacing
            //it: a viewer remembers where it had a score, and the score it had
            //is this object. Upstream does the same, for the same reason.
            MusicDocument document = _documents != null && i < _documents.Count
                ? _documents[i]
                : null;
            if (document == null)
            {
                document = MusicDocument.LoadSvgs(group.Pages, Typefaces);
                document.FileName = group.BaseName;
            }
            else
            {
                document.SetSource(SvgPage.Load(group.Pages, Typefaces), group.BaseName);
            }

            document.Updated = onlyNewer || group.Pages.All(results.IsNewer);
            documents.Add(document);
        }

        //Anything the run no longer produces is finished with.
        foreach (MusicDocument old in (_documents ?? Enumerable.Empty<MusicDocument>())
            .Where(d => !documents.Contains(d)))
        {
            old.Dispose();
        }

        _documents = documents;
        return true;
    }

    /// <summary>Forgets the scores; the next request reads them again.</summary>
    public void Clear()
    {
        foreach (MusicDocument document in _documents ?? Enumerable.Empty<MusicDocument>())
        {
            document.Dispose();
        }

        _documents = null;
    }

    /// <summary>
    /// Puts the SVG files of one run into scores: files that differ only by a
    /// <c>-&lt;page&gt;</c> suffix belong to the same score, in page order.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>Each score's base name and its pages, in order.</returns>
    internal static IReadOnlyList<(string BaseName, IReadOnlyList<string> Pages)> GroupPages(
        IEnumerable<string> files)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            int dash = name.LastIndexOf('-');
            string stem = dash > 0 && int.TryParse(name.AsSpan(dash + 1), out _)
                ? name.Substring(0, dash)
                : name;
            string key = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, stem);
            if (!groups.TryGetValue(key, out List<string> pages))
            {
                pages = new List<string>();
                groups[key] = pages;
                order.Add(key);
            }

            pages.Add(file);
        }

        var result = new List<(string, IReadOnlyList<string>)>();
        foreach (string key in order)
        {
            List<string> pages = groups[key];
            pages.Sort(PathUtil.CompareNaturally);
            result.Add((key + ".svg", pages));
        }

        return result;
    }
}
