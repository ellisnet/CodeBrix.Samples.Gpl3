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
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>A catalog that answers from a table, for the tests.</summary>
internal sealed class TestCatalog : ITranslationCatalog
{
    //Gettext separates a context from its message with EOT, and so does this.
    private const string ContextSeparator = "\u0004";

    private readonly Dictionary<string, string> _entries
        = new Dictionary<string, string>(StringComparer.Ordinal);

    public TestCatalog(string language) => Language = language;

    public string Language { get; }

    public void Add(string context, string message, string translation)
        => _entries[Key(context, message)] = translation;

    public string Lookup(string context, string message)
        => _entries.TryGetValue(Key(context, message), out var translation)
            ? translation
            : null;

    public string LookupPlural(string context, string message, string plural, long count)
        => Lookup(context, count == 1 ? message : plural);

    private static string Key(string context, string message)
        => (context ?? string.Empty) + ContextSeparator + message;
}

/// <summary>Looking user-visible strings up.</summary>
/// <remarks>The installed catalog is process-wide, so these share the
/// collection that installs one (board trap 55).</remarks>
[Collection(nameof(LanguageCollection))]
public class TranslationTests : IDisposable
{
    public void Dispose() => I18n.Install(null);

    [Fact]
    public void an_untranslated_string_comes_back_as_english()
    {
        //Arrange
        I18n.Install(null);

        //Act, Assert
        I18n.Get("&Save Document").Should().Be("&Save Document");
    }

    [Fact]
    public void an_installed_catalog_is_consulted()
    {
        //Arrange
        TestCatalog catalog = new TestCatalog("de");
        catalog.Add(null, "&Save Document", "&Dokument speichern");

        //Act
        I18n.Install(catalog);

        //Assert
        I18n.Get("&Save Document").Should().Be("&Dokument speichern");
        I18n.Language.Should().Be("de");
    }

    [Fact]
    public void a_string_the_catalog_has_never_seen_falls_back_to_english()
    {
        //Arrange
        I18n.Install(new TestCatalog("de"));

        //Act, Assert — our own divergent strings behave this way until the
        //harvest tool catches up with them.
        I18n.Get("Fresco.Brix only string").Should().Be("Fresco.Brix only string");
    }

    [Fact]
    public void the_context_distinguishes_two_uses_of_the_same_english()
    {
        //Arrange
        TestCatalog catalog = new TestCatalog("de");
        catalog.Add("menu title", "&File", "&Datei");
        catalog.Add("action: new document", "New", "Neu");

        //Act
        I18n.Install(catalog);

        //Assert
        I18n.Get("menu title", "&File").Should().Be("&Datei");
        I18n.Get("action: new document", "New").Should().Be("Neu");
        I18n.Get("New").Should().Be("New");
    }

    [Theory]
    [InlineData(1, "one file")]
    [InlineData(2, "several files")]
    [InlineData(0, "several files")]
    public void a_plural_picks_the_form_for_the_count(long count, string expected)
    {
        //Arrange
        I18n.Install(null);

        //Act
        string text = I18n.Get("one file", "several files", count);

        //Assert
        text.Should().Be(expected);
    }

    [Fact]
    public void a_placeholder_is_filled_in_by_name()
    {
        //Arrange, Act
        string text = I18n.Format("&About {appname}...", ("appname", "Fresco.Brix"));

        //Assert
        text.Should().Be("&About Fresco.Brix...");
    }

    [Fact]
    public void several_placeholders_are_filled_in_any_order()
    {
        //Arrange, Act — translators reorder them, which is why they are named.
        string text = I18n.Format(
            "{error} while reading {filename}",
            ("filename", "score.ly"), ("error", "No such file"));

        //Assert
        text.Should().Be("No such file while reading score.ly");
    }

    [Fact]
    public void an_unknown_placeholder_is_left_alone_rather_than_blanking_the_label()
    {
        //Arrange, Act
        string text = I18n.Format("Hello {who} from {where}", ("who", "Fresco"));

        //Assert
        text.Should().Be("Hello Fresco from {where}");
    }

    [Fact]
    public void an_unclosed_brace_does_not_throw_in_front_of_the_user()
    {
        //Arrange, Act
        string text = I18n.Format("Broken {appname", ("appname", "Fresco.Brix"));

        //Assert
        text.Should().Be("Broken {appname");
    }

    [Fact]
    public void changing_the_language_is_announced()
    {
        //Arrange
        int announcements = 0;
        void Handler(object sender, EventArgs e) => announcements++;
        I18n.LanguageChanged += Handler;

        //Act
        I18n.Install(new TestCatalog("fr"));
        I18n.LanguageChanged -= Handler;

        //Assert
        announcements.Should().Be(1);
    }
}
