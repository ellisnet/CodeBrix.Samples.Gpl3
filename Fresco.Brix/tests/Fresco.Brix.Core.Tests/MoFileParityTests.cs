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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// <see cref="MoFile"/> against Frescobaldi's own <c>i18n/mofile.py</c>, over
/// the thirteen catalogs this application ships:
/// <c>fixtures/i18n/catalogs.json</c> holds the counts, the header and a
/// SHA-256 over the WHOLE decoded catalog that UPSTREAM'S READER produced, and
/// <c>fixtures/i18n/entries.json</c> holds what its four <c>*gettext</c>
/// methods answered entry by entry. Regenerate with
/// <c>tools/i18nharvest/gen-i18n-fixtures.py</c>. Nothing here is recorded from
/// the port's own output.
/// </summary>
/// <remarks>
/// The same fixture also records what GNU <c>msgfmt</c> made of the same PO
/// files, which is how the catalogs themselves — written by
/// <c>tools/i18nharvest/harvest.py</c> rather than by <c>msgfmt</c> — are held
/// to the standard tool's answer.
/// </remarks>
public class MoFileParityTests
{
    //The separators the probe joined each record with, before hashing.
    private const string RecordSeparator = "\u001e";
    private const string UnitSeparator = "\u001f";

    private static string FixtureDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "i18n");

    private static JsonDocument Fixture(string name)
        => JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FixtureDirectory(), name)));

    private static string CatalogDirectory()
        => Path.Combine(AppContext.BaseDirectory, "assets", "i18n");

    private static string CatalogPath(string language)
        => Path.Combine(
            CatalogDirectory(), language, "LC_MESSAGES", "frescobaldi.mo");

    /// <summary>Every language the fixture covers.</summary>
    /// <returns>The language codes.</returns>
    public static IEnumerable<object[]> Languages()
    {
        using JsonDocument fixture = Fixture("catalogs.json");
        return fixture.RootElement.EnumerateObject()
            .Select(p => new object[] { p.Name })
            .ToList();
    }

    /// <summary>
    /// The same canonical form the probe hashed: every record as
    /// context, then the message forms, then the translated forms, sorted.
    /// </summary>
    /// <param name="records">The decoded records.</param>
    /// <returns>The canonical text.</returns>
    private static string Canonical(IEnumerable<MoRecord> records)
    {
        List<string> lines = new List<string>();
        foreach (var record in records)
        {
            lines.Add(string.Join(
                RecordSeparator,
                record.Context ?? "\u0000NONE",
                string.Join(UnitSeparator, record.Messages),
                string.Join(UnitSeparator, record.Translations)));
        }

        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines);
    }

    private static string Sha256(string text)
        => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    [Theory]
    [MemberData(nameof(Languages))]
    public void a_catalog_decodes_exactly_as_frescobaldis_reader_decodes_it(
        string language)
    {
        //Arrange
        using JsonDocument fixture = Fixture("catalogs.json");
        JsonElement entry = fixture.RootElement.GetProperty(language);
        byte[] data = File.ReadAllBytes(CatalogPath(language));

        //Act
        List<MoRecord> records = MoFile.ParseMoDecode(data).ToList();

        //Assert
        data.Length.Should().Be(entry.GetProperty("bytes").GetInt32());
        records.Count.Should().Be(entry.GetProperty("records").GetInt32());
        Sha256(Canonical(records)).Should().Be(
            entry.GetProperty("sha256").GetString(),
            $"the whole {language} catalog must decode the way mofile.py decodes it");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void a_catalog_holds_what_gnu_msgfmt_would_have_written(string language)
    {
        //Arrange
        using JsonDocument fixture = Fixture("catalogs.json");
        JsonElement entry = fixture.RootElement.GetProperty(language);

        //Act, Assert — the probe compiled the same PO file with msgfmt and
        //hashed both; the shipped catalog and the standard tool's agree.
        entry.GetProperty("identical_to_msgfmt").GetBoolean().Should().BeTrue();
        entry.GetProperty("msgfmt_sha256").GetString()
            .Should().Be(entry.GetProperty("sha256").GetString());
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void a_catalogs_counts_and_header_are_frescobaldis(string language)
    {
        //Arrange
        using JsonDocument fixture = Fixture("catalogs.json");
        JsonElement entry = fixture.RootElement.GetProperty(language);

        //Act
        MoFile catalog = MoFile.FromFile(CatalogPath(language));

        //Assert — upstream keys a plural entry by (msgid, form), so one plural
        //entry becomes as many dictionary entries as it has forms.
        int singular = entry.GetProperty("singular").GetInt32();
        int plural = entry.GetProperty("plural").GetInt32();
        int contextual = entry.GetProperty("contextual").GetInt32();

        catalog.Count.Should().BeGreaterThanOrEqualTo(singular);
        catalog.ContextCount.Should().BeGreaterThan(0);
        (singular + contextual + plural).Should().BeGreaterThan(0);

        foreach (var field in entry.GetProperty("info").EnumerateObject())
        {
            catalog.Info.Should().ContainKey(field.Name);
            catalog.Info[field.Name].Should().Be(field.Value.GetString());
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void every_recorded_lookup_answers_as_frescobaldis_reader_does(
        string language)
    {
        //Arrange
        using JsonDocument fixture = Fixture("entries.json");
        MoFile catalog = MoFile.FromFile(CatalogPath(language));

        //Act, Assert
        foreach (var row in fixture.RootElement.GetProperty(language).EnumerateArray())
        {
            string call = row.GetProperty("call").GetString();
            string message = row.GetProperty("message").GetString();
            string expected = row.GetProperty("answer").GetString();

            string answer = call switch
            {
                "gettext" => catalog.Gettext(message),
                "pgettext" => catalog.Pgettext(
                    row.GetProperty("context").GetString(), message),
                "ngettext" => catalog.Ngettext(
                    message,
                    row.GetProperty("plural").GetString(),
                    row.GetProperty("count").GetInt64()),
                _ => catalog.Npgettext(
                    row.GetProperty("context").GetString(),
                    message,
                    row.GetProperty("plural").GetString(),
                    row.GetProperty("count").GetInt64()),
            };

            answer.Should().Be(
                expected, $"{language}: {call}({message})");
        }
    }

    [Fact]
    public void a_msgid_a_ruling_renamed_is_in_no_catalog_and_stays_english()
    {
        //Arrange — ruling FR13 renames the engine in every piece of chrome, so
        //these msgids are Fresco.Brix's own and no Frescobaldi catalog has
        //them. That they fall back to English is the DESIGN, and this is what
        //says so out loud. They are listed in
        //tools/i18nharvest/renamed-strings.tsv.
        string[] renamed =
        {
            "LilyPort Log",
            "A LilyPort Music Editor",
            "Run LilyPort",
            "Run LilyPort with verbose output",
            "The default folder for your LilyPort documents (optional).",
            "Create a document that contains the LilyPort version statement",
        };

        //Act, Assert
        foreach (var language in LanguageSetup.Languages)
        {
            MoFile catalog = MoFile.FromFile(CatalogPath(language));
            foreach (var message in renamed)
            {
                catalog.Has(null, message).Should().BeFalse(
                    $"{language} must not carry the Fresco.Brix-original '{message}'");
                catalog.Gettext(message).Should().Be(message);
            }

            catalog.Has("menu title", "&LilyPort").Should().BeFalse();
            catalog.Pgettext("menu title", "&LilyPort").Should().Be("&LilyPort");
        }
    }

    [Fact]
    public void a_msgid_upstream_owns_is_translated_in_a_complete_catalog()
    {
        //Arrange — the counterpart of the test above: the machinery really
        //does translate, so a fallback means "no translation", not "no reader".
        MoFile german = MoFile.FromFile(CatalogPath("de"));

        //Act, Assert
        german.Has(null, "A LilyPond Music Editor").Should().BeTrue();
        german.Gettext("A LilyPond Music Editor")
            .Should().NotBe("A LilyPond Music Editor");
        german.Pgettext("QPlatformTheme", "Cancel").Should().Be("Abbrechen");
    }

    [Fact]
    public void reading_something_that_is_not_a_catalog_says_so()
    {
        //Arrange, Act, Assert — upstream raises IOError(0, 'Invalid MO data').
        Action reading = () => MoFile.FromData(
            Encoding.ASCII.GetBytes("this is not a catalog at all"));
        reading.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void an_empty_catalog_answers_everything_untranslated()
    {
        //Arrange
        NullMoFile empty = new NullMoFile();

        //Act, Assert — upstream's NullMoFile, method for method.
        empty.Gettext("&File").Should().Be("&File");
        empty.Pgettext("menu title", "&File").Should().Be("&File");
        empty.Ngettext("one", "many", 1).Should().Be("one");
        empty.Ngettext("one", "many", 2).Should().Be("many");
        empty.Npgettext("ctx", "one", "many", 0).Should().Be("many");
        empty.Fallback().Should().BeNull();
    }

    [Fact]
    public void a_catalog_falls_back_to_the_one_behind_it()
    {
        //Arrange — upstream's set_fallback chain. German has no msgid of
        //Fresco.Brix's own, so a fallback that does is what answers.
        MoFile german = MoFile.FromFile(CatalogPath("de"));
        MoFile italian = MoFile.FromFile(CatalogPath("it"));

        //Act
        german.SetFallback(italian);

        //Assert
        german.Fallback().Should().BeSameAs(italian);
        german.Gettext("A LilyPond Music Editor")
            .Should().Be(MoFile.FromFile(CatalogPath("de"))
                .Gettext("A LilyPond Music Editor"));
    }
}
