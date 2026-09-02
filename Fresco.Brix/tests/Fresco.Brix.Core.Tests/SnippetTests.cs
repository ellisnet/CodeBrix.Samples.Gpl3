// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.QuickInsert;
using Fresco.Brix.Services;
using Fresco.Brix.Sessions;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>A settings store in a throw-away folder, for the tests.</summary>
public static class TestSettings
{
    /// <summary>Makes a store nothing else can see.</summary>
    /// <returns>The store.</returns>
    /// <remarks>//was previously: a throw-away FILE — the settings add-in the
    /// store is now a facade over locates the file inside a folder it owns, and
    /// keeps its own backups there.</remarks>
    public static SettingsStore Create()
        => new SettingsStore(Path.Combine(
            Path.GetTempPath(),
            "frescobrix-tests",
            Path.GetRandomFileName()));
}

/// <summary>Reading a snippet: its variables, its title, its expansions.</summary>
public class SnippetParserTests
{
    [Fact]
    public void the_variable_lines_are_not_part_of_the_text()
    {
        //Arrange, Act
        SnippetText snippet = SnippetParser.Parse(
            "-*- menu: blocks; indent: no;\n\\score {\n  $CURSOR\n}\n");

        //Assert
        snippet.Text.Should().StartWith("\\score {");
        snippet.Variable("menu").Should().Be("blocks");
        snippet.Variable("indent").Should().Be("no");
    }

    [Fact]
    public void a_variable_with_no_value_means_yes()
    {
        //Arrange, Act
        SnippetText snippet = SnippetParser.Parse("-*- template; menu;\ntext\n");

        //Assert
        snippet.Variable("template").Should().Be("yes");
        snippet.VariableHas("template", "yes").Should().BeTrue();
    }

    [Fact]
    public void the_expansions_come_out_in_order()
    {
        //Arrange, Act
        List<SnippetPart> parts = SnippetParser
            .Expand("a $CURSOR b $$ c ${note} d").ToList();

        //Assert
        parts.Select(p => p.Expansion)
            .Should().Equal(new[] { "CURSOR", "$", "note", string.Empty });
        parts[0].Text.Should().Be("a ");
        parts[3].Text.Should().Be(" d");
    }

    [Fact]
    public void a_braced_expansion_can_hold_an_escaped_brace()
    {
        //Arrange, Act
        List<SnippetPart> parts = SnippetParser.Expand(@"${a\}b}").ToList();

        //Assert
        parts[0].Expansion.Should().Be("a}b");
    }

    [Fact]
    public void a_title_is_the_first_and_last_line_with_the_expansions_elided()
    {
        //Arrange, Act
        string title = SnippetParser.MakeTitle("\n\\score {\n  $CURSOR\n}\n\n");

        //Assert
        title.Should().Be("\\score { ... }");
    }
}

/// <summary>The snippets the application ships and the ones the user writes.</summary>
public class SnippetLibraryTests
{
    private static SnippetLibrary Make(out SettingsStore settings)
    {
        settings = TestSettings.Create();
        return new SnippetLibrary(settings);
    }

    [Fact]
    public void the_built_in_snippets_are_there()
    {
        //Arrange
        SnippetLibrary library = Make(out _);

        //Act, Assert
        library.Names().Should().Contain("blankline");
        library.Names().Count.Should().BeGreaterThan(20);
    }

    [Fact]
    public void no_shipped_snippet_runs_python()
    {
        //Arrange, Act, Assert
        //FR5.3 excludes snippet Python code, so no shipped snippet may declare
        //the variable that would ask for it.
        BuiltinSnippets.All.Should().NotContain(
            s => SnippetParser.Parse(s.Text).Variable("python").Length > 0);
    }

    [Fact]
    public void a_user_snippet_wins_over_the_built_in_of_the_same_name()
    {
        //Arrange
        SnippetLibrary library = Make(out _);

        //Act
        library.Save("blankline", "changed\n", "My Blank Line");

        //Assert
        library.Text("blankline").Should().Be("changed\n");
        library.Title("blankline").Should().Be("My Blank Line");
        library.IsOriginal("blankline").Should().BeFalse();
    }

    [Fact]
    public void an_edit_that_matches_the_built_in_is_forgotten_again()
    {
        //Arrange
        SnippetLibrary library = Make(out _);
        string original = library.Text("blankline");

        //Act
        library.Save("blankline", "changed", "Changed");
        library.Save("blankline", original, null);

        //Assert
        library.IsOriginal("blankline").Should().BeTrue();
    }

    [Fact]
    public void a_deleted_built_in_stays_deleted()
    {
        //Arrange
        SnippetLibrary library = Make(out _);

        //Act
        library.Delete("blankline");

        //Assert
        library.Names().Should().NotContain("blankline");
    }

    [Fact]
    public void a_new_snippet_gets_a_name_nothing_else_uses()
    {
        //Arrange
        SnippetLibrary library = Make(out _);

        //Act
        string first = library.Save(null, "one", "One");
        string second = library.Save(null, "two", "Two");

        //Assert
        first.Should().NotBe(second);
        library.Text(first).Should().Be("one");
        library.Text(second).Should().Be("two");
    }

    [Fact]
    public void the_snippets_survive_a_round_trip_through_a_file()
    {
        //Arrange
        SnippetLibrary library = Make(out _);
        string name = library.Save(null, "-*- menu;\n\\bold $CURSOR", "Bold");
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".xml");

        try
        {
            //Act
            SnippetImportExport.Save(library, new[] { name }, path);
            IReadOnlyList<PortableSnippet> read = SnippetImportExport.Load(path);

            //Assert
            read.Should().HaveCount(1);
            read[0].Title.Should().Be("Bold");
            read[0].Body.Should().Contain("\\bold $CURSOR");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>Putting a snippet into a document.</summary>
public class SnippetInserterTests
{
    private static SnippetLibrary Library() => new SnippetLibrary(TestSettings.Create());

    [Fact]
    public void the_cursor_marker_says_where_the_caret_lands()
    {
        //Arrange
        SnippetLibrary library = Library();
        string name = library.Save(null, "\\bold { $CURSOR }", "Bold");
        EditorDocument document = ToolDocument.Open("start\n");

        //Act
        SnippetInsertion result = SnippetInserter.Insert(library, name, document, 0, 0);

        //Assert
        document.Text.Should().StartWith("\\bold {  }");
        result.SelectionStart.Should().Be("\\bold { ".Length);
        result.SelectionEnd.Should().Be(result.SelectionStart);
    }

    [Fact]
    public void the_selection_marker_puts_the_selected_text_back()
    {
        //Arrange
        SnippetLibrary library = Library();
        string name = library.Save(null, "\\bold { $SELECTION }", "Bold");
        EditorDocument document = ToolDocument.Open("hello world\n");

        //Act
        SnippetInserter.Insert(library, name, document, 0, 5);

        //Assert
        document.Text.Should().Be("\\bold { hello } world\n");
    }

    [Fact]
    public void a_snippet_that_needs_a_selection_does_nothing_without_one()
    {
        //Arrange
        SnippetLibrary library = Library();
        string name = library.Save(null, "-*- selection: yes;\n[$SELECTION]", "Wrap");
        EditorDocument document = ToolDocument.Open("text\n");

        //Act
        SnippetInsertion result = SnippetInserter.Insert(library, name, document, 0, 0);

        //Assert
        result.Inserted.Should().BeFalse();
        document.Text.Should().Be("text\n");
    }

    [Fact]
    public void a_dollar_dollar_becomes_one_dollar()
    {
        //Arrange
        SnippetLibrary library = Library();
        string name = library.Save(null, "cost: $$5", "Cost");
        EditorDocument document = ToolDocument.Open(string.Empty);

        //Act
        SnippetInserter.Insert(library, name, document, 0, 0);

        //Assert
        document.Text.Should().Be("cost: $5");
    }

    [Fact]
    public void the_engine_version_expands()
    {
        //Arrange
        SnippetLibrary library = Library();
        string name = library.Save(null, "\\version \"$LILYPOND_VERSION\"\n", "Version");
        EditorDocument document = ToolDocument.Open(string.Empty);

        //Act
        SnippetInserter.Insert(library, name, document, 0, 0);

        //Assert
        document.Text.Should().Be(
            "\\version \"" + Fresco.Brix.Engrave.LilyPortEngine.CompatibleWithVersion
                + "\"\n");
    }

    [Fact]
    public void an_anchor_and_a_cursor_together_leave_a_selection()
    {
        //Arrange
        SnippetLibrary library = Library();
        string name = library.Save(null, "a${ANCHOR}bc${CURSOR}d", "Range");
        EditorDocument document = ToolDocument.Open(string.Empty);

        //Act
        SnippetInsertion result = SnippetInserter.Insert(library, name, document, 0, 0);

        //Assert
        document.Text.Should().Be("abcd");
        result.SelectionStart.Should().Be(1);
        result.SelectionEnd.Should().Be(3);
    }
}

/// <summary>Filtering the snippet list.</summary>
public class SnippetFilterTests
{
    [Fact]
    public void a_colon_filters_on_a_declared_variable()
    {
        //Arrange
        SnippetLibrary library = new SnippetLibrary(TestSettings.Create());
        string withMenu = library.Save(null, "-*- menu: blocks;\na", "A");
        string without = library.Save(null, "b", "B");

        //Act
        SnippetFilterResult result = SnippetFilter.Apply(
            library, new[] { withMenu, without }, ":menu");

        //Assert
        result.Names.Should().Equal(new[] { withMenu });
    }

    [Fact]
    public void a_colon_and_a_value_filters_on_the_value()
    {
        //Arrange
        SnippetLibrary library = new SnippetLibrary(TestSettings.Create());
        string blocks = library.Save(null, "-*- menu: blocks;\na", "A");
        string other = library.Save(null, "-*- menu: other;\nb", "B");

        //Act
        SnippetFilterResult result = SnippetFilter.Apply(
            library, new[] { blocks, other }, ":menu blocks");

        //Assert
        result.Names.Should().Equal(new[] { blocks });
    }

    [Fact]
    public void plain_text_matches_the_title()
    {
        //Arrange
        SnippetLibrary library = new SnippetLibrary(TestSettings.Create());
        string bold = library.Save(null, "a", "Bold text");
        string italic = library.Save(null, "b", "Italic text");

        //Act
        SnippetFilterResult result = SnippetFilter.Apply(
            library, new[] { bold, italic }, "bold");

        //Assert
        result.Names.Should().Equal(new[] { bold });
    }

    [Fact]
    public void the_name_variable_matched_exactly_selects_the_snippet()
    {
        //Arrange
        SnippetLibrary library = new SnippetLibrary(TestSettings.Create());
        string named = library.Save(null, "-*- name: bld;\na", "Bold");

        //Act
        SnippetFilterResult result = SnippetFilter.Apply(
            library, new[] { named }, "bld");

        //Assert
        result.ExactMatch.Should().Be(named);
    }

    [Fact]
    public void the_menu_groups_come_out_in_order()
    {
        //Arrange
        SnippetLibrary library = new SnippetLibrary(TestSettings.Create());
        library.Save("zz", "-*- menu: zeta;\na", "A");
        library.Save("aa", "-*- menu: alpha;\nb", "B");

        //Act
        var groups = SnippetFilter.Grouped(library, "menu");

        //Assert
        var names = groups.Select(g => g.Group).ToList();
        names.Should().Contain("alpha");
        names.Should().Contain("zeta");
        names.IndexOf("alpha").Should().BeLessThan(names.IndexOf("zeta"));

        //A `menu' declared with no value sorts FIRST, ahead of every named
        //group — upstream's rule, and the built-in snippets use it.
        names.First().Should().Be("yes");
    }
}

/// <summary>Making a template out of the document the user is in.</summary>
public class SnippetTemplateTests
{
    [Fact]
    public void the_caret_becomes_a_cursor_marker()
    {
        //Arrange, Act
        string template = SnippetTemplate.FromDocument("abcdef", 3, 3);

        //Assert
        template.Should().StartWith(SnippetTemplate.HeaderLine);
        template.Should().Contain("abc${CURSOR}def");
    }

    [Fact]
    public void a_selection_becomes_an_anchor_and_a_cursor()
    {
        //Arrange, Act
        string template = SnippetTemplate.FromDocument("abcdef", 2, 4);

        //Assert
        template.Should().Contain("ab${ANCHOR}cd${CURSOR}ef");
    }

    [Fact]
    public void a_dollar_in_the_document_is_doubled()
    {
        //Arrange, Act
        string template = SnippetTemplate.FromDocument("cost $5", 0, 0);

        //Assert
        template.Should().Contain("cost $$5");
    }

    [Fact]
    public void the_run_marker_is_added_on_request()
    {
        //Arrange, Act
        string template = SnippetTemplate.FromDocument("x", 0, 0, engraveOnUse: true);

        //Assert
        template.Should().StartWith(
            SnippetTemplate.HeaderLine + SnippetTemplate.RunMarker);
    }
}

/// <summary>What a Quick Insert button writes.</summary>
public class QuickInsertTests
{
    [Fact]
    public void an_articulation_lands_after_the_note()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("\\relative c' { c4 d e f }\n");
        int caret = document.Text.IndexOf("c4", StringComparison.Ordinal);

        //Act
        QuickInsertActions.Insert(
            document, "articulation_staccato", caret, caret,
            InsertDirection.Neutral, allowShorthands: true);

        //Assert
        document.Text.Should().Be("\\relative c' { c4-. d e f }\n");
    }

    [Fact]
    public void the_direction_picker_decides_the_operator()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("\\relative c' { c4 d }\n");
        int caret = document.Text.IndexOf("c4", StringComparison.Ordinal);

        //Act
        QuickInsertActions.Insert(
            document, "articulation_fermata", caret, caret,
            InsertDirection.Up, allowShorthands: true);

        //Assert
        document.Text.Should().Be("\\relative c' { c4^\\fermata d }\n");
    }

    [Fact]
    public void every_note_in_a_selection_gets_the_articulation()
    {
        //Arrange
        string text = "\\relative c' { c4 d e f }\n";
        EditorDocument document = ToolDocument.Open(text);
        int start = text.IndexOf("c4", StringComparison.Ordinal);
        int end = text.IndexOf(" }", StringComparison.Ordinal);

        //Act
        QuickInsertActions.Insert(
            document, "articulation_tenuto", start, end,
            InsertDirection.Neutral, allowShorthands: false);

        //Assert
        document.Text.Should().Be(
            "\\relative c' { c4\\tenuto d\\tenuto e\\tenuto f\\tenuto }\n");
    }

    [Fact]
    public void a_slur_wraps_the_selection()
    {
        //Arrange
        string text = "\\relative c' { c4 d e f }\n";
        EditorDocument document = ToolDocument.Open(text);
        int start = text.IndexOf("c4", StringComparison.Ordinal);
        int end = text.IndexOf(" }", StringComparison.Ordinal);

        //Act
        QuickInsertActions.Insert(
            document, "spanner_slur", start, end,
            InsertDirection.Neutral, allowShorthands: true);

        //Assert
        document.Text.Should().Be("\\relative c' { c4( d e f) }\n");
    }

    [Fact]
    public void a_dynamic_lands_after_the_first_note()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("\\relative c' { c4 d }\n");
        int caret = document.Text.IndexOf("c4", StringComparison.Ordinal);

        //Act
        QuickInsertActions.Insert(
            document, "dynamic_f", caret, caret,
            InsertDirection.Neutral, allowShorthands: true);

        //Assert
        document.Text.Should().Be("\\relative c' { c4\\f d }\n");
    }

    [Fact]
    public void a_hairpin_over_a_selection_is_terminated()
    {
        //Arrange
        string text = "\\relative c' { c4 d e f }\n";
        EditorDocument document = ToolDocument.Open(text);
        int start = text.IndexOf("c4", StringComparison.Ordinal);
        int end = text.IndexOf(" }", StringComparison.Ordinal);

        //Act
        QuickInsertActions.Insert(
            document, "dynamic_hairpin_cresc", start, end,
            InsertDirection.Neutral, allowShorthands: true);

        //Assert
        document.Text.Should().Be("\\relative c' { c4\\< d e f\\! }\n");
    }

    [Fact]
    public void a_bar_line_is_written_for_the_documents_own_version()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\version \"2.24.0\"\n\\relative c' { c4 d }\n");
        int caret = document.Text.Length - 1;

        //Act
        QuickInsertActions.Insert(
            document, "bar_double", caret, caret,
            InsertDirection.Neutral, allowShorthands: true);

        //Assert
        document.Text.Should().Contain("\\bar \"||\"");
    }

    [Fact]
    public void the_shorthand_switch_decides_the_form()
    {
        //Arrange, Act, Assert
        QuickInsertLogic.ArticulationText("staccato", InsertDirection.Neutral, true)
            .Should().Be("-.");
        QuickInsertLogic.ArticulationText("staccato", InsertDirection.Neutral, false)
            .Should().Be("\\staccato");
        QuickInsertLogic.ArticulationText("staccato", InsertDirection.Up, true)
            .Should().Be("^.");
    }

    [Fact]
    public void every_button_the_panel_offers_has_an_engraved_icon()
    {
        //Arrange
        var names = QuickInsertPanel.Tools()
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Buttons)
            .Select(b => b.Name)
            .ToList();

        //Act
        var missing = names.Where(n => !SymbolIcons.Has(n)).ToList();

        //Assert
        names.Count.Should().BeGreaterThan(70);
        missing.Should().BeEmpty();
    }
}

/// <summary>Named sessions.</summary>
public class SessionStoreTests
{
    [Fact]
    public void a_session_survives_a_round_trip()
    {
        //Arrange
        SessionStore store = new SessionStore(TestSettings.Create());

        //Act
        store.Write("Bach", new SessionData
        {
            Paths = new[] { "/tmp/a.ly", "/tmp/b.ly" },
            ActiveIndex = 1,
            AutoSave = false,
            IncludePath = new[] { "/tmp/include" },
        });
        SessionData read = store.Read("Bach");

        //Assert
        read.Paths.Should().Equal(new[] { "/tmp/a.ly", "/tmp/b.ly" });
        read.ActiveIndex.Should().Be(1);
        read.AutoSave.Should().BeFalse();
        read.IncludePath.Should().Equal(new[] { "/tmp/include" });
    }

    [Fact]
    public void renaming_a_session_keeps_what_it_holds()
    {
        //Arrange
        SessionStore store = new SessionStore(TestSettings.Create());
        store.Write("Old", new SessionData { Paths = new[] { "/tmp/a.ly" } });

        //Act
        store.Rename("Old", "New");

        //Assert
        store.Exists("Old").Should().BeFalse();
        store.Read("New").Paths.Should().Equal(new[] { "/tmp/a.ly" });
    }

    [Fact]
    public void deleting_a_session_removes_it()
    {
        //Arrange
        SessionStore store = new SessionStore(TestSettings.Create());
        store.Write("Gone", new SessionData());

        //Act
        store.Delete("Gone");

        //Assert
        store.SessionNames().Should().NotContain("Gone");
    }

    [Fact]
    public void the_names_sort_the_way_a_person_reads_them()
    {
        //Arrange
        SessionStore store = new SessionStore(TestSettings.Create());
        foreach (var name in new[] { "piece10", "piece2", "piece1" })
        {
            store.Write(name, new SessionData());
        }

        //Act, Assert
        store.SessionNames().Should().Equal(new[] { "piece1", "piece2", "piece10" });
    }

    [Fact]
    public void a_chosen_startup_session_that_is_gone_falls_back_to_none()
    {
        //Arrange
        SettingsStore settings = TestSettings.Create();
        SessionStore store = new SessionStore(settings);
        store.Startup = SessionStartup.Custom;
        settings.SetString(SessionStore.CustomKey, "Vanished");

        //Act
        string name = store.DefaultSessionName();

        //Assert
        name.Should().BeNull();
        store.Startup.Should().Be(SessionStartup.None);
    }

    [Fact]
    public void the_current_session_is_announced()
    {
        //Arrange
        SessionStore store = new SessionStore(TestSettings.Create());
        int changes = 0;
        store.CurrentSessionChanged += (_, _) => changes++;

        //Act
        store.SetCurrentSession("Work");
        store.SetCurrentSession("Work");
        store.SetCurrentSession(null);

        //Assert
        changes.Should().Be(2);
        store.CurrentSession.Should().BeNull();
    }
}

/// <summary>The other small editor tools.</summary>
public class EditorToolServiceTests
{
    [Fact]
    public void the_include_target_under_the_caret_is_found()
    {
        //Arrange
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        string included = Path.Combine(directory, "notes.ily");
        File.WriteAllText(included, "% notes\n");
        string master = Path.Combine(directory, "score.ly");
        File.WriteAllText(master, "\\include \"notes.ily\"\n");

        try
        {
            EditorDocument document = EditorDocument.NewFromPath(master);
            string line = "\\include \"notes.ily\"";

            //Act
            var targets = OpenFileAtCursorTargets(document, line);

            //Assert
            targets.Should().Contain(t => Path.GetFileName(t) == "notes.ily");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static IReadOnlyList<string> OpenFileAtCursorTargets(
        EditorDocument document, string line)
        => Fresco.Brix.Tools.OpenFileAtCursor.IncludeTargets(
            document, line, line.IndexOf("notes", StringComparison.Ordinal));

    [Fact]
    public void a_variable_reference_leads_to_its_definition()
    {
        //Arrange
        string text = "melody = { c4 d e f }\n\n\\score {\n  \\melody\n}\n";
        EditorDocument document = ToolDocument.Open(text);
        int caret = text.IndexOf("\\melody", StringComparison.Ordinal) + 2;

        //Act
        DefinitionTarget target = Fresco.Brix.Tools.GotoDefinition.Find(document, caret);

        //Assert
        target.Should().NotBeNull();
        target.Position.Should().Be(0);
    }

    [Fact]
    public void a_note_is_not_a_reference_to_anything()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("\\relative c' { c4 d }\n");
        int caret = document.Text.IndexOf("c4", StringComparison.Ordinal);

        //Act
        DefinitionTarget target = Fresco.Brix.Tools.GotoDefinition.Find(document, caret);

        //Assert
        target.Should().BeNull();
    }

    [Fact]
    public void the_tooltip_names_the_variable_the_music_belongs_to()
    {
        //Arrange
        string text = "melody = {\n  c4 d e f\n}\n";
        EditorDocument document = ToolDocument.Open(text);
        int caret = text.IndexOf("d e", StringComparison.Ordinal);

        //Act
        string definition = Fresco.Brix.Tools.DocumentTooltip.Definition(document, caret);

        //Assert
        definition.Should().Be("melody");
    }

    [Fact]
    public void the_include_tree_puts_an_included_file_under_its_master()
    {
        //Arrange
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        string included = Path.Combine(directory, "notes.ily");
        File.WriteAllText(included, "melody = { c4 }\n");
        string master = Path.Combine(directory, "score.ly");
        File.WriteAllText(master, "\\include \"notes.ily\"\n\\score { \\melody }\n");

        try
        {
            DocumentManager documents = new DocumentManager();
            documents.OpenDocument(master);
            documents.OpenDocument(included);

            //Act
            var roots = DocumentTree.Build(documents);

            //Assert
            roots.Should().HaveCount(1);
            roots[0].Path.Should().Be(master);
            roots[0].Children.Should().HaveCount(1);
            roots[0].Children[0].Path.Should().Be(included);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void the_completion_history_remembers_and_forgets_nothing()
    {
        //Arrange
        SettingsStore settings = TestSettings.Create();
        CompletionHistory history = CompletionHistory.For("test/history", settings);

        //Act
        history.Add("gamma");
        history.Add("alpha");
        history.Add("  ");
        history.Add("alpha");

        //Assert
        history.Strings.Should().Equal(new[] { "alpha", "gamma" });
    }

    [Fact]
    public void the_unicode_blocks_are_there_and_answer_for_a_character()
    {
        //Arrange, Act
        var block = Fresco.Brix.Editor.UnicodeBlocks.BlockOf('A');

        //Assert
        Fresco.Brix.Editor.UnicodeBlocks.Blocks.Count.Should().BeGreaterThan(100);
        block.Should().NotBeNull();
        block.Value.Name.Should().Be("Basic Latin");
    }
}
