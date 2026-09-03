// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.PdfDocuments.Pdf;
using CodeBrix.PdfDocuments.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Documentation;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One entry in a manual's table of contents: a heading, how deep it sits, and
/// which page it is on.
/// </summary>
public sealed class ManualOutlineEntry
{
    /// <summary>Creates an entry.</summary>
    /// <param name="title">The heading as the manual prints it, section number
    /// and all.</param>
    /// <param name="page">The 1-based page it is on.</param>
    /// <param name="level">How deep it sits; the manual's own title is 0.</param>
    internal ManualOutlineEntry(string title, int page, int level)
    {
        Title = title ?? string.Empty;
        Page = page;
        Level = level;
        Heading = StripSectionNumber(Title);
    }

    /// <summary>Matches the section number a Texinfo heading starts with.</summary>
    /// <remarks>The manuals number every heading — <c>1.1.10 BarCheckEvent</c>,
    /// <c>36.11.1 Modifying ties and slurs</c> — and appendices letter them.
    /// The number is what a reader sees and what <see cref="Title"/> keeps; the
    /// NAME is what a search has to match.</remarks>
    private static readonly Regex SectionNumber
        = new Regex(@"^\s*(?:[0-9]+|[A-Z])(?:\.[0-9]+)*\s+", RegexOptions.Compiled);

    /// <summary>Gets the heading as the manual prints it.</summary>
    public string Title { get; }

    /// <summary>Gets the heading with its section number removed.</summary>
    public string Heading { get; }

    /// <summary>Gets the 1-based page the heading is on.</summary>
    public int Page { get; }

    /// <summary>Gets how deep the heading sits; the title page is level 0.</summary>
    public int Level { get; }

    /// <inheritdoc/>
    public override string ToString() => Title;

    private static string StripSectionNumber(string title)
    {
        Match match = SectionNumber.Match(title);
        return match.Success ? title.Substring(match.Length) : title;
    }
}

/// <summary>
/// What a manual's PDF says about itself: how many pages, how big they are, and
/// its own table of contents.
/// </summary>
public sealed class ManualStructure
{
    /// <summary>Creates a structure record.</summary>
    /// <param name="pageCount">How many pages.</param>
    /// <param name="width">The page width, in points.</param>
    /// <param name="height">The page height, in points.</param>
    /// <param name="outline">The table of contents, in reading order.</param>
    /// <param name="pageSizes">Each page's own size in points, or null when
    /// every page is <paramref name="width"/> by <paramref name="height"/>.</param>
    internal ManualStructure(
        int pageCount,
        double width,
        double height,
        IReadOnlyList<ManualOutlineEntry> outline,
        IReadOnlyList<PageSize> pageSizes = null)
    {
        PageCount = pageCount;
        PageWidthPoints = width;
        PageHeightPoints = height;
        Outline = outline;
        PageSizes = pageSizes ?? Array.Empty<PageSize>();
    }

    /// <summary>Gets how many pages the manual has.</summary>
    public int PageCount { get; }

    /// <summary>Gets the page width, in points.</summary>
    public double PageWidthPoints { get; }

    /// <summary>Gets the page height, in points.</summary>
    public double PageHeightPoints { get; }

    /// <summary>Gets the table of contents, flattened into reading order.</summary>
    public IReadOnlyList<ManualOutlineEntry> Outline { get; }

    /// <summary>Gets each page's own size, in points.</summary>
    /// <remarks>
    /// //was previously: nothing — the nine bundled manuals are 595&#215;842pt
    /// from end to end (measured at W10, asserted by
    /// <c>ManualCatalogTests</c>), so ONE geometry was enough. Board wave W15
    /// opens a PDF the USER chose, which may perfectly well mix portrait and
    /// landscape or A4 and Letter, so every page's own size is read too — it
    /// costs nothing, because the page objects are already walked to build the
    /// destination map. <see cref="PageWidthPoints"/> and
    /// <see cref="PageHeightPoints"/> stay the FIRST page's, which is what the
    /// Documentation Browser reads.
    /// </remarks>
    public IReadOnlyList<PageSize> PageSizes { get; }
}

/// <summary>One page's size, in points.</summary>
/// <param name="Width">The width, in points.</param>
/// <param name="Height">The height, in points.</param>
public readonly record struct PageSize(double Width, double Height);

/// <summary>
/// A manual's table of contents, read out of the PDF's own bookmark tree.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes the panel more than a page-turner and what makes
/// contextual help land on a PAGE rather than on a manual: the renderer writes
/// every Texinfo node into the PDF outline with a destination, so the document
/// carries its own index. MEASURED at W10: 591 entries for the Notation
/// Reference, 810 for the Internals Reference — one per node, all of them
/// resolving to a page — read in 553 ms and 240 ms respectively.
/// </para>
/// <para>
/// ⚠ Reading one costs up to half a second, so it is done ONCE per manual, off
/// the UI thread, and kept.
/// </para>
/// </remarks>
public static class ManualOutline
{
    /// <summary>Reads a PDF's outline, flattened into reading order.</summary>
    /// <param name="path">The PDF.</param>
    /// <returns>The entries, in the order they appear; empty when the file has
    /// no outline or cannot be read.</returns>
    public static IReadOnlyList<ManualOutlineEntry> Read(string path)
        => ReadStructure(path)?.Outline ?? Array.Empty<ManualOutlineEntry>();

    /// <summary>Reads everything a PDF says about its own shape and contents.</summary>
    /// <param name="path">The PDF.</param>
    /// <returns>The structure, or null when the file is missing or unreadable.</returns>
    public static ManualStructure ReadStructure(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return null; }

        try
        {
            //InformationOnly: nothing here modifies the document, and the
            //cheaper open is the difference between a quarter of a second and
            //a full parse of thirty-four megabytes.
            PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly);
            if (document.PageCount < 1) { return null; }

            //A destination names a PAGE OBJECT, not a number, so the numbers
            //are looked up rather than counted — walking Pages to find each one
            //would be quadratic, and the Internals Reference has 810 of them.
            Dictionary<PdfPage, int> numbers = new Dictionary<PdfPage, int>();
            List<PageSize> sizes = new List<PageSize>(document.PageCount);
            for (int i = 0; i < document.PageCount; i++)
            {
                PdfPage page = document.Pages[i];
                numbers[page] = i + 1;

                //Board trap 65: PdfPage.Width/Height are the box AS TURNED by
                ///Rotate, which is exactly the size the page is DISPLAYED at —
                //so this is the number the paged view wants and no rotation
                //arithmetic belongs here.
                sizes.Add(new PageSize(page.Width.Point, page.Height.Point));
            }

            List<ManualOutlineEntry> entries = new List<ManualOutlineEntry>();
            Walk(document.Outlines, 0, numbers, entries);

            //⚠ ONE GEOMETRY FOR THE WHOLE MANUAL, and it is a measurement, not
            //an assumption: every one of the nine renders at 595x842pt from end
            //to end, because the Texinfo renderer lays out one page shape per
            //document. ManualCatalogTests checks the first, middle and last
            //page of every manual still agree, so the day one of them stops
            //being uniform the test says so instead of the pages coming out the
            //wrong shape.
            PdfPage first = document.Pages[0];
            return new ManualStructure(
                document.PageCount, first.Width.Point, first.Height.Point, entries, sizes);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or ArgumentException)
        {
            //A manual whose structure cannot be read is reported as absent —
            //the panel then says the manual is not installed, which is what a
            //reader can act on.
            return null;
        }
    }

    private static void Walk(
        PdfOutlineCollection outlines,
        int level,
        Dictionary<PdfPage, int> numbers,
        List<ManualOutlineEntry> entries)
    {
        foreach (PdfOutline outline in outlines)
        {
            int page = outline.DestinationPage != null
                && numbers.TryGetValue(outline.DestinationPage, out int number)
                ? number
                : 0;
            entries.Add(new ManualOutlineEntry(outline.Title, page, level));
            if (outline.Outlines.Count > 0)
            {
                Walk(outline.Outlines, level + 1, numbers, entries);
            }
        }
    }
}
