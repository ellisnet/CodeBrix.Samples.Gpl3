// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Ly;
using Fresco.Brix.Tools;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>Shared setup for the tools that edit a document.</summary>
public static class ToolDocument
{
    /// <summary>Makes an unsaved document holding some text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The document.</returns>
    public static EditorDocument Open(string text)
    {
        EditorDocument document = new EditorDocument();
        document.Document.Text = text;
        return document;
    }

    /// <summary>Makes an ly cursor over a document's whole text.</summary>
    /// <param name="document">The document.</param>
    /// <param name="start">The range start.</param>
    /// <param name="end">The range end, or null for the whole document.</param>
    /// <returns>The cursor.</returns>
    public static Cursor Range(EditorDocument document, int start = 0, int? end = null)
        => new Cursor(
            DocumentEditorState.For(document).LyDocument,
            start,
            end ?? document.Text.Length);
}

/// <summary>Taking one kind of input back out of a selection.</summary>
public class QuickRemoveTests
{
    [Fact]
    public void the_slurs_go_and_the_notes_stay()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4( d e) f }\n");

        //Act
        QuickRemove.Slurs(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("\\relative c' { c4 d e f }\n");
    }

    [Fact]
    public void the_beams_go()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c8[ d e f] }\n");

        //Act
        QuickRemove.Beams(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("\\relative c' { c8 d e f }\n");
    }

    [Fact]
    public void the_dynamics_go()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4\\f d\\p e f }\n");

        //Act
        QuickRemove.Dynamics(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("\\relative c' { c4 d e f }\n");
    }

    [Fact]
    public void an_articulation_goes_with_the_direction_that_points_it()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4^\\marcato d_\\staccato e f }\n");

        //Act
        QuickRemove.Articulations(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("\\relative c' { c4 d e f }\n");
    }

    [Fact]
    public void an_ornament_is_not_an_articulation()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4^\\marcato d^\\trill e f }\n");

        //Act
        QuickRemove.Ornaments(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("\\relative c' { c4^\\marcato d e f }\n");
    }

    [Fact]
    public void the_fingerings_go()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4-1 d-2 e-3 f }\n");

        //Act
        QuickRemove.Fingerings(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("\\relative c' { c4 d e f }\n");
    }

    [Fact]
    public void the_comments_go()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "% a note about the piece\n\\relative c' { c4 d } % trailing\n");

        //Act
        QuickRemove.Comments(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("\n\\relative c' { c4 d } \n");
    }

    [Fact]
    public void the_ligatures_go()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { \\[ c4 d \\] }\n");

        //Act
        QuickRemove.Ligatures(ToolDocument.Range(document));

        //Assert
        //The token goes and nothing else does — the spaces that surrounded it
        //are the user's text, and upstream leaves them alone too.
        document.Text.Should().Be("\\relative c' {  c4 d  }\n");
    }

    [Fact]
    public void a_postfix_markup_goes_whole()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4^\\markup { \\bold Allegro } d e f }\n");

        //Act
        QuickRemove.Markup(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("\\relative c' { c4 d e f }\n");
    }

    [Fact]
    public void a_postfix_string_goes_whole()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4^\"Allegro\" d e f }\n");

        //Act
        QuickRemove.Markup(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("\\relative c' { c4 d e f }\n");
    }

    [Fact]
    public void only_the_selection_is_touched()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4( d) e( f) }\n");
        int half = document.Text.IndexOf("e(", StringComparison.Ordinal);

        //Act
        QuickRemove.Slurs(ToolDocument.Range(document, 0, half));

        //Assert
        document.Text.Should().Be("\\relative c' { c4 d e( f) }\n");
    }

    [Fact]
    public void forcing_a_direction_rewrites_the_operators()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4^\\marcato d_\\staccato }\n");

        //Act
        QuickRemove.ForceDirections(ToolDocument.Range(document), "down");

        //Assert
        document.Text.Should().Be("\\relative c' { c4_\\marcato d_\\staccato }\n");
    }

    [Fact]
    public void forcing_a_direction_rewrites_the_commands_too()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { \\slurUp c4( d) }\n");

        //Act
        QuickRemove.ForceDirections(ToolDocument.Range(document), "neutral");

        //Assert
        document.Text.Should().Be("\\relative c' { \\slurNeutral c4( d) }\n");
    }
}

/// <summary>Cutting a selection and giving it a name.</summary>
public class CutAssignTests
{
    [Fact]
    public void the_selection_becomes_a_variable_and_leaves_a_reference()
    {
        //Arrange
        string text = "\\version \"2.24.0\"\n\n\\score {\n  { c4 d e f }\n}\n";
        EditorDocument document = ToolDocument.Open(text);
        int start = text.IndexOf("c4", StringComparison.Ordinal);
        int end = text.IndexOf(" }\n}", StringComparison.Ordinal);

        //Act
        bool assigned = CutAssign.Assign(document, "melody", start, end);

        //Assert
        assigned.Should().BeTrue();
        document.Text.Should().Contain("melody = { c4 d e f }");
        document.Text.Should().Contain("{ \\melody }");
    }

    [Fact]
    public void a_multi_line_selection_is_wrapped_on_its_own_lines()
    {
        //Arrange
        string text = "music = {\n  c4 d\n  e f\n}\n";
        EditorDocument document = ToolDocument.Open(text);
        int start = text.IndexOf("c4", StringComparison.Ordinal);
        int end = text.IndexOf("\n}", StringComparison.Ordinal);

        //Act
        CutAssign.Assign(document, "part", start, end);

        //Assert
        document.Text.Should().Contain("part = {\n");
        document.Text.Should().Contain("\\part");
    }

    [Fact]
    public void a_selection_in_lyric_mode_keeps_its_mode()
    {
        //Arrange
        string text = "words = \\lyricmode {\n  A -- ve Ma -- ri -- a\n}\n";
        EditorDocument document = ToolDocument.Open(text);
        int start = text.IndexOf("A --", StringComparison.Ordinal);
        int end = text.IndexOf("\n}", StringComparison.Ordinal);

        //Act
        CutAssign.Assign(document, "verse", start, end);

        //Assert
        document.Text.Should().Contain("verse = \\lyricmode {");
    }

    [Fact]
    public void nothing_selected_assigns_nothing()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4 d }\n");

        //Act
        bool assigned = CutAssign.Assign(document, "x", 3, 3);

        //Assert
        assigned.Should().BeFalse();
        document.Text.Should().Be("{ c4 d }\n");
    }

    [Fact]
    public void the_proposed_include_file_takes_the_ily_extension()
    {
        //Arrange
        string text = "\\version \"2.24.0\"\n\n{ c4 d e f }\n";
        EditorDocument document = ToolDocument.Open(text);
        int start = text.IndexOf('{');

        //Act
        IncludeFileProposal proposal = CutAssign.ProposeIncludeFile(
            document, start, text.Length - 1);

        //Assert
        proposal.Should().NotBeNull();
        Path.GetExtension(proposal.Path).Should().Be(".ily");
        proposal.Text.Should().StartWith("\\version \"2.24.0\"");
    }
}

/// <summary>The document outline.</summary>
public class DocumentStructureTests
{
    [Fact]
    public void a_score_is_a_title_item()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\version \"2.24.0\"\n\\score {\n  { c4 }\n}\n");

        //Act
        IReadOnlyList<OutlineItem> outline
            = DocumentStructure.For(document).Outline();

        //Assert
        outline.Should().Contain(i => i.Text == "\\score" && i.IsTitle);
    }

    [Fact]
    public void an_assignment_is_listed()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("melody = { c4 d e f }\n");

        //Act
        IReadOnlyList<OutlineItem> outline
            = DocumentStructure.For(document).Outline();

        //Assert
        outline.Should().Contain(i => i.Text.StartsWith("melody"));
    }

    [Fact]
    public void a_fixme_in_a_comment_is_an_alert()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "% FIXME wrong key\n{ c4 }\n");

        //Act
        IReadOnlyList<OutlineItem> outline
            = DocumentStructure.For(document).Outline();

        //Assert
        outline.Should().Contain(i => i.IsAlert && i.Text.Contains("FIXME"));
    }

    [Fact]
    public void a_score_inside_a_comment_is_not_listed()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "% \\score is mentioned here\n{ c4 }\n");

        //Act
        IReadOnlyList<OutlineItem> outline
            = DocumentStructure.For(document).Outline();

        //Assert
        outline.Should().NotContain(i => i.Text == "\\score");
    }

    [Fact]
    public void blanking_the_comments_keeps_every_offset()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "{ c4 } % tail\n%{ block %}\n{ d4 }\n");

        //Act
        string blanked = DocumentStructure.RemoveComments(document);

        //Assert
        blanked.Length.Should().Be(document.Text.Length);
        blanked.Should().NotContain("tail");
        blanked.Should().Contain("{ d4 }");
    }

    [Fact]
    public void the_items_come_back_in_document_order()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "melody = { c4 }\n\\score {\n  \\melody\n}\n");

        //Act
        IReadOnlyList<OutlineItem> outline
            = DocumentStructure.For(document).Outline();

        //Assert
        outline.Select(i => i.Position).Should().BeInAscendingOrder();
    }
}

/// <summary>The marked lines of a document.</summary>
public class BookmarksTests
{
    [Fact]
    public void a_marked_line_reports_its_mark()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("a\nb\nc\nd\n");
        Bookmarks marks = Bookmarks.For(document);

        //Act
        marks.SetMark(2, Bookmarks.MarkType);

        //Assert
        marks.HasMark(2, Bookmarks.MarkType).Should().BeTrue();
        marks.HasMark(1, Bookmarks.MarkType).Should().BeFalse();
    }

    [Fact]
    public void toggling_twice_leaves_no_mark()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("a\nb\nc\n");
        Bookmarks marks = Bookmarks.For(document);

        //Act
        marks.ToggleMark(1, Bookmarks.MarkType);
        marks.ToggleMark(1, Bookmarks.MarkType);

        //Assert
        marks.MarkedLines(Bookmarks.MarkType).Should().BeEmpty();
    }

    [Fact]
    public void a_mark_moves_down_when_a_line_is_inserted_above_it()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("a\nb\nc\n");
        Bookmarks marks = Bookmarks.For(document);
        marks.SetMark(2, Bookmarks.MarkType);

        //Act
        document.Document.Insert(0, "new\n");

        //Assert
        marks.MarkedLines(Bookmarks.MarkType).Should().Equal(new[] { 3 });
    }

    [Fact]
    public void the_next_mark_is_the_first_one_below()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("a\nb\nc\nd\ne\n");
        Bookmarks marks = Bookmarks.For(document);
        marks.SetMark(1, Bookmarks.MarkType);
        marks.SetMark(3, Bookmarks.MarkType);

        //Act, Assert
        marks.NextMark(0).Should().Be(1);
        marks.NextMark(1).Should().Be(3);
        marks.NextMark(3).Should().Be(-1);
        marks.PreviousMark(3).Should().Be(1);
        marks.PreviousMark(1).Should().Be(-1);
    }

    [Fact]
    public void clearing_one_kind_leaves_the_other()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("a\nb\nc\n");
        Bookmarks marks = Bookmarks.For(document);
        marks.SetMark(0, Bookmarks.MarkType);
        marks.SetMark(1, Bookmarks.ErrorType);

        //Act
        marks.Clear(Bookmarks.ErrorType);

        //Assert
        marks.MarkedLines(Bookmarks.MarkType).Should().Equal(new[] { 0 });
        marks.MarkedLines(Bookmarks.ErrorType).Should().BeEmpty();
    }

    [Fact]
    public void the_marks_survive_a_round_trip_through_the_metainfo()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("a\nb\nc\nd\n");
        Bookmarks marks = Bookmarks.For(document);
        marks.SetMark(1, Bookmarks.MarkType);
        marks.SetMark(3, Bookmarks.ErrorType);

        //Act
        string encoded = marks.Encode();

        //Assert
        Bookmarks.Decode(encoded).Should().Contain((Bookmarks.MarkType, 1));
        Bookmarks.Decode(encoded).Should().Contain((Bookmarks.ErrorType, 3));
    }
}

/// <summary>Back and forward through the places the user has been.</summary>
public class BrowserInterfaceTests
{
    private static (DocumentManager Documents, BrowserInterface Browser) Make()
    {
        DocumentManager documents = new DocumentManager();
        BrowserInterface browser = new BrowserInterface(documents);
        EditorDocument current = null;
        int offset = 0;
        browser.CurrentPosition = () => new BrowsePosition
        {
            Document = documents.CurrentDocument,
            Anchor = documents.CurrentDocument?.Document.CreateAnchor(0),
        };
        browser.GoToPosition = position =>
        {
            current = position.Document;
            offset = position.Anchor?.Offset ?? 0;
            if (position.Document != null)
            {
                documents.CurrentDocument = position.Document;
            }
        };
        return (documents, browser);
    }

    [Fact]
    public void back_is_off_until_something_has_been_jumped_to()
    {
        //Arrange, Act
        var (_, browser) = Make();

        //Assert
        browser.Actions.GoBack.IsEnabled.Should().BeFalse();
        browser.Actions.GoForward.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void a_jump_turns_back_on()
    {
        //Arrange
        var (documents, browser) = Make();
        EditorDocument first = documents.CreateDocument();
        documents.CurrentDocument = first;
        EditorDocument second = documents.CreateDocument();

        //Act
        browser.GoTo(second, 0);

        //Assert
        browser.Actions.GoBack.IsEnabled.Should().BeTrue();
        browser.Actions.GoForward.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void going_back_then_forward_returns_to_where_it_started()
    {
        //Arrange
        var (documents, browser) = Make();
        EditorDocument first = documents.CreateDocument();
        documents.CurrentDocument = first;
        EditorDocument second = documents.CreateDocument();
        browser.GoTo(second, 0);

        //Act
        browser.GoBack();

        //Assert
        documents.CurrentDocument.Should().Be(first);
        browser.Actions.GoForward.IsEnabled.Should().BeTrue();

        //Act
        browser.GoForward();

        //Assert
        documents.CurrentDocument.Should().Be(second);
    }

    [Fact]
    public void closing_a_document_forgets_its_places()
    {
        //Arrange
        var (documents, browser) = Make();
        EditorDocument first = documents.CreateDocument();
        documents.CurrentDocument = first;
        EditorDocument second = documents.CreateDocument();
        browser.GoTo(second, 0);

        //Act
        documents.CloseDocument(second);

        //Assert
        browser.Count.Should().BeLessThan(3);
    }
}

/// <summary>Which document comes forward when one is closed.</summary>
public class HistoryManagerTests
{
    [Fact]
    public void the_most_recent_document_is_first()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        Fresco.Brix.Services.HistoryManager history
            = new Fresco.Brix.Services.HistoryManager(documents);
        EditorDocument first = documents.CreateDocument();
        EditorDocument second = documents.CreateDocument();

        //Act
        documents.CurrentDocument = first;
        documents.CurrentDocument = second;

        //Assert
        history.Documents().First().Should().Be(second);
    }

    [Fact]
    public void closing_the_front_document_falls_back_to_the_one_before_it()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        Fresco.Brix.Services.HistoryManager history
            = new Fresco.Brix.Services.HistoryManager(documents);
        EditorDocument first = documents.CreateDocument();
        EditorDocument second = documents.CreateDocument();
        documents.CurrentDocument = first;
        documents.CurrentDocument = second;

        //Act
        EditorDocument successor = history.SuccessorOf(second);

        //Assert
        successor.Should().Be(first);
    }

    [Fact]
    public void closing_a_background_document_needs_no_successor()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        Fresco.Brix.Services.HistoryManager history
            = new Fresco.Brix.Services.HistoryManager(documents);
        EditorDocument first = documents.CreateDocument();
        EditorDocument second = documents.CreateDocument();
        documents.CurrentDocument = first;
        documents.CurrentDocument = second;

        //Act
        EditorDocument successor = history.SuccessorOf(first);

        //Assert
        successor.Should().BeNull();
    }
}

/// <summary>The shortcut spellings the ported action tables use.</summary>
public class QtShortcutNameTests
{
    [Theory]
    [InlineData("Alt+Backspace")]
    [InlineData("Alt+Return")]
    [InlineData("Ctrl+Shift+Return")]
    [InlineData("Ctrl+(")]
    [InlineData("Ctrl+\"")]
    [InlineData("Alt+PageDown")]
    [InlineData("Ctrl+Delete")]
    [InlineData("Insert")]
    [InlineData("Ctrl+;")]
    [InlineData("Alt+'")]
    public void every_shortcut_the_ported_tables_use_parses(string text)
    {
        //Arrange, Act
        Fresco.Brix.Commands.KeySequence shortcut
            = Fresco.Brix.Commands.KeySequence.Parse(text);

        //Assert
        //A shortcut that does not parse is silently DROPPED, so a typo here
        //costs a command its key with nothing to show for it.
        shortcut.Should().NotBeNull();
    }

    [Fact]
    public void a_qt_key_name_means_the_same_key_as_the_platforms()
    {
        //Arrange, Act, Assert
        Fresco.Brix.Commands.KeySequence.Parse("Alt+Backspace")
            .Should().Be(Fresco.Brix.Commands.KeySequence.Parse("Alt+Back"));
        Fresco.Brix.Commands.KeySequence.Parse("Ctrl+Return")
            .Should().Be(Fresco.Brix.Commands.KeySequence.Parse("Ctrl+Enter"));
    }

    [Fact]
    public void a_shifted_character_means_its_key_with_shift()
    {
        //Arrange, Act, Assert
        Fresco.Brix.Commands.KeySequence.Parse("Ctrl+(")
            .Should().Be(Fresco.Brix.Commands.KeySequence.Parse("Ctrl+Shift+9"));
    }
}
