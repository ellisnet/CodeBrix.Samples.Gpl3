// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The Emmentaler metadata reader, run against the fonts this repo builds and ships.
/// <para>
/// The expected values are read from the font's OWN global table where possible —
/// <c>black_notehead_width</c> against the measured width of <c>noteheads.s2</c>, for
/// instance — so the test checks the whole chain (sfnt directory, zlib, Scheme
/// evaluation, unit scaling) against a figure the font states about itself.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class OpenTypeFontTests
{
    private static string FontPath(string name)
        => Path.Combine(AppContext.BaseDirectory, "TestFonts", name);

    private static Interpreter CoreInterpreter()
    {
        // Only the LilyScheme core is needed to evaluate a quoted alist; the LilyPond
        // Scheme layer is irrelevant here and loading it would cost 20 seconds.
        Interpreter interpreter = new Interpreter();
        SchemeBootstrap.LoadCore(interpreter);
        return interpreter;
    }

    [Fact]
    public void the_shipped_emmentaler_carries_both_lilypond_tables()
    {
        //Arrange
        SfntReader reader = SfntReader.FromFile(FontPath("emmentaler-20.otf"));

        //Act
        bool hasCharacterTable = reader.HasTable("LILC");

        //Assert
        hasCharacterTable.Should().BeTrue();
        reader.HasTable("LILY").Should().BeTrue();
        reader.HasTable("CFF ").Should().BeTrue();
    }

    [Fact]
    public void the_design_units_per_em_come_from_the_head_table()
    {
        //Arrange
        SfntReader reader = SfntReader.FromFile(FontPath("emmentaler-20.otf"));

        //Act
        int unitsPerEm = reader.UnitsPerEm;

        //Assert
        unitsPerEm.Should().Be(1000);
    }

    [Fact]
    public void glyph_names_come_from_the_cff_charset()
    {
        //Arrange
        // The post table is format 3.0 and carries no names at all, so a reader that
        // looked there would find nothing. This is master plan section 11 correction 2.
        SfntReader reader = SfntReader.FromFile(FontPath("emmentaler-20.otf"));

        //Act
        List<string> names = reader.ReadCffGlyphNames();

        //Assert
        names.Count.Should().Be(668);
        names[0].Should().Be(".notdef");
        names.Should().Contain("noteheads.s2");
        names.Should().Contain("clefs.G");
        names.Should().Contain("rests.2");
    }

    [Fact]
    public void the_brace_font_has_its_own_smaller_glyph_inventory()
    {
        //Arrange
        SfntReader reader = SfntReader.FromFile(FontPath("emmentaler-brace.otf"));

        //Act
        List<string> names = reader.ReadCffGlyphNames();

        //Assert
        names.Count.Should().Be(577);
    }

    [Fact]
    public void the_character_table_is_zlib_compressed_and_the_global_table_is_not()
    {
        //Arrange
        // Master plan section 11 correction 1: only LILC is compressed. A reader that
        // demanded inflation on both would fail on every font.
        SfntReader reader = SfntReader.FromFile(FontPath("emmentaler-20.otf"));
        byte[] characterTable = reader.GetTable("LILC");
        byte[] globalTable = reader.GetTable("LILY");

        //Act
        string characters = OpenTypeFont.DecodeTable(characterTable);
        string globals = OpenTypeFont.DecodeTable(globalTable);

        //Assert
        characters.Length.Should().BeGreaterThan(characterTable.Length);
        globals.Length.Should().Be(globalTable.Length);
        globals.Should().Contain("design_size");
    }

    [Fact]
    public void the_metadata_tables_are_evaluated_into_alists()
    {
        //Arrange
        // Master plan section 11 correction 3: the tables are scm_eval_string'd, not
        // merely parsed. This is why LilyScheme is needed to load the music font.
        Interpreter interpreter = CoreInterpreter();

        //Act
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), interpreter);

        //Assert
        font.GlobalTable.Count.Should().BeGreaterThan(5);
        font.CharacterTable.Count.Should().BeGreaterThan(600);
        font.DesignSize.Should().Be(20.0);
    }

    [Fact]
    public void a_glyph_index_can_be_looked_up_from_its_name()
    {
        //Arrange
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), CoreInterpreter());

        //Act
        int index = font.NameToIndex("noteheads.s2");

        //Assert
        index.Should().BeGreaterThan(0);
        font.GlyphNames[index].Should().Be("noteheads.s2");
        font.NameToIndex("no-such-glyph").Should().Be(OpenTypeFont.GlyphIndexInvalid);
    }

    [Fact]
    public void the_note_head_width_matches_what_the_font_says_about_itself()
    {
        //Arrange
        // The font's own LILY table records black_notehead_width in staff spaces.
        // The measured LILC bbox, converted out of the port's millimetre units, has
        // to agree — which exercises the whole chain at once.
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), CoreInterpreter());
        double declared = ReadGlobal(font, "black_notehead_width");

        //Act
        Box box = font.GetGlyphDimensions("noteheads.s2");
        double measured = box.X.Length / Dimensions.Point;

        //Assert
        measured.Should().BeApproximately(declared, 1e-4);
    }

    [Fact]
    public void a_glyph_bounding_box_is_read_from_the_metadata_not_the_outline()
    {
        //Arrange
        // Upstream reads dimensions from LILC, never from the outline. That is what
        // makes this repo's own font build usable even though its outline bounding
        // boxes differ from the official release on about 18% of glyphs.
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), CoreInterpreter());

        //Act
        Box clef = font.GetGlyphDimensions("clefs.G");

        //Assert
        clef.X.Length.Should().BeGreaterThan(0.0);

        // A G clef reaches well above the staff and below it.
        clef.Y.Right.Should().BeGreaterThan(0.0);
        clef.Y.Left.Should().BeLessThan(0.0);
    }

    [Fact]
    public void an_unknown_glyph_has_empty_dimensions()
    {
        //Arrange
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), CoreInterpreter());

        //Act
        Box box = font.GetGlyphDimensions("no-such-glyph");

        //Assert
        box.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void a_note_head_records_where_its_stem_attaches()
    {
        //Arrange
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), CoreInterpreter());

        //Act
        Offset up = font.AttachmentPoint("noteheads.s2", Direction.Positive, out bool rotate);

        //Assert
        rotate.Should().BeFalse();

        // The up stem attaches at the note head's right edge, part way up.
        up.X.Should().BeApproximately(font.GetGlyphDimensions("noteheads.s2").X.Right, 1e-9);
        up.Y.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void a_down_stem_reads_the_separate_attachment_down_point()
    {
        //Arrange
        // This Emmentaler carries attachment-down, so the rotation fallback is NOT
        // used. The fallback exists for fonts predating SMuFL compliance; asserting
        // it here would have pinned the wrong branch.
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), CoreInterpreter());
        Offset up = font.AttachmentPoint("noteheads.s2", Direction.Positive, out _);

        //Act
        Offset down = font.AttachmentPoint("noteheads.s2", Direction.Negative, out bool rotate);

        //Assert
        rotate.Should().BeFalse();

        // The down stem attaches at the left edge, mirrored vertically.
        down.X.Should().Be(0.0);
        down.Y.Should().BeApproximately(-up.Y, 1e-9);
    }

    [Fact]
    public void the_rotation_fallback_fires_when_a_glyph_has_no_attachment_down()
    {
        //Arrange
        // Glyphs that are not note heads carry no attachment at all, which is the
        // path that reports back "you must rotate".
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), CoreInterpreter());

        //Act
        Offset point = font.AttachmentPoint("clefs.G", Direction.Negative, out bool rotate);

        //Assert
        rotate.Should().BeTrue();
        point.Should().Be(Offset.Zero);
    }

    [Fact]
    public void indexed_glyph_dimensions_agree_with_named_ones()
    {
        //Arrange
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), CoreInterpreter());
        int index = font.NameToIndex("noteheads.s2");

        //Act
        Box byIndex = font.GetIndexedGlyphDimensions(index);

        //Assert
        byIndex.Should().Be(font.GetGlyphDimensions("noteheads.s2"));
    }

    [Fact]
    public void a_font_can_be_read_without_an_interpreter_for_names_alone()
    {
        //Arrange
        // No interpreter means the tables cannot be evaluated. That limitation is
        // explicit rather than silent: names still work, metadata is simply empty.
        OpenTypeFont font = new OpenTypeFont(FontPath("emmentaler-20.otf"), null);

        //Act
        int names = font.GlyphNames.Count;
        int glyphs = font.GlyphCount;

        //Assert
        //was previously: font.GlyphCount.Should().Be(668);
        // RESTATED at PARITY 6 (rule 33). The old assertion used GlyphCount to stand for
        // "the names were read", and its 668 was RECORDED FROM THE PORT — the CFF charset
        // size, .notdef included. GlyphCount is now upstream's Open_type_font::count,
        // which answers index_to_charcode_map_.size (), so the two are different numbers
        // and each is asserted for what it actually means.
        names.Should().Be(668);

        // Read off the ORACLE (rule 35): pinned LilyPond 2.27.2 under the corpus's font
        // pinning answers `ly:otf-glyph-count' 667 for fetaMusic and 576 for fetaBraces.
        // The one-glyph gap between the two numbers here is .notdef, which sits at index
        // 0 and carries no charcode.
        glyphs.Should().Be(667);
        (names - glyphs).Should().Be(1);

        font.CharacterTable.Count.Should().Be(0);
    }

    private static double ReadGlobal(OpenTypeFont font, string name)
    {
        object value = font.GlobalTable[CodeBrix.LilyScheme.Values.Symbol.Intern(name)];
        return value switch
        {
            double d => d,
            long l => l,
            int i => i,
            _ => throw new InvalidOperationException("Unexpected global table value: " + value),
        };
    }
}
