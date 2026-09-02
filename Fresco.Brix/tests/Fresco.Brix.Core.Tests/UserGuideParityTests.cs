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
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The user-guide framework against Frescobaldi's OWN <c>userguide/</c>
/// package: the <c>#</c>-block split, the page format's inline rules, the
/// title/children/see-also reading, and the whole body with every
/// <c>{variable}</c> resolved.
/// </summary>
/// <remarks>
/// <para>
/// The pages parsed here are FRESCOBALDI'S, straight out of
/// <c>fixtures/userguide/userguide.json</c> — never this repository's own
/// <c>assets/userguide</c>, whose text is edited where a ruling changed what
/// the application does. What is being proved is that the FRAMEWORK reads a
/// page exactly as upstream reads it; what the shipped pages then SAY is
/// <see cref="UserGuideTests"/>'s business.
/// </para>
/// <para>
/// Four of the resolver's answers come from a RUNNING application rather than
/// from a page file — the application's own name, version and author, and the
/// shortcut currently bound to an action. In this port they are PARAMETERS
/// (<see cref="GuideContext"/>) rather than reads of a global, so the test
/// hands the library upstream's own answers and every byte of every body is
/// then compared exactly. A <c>```lilypond</c> block is the fifth: upstream
/// colorizes it against the editor's colour scheme, this port colorizes when
/// it DRAWS the block, and the probe records the plain form both keep.
/// </para>
/// </remarks>
public class UserGuideParityTests
{
    /// <summary>A page's <c>#</c>-named blocks are split as upstream splits them.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(Pages))]
    public void page_blocks_are_upstreams(string name)
    {
        //Arrange
        JsonElement expected = Page(name);

        //Act
        (string document, Dictionary<string, List<string>> blocks)
            = GuideReader.SplitDocument(
                expected.GetProperty("source").GetString());

        //Assert
        document.Should().Be(expected.GetProperty("document").GetString());
        foreach (JsonProperty block in expected.GetProperty("blocks").EnumerateObject())
        {
            blocks.Should().ContainKey(block.Name);
            List<string> lines = new List<string>();
            foreach (JsonElement line in block.Value.EnumerateArray())
            {
                lines.Add(line.GetString());
            }

            blocks[block.Name].Should().Equal(lines);
        }

        int expectedBlocks = 0;
        foreach (JsonProperty _ in expected.GetProperty("blocks").EnumerateObject())
        {
            expectedBlocks++;
        }

        blocks.Count.Should().Be(expectedBlocks);
    }

    /// <summary>A page's parse tree is upstream's, the guide's rules included.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(Pages))]
    public void page_parses_to_upstreams_own_tree(string name)
    {
        //Arrange
        GuideLibrary library = Library();

        //Act
        GuidePage page = library.Page(name);

        //Assert
        page.Tree.Dump().Should().Be(Page(name).GetProperty("tree").GetString());
    }

    /// <summary>A page's title, children and see-also list are upstream's.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(Pages))]
    public void page_structure_is_upstreams(string name)
    {
        //Arrange
        GuideLibrary library = Library();
        JsonElement expected = Page(name);

        //Act
        GuidePage page = library.Page(name);

        //Assert
        page.Title.Should().Be(expected.GetProperty("title").GetString());
        page.Children.Should().Equal(Strings(expected.GetProperty("children")));
        page.SeeAlso.Should().Equal(Strings(expected.GetProperty("seealso")));
        page.IsPopup.Should().Be(expected.GetProperty("is_popup").GetBoolean());
        library.FormatLink(name).Should()
            .Be(expected.GetProperty("link").GetString());
    }

    /// <summary>A page's whole body — variables resolved — is upstream's.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(Pages))]
    public void page_body_is_upstreams(string name)
    {
        //Arrange
        GuideLibrary library = Library();

        //Act
        string body = library.Page(name).Body();

        //Assert
        body.Should().Be(Page(name).GetProperty("body").GetString());
    }

    /// <summary>The table of contents walks the pages the way upstream walks them.</summary>
    [Fact]
    public void table_of_contents_is_upstreams()
    {
        //Arrange
        GuideLibrary library = Library();

        //Act
        string contents = library.TableOfContents();

        //Assert
        contents.Should().Be(
            UserGuideFixtures.UserGuide.GetProperty("table_of_contents").GetString());
    }

    /// <summary>
    /// The one row of the predefined menu table this port renames, declared
    /// here so a silent drift shows up as a failure. Every language name now
    /// matches too, since W-I18N ported upstream's own table.
    /// </summary>
    [Fact]
    public void the_declared_differences_from_upstream_are_these_and_no_others()
    {
        //Arrange
        JsonElement menus = UserGuideFixtures.UserGuide.GetProperty("menu_names");

        //Act & Assert — every predefined menu name is upstream's except
        //`lilypond', which names the &LilyPort menu here (ruling FR13).
        foreach (JsonProperty entry in menus.EnumerateObject())
        {
            GuideContext.DefaultMenuNames.Should().ContainKey(entry.Name);
            if (entry.Name == "lilypond")
            {
                entry.Value.GetString().Should().Be("menu title|&LilyPond");
                GuideContext.DefaultMenuNames[entry.Name].Should()
                    .Be("menu title|&LilyPort");
                continue;
            }

            GuideContext.DefaultMenuNames[entry.Name].Should()
                .Be(entry.Value.GetString());
        }

        //...and the language names, which are now upstream's OWN table
        //(W-I18N ported it as Services/LanguageNames.g.cs), so there is no
        //longer any difference at all.
        //was previously: the framework's own culture data, which read pt_BR as
        //"Portuguese (Brazil)" where upstream says "Brazilian Portuguese" —
        //the one declared difference W12B recorded, and the one W-I18N was
        //expected to close.
        JsonElement languages
            = UserGuideFixtures.UserGuide.GetProperty("language_names");
        foreach (JsonProperty entry in languages.EnumerateObject())
        {
            GuideContext.DefaultLanguageName(entry.Name)
                .Should().Be(entry.Value.GetString());
        }
    }

    /// <summary>
    /// The parent map the navigation is built from is upstream's own walk from
    /// the index page.
    /// </summary>
    [Fact]
    public void the_parent_of_every_page_is_upstreams()
    {
        //Arrange
        GuideLibrary library = Library();

        //Act & Assert — upstream computes parents by walking SUBDOCS from
        //`index'; a page nobody lists has none, and `index' itself has none.
        library.Parents("index").Should().BeEmpty();
        library.Parents("getstarted").Should().Equal(new[] { "index" });
        library.Parents("scorewiz").Should().Equal(new[] { "getstarted" });
        library.Parents("prefs_general").Should().Equal(new[] { "preferences" });
        library.Parents("404").Should().BeEmpty();
    }

    /// <summary>
    /// The oracle is upstream's, and it is still the dead syntax this wave
    /// edits out: a regenerated fixture in which Frescobaldi has renamed its
    /// own pages FAILS here rather than passing quietly.
    /// </summary>
    [Fact]
    public void the_fixture_still_records_upstreams_own_page_set()
    {
        //Arrange
        List<string> names = new List<string>();

        //Act
        foreach (JsonProperty page in
            UserGuideFixtures.UserGuide.GetProperty("pages").EnumerateObject())
        {
            names.Add(page.Name);
        }

        //Assert
        names.Count.Should().Be(80);
        names.Should().Contain("prefs_lilydoc");
        names.Should().Contain("midi_synth");
        names.Should().Contain("musicxml_import");
        names.Should().Contain("documentfonts_command");
    }

    /// <summary>The page names.</summary>
    /// <returns>The names.</returns>
    public static TheoryData<string> Pages()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (JsonProperty page in
            UserGuideFixtures.UserGuide.GetProperty("pages").EnumerateObject())
        {
            data.Add(page.Name);
        }

        return data;
    }

    private static JsonElement Page(string name)
        => UserGuideFixtures.UserGuide.GetProperty("pages").GetProperty(name);

    private static IReadOnlyList<string> Strings(JsonElement array)
    {
        List<string> values = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            values.Add(item.GetString());
        }

        return values;
    }

    /// <summary>
    /// A library over UPSTREAM's own page text, with a deterministic context so
    /// nothing in the comparison depends on a running window.
    /// </summary>
    /// <returns>The library.</returns>
    private static GuideLibrary Library()
    {
        JsonElement functions
            = UserGuideFixtures.UserGuide.GetProperty("resolve_functions");
        string sentinel = UserGuideFixtures.UserGuide
            .GetProperty("shortcut_sentinel").GetString();

        GuideLibrary library = new GuideLibrary(new FixturePageStore());

        //The four answers that come from a RUNNING application rather than from
        //a page file are PARAMETERS of this port (GuideContext), which is what
        //lets the comparison be exact: the context is handed upstream's own
        //answers, and every other byte of the body is then upstream's too.
        library.Context.AppName = functions.GetProperty("appname").GetString();
        library.Context.Version = functions.GetProperty("version").GetString();
        library.Context.Author = functions.GetProperty("author").GetString();
        library.Context.Shortcut = (collection, action)
            => sentinel.Replace("{0}", collection).Replace("{1}", action);

        //Two more the same way: upstream's own predefined menu names (ruling
        //FR13 renames exactly one row of them here) and upstream's own language
        //names (its `language_names' data package arrives with W-I18N).
        Dictionary<string, string> menus
            = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty entry in
            UserGuideFixtures.UserGuide.GetProperty("menu_names").EnumerateObject())
        {
            menus[entry.Name] = entry.Value.GetString();
        }

        library.Context.MenuNames = menus;

        JsonElement languages = UserGuideFixtures.UserGuide.GetProperty("language_names");
        library.Context.LanguageName = code
            => languages.TryGetProperty(code, out JsonElement name)
                ? name.GetString()
                : code;

        return library;
    }

    /// <summary>A page store over the fixture, so the parity test reads what
    /// Frescobaldi shipped rather than what this repository ships.</summary>
    private sealed class FixturePageStore : IGuidePageStore
    {
        public string Read(string name)
        {
            JsonElement pages = UserGuideFixtures.UserGuide.GetProperty("pages");
            return pages.TryGetProperty(name, out JsonElement page)
                ? page.GetProperty("source").GetString()
                : null;
        }

        public IReadOnlyList<string> Names()
        {
            List<string> names = new List<string>();
            foreach (JsonProperty page in
                UserGuideFixtures.UserGuide.GetProperty("pages").EnumerateObject())
            {
                names.Add(page.Name);
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        public string PathOf(string fileName) => fileName;
    }
}
