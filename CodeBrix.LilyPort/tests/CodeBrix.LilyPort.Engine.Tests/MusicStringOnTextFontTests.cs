// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// What a MUSIC-encoded string does when it is set in a TEXT font — the case
/// <c>\override Staff.TimeSignature.font-name</c> creates.
/// <para>
/// Upstream's <c>Pango_font::text_stencil</c> encapsulates a shaped run as
/// <c>utf-8-string</c> only when <c>(!music_string || !music_strings_to_paths)</c>
/// (<c>pango-font.cc:574</c>). The SVG backend sets <c>music-strings-to-paths</c>, so
/// for a MUSIC string both terms are false, the wrapper is skipped, and the expression
/// stays the raw per-glyph drawing — which <c>output-svg.scm</c> then draws through
/// <c>music-string-to-path</c>, asking <c>ly:find-file</c> for
/// <c>&lt;font-name-style&gt;.svg</c>. LilyPond ships those companions for the
/// EMMENTALER faces alone, so a text face fails the lookup, warns, and draws nothing.
/// </para>
/// <para>
/// THE LATIN1 CASE IS THE CONTROL, and it carries the whole weight of the claim: a rule
/// that dropped the wrapper for every run would satisfy the music-string assertion
/// perfectly and would stop every ordinary text run in the corpus from being drawn.
/// </para>
/// <para>
/// THE EXTENTS ARE THE SECOND CONTROL. Upstream returns <c>dest</c> with
/// <c>dest.extent_box ()</c> whichever branch it took, so the run still OCCUPIES its
/// shaped width — only the drawing is lost. A fix that changed the box would move
/// everything around a time signature while looking like it had merely stopped drawing
/// two digits.
/// </para>
/// </summary>
public class MusicStringOnTextFontTests
{
    private const string Utf8String = "utf-8-string";

    /// <summary>The digits of the 3/4 time signature <c>font-name.ly</c> sets.</summary>
    private const string Digits = "34";

    private static TextFontMetric Serif()
        => new TextFontMetric("serif", false, false, false, 2.2, 1.7573);

    private static string HeadOf(Stencil stencil)
        => stencil.Expression is Pair pair && pair.Car is Symbol head ? head.Name : null;

    [Fact]
    public void a_music_string_is_not_wrapped_in_utf_8_string()
    {
        //Arrange
        TextFontMetric font = Serif();

        //Act
        Stencil music = font.TextStencil(Digits, string.Empty, true);

        //Assert
        HeadOf(music).Should().NotBe(Utf8String,
            "pango-font.cc:574 skips the encapsulation when the run is a music string"
            + " and the backend turns music strings into paths");
    }

    [Fact]
    public void an_ordinary_text_run_is_still_wrapped_which_is_the_control()
    {
        //Arrange
        TextFontMetric font = Serif();

        //Act
        Stencil text = font.TextStencil(Digits, string.Empty, false);

        //Assert
        // Without this the rule above could be "never wrap", which would draw no text
        // anywhere in the corpus.
        HeadOf(text).Should().Be(Utf8String);
    }

    [Fact]
    public void dropping_the_wrapper_does_not_move_the_run()
    {
        //Arrange
        TextFontMetric font = Serif();

        //Act
        Stencil music = font.TextStencil(Digits, string.Empty, true);
        Stencil text = font.TextStencil(Digits, string.Empty, false);

        //Assert
        // Upstream returns dest.extent_box () on both branches. The run keeps its place
        // and its width; only the drawing is lost.
        music.XExtent.Left.Should().Be(text.XExtent.Left);
        music.XExtent.Right.Should().Be(text.XExtent.Right);
        music.YExtent.Left.Should().Be(text.YExtent.Left);
        music.YExtent.Right.Should().Be(text.YExtent.Right);

        // ...and the extent is a real measurement, not an empty box, or the equality
        // above would hold vacuously.
        music.XExtent.Right.Should().BeGreaterThan(music.XExtent.Left);
    }
}
