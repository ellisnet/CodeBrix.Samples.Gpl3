// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using CodeBrix.LilyPort.Engine.Fonts;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// WHICH FONTS THE PACKAGE SHIPS, ASSERTED AGAINST THE PACKAGE.
/// <para>
/// The port ships Emmentaler fonts it builds itself (ruling R19) and 24 text faces
/// vendored byte-for-byte (D13/D23). Since PARITY 24 the repository ALSO carries
/// LilyPond's own Emmentaler binaries, under <c>tests/fixtures/lilypond-fonts/</c>, as
/// a measuring instrument: running the corpus against both builds is what separates
/// "the engine disagrees with upstream" from "the two font builds disagree".
/// </para>
/// <para>
/// ⚠ THOSE FIXTURES MUST NEVER REACH THE NUGET PACKAGE, and "testing only" written in a
/// README is not a guarantee — it is a sentence that stays true until somebody widens a
/// glob. This is the fence that makes it enforceable. It reads the BUILT PACKAGE rather
/// than the project file, because the project file is the thing that would be wrong:
/// a test that re-read the same globs it is meant to police would agree with them
/// whatever they said.
/// </para>
/// </summary>
public class PackagedFontTests
{
    [Fact]
    public void the_package_ships_the_ports_own_music_fonts_and_none_of_lilyponds()
    {
        //Arrange
        string package = FindPackage();
        if (package == null)
        {
            // Nothing to police until a package has been produced. Xunit has no
            // "inconclusive", and a silent pass here would be exactly the failure mode
            // this class exists to prevent — so it says so out loud instead.
            Assert.Fail(
                "no .nupkg found under src/CodeBrix.LilyPort/bin — run `dotnet pack -c "
                + "Release' before this fence can check anything.");
        }

        //Act
        List<string> fonts;
        using (ZipArchive archive = ZipFile.OpenRead(package))
        {
            fonts = archive.Entries
                .Select(entry => entry.FullName)
                .Where(name => name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        //Assert
        // A .nupkg carries the port's fonts as EMBEDDED RESOURCES inside the assembly,
        // not as loose files, so the expected count of loose .otf entries is zero. The
        // claim being made is the strong one: no font file of any kind rides along in
        // the package, whatever its provenance.
        fonts.Should().BeEmpty();

        // THE CONTROL, and it is the one that matters: the fixtures exist, they are
        // named exactly as the shipped faces are, and this test would therefore catch
        // them. Without it, "no .otf in the package" would keep passing if the fixtures
        // were deleted, renamed, or never added — a fence over an empty set.
        IReadOnlyList<string> fixtures = FixtureFontNames();
        fixtures.Count.Should().Be(9);
        fixtures.Should().Contain("emmentaler-26.otf");

        using (ZipArchive archive = ZipFile.OpenRead(package))
        {
            foreach (string fixture in fixtures)
            {
                archive.Entries
                    .Any(entry => entry.FullName.EndsWith(
                        fixture, StringComparison.OrdinalIgnoreCase))
                    .Should().BeFalse();
            }
        }
    }

    [Fact]
    public void the_engine_still_carries_its_own_emmentaler_after_the_fixtures_landed()
    {
        //Arrange
        // The other half of the same claim, and the reason it is not redundant: a change
        // that removed the port's OWN fonts from the assembly would leave the package
        // clean and the fence above green while breaking every rendered page.

        //Act
        byte[] shipped = FontAssets.MusicFont("emmentaler-26");

        //Assert
        shipped.Should().NotBeNull();
        shipped.Length.Should().BeGreaterThan(1000);

        // AND IT IS OURS, NOT THEIRS. Both builds carry the same glyph inventory and the
        // same LILC metadata, so a size check cannot tell them apart; the outlines can.
        // If these ever compare equal, either the fixture was overwritten with our build
        // or — the failure this is really watching for — our shipped asset was replaced
        // with LilyPond's, which is exactly what R19 rules out.
        string fixture = Path.Combine(FixtureDirectory(), "emmentaler-26.otf");
        File.Exists(fixture).Should().BeTrue();
        File.ReadAllBytes(fixture).SequenceEqual(shipped).Should().BeFalse();
    }

    private static IReadOnlyList<string> FixtureFontNames()
        => Directory.GetFiles(FixtureDirectory(), "*.otf")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static string FixtureDirectory()
        => Path.Combine(RepositoryRoot(), "tests", "fixtures", "lilypond-fonts");

    private static string FindPackage()
    {
        string bin = Path.Combine(RepositoryRoot(), "src", "CodeBrix.LilyPort", "bin");
        if (!Directory.Exists(bin))
        {
            return null;
        }

        return Directory.GetFiles(bin, "*.nupkg", SearchOption.AllDirectories)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    /// <summary>
    /// Walks up from the test assembly to the repository root — the directory holding
    /// <c>THIRD-PARTY-NOTICES.txt</c>, which is a file that exists for its own reasons
    /// and is therefore a landmark rather than a marker planted for this test.
    /// </summary>
    /// <returns>The repository root.</returns>
    private static string RepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
        while (directory != null
               && !File.Exists(Path.Combine(directory.FullName, "THIRD-PARTY-NOTICES.txt")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("repository root not found");
    }
}
