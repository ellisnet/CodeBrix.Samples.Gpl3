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
using CodeBrix.LilyPort.Flower;
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
        // The end of the pipeline. EPG13 replaced a stand-in here: the backend used to
        // write <use xlink:href="#noteheads.s2"/>, which named the glyph and drew
        // nothing. LilyPond emits no <use> elements at all — each glyph is its own
        // outline, scaled by the drawing size over the units per em — so that is what
        // the comparator counts as the glyph inventory and what has to appear.
        svg.Should().StartWith("<?xml version=\"1.0\"");
        svg.Should().Contain("<svg");
        svg.Should().NotContain("xlink:href=");

        // The note head and the treble clef, by their own outlines. Taken from the
        // shipped SVG font, which is where upstream's SVG backend takes them from.
        svg.Should().Contain(
            "d=\"M0 -46c0 91 116 182 217 182c63 0 109 -35 109 -90"
            + "c0 -87 -110 -182 -220 -182c-67 0 -106 39 -106 90z\"");
        svg.Should().Contain("<path transform=\"scale(");

        // Staff lines are DRAWN rather than referenced: Lookup::horizontal_line emits a
        // draw-line, which the SVG backend writes as a <line>. So the five staff lines
        // are five <line> elements, not five glyph references.
        svg.Should().Contain("<line ");
    }

    [Fact]
    public void the_real_context_tree_names_the_translators_it_cannot_make()
    {
        //Arrange
        // The tree is built from ly/engraver-init.ly's own definitions now, so the
        // \consists lists name every translator upstream has and the port answers for
        // the ones it has ported. Naming the rest rather than merely omitting them is
        // what lets a comparison against real LilyPond tell a missing feature from a
        // wrong one, and it is gate G4's measurement.
        EngraveQuarterNote();

        //Act
        IReadOnlyList<string> missing = EngraveResult.MissingTranslators();

        //Assert
        // Beam_engraver was the example here until EPG10 ported it, then Tie_engraver
        // until EPG11 did, then Font_size_engraver until EPG14 did.
        // Spanner_break_forbid_engraver stands in their place -- it is EPG15's, and it
        // is what now leads the sweep's unported-translator demand list at 3,143 misses.
        // When EPG15 lands, pick another.
        missing.Should().Contain("Spanner_break_forbid_engraver");
        missing.Should().NotContain("Beam_engraver");
        missing.Should().NotContain("Tie_engraver");

        // EPG14's, all fifteen of them, on the ported side of the fence now.
        missing.Should().NotContain("Font_size_engraver");
        missing.Should().NotContain("Script_engraver");
        missing.Should().NotContain("Dynamic_engraver");
        missing.Should().NotContain("Text_engraver");
        missing.Should().NotContain("Instrument_name_engraver");
        missing.Should().NotContain("Ledger_line_engraver");

        // ...and the ported ones are NOT in it, which is the half that can rot silently.
        missing.Should().NotContain("Clef_engraver");
        missing.Should().NotContain("Note_heads_engraver");
        missing.Should().NotContain("Staff_symbol_engraver");

        // EPG4's three. Spacing_engraver was on the missing side of this fence until
        // 2026-08-05, which is exactly what the fence is for.
        missing.Should().NotContain("Spacing_engraver");
        missing.Should().NotContain("Note_spacing_engraver");
        missing.Should().NotContain("Separating_line_group_engraver");

        // Wave A moved these across on 2026-08-07 (both were on the Contain side,
        // which is exactly what the fence is for; Beam and Tie stand in as the
        // still-missing sentinels above until EPG10/EPG11).
        missing.Should().NotContain("Stem_engraver");
        missing.Should().NotContain("Bar_engraver");
        missing.Should().NotContain("Rest_engraver");
        missing.Should().NotContain("Timing_translator");
        missing.Should().NotContain("Staff_collecting_engraver");
        missing.Should().NotContain("Accidental_engraver");

        // EPG11 and EPG12 moved these across on 2026-08-08.
        missing.Should().NotContain("Laissez_vibrer_engraver");
        missing.Should().NotContain("Repeat_tie_engraver");
        missing.Should().NotContain("Slur_engraver");
        missing.Should().NotContain("Phrasing_slur_engraver");
    }

    [Fact]
    public void a_note_head_has_a_real_horizontal_extent()
    {
        //Arrange
        // EPG11/EPG12's headline finding, fenced. lily/grob.cc's constructor installs a
        // DEFAULT X-extent callback on any grob whose description does not name one, and
        // the port's constructor had never done so. NoteHead is the one common grob that
        // does not name X-extent -- so until 2026-08-08 every note head in every score
        // answered an EMPTY width, and the empty answer was cached the first time
        // anything asked.
        //
        // Nothing asked for eleven groups: spacing measures note COLUMNS, beams measure
        // stems. Tie_formatting_problem is the first code in the engine to ask a note
        // head for its width, and it feeds the answer into a Skyline, where an empty
        // interval becomes a NaN roof height and the page dies. Asserted here rather than
        // among the tie tests because it is not a tie fact.
        EngraveResult result = EngraveQuarterNote();

        //Act
        Grob head = FindGrob(result, "NoteHead");

        //Assert
        head.Should().NotBeNull();
        head.Extent(head, Axis.X).IsEmpty.Should().BeFalse();
        head.Extent(head, Axis.X).Length.Should().BeGreaterThan(0.0);

        // ...and the Y half, which was already working because NoteHead names Y-extent
        // explicitly, so this is the control that says the X assertion means something.
        head.Extent(head, Axis.Y).IsEmpty.Should().BeFalse();
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
