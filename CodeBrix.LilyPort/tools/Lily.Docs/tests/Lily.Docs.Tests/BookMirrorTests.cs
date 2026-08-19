// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lily.Docs;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// <c>book-mirror/</c> holds the faithfulness authority for the snippet seam, and its one
/// rule is NEVER EDIT — a mirror that has been edited is not a mirror, and the re-sync
/// workflow depends on it being one.
/// <para>
/// These gates make that rule mechanical. The sha256s are read out of the mirror's own
/// README rather than restated here, so the README and the files cannot disagree: a
/// re-sync that updates the files and forgets the README fails, and so does one that
/// updates the README and forgets the files.
/// </para>
/// </summary>
public sealed class BookMirrorTests
{
    /// <summary>
    /// The four files the authority actually consists of.
    /// <para>
    /// ⚠ FOUR, not the two decision D49(c) named. <c>compose_ly</c> — the function the seam
    /// ports — reads its defaults from <c>self.formatter.default_snippet_options</c>, which
    /// is defined in <c>book_base.py</c> and widened by <c>book_texinfo.py</c>. Wave LD2
    /// found the gap the same way wave LD1 found <c>cyrillic.itexi</c>: by following the
    /// dependency one level further than the original measurement did.
    /// </para>
    /// </summary>
    private static readonly string[] MirroredFiles =
    {
        "lilypond-book.py", "book_snippets.py", "book_base.py", "book_texinfo.py",
    };

    private static string MirrorDirectory =>
        Path.Combine(ToolPaths.RepositoryRoot, "book-mirror");

    /// <summary>Every file the authority consists of is mirrored.</summary>
    [Fact]
    public void every_authority_file_is_mirrored()
    {
        //Arrange
        List<string> missing = new List<string>();

        //Act
        foreach (string name in MirroredFiles)
        {
            if (!File.Exists(Path.Combine(MirrorDirectory, name)))
            {
                missing.Add(name);
            }
        }

        //Assert
        missing.Should().BeEmpty();
    }

    /// <summary>
    /// Each mirrored file still hashes to what the README records — which is to say, nobody
    /// has edited it.
    /// </summary>
    [Fact]
    public void each_mirrored_file_matches_the_recorded_hash()
    {
        //Arrange
        IReadOnlyDictionary<string, string> recorded = ReadRecordedHashes();

        //Act
        List<string> wrong = new List<string>();
        foreach (string name in MirroredFiles)
        {
            string actual = HashOf(Path.Combine(MirrorDirectory, name));
            if (!recorded.TryGetValue(name, out string expected))
            {
                wrong.Add(name + ": the README records no hash for it");
            }
            else if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                wrong.Add(name + ": README says " + expected + ", file hashes to " + actual);
            }
        }

        //Assert
        wrong.Should().BeEmpty();
    }

    /// <summary>
    /// The README records a hash for every mirrored file AND for no file that is absent, so
    /// the two lists are the same list.
    /// </summary>
    [Fact]
    public void the_readme_records_exactly_the_mirrored_files()
    {
        //Arrange
        IReadOnlyDictionary<string, string> recorded = ReadRecordedHashes();

        //Act
        List<string> names = new List<string>(recorded.Keys);
        names.Sort(StringComparer.Ordinal);
        List<string> expected = new List<string>(MirroredFiles);
        expected.Sort(StringComparer.Ordinal);

        //Assert
        names.Should().BeEquivalentTo(expected);
    }

    /// <summary>
    /// THE CONTROL: the hash function actually distinguishes files. Without this, a broken
    /// hash that returned a constant would make every gate above pass.
    /// </summary>
    [Fact]
    public void the_hash_distinguishes_two_different_mirrored_files()
    {
        //Arrange
        string first = Path.Combine(MirrorDirectory, "book_base.py");
        string second = Path.Combine(MirrorDirectory, "book_texinfo.py");

        //Act
        string firstHash = HashOf(first);
        string secondHash = HashOf(second);

        //Assert
        firstHash.Should().NotBe(secondHash);
        firstHash.Length.Should().Be(64);
    }

    /// <summary>
    /// Nothing in the repository EXECUTES the mirror — it is reference text, and the port
    /// has no Python runtime dependency. A build or test step that started reading it would
    /// be a real change of standing, so it is fenced.
    /// </summary>
    [Fact]
    public void no_project_file_references_the_mirror()
    {
        //Arrange
        string toolsDirectory = Path.Combine(ToolPaths.RepositoryRoot, "tools");

        //Act
        List<string> referencing = new List<string>();
        foreach (string project in Directory.GetFiles(toolsDirectory, "*.csproj",
            SearchOption.AllDirectories))
        {
            if (File.ReadAllText(project).Contains("book-mirror", StringComparison.Ordinal))
            {
                referencing.Add(project);
            }
        }

        //Assert
        referencing.Should().BeEmpty();
    }

    private static IReadOnlyDictionary<string, string> ReadRecordedHashes()
    {
        string readme = File.ReadAllText(Path.Combine(MirrorDirectory, "README.txt"));
        Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(readme,
            @"^\s*sha256\s+(?<hash>[0-9a-f]{64})\s+(?<name>\S+)\s*$", RegexOptions.Multiline))
        {
            hashes[match.Groups["name"].Value] = match.Groups["hash"].Value;
        }

        return hashes;
    }

    private static string HashOf(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
