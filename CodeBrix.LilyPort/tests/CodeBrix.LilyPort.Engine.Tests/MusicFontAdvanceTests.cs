// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Fences the music font's <c>hmtx</c> advances against its own glyph boxes.
/// <para>
/// The STAFF-LINES follow-up (2026-08-11) found <c>IndexedAdvance</c> multiplying raw
/// font units by the POINT constant — a unit from a different domain — so every
/// composed-run advance came out exactly FIFTY times the glyph's width. A time
/// signature whose spec prints its numerals as a STRING (<c>(1/2 . 3/4)</c> gives
/// "1/2" over "3/4") spread three characters across sixty staff spaces, and line
/// breaking exploded the whole <c>time-signature-grob-*</c> family into six times the
/// oracle's system count.
/// </para>
/// <para>
/// The fence is the RELATIONSHIP, not a literal: a glyph's advance and its inked
/// width are the same kind of number and, for Emmentaler's digits, nearly equal —
/// while the broken scaling was off by a factor of fifty. Asserting a band around
/// 1.0 catches any unit-domain mixup without pinning the font's exact metrics,
/// which belong to the font.
/// </para>
/// </summary>
public class MusicFontAdvanceTests
{
    private static OpenTypeFontMetric LoadMusicFont()
    {
        byte[] bytes = FontAssets.MusicFont("emmentaler-20");
        bytes.Should().NotBeNull();

        Interpreter interpreter = new Interpreter();
        SchemeBootstrap.LoadCore(interpreter);

        OpenTypeFont font = new OpenTypeFont(bytes, "emmentaler-20", interpreter);
        return new OpenTypeFontMetric(font, "emmentaler-20");
    }

    [Theory]
    [InlineData("one")]
    [InlineData("two")]
    [InlineData("three")]
    [InlineData("four")]
    [InlineData("slash")]
    public void a_glyphs_advance_is_commensurate_with_its_own_box(string glyphName)
    {
        //Arrange
        OpenTypeFontMetric metric = LoadMusicFont();
        int index = metric.NameToIndex(glyphName);
        index.Should().NotBe(FontMetric.GlyphIndexInvalid);

        //Act
        double advance = metric.IndexedAdvance(index);
        double boxWidth = metric.GetIndexedCharDimensions(index).X.Length;

        //Assert
        (advance > 0).Should().BeTrue("a digit advances the pen");
        (boxWidth > 0).Should().BeTrue("a digit has ink");

        // The broken scaling answered ratio == 50 exactly; the true ratio for these
        // glyphs is within a few percent of 1. The band is generous on purpose —
        // it fences the unit DOMAIN, not the font's metrics.
        double ratio = advance / boxWidth;
        (ratio > 0.8 && ratio < 1.2).Should().BeTrue(
            "advance and box width live in the same unit space (got ratio "
            + ratio.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            + " for '" + glyphName + "')");
    }
}
