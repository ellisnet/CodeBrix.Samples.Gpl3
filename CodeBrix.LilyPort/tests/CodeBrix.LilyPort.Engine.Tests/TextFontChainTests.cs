// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Linq;
using CodeBrix.LilyPort.Engine.Fonts;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Which face a requested font family resolves to — ruling R14 (Jeremy, 2026-08-17).
/// <para>
/// Upstream asks fontconfig and never inspects a family name. Under the reference
/// corpus's own pinning (<c>reference-fonts.conf.in</c>, whose single <c>&lt;dir&gt;</c>
/// is the oracle's bundled font directory and which aliases the four generic names and
/// nothing else) that gives two rules, and both are MEASURED with <c>fc-match</c> rather
/// than assumed — a font configuration is an oracle too (rule 35a):
/// </para>
/// <code>
///     fc-match "serif"                            -> C059 Roman
///     fc-match "sans" / "sans-serif"              -> Nimbus Sans Regular
///     fc-match "monospace"                        -> Nimbus Mono PS Regular
///     fc-match "DejaVu Sans" / "Linux Libertine O" / "Arial" / "Foo Bar Baz"
///                                                 -> TeX Gyre Schola Regular
///     fc-match "Linux Libertine O,serif"          -> C059 Roman
///     fc-match "Linux Libertine Mono O,monospace" -> Nimbus Mono PS Regular
///     fc-match "DejaVu Sans:weight=bold"          -> TeX Gyre Schola Bold
/// </code>
/// <para>
/// ⚠ AND ONE GROUP OF NAMES fc-match CANNOT ANSWER FOR. LilyPond loads its own shipped
/// <c>fonts/00-lilypond-fonts.conf</c> into its process FcConfig (<c>lily/font-config.cc</c>),
/// so its three virtual families are aliased inside the oracle even though
/// <c>FONTCONFIG_FILE</c> replaced the system configuration. A shell <c>fc-match</c>
/// therefore MISREPORTS them — it answers TeX Gyre Schola for <c>"LilyPond Serif"</c>
/// where the oracle answers C059. Their expectations are read off that conf; see
/// <see cref="lilyponds_own_three_virtual_family_names_resolve_by_category"/>.
/// </para>
/// <para>
/// THE SECOND RULE IS WHY THE OBVIOUS IMPLEMENTATION IS WRONG. A family is a CSS family
/// LIST; fontconfig walks it and takes the first entry it can satisfy. Sending the whole
/// string to the unknown chain would put <c>kievan-notation</c>'s
/// <c>"Linux Libertine O,serif"</c> on Schola where the oracle reaches C059, so the
/// walk is load-bearing and <see cref="a_family_list_falls_through_to_the_generic_name_it_ends_with"/>
/// is its fence.
/// </para>
/// <para>
/// ⚠ EVERY CLAIM HERE IS PAIRED (rule 33): a fence that only checked the unmatched case
/// would pass with the whole family table collapsed onto one face, so each test also
/// asserts what must come out DIFFERENTLY.
/// </para>
/// </summary>
public class TextFontChainTests
{
    private const string Schola = "texgyreschola-regular.otf";
    private const string ScholaBold = "texgyreschola-bold.otf";
    private const string ScholaItalic = "texgyreschola-italic.otf";
    private const string ScholaBoldItalic = "texgyreschola-bolditalic.otf";
    private const string Serif = "C059-Roman.otf";
    private const string Sans = "NimbusSans-Regular.otf";
    private const string Mono = "NimbusMonoPS-Regular.otf";

    /// <summary>Returns the file names of the faces a request resolves to, in order.</summary>
    /// <param name="family">The family or family list.</param>
    /// <param name="bold">Whether bold was asked for.</param>
    /// <param name="italic">Whether italic was asked for.</param>
    /// <returns>The file names.</returns>
    private static List<string> Chain(string family, bool bold = false, bool italic = false)
        => TextFontChain.For(family, bold, italic).Select(face => face.FileName).ToList();

    /// <summary>Returns the first face a request resolves to.</summary>
    /// <param name="family">The family or family list.</param>
    /// <param name="bold">Whether bold was asked for.</param>
    /// <param name="italic">Whether italic was asked for.</param>
    /// <returns>The file name.</returns>
    private static string First(string family, bool bold = false, bool italic = false)
        => Chain(family, bold, italic).FirstOrDefault();

    [Fact]
    public void a_family_the_vendored_faces_do_not_provide_resolves_to_tex_gyre_schola()
    {
        //Arrange
        // The four unavailable families the seven R14 corpus rows actually name, plus two
        // names no font collection anywhere provides.
        string[] unavailable =
        {
            "DejaVu Sans", "DejaVu Serif", "DejaVu Sans Mono", "Linux Libertine O",
            "Arial", "Foo Bar Baz",
        };

        //Act
        List<string> resolved = unavailable.Select(name => First(name)).ToList();

        //Assert
        resolved.Should().AllBe(Schola);

        // THE CONTROL, and it is the whole point: a table collapsed onto one face would
        // satisfy the assertion above. The generic names must still answer their own
        // faces, and all four answers must be distinct from each other and from Schola.
        List<string> generics = new List<string> { First("serif"), First("sans"), First("monospace") };
        generics.Should().Equal(new List<string> { Serif, Sans, Mono });
        generics.Should().NotContain(Schola);
        generics.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void a_family_list_falls_through_to_the_generic_name_it_ends_with()
    {
        //Arrange
        // kievan-notation.ly's own three requests — the file this rule exists to keep at
        // MATCH (rule 35b: the regression file's material, not invented material).

        //Act
        string serifList = First("Linux Libertine O,serif");
        string sansList = First("Linux Biolinum O,sans-serif");
        string monoList = First("Linux Libertine Mono O,monospace");

        //Assert
        serifList.Should().Be(Serif);
        sansList.Should().Be(Sans);
        monoList.Should().Be(Mono);

        // THE CONTROL: the same leading name WITHOUT the generic entry behind it is
        // unknown and goes to Schola. That pair is what makes the walk load-bearing — if
        // the list were matched as one string, both halves of this test would read Schola.
        First("Linux Libertine O").Should().Be(Schola);
        First("Linux Libertine Mono O").Should().Be(Schola);
    }

    [Fact]
    public void a_name_that_merely_contains_a_generic_word_is_not_that_generic()
    {
        //Arrange
        // The retired heuristic's exact failures: "DejaVu Sans Mono" contains both "sans"
        // and "mono" and IS neither, and "Bitstream Vera Sans," contains "sans".

        //Act
        string monoish = First("DejaVu Sans Mono");
        string sansish = First("Bitstream Vera Sans,");

        //Assert
        monoish.Should().Be(Schola);
        monoish.Should().NotBe(Mono);
        monoish.Should().NotBe(Sans);
        sansish.Should().Be(Schola);
        sansish.Should().NotBe(Sans);

        // THE CONTROL: the generic names themselves are matched, so this is a statement
        // about EXACTNESS and not about the table having lost its sans and mono entries.
        First("monospace").Should().Be(Mono);
        First("sans").Should().Be(Sans);
        First("sans-serif").Should().Be(Sans);
    }

    [Fact]
    public void an_unmatched_family_keeps_the_style_it_asked_for()
    {
        //Arrange
        // markup-time-signatures asks for `font-name = "Bitstream Vera Sans, Bold"',
        // whose style word FontInterface.ParseDescription strips before this is reached —
        // so the bold face has to come out of the style index, not the name.

        //Act
        string regular = First("DejaVu Sans");
        string bold = First("DejaVu Sans", bold: true);
        string italic = First("DejaVu Sans", italic: true);
        string boldItalic = First("DejaVu Sans", bold: true, italic: true);

        //Assert
        regular.Should().Be(Schola);
        bold.Should().Be(ScholaBold);
        italic.Should().Be(ScholaItalic);
        boldItalic.Should().Be(ScholaBoldItalic);

        // THE CONTROL: four requests, four DIFFERENT files. A chain that ignored the
        // style index would pass every individual assertion above if they all named the
        // regular face, and this is what says they do not.
        new List<string> { regular, bold, italic, boldItalic }
            .Distinct().Should().HaveCount(4);
    }

    [Fact]
    public void the_unknown_chain_holds_one_face_where_a_generic_chain_holds_two()
    {
        //Arrange
        // D23's chain is URW face, then TeX Gyre face, then STOP. An unavailable family
        // has no URW face to start from: fontconfig answers ONE face for it, so the port
        // offers one rather than inventing a second level of coverage for a request
        // upstream cannot satisfy either.

        //Act
        List<string> unknown = Chain("DejaVu Sans");
        List<string> serif = Chain("serif");
        List<string> sans = Chain("sans");
        List<string> typewriter = Chain("monospace");

        //Assert
        unknown.Should().Equal(new List<string> { Schola });
        serif.Should().Equal(new List<string> { Serif, Schola });
        sans.Should().Equal(new List<string> { Sans, "texgyreheros-regular.otf" });
        typewriter.Should().Equal(new List<string> { Mono, "texgyrecursor-regular.otf" });

        // The serif chain's SECOND level is Schola too, which is worth stating: the
        // unknown chain is not a new face in the port, it is an existing vendored face
        // reached first instead of second. D23 is untouched — no system font, ever.
        serif.Should().Contain(Schola);
    }

    [Fact]
    public void lilyponds_own_three_virtual_family_names_resolve_by_category()
    {
        //Arrange
        // NOT the CSS generics and NOT unknown names: upstream's shipped
        // fonts/00-lilypond-fonts.conf aliases these three by category, and LilyPond
        // loads that conf into its own FcConfig at startup (lily/font-config.cc), so they
        // are aliased inside the oracle's process even under FONTCONFIG_FILE.
        //
        // ⚠ THE EXPECTATIONS COME OFF THAT CONF, NOT OFF fc-match, and the distinction is
        // not academic: a shell `fc-match "LilyPond Serif"' answers TeX Gyre Schola,
        // because the shell's fontconfig never sees the conf LilyPond adds. The conf's
        // prefer lists are C059 then TeX Gyre Schola, Nimbus Sans then TeX Gyre Heros,
        // and Nimbus Mono PS then TeX Gyre Cursor -- which is D23's chain face for face.
        //
        // markup-music-glyph.ly asks for "LilyPond Sans Serif" through font-name, which
        // is how these names reach the SVG backend at all: paper-defaults-init.ly's
        // backend switch never emits them, but an explicit font-name override bypasses it.

        //Act
        string serif = First("LilyPond Serif");
        string sans = First("LilyPond Sans Serif");
        string mono = First("LilyPond Monospace");

        //Assert
        serif.Should().Be(Serif);
        sans.Should().Be(Sans);
        mono.Should().Be(Mono);

        // THE CONTROL: "LilyPond Sans Serif" contains the word "Serif" and is NOT the
        // serif chain, and none of the three is the unknown chain — which is what says
        // they are matched as whole names against a table rather than sniffed or dropped.
        sans.Should().NotBe(Serif);
        new List<string> { serif, sans, mono }.Should().NotContain(Schola);
        First("LilyPond Handwriting").Should().Be(Schola);
    }

    [Fact]
    public void an_absent_family_name_still_resolves_to_the_serif_chain()
    {
        //Arrange
        // Not the unknown chain: no family ASKED FOR is not the same as a family asked
        // for and not available. Upstream's default family is `serif' (font-select.cc:139).

        //Act
        List<string> empty = Chain(string.Empty);
        List<string> missing = Chain(null);

        //Assert
        empty.Should().Equal(new List<string> { Serif, Schola });
        missing.Should().Equal(new List<string> { Serif, Schola });

        // THE CONTROL: an unavailable name is treated differently, which is what says the
        // two cases have not been merged.
        First("DejaVu Serif").Should().Be(Schola);
    }
}
