// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The 2026-08-08 carry-forward session's fixes, asserted against HAND-COMPUTED
/// values: key-alist transposition (the identity stand-in behind the wrong MIDI key
/// signatures), the Scheme-number contract on its output, the music font's character
/// map (what lets a STRING be set in the feta font), and <c>Prob</c>'s
/// <c>equal?</c> handler.
/// </summary>
/// <remarks>
/// Same rule every Epg*Tests file sets: never assert what the port happens to
/// produce. The d-minor expectation below is worked out from the transposition rule
/// by hand — the untransposed minor pattern has flats on steps 2, 5 and 6, and
/// moving it up a second maps them onto steps 3, 6 and 0, where all but the flat on
/// 6 (b-flat) cancel against d minor's naturals.
/// </remarks>
[Collection(EngineGlobalStateCollection.Name)]
public class CarryForwardFixTests
{
    private static object MinorPattern()
    {
        // The pitch-alist \minor supplies, relative to c: flats on e (2), a (5), b (6).
        object result = Nil.Instance;
        (long Step, long Num)[] entries =
        {
            (6, -1), (5, -1), (4, 0), (3, 0), (2, -1), (1, 0), (0, 0),
        };

        foreach ((long step, long num) in entries)
        {
            object alteration = num == 0 ? 0L : (object)new Ratio(num, 2);
            result = new Pair(new Pair(step, alteration), result);
        }

        return result;
    }

    [Fact]
    public void transposing_the_minor_pattern_by_d_leaves_exactly_one_flat_on_b()
    {
        //Arrange
        // \key d \minor: transpose the c-rooted minor pattern by the pitch d.
        Pitch d = new Pitch(-1, 1, Rational.Zero);

        //Act
        object transposed = MusicSequence.TransposeKeyAlist(MinorPattern(), d);

        //Assert
        // HAND-COMPUTED: c->d, d->e, eb->f, f->g, g->a, ab->bb, bb->c. One flat, on b.
        double flatSum = 0;
        int entries = 0;
        object cursor = transposed;
        while (cursor is Pair pair)
        {
            entries++;
            Pair entry = (Pair)pair.Car;
            Bootstrap.SchemeConvert.TryToRational(entry.Cdr, out Rational alteration)
                .Should().BeTrue();
            flatSum += 2.0 * alteration.ToDouble();
            cursor = pair.Cdr;
        }

        entries.Should().Be(7);

        // alterations-in-key's own sum: sharps minus flats. d minor is -1, and the
        // identity stand-in this fences answered -3 (the untransposed pattern).
        flatSum.Should().Be(-1.0);
    }

    [Fact]
    public void a_transposed_key_alist_carries_scheme_numbers_not_host_rationals()
    {
        //Arrange
        Pitch d = new Pitch(-1, 1, Rational.Zero);

        //Act
        object transposed = MusicSequence.TransposeKeyAlist(MinorPattern(), d);

        //Assert
        // The alist is read back by SCHEME (alterations-in-key multiplies each cdr),
        // so a Flower Rational in a cdr is a wrong-type-arg at the first (* (cdr p) 2)
        // — which is exactly how the fix was found: the whole performance threw and
        // the .midi was truncated. Upstream stores to_scm (orig.get_alteration ()).
        object cursor = transposed;
        while (cursor is Pair pair)
        {
            object alteration = ((Pair)pair.Car).Cdr;
            (alteration is Rational).Should().BeFalse();
            (alteration is long || alteration is Ratio).Should().BeTrue();
            cursor = pair.Cdr;
        }
    }

    [Fact]
    public void the_music_font_maps_the_feta_text_characters_through_its_cmap()
    {
        //Arrange
        // The glyphs \number and figured bass set as STRINGS: digits, and the plus the
        // figure formatter appends for augmented steps. Their presence in the cmap is
        // what makes a fetaText string settable at all; until 2026-08-08 the port
        // answered an empty stencil for every such string.
        OpenTypeFontMetric font = AllFontMetrics.FindOtfFont("emmentaler-20");

        //Assert
        font.Should().NotBeNull();
        foreach (char c in "0123456789+")
        {
            int index = font.CharToGlyphIndex(c);
            index.Should().NotBe(FontMetric.GlyphIndexInvalid);
            font.IndexToName(index).Should().NotBeNull();

            // hmtx advances are what the composed run's pen moves by; a zero advance
            // would stack every digit of "64" on one spot.
            font.IndexedAdvance(index).Should().BeGreaterThan(0.0);
        }
    }

    [Fact]
    public void probs_of_equal_properties_are_scheme_equal_and_different_ones_are_not()
    {
        //Arrange
        // Prob::equal_p compares class names and both alists positionally, skipping
        // origin. Fenced from BOTH sides, the RATCHET-FIX rule: a handler that made
        // everything compare equal would be its own bug.
        Prob a = new Prob(Symbol.Intern("paper-system"), Nil.Instance);
        Prob b = new Prob(Symbol.Intern("paper-system"), Nil.Instance);
        a.SetProperty(Symbol.Intern("page-count"), 2L);
        b.SetProperty(Symbol.Intern("page-count"), 2L);

        Prob c = new Prob(Symbol.Intern("paper-system"), Nil.Instance);
        c.SetProperty(Symbol.Intern("page-count"), 3L);

        //Assert
        SchemeUtilities.IsEqual(a, b).Should().BeTrue();
        SchemeUtilities.IsEqual(a, c).Should().BeFalse();
    }
}
