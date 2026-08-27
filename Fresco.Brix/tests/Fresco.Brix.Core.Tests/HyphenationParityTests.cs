// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Editor;
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
/// <see cref="Hyphenator"/> against Frescobaldi's own hyphenator:
/// <c>fixtures/hyphenation.json</c> holds what <c>hyphenator.py</c> ITSELF
/// answered, over all 24 bundled dictionaries (regenerate with
/// <c>tools/hyphenprobe/gen-hyphen-fixtures.py</c>). Nothing here is recorded
/// from the port's own output.
/// </summary>
/// <remarks>
/// Upstream's module needs no PyQt and no Frescobaldi, so the probe imports
/// and calls it directly rather than lifting definitions out of it by AST
/// the way <c>tools/varprobe</c> has to (board trap 21).
/// </remarks>
public class HyphenationParityTests
{
    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "hyphenation.json");

    private static string DictionaryPath(string file)
        => Path.Combine(HyphenDictionaries.BundledDirectory, file);

    /// <summary>
    /// Reads the patterns a fixture entry describes: a bundled dictionary, or
    /// one of the made-up ones the probe writes to exercise the non-standard
    /// spellings none of the bundled dictionaries happens to use.
    /// </summary>
    /// <param name="entry">The fixture entry.</param>
    /// <returns>The patterns.</returns>
    private static HyphenationPatterns PatternsFor(JsonElement entry)
    {
        JsonElement file = entry.GetProperty("file");
        return file.ValueKind == JsonValueKind.Null
            ? HyphenationPatterns.Parse(entry.GetProperty("text").GetString())
            : HyphenationPatterns.Read(DictionaryPath(file.GetString()));
    }

    /// <summary>Makes the hyphenator a fixture entry describes.</summary>
    /// <param name="entry">The fixture entry.</param>
    /// <returns>The hyphenator, with the entry's own margins.</returns>
    private static Hyphenator HyphenatorFor(JsonElement entry)
        => new Hyphenator(
            PatternsFor(entry),
            entry.GetProperty("left").GetInt32(),
            entry.GetProperty("right").GetInt32());

    /// <summary>Every dictionary in the fixture, as test data.</summary>
    /// <returns>The dictionary indexes and their languages.</returns>
    public static IEnumerable<object[]> Dictionaries()
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        return fixture.RootElement.EnumerateArray()
            .Select((element, index) => new object[]
            {
                index,
                element.GetProperty("language").GetString(),
            })
            .ToList();
    }

    [Theory]
    [MemberData(nameof(Dictionaries))]
    public void the_patterns_read_match_frescobaldi(int index, string language)
    {
        //Arrange
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        JsonElement entry = fixture.RootElement[index];

        //Act
        HyphenationPatterns patterns = PatternsFor(entry);

        //Assert — the count catches a character set read wrongly, which
        //silently turns letters into other letters and keys the table by them.
        patterns.Count.Should().Be(entry.GetProperty("patterns").GetInt32());
        language.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(Dictionaries))]
    public void the_break_points_match_frescobaldi(int index, string language)
    {
        //Arrange
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        JsonElement entry = fixture.RootElement[index];
        HyphenationPatterns patterns = PatternsFor(entry);
        Hyphenator hyphenator = HyphenatorFor(entry);

        List<string> expected = new List<string>();
        List<string> actual = new List<string>();

        //Act
        foreach (JsonElement probe in entry.GetProperty("probes").EnumerateArray())
        {
            string word = probe.GetProperty("word").GetString();

            expected.Add($"{word} raw:{Numbers(probe.GetProperty("raw"))}");
            actual.Add($"{word} raw:{string.Join(
                ",", patterns.Positions(word).Select(p => p.Index))}");

            expected.Add($"{word} cut:{Numbers(probe.GetProperty("positions"))}");
            actual.Add($"{word} cut:{string.Join(
                ",", hyphenator.Positions(word).Select(p => p.Index))}");

            //And the change, the index and the cut a dictionary that spells
            //its breaks its own way carries at each of them.
            expected.Add($"{word} data:{Data(probe.GetProperty("data"))}");
            actual.Add($"{word} data:{string.Join(
                " ",
                hyphenator.Positions(word).Select(p => p.IsNonstandard
                    ? $"{p.Change},{p.ChangeIndex},{p.Cut}"
                    : "-"))}");
        }

        //Assert
        string.Join("\n", actual).Should().Be(string.Join("\n", expected));
        language.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(Dictionaries))]
    public void the_hyphenated_words_match_frescobaldi(int index, string language)
    {
        //Arrange
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        JsonElement entry = fixture.RootElement[index];
        Hyphenator hyphenator = HyphenatorFor(entry);

        List<string> expected = new List<string>();
        List<string> actual = new List<string>();

        //Act
        foreach (JsonElement probe in entry.GetProperty("probes").EnumerateArray())
        {
            string word = probe.GetProperty("word").GetString();

            //Both hyphens: the lyric one the application asks for, and the
            //plain one, because the two take different paths through the
            //splice when a dictionary spells a break its own way.
            expected.Add(probe.GetProperty("inserted").GetString());
            actual.Add(hyphenator.Inserted(word, " -- "));
            expected.Add(probe.GetProperty("plain").GetString());
            actual.Add(hyphenator.Inserted(word));
        }

        //Assert
        string.Join("\n", actual).Should().Be(string.Join("\n", expected));
        language.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(Dictionaries))]
    public void the_ways_of_breaking_in_two_match_frescobaldi(int index, string language)
    {
        //Arrange
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        JsonElement entry = fixture.RootElement[index];
        Hyphenator hyphenator = HyphenatorFor(entry);

        List<string> expected = new List<string>();
        List<string> actual = new List<string>();

        //Act
        foreach (JsonElement probe in entry.GetProperty("probes").EnumerateArray())
        {
            string word = probe.GetProperty("word").GetString();
            foreach (JsonElement pair in probe.GetProperty("iterate").EnumerateArray())
            {
                expected.Add($"{pair[0].GetString()}|{pair[1].GetString()}");
            }

            foreach (var (first, second) in hyphenator.Iterate(word))
            {
                actual.Add($"{first}|{second}");
            }
        }

        //Assert
        string.Join("\n", actual).Should().Be(string.Join("\n", expected));
        language.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void a_hex_escape_becomes_the_character_it_names()
    {
        //Arrange, Act — upstream's ^^hh, which the Swedish dictionary uses.
        string resolved = HyphenationPatterns.ReplaceHex("a^^e4b");

        //Assert
        resolved.Should().Be("aäb");
    }

    [Fact]
    public void the_bundled_dictionaries_are_all_found()
    {
        //Arrange, Act
        IReadOnlyDictionary<string, string> found
            = HyphenDictionaries.FindDictionaries();

        //Assert — the 24 files this application ships. A machine with its own
        //hyphen directory offers more, never fewer.
        foreach (var language in new[]
        {
            "cs_CZ", "da_DK", "de_DE", "el_GR", "en_CA", "en_GB", "en_US",
            "es_ES", "fi_FI", "fr", "ga_IE", "hu", "id_ID", "is_IS", "it",
            "nl_NL", "nn_NO", "pl", "pt_BR", "pt_PT", "ru_RU", "sk_SK",
            "sv_SE", "uk_UA",
        })
        {
            found.ContainsKey(language).Should().BeTrue();
        }
    }

    [Fact]
    public void every_bundled_dictionary_keeps_its_license_notice()
    {
        //Arrange — some of these dictionaries are conveyed on condition that
        //their README travels with them (the Dutch one says so in as many
        //words), so a dictionary shipped without one is a compliance bug.
        //See THIRD-PARTY-NOTICES.txt section 5.
        //
        //Three dictionaries have no README of their own. en_US is covered by
        //the en_CA/en_GB one, which names hyph_en_US as the same patterns;
        //fr and it arrived with none at all, which is section 5's open row.
        Dictionary<string, string> sharedReadme = new Dictionary<string, string>
        {
            ["en_US"] = "README_hyph_en_GB.txt",

            //Named for the country, while the dictionary is named for the
            //language alone.
            ["pl"] = "README_hyph_pl_PL.txt",
            ["hu"] = "README_hyph_hu_HU.txt",
        };
        string[] undocumented = { "fr", "it" };

        //Act
        List<string> missing = new List<string>();
        foreach (var file in Directory.GetFiles(
            HyphenDictionaries.BundledDirectory, "hyph_*.dic"))
        {
            string language = Path.GetFileNameWithoutExtension(file).Substring(5);
            if (undocumented.Contains(language)) { continue; }

            if (sharedReadme.TryGetValue(language, out string shared))
            {
                if (!File.Exists(Path.Combine(
                    HyphenDictionaries.BundledDirectory, shared)))
                {
                    missing.Add(language);
                }

                continue;
            }

            string stem = Path.Combine(
                HyphenDictionaries.BundledDirectory,
                "README_" + Path.GetFileNameWithoutExtension(file));
            if (!File.Exists(stem + ".txt") && !File.Exists(stem + ".txt.gz"))
            {
                missing.Add(language);
            }
        }

        //Assert
        string.Join(",", missing).Should().Be(string.Empty);
    }

    [Theory]
    [InlineData("ISO8859-1", 0xE4, "ä")]
    [InlineData("ISO8859-2", 0xE1, "á")]
    [InlineData("ISO8859-7", 0xE1, "α")]
    [InlineData("KOI8-R", 0xC1, "а")]
    [InlineData("KOI8-U", 0xA4, "є")]
    public void a_dictionarys_own_character_set_is_honoured(
        string charset, int value, string expected)
    {
        //Arrange, Act — .NET carries Latin-1 and nothing else of these, which
        //is why the tables exist at all.
        bool decoded = Charsets.TryDecode(
            charset, new[] { (byte)value }, out string text);

        //Assert
        decoded.Should().BeTrue();
        text.Should().Be(expected);
    }

    private static string Data(JsonElement array)
        => string.Join(
            " ",
            array.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null
                ? "-"
                : $"{e[0].GetString()},{e[1].GetInt32()},{e[2].GetInt32()}"));

    private static string Numbers(JsonElement array)
        => string.Join(",", array.EnumerateArray().Select(e => e.GetInt32()));
}
