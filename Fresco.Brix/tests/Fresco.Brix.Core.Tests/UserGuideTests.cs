// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Shell;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using Fresco.Brix.UserGuide;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The user guide THIS APPLICATION SHIPS: that every page parses, that every
/// link in it goes somewhere, that every variable in it resolves, and that the
/// pages a ruling changed say what the application now does.
/// </summary>
/// <remarks>
/// <see cref="UserGuideParityTests"/> proves the FRAMEWORK against
/// Frescobaldi's own code, over Frescobaldi's own pages. This proves the
/// CORPUS: 69 of upstream's 80 pages, edited, plus one page that is this
/// application's own.
/// //was previously: 68, while <c>manuscriptview</c> was dropped with the tool
/// it documents (W13 / audit B N4). Jeremy ruled the Manuscript Viewer into v1
/// on 2026-09-02 (ruling FR17, board wave W15), so the page is back — edited:
/// the printing sentence is gone (FR5.5) and it says what this application's
/// own PDF export does and does not write.
/// //was previously that: 69 of upstream's 80, before the page was dropped.
/// </remarks>
public class UserGuideTests
{
    /// <summary>
    /// The pages upstream ships that this application does not, each with the
    /// reason.
    /// </summary>
    /// <remarks>
    /// They are not shipped and nothing links to them. All eleven are killed by
    /// a ruling.
    /// //was previously: twelve, the twelfth being <c>manuscriptview</c>, killed
    /// by the tool it documents not existing here. The tool exists now (ruling
    /// FR17, board wave W15) and the page ships with it.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> DeadPages
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["extending"] = "FR5.3 — no extensions system",
            ["ext_configuration"] = "FR5.3",
            ["ext_installation"] = "FR5.3",
            ["ext_usage"] = "FR5.3",
            ["prefs_extensions"] = "FR5.3",
            ["snippet_python"] = "FR5.3 — no user scripting",
            ["git"] = "FR5.7 — no version-control tracking",
            ["prefs_lilypond"] = "FR5.1 — one engine, compiled in",
            ["prefs_lilypond_autoversion"] = "FR5.1",
            ["prefs_lilydoc"] = "FR5.1 + FR8 — the manuals are bundled assets",
            ["midi_synth"] = "FR6 — synthesis is in-process, no MIDI ports",
            //was previously: ["manuscriptview"] = "W13 — no Manuscript Viewer
            //panel; post-v1". The panel is v1 (ruling FR17, board wave W15) and
            //the page ships again, with the printing sentence taken out under
            //FR5.5 — which is what put it on this list in the first place.
        };

    /// <summary>
    /// The pages File &gt; Import brought with it (board row W-IMPORT).
    /// </summary>
    /// <remarks>
    /// //was previously: these five were HELD, not shipped, and the links to
    /// them fell to <c>404.md</c> — which is what
    /// <c>an_import_page_falls_to_404_and_names_itself</c> asserted until this
    /// wave. All five are edited: there are no external converters to install
    /// and no version to choose (rulings FD1 and FR5.1), and the command-line
    /// text box the pages describe has not existed in Frescobaldi's own dialog
    /// for years.
    /// </remarks>
    private static readonly IReadOnlyList<string> ImportPages = new[]
    {
        "import", "import_all", "abc_import", "midi_import", "musicxml_import",
    };

    /// <summary>
    /// A resource name no page in the corpus has, so the 404 page can still be
    /// proved to name what was asked for.
    /// </summary>
    private const string MissingPage = "no_such_page";

    /// <summary>The page this application writes that upstream has no page for.</summary>
    /// <remarks>Upstream's is <c>prefs_lilydoc</c>, which FR5.1 and FR8 kill;
    /// the Documentation preferences page is Fresco.Brix's own and records
    /// <c>prefs_documentation</c> as its help identifier (W12A).</remarks>
    private const string OriginalPage = "prefs_documentation";

    /// <summary>Every page reachable from the index parses and has a title.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(ShippedPages))]
    public void every_shipped_page_parses_and_has_a_title(string name)
    {
        //Arrange
        GuideLibrary library = Library();

        //Act
        GuidePage page = library.Page(name);

        //Assert
        page.IsMissing.Should().BeFalse();
        page.Title.Should().NotBe("No Title");
        page.Body().Should().NotBeEmpty();
    }

    /// <summary>
    /// Every link in every shipped page reaches a shipped page.
    /// </summary>
    /// <remarks>//was previously: a link to one of the five import pages was
    /// required NOT to resolve, because they were held back for W-IMPORT.</remarks>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(ShippedPages))]
    public void every_link_resolves(string name)
    {
        //Arrange
        GuideLibrary library = Library();
        GuidePage page = library.Page(name);
        List<string> targets = new List<string>();
        targets.AddRange(page.Children);
        targets.AddRange(page.SeeAlso);
        foreach (string variable in page.Variables)
        {
            string[] parts = variable.Split(
                (char[])null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3
                && string.Equals(parts[1], "help", StringComparison.OrdinalIgnoreCase))
            {
                targets.Add(parts[2]);
            }
        }

        //...and the inline [pagename text] links, which are page names too.
        foreach (MarkdownTree.Node link in page.Tree.Find("link"))
        {
            string url = link.Arguments.Count > 0
                ? link.Arguments[0] as string
                : string.Empty;
            if (url.Length > 0
                && !url.Contains("://", StringComparison.Ordinal)
                && !url.Contains('@', StringComparison.Ordinal))
            {
                targets.Add(url);
            }
        }

        //Act & Assert
        foreach (string target in targets)
        {
            library.Exists(target).Should()
                .BeTrue($"page '{name}' links to '{target}'");
        }
    }

    /// <summary>Every <c>{variable}</c> the reader can see resolves.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(ShippedPages))]
    public void every_variable_substitutes(string name)
    {
        //Arrange
        GuideLibrary library = Library();
        GuidePage page = library.Page(name);
        GuideResolver resolver = page.Resolver();

        //Act & Assert — only the text a reader sees: a `{` inside a code block
        //is LilyPond, not a variable, and is never substituted.
        foreach (MarkdownTree.Node text in page.Tree.Find("inline_text"))
        {
            string content = text.Arguments.Count > 0
                ? text.Arguments[0] as string
                : string.Empty;
            foreach (System.Text.RegularExpressions.Match match
                in GuideReader.VariablePattern.Matches(content))
            {
                GuideValue value = resolver.Resolve(match.Groups[1].Value);
                value.Should().NotBeNull(
                    $"page '{name}' uses {match.Value}");
                value.Html.Should().NotBeNull();
            }
        }
    }

    /// <summary>Every declared variable is used by the page that declares it.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(ShippedPages))]
    public void every_declared_variable_is_used(string name)
    {
        //Arrange
        GuideLibrary library = Library();
        GuidePage page = library.Page(name);
        HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkdownTree.Node text in page.Tree.Find("inline_text"))
        {
            string content = text.Arguments.Count > 0
                ? text.Arguments[0] as string
                : string.Empty;
            foreach (System.Text.RegularExpressions.Match match
                in GuideReader.VariablePattern.Matches(content))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        //Act & Assert
        foreach (string variable in page.Variables)
        {
            string[] parts = variable.Split(
                (char[])null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) { continue; }

            //`userguide_page' is appended to every page by the framework and is
            //used only by 404.
            if (parts[0] == "userguide_page") { continue; }

            used.Should().Contain(parts[0],
                $"page '{name}' declares {parts[0]} and never uses it");
        }
    }

    /// <summary>
    /// Every <c>shortcut</c> variable names an action this application has.
    /// </summary>
    [Fact]
    public void every_shortcut_variable_names_a_real_action()
    {
        //Arrange
        GuideLibrary library = Library();
        ActionCollectionManager manager = Actions();
        List<string> missing = new List<string>();

        //Act
        foreach (string name in library.Names())
        {
            foreach (string variable in library.Page(name).Variables)
            {
                string[] parts = variable.Split(
                    (char[])null, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3
                    || !string.Equals(parts[1], "shortcut", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] target = parts[2].Split(
                    (char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (target.Length < 2
                    || manager.Action(target[0], target[1]) == null)
                {
                    missing.Add($"{name}: {parts[2]}");
                }
            }
        }

        //Assert
        missing.Should().BeEmpty();
    }

    /// <summary>Every <c>image</c> variable names a file that ships.</summary>
    [Fact]
    public void every_image_variable_names_a_shipped_file()
    {
        //Arrange
        GuideLibrary library = Library();
        List<string> missing = new List<string>();

        //Act
        foreach (string name in library.Names())
        {
            foreach (string variable in library.Page(name).Variables)
            {
                string[] parts = variable.Split(
                    (char[])null, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3
                    && string.Equals(parts[1], "image", StringComparison.Ordinal)
                    && !File.Exists(library.Store.PathOf(parts[2])))
                {
                    missing.Add($"{name}: {parts[2]}");
                }
            }
        }

        //Assert
        missing.Should().BeEmpty();
    }

    /// <summary>
    /// The help identifier every dialog records reaches a page that exists.
    /// </summary>
    /// <param name="identifier">The recorded identifier.</param>
    /// <remarks>W12A and this wave's first tranche recorded these on the
    /// preferences pages and the dialogs; this is the assertion that the
    /// wiring goes somewhere.</remarks>
    [Theory]
    [MemberData(nameof(HelpIdentifiers))]
    public void every_recorded_help_identifier_reaches_a_page(string identifier)
    {
        //Arrange
        GuideLibrary library = Library();

        //Act
        bool exists = library.Exists(identifier);

        //Assert
        exists.Should().BeTrue($"'{identifier}' is a recorded help identifier");
        library.Page(identifier).IsMissing.Should().BeFalse();
    }

    /// <summary>
    /// A link to a page that is not there lands on 404, which names what was
    /// asked for.
    /// </summary>
    /// <remarks>//was previously: driven over the five import pages, which
    /// were held back for W-IMPORT and are now shipped; the mechanism is the
    /// same and is still worth proving, so it is driven over a name the corpus
    /// will never have.</remarks>
    [Fact]
    public void a_missing_page_falls_to_404_and_names_itself()
    {
        //Arrange
        GuideLibrary library = Library();

        //Act
        GuidePage page = library.Page(MissingPage);

        //Assert
        page.IsMissing.Should().BeTrue();
        page.Title.Should().Be("Not Found");
        page.Body().Should().Contain(MissingPage);
    }

    /// <summary>The five import pages ship, and every one of them parses.</summary>
    /// <param name="name">The page.</param>
    [Theory]
    [MemberData(nameof(ShippedImportPages))]
    public void an_import_page_ships(string name)
    {
        //Arrange
        GuideLibrary library = Library();

        //Act
        GuidePage page = library.Page(name);

        //Assert
        page.IsMissing.Should().BeFalse();
        page.Title.Should().NotBe("No Title");
        page.Body().Should().NotBeEmpty();
    }

    /// <summary>
    /// The import pages describe THIS application: no converter to install, no
    /// engine version to choose, and no command line to edit.
    /// </summary>
    /// <param name="name">The page.</param>
    /// <remarks>The three sentences taken out are upstream's "the command line
    /// tool `abc2ly' from the LilyPond package", "You can also change the
    /// LilyPond version to use." and the paragraph about "a text area that
    /// mimics the command line text" — the last of which upstream's own dialog
    /// has not had for years.</remarks>
    [Theory]
    [MemberData(nameof(ShippedImportPages))]
    public void an_import_page_describes_this_application(string name)
    {
        //Arrange
        string text = File.ReadAllText(Library().Store.PathOf(name + ".md"));

        //Act & Assert
        text.Should().NotContain("LilyPond package");
        text.Should().NotContain("LilyPond version to use");
        text.Should().NotContain("command line");
        text.Should().NotContain("Frescobaldi");
    }

    /// <summary>The corpus is the 68 survivors plus this port's own page.</summary>
    [Fact]
    public void the_shipped_page_set_is_the_ruled_one()
    {
        //Arrange
        GuideLibrary library = Library();

        //Act
        IReadOnlyList<string> names = library.Names();

        //Assert — 80 upstream pages, 11 dropped, 1 written here: 69 + 1.
        //was previously: 69, as 68 + 1, while `manuscriptview' was dropped with
        //the tool it documents. Jeremy ruled the tool into v1 on 2026-09-02
        //(FR17), so the page is back.
        //was previously that: 70, as 69 + 1, before the page was dropped.
        //was previously that: 65, with the five import pages held back.
        names.Count.Should().Be(70);
        foreach (string dead in DeadPages.Keys)
        {
            names.Should().NotContain(dead);
        }

        foreach (string page in ImportPages) { names.Should().Contain(page); }

        names.Should().Contain(OriginalPage);
        names.Should().Contain("404");
    }

    /// <summary>
    /// The five Document Fonts pages say what the dialog WRITES, not what
    /// Frescobaldi's dialog wrote.
    /// </summary>
    /// <remarks>The composer's FR14 divergence (W12B tranche 1, §3.1): the
    /// dead <c>set-global-fonts</c> is gone, and so is the short
    /// <c>fonts.serif</c> form, which board trap 67 says is a silent
    /// no-op.</remarks>
    [Fact]
    public void the_document_font_pages_document_the_long_form()
    {
        //Arrange
        GuideLibrary library = Library();
        string[] pages =
        {
            "documentfonts", "documentfonts_text", "documentfonts_music",
            "documentfonts_command", "documentfonts_preview",
        };

        //Act & Assert
        foreach (string name in pages)
        {
            string body = library.Page(name).Body();
            body.Should().NotContain("set-global-fonts");
            body.Should().NotContain("#:brace");
            body.Should().NotContain("#:factor");
            body.Should().NotContain("#:roman");
        }

        string command = library.Page("documentfonts_command").Body();
        command.Should().Contain("property-defaults.fonts.music");
        command.Should().Contain("property-defaults.fonts.serif");
        command.Should().Contain("property-defaults.fonts.sans");
        command.Should().Contain("property-defaults.fonts.typewriter");

        //...and never the short form, anywhere in the corpus.
        foreach (string name in library.Names())
        {
            string body = library.Page(name).Body();
            int index = body.IndexOf("fonts.serif", StringComparison.Ordinal);
            while (index >= 0)
            {
                body.Substring(0, index).Should()
                    .EndWith("property-defaults.", $"in page '{name}'");
                index = body.IndexOf(
                    "fonts.serif", index + 1, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Nothing in the guide names an engine the user can install or choose
    /// (FR5.1), and nothing names a page a ruling killed.
    /// </summary>
    [Fact]
    public void no_page_points_at_a_feature_a_ruling_removed()
    {
        //Arrange
        GuideLibrary library = Library();
        List<string> offences = new List<string>();

        //Act
        foreach (string name in library.Names())
        {
            string text = File.ReadAllText(library.Store.PathOf(name + ".md"));
            foreach (string dead in DeadPages.Keys)
            {
                //A WORD, not a substring: `git' is a dead page and `github.com'
                //is a URL in the contributing page.
                if (System.Text.RegularExpressions.Regex.IsMatch(
                    text, @"\b" + dead + @"\b"))
                {
                    offences.Add($"{name} names the dead page '{dead}'");
                }
            }
        }

        //Assert
        offences.Should().BeEmpty();
    }

    /// <summary>The guide is English only (ruling FR5.6).</summary>
    [Fact]
    public void the_guide_is_english_only()
    {
        //Arrange
        GuideLibrary library = Library();
        GuideParser parser = new GuideParser();

        //Act — the translation seam is ported and is the identity here, so a
        //page's text reaches the reader exactly as it is written.
        MarkdownTree tree = new MarkdownTree();
        parser.Parse("A sentence with {appname} in it.", tree);

        //Assert
        tree.Text().Should().Be("A sentence with {appname} in it.");
    }

    /// <summary>The shipped page names.</summary>
    /// <returns>The names.</returns>
    public static TheoryData<string> ShippedPages()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (string name in Library().Names()) { data.Add(name); }

        return data;
    }

    /// <summary>The five pages File &gt; Import brought with it.</summary>
    /// <returns>The names.</returns>
    public static TheoryData<string> ShippedImportPages()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (string name in ImportPages) { data.Add(name); }

        return data;
    }

    /// <summary>
    /// Every help identifier the application records, from W12A's preferences
    /// pages and dialogs and from this wave's Document Fonts dialog.
    /// </summary>
    /// <returns>The identifiers.</returns>
    public static TheoryData<string> HelpIdentifiers()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (string identifier in RecordedHelpIdentifiers()) { data.Add(identifier); }

        return data;
    }

    /// <summary>
    /// The help identifiers, read off the objects that record them rather than
    /// written out here, so a page renamed in code fails this test.
    /// </summary>
    /// <returns>The identifiers.</returns>
    internal static IReadOnlyList<string> RecordedHelpIdentifiers()
    {
        List<string> identifiers = new List<string>
        {
            //The dialogs, each from the constant it records.
            DocumentFontsDialog.HelpIdentifier,
            //externalchanges — ChangedDocumentsDialog's button (W12A tranche 3).
            "externalchanges",
            //outline_configure — the Tools page's pattern editor.
            "outline_configure",
            //The guide's own entry points.
            GuideLibrary.IndexPage,
            GuideLibrary.ContentsPage,
            "credits",
        };

        foreach (Preferences.PreferencesPage page in PreferencePages())
        {
            identifiers.Add(page.Help);
        }

        return identifiers;
    }

    /// <summary>The preferences pages, in the dialog's own order.</summary>
    /// <returns>The pages.</returns>
    private static IReadOnlyList<Preferences.PreferencesPage> PreferencePages()
    {
        Preferences.PreferencesContext context = new Preferences.PreferencesContext();
        return new Preferences.PreferencesDialog(context).Pages;
    }

    private static ActionCollectionManager Actions()
    {
        ActionCollectionManager manager = new ActionCollectionManager();
        manager.Add(new MainActions());
        manager.Add(new ViewActions());
        manager.Add(new EngraveActions());
        manager.Add(new LogActions());
        manager.Add(new MusicViewActions());
        manager.Add(new ScoreWizardActions());
        manager.Add(new LyricsActions());
        manager.Add(new SnippetToolActions());
        manager.Add(new SnippetShortcuts(new SnippetLibrary()));
        manager.Add(new DocumentationActions());
        return manager;
    }

    private static GuideLibrary Library()
        => new GuideLibrary(
            Path.Combine(AppContext.BaseDirectory, "assets", "userguide"));
}
