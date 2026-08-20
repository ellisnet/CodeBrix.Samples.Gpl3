// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Lily.Docs;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// Everywhere this repository holds a SECOND copy of a file, and the gates that stop the
/// two drifting apart quietly.
/// <para>
/// Two sets are covered. The vendored GFDL assets (decision D49(a)) and the Documentation
/// mirror (D49(b)) hold the same three files. The <c>svg-dialect/</c> folder holds
/// reference copies of the SVG inventory's scanner and gate, so that folder can be read on
/// its own without opening anything outside it.
/// </para>
/// <para>
/// ⚠ Both sets exist for reasons that are individually right, and in both cases the reason
/// evaporates the moment the copies disagree. A copy that CAN drift WILL drift, so it is
/// fenced rather than trusted.
/// </para>
/// </summary>
public sealed class VendoredAssetTests
{
    /// <summary>
    /// The three files the Internals Reference reaches through its single
    /// <c>@include</c>. Not two: <c>common-macros.itexi</c> pulls
    /// <c>cyrillic.itexi</c>, one level below where the Phase-5 plan's original
    /// measurement stopped, and rendering without it costs an Include warning and the
    /// macros that file defines.
    /// </summary>
    private static readonly string[] RequiredAssets =
    {
        "macros.itexi", "common-macros.itexi", "cyrillic.itexi",
    };

    /// <summary>Every required asset is vendored.</summary>
    [Fact]
    public void every_required_asset_is_vendored()
    {
        //Arrange
        string assetsEn = Path.Combine(ToolPaths.AssetsDirectory, "en");

        //Act
        List<string> missing = new List<string>();
        foreach (string name in RequiredAssets)
        {
            if (!File.Exists(Path.Combine(assetsEn, name)))
            {
                missing.Add(name);
            }
        }

        //Assert
        missing.Should().BeEmpty();
    }

    /// <summary>Each vendored asset is byte identical to the documentation mirror.</summary>
    [Fact]
    public void each_vendored_asset_is_byte_identical_to_the_documentation_mirror()
    {
        //Arrange
        string assetsEn = Path.Combine(ToolPaths.AssetsDirectory, "en");
        string mirrorEn = Path.Combine(ToolPaths.CorpusDirectory, "en");

        //Act
        List<string> differing = new List<string>();
        foreach (string name in RequiredAssets)
        {
            byte[] vendored = File.ReadAllBytes(Path.Combine(assetsEn, name));
            byte[] mirrored = File.ReadAllBytes(Path.Combine(mirrorEn, name));
            if (!ByteEquals(vendored, mirrored))
            {
                differing.Add(name);
            }
        }

        //Assert
        // Both copies exist for reasons that are individually right — the assets so the
        // tool runs without a corpus, the mirror because the corpus manuals need the
        // same files — and neither reason survives the two disagreeing.
        differing.Should().BeEmpty();
    }

    /// <summary>The vendored set is the complete include closure.</summary>
    [Fact]
    public void the_vendored_set_is_the_complete_include_closure()
    {
        //Arrange
        string assetsEn = Path.Combine(ToolPaths.AssetsDirectory, "en");

        //Act
        HashSet<string> reached = new HashSet<string>();
        Queue<string> pending = new Queue<string>();
        pending.Enqueue("macros.itexi");
        List<string> unresolved = new List<string>();
        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            if (!reached.Add(name))
            {
                continue;
            }

            string path = Path.Combine(assetsEn, name);
            if (!File.Exists(path))
            {
                unresolved.Add(name);
                continue;
            }

            foreach (Match match in Regex.Matches(
                File.ReadAllText(path), @"^\s*@include\s+(\S+)", RegexOptions.Multiline))
            {
                string included = match.Groups[1].Value;
                if (included.StartsWith("en/", System.StringComparison.Ordinal))
                {
                    pending.Enqueue(included.Substring(3));
                }
                else if (included != VersionItexiName)
                {
                    unresolved.Add(included);
                }
            }
        }

        //Assert
        // The closure is COMPUTED, not listed. A future upstream that adds a fourth
        // support file to common-macros.itexi fails here at the moment it is vendored
        // in, rather than silently at render time as one Include warning among twelve —
        // which is exactly how cyrillic.itexi was missed in the first place.
        unresolved.Should().BeEmpty();
        reached.Should().BeEquivalentTo(RequiredAssets);
    }

    private static bool ByteEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>version.itexi</c> is BUILD-GENERATED and deliberately never vendored;
    /// Lily.Docs writes a stand-in at render time.
    /// </summary>
    /// <summary>
    /// The sources <c>svg-dialect/</c> keeps a reference copy of, as
    /// <c>copy name -&gt; path of the live original, relative to tools/Lily.Docs</c>.
    /// <para>
    /// The copies are there so the folder answers "what is this inventory and what is
    /// asserted about it?" without the reader leaving it. They are NOT compiled: both
    /// projects glob sources from their own directories and that folder is a sibling of
    /// neither, which is also why nothing else would notice them going stale.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string[]> SvgDialectReferenceCopies =
        new Dictionary<string, string[]>
        {
            { "SvgDialectInventory.cs", new[] { "src", "Lily.Docs", "Snippets" } },
            { "SvgDialectInventoryTests.cs", new[] { "tests", "Lily.Docs.Tests" } },
        };

    /// <summary>Every source the svg-dialect folder claims to copy is actually there.</summary>
    [Fact]
    public void every_svg_dialect_reference_copy_is_present()
    {
        //Arrange
        string folder = ToolPaths.SvgDialectDirectory;

        //Act
        List<string> missing = new List<string>();
        foreach (string name in SvgDialectReferenceCopies.Keys)
        {
            if (!File.Exists(Path.Combine(folder, name)))
            {
                missing.Add(name);
            }
        }

        //Assert
        // The folder's own README promises it can be read without opening anything outside
        // it. A copy that was never made breaks that promise silently.
        missing.Should().BeEmpty();
    }

    /// <summary>
    /// Each svg-dialect reference copy is byte identical to the live source it names.
    /// </summary>
    [Fact]
    public void each_svg_dialect_reference_copy_is_byte_identical_to_its_source()
    {
        //Arrange
        string folder = ToolPaths.SvgDialectDirectory;
        string toolRoot = Path.GetDirectoryName(folder);

        //Act
        List<string> differing = new List<string>();
        foreach (KeyValuePair<string, string[]> entry in SvgDialectReferenceCopies)
        {
            string source = toolRoot;
            foreach (string segment in entry.Value)
            {
                source = Path.Combine(source, segment);
            }

            source = Path.Combine(source, entry.Key);
            if (!File.Exists(source)
                || !ByteEquals(File.ReadAllBytes(Path.Combine(folder, entry.Key)),
                    File.ReadAllBytes(source)))
            {
                differing.Add(entry.Key);
            }
        }

        //Assert
        // Editing the live source without refreshing the copy fails HERE, which is the
        // whole point: nothing else in the build would ever notice, because the copies are
        // not compiled and nothing imports them.
        differing.Should().BeEmpty();
    }

    private const string VersionItexiName = "version.itexi";

    /// <summary>Version itexi is not vendored.</summary>
    [Fact]
    public void version_itexi_is_not_vendored()
    {
        //Arrange
        string assetsEn = Path.Combine(ToolPaths.AssetsDirectory, "en");

        //Act
        bool vendoredAtRoot = File.Exists(Path.Combine(ToolPaths.AssetsDirectory, VersionItexiName));
        bool vendoredUnderEn = File.Exists(Path.Combine(assetsEn, VersionItexiName));

        //Assert
        // A vendored copy would freeze a version string that disagrees with the engine
        // the moment the port's version moves, and it would do so silently.
        vendoredAtRoot.Should().BeFalse();
        vendoredUnderEn.Should().BeFalse();
    }
}
