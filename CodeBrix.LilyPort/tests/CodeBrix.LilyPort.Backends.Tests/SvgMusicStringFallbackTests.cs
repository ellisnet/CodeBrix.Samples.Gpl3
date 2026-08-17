// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Backends;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Backends.Tests;

/// <summary>
/// A music string set in a text face draws NOTHING, which is
/// <c>output-svg.scm</c>'s <c>music-string-to-path</c> falling to its warning arm.
/// <para>
/// Upstream builds the file name from the face and asks <c>ly:find-file</c> for
/// <c>&lt;font-name-style&gt;.svg</c>. LilyPond ships those companions for the
/// Emmentaler faces alone — the oracle's own font tree has nine of them, all
/// <c>emmentaler-*</c> — so the lookup fails for every text face and the glyph is not
/// drawn. The port reaches the same place: a bare <c>glyph-outline</c> is produced only
/// by <see cref="CodeBrix.LilyPort.Engine.Fonts.TextFontMetric"/>, and only for a run
/// whose <c>font-encoding</c> is a music encoding.
/// </para>
/// <para>
/// THE CONTROL IS THE WRAPPED RUN. The same digits inside a <c>utf-8-string</c> must
/// still be set as real text, or the rule under test would be "text fonts draw
/// nothing" and every page in the corpus would lose its words.
/// </para>
/// </summary>
public class SvgMusicStringFallbackTests
{
    /// <summary>The 3/4 time signature of <c>font-name.ly</c>, whose two digits are
    /// exactly the two glyphs the oracle declines to draw.</summary>
    private const string Digits = "34";

    [Fact]
    public void a_bare_glyph_outline_draws_nothing_and_is_understood()
    {
        //Arrange
        SvgBackend backend = new SvgBackend();
        object expression = Pair.List(
            Symbol.Intern("glyph-outline"), Nil.Instance, 0L, 1.0);

        //Act
        object handled = backend.Output(expression);

        //Assert
        handled.Should().Be(true,
            "the command is understood -- upstream reaches music-string-to-path and"
            + " takes its warning arm, which is not the same as an unknown head");
        backend.Body.Should().BeEmpty("the .svg companion the face would need does not exist");
        backend.UnhandledCommands.Should().BeEmpty();
    }

    [Fact]
    public void a_wrapped_run_is_still_set_as_text_which_is_the_control()
    {
        //Arrange
        SvgBackend backend = new SvgBackend();
        object expression = Pair.List(
            Symbol.Intern("utf-8-string"),
            new MutableString("serif 2.200"),
            new MutableString(Digits),
            Nil.Instance);

        //Act
        backend.Output(expression);

        //Assert
        // Without this the claim above could be satisfied by a backend that draws no
        // text at all.
        backend.Body.Should().Contain("<text");
        backend.Body.Should().Contain(Digits);
    }
}
