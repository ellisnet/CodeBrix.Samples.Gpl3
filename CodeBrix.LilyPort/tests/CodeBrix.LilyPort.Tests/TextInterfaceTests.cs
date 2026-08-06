// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Backends;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// The text half of EPG13, end to end: a markup string, the font the property chain
/// selects for it, the stencil it produces, and the SVG the backend writes.
/// <para>
/// This chain cannot yet be observed through the regression suite. Text inside a score
/// needs <c>Text_engraver</c> (EPG14) to make a grob to hang it on, and the tagline at
/// the foot of every reference page needs page layout (EPG16); until those land, a
/// swept file shows no text whether the text machinery works or not. So it is fenced
/// here directly.
/// </para>
/// <para>
/// The font-size assertion is the calibration that matters. Every reference page in the
/// suite carries the tagline at <c>font-size="2.2000"</c>, and that number is the whole
/// chain in one figure: <c>text-font-size</c> of 11, times the point constant, divided
/// by the paper's <c>output-scale</c>. Getting it wrong by a thousandth would make
/// every text element in the suite differ.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class TextInterfaceTests
{
    private static OutputDef Paper()
    {
        LilyPondInit.DefaultLayout();
        OutputDef paper = LilyPondInit.DefaultPaper();
        paper.Should().NotBeNull();
        return paper;
    }

    /// <summary>
    /// The property chain a real caller hands to a markup: one link, the layout's
    /// <c>property-defaults</c>.
    /// <para>
    /// Passing an EMPTY chain instead is not a smaller version of this — it is a
    /// different question. The <c>fonts</c> alist that maps <c>serif</c> to a family
    /// name lives in <c>property-defaults</c>, so without it every font request falls
    /// through to the "no entry for font family" fallback and reports the FontConfig
    /// alias rather than the CSS family the SVG backend expects.
    /// </para>
    /// </summary>
    /// <param name="paper">The layout.</param>
    /// <param name="overrides">Extra properties to put in front, as a flat alist.</param>
    /// <returns>The chain.</returns>
    private static object Props(OutputDef paper, params object[] overrides)
    {
        object defaults = paper.LookupVariable(Symbol.Intern("property-defaults"))
                          ?? Nil.Instance;

        return overrides.Length == 0
            ? Pair.List(defaults)
            : Pair.List(Pair.List(overrides), defaults);
    }

    [Fact]
    public void a_plain_string_markup_selects_a_text_font_and_becomes_a_utf8_stencil()
    {
        //Arrange
        OutputDef paper = Paper();

        //Act
        Stencil stencil = TextInterface.InterpretMarkup(
            paper, Props(paper), new MutableString("Allegro"));

        //Assert
        stencil.Expression.Should().BeOfType<Pair>();
        ((Symbol)((Pair)stencil.Expression).Car).Name.Should().Be("utf-8-string");
        stencil.XExtent.Length.Should().BeGreaterThan(0.0);
        stencil.YExtent.Length.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void the_default_text_font_is_serif_at_the_taglines_size()
    {
        //Arrange
        OutputDef paper = Paper();

        //Act
        FontMetric font = FontInterface.SelectFont(
            paper,
            Props(paper, new Pair(Symbol.Intern("font-encoding"), Symbol.Intern("latin1"))));

        //Assert
        font.Should().BeOfType<TextFontMetric>();
        TextFontMetric text = (TextFontMetric)font;
        text.Family.Should().Be("serif");
        text.Bold.Should().BeFalse();
        text.Italic.Should().BeFalse();
        text.Chain.Should().NotBeEmpty();
    }

    [Fact]
    public void the_svg_backend_writes_the_same_text_element_the_oracle_does()
    {
        //Arrange
        OutputDef paper = Paper();
        Stencil stencil = TextInterface.InterpretMarkup(
            paper, Props(paper), new MutableString("LilyPond v2.27.2"));
        SvgBackend backend = new SvgBackend
        {
            UnitLength = paper.GetDimension("output-scale"),
        };

        //Act
        string fragment = backend.RenderFragment(stencil);

        //Assert
        // Byte for byte what every reference page carries for its tagline.
        fragment.Should().Contain(
            "<text font-family=\"serif\" font-size=\"2.2000\""
            + " text-anchor=\"start\" fill=\"currentColor\">\n"
            + "<tspan>LilyPond v2.27.2</tspan>\n</text>");
        backend.UnhandledCommands.Should().BeEmpty();
    }

    [Fact]
    public void bold_and_italic_reach_the_element_as_pango_style_words()
    {
        //Arrange
        OutputDef paper = Paper();
        object props = Props(
            paper,
            new Pair(Symbol.Intern("font-encoding"), Symbol.Intern("latin1")),
            new Pair(Symbol.Intern("font-series"), Symbol.Intern("bold")),
            new Pair(Symbol.Intern("font-shape"), Symbol.Intern("italic")));

        //Act
        Stencil stencil = TextInterface.InterpretMarkup(paper, props, new MutableString("f"));
        string fragment = new SvgBackend
        {
            UnitLength = paper.GetDimension("output-scale"),
        }.RenderFragment(stencil);

        //Assert
        fragment.Should().Contain("font-weight=\"bold\"");
        fragment.Should().Contain("font-style=\"italic\"");
    }

    [Fact]
    public void markup_content_is_escaped_the_way_upstream_escapes_it()
    {
        //Arrange
        OutputDef paper = Paper();

        //Act
        Stencil stencil = TextInterface.InterpretMarkup(
            paper, Props(paper), new MutableString("a < b & c > d"));
        string fragment = new SvgBackend().RenderFragment(stencil);

        //Assert
        // & and < are replaced; a bare > is legal character data and upstream leaves it.
        fragment.Should().Contain("<tspan>a &lt; b &amp; c > d</tspan>");
    }

    [Fact]
    public void wider_text_reserves_more_room_than_narrower_text()
    {
        //Arrange
        OutputDef paper = Paper();

        //Act
        Stencil narrow = TextInterface.InterpretMarkup(
            paper, Props(paper), new MutableString("i"));
        Stencil wide = TextInterface.InterpretMarkup(
            paper, Props(paper), new MutableString("MMMMM"));

        //Assert
        wide.XExtent.Length.Should().BeGreaterThan(narrow.XExtent.Length);

        // The vertical extent is INK, so a letter with no ascender or descender is
        // shorter than one with both. That is the figure the engraver reserves room
        // from, and reading it off the logical rectangle instead would make every line
        // of text the same height.
        Stencil xHeight = TextInterface.InterpretMarkup(
            paper, Props(paper), new MutableString("x"));
        Stencil tall = TextInterface.InterpretMarkup(
            paper, Props(paper), new MutableString("Ry"));
        tall.YExtent.Length.Should().BeGreaterThan(xHeight.YExtent.Length);
    }

    [Fact]
    public void every_whitespace_character_becomes_a_plain_space()
    {
        //Arrange
        string input = "a\tb\nc";

        //Act
        string cleaned = TextInterface.NormalizeWhitespace(input);

        //Assert
        cleaned.Should().Be("a b c");
    }

    [Fact]
    public void the_d23_chain_reaches_the_vendored_urw_face_first()
    {
        //Arrange
        //Act
        System.Collections.Generic.IReadOnlyList<TextFace> chain
            = TextFontChain.For("serif", false, false);

        //Assert
        chain.Should().NotBeEmpty();
        chain[0].FileName.Should().Be("C059-Roman.otf");

        // ... and the TeX Gyre fallback behind it, exactly as upstream's
        // 00-lilypond-fonts.conf orders them. Nothing beyond: no DejaVu, no system font.
        chain.Count.Should().Be(2);
        chain[1].FileName.Should().Be("texgyreschola-regular.otf");
    }
}
