// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// What <c>ly:font-name</c> answers, which is a THREE-WAY rule and not a name lookup.
/// <para>
/// Upstream's <c>Font_metric::font_name ()</c> has a body — it returns the literal
/// string <c>"unknown"</c> — and exactly one class overrides it,
/// <c>Open_type_font::font_name ()</c>, with the face's PostScript name;
/// <c>Modified_font_metric</c> forwards to the font it wraps, and <c>Pango_font</c>
/// does not override at all, so a TEXT font answers <c>"unknown"</c>.
/// </para>
/// <para>
/// THAT DEFAULT IS LOAD-BEARING RATHER THAN A PLACEHOLDER, which is why it is fenced.
/// <c>ly/property-init.ly</c>'s <c>cross-style</c> — the callback behind
/// <c>\xNotesOn</c> — asks the layout for a <c>latin1</c> font and then tests
/// </para>
/// <code>
///     (string=? (ly:font-name font) "unknown")
/// </code>
/// <para>
/// to decide whether it was handed a text font rather than a music one. When it was, it
/// clears <c>font-name</c> and sets <c>font-family</c> to <c>music</c>, and that switch
/// is the only reason a cross notehead is ever drawn. The port declared
/// <see cref="FontMetric.FontName"/> ABSTRACT, which threw the base body away and made
/// each subclass invent an answer; the text font returned its own description, the
/// string comparison was false, the switch never ran, and <c>dead-notes</c> lost ten
/// <c>noteheads.s2cross</c> glyphs.
/// </para>
/// <para>
/// The music-font case is the CONTROL and it is not decoration: a rule that answered
/// <c>"unknown"</c> for everything would satisfy the text-font claim perfectly and
/// would break every music-glyph lookup instead.
/// </para>
/// </summary>
// AllFontMetrics.FindOtfFont builds its OpenTypeFontMetric against
// Bootstrap.LilyPondScheme.Current and caches it PROCESS-WIDE by name, so asking for a
// music font outside the serialized collection poisons that cache for every engraving
// test that runs afterwards — which is exactly what it did on the first draft: two
// VerticalOrganizationEngraverTests cases failed with "expected grob: ()" in the full
// suite and passed when their own class was run alone (trap 2 at the fixture level).
// Rule 8: a fixture that reaches the interpreter serializes.
[Collection(EngineGlobalStateCollection.Name)]
public class FontNameDefaultTests
{
    private const string MusicFontName = "emmentaler-20";

    /// <summary>The one string <c>cross-style</c> compares against, restated here
    /// rather than imported from the code under test.</summary>
    private const string Unknown = "unknown";

    [Fact]
    public void a_text_font_answers_unknown_the_way_pango_font_does()
    {
        //Arrange
        TextFontMetric text = new TextFontMetric("serif", false, false, false, 2.2, 1.7573);

        //Act
        string name = text.FontName;

        //Assert
        name.Should().Be(Unknown,
            "Pango_font does not override Font_metric::font_name, so a text font takes"
            + " the base default -- which is what cross-style tests for");

        // The description is still available to the callers that genuinely want it, and
        // it is NOT the same string: that is what makes the claim above meaningful.
        text.DescriptionString.Should().NotBe(Unknown);
    }

    [Fact]
    public void a_music_font_answers_its_own_name_which_is_the_control()
    {
        //Arrange
        OpenTypeFontMetric music = AllFontMetrics.FindOtfFont(MusicFontName);
        music.Should().NotBeNull("the fixture needs the vendored music font");

        //Act
        string name = music.FontName;

        //Assert
        // Upstream's single override. If this also answered "unknown" the text-font
        // rule above would be vacuous, and every glyph lookup that goes by font name
        // would be broken instead.
        name.Should().NotBe(Unknown);
        name.Should().Be(MusicFontName);
    }

    [Fact]
    public void a_scaled_font_forwards_to_the_font_it_wraps()
    {
        //Arrange — Modified_font_metric::font_name () is
        //`return original_font ()->font_name ();`, for both kinds of original.
        OpenTypeFontMetric music = AllFontMetrics.FindOtfFont(MusicFontName);
        music.Should().NotBeNull();
        TextFontMetric text = new TextFontMetric("serif", false, false, false, 2.2, 1.7573);

        //Act
        ModifiedFontMetric scaledMusic = new ModifiedFontMetric(music, 1.0);
        ModifiedFontMetric scaledText = new ModifiedFontMetric(text, 1.0);

        //Assert
        scaledMusic.FontName.Should().Be(music.FontName);
        scaledText.FontName.Should().Be(Unknown);

        // The two must not agree, or forwarding is not being tested at all.
        scaledMusic.FontName.Should().NotBe(scaledText.FontName);
    }
}
