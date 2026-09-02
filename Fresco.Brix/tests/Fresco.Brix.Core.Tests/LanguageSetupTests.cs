// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// Finding, choosing and installing an interface language.
/// </summary>
/// <remarks>
/// These tests INSTALL a catalog, which is process-global state — the same
/// thing upstream's <c>builtins._</c> is — so they share a collection that is
/// not run in parallel with anything else that reads it (board trap 55), and
/// each one puts English back.
/// </remarks>
[Collection(nameof(LanguageCollection))]
public class LanguageSetupTests : IDisposable
{
    /// <summary>Puts the application back into English.</summary>
    public void Dispose()
    {
        LanguageSetup.CatalogDirectoryOverride = null;
        LanguageSetup.Reset();
    }

    private static SettingsStore Store()
        => new SettingsStore(Path.Combine(
            Path.GetTempPath(),
            "frescobrix-i18n-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void the_thirteen_ruled_languages_are_installed()
    {
        //Arrange, Act
        IReadOnlyList<string> available = LanguageSetup.Available();

        //Assert — ruling FR5.6's list, and nothing else. The five CJK
        //catalogs Frescobaldi also ships are deliberately not here.
        available.Should().BeEquivalentTo(LanguageSetup.Languages);
        available.Count.Should().Be(13);
        available.Should().NotContain("ja");
        available.Should().NotContain("zh_CN");
        available.Should().NotContain("en");
    }

    [Fact]
    public void english_needs_no_catalog()
    {
        //Arrange, Act, Assert — upstream's own test, verbatim: "C", "en" and
        //"en_XXX" all mean untranslated.
        LanguageSetup.IsEnglish("C").Should().BeTrue();
        LanguageSetup.IsEnglish("en").Should().BeTrue();
        LanguageSetup.IsEnglish("en_GB").Should().BeTrue();
        LanguageSetup.IsEnglish(null).Should().BeTrue();
        LanguageSetup.IsEnglish("de").Should().BeFalse();
        LanguageSetup.IsEnglish("pt_BR").Should().BeFalse();

        LanguageSetup.CatalogFor("C").Should().BeNull();
        LanguageSetup.CatalogFor("en_GB").Should().BeNull();
    }

    [Fact]
    public void a_regional_code_reads_its_base_languages_catalog()
    {
        //Arrange, Act — upstream: "either `language' or
        //`language.split('_')[0]' is in the list returned by available()".
        ITranslationCatalog german = LanguageSetup.CatalogFor("de_AT");

        //Assert
        german.Should().NotBeNull();
        german.Lookup(null, "A LilyPond Music Editor")
            .Should().Be("Ein Noten-Editor für LilyPond");
    }

    [Fact]
    public void a_language_with_no_catalog_says_so()
    {
        //Arrange, Act, Assert — upstream raises UnknownLanguageError.
        Action asking = () => LanguageSetup.CatalogFor("ja");
        asking.Should().Throw<UnknownLanguageException>();
    }

    [Fact]
    public void installing_a_language_changes_what_the_lookup_answers()
    {
        //Arrange
        I18n.Get("A LilyPond Music Editor").Should().Be("A LilyPond Music Editor");

        //Act
        LanguageSetup.Install("de");

        //Assert
        I18n.Language.Should().Be("de");
        I18n.Get("A LilyPond Music Editor")
            .Should().Be("Ein Noten-Editor für LilyPond");
        I18n.Get("QPlatformTheme", "Cancel").Should().Be("Abbrechen");
    }

    [Fact]
    public void a_renamed_msgid_stays_english_in_every_language()
    {
        //Arrange — ruling FR13's consequence, the whole point of the
        //renamed-string table: these are Fresco.Brix's own msgids and no
        //Frescobaldi catalog has them.
        foreach (var language in LanguageSetup.Languages)
        {
            //Act
            LanguageSetup.Install(language);

            //Assert
            I18n.Get("LilyPort Log").Should().Be("LilyPort Log");
            I18n.Get("A LilyPort Music Editor").Should().Be("A LilyPort Music Editor");
            I18n.Get("menu title", "&LilyPort").Should().Be("&LilyPort");
        }
    }

    [Fact]
    public void the_setting_decides_the_language()
    {
        //Arrange
        using SettingsStore settings = Store();
        settings.SetString("language", "fr");

        //Act
        string installed = LanguageSetup.Setup(settings);

        //Assert
        installed.Should().Be("fr");
        I18n.Language.Should().Be("fr");
        //⚠ The French translators dropped the accelerator marker; the msgid
        //keeps it and the translation does not, which is exactly what a
        //display that strips markers has to cope with.
        I18n.Get("menu title", "&File").Should().Be("Fichier");
    }

    [Fact]
    public void the_setting_C_means_untranslated_english()
    {
        //Arrange
        using SettingsStore settings = Store();
        settings.SetString("language", "C");

        //Act
        string installed = LanguageSetup.Setup(settings);

        //Assert
        installed.Should().Be("C");
        I18n.Language.Should().Be("en");
        I18n.Get("menu title", "&File").Should().Be("&File");
    }

    [Fact]
    public void a_setting_naming_a_language_with_no_catalog_falls_back_to_english()
    {
        //Arrange — upstream shows an error box and carries on in English;
        //the picker can never produce this, but a hand-edited store can.
        using SettingsStore settings = Store();
        settings.SetString("language", "ja");

        //Act
        string installed = LanguageSetup.Setup(settings);

        //Assert
        installed.Should().Be("C");
        I18n.Get("menu title", "&File").Should().Be("&File");
    }

    [Fact]
    public void with_no_catalogs_at_all_the_application_runs_in_english()
    {
        //Arrange — the assets folder is droppable (board rule 13).
        string empty = Path.Combine(
            Path.GetTempPath(), "frescobrix-i18n-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        LanguageSetup.CatalogDirectoryOverride = empty;
        LanguageSetup.Reset();
        LanguageSetup.CatalogDirectoryOverride = empty;

        using SettingsStore settings = Store();
        settings.SetString("language", "de");

        //Act
        string installed = LanguageSetup.Setup(settings);

        //Assert
        LanguageSetup.Available().Should().BeEmpty();
        installed.Should().Be("C");
        I18n.Get("menu title", "&File").Should().Be("&File");

        Directory.Delete(empty, recursive: true);
    }

    [Fact]
    public void the_system_default_is_the_first_preferred_language_we_have()
    {
        //Arrange, Act — upstream's default(): the first of the operating
        //system's languages that is available, or "C".
        string chosen = LanguageSetup.Default();

        //Assert
        List<string> allowed = new List<string>(LanguageSetup.Available()) { "en", "C" };
        (allowed.Contains(chosen)
            || allowed.Contains(chosen.Split('_')[0])).Should().BeTrue(
            $"default() answered '{chosen}', which is neither available nor C");
    }

    [Fact]
    public void the_preferred_languages_are_the_locales_own_chain()
    {
        //Arrange, Act
        IReadOnlyList<string> preferred = LanguageSetup.Preferred();

        //Assert — upstream separates language and country with an underscore,
        //never a hyphen, because that is how a catalog folder is named.
        foreach (var language in preferred)
        {
            language.Should().NotContain("-");
        }
    }

    [Fact]
    public void a_catalog_is_read_once_and_kept()
    {
        //Arrange, Act
        ITranslationCatalog first = LanguageSetup.CatalogFor("it");
        ITranslationCatalog second = LanguageSetup.CatalogFor("it");

        //Assert
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void the_score_wizard_can_name_parts_in_another_language()
    {
        //Arrange — upstream's `i18n.translator(lang)': the part titles are
        //written with a DIFFERENT translator from the interface's.
        LanguageSetup.Install("de");

        //Act
        Translator french = I18n.TranslatorFor("fr");

        //Assert
        I18n.Get("Violin").Should().Be("Violine");
        french(null, "Violin").Should().Be("Violon");
        I18n.TranslatorFor("C")(null, "Violin").Should().Be("Violin");
        I18n.TranslatorFor("ja")(null, "Violin").Should().Be("Violin");
    }
}

/// <summary>
/// The collection every test that installs a catalog belongs to.
/// </summary>
/// <remarks>The installed catalog is process-wide, exactly as upstream's
/// <c>builtins._</c> is; two tests changing it at once would see each
/// other's language (board trap 55).</remarks>
[CollectionDefinition(nameof(LanguageCollection), DisableParallelization = true)]
public class LanguageCollection
{
}
