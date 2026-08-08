// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Objects;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG5's rest glyph naming: the <c>rests.&lt;durlog&gt;</c> scheme, the ledger
/// suffix, and the styles that deliberately fall back to the default — worked out from
/// <c>lily/rest.cc</c>, which ships no tests.
/// </summary>
public class RestTests
{
    private static Grob BareRest() => SpacingFixtures.NewSpacingGrob();

    [Fact]
    public void a_quarter_rest_is_rests_2()
    {
        //Arrange
        Grob rest = BareRest();

        //Act
        string name = Rest.GlyphName(rest, 2, string.Empty, false, 0.0);

        //Assert
        name.Should().Be("rests.2");
    }

    [Fact]
    public void the_default_style_is_spelled_as_no_suffix()
    {
        //Arrange
        // "Some parts of lily still prefer style `default' over the empty string" —
        // rest.cc corrects it in glyph_name, and the glyph names in the font carry no
        // `default' suffix.
        Grob rest = BareRest();

        //Act
        string name = Rest.GlyphName(rest, 3, "default", false, 0.0);

        //Assert
        name.Should().Be("rests.3");
    }

    [Fact]
    public void classical_and_z_styles_only_differ_in_quarter_rests()
    {
        //Arrange
        Grob rest = BareRest();

        //Act / Assert
        Rest.GlyphName(rest, 2, "classical", false, 0.0).Should().Be("rests.2classical");
        Rest.GlyphName(rest, 2, "z", false, 0.0).Should().Be("rests.2z");

        // Any other duration falls back to the default shape.
        Rest.GlyphName(rest, 3, "classical", false, 0.0).Should().Be("rests.3");
        Rest.GlyphName(rest, 1, "z", false, 0.0).Should().Be("rests.1");
    }

    [Fact]
    public void mensural_styles_have_no_short_rests_and_never_ledger()
    {
        //Arrange
        Grob rest = BareRest();

        //Act / Assert
        // 32nds and shorter do not exist in the mensural fonts; the style falls away.
        Rest.GlyphName(rest, 5, "mensural", true, 0.0).Should().Be("rests.5");
        Rest.GlyphName(rest, 5, "neomensural", true, 0.0).Should().Be("rests.5");

        // Within range the style stays, and the bogus ledger suffix is suppressed
        // even when a ledger would otherwise be chosen.
        Rest.GlyphName(rest, 0, "mensural", true, 0.0).Should().Be("rests.0mensural");
    }

    [Fact]
    public void long_rests_off_a_staff_line_take_the_ledgered_glyph()
    {
        //Arrange
        // The fixture grob has no staff symbol, so on_staff_line answers false for
        // every position — which is exactly the state in which a whole or half rest
        // needs its own ledger.
        Grob rest = BareRest();

        //Act / Assert
        Rest.GlyphName(rest, 0, string.Empty, true, 0.0).Should().Be("rests.0o");
        Rest.GlyphName(rest, 1, string.Empty, true, 0.0).Should().Be("rests.1o");

        // A breve is ledgered only when NEITHER lying on nor hanging from a line;
        // with no staff at all both tests fail and the ledger appears.
        Rest.GlyphName(rest, -1, string.Empty, true, 0.0).Should().Be("rests.-1o");

        // Quarter rests and shorter never ledger.
        Rest.GlyphName(rest, 2, string.Empty, true, 0.0).Should().Be("rests.2");
    }

    [Fact]
    public void negative_duration_logs_keep_the_minus_for_the_font_to_map()
    {
        //Arrange
        // C++ std::to_string(-1) is "-1"; the font's own glyph is rests.M1, and it is
        // Font_metric::find_by_name that replaces '-' with 'M'. glyph_name must NOT do
        // the mapping itself, or a font carrying literal-minus names would break.
        Grob rest = BareRest();

        //Act
        string name = Rest.GlyphName(rest, -1, string.Empty, false, 0.0);

        //Assert
        name.Should().Be("rests.-1");
    }
}
