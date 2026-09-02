// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using Fresco.Brix.DocumentFonts;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The music-font model against Frescobaldi's OWN <c>fonts/musicfonts.py</c>:
/// <c>fixtures/fonts/musicfonts.json</c> holds what upstream's
/// <c>MusicFontFamily</c> made of five lists of file names (regenerate with
/// <c>tools/fontprobe/gen-font-fixtures.py</c>), and then what the install and
/// remove half does over a real folder.
/// </summary>
public class MusicFontsTests : IDisposable
{
    private readonly string _directory;

    /// <summary>Creates the fixture over a scratch directory.</summary>
    public MusicFontsTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-musicfonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Removes the scratch directory.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Gets the recorded file sets, one test case each.</summary>
    public static IEnumerable<object[]> FileSets
        => Fixture().EnumerateArray()
            .Select(set => new object[] { set.GetProperty("name").GetString() });

    /// <summary>
    /// The same file names are read as music fonts, or not, as upstream reads
    /// them.
    /// </summary>
    /// <param name="name">The file set.</param>
    [Theory]
    [MemberData(nameof(FileSets))]
    public void file_names_are_classified_as_frescobaldi_classifies_them(string name)
    {
        //Arrange
        JsonElement set = Set(name);

        //Act
        //Assert
        foreach (JsonElement file in set.GetProperty("files").EnumerateArray())
        {
            string fileName = file.GetProperty("file").GetString();
            var parsed = MusicFontFamily.ParseFileName(fileName);

            if (!file.GetProperty("isMusicFont").GetBoolean())
            {
                parsed.Should().BeNull();
                continue;
            }

            parsed.Should().NotBeNull();
            parsed.Value.Family.Should().Be(file.GetProperty("family").GetString());
            parsed.Value.Size.Should().Be(file.GetProperty("size").GetString());
            parsed.Value.Type.Should().Be(file.GetProperty("type").GetString());
        }
    }

    /// <summary>
    /// A family's completeness, missing sizes and brace flag are upstream's.
    /// </summary>
    /// <param name="name">The file set.</param>
    [Theory]
    [MemberData(nameof(FileSets))]
    public void families_report_what_frescobaldi_reports(string name)
    {
        //Arrange
        JsonElement set = Set(name);
        Dictionary<string, MusicFontFamily> families =
            new Dictionary<string, MusicFontFamily>(StringComparer.Ordinal);
        foreach (JsonElement file in set.GetProperty("files").EnumerateArray())
        {
            if (!file.GetProperty("isMusicFont").GetBoolean()) { continue; }

            string family = file.GetProperty("family").GetString();
            if (!families.TryGetValue(family, out MusicFontFamily entry))
            {
                families[family] = entry = new MusicFontFamily();
            }

            entry.Add(
                file.GetProperty("type").GetString(),
                file.GetProperty("size").GetString(),
                file.GetProperty("file").GetString());
        }

        //Act
        //Assert
        JsonElement expected = set.GetProperty("families");
        families.Count.Should().Be(expected.GetArrayLength());

        foreach (JsonElement recorded in expected.EnumerateArray())
        {
            MusicFontFamily family = families[recorded.GetProperty("family").GetString()];
            family.IsComplete().Should().Be(recorded.GetProperty("complete").GetBoolean());

            JsonElement types = recorded.GetProperty("types");
            foreach (string type in MusicFontFamily.Types)
            {
                JsonElement state = types.GetProperty(type);
                family.Sizes(type).Should().Equal(Strings(state.GetProperty("sizes")));
                family.MissingSizes(type).Should()
                    .Equal(Strings(state.GetProperty("missingSizes")));
                family.HasBrace(type).Should().Be(state.GetProperty("hasBrace").GetBoolean());
                family.IsComplete(type).Should()
                    .Be(state.GetProperty("complete").GetBoolean());
            }
        }
    }

    /// <summary>The eight design sizes are upstream's own list.</summary>
    [Fact]
    public void the_size_list_is_frescobaldis()
    {
        //Arrange
        JsonElement set = Fixture().EnumerateArray().First();

        //Act
        IReadOnlyList<string> sizes = MusicFontFamily.SizesList;

        //Assert
        sizes.Should().Equal(Strings(set.GetProperty("sizesList")));
    }

    /// <summary>A repository's fonts reach the installation folder.</summary>
    [Fact]
    public void installing_copies_a_whole_family_into_the_app_folder()
    {
        //Arrange
        string source = Path.Combine(_directory, "repo");
        string target = Path.Combine(_directory, "installed");
        WriteFamily(source, "spikefont");

        //Act
        InstalledMusicFonts installed = new InstalledMusicFonts(target);
        MusicFontRepo repo = new MusicFontRepo(source);
        repo.FlagForInstall(installed);
        int copied = repo.InstallFlagged(installed);

        //Assert
        copied.Should().Be(18);
        installed.Families().Should().Equal(new[] { "spikefont" });
        installed.Family("spikefont").IsComplete("otf").Should().BeTrue();
        installed.Family("spikefont").IsComplete("svg").Should().BeTrue();
        installed.Family("spikefont").HasBrace("otf").Should().BeTrue();

        //The copies are FILES, and they are inside the application's folder —
        //which is what makes them removable.
        installed.Family("spikefont").Status("otf", "20")
            .Should().Be(MusicFontStatus.File);
        File.Exists(Path.Combine(target, "spikefont-20.otf")).Should().BeTrue();
    }

    /// <summary>Installing again adds nothing, because nothing is missing.</summary>
    [Fact]
    public void installing_twice_flags_nothing_the_second_time()
    {
        //Arrange
        string source = Path.Combine(_directory, "repo");
        string target = Path.Combine(_directory, "installed");
        WriteFamily(source, "spikefont");
        InstalledMusicFonts installed = new InstalledMusicFonts(target);
        new MusicFontRepo(source).FlagForInstall(installed);

        //Act
        MusicFontRepo first = new MusicFontRepo(source);
        first.FlagForInstall(installed);
        first.InstallFlagged(installed);

        MusicFontRepo second = new MusicFontRepo(source);
        second.FlagForInstall(installed);

        //Assert
        second.InstallableFonts.Families().Should().BeEmpty();
    }

    /// <summary>Removing a family takes its files with it.</summary>
    [Fact]
    public void removing_a_family_deletes_its_files()
    {
        //Arrange
        string source = Path.Combine(_directory, "repo");
        string target = Path.Combine(_directory, "installed");
        WriteFamily(source, "spikefont");
        InstalledMusicFonts installed = new InstalledMusicFonts(target);
        MusicFontRepo repo = new MusicFontRepo(source);
        repo.FlagForInstall(installed);
        repo.InstallFlagged(installed);

        //Act
        installed.Remove(new[] { "spikefont" });

        //Assert
        installed.Families().Should().BeEmpty();
        Directory.GetFiles(target).Should().BeEmpty();

        //THE CONTROL: the repository the font came from is untouched.
        Directory.GetFiles(source).Length.Should().Be(18);
    }

    /// <summary>A family holding a file from outside the folder is refused.</summary>
    /// <remarks>Upstream refuses to remove real FILES, to protect the LilyPond
    /// installation's own fonts. The folder here belongs to the application, so
    /// what the rule protects is a file that came from somewhere else — which
    /// is the only thing a hand-made link can point at.</remarks>
    [Fact]
    public void removing_refuses_a_family_holding_a_file_from_elsewhere()
    {
        //Arrange
        string source = Path.Combine(_directory, "repo");
        string target = Path.Combine(_directory, "installed");
        WriteFamily(source, "spikefont");
        Directory.CreateDirectory(target);
        InstalledMusicFonts installed = new InstalledMusicFonts(target);
        installed.AddFile(Path.Combine(source, "spikefont-20.otf"));

        //Act
        Action removing = () => installed.Remove(new[] { "spikefont" });

        //Assert
        removing.Should().Throw<MusicFontFileRemoveException>();
        File.Exists(Path.Combine(source, "spikefont-20.otf")).Should().BeTrue();
    }

    /// <summary>The application's folder is put in front of the engine's own.</summary>
    [Fact]
    public void registering_puts_the_folder_in_the_engines_search_path_once()
    {
        //Arrange
        string folder = Path.Combine(_directory, "registered");
        int before = FontAssets.SearchPaths.Count;

        //Act
        InstalledMusicFonts.Register(folder);
        InstalledMusicFonts.Register(folder);

        //Assert
        try
        {
            FontAssets.SearchPaths.Count.Should().Be(before + 1);
            FontAssets.SearchPaths.Should().Contain(folder);
            Directory.Exists(folder).Should().BeTrue();
        }
        finally
        {
            FontAssets.SearchPaths.Remove(folder);
        }
    }

    /// <summary>A row says what the family holds, type by type.</summary>
    [Fact]
    public void a_row_describes_what_the_family_holds()
    {
        //Arrange
        MusicFontFamily family = new MusicFontFamily();
        foreach (string size in MusicFontFamily.SizesList)
        {
            family.Add("otf", size, "spikefont-" + size + ".otf");
        }

        family.Add("otf", "brace", "spikefont-brace.otf");
        family.Add("svg", "20", "spikefont-20.svg");
        family.Family.Should().BeNull();

        //Act
        MusicFontRow row = new MusicFontRow(family);

        //Assert
        row.Types.Count.Should().Be(3);
        row.Types[0].IsComplete.Should().BeTrue();
        row.Types[0].HasBrace.Should().BeTrue();
        row.Types[1].IsComplete.Should().BeFalse();
        row.Types[1].IsEmpty.Should().BeFalse();
        row.Types[2].IsEmpty.Should().BeTrue();
        row.Describe().Should().Contain("OTF");
    }

    /// <summary>Writes a complete music-font family of empty files.</summary>
    /// <param name="folder">Where to write it.</param>
    /// <param name="family">The family name.</param>
    private static void WriteFamily(string folder, string family)
    {
        Directory.CreateDirectory(folder);
        List<string> sizes = MusicFontFamily.SizesList.Concat(new[] { "brace" }).ToList();
        foreach (string size in sizes)
        {
            foreach (string type in new[] { "otf", "svg" })
            {
                File.WriteAllText(
                    Path.Combine(folder, family + "-" + size + "." + type), size);
            }
        }
    }

    /// <summary>Reads a JSON string array.</summary>
    /// <param name="array">The array.</param>
    /// <returns>The strings.</returns>
    private static IReadOnlyList<string> Strings(JsonElement array)
        => array.EnumerateArray().Select(value => value.GetString()).ToList();

    /// <summary>Reads one recorded file set.</summary>
    /// <param name="name">The set.</param>
    /// <returns>The set.</returns>
    private static JsonElement Set(string name)
        => Fixture().EnumerateArray()
            .First(set => set.GetProperty("name").GetString() == name);

    /// <summary>Reads the fixture file.</summary>
    /// <returns>The file sets.</returns>
    private static JsonElement Fixture()
    {
        JsonDocument fixture = JsonDocument.Parse(
            File.ReadAllText(FontFixtures.Path("musicfonts.json")));
        return fixture.RootElement;
    }
}
