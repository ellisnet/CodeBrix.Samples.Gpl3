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
/// The vendored GFDL assets (decision D49(a)) and the Documentation mirror (D49(b))
/// hold the SAME three files. Two copies of one file can drift; these gates make that
/// impossible to do quietly.
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
