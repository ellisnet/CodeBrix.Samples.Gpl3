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
/// Rulings FR9 and FR13 hold for what a TRANSLATION puts on screen, not only
/// for what the code asks for.
/// </summary>
/// <remarks>
/// Every msgid a ruling touched was rewritten, so the code cannot ask for a
/// string that names Frescobaldi or LilyPond in the chrome. A translator
/// writing for Frescobaldi can still put the product name into a message whose
/// English has none — correct there, wrong here — and
/// <see cref="MoCatalog.Guard"/> refuses those. This walks the real shipped
/// catalogs, so a catalog refresh that brings in a new one fails here.
/// </remarks>
public class CatalogNameGuardTests
{
    private static string CatalogPath(string language)
        => Path.Combine(
            AppContext.BaseDirectory, "assets", "i18n", language,
            "LC_MESSAGES", "frescobaldi.mo");

    /// <summary>Every shipped language.</summary>
    /// <returns>The language codes.</returns>
    public static IEnumerable<object[]> Languages()
        => LanguageSetup.Languages.Select(l => new object[] { l }).ToList();

    [Theory]
    [MemberData(nameof(Languages))]
    public void no_translation_this_application_shows_can_name_the_wrong_product(
        string language)
    {
        //Arrange
        byte[] data = File.ReadAllBytes(CatalogPath(language));
        MoCatalog catalog = new MoCatalog(language, MoFile.FromData(data));
        int examined = 0;
        int refused = 0;
        int offending = 0;

        //Act, Assert — every entry the catalog carries, asked for the way the
        //application asks for it.
        foreach (var record in MoFile.ParseMoDecode(data))
        {
            //The header, and the three plural entries — those are keyed by
            //(msgid, form) and are asked for through LookupPlural, which the
            //theory below covers.
            if (record.Messages.Count != 1 || record.Messages[0].Length == 0)
            {
                continue;
            }

            string message = record.Messages[0];
            string raw = record.Translations[0];
            string answer = catalog.Lookup(record.Context, message);
            examined++;

            bool introduces = MoCatalog.ForbiddenNames.Any(name =>
                raw.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                && message.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0);
            if (introduces) { offending++; }

            foreach (var name in MoCatalog.ForbiddenNames)
            {
                if (message.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                answer.IndexOf(name, StringComparison.OrdinalIgnoreCase)
                    .Should().BeLessThan(
                        0,
                        $"{language}: the translation of '{message}' would put "
                        + $"'{name}' on screen");
            }

            if (!string.Equals(answer, raw, StringComparison.Ordinal)
                && raw.Length > 0)
            {
                refused++;
            }
        }

        examined.Should().BeGreaterThan(100);

        //Exactly the offending entries are refused, and nothing else is
        //touched: the guard is not quietly dropping translations.
        refused.Should().Be(offending);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void no_plural_form_can_name_the_wrong_product_either(string language)
    {
        //Arrange
        byte[] data = File.ReadAllBytes(CatalogPath(language));
        MoCatalog catalog = new MoCatalog(language, MoFile.FromData(data));
        int examined = 0;

        //Act, Assert
        foreach (var record in MoFile.ParseMoDecode(data))
        {
            if (record.Messages.Count < 2) { continue; }

            for (long count = 0; count < 12; count++)
            {
                string english = count == 1 ? record.Messages[0] : record.Messages[1];
                string answer = catalog.LookupPlural(
                    record.Context, record.Messages[0], record.Messages[1], count);
                examined++;

                foreach (var name in MoCatalog.ForbiddenNames)
                {
                    if (english.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    answer.IndexOf(name, StringComparison.OrdinalIgnoreCase)
                        .Should().BeLessThan(0, $"{language} n={count}");
                }
            }
        }

        //Upstream has three plural entries; a catalog carries as many of them
        //as its translator confirmed, and each is asked over twelve counts.
        //Galician and Turkish confirmed none of the three, so they examine
        //nothing here and the assertion is only that the walk was consistent.
        (examined % 12).Should().Be(0);
        (examined / 12).Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public void the_russian_word_for_about_does_not_relabel_the_application()
    {
        //Arrange — ru.po translates the bare msgid "About" as "О Frescobaldi",
        //which is right for Frescobaldi's own About window and wrong for this
        //application's (ruling FR9).
        MoCatalog russian = new MoCatalog(
            "ru", MoFile.FromFile(CatalogPath("ru")));

        //Act, Assert
        russian.File.Gettext("About").Should().Be("О Frescobaldi");
        russian.Lookup(null, "About").Should().Be("About");

        //...and the message that DOES carry the placeholder is untouched, so
        //the window itself is still titled in Russian.
        russian.Lookup(null, "About {appname}").Should().Be("О {appname}");
    }

    [Fact]
    public void a_translation_naming_the_engine_is_refused()
    {
        //Arrange — ruling FR13: no UI element names LilyPond. Three catalogs
        //introduce the name into a message whose English has none.
        MoCatalog german = new MoCatalog("de", MoFile.FromFile(CatalogPath("de")));
        MoCatalog italian = new MoCatalog("it", MoFile.FromFile(CatalogPath("it")));
        MoCatalog russian = new MoCatalog("ru", MoFile.FromFile(CatalogPath("ru")));

        //Act, Assert
        german.Lookup(null, "Display Paper Columns")
            .Should().Be("Display Paper Columns");
        italian.Lookup(null, "Delete intermediate output files")
            .Should().Be("Delete intermediate output files");
        italian.Lookup(null, "Display plain log output")
            .Should().Be("Display plain log output");
        russian.Lookup(null, "Documentation Browser")
            .Should().Be("Documentation Browser");
        russian.Lookup(null, "&Documentation Browser")
            .Should().Be("&Documentation Browser");
    }

    [Fact]
    public void a_message_that_names_them_itself_keeps_its_translation()
    {
        //Arrange — the guard is about a translation INTRODUCING a name. A
        //message that already carries one is a message a ruling allowed
        //(About may state the lineage), and its translation stands.
        MoCatalog german = new MoCatalog("de", MoFile.FromFile(CatalogPath("de")));

        //Act, Assert
        german.Lookup(null, "A LilyPond Music Editor")
            .Should().Be("Ein Noten-Editor für LilyPond");
        german.Lookup("menu title", "&LilyPond").Should().Be("&LilyPond");
    }

    [Theory]
    [InlineData("Preferences", "Einstellungen", "Einstellungen")]
    [InlineData("About", "О Frescobaldi", "About")]
    [InlineData("Paper Columns", "LilyPond-Spalten", "Paper Columns")]
    [InlineData("A LilyPond thing", "Ein LilyPond-Ding", "Ein LilyPond-Ding")]
    [InlineData("About Frescobaldi", "О Frescobaldi", "О Frescobaldi")]
    public void the_guard_answers_the_english_only_when_a_name_is_introduced(
        string message, string translation, string expected)
    {
        //Arrange, Act, Assert
        MoCatalog.Guard(message, translation).Should().Be(expected);
    }
}
