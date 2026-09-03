// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documentation;
using Fresco.Brix.MusicView;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fresco.Brix.Manuscripts; //was previously: frescobaldi/pagedview.py (loadPdf) + viewers/documents.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One manuscript, open: the PDF's pages ready for the paged view, and whatever
/// clickable areas the file carries.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>pagedview.loadPdf(filename)</c> — one line, because Poppler
/// does the work. The same job here is <see cref="PdfManual.OpenFileAsync"/>
/// for the pages (ruling FR8's rasteriser, the byte-capped picture cache and
/// the 2,048-pixel render cap, all of it the Documentation Browser's own and
/// deliberately not written twice) plus <see cref="PdfLinks"/> for the
/// annotations Poppler would have handed over with them.
/// </para>
/// <para>
/// The whole read happens off the UI thread, because a manuscript may be as
/// large as anything a user has on disk.
/// </para>
/// </remarks>
public sealed class PdfManuscript : IDisposable
{
    private readonly PdfManual _pdf;

    private bool _disposed;

    private PdfManuscript(PdfManual pdf, MusicDocument document, bool hasLinks)
    {
        _pdf = pdf;
        Document = document;
        HasLinks = hasLinks;
    }

    /// <summary>Gets the PDF's path.</summary>
    public string Path => _pdf.Path;

    /// <summary>Gets how many pages it has.</summary>
    public int PageCount => _pdf.PageCount;

    /// <summary>Gets the document the paged view shows.</summary>
    public MusicDocument Document { get; }

    /// <summary>Gets the pages, as picture sources.</summary>
    public IReadOnlyList<IPageImageSource> Pages => _pdf.Pages;

    /// <summary>Gets whether the file carries any clickable areas at all.</summary>
    /// <remarks>Almost every manuscript answers false; a score engraved with
    /// point-and-click links answers true, and so does a PDF that merely
    /// carries web addresses — which the guide says is a use of its own.</remarks>
    public bool HasLinks { get; }

    /// <summary>Opens a PDF as a manuscript.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The open manuscript, or null when the file is missing or
    /// unreadable.</returns>
    public static async Task<PdfManuscript> OpenAsync(string path)
    {
        PdfManual pdf = await PdfManual.OpenFileAsync(path).ConfigureAwait(true);
        if (pdf == null) { return null; }

        //The annotations are read on a worker for the reason the outline is:
        //the page tree of a large document takes real time to walk, and the
        //window must not spend it frozen.
        IReadOnlyList<LinkList> links = await Task
            .Run(() => PdfLinks.Read(path))
            .ConfigureAwait(true);

        MusicDocument document = pdf.ToDocument();
        for (int i = 0; i < document.Pages.Count && i < links.Count; i++)
        {
            if (document.Pages[i] is RasterPage page) { page.SetLinks(links[i]); }
        }

        return new PdfManuscript(pdf, document, links.Count > 0);
    }

    /// <summary>Releases the rasteriser and every cached picture.</summary>
    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        Document?.Dispose();
        _pdf.Dispose();
    }
}
