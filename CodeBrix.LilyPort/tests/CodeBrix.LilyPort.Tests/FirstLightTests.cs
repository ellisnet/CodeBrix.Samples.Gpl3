// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// FIRST LIGHT: the whole engine running together on `{ c'4 }`-equivalent music.
/// <para>
/// Every other test in the port exercises one layer. This one goes music tree →
/// iterators → stream events → contexts → engravers → paper columns → one system →
/// grob stencils → the music font → the SVG backend, and fails if any single link is
/// missing. That is the point: the layers were all green individually long before they
/// were green together.
/// </para>
/// <para>
/// It is deliberately NOT a comparison against LilyPond's own output. Spacing needs
/// <c>Spacing_spanner</c>, which is not ported, so the columns are all still at the
/// origin — the glyphs are right and their horizontal places are not. The regression
/// comparator grades exactly that distinction, and reaching it is what first light is
/// for. See <see cref="EngraveResult.MissingTranslators"/>.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class FirstLightTests
{
    private static readonly object LoadGate = new object();

    private static Interpreter _interpreter;

    private static Interpreter Loaded()
    {
        lock (LoadGate)
        {
            if (_interpreter == null || !ReferenceEquals(LilyPondScheme.Current, _interpreter))
            {
                Interpreter interpreter = null;
                Interpreter.RunWithLargeStack(() =>
                {
                    interpreter = LilyPondScheme.CreateInterpreter();
                    LilyPondScheme.LoadViaLilyScm(interpreter);
                });

                _interpreter = interpreter;
            }

            return _interpreter;
        }
    }

    private static object Eval(string source)
    {
        Interpreter interpreter = Loaded();
        object result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            foreach (object form in SchemeReader.ReadAll(source, "<first-light>"))
            {
                result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }
        });

        return result;
    }

    /// <summary>Builds `{ c'4 }` through the real Scheme layer, as LilyPond's own parser would.</summary>
    private static MusicObject QuarterNoteC()
    {
        Eval(@"(define first-light-music
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 0 0)))))");
        return (MusicObject)Eval("first-light-music");
    }

    private static EngraveResult EngraveQuarterNote()
    {
        MusicObject music = QuarterNoteC();
        EngraveResult result = null;
        Interpreter.RunWithLargeStack(() => result = LilyPortEngraver.Engrave(music));
        return result;
    }

    [Fact]
    public void engraving_a_quarter_note_produces_a_system_with_grobs_on_it()
    {
        //Arrange / Act
        EngraveResult result = EngraveQuarterNote();

        //Assert
        // A system at all means Score_engraver ran its initialize and the paper score
        // exists. Zero-length music would have made Iterate decide there was nothing to
        // do and return before any of this -- silently, which is the failure mode the
        // Track T session recorded.
        result.System.Should().NotBeNull();
        result.PaperScore.Should().NotBeNull();
        result.PaperScore.RootSystem.Should().BeSameAs(result.System);
        result.System.ElementCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void the_four_engravers_each_put_their_grob_on_the_system()
    {
        //Arrange / Act
        EngraveResult result = EngraveQuarterNote();

        //Assert
        // One staff symbol, one clef, one note head, one vertical axis group. If any of
        // them is missing, the engraver that makes it never heard its cue.
        List<string> names = new List<string>();
        foreach (Grob grob in result.System.AllElements)
        {
            names.Add(grob.Name);
        }

        names.Should().Contain("StaffSymbol");
        names.Should().Contain("Clef");
        names.Should().Contain("NoteHead");
        names.Should().Contain("VerticalAxisGroup");
    }

    [Fact]
    public void paper_columns_are_created_in_pairs_and_bound_the_system()
    {
        //Arrange / Act
        EngraveResult result = EngraveQuarterNote();

        //Assert
        // Paper_column_engraver makes a non-musical and a musical column per timestep,
        // and the system is bounded by the first and last non-musical ones.
        result.System.Columns.Count.Should().BeGreaterThanOrEqualTo(2);
        result.System.GetBound(CodeBrix.LilyPort.Flower.Direction.Negative).Should().NotBeNull();
        result.System.GetBound(CodeBrix.LilyPort.Flower.Direction.Positive).Should().NotBeNull();
    }

    [Fact]
    public void the_note_head_resolves_a_glyph_out_of_the_real_emmentaler()
    {
        //Arrange / Act
        EngraveResult result = EngraveQuarterNote();

        //Assert
        // noteheads.s2 is the quarter-note head. Getting here means the font was found,
        // its LILC metadata was evaluated by LilyScheme, and select-head-glyph in the
        // vendored output-lib.scm resolved the style and duration-log to a suffix.
        Grob head = FindGrob(result, "NoteHead");
        head.Should().NotBeNull();

        object glyphName = head.GetProperty("glyph-name");
        glyphName.Should().BeOfType<MutableString>();
        glyphName.ToString().Should().Contain("noteheads.s2");
    }

    [Fact]
    public void the_clef_resolves_the_treble_glyph()
    {
        //Arrange / Act
        EngraveResult result = EngraveQuarterNote();

        //Assert
        Grob clef = FindGrob(result, "Clef");
        clef.Should().NotBeNull();
        clef.GetProperty("glyph-name").ToString().Should().Contain("clefs.G");
    }

    [Fact]
    public void the_staff_symbol_draws_five_lines()
    {
        //Arrange / Act
        EngraveResult result = EngraveQuarterNote();

        //Assert
        // line-positions comes from ly:staff-symbol::calc-line-positions, and the
        // stencil is one horizontal line per position.
        Grob staff = FindGrob(result, "StaffSymbol");
        staff.Should().NotBeNull();
        Pair.ToList(staff.GetProperty("line-positions")).Count.Should().Be(5);

        CodeBrix.LilyPort.Engine.Layout.Stencil? stencil = staff.GetStencil();
        stencil.HasValue.Should().BeTrue();
        stencil.Value.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void the_system_renders_to_an_svg_document_naming_its_glyphs()
    {
        //Arrange
        MusicObject music = QuarterNoteC();

        //Act
        string svg = null;
        Interpreter.RunWithLargeStack(() => svg = LilyPortEngraver.EngraveToSvg(music));

        //Assert
        // The end of the pipeline. The <use> references are what the regression
        // comparator counts as the glyph inventory, so their presence is exactly the
        // signal that makes the harness usable against the port.
        svg.Should().StartWith("<?xml version=\"1.0\"");
        svg.Should().Contain("<svg");
        svg.Should().Contain("xlink:href=\"#noteheads.s2\"");
        svg.Should().Contain("xlink:href=\"#clefs.G\"");

        // Staff lines are DRAWN rather than referenced: Lookup::horizontal_line emits a
        // draw-line, which the SVG backend writes as a <line>. So the five staff lines
        // are five <line> elements, not five glyph references.
        svg.Should().Contain("<line ");
    }

    [Fact]
    public void the_stand_in_context_tree_names_what_it_is_missing()
    {
        //Arrange / Act / Assert
        // The factory in LilyPortEngraver stands in for ly/engraver-init.ly. Naming the
        // absent translators rather than merely omitting them is what lets a comparison
        // against real LilyPond tell a missing feature from a wrong one.
        EngraveResult.MissingTranslators.Should().Contain("Spacing_engraver");
        EngraveResult.MissingTranslators.Should().Contain("Bar_engraver");
        EngraveResult.MissingTranslators.Should().Contain("Stem_engraver");
    }

    private static Grob FindGrob(EngraveResult result, string name)
    {
        foreach (Grob grob in result.System.AllElements)
        {
            if (string.Equals(grob.Name, name, StringComparison.Ordinal))
            {
                return grob;
            }
        }

        return null;
    }
}
