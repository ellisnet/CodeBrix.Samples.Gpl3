// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The substitution fences: a run is shaped with the OpenType features it asks for.
/// <para>
/// Upstream reads <c>font-features</c> off the property chain, joins the list with
/// commas and hands the string to <c>pango_attr_font_features_new</c>, after which
/// HarfBuzz applies the named features. Until PARITY 9 the port applied NO substitution
/// at all, so every grob that asks for <c>ss01</c> drew the plain digits where the
/// oracle draws the <c>fattened</c> ones.
/// </para>
/// <para>
/// Every expected mapping here is HAND-COMPUTED from the font's own build source rather
/// than recorded from the port: <c>mf/emmentaler_features.py</c> in the pinned
/// LilyPond 2.27.2 tree defines <c>ss01("three", "fattened.three")</c>,
/// <c>tnum("fattened.four.alt", "fattened.fixedwidth.four.alt")</c> and
/// <c>cv47("fattened.four", "fattened.four.alt")</c>. Each fact is paired with a
/// control that must come out differently, so a reader that answered a constant — or
/// that substituted unconditionally — could not pass both halves.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SubstitutionTableTests
{
    private static OpenTypeFont Emmentaler()
    {
        // Through FontAssets and a core interpreter, the way MusicFontAdvanceTests does
        // it: the vendored faces are EMBEDDED resources, so loading one by file path
        // works only from a directory that happens to hold a copy.
        byte[] bytes = FontAssets.MusicFont("emmentaler-20");
        bytes.Should().NotBeNull();

        Interpreter interpreter = new Interpreter();
        SchemeBootstrap.LoadCore(interpreter);
        return new OpenTypeFont(bytes, "emmentaler-20", interpreter);
    }

    private static List<int> Run(OpenTypeFont font, params string[] glyphNames)
    {
        List<int> glyphs = new List<int>(glyphNames.Length);
        foreach (string name in glyphNames)
        {
            glyphs.Add(font.NameToIndex(name));
        }

        return glyphs;
    }

    private static string NameOf(OpenTypeFont font, int glyph)
        => glyph >= 0 && glyph < font.GlyphNames.Count ? font.GlyphNames[glyph] : null;

    [Fact]
    public void ss01_selects_the_fattened_digit_and_no_feature_leaves_it_plain()
    {
        //Arrange
        OpenTypeFont font = Emmentaler();
        List<int> asked = Run(font, "three");
        List<int> control = Run(font, "three");

        //Act
        font.Substitutions.Apply(asked, "ss01");
        font.Substitutions.Apply(control, string.Empty);

        //Assert
        // emmentaler_features.py: ss01("three", "fattened.three"). The control asks for
        // nothing, and Emmentaler declares no default-on feature, so it must come back
        // untouched -- a table that substituted unconditionally fails here.
        NameOf(font, asked[0]).Should().Be("fattened.three");
        NameOf(font, control[0]).Should().Be("three");
    }

    [Fact]
    public void the_three_digit_features_compose_through_lookup_order()
    {
        //Arrange
        OpenTypeFont font = Emmentaler();
        List<int> all = Run(font, "four");
        List<int> cv47Only = Run(font, "four");

        //Act
        // The order the tags are NAMED in is deliberately not the order they must be
        // applied in: HarfBuzz walks lookups by lookup-list index, and Emmentaler's
        // indices are dlig 0, tnum 1, cv47 2, ss01 3. Naming them backwards must give
        // the same answer as naming them forwards.
        font.Substitutions.Apply(all, "ss01,cv47,tnum");
        font.Substitutions.Apply(cv47Only, "cv47");

        //Assert
        // four --(tnum)--> fixedwidth.four --(cv47)--> fixedwidth.four.alt
        //      --(ss01)--> fattened.fixedwidth.four.alt
        // The control takes only the cv47 step, so the two cannot both pass unless the
        // features really compose rather than one of them winning.
        NameOf(font, all[0]).Should().Be("fattened.fixedwidth.four.alt");
        NameOf(font, cv47Only[0]).Should().Be("four.alt");
    }

    [Fact]
    public void a_feature_the_font_does_not_declare_changes_nothing()
    {
        //Arrange
        OpenTypeFont font = Emmentaler();
        List<int> unknown = Run(font, "three");
        List<int> known = Run(font, "three");

        //Act
        bool unknownChanged = font.Substitutions.Apply(unknown, "smcp,onum,zero");
        bool knownChanged = font.Substitutions.Apply(known, "ss01");

        //Assert
        // Emmentaler declares exactly dlig, tnum, cv47 and ss01. The control proves the
        // table is live, so "nothing changed" here is about the tags and not about a
        // table that never substitutes.
        unknownChanged.Should().BeFalse();
        knownChanged.Should().BeTrue();
    }

    [Fact]
    public void a_leading_minus_turns_a_named_feature_off_and_the_last_setting_wins()
    {
        //Arrange
        // LilyPond writes both forms: BassFigure asks for "tnum", \typewriter for
        // "-liga". It never writes a tag twice, so the repeated cases below are about
        // the PARSER's rule rather than about any real input — and the rule is
        // HarfBuzz's, where the feature list is applied in order and a later setting
        // overrides an earlier one for the same tag.
        OpenTypeFont font = Emmentaler();
        List<int> offLast = Run(font, "three");
        List<int> onLast = Run(font, "three");
        List<int> plainOff = Run(font, "three");
        List<int> control = Run(font, "three");

        //Act
        font.Substitutions.Apply(offLast, "ss01,-ss01");
        font.Substitutions.Apply(onLast, "-ss01,ss01");
        font.Substitutions.Apply(plainOff, "-ss01");
        font.Substitutions.Apply(control, "ss01");

        //Assert
        // The two repeated cases must come out DIFFERENTLY from each other, which is
        // what makes this a fence on the ordering rule and not on the minus alone; the
        // control proves the tag would otherwise have fired.
        NameOf(font, offLast[0]).Should().Be("three");
        NameOf(font, onLast[0]).Should().Be("fattened.three");
        NameOf(font, plainOff[0]).Should().Be("three");
        NameOf(font, control[0]).Should().Be("fattened.three");
    }
}
