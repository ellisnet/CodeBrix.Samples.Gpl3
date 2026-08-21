// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.MusicView;
using SilverAssertions;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>Reading the URLs the engine writes into a page.</summary>
public class TextEditLinkTests
{
    [Fact]
    public void the_engines_four_field_url_reads_as_file_line_and_character()
    {
        //Arrange
        string url = "textedit:///home/j/score.ly:8:26:27";

        //Act
        bool ok = TextEditLink.TryParse(url, out TextEditPlace place);

        //Assert — the THIRD field is the character index; the fourth is a
        //display column and is deliberately dropped.
        ok.Should().BeTrue();
        place.FileName.Should().Be("/home/j/score.ly");
        place.Line.Should().Be(8);
        place.Column.Should().Be(26);
    }

    [Fact]
    public void a_percent_encoded_file_name_is_decoded()
    {
        //Arrange
        string url = "textedit:///home/j/my%20score.ly:1:0:0";

        //Act
        TextEditLink.TryParse(url, out TextEditPlace place);

        //Assert
        place.FileName.Should().Be("/home/j/my score.ly");
    }

    [Fact]
    public void a_url_without_the_trailing_column_is_not_one_of_ours()
    {
        //Arrange
        string url = "textedit:///home/j/score.ly:8:26";

        //Act
        bool ok = TextEditLink.TryParse(url, out _);

        //Assert
        ok.Should().BeFalse();
    }

    [Fact]
    public void an_ordinary_web_link_is_not_a_textedit_link()
    {
        //Arrange
        string url = "https://lilypond.org";

        //Act
        bool parsed = TextEditLink.TryParse(url, out _);

        //Assert
        parsed.Should().BeFalse();
        TextEditLink.IsTextEdit(url).Should().BeFalse();
    }
}

/// <summary>Putting a run's SVG files back together into scores.</summary>
public class ScoreGroupingTests
{
    [Fact]
    public void files_of_one_score_are_grouped_and_put_in_page_order()
    {
        //Arrange — the natural sort's whole point: page 2 before page 10.
        string[] files =
        {
            "/out/score-10.svg", "/out/score-2.svg", "/out/score-1.svg",
        };

        //Act
        var groups = ScoreDocuments.GroupPages(files);

        //Assert
        groups.Count.Should().Be(1);
        groups[0].BaseName.Should().Be("/out/score.svg");
        groups[0].Pages.Should().Equal("/out/score-1.svg", "/out/score-2.svg", "/out/score-10.svg");
    }

    [Fact]
    public void a_single_page_score_keeps_its_own_name()
    {
        //Arrange
        string[] files = { "/out/score.svg" };

        //Act
        var groups = ScoreDocuments.GroupPages(files);

        //Assert
        groups[0].BaseName.Should().Be("/out/score.svg");
        groups[0].Pages.Should().Equal("/out/score.svg");
    }

    [Fact]
    public void two_scores_from_one_run_stay_apart()
    {
        //Arrange — what \bookOutputName produces.
        string[] files =
        {
            "/out/violin-1.svg", "/out/piano-1.svg", "/out/violin-2.svg",
        };

        //Act
        var groups = ScoreDocuments.GroupPages(files);

        //Assert
        groups.Count.Should().Be(2);
        groups.Select(g => g.BaseName).Should().Equal("/out/violin.svg", "/out/piano.svg");
        groups[0].Pages.Count.Should().Be(2);
    }
}

/// <summary>Answering the score's font families from the engine's own faces.</summary>
public class ScoreTypefaceTests
{
    [Fact]
    public void the_css_generics_the_backend_writes_all_resolve()
    {
        //Arrange
        var resolver = new LilyPortTypefaceResolver();

        //Act
        SKTypeface serif = resolver.Resolve(
            "serif", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        SKTypeface sans = resolver.Resolve(
            "sans", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        SKTypeface mono = resolver.Resolve(
            "monospace", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        //Assert — the very faces the engine measured the text with.
        serif.FamilyName.Should().Contain("C059");
        sans.FamilyName.Should().Contain("Nimbus Sans");
        mono.FamilyName.Should().Contain("Nimbus Mono");
    }

    [Fact]
    public void bold_and_italic_pick_the_right_face_of_the_chain()
    {
        //Arrange
        var resolver = new LilyPortTypefaceResolver();

        //Act
        SKTypeface bold = resolver.Resolve(
            "serif", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        SKTypeface italic = resolver.Resolve(
            "serif", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);

        //Assert
        bold.IsBold.Should().BeTrue();
        italic.IsItalic.Should().BeTrue();
    }

    [Fact]
    public void a_css_list_resolves_on_the_first_name_that_is_known()
    {
        //Arrange — what the engine writes for a document that asked for a face
        //it does not ship.
        string family = "Linux Libertine O, Noto Serif CJK JP, Noto Serif JP, serif";

        //Act
        string chain = LilyPortTypefaceResolver.Normalize(family);

        //Assert
        chain.Should().Be("serif");
    }

    [Fact]
    public void the_engines_three_virtual_names_resolve_by_category()
    {
        //Assert
        LilyPortTypefaceResolver.Normalize("LilyPond Serif").Should().Be("serif");
        LilyPortTypefaceResolver.Normalize("LilyPond Sans Serif").Should().Be("sans");
        LilyPortTypefaceResolver.Normalize("LilyPond Monospace").Should().Be("typewriter");
    }

    [Fact]
    public void a_family_nothing_answers_gets_the_last_resort_face_rather_than_nothing()
    {
        //Arrange
        var resolver = new LilyPortTypefaceResolver();

        //Act
        SKTypeface face = resolver.Resolve(
            "Foo Bar Baz", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        //Assert — the engine measured it with this face, so it is drawn with it;
        //what never happens is reaching a font installed on the machine.
        LilyPortTypefaceResolver.Normalize("Foo Bar Baz").Should().Be("unknown");
        face.FamilyName.Should().Contain("Schola");
    }
}

/// <summary>
/// Binding a score's links to the source document, so they keep pointing at the
/// right notes while the user types.
/// </summary>
public class BoundLinksTests
{
    private static EditorDocument DocumentWith(string text)
    {
        var document = new EditorDocument();
        document.Document.Text = text;
        return document;
    }

    [Fact]
    public void a_link_binds_to_the_character_the_engine_named()
    {
        //Arrange — line 2, character 5 is the "e".
        EditorDocument document = DocumentWith("\\relative c' {\nc d e f\n}\n");
        var links = new Dictionary<(int Line, int Column), List<object>>
        {
            [(2, 4)] = new List<object> { "e" },
        };

        //Act
        var bound = new BoundLinks(document, links);
        var cursor = bound.Cursor(2, 4);

        //Assert
        cursor.Should().NotBeNull();
        document.Document.GetText(cursor.Value.Offset, 1).Should().Be("e");
    }

    [Fact]
    public void an_anchor_moves_with_the_text_that_is_typed_before_it()
    {
        //Arrange
        EditorDocument document = DocumentWith("\\relative c' {\nc d e f\n}\n");
        var links = new Dictionary<(int Line, int Column), List<object>>
        {
            [(2, 4)] = new List<object> { "e" },
        };
        var bound = new BoundLinks(document, links);
        int before = bound.Cursor(2, 4).Value.Offset;

        //Act — insert two characters ahead of it.
        document.Document.Insert(0, "% ");

        //Assert — the score is now older than the text, and the link still
        //points at the same note.
        bound.Cursor(2, 4).Value.Offset.Should().Be(before + 2);
        document.Document.GetText(bound.Cursor(2, 4).Value.Offset, 1).Should().Be("e");
    }

    [Fact]
    public void a_caret_on_a_link_selects_that_link()
    {
        //Arrange
        EditorDocument document = DocumentWith("c d e f\n");
        var links = new Dictionary<(int Line, int Column), List<object>>
        {
            [(1, 0)] = new List<object> { "c" },
            [(1, 2)] = new List<object> { "d" },
            [(1, 4)] = new List<object> { "e" },
        };
        var bound = new BoundLinks(document, links);

        //Act
        var range = bound.Indices(4, 4, null);

        //Assert
        range.Should().NotBeNull();
        range.Value.Start.Should().Be(2);
        range.Value.Length.Should().Be(1);
    }

    [Fact]
    public void a_selection_covers_every_link_inside_it()
    {
        //Arrange
        EditorDocument document = DocumentWith("c d e f\n");
        var links = new Dictionary<(int Line, int Column), List<object>>
        {
            [(1, 0)] = new List<object> { "c" },
            [(1, 2)] = new List<object> { "d" },
            [(1, 4)] = new List<object> { "e" },
            [(1, 6)] = new List<object> { "f" },
        };
        var bound = new BoundLinks(document, links);

        //Act — select "d e".
        var range = bound.Indices(2, 5, null);

        //Assert
        range.Value.Start.Should().Be(1);
        range.Value.Length.Should().Be(2);
    }

    [Fact]
    public void a_caret_before_every_link_selects_none_of_them()
    {
        //Arrange
        EditorDocument document = DocumentWith("  c d e\n");
        var links = new Dictionary<(int Line, int Column), List<object>>
        {
            [(1, 2)] = new List<object> { "c" },
        };
        var bound = new BoundLinks(document, links);

        //Act
        var range = bound.Indices(0, 0, null);

        //Assert
        range.Should().BeNull();
    }

    [Fact]
    public void a_caret_on_a_later_line_than_its_nearest_link_clears_the_highlight()
    {
        //Arrange
        EditorDocument document = DocumentWith("c d e\n\n\n");
        var links = new Dictionary<(int Line, int Column), List<object>>
        {
            [(1, 0)] = new List<object> { "c" },
        };
        var bound = new BoundLinks(document, links);

        //Act — the caret is on line 3, the link on line 1.
        var range = bound.Indices(7, 7, null);

        //Assert — an empty range means "clear what was highlighted", which is
        //not the same as null, "leave it alone".
        range.Should().NotBeNull();
        range.Value.Length.Should().Be(0);
    }
}

/// <summary>Working out how much source text one link stands for.</summary>
public class CursorPositionsTests
{
    private static AteLyDocument LyDocumentFor(string text)
    {
        var document = new EditorDocument();
        document.Document.Text = text;
        var highlighter = new LyHighlighter(document.Document);
        return new AteLyDocument(document.Document, highlighter);
    }

    //The text has to LOOK like LilyPond, because the tokenizer takes its mode
    //from the document — which is exactly how the editor feeds it in the app.
    private const string Slurred = "\\relative c' { c4( d4 e4) }";
    private const string Marked = "\\relative c' { c4^\"hello there\" d4 }";

    [Fact]
    public void a_plain_note_stands_for_just_itself()
    {
        //Arrange — the "d" of the slurred phrase.
        AteLyDocument document = LyDocumentFor(Slurred);

        //Act
        var positions = CursorPositions.Positions(document, 19);

        //Assert
        positions.Count.Should().Be(1);
        positions[0].Should().Be((19, 1));
    }

    [Fact]
    public void a_slur_stands_for_both_of_its_ends()
    {
        //Arrange
        AteLyDocument document = LyDocumentFor(Slurred);

        //Act — the link points at the opening bracket.
        var positions = CursorPositions.Positions(document, 17);

        //Assert — and the closing one comes with it, which is what makes
        //hovering a slur light up the whole slur in the source.
        positions.Count.Should().Be(2);
        positions[0].Should().Be((17, 1));
        positions[1].Should().Be((24, 1));
    }

    [Fact]
    public void a_string_stands_for_the_whole_string()
    {
        //Arrange
        AteLyDocument document = LyDocumentFor(Marked);

        //Act — the link points at the direction marker.
        var positions = CursorPositions.Positions(document, 17);

        //Assert — from the ^ to the closing quote.
        positions.Count.Should().Be(1);
        positions[0].Start.Should().Be(17);
        positions[0].Length.Should().Be(14);
    }

    [Fact]
    public void nothing_at_the_end_of_the_document_stands_for_nothing()
    {
        //Arrange
        AteLyDocument document = LyDocumentFor(Slurred);

        //Act
        var positions = CursorPositions.Positions(document, Slurred.Length);

        //Assert
        positions.Should().BeEmpty();
    }
}
