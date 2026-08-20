// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lily.Docs;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// The two vendored sets decision D57 brought into the repository (ruled 2026-08-19):
/// <c>assets/bib/</c>, the five bibliographies translated once by the BibTeX oracle, and
/// <c>assets/staged/</c>, the two source-tree files the Contributor's Guide prints verbatim.
/// <para>
/// ⚠ WHAT MAKES THESE DIFFERENT FROM EVERY OTHER VENDORED FILE IN THIS REPOSITORY: they are
/// not copies of anything. <c>Documentation/</c> and <c>assets/en/</c> hold BYTE COPIES of
/// files that exist upstream, and their gates diff the two. These five <c>.itexi</c> files
/// exist nowhere — upstream MAKES them at build time and ships neither the output nor a way
/// to make it without a TeX installation. So there is no second copy to diff against, and a
/// recorded hash is the only fence available.
/// </para>
/// </summary>
public sealed class VendoredBuildProductTests
{
    /// <summary>
    /// The five bibliographies and the entry count each one carries, MEASURED 2026-08-19
    /// from the oracle run that produced them.
    /// <para>
    /// The counts are here rather than only in the manifest because a hash says a file did
    /// not change and says nothing about what is IN it. An entry count is the one structural
    /// fact about a bibliography that a reader can check by eye against the source
    /// <c>.bib</c>, and every one of these reconciles exactly with its own: <c>colorado</c>
    /// 51 <c>@Book</c>/<c>@Article</c> entries, <c>computer-notation</c> 61 (its 63
    /// <c>@</c>-lines include two <c>@String</c> definitions, which are not entries),
    /// <c>engravingbib</c> 35, <c>we-wrote</c> 8, <c>others-did</c> 5.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, int> BibliographyEntries =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "colorado.itexi", 51 },
            { "computer-notation.itexi", 61 },
            { "engravingbib.itexi", 35 },
            { "we-wrote.itexi", 8 },
            { "others-did.itexi", 5 },
        };

    /// <summary>The three the essay manual actually includes.</summary>
    private static readonly string[] EssayBibliographies =
    {
        "colorado.itexi", "computer-notation.itexi", "engravingbib.itexi",
    };

    /// <summary>The two files the contributors guide prints verbatim.</summary>
    private static readonly string[] StagedFiles = { "ROADMAP", "code-review-checklist.md" };

    /// <summary>Every vendored build product matches its recorded manifest.</summary>
    [Theory]
    [InlineData("bib")]
    [InlineData("staged")]
    public void every_vendored_build_product_matches_its_recorded_manifest(string set)
    {
        //Arrange
        string directory = set == "bib"
            ? ToolPaths.BibliographyAssetsDirectory
            : ToolPaths.StagedAssetsDirectory;
        Dictionary<string, string> recorded = ReadManifest(
            Path.Combine(directory, "MANIFEST.sha256"));

        //Act
        List<string> wrong = new List<string>();
        foreach (KeyValuePair<string, string> entry in recorded)
        {
            string path = Path.Combine(directory, entry.Key);
            if (!File.Exists(path))
            {
                wrong.Add(entry.Key + ": absent");
            }
            else if (!string.Equals(Sha256Of(path), entry.Value, StringComparison.Ordinal))
            {
                wrong.Add(entry.Key + ": content changed");
            }
        }

        //Assert
        // ⚠ THE ONLY FENCE THESE FILES CAN HAVE. Every other vendored set in this repository
        // is a copy of something that still exists upstream, so its gate diffs the two
        // copies. These are oracle OUTPUT: reproducing them needs a TeX installation and the
        // exact command in the directory's README, which is precisely the dependency this
        // decision removed. A recorded hash is what stands in for the second copy.
        wrong.Should().BeEmpty();

        // The paired half — the manifest names everything on disk, so a file added without
        // its hash being written down fails here rather than travelling unrecorded.
        List<string> unlisted = Directory.EnumerateFiles(directory)
            .Select(Path.GetFileName)
            .Where(name => name != "MANIFEST.sha256" && name != "README.txt")
            .Where(name => !recorded.ContainsKey(name))
            .ToList();
        unlisted.Should().BeEmpty();
    }

    /// <summary>Each bibliography is a closed itemize carrying its measured entries.</summary>
    [Fact]
    public void each_bibliography_is_a_closed_itemize_carrying_its_measured_entries()
    {
        //Arrange
        List<string> wrong = new List<string>();

        //Act
        foreach (KeyValuePair<string, int> expected in BibliographyEntries)
        {
            string path = Path.Combine(ToolPaths.BibliographyAssetsDirectory, expected.Key);
            string text = File.ReadAllText(path);
            int items = Regex.Matches(text, "^@item$", RegexOptions.Multiline).Count;
            if (items != expected.Value)
            {
                wrong.Add($"{expected.Key}: {items} entries, expected {expected.Value}");
            }

            if (!text.StartsWith("@c bib -> itexi intro", StringComparison.Ordinal)
                || !text.TrimEnd().EndsWith("@c bib -> itexi end", StringComparison.Ordinal)
                || !text.Contains("@itemize", StringComparison.Ordinal)
                || !text.Contains("@end itemize", StringComparison.Ordinal))
            {
                wrong.Add(expected.Key + ": not a closed bib -> itexi document");
            }
        }

        //Assert
        // ⚠ A HASH SAYS A FILE DID NOT CHANGE; IT DOES NOT SAY THE FILE IS A BIBLIOGRAPHY.
        // These files were made by a tool nobody here runs any more, from a style program
        // nobody here executes, so the structural claim is worth stating in its own right —
        // a truncated bibtex run produces a perfectly valid prefix, and its hash would be
        // recorded as happily as a whole one.
        //
        // ⚠ The markers are lily-bib.bst's own: it wraps its output in
        // `@c bib -> itexi intro' and `@c bib -> itexi end', which is how a reader of the
        // rendered manual could tell where this content came from.
        wrong.Should().BeEmpty();
        BibliographyEntries.Values.Sum().Should().Be(160);
    }

    /// <summary>The style file that produced them is vendored beside them.</summary>
    [Fact]
    public void the_style_file_that_produced_them_is_vendored_beside_them()
    {
        //Arrange
        string path = Path.Combine(ToolPaths.BibliographyAssetsDirectory, "lily-bib.bst");

        //Act
        string text = File.Exists(path) ? File.ReadAllText(path) : null;

        //Assert
        // ⚠ THE RECIPE, AND IT IS WHAT MAKES THE OUTPUT CHECKABLE RATHER THAN MERELY
        // PRESENT. Without it the five .itexi files are bytes nobody can re-derive: the
        // translation is BibTeX interpreting THIS program, not anything in the thirty-line
        // Python wrapper upstream calls. It is never executed here — same standing as
        // book-mirror/ and parser-mirror/ — and it earns its place on a corpus re-sync,
        // where diffing it against the new checkout's copy is what says whether
        // regeneration is owed at all. Five commits have touched it in thirty years, so the
        // answer is usually no.
        text.Should().NotBeNull();
        text.Should().Contain("ENTRY");
        text.Should().Contain("bib -> itexi intro");
        Regex.Matches(text, @"^FUNCTION", RegexOptions.Multiline).Count
            .Should().BeGreaterThan(20);
    }

    /// <summary>Every vendored build product is reachable by the name its manual uses.</summary>
    [Fact]
    public void every_vendored_build_product_is_reachable_by_the_name_its_manual_uses()
    {
        //Arrange
        // Built exactly as a render builds it, rather than by joining paths here — the
        // question is whether the RENDERER can find these files, and the renderer's answer
        // comes from RenderPaths.
        Lily.Docs.Rendering.RenderPaths paths = new Lily.Docs.Rendering.RenderPaths(
            GeneratedDocumentation.Directory, ToolPaths.AssetsDirectory,
            Path.GetTempPath(), ToolPaths.CorpusDirectory);

        //Act
        List<string> unreachable = new List<string>();
        foreach (string name in EssayBibliographies.Concat(StagedFiles))
        {
            bool found = paths.IncludeSearchPaths
                .Any(directory => File.Exists(Path.Combine(directory, name)));
            if (!found)
            {
                unreachable.Add(name);
            }
        }

        //Assert
        // ⚠ BARE NAMES, WHICH IS WHY THE TWO SUBDIRECTORIES ARE ON THE SEARCH PATH IN THEIR
        // OWN RIGHT. essay's literature list writes `@include colorado.itexi' and the
        // Contributor's Guide writes `@verbatiminclude ROADMAP'; neither is reachable
        // through the assets ROOT, which is on the path for `en/macros.itexi'. Getting this
        // wrong does not fail a build — it produces the Include warnings this decision was
        // opened to remove, which is a slow way to find out.
        unreachable.Should().BeEmpty();

        // ⚠ The two web.texi bibliographies are deliberately NOT asserted reachable, because
        // nothing in decision D48's scope includes them. They are vendored so that a future
        // day needing them does not also need a TeX installation.
        File.Exists(Path.Combine(ToolPaths.BibliographyAssetsDirectory, "we-wrote.itexi"))
            .Should().BeTrue();
        File.Exists(Path.Combine(ToolPaths.BibliographyAssetsDirectory, "others-did.itexi"))
            .Should().BeTrue();
    }

    /// <summary>The bibliography sources are still mirrored beside their translation.</summary>
    [Fact]
    public void the_bibliography_sources_are_still_mirrored_beside_their_translation()
    {
        //Arrange
        string bibDirectory = Path.Combine(ToolPaths.CorpusDirectory, "bib");

        //Act
        List<string> missing = BibliographyEntries.Keys
            .Select(name => Path.GetFileNameWithoutExtension(name) + ".bib")
            .Where(name => !File.Exists(Path.Combine(bibDirectory, name)))
            .ToList();

        //Assert
        // ⚠ THE INPUTS STAY, AND THEY STAY IN THE MIRROR RATHER THAN BESIDE THE OUTPUT.
        // The .bib files ARE documentation sources and belong in the FDL tree; the .bst that
        // consumes them is GPL LilyPond source and does not. Keeping both means the whole
        // translation is reproducible from THIS repository — sources, style program and the
        // command in assets/bib/README.txt — without the upstream checkout, which is the
        // point of the ruling.
        missing.Should().BeEmpty();
    }

    private static Dictionary<string, string> ReadManifest(string path)
    {
        Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            int separator = line.IndexOf("  ", StringComparison.Ordinal);
            if (separator > 0)
            {
                entries[line.Substring(separator + 2)] = line.Substring(0, separator);
            }
        }

        return entries;
    }

    private static string Sha256Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
