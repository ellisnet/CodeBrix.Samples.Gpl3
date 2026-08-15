// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Builds a <c>glyph-string</c> over named glyphs of one font, the way
    /// <c>Text_interface::interpret_string</c> does for a music-font run: one
    /// <c>(width (down . up) x-offset y-offset index name)</c> per glyph, in order, with
    /// the width taken from the font's own advance.
    /// </summary>
    /// <param name="font">The font metric.</param>
    /// <param name="names">The glyph names, in run order.</param>
    /// <returns>The expression.</returns>
    private static object GlyphString(FontMetric font, params string[] names)
    {
        List<object> descriptions = new List<object>(names.Length);

        for (int i = 0; i < names.Length; i++)
        {
            int index = font.NameToIndex(names[i]);
            Interval height = font.GetIndexedCharDimensions(index)[Axis.Y];

            descriptions.Add(Pair.List(
                Advance(font, names, i),
                new Pair(height.Left, height.Right),
                0.0,
                0.0,
                (long)index,
                new MutableString(names[i])));
        }

        return Pair.List(
            Symbol.Intern("glyph-string"),
            font,
            new MutableString(font.FontName),
            font.FontScaling,
            false,
            Pair.ListFrom(descriptions),
            new MutableString(string.Empty),
            0L,
            new MutableString(string.Concat(names)),
            false);
    }

    /// <summary>
    /// The advance the font reports for one glyph of a run, kern to the next included —
    /// which is the quantity upstream's Pango geometry carries as the glyph's width.
    /// </summary>
    /// <param name="font">The font metric.</param>
    /// <param name="names">The whole run.</param>
    /// <param name="position">The glyph's position in the run.</param>
    /// <returns>The advance.</returns>
    private static double Advance(FontMetric font, string[] names, int position)
    {
        int index = font.NameToIndex(names[position]);
        double advance = font.IndexedAdvance(index);

        if (position + 1 < names.Length)
        {
            advance += font.IndexedKerning(index, font.NameToIndex(names[position + 1]));
        }

        return advance;
    }

    [Fact]
    public void a_multi_glyph_run_places_each_glyph_by_the_cumulative_advance()
    {
        //Arrange
        // Three glyphs, because two cannot tell a CUMULATIVE advance from a per-glyph
        // one: the third's placement is only right if the first two were both added.
        FontMetric font = Emmentaler(1.0 / 1.7572990);
        string[] names = { "one", "two", "three" };

        Stencil run = new Stencil(
            GlyphString(font, names), new Interval(0, 3), new Interval(0, 1));

        //Act
        string fragment = new SvgBackend().RenderFragment(run);
        MatchCollection transforms = Regex.Matches(fragment, "<path transform=\"([^\"]*)\"");

        //Assert
        transforms.Count.Should().Be(3);

        // output-svg.scm's dump-path writes the compound form only when the placement is
        // non-zero, so the FIRST glyph of a run is written exactly as a lone glyph is.
        // The oracle's own repeat-volta-body-empty.svg opens every one of its runs that
        // way, which is where the shape was read from (rule 35).
        transforms[0].Groups[1].Value.Should().Be("scale(0.0040, -0.0040)");

        // The second and third carry the placement on the PATH, not on a wrapper, and the
        // third's is the SUM of the two advances before it — the `next-horiz-adv' global,
        // whose comment says it accumulates "only if there is more than one glyph".
        double second = Advance(font, names, 0);
        double third = second + Advance(font, names, 1);

        transforms[1].Groups[1].Value.Should().Be(
            "translate(" + second.ToString("F4", CultureInfo.InvariantCulture)
            + ", 0.0000) scale(0.0040, -0.0040)");
        transforms[2].Groups[1].Value.Should().Be(
            "translate(" + third.ToString("F4", CultureInfo.InvariantCulture)
            + ", 0.0000) scale(0.0040, -0.0040)");

        // Cumulative, not per-glyph: the two must differ, or the assertion above would
        // pass on a backend that never accumulated anything.
        third.Should().NotBe(second);
    }

    [Fact]
    public void a_single_glyph_run_is_written_bare_and_a_longer_one_is_wrapped()
    {
        //Arrange
        FontMetric font = Emmentaler(1.0 / 1.7572990);

        Stencil Run(params string[] names) => new Stencil(
            GlyphString(font, names), new Interval(0, 3), new Interval(0, 1));

        //Act
        string one = new SvgBackend().RenderFragment(Run("one"));
        string two = new SvgBackend().RenderFragment(Run("one", "two"));

        //Assert
        // `(if (= 1 (length w-hd-x-y-g-gn)) ...)': a run of one gets no group of its own,
        // and a run of more than one gets an attribute-less <g> around the paths.
        one.Should().NotContain("<g>\n");
        two.Should().Contain("<g>\n");

        // The CONTROL that makes the first half mean something: the single-glyph run is
        // the same glyph, drawn the same way, and it is the wrapper alone that differs.
        one.Should().Contain("<path transform=\"scale(0.0040, -0.0040)\"");
        two.Should().Contain("<path transform=\"scale(0.0040, -0.0040)\"");
    }

    [Fact]
    public void a_glyph_with_no_outline_still_advances_the_run()
    {
        //Arrange
        // `space' is Emmentaler's own zero-outline glyph, and it is upstream's
        // "glyph-strings without path data" branch: nothing is drawn and next-horiz-adv
        // moves on regardless.
        FontMetric font = Emmentaler(1.0 / 1.7572990);
        string[] names = { "one", "space", "two" };

        Stencil run = new Stencil(
            GlyphString(font, names), new Interval(0, 3), new Interval(0, 1));

        //Act
        string fragment = new SvgBackend().RenderFragment(run);
        MatchCollection transforms = Regex.Matches(fragment, "<path transform=\"([^\"]*)\"");

        //Assert
        // Two paths for three glyphs.
        transforms.Count.Should().Be(2);

        // And the surviving glyph sits PAST the space, not where the space was — which is
        // the whole reason a glyph that draws nothing stays in the list.
        double past = Advance(font, names, 0) + Advance(font, names, 1);
        transforms[1].Groups[1].Value.Should().Be(
            "translate(" + past.ToString("F4", CultureInfo.InvariantCulture)
            + ", 0.0000) scale(0.0040, -0.0040)");

        // The control: dropping the space would put it at the first advance alone.
        past.Should().NotBe(Advance(font, names, 0));
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
