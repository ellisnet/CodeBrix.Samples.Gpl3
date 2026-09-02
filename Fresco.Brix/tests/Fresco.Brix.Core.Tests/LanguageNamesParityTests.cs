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
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// <see cref="LanguageNames"/> against Frescobaldi's own
/// <c>language_names.languageName()</c>:
/// <c>fixtures/i18n/languagenames.json</c> holds what UPSTREAM'S OWN FUNCTION
/// answered over a spread of codes in every language. Regenerate with
/// <c>tools/i18nharvest/gen-i18n-fixtures.py</c>. Nothing here is recorded from
/// the port's own output.
/// </summary>
/// <remarks>
/// The port keeps thirteen of upstream's eighteen tables: ruling FR5.6 leaves
/// out the five CJK interface languages, and their tables go with them. That
/// makes no difference to any row here, because the probe only asks in
/// languages the port keeps — and for <c>en</c> and <c>sv</c>, which NEITHER
/// side has a table for, both fall through to <c>C</c> and answer the same.
/// The test counts the rows it checked and asserts that it skipped none, so a
/// table quietly going missing fails.
/// </remarks>
public class LanguageNamesParityTests
{
    private static string FixturePath()
        => Path.Combine(
            AppContext.BaseDirectory, "fixtures", "i18n", "languagenames.json");

    private static JsonDocument Fixture()
        => JsonDocument.Parse(File.ReadAllText(FixturePath()));

    private static IReadOnlyList<string> Kept()
    {
        using JsonDocument fixture = Fixture();
        return fixture.RootElement.GetProperty("tables_kept")
            .EnumerateArray().Select(e => e.GetString()).ToList();
    }

    [Fact]
    public void the_tables_kept_are_the_thirteen_the_ruling_leaves()
    {
        //Arrange
        using JsonDocument fixture = Fixture();
        List<string> upstream = fixture.RootElement.GetProperty("tables_upstream")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        //Act
        IReadOnlyList<string> kept = Kept();

        //Assert — C, plus the eleven of the thirteen interface languages
        //upstream has a table for. Upstream has none for `en' or `sv' either:
        //both already fall through to C there.
        upstream.Count.Should().Be(18);
        kept.Count.Should().Be(13);
        LanguageNames.NamingLanguages.Count.Should().Be(13);
        foreach (var table in kept)
        {
            LanguageNames.NamingLanguages.Should().Contain(table);
        }

        kept.Should().Contain("C");
        kept.Should().NotContain("ja");
        kept.Should().NotContain("zh_CN");
        upstream.Should().NotContain("en");
        upstream.Should().NotContain("sv");
    }

    [Fact]
    public void every_recorded_name_is_frescobaldis()
    {
        //Arrange
        using JsonDocument fixture = Fixture();
        IReadOnlyList<string> kept = Kept();
        List<string> upstream = fixture.RootElement.GetProperty("tables_upstream")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        int checkedRows = 0;
        int droppedTableRows = 0;

        //Act, Assert
        foreach (var row in fixture.RootElement.GetProperty("rows").EnumerateArray())
        {
            string code = row.GetProperty("code").GetString();
            JsonElement languageValue = row.GetProperty("language");
            string language = languageValue.ValueKind == JsonValueKind.Null
                ? null
                : languageValue.GetString();
            string expected = row.GetProperty("answer").GetString();

            //A naming language whose table upstream HAS and this application
            //dropped can only differ. A language neither has a table for --
            //`en' and `sv' -- falls through to C on BOTH sides, so it is
            //checked like any other.
            if (language != null && upstream.Contains(language)
                && !kept.Contains(language))
            {
                droppedTableRows++;
                continue;
            }

            //`null' means "the current locale" upstream and "the interface
            //language" here; with no catalog installed both are English, so
            //the C table answers, and that is what is compared.
            string answer = language == null
                ? LanguageNames.LanguageName(code, "C")
                : LanguageNames.LanguageName(code, language);

            answer.Should().Be(expected, $"languageName({code}, {language})");
            checkedRows++;
        }

        checkedRows.Should().Be(
            fixture.RootElement.GetProperty("rows").GetArrayLength());
        droppedTableRows.Should().Be(0);
    }

    [Fact]
    public void a_language_with_no_table_of_its_own_is_named_in_english()
    {
        //Arrange, Act, Assert — upstream has no `sv' and no `en' table, so
        //both fall through to C, which is English.
        LanguageNames.LanguageName("nl", "sv").Should().Be("Dutch");
        LanguageNames.LanguageName("nl", "en").Should().Be("Dutch");
        LanguageNames.LanguageName("nl", "nl").Should().Be("Nederlands");
        LanguageNames.LanguageName("nl", "de").Should().Be("Niederländisch");
    }

    [Fact]
    public void a_regional_code_settles_for_its_base_language()
    {
        //Arrange, Act, Assert — upstream tries the code, then its base.
        LanguageNames.LanguageName("de_DE", "C").Should().Be("German");
        LanguageNames.LanguageName("pt_BR", "C").Should().Be("Brazilian Portuguese");
        LanguageNames.LanguageName("nl_NL", "C").Should().Be("Dutch");
    }

    [Fact]
    public void a_code_that_names_no_language_comes_back_as_itself()
    {
        //Arrange, Act, Assert
        LanguageNames.LanguageName("xx", "C").Should().Be("xx");
        LanguageNames.LanguageName("xx_YY", "C").Should().Be("xx_YY");
        LanguageNames.LanguageName(string.Empty).Should().Be(string.Empty);
        LanguageNames.LanguageName(null).Should().BeNull();
    }
}
