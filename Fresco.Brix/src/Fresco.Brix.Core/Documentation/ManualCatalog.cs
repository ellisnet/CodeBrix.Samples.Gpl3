// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Documentation; //was previously: frescobaldi/lilydoc/ (manager, documentation, manual)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One bundled manual: which file it is, what it is called, and how long it is.
/// </summary>
/// <remarks>
/// Upstream's <c>lilydoc.Documentation</c> is an INSTALLATION of the LilyPond
/// documentation, found on disk or fetched over the network, which has to
/// discover its own version by reading a <c>VERSION</c> file. None of that
/// survives: ruling FR5.1 leaves one engine, ruling FR8 makes the manuals
/// bundled PDFs, and a bundled asset knows what it is at compile time.
/// </remarks>
public sealed class ManualDefinition
{
    /// <summary>Creates a manual definition.</summary>
    /// <param name="name">The stable short name, which is also the file stem.</param>
    /// <param name="title">The manual's own title.</param>
    /// <param name="pageCount">How many pages the shipped PDF has.</param>
    internal ManualDefinition(string name, string title, int pageCount)
    {
        Name = name;
        Title = title;
        PageCount = pageCount;
    }

    /// <summary>Gets the stable short name, e.g. <c>notation</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets the manual's own title, as printed on its own title page.
    /// </summary>
    /// <remarks>
    /// ⚠ RULING FD12 (Jeremy, 2026-08-21): the titles are the DOCUMENTS' OWN and
    /// are not rewritten, so several of them say "LilyPond". Ruling FR13 keeps
    /// that word out of the application's CHROME and allows it where the point
    /// IS the lineage — it names documentation as one of those places — and
    /// these are third-party documents with FDL-protected titles. Renaming one
    /// in the chooser would misname the file it opens. Everything AROUND them
    /// (the panel, the menu entries, the toolbar) says LilyPort.
    /// </remarks>
    public string Title { get; }

    /// <summary>Gets how many pages the shipped PDF has.</summary>
    /// <remarks>
    /// MEASURED at W10 (2026-08-21) by reading the shipped file, and recorded
    /// here so that a truncated or stale asset is a TEST FAILURE rather than a
    /// manual that quietly stops early. <c>assets/docs/MANIFEST.txt</c> carries
    /// the same figures beside each file's size and hash.
    /// </remarks>
    public int PageCount { get; }

    /// <summary>Gets the asset file name, e.g. <c>notation.pdf</c>.</summary>
    public string FileName => Name + ".pdf";
}

/// <summary>
/// The manuals Fresco.Brix ships — LilyPort's own renderings of the nine
/// manuals, bundled as PDFs.
/// </summary>
/// <remarks>
/// <para>
/// Board decision D48 ruled nine manuals owed, and W10 bundles all nine
/// (Jeremy, 2026-08-21: 3,368 pages, 51.7 MB). W11½ (2026-08-26) re-rendered
/// them through the vector documentation chain: the same 3,368 pages — every
/// count below unchanged — at 27.2 MB, with each engraved example placed as
/// vector content and real text. They are made by the repo tool
/// <c>tools/manuals</c>, which drives CodeBrix.LilyPort's own
/// <c>tools/Lily.Docs</c>; NOTHING at application runtime renders a manual, and
/// LilyPort never grows a documentation dependency (decision D52, both ways).
/// </para>
/// <para>
/// The ORDER is reading order — the Learning Manual for someone new to the
/// language, the Notation Reference for someone looking something up — rather
/// than Lily.Docs' command-line order. <c>tools/manuals</c> declares the same
/// order and <c>ManualCatalogTests</c> checks the two still agree.
/// </para>
/// </remarks>
public static class ManualCatalog
{
    /// <summary>The manuals, in the order the documentation panel lists them.</summary>
    public static readonly IReadOnlyList<ManualDefinition> All = new[]
    {
        new ManualDefinition("learning", "LilyPond Learning Manual", 253),
        new ManualDefinition("notation", "LilyPond Notation Reference", 1280),
        new ManualDefinition("usage", "LilyPond Application Usage", 96),
        new ManualDefinition("extending", "Extending LilyPond", 76),
        new ManualDefinition("internals", "LilyPond Internals Reference", 1266),
        new ManualDefinition("essay", "Essay on automated music engraving", 63),
        new ManualDefinition("music-glossary", "LilyPond Music Glossary", 135),
        new ManualDefinition("changes", "LilyPond Changes", 10),
        new ManualDefinition("contributor", "LilyPond Contributor's Guide", 189),
    };

    /// <summary>The manual a reader is shown when they have asked for none.</summary>
    /// <remarks>Upstream's home page is the manual INDEX, which a set of PDFs
    /// has no equivalent of; the Learning Manual is where its index sends a
    /// new reader first.</remarks>
    public const string DefaultName = "learning";

    /// <summary>The reference for objects, properties and contexts.</summary>
    public const string InternalsName = "internals";

    /// <summary>The reference for the notation language itself.</summary>
    public const string NotationName = "notation";

    /// <summary>Finds a manual by name, or null.</summary>
    /// <param name="name">The short name.</param>
    /// <returns>The manual, or null.</returns>
    public static ManualDefinition Find(string name)
        => string.IsNullOrEmpty(name)
            ? null
            : All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));

    /// <summary>Gets the total number of pages the bundle carries.</summary>
    public static int TotalPageCount => All.Sum(m => m.PageCount);
}
