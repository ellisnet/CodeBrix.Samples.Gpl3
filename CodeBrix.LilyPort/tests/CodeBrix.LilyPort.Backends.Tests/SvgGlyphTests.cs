// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Backends;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Backends.Tests;

/// <summary>
/// Music glyphs, from the font to the <c>path</c> element.
/// <para>
/// EPG13 replaced a stand-in here. The backend used to write
/// <c>&lt;use xlink:href="#noteheads.s2"/&gt;</c>, which kept the glyph NAME visible
/// but drew nothing, and no reference page contains such an element — upstream's SVG
/// backend writes each glyph's outline inline, lifted out of the shipped
/// <c>.svg</c> font and scaled by the drawing size over the units per em.
/// </para>
/// <para>
/// The expected string below is not a guess about that format: it is what the oracle
/// writes for the same glyph, taken from the committed reference pages.
/// </para>
/// </summary>
public class SvgGlyphTests
{
    /// <summary>
    /// Loads the music font with an interpreter of its own.
    /// <para>
    /// The interpreter is not optional and not incidental. A font's design size lives
    /// in its <c>LILY</c> table, the table is Scheme source that has to be EVALUATED,
    /// and a font loaded without an interpreter reports a design size of 1 instead of
    /// 20 — which does not fail, it just draws every glyph at a twentieth of its size.
    /// Going through <see cref="AllFontMetrics"/> would pick up whatever interpreter
    /// happened to be ambient, so this builds one.
    /// </para>
    /// </summary>
    /// <param name="magnification">The magnification to view the font at.</param>
    /// <returns>The font metric.</returns>
    private static FontMetric Emmentaler(double magnification = 1.0)
    {
        Interpreter interpreter = new Interpreter();
        SchemeBootstrap.LoadCore(interpreter);

        byte[] bytes = FontAssets.MusicFont("emmentaler-20");
        bytes.Should().NotBeNull();

        OpenTypeFontMetric font = new OpenTypeFontMetric(
            new OpenTypeFont(bytes, "emmentaler-20", interpreter), "emmentaler-20");

        return magnification == 1.0
            ? (FontMetric)font
            : new ModifiedFontMetric(font, magnification);
    }

    [Fact]
    public void a_named_glyph_is_written_as_its_own_outline()
    {
        //Arrange
        // The magnification a default 20-point staff produces: the font's design size
        // is 20 points, and one output unit is one staff space, so design size times
        // magnification comes to 4 and the drawing scale to 4/1000.
        FontMetric font = Emmentaler(1.0 / 1.7572990);
        Stencil glyph = new Stencil(
            Pair.List(
                Symbol.Intern("named-glyph"),
                font,
                new MutableString("noteheads.s2")),
            new Interval(0, 1),
            new Interval(0, 1));

        //Act
        string fragment = new SvgBackend().RenderFragment(glyph);

        //Assert
        // The interpreter wraps every command in its own translation, so the path sits
        // inside a group rather than at the start of the fragment.
        fragment.Should().Contain(
            "<path transform=\"scale(0.0040, -0.0040)\" d=\"M0 -46c0 91");
        fragment.Should().Contain("fill=\"currentColor\"/>");

        // The Y flip lives in the transform, NOT in the path data. Running the outline
        // through the backend's own coordinate negation as well would turn every note
        // head upside down while keeping the document perfectly well formed.
        fragment.Should().Contain("d=\"M0 -46c0 91 116 182 217 182c63 0 109 -35 109 -90"
            + "c0 -87 -110 -182 -220 -182c-67 0 -106 39 -106 90z\"");
    }

    [Fact]
    public void the_drawing_scale_follows_the_font_magnification()
    {
        //Arrange
        Stencil Glyph(FontMetric font) => new Stencil(
            Pair.List(Symbol.Intern("named-glyph"), font, new MutableString("noteheads.s2")),
            new Interval(0, 1),
            new Interval(0, 1));

        //Act
        string full = new SvgBackend().RenderFragment(Glyph(Emmentaler(1.0 / 1.7572990)));
        string half = new SvgBackend().RenderFragment(Glyph(Emmentaler(0.5 / 1.7572990)));

        //Assert
        full.Should().Contain("scale(0.0040, -0.0040)");
        half.Should().Contain("scale(0.0020, -0.0020)");

        // Same glyph, same path data — only the scale moves.
        full.Substring(full.IndexOf("d=\"")).Should()
            .Be(half.Substring(half.IndexOf("d=\"")));
    }

    [Fact]
    public void a_glyph_the_font_does_not_have_draws_nothing_at_all()
    {
        //Arrange
        Stencil glyph = new Stencil(
            Pair.List(
                Symbol.Intern("named-glyph"),
                Emmentaler(),
                new MutableString("no.such.glyph")),
            new Interval(0, 1),
            new Interval(0, 1));

        //Act
        SvgBackend backend = new SvgBackend();
        string fragment = backend.RenderFragment(glyph);

        //Assert
        fragment.Should().NotContain("<path");

        // Not understood is a different answer from nothing to draw, and only the
        // first should be reported.
        backend.UnhandledCommands.Should().BeEmpty();
    }

    [Fact]
    public void the_shipped_svg_font_covers_the_glyphs_the_otf_names()
    {
        //Arrange
        OpenTypeFont font = new OpenTypeFont(
            FontAssets.MusicFont("emmentaler-20"), "emmentaler-20", null);

        //Act
        int covered = 0;
        int missing = 0;
        foreach (string name in font.GlyphNames)
        {
            if (name == ".notdef")
            {
                continue;
            }

            if (font.Outlines.Outline(name) == null)
            {
                missing++;
            }
            else
            {
                covered++;
            }
        }

        //Assert
        covered.Should().BeGreaterThan(600);

        // A glyph the OTF names but the SVG font has no entry for would silently draw
        // nothing, which is the failure mode the old `use` stand-in hid.
        missing.Should().Be(0);
    }
}
