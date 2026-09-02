// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.DocumentFonts;
using Fresco.Brix.Preferences;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The rest of the document-font feature: the text-font world the engine
/// reports, the sample documents the preview engraves, and the two folders the
/// Paths preferences page configures.
/// </summary>
public class DocumentFontsTests : IDisposable
{
    private readonly string _directory;

    /// <summary>Creates the fixture over a scratch directory.</summary>
    public DocumentFontsTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "frescobrix-docfonts-" + Guid.NewGuid().ToString("N"));
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

    private string StorePath => _directory;

    // ----------------------------------------------------- the text-font world

    /// <summary>
    /// The listing the ENGINE printed parses into the world it describes.
    /// </summary>
    /// <remarks><c>fixtures/fonts/fontworld.txt</c> is what
    /// <c>ly:font-config-display-fonts</c> itself wrote, captured from a real
    /// engine run — not from this port.</remarks>
    [Fact]
    public void the_engines_own_listing_parses_into_its_world()
    {
        //Arrange
        string listing = File.ReadAllText(FontFixtures.Path("fontworld.txt"));

        //Act
        TextFontWorld world = TextFontWorld.Parse(listing);

        //Assert
        world.FaceCount.Should().Be(24);
        world.DocumentSupplied.Should().BeEmpty();
        world.Families.Select(family => family.Name).Should().Equal(new[]
        {
            "C059", "Nimbus Mono PS", "Nimbus Sans",
            "TeX Gyre Cursor", "TeX Gyre Heros", "TeX Gyre Schola",
        });

        //THE CONTROL — by construction no system font can be here (ruling R18
        //and standing rule 6): every family is one of the port's own six.
        world.Families.Should().HaveCount(6);
        foreach (TextFontFamily family in world.Families)
        {
            family.Faces.Should().HaveCount(4);
            foreach (TextFontFace face in family.Faces)
            {
                face.Location.Should().StartWith("CodeBrix.LilyPort.Engine.dll/");
                face.FileName.Should().EndWith(".otf");
            }
        }
    }

    /// <summary>
    /// Reading the engine's two lists directly answers what parsing its
    /// listing does.
    /// </summary>
    [Fact]
    public void reading_the_world_directly_matches_the_listing()
    {
        //Arrange
        TextFontWorld parsed = TextFontWorld.Parse(
            File.ReadAllText(FontFixtures.Path("fontworld.txt")));

        //Act
        TextFontWorld loaded = TextFontWorld.Load();

        //Assert
        loaded.Families.Select(family => family.Name)
            .Should().Equal(parsed.Families.Select(family => family.Name).ToList());
        loaded.FaceCount.Should().Be(parsed.FaceCount);
        loaded.ToListing().Should().Be(parsed.ToListing());
    }

    /// <summary>A document-supplied face is listed under its own family.</summary>
    [Fact]
    public void a_document_supplied_face_is_listed_separately()
    {
        //Arrange
        string listing =
            "vendored faces (1):\n"
            + "  C059 -- CodeBrix.LilyPort.Engine.dll/"
            + "CodeBrix.LilyPort.Engine.Fonts.text.C059-Roman.otf\n"
            + "document-supplied fonts (1):\n"
            + "  LilyJAZZ Text -- /home/scores/fonts/lilyjazz-text.otf\n";

        //Act
        TextFontWorld world = TextFontWorld.Parse(listing);

        //Assert
        world.Families.Should().HaveCount(2);
        world.DocumentSupplied.Should().HaveCount(1);
        world.DocumentSupplied[0].Name.Should().Be("LilyJAZZ Text");
        world.DocumentSupplied[0].Faces[0].FileName.Should().Be("lilyjazz-text.otf");
    }

    /// <summary>
    /// What the tab OFFERS is what the engine can actually select.
    /// </summary>
    /// <remarks>
    /// ⚠ MEASURED 2026-09-01: the engine resolves a family request through
    /// generics and the document's own registrations, never through the family
    /// name a vendored face declares — so a list of the six declared families
    /// would be inert (every one falls into R14's <c>unknown</c> arm and
    /// engraves in TeX Gyre Schola). See
    /// <see cref="TextFontWorld.Selectors"/>.
    /// </remarks>
    [Fact]
    public void the_offered_names_are_the_ones_the_engine_can_select()
    {
        //Arrange
        TextFontWorld world = TextFontWorld.Parse(
            File.ReadAllText(FontFixtures.Path("fontworld.txt")));

        //Act
        IReadOnlyList<TextFontSelector> choices = world.SelectableNames();

        //Assert
        choices.Select(choice => choice.Name).Should().Equal(new[]
        {
            "serif", "sans", "sans-serif", "monospace",
            "LilyPond Serif", "LilyPond Sans Serif", "LilyPond Monospace",
        });

        //Each name reaches its own two vendored families, in the engine's own
        //fallback order, and every face behind it is a real one.
        choices[0].FamilyNames.Should().Equal(new[] { "C059", "TeX Gyre Schola" });
        choices[1].FamilyNames.Should().Equal(new[] { "Nimbus Sans", "TeX Gyre Heros" });
        choices[3].FamilyNames.Should().Equal(new[] { "Nimbus Mono PS", "TeX Gyre Cursor" });
        foreach (TextFontSelector choice in choices)
        {
            choice.Faces.Should().HaveCount(8);
            choice.IsDocumentSupplied.Should().BeFalse();
        }

        //THE CONTROL: none of the six DECLARED families is offered on its own,
        //because asking for one of them by name selects nothing.
        choices.Select(choice => choice.Name).Should().NotContain("Nimbus Sans");
        choices.Select(choice => choice.Name).Should().NotContain("TeX Gyre Heros");
    }

    /// <summary>A document's own face IS selectable, under its own name.</summary>
    [Fact]
    public void a_document_supplied_family_is_offered_under_its_own_name()
    {
        //Arrange
        string listing =
            "vendored faces (1):\n"
            + "  C059 -- CodeBrix.LilyPort.Engine.dll/"
            + "CodeBrix.LilyPort.Engine.Fonts.text.C059-Roman.otf\n"
            + "document-supplied fonts (1):\n"
            + "  LilyJAZZ Text -- /home/scores/fonts/lilyjazz-text.otf\n";

        //Act
        IReadOnlyList<TextFontSelector> choices =
            TextFontWorld.Parse(listing).SelectableNames();

        //Assert
        //Only `serif' and `LilyPond Serif' can be answered from one C059 face;
        //the document's own name comes last and is exact (R16).
        choices.Last().Name.Should().Be("LilyJAZZ Text");
        choices.Last().IsDocumentSupplied.Should().BeTrue();
        choices.Last().Faces.Should().HaveCount(1);
    }

    /// <summary>The filter never hides a face, and always hides a music font.</summary>
    /// <remarks>Upstream's <c>FontFilterProxyModel</c>: "Child elements are
    /// never filtered. Font names that are also in the list of installed
    /// notation fonts are always filtered."</remarks>
    [Fact]
    public void the_filter_hides_music_fonts_and_matches_case_insensitively()
    {
        //Arrange
        TextFontWorld world = TextFontWorld.Parse(
            File.ReadAllText(FontFixtures.Path("fontworld.txt")));

        //Act
        IReadOnlyList<TextFontFamily> gyre = world.Filter("gyre");
        IReadOnlyList<TextFontFamily> hidden = world.Filter(
            string.Empty, new[] { "c059" });
        IReadOnlyList<TextFontFamily> broken = world.Filter("[");

        //Assert
        gyre.Select(family => family.Name).Should().Equal(new[]
        {
            "TeX Gyre Cursor", "TeX Gyre Heros", "TeX Gyre Schola",
        });
        gyre[0].Faces.Should().HaveCount(4);

        hidden.Select(family => family.Name).Should().NotContain("C059");
        hidden.Should().HaveCount(5);

        //A pattern that will not compile matches nothing rather than throwing:
        //the user is still typing it.
        broken.Should().BeEmpty();
    }

    // ---------------------------------------------------------- sample scores

    /// <summary>The six shipped samples are upstream's own, in its order.</summary>
    [Fact]
    public void the_six_samples_are_frescobaldis_own()
    {
        //Arrange
        //Act
        IReadOnlyList<string> ids = FontSamples.Provided
            .Select(sample => sample.Id).ToList();

        //Assert
        ids.Should().Equal(new[]
        {
            "bach.ly", "scriabine.ly", "berg-string-quartet.ly",
            "realbook.ly", "schenker.ly", "glyphs.ly",
        });
    }

    /// <summary>Every shipped sample is beside the application.</summary>
    [Fact]
    public void every_shipped_sample_is_installed()
    {
        //Arrange
        //Act
        //Assert
        foreach (FontSample sample in FontSamples.Provided)
        {
            File.Exists(FontSamples.TemplatePath(sample.Id))
                .Should().BeTrue(sample.Id + " should be shipped as an asset");
        }
    }

    /// <summary>
    /// A leading staff-size call is lifted out so it lands AFTER the fonts.
    /// </summary>
    [Fact]
    public void a_leading_staff_size_is_lifted_out_of_the_sample()
    {
        //Arrange
        const string Sample = "#(set-global-staff-size 16)\n{ c'4 }\n";

        //Act
        (string size, string body) = FontSamples.SplitStaffSize(Sample);
        string document = FontSamples.Compose("2.27.2", "\\paper { }", Sample);

        //Assert
        size.Should().Be("#(set-global-staff-size 16)");
        body.Should().Be("\n{ c'4 }\n");
        document.Should().StartWith("\\version \"2.27.2\"\n");
        document.IndexOf("set-global-staff-size", StringComparison.Ordinal)
            .Should().BeLessThan(document.IndexOf("\\paper", StringComparison.Ordinal));

        //THE CONTROL: a sample with no staff-size call keeps everything it had.
        (string none, string whole) = FontSamples.SplitStaffSize("{ c'4 }");
        none.Should().BeEmpty();
        whole.Should().Be("{ c'4 }");
    }

    /// <summary>
    /// The composed document puts the runner's book handler back after the
    /// sample has redefined it (board trap 7).
    /// </summary>
    /// <remarks>Four of the six samples open with
    /// <c>\include "lilypond-book-preamble.ly"</c>, which points
    /// <c>default-toplevel-book-handler</c> at
    /// <c>print-book-with-defaults-as-systems</c>. MEASURED: without the
    /// wrapper the engine produces ZERO pages and the preview says nothing
    /// useful.</remarks>
    [Fact]
    public void the_composed_sample_restores_the_runners_book_handler()
    {
        //Arrange
        string sample = File.ReadAllText(FontSamples.TemplatePath("bach.ly"));
        sample.Should().Contain("\\include \"lilypond-book-preamble.ly\"");

        //Act
        string document = FontSamples.Compose("2.27.2", "\\paper { }", sample);

        //Assert
        int held = document.IndexOf(
            "#(define fresco-brix-preview-book-handler default-toplevel-book-handler)",
            StringComparison.Ordinal);
        int include = document.IndexOf(
            "\\include \"lilypond-book-preamble.ly\"", StringComparison.Ordinal);
        int restored = document.IndexOf(
            "#(define default-toplevel-book-handler fresco-brix-preview-book-handler)",
            StringComparison.Ordinal);

        held.Should().BeGreaterThan(-1);
        include.Should().BeGreaterThan(held);
        restored.Should().BeGreaterThan(include);
    }

    /// <summary>Only the shipped samples are cached between runs.</summary>
    [Fact]
    public void only_the_shipped_samples_are_cached_persistently()
    {
        //Arrange
        //Act
        //Assert
        FontSamples.CachePersistently("bach.ly").Should().BeTrue();
        FontSamples.CachePersistently(FontSamples.CustomId).Should().BeFalse();
        FontSamples.CachePersistently(FontSamples.CurrentId).Should().BeFalse();
    }

    // -------------------------------------------------------- the two folders

    /// <summary>The Paths page's two music-font folders round-trip.</summary>
    [Fact]
    public void the_two_music_font_folders_round_trip_through_the_store()
    {
        //Arrange
        PathValues written = new PathValues
        {
            HyphenationPaths = Array.Empty<string>(),
            MusicFontRepository = "/home/scores/fonts",
            MusicFontCache = "/home/scores/font-cache",
        };

        //Act
        using (SettingsStore store = new SettingsStore(StorePath))
        {
            written.Save(store);
        }

        PathValues read = new PathValues();
        using (SettingsStore store = new SettingsStore(StorePath))
        {
            read.Load(store);
        }

        //Assert
        read.MusicFontRepository.Should().Be("/home/scores/fonts");
        read.MusicFontCache.Should().Be("/home/scores/font-cache");
    }

    /// <summary>Both folders are upstream's own settings keys.</summary>
    [Fact]
    public void the_two_folders_use_frescobaldis_own_keys()
    {
        //Arrange
        PathValues written = new PathValues
        {
            HyphenationPaths = Array.Empty<string>(),
            MusicFontRepository = "/repo",
            MusicFontCache = "/cache",
        };

        //Act
        using SettingsStore store = new SettingsStore(StorePath);
        written.Save(store);

        //Assert
        store.GetString("music-fonts/font-repo").Should().Be("/repo");
        store.GetString("music-fonts/font-cache").Should().Be("/cache");
    }

    /// <summary>An unset cache folder falls back to the temporary directory.</summary>
    [Fact]
    public void an_unset_cache_folder_falls_back_to_the_temporary_directory()
    {
        //Arrange
        using SettingsStore store = new SettingsStore(StorePath);

        //Act
        string fallback = DocumentFontSettings.PersistentCacheDirectory(store);
        store.SetString(DocumentFontSettings.FontCacheKey, "/chosen");
        string chosen = DocumentFontSettings.PersistentCacheDirectory(store);

        //Assert
        fallback.Should().Be(
            Path.Combine(Path.GetTempPath(), AppInfo.Name + "-music-font-samples"));
        chosen.Should().Be("/chosen");
    }

    /// <summary>No repository configured means no repository object.</summary>
    [Fact]
    public void an_unset_repository_answers_nothing()
    {
        //Arrange
        using SettingsStore store = new SettingsStore(StorePath);

        //Act
        MusicFontRepo none = DocumentFontSettings.MusicFontsRepo(store);
        store.SetString(DocumentFontSettings.FontRepoKey, _directory);
        MusicFontRepo some = DocumentFontSettings.MusicFontsRepo(store);

        //Assert
        none.Should().BeNull();
        some.Should().NotBeNull();
        some.Root.Should().Be(_directory);

        //Upstream's default is on, so a configured repository installs itself.
        DocumentFontSettings.AutoInstall(store).Should().BeTrue();
    }

    /// <summary>The installation folder sits beside the settings, not in the
    /// application's own directory.</summary>
    /// <remarks>⚠ The W13b removal of <c>&lt;appdir&gt;/fonts/otf</c> means the
    /// only place an installed font can live is user data.</remarks>
    [Fact]
    public void the_install_folder_is_user_data_beside_the_settings()
    {
        //Arrange
        //Act
        string folder = InstalledMusicFonts.DefaultDirectory();

        //Assert
        folder.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName, "fonts", "music"));
        folder.Should().NotBe(Path.Combine(AppContext.BaseDirectory, "fonts", "otf"));
    }
}
