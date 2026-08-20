// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;

namespace Lily.Docs;

/// <summary>
/// The repository directories Lily.Docs reads from.
/// <para>
/// Everything here is found by walking UP from the running assembly to the repository
/// marker, never by a relative path from the working directory: the generation step
/// changes the working directory (upstream's script writes by relative name), so a
/// path resolved against it would mean different things before and after a render.
/// </para>
/// </summary>
public static class ToolPaths
{
    private const string RepositoryMarker = "CodeBrix.LilyPort.slnx";

    private static readonly Lazy<string> LazyRepositoryRoot = new Lazy<string>(FindRepositoryRoot);

    /// <summary>
    /// The CodeBrix.LilyPort repository root — the directory holding
    /// <c>CodeBrix.LilyPort.slnx</c>.
    /// </summary>
    public static string RepositoryRoot => LazyRepositoryRoot.Value;

    /// <summary>
    /// The vendored-asset ROOT: the directory CONTAINING <c>en/</c>, because manuals
    /// include <c>en/macros.itexi</c> by that path rather than by file name alone.
    /// <para>
    /// The build copies the assets beside the assembly, so a tool run out of its output
    /// directory works with no repository present. The repository copy is the fallback,
    /// which is what a test run before a copy has happened uses.
    /// </para>
    /// </summary>
    public static string AssetsDirectory
    {
        get
        {
            string beside = Path.Combine(AppContext.BaseDirectory, "assets");
            if (Directory.Exists(Path.Combine(beside, "en")))
            {
                return beside;
            }

            return Path.Combine(RepositoryRoot, "tools", "Lily.Docs", "assets");
        }
    }

    /// <summary>
    /// The five translated bibliographies and the BibTeX style program that produced
    /// them — <c>assets/bib/</c> (decision D57, ruled 2026-08-19).
    /// <para>
    /// The essay manual's literature list <c>@include</c>s three of them by BARE NAME, so
    /// this directory goes on the include search path in its own right rather than being
    /// reachable through the assets root.
    /// </para>
    /// <para>
    /// ⚠ THEY ARE ORACLE OUTPUT, VENDORED, AND THAT IS THE WHOLE RULING. Upstream generates
    /// them from <c>Documentation/bib/*.bib</c> with <c>bib2texi.py</c>, which is thirty
    /// lines that shell out to the BibTeX BINARY with an 8.5&#160;KB <c>.bst</c> style
    /// program — so writing "our own" would mean writing a BibTeX style-language
    /// interpreter. MEASURED against thirty years of upstream history: five files, 160
    /// entries, seven commits out of 35,717. Static reference data, not a living format.
    /// </para>
    /// </summary>
    public static string BibliographyAssetsDirectory =>
        Path.Combine(AssetsDirectory, "bib");

    /// <summary>
    /// The two source-tree files the Contributor's Guide prints verbatim —
    /// <c>assets/staged/</c> (decision D57, ruled 2026-08-19).
    /// </summary>
    /// <remarks>
    /// ⚠ NEITHER LIVES IN <c>Documentation/</c> UPSTREAM: <c>ROADMAP</c> is at the source
    /// tree ROOT and the checklist under <c>.agents/</c>, and the doc build COPIES both into
    /// its output directory before rendering. They are kept out of this repository's
    /// Documentation mirror because they are GPL source rather than FDL manual text, and the
    /// notices file declares that tree cleanly separated.
    /// </remarks>
    public static string StagedAssetsDirectory =>
        Path.Combine(AssetsDirectory, "staged");

    /// <summary>
    /// The repository's Documentation mirror — the corpus manuals' own source text
    /// (decision D49(b)).
    /// <para>
    /// ⚠ This is the ONLY corpus path Lily.Docs ever reads. Standing rule 7 is that no
    /// build or test step touches <c>~/GitHome/lilypond</c>, and Phase 5 takes no
    /// exception to it; the mirror exists precisely so that rule stays intact.
    /// </para>
    /// </summary>
    public static string CorpusDirectory => Path.Combine(RepositoryRoot, "Documentation");

    /// <summary>
    /// Where the oracle's own frozen composed snippet sources live — the fence that says
    /// Lily.Docs composes a snippet the way lilypond-book composes it.
    /// <para>
    /// Frozen rather than produced live on purpose: standing rule 7 keeps every build and
    /// test step out of <c>~/GitHome/lilypond</c>, and D49(b) asks that the corpus gates
    /// always RUN rather than skip. A test that shelled out to the oracle would break both
    /// and would depend on an install that is not in the repository.
    /// </para>
    /// </summary>
    public static string ComposedReferenceDirectory =>
        Path.Combine(RepositoryRoot, "tools", "Lily.Docs", "composed-reference");

    /// <summary>Where the frozen expected-warnings baselines live.</summary>
    public static string ExpectedWarningsDirectory =>
        Path.Combine(RepositoryRoot, "tools", "Lily.Docs", "expected-warnings");

    /// <summary>
    /// Where the frozen SVG dialect inventory and its specification live — what the port's
    /// engine actually emits for a documentation snippet, written down so a downstream
    /// renderer can implement against it.
    /// <para>
    /// ⚠ THE INVENTORY LIVES IN THIS REPOSITORY AND NOWHERE ELSE. It is measured over
    /// engraved output derived from the GFDL corpus mirror, and this repository is the
    /// GPL-3 one; the MIT-licensed package repositories that consume the specification read
    /// it, and never take a copy of the pictures it was measured from.
    /// </para>
    /// </summary>
    public static string SvgDialectDirectory =>
        Path.Combine(RepositoryRoot, "tools", "Lily.Docs", "svg-dialect");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryMarker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"could not find {RepositoryMarker} above {AppContext.BaseDirectory}");
    }
}
