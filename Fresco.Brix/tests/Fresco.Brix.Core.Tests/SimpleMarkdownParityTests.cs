// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.UserGuide;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The markdown parser against Frescobaldi's OWN <c>simplemarkdown.py</c>.
/// </summary>
/// <remarks>
/// <c>fixtures/userguide/simplemarkdown.json</c> holds the parse tree, the
/// HTML and the plain text upstream's module produced for all 80 shipped
/// user-guide pages and for 43 hand-written corner cases the pages do not
/// reach (regenerate with
/// <c>tools/userguideprobe/gen-userguide-fixtures.py</c>). The module imports
/// nothing but <c>contextlib</c>, so it ran unshimmed — board trap 49 — and
/// nothing here is recorded from this port's own output.
/// </remarks>
public class SimpleMarkdownParityTests
{
    /// <summary>Every snippet's parse tree is upstream's, node for node.</summary>
    /// <param name="name">The snippet.</param>
    [Theory]
    [MemberData(nameof(Snippets))]
    public void snippet_parses_to_upstreams_own_tree(string name)
    {
        //Arrange
        JsonElement snippet = Fixture.GetProperty("snippets").GetProperty(name);
        string source = snippet.GetProperty("source").GetString();

        //Act
        string dump = SimpleMarkdown.Tree(source).Dump();

        //Assert
        dump.Should().Be(snippet.GetProperty("tree").GetString());
    }

    /// <summary>Every snippet's HTML is upstream's, byte for byte.</summary>
    /// <param name="name">The snippet.</param>
    [Theory]
    [MemberData(nameof(Snippets))]
    public void snippet_renders_to_upstreams_own_html(string name)
    {
        //Arrange
        JsonElement snippet = Fixture.GetProperty("snippets").GetProperty(name);
        string source = snippet.GetProperty("source").GetString();

        //Act
        string html = SimpleMarkdown.Html(source);

        //Assert
        html.Should().Be(snippet.GetProperty("html").GetString());
    }

    /// <summary>Every shipped page's parse tree is upstream's.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(Pages))]
    public void page_parses_to_upstreams_own_tree(string name)
    {
        //Arrange
        JsonElement page = Fixture.GetProperty("pages").GetProperty(name);
        string source = UserGuideFixtures.PageSource(name);

        //Act
        MarkdownTree tree = SimpleMarkdown.Tree(source);

        //Assert
        tree.Dump().Should().Be(page.GetProperty("tree").GetString());
        tree.Text().Should().Be(page.GetProperty("text").GetString());
    }

    /// <summary>Every shipped page's HTML is upstream's.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(Pages))]
    public void page_renders_to_upstreams_own_html(string name)
    {
        //Arrange
        JsonElement page = Fixture.GetProperty("pages").GetProperty(name);

        //Act
        string html = SimpleMarkdown.Html(UserGuideFixtures.PageSource(name));

        //Assert
        html.Should().Be(page.GetProperty("html").GetString());
    }

    /// <summary>Inline-only rendering matches upstream's <c>html_inline</c>.</summary>
    [Fact]
    public void inline_rendering_is_upstreams()
    {
        //Arrange
        JsonElement inline = Fixture.GetProperty("inline");
        Dictionary<string, string> sources = new Dictionary<string, string>
        {
            ["plain"] = "plain text",
            ["emphasis"] = "*emphasized*",
            ["code"] = "`code`",
            ["link"] = "[http://example.org/ text]",
            ["backtick_pair"] = "a `b` c `d` e",
            ["no_url"] = "[bare]",
        };

        //Act & Assert
        foreach (KeyValuePair<string, string> pair in sources)
        {
            SimpleMarkdown.HtmlInline(pair.Value).Should()
                .Be(inline.GetProperty(pair.Key).GetString());
        }
    }

    /// <summary>
    /// The oracle really is upstream's: it covers all 80 pages, including the
    /// 16 this port does not ship.
    /// </summary>
    [Fact]
    public void the_fixture_covers_every_upstream_page()
    {
        //Arrange & Act
        int pages = 0;
        foreach (JsonProperty _ in Fixture.GetProperty("pages").EnumerateObject())
        {
            pages++;
        }

        //Assert
        pages.Should().Be(80);
    }

    /// <summary>The snippet names.</summary>
    /// <returns>The names.</returns>
    public static TheoryData<string> Snippets()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (JsonProperty snippet in
            Fixture.GetProperty("snippets").EnumerateObject())
        {
            data.Add(snippet.Name);
        }

        return data;
    }

    /// <summary>The page names.</summary>
    /// <returns>The names.</returns>
    public static TheoryData<string> Pages()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (JsonProperty page in Fixture.GetProperty("pages").EnumerateObject())
        {
            data.Add(page.Name);
        }

        return data;
    }

    private static JsonElement Fixture => UserGuideFixtures.SimpleMarkdown;
}

/// <summary>Where the user-guide parity fixtures live, and what is in them.</summary>
internal static class UserGuideFixtures
{
    private static JsonDocument _simpleMarkdown;
    private static JsonDocument _userGuide;

    /// <summary>Gets the pure-parser fixture.</summary>
    internal static JsonElement SimpleMarkdown
        => (_simpleMarkdown ??= Load("simplemarkdown.json")).RootElement;

    /// <summary>Gets the user-guide fixture.</summary>
    internal static JsonElement UserGuide
        => (_userGuide ??= Load("userguide.json")).RootElement;

    /// <summary>Answers a fixture's path.</summary>
    /// <param name="name">The file.</param>
    /// <returns>The path.</returns>
    internal static string Path(string name)
        => System.IO.Path.Combine(
            AppContext.BaseDirectory, "fixtures", "userguide", name);

    /// <summary>
    /// Answers a page's source AS FRESCOBALDI SHIPS IT, out of the user-guide
    /// fixture — never out of this repository's own (edited) assets.
    /// </summary>
    /// <param name="name">The page.</param>
    /// <returns>The source text.</returns>
    internal static string PageSource(string name)
        => UserGuide.GetProperty("pages").GetProperty(name)
            .GetProperty("source").GetString();

    private static JsonDocument Load(string name)
        => JsonDocument.Parse(File.ReadAllText(Path(name)));
}
