// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.MusicView; //was previously: qpageview/document.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A set of pages shown together — one engraved score.
/// </summary>
/// <remarks>
/// Upstream's Document wraps ONE PDF, which is why its music view has a
/// document chooser: a run can produce several. Here a run produces one SVG
/// FILE PER PAGE, so a document is the set of files a single base name
/// produced, in page order, and the chooser lists base names rather than files.
/// </remarks>
public class MusicDocument
{
    private readonly List<ScorePage> _pages = new List<ScorePage>();

    /// <summary>Creates an empty document.</summary>
    public MusicDocument()
    {
    }

    /// <summary>Creates a document over the given pages.</summary>
    /// <param name="pages">The pages, in order.</param>
    public MusicDocument(IEnumerable<ScorePage> pages)
    {
        if (pages != null) { _pages.AddRange(pages); }
    }

    /// <summary>Gets or sets the name this document is known by.</summary>
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets whether the files are newer than the source document.
    /// </summary>
    /// <remarks>
    /// Upstream tints the chooser when they are not, which is its way of saying
    /// "this is what the score looked like before your last edit".
    /// </remarks>
    public bool Updated { get; set; } = true;

    /// <summary>Gets the pages, in order.</summary>
    public IReadOnlyList<ScorePage> Pages => _pages;

    /// <summary>Gets how many pages there are.</summary>
    public int Count => _pages.Count;

    /// <summary>Reads a document from SVG files, one page per file.</summary>
    /// <param name="fileNames">The files, in page order.</param>
    /// <param name="typefaces">Who answers the score's font families (trap 9).</param>
    /// <returns>The document.</returns>
    public static MusicDocument LoadSvgs(
        IEnumerable<string> fileNames, IScoreTypefaceResolver typefaces = null)
    {
        var names = fileNames?.ToList() ?? new List<string>();
        var document = new MusicDocument(SvgPage.Load(names, typefaces))
        {
            FileName = names.Count > 0 ? names[0] : null,
        };
        return document;
    }

    /// <summary>
    /// Replaces this document's pages with the ones a fresh run produced.
    /// </summary>
    /// <param name="pages">The new pages, in order.</param>
    /// <param name="fileName">The name the score is known by.</param>
    /// <remarks>
    /// Upstream re-points its Document objects at the new files rather than
    /// building new ones, precisely so that a viewer keeps whatever it
    /// remembers about them — which is what stops a re-engrave from throwing
    /// the reader back to page one.
    /// </remarks>
    public void SetSource(IEnumerable<ScorePage> pages, string fileName)
    {
        foreach (ScorePage page in _pages)
        {
            if (page is IDisposable disposable) { disposable.Dispose(); }
        }

        _pages.Clear();
        if (pages != null) { _pages.AddRange(pages); }

        FileName = fileName;
    }

    /// <summary>Forgets every page's parsed contents, so the files are read again.</summary>
    public void Reload()
    {
        foreach (ScorePage page in _pages)
        {
            if (page is SvgPage svg) { svg.Reload(); }
        }
    }

    /// <summary>Releases every page that holds unmanaged drawing state.</summary>
    public void Dispose()
    {
        foreach (ScorePage page in _pages)
        {
            if (page is IDisposable disposable) { disposable.Dispose(); }
        }

        _pages.Clear();
    }
}
