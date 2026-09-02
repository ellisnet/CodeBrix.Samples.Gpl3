// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Ly.Music;
using Fresco.Brix.ObjectEditor;
using SilverAssertions;
using System;
using System.Globalization;
using System.Threading;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The Object Editor's working half against upstream's
/// <c>objecteditor/defineoffset.py</c>: which grob an item is overridden
/// through, the override command it writes, and where that command lands.
/// </summary>
public class ObjectEditorTests
{
    /// <summary>
    /// Upstream's <c>item2objectDict</c> has four entries, and everything else
    /// falls through to a placeholder.
    /// </summary>
    /// <param name="item">The music item.</param>
    /// <param name="grob">The grob it is overridden through.</param>
    /// <param name="context">The context, or the empty string.</param>
    [Theory]
    [MemberData(nameof(TheFourEntries))]
    public void the_table_is_upstreams_own(Item item, string grob, string context)
    {
        //Arrange
        DefineOffset define = new DefineOffset(ToolDocument.Open("c4"));

        //Act
        string answer = define.ItemToObject(item);

        //Assert
        answer.Should().Be(grob);
        define.LilyObject.Should().Be(grob);
        define.LilyContext.Should().Be(context);
    }

    /// <summary>The four entries of upstream's table.</summary>
    /// <returns>The item, the grob and the context.</returns>
    public static TheoryData<Item, string, string> TheFourEntries => new()
    {
        //python-ly's `String', which is StringItem here — the port's one
        //renamed music item, looked up under upstream's own name.
        { new StringItem(), "TextScript", string.Empty },
        { new Markup(), "TextScript", string.Empty },
        { new Tempo(), "MetronomeMark", "Score" },
        { new Articulation(), "Script", string.Empty },
    };

    [Fact]
    public void anything_the_table_does_not_know_answers_upstreams_own_placeholder()
    {
        //Arrange — ⚠ ODD BUT DELIBERATE: upstream's item2object() falls back to
        //the literal grob name "still testing!", because the module is its own
        //declared "very first stub". Ported faithfully, not fixed.
        DefineOffset define = new DefineOffset(ToolDocument.Open("c4"));

        //Act, Assert
        define.ItemToObject(new Note()).Should().Be("still testing!");
        define.ItemToObject(null).Should().Be("still testing!");
    }

    [Fact]
    public void a_markup_attached_to_a_note_is_overridden_through_TextScript()
    {
        //Arrange — upstream reads the node at the cursor and takes its FIRST
        //music child; for `c4^\markup {…}' the node is the Postfix and its
        //first child is the Markup.
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4^\\markup { hi } d e f }\n");
        DefineOffset define = new DefineOffset(document);

        //Act
        string grob = define.GetCurrentLilyObject(document.Text.IndexOf('^'));

        //Assert
        grob.Should().Be("TextScript");
        define.LilyContext.Should().BeEmpty();
        define.Position.Should().Be(document.Text.IndexOf('^'));
    }

    [Fact]
    public void a_quoted_string_attached_to_a_note_is_overridden_through_TextScript()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4^\"hi\" d e f }\n");
        DefineOffset define = new DefineOffset(document);

        //Act
        string grob = define.GetCurrentLilyObject(document.Text.IndexOf('^'));

        //Assert
        grob.Should().Be("TextScript");
    }

    [Fact]
    public void an_articulation_is_overridden_through_Script()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4-. d e f }\n");
        DefineOffset define = new DefineOffset(document);

        //Act
        string grob = define.GetCurrentLilyObject(document.Text.IndexOf("-."));

        //Assert
        grob.Should().Be("Script");
    }

    [Fact]
    public void a_tempo_mark_is_overridden_through_MetronomeMark_in_the_Score()
    {
        //Arrange — the music list holding the tempo mark; its first music
        //child is the Tempo, which is the one entry of upstream's table that
        //carries a context.
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { \\tempo 4 = 60 c4 d e f }\n");
        DefineOffset define = new DefineOffset(document);

        //Act
        string grob = define.GetCurrentLilyObject(document.Text.IndexOf('{'));

        //Assert
        grob.Should().Be("MetronomeMark");
        define.LilyContext.Should().Be("Score");
    }

    [Fact]
    public void a_plain_note_answers_the_placeholder()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("\\relative c' { c4 d e f }\n");
        DefineOffset define = new DefineOffset(document);

        //Act
        string grob = define.GetCurrentLilyObject(document.Text.IndexOf("c4"));

        //Assert
        grob.Should().Be("still testing!");
    }

    [Fact]
    public void the_override_carries_two_decimal_places_and_the_grob()
    {
        //Arrange
        DefineOffset define = new DefineOffset(ToolDocument.Open("c4"));
        define.ItemToObject(new Markup());

        //Act
        string command = define.CreateOffsetOverride(1.5, -2.25);

        //Assert
        command.Should().Be(
            "\\once \\override TextScript.extra-offset = #'(1.50 . -2.25)");
    }

    [Fact]
    public void a_grob_with_a_context_is_written_as_context_dot_grob()
    {
        //Arrange
        DefineOffset define = new DefineOffset(ToolDocument.Open("c4"));
        define.ItemToObject(new Tempo());

        //Act
        string command = define.CreateOffsetOverride(0, 3);

        //Assert
        command.Should().Be(
            "\\once \\override Score.MetronomeMark.extra-offset = #'(0.00 . 3.00)");
    }

    [Fact]
    public void the_override_is_written_in_the_invariant_culture()
    {
        //Arrange — standing rule 7: a German locale must not write 1,50.
        CultureInfo previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            DefineOffset define = new DefineOffset(ToolDocument.Open("c4"));
            define.ItemToObject(new Markup());

            //Act
            string command = define.CreateOffsetOverride(1.5, 2.5);

            //Assert
            command.Should().Contain("(1.50 . 2.50)");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void the_override_goes_on_a_line_of_its_own_in_front_of_the_object()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' {\n  c4^\\markup { hi } d e f\n}\n");
        DefineOffset define = new DefineOffset(document);
        define.GetCurrentLilyObject(document.Text.IndexOf('^'));

        //Act
        define.InsertOverride(1, 2);

        //Assert — and the reformat pass put it at the indent of its neighbours.
        document.Text.Should().Be(
            "\\relative c' {\n"
            + "  \\once \\override TextScript.extra-offset = #'(1.00 . 2.00)\n"
            + "  c4^\\markup { hi } d e f\n"
            + "}\n");
    }

    [Fact]
    public void nothing_is_written_before_an_object_has_been_chosen()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("\\relative c' { c4 d e f }\n");
        DefineOffset define = new DefineOffset(document);
        string before = document.Text;

        //Act
        define.InsertOverride(1, 2);

        //Assert
        document.Text.Should().Be(before);
    }

    [Fact]
    public void the_one_renamed_music_item_is_looked_up_under_upstreams_own_name()
    {
        //Assert — python-ly's `String' is `StringItem' here, and upstream's
        //table is keyed by python-ly's names.
        DefineOffset.UpstreamName(new StringItem()).Should().Be("String");
        DefineOffset.UpstreamName(new Markup()).Should().Be("Markup");
        DefineOffset.UpstreamName(null).Should().BeNull();
    }
}
