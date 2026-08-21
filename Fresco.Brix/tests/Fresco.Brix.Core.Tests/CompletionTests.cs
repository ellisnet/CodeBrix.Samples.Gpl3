// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Completion;
using Fresco.Brix.Documents;
using SilverAssertions;
using System;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>What the completion popup offers where the caret is.</summary>
public class CompletionAnalyzerTests
{
    /// <summary>
    /// Analyzes a document written with a <c>|</c> where the caret is.
    /// </summary>
    /// <param name="textWithCaret">The text, with one <c>|</c>.</param>
    /// <returns>The result.</returns>
    private static CompletionResult Analyze(string textWithCaret)
    {
        int caret = textWithCaret.IndexOf('|');
        EditorDocument document = ToolDocument.Open(textWithCaret.Remove(caret, 1));
        return new CompletionAnalyzer().Completions(document, caret);
    }

    private static string[] Inserts(CompletionResult result)
        => result.Model?.Entries.Select(e => e.Insert).ToArray() ?? Array.Empty<string>();

    [Fact]
    public void the_top_level_offers_version_and_score()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\ver|");

        //Assert
        result.HasCompletions.Should().BeTrue();
        Inserts(result).Should().Contain("\\version");
        Inserts(result).Should().Contain("\\score {");
    }

    [Fact]
    public void a_top_level_variable_is_offered_with_its_equals_sign()
    {
        //Arrange, Act
        CompletionResult result = Analyze("pipe|");

        //Assert
        result.Model.Entries.Should().Contain(
            e => e.Insert == "pipeSymbol = " && e.Display == "pipeSymbol");
    }

    [Fact]
    public void inside_a_header_the_variables_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\header {\n  ti|\n}\n");

        //Assert
        result.Model.Entries.Should().Contain(
            e => e.Insert == "title = " && e.Display == "title");
    }

    [Fact]
    public void inside_a_paper_block_the_paper_variables_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\paper {\n  paper-|\n}\n");

        //Assert
        Inserts(result).Should().Contain(i => i.StartsWith("paper-height"));
    }

    [Fact]
    public void after_clef_the_clef_names_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\relative c' { \\clef |");

        //Assert
        Inserts(result).Should().Contain("treble");
        Inserts(result).Should().Contain("bass");
    }

    [Fact]
    public void after_repeat_the_repeat_types_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\relative c' { \\repeat |");

        //Assert
        Inserts(result).Should().Contain("volta");
        Inserts(result).Should().Contain("unfold");
    }

    [Fact]
    public void after_language_the_note_languages_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\language \"|");

        //Assert
        Inserts(result).Should().Contain("nederlands");
        Inserts(result).Should().Contain("deutsch");
    }

    [Fact]
    public void after_a_key_note_the_modes_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\relative c' { \\key c |");

        //Assert
        Inserts(result).Should().Contain("\\major");
        Inserts(result).Should().Contain("\\minor");
    }

    [Fact]
    public void after_override_the_contexts_and_grobs_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\relative c' { \\override |");

        //Assert
        Inserts(result).Should().Contain("Staff");
        Inserts(result).Should().Contain("NoteHead");
    }

    [Fact]
    public void after_a_grob_name_and_a_dot_the_properties_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\relative c' { \\override NoteHead.|");

        //Assert
        Inserts(result).Should().Contain("color");
    }

    [Fact]
    public void inside_markup_the_markup_commands_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\markup { \\bo|");

        //Assert
        Inserts(result).Should().Contain("\\bold");
    }

    [Fact]
    public void inside_a_layout_block_the_layout_variables_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\layout {\n  ind|\n}\n");

        //Assert
        Inserts(result).Should().Contain(i => i.StartsWith("indent"));
    }

    [Fact]
    public void inside_a_with_block_the_context_properties_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\new Staff \\with {\n  |\n}\n");

        //Assert
        result.HasCompletions.Should().BeTrue();
        Inserts(result).Should().Contain(i => i.StartsWith("\\consists"));
    }

    [Fact]
    public void after_new_the_context_names_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\score { \\new |");

        //Assert
        Inserts(result).Should().Contain("Staff");
        Inserts(result).Should().Contain("Voice");
    }

    [Fact]
    public void in_music_a_backslash_offers_the_music_commands()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\relative c' { c4 \\tr|");

        //Assert
        Inserts(result).Should().Contain("\\transpose");
        result.Column.Should().Be("\\relative c' { c4 ".Length);
    }

    [Fact]
    public void a_variable_the_document_defines_is_offered_in_music()
    {
        //Arrange, Act
        CompletionResult result = Analyze(
            "myMelody = { c4 d }\n\n\\score {\n  \\relative c' { \\my|\n");

        //Assert
        Inserts(result).Should().Contain("\\myMelody");
    }

    [Fact]
    public void inside_a_score_the_score_contents_are_offered()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\score {\n  \\lay|\n}\n");

        //Assert
        Inserts(result).Should().Contain("\\layout {");
    }

    [Fact]
    public void a_parser_with_no_tests_offers_nothing()
    {
        //Arrange, Act
        //Inside a block comment the active parser is ParseBlockComment, which
        //upstream's table has no entry for. (A LINE comment is different: the
        //state there is still ParseGlobal, so upstream does offer the
        //top-level list inside one, and so does this.)
        CompletionResult result = Analyze("%{ a note about the piece |");

        //Assert
        result.HasCompletions.Should().BeFalse();
    }

    [Fact]
    public void probe_the_real_document_shape()
    {
        //Arrange
        string text = "\\version \"2.24.0\"\n\n\\header {\n  title = \"W5 Verification\"\n}\n\nmelody = \\relative c' {\n  c4^. d e f | g a b c | \\tr|\n}\n\n\\score {\n  \\melody\n  \\layout { }\n}\n";
        int caret = text.IndexOf('|', text.IndexOf("\\tr", System.StringComparison.Ordinal));
        Fresco.Brix.Documents.EditorDocument document
            = ToolDocument.Open(text.Remove(caret, 1));

        //Act
        CompletionResult result = new CompletionAnalyzer().Completions(document, caret);

        //Assert
        int lineStart = text.LastIndexOf('\n', caret - 1) + 1;
        result.HasCompletions.Should().BeTrue();
        result.Column.Should().Be(caret - lineStart - 3);
    }

    [Fact]
    public void the_column_marks_where_the_completed_word_starts()
    {
        //Arrange, Act
        CompletionResult result = Analyze("\\header {\n  tit|\n}\n");

        //Assert
        //The line is "  tit"; the word starts at column 2.
        result.Column.Should().Be(2);
    }
}

/// <summary>The fixed completion lists.</summary>
public class CompletionDataTests
{
    [Fact]
    public void a_command_model_shows_and_inserts_the_backslash()
    {
        //Arrange, Act
        CompletionModel model = CompletionModel.OfCommands(new[] { "bold" });

        //Assert
        model.Entries[0].Insert.Should().Be("\\bold");
        model.Entries[0].Display.Should().Be("\\bold");
    }

    [Fact]
    public void a_variable_model_shows_the_name_and_inserts_the_equals_sign()
    {
        //Arrange, Act
        CompletionModel model = CompletionModel.OfVariables(new[] { "title" });

        //Assert
        model.Entries[0].Insert.Should().Be("title = ");
        model.Entries[0].Display.Should().Be("title");
    }

    [Fact]
    public void a_command_or_variable_model_treats_the_two_differently()
    {
        //Arrange, Act
        CompletionModel model = CompletionModel.OfCommandsOrVariables(
            new[] { "\\override", "indent" });

        //Assert
        model.Entries[0].Insert.Should().Be("\\override");
        model.Entries[1].Insert.Should().Be("indent = ");
    }

    [Fact]
    public void a_scheme_symbol_model_can_carry_the_hash_quote()
    {
        //Arrange, Act
        CompletionModel model = CompletionModel.OfSchemeSymbols(
            new[] { "color" }, hashQuote: true);

        //Assert
        model.Entries[0].Insert.Should().Be("#'color");
    }

    [Fact]
    public void the_top_level_list_holds_the_blocks_and_the_modes()
    {
        //Arrange, Act
        string[] inserts = CompletionData.TopLevelContents.Entries
            .Select(e => e.Insert).ToArray();

        //Assert
        inserts.Should().Contain("\\score {");
        inserts.Should().Contain("\\header {");
        inserts.Should().Contain("\\version");
    }

    [Fact]
    public void the_grob_properties_come_from_the_engine_data()
    {
        //Arrange, Act
        CompletionModel model = CompletionData.GrobProperties("NoteHead", false);

        //Assert
        model.Count.Should().BeGreaterThan(0);
        model.Entries.Select(e => e.Insert).Should().Contain("color");
    }
}

/// <summary>Finding and replacing.</summary>
public class SearchLogicTests
{
    [Fact]
    public void every_occurrence_is_found()
    {
        //Arrange, Act
        var matches = Fresco.Brix.Search.SearchLogic.Find("a b a b a", "a");

        //Assert
        matches.Select(m => m.Start).Should().Equal(new[] { 0, 4, 8 });
    }

    [Fact]
    public void case_can_be_ignored()
    {
        //Arrange, Act
        var matches = Fresco.Brix.Search.SearchLogic.Find(
            "Alpha alpha", "ALPHA", caseSensitive: false);

        //Assert
        matches.Should().HaveCount(2);
    }

    [Fact]
    public void a_plain_term_is_not_a_regular_expression()
    {
        //Arrange, Act
        var matches = Fresco.Brix.Search.SearchLogic.Find("a.b axb", "a.b");

        //Assert
        matches.Should().HaveCount(1);
        matches[0].Start.Should().Be(0);
    }

    [Fact]
    public void a_regex_term_is_one()
    {
        //Arrange, Act
        var matches = Fresco.Brix.Search.SearchLogic.Find("a.b axb", "a.b", regex: true);

        //Assert
        matches.Should().HaveCount(2);
    }

    [Fact]
    public void a_broken_expression_finds_nothing_rather_than_throwing()
    {
        //Arrange, Act
        var matches = Fresco.Brix.Search.SearchLogic.Find("abc", "a(", regex: true);

        //Assert
        matches.Should().BeEmpty();
    }

    [Fact]
    public void a_range_confines_the_search_and_the_offsets_stay_absolute()
    {
        //Arrange, Act
        var matches = Fresco.Brix.Search.SearchLogic.Find(
            "aaa aaa", "a", rangeStart: 4, rangeEnd: 7);

        //Assert
        matches.Select(m => m.Start).Should().Equal(new[] { 4, 5, 6 });
    }

    [Fact]
    public void a_replacement_only_goes_through_when_the_text_still_matches()
    {
        //Arrange, Act, Assert
        Fresco.Brix.Search.SearchLogic
            .ReplacementFor("cat", "cat", "dog").Should().Be("dog");
        Fresco.Brix.Search.SearchLogic
            .ReplacementFor("cow", "cat", "dog").Should().BeNull();
    }

    [Fact]
    public void a_regex_replacement_expands_its_groups()
    {
        //Arrange, Act
        string replacement = Fresco.Brix.Search.SearchLogic.ReplacementFor(
            "c4 d4", @"(\w)4 (\w)4", "$2 $1", regex: true);

        //Assert
        replacement.Should().Be("d c");
    }

    [Fact]
    public void a_selected_word_becomes_the_search_term()
    {
        //Arrange, Act, Assert
        Fresco.Brix.Search.SearchLogic
            .TermForSelection("melody", false).Should().Be("melody");
        Fresco.Brix.Search.SearchLogic
            .TermForSelection("+++", false).Should().Be(string.Empty);
        Fresco.Brix.Search.SearchLogic
            .TermForSelection("a.b", true).Should().Be(@"a\.b");
    }

    [Fact]
    public void the_bisections_find_the_neighbouring_matches()
    {
        //Arrange
        var matches = Fresco.Brix.Search.SearchLogic.Find("a b a b a", "a");

        //Act, Assert
        Fresco.Brix.Search.SearchLogic.BisectRight(matches, 0).Should().Be(1);
        Fresco.Brix.Search.SearchLogic.BisectLeft(matches, 4).Should().Be(1);
        Fresco.Brix.Search.SearchLogic.BisectRight(matches, 8).Should().Be(3);
    }
}
