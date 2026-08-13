// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// EPG23 follow-up (2026-08-12) end to end: a footnote stuck onto a SPANNER must reach
/// the page, and simultaneous footnotes must be numbered in upstream's reading order.
/// <para>
/// Two defects are fenced here, both found chasing the two red ratchet rows EPG23 left
/// on <c>footnote-auto-numbering-vertical-order</c>.
/// </para>
/// <para>
/// The first is the sticky-spanner bound inheritance (<c>spanner.cc</c>'s
/// <c>get_bound</c>): a <c>Footnote</c> on a beam or a hairpin is a SPANNER and is given
/// no bounds of its own, so with the fallback missing it failed break processing and
/// never reached any system. Only footnotes on ITEMS — note heads — engraved. Nothing
/// warned, because a footnote that is simply absent takes its mark, its number and its
/// annotation line with it.
/// </para>
/// <para>
/// The second is <c>grob_2D_less</c>'s <c>X-offset</c> test. Upstream reads it with
/// <c>from_scm&lt;double&gt; (…, 0.0)</c>, which accepts ANY number; the port required a
/// C# <c>double</c>, and a footnote's <c>X-offset</c> is copied straight off the
/// <c>\footnote #'(1 . 1)</c> pair as an EXACT integer. Every footnote therefore read as
/// "not offset right" and sorted at its LEFT column rank, which interleaves the staves
/// instead of ordering beams before hairpins.
/// </para>
/// <para>
/// Expected numbers are hand-computed from upstream's rule, not recorded: a footnote
/// spanner with a positive <c>X-offset</c> sorts at its RIGHT column rank, the beam here
/// ends before the hairpin does, and grobs sharing a rank order top staff first. Nothing
/// asserts a literal off the port's own output — the ordering test's control asserts the
/// OPPOSITE order and must fail to render.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class StickyFootnoteEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-stickyfn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BatchRunResult Run(string body, string name)
        => BatchRunner.RunText(Version + body, name, null, ScratchDirectory());

    /// <summary>Every text run on every page of a render, markup stripped.</summary>
    private static List<string> TextRuns(BatchRunResult result)
    {
        List<string> runs = new List<string>();
        foreach (string path in result.SvgPaths)
        {
            string svg = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(svg, "<text[^>]*>(.*?)</text>", RegexOptions.Singleline))
            {
                runs.Add(Regex.Replace(match.Groups[1].Value, "<[^>]+>", string.Empty).Trim());
            }
        }

        return runs;
    }

    /// <summary>
    /// A one-bar score, optionally carrying a footnote on the chord's middle note head
    /// and/or on the beam. The marks are given EXPLICITLY — single letters — so they can
    /// be told apart from the automatic numbers and from the engraving tagline.
    /// </summary>
    /// <param name="headFootnote">The footnote to hang on a note head, or empty.</param>
    /// <param name="beamFootnote">The footnote to hang on the beam, or empty.</param>
    /// <returns>The score source.</returns>
    private static string Score(string headFootnote, string beamFootnote)
        => "\\book { \\score { \\new Staff \\relative {\n"
           + "  d'4 e < f " + headFootnote + " a c >\n"
           + "  a8" + beamFootnote + " [ b c d ] a4 b c |\n"
           + "} } }\n";

    [Fact]
    public void a_footnote_on_a_beam_reaches_the_page()
    {
        //Arrange
        // Three renders of the same bar. The bare one proves the marks are not page
        // furniture; the note-head one proves the observable works (that footnote
        // engraved all along, because a note head is an Item); the beam one is the
        // regression — its Footnote is a SPANNER and inherits its bounds from the beam.
        string bare = Score(string.Empty, string.Empty);
        string onHead = Score("\\footnote \\markup { H } #'(1 . -1) \\markup { hh }", string.Empty);
        string onBeam = Score(
            string.Empty, "-\\footnote \\markup { B } #'(1 . 1) \\markup { bb }");

        //Act
        List<string> bareRuns = TextRuns(Run(bare, "sticky-bare"));
        List<string> headRuns = TextRuns(Run(onHead, "sticky-head"));
        List<string> beamRuns = TextRuns(Run(onBeam, "sticky-beam"));

        //Assert
        bareRuns.Should().NotContain("H");
        bareRuns.Should().NotContain("B");
        headRuns.Should().Contain("H");
        beamRuns.Should().Contain("B");

        // And the footnote brings its TEXT down to the bottom of the page too, so this
        // is a whole footnote and not just a surviving mark.
        headRuns.Should().Contain("hh");
        beamRuns.Should().Contain("bb");
    }

    [Fact]
    public void simultaneous_footnotes_are_numbered_beams_before_hairpins()
    {
        //Arrange
        // Two staves, four footnotes, all starting on the SAME note. The beam ends at
        // the last beamed eighth; the hairpin runs on to the dynamic, so its right
        // column rank is later. Upstream therefore numbers both beams before either
        // hairpin, top staff first: beam 0, beam 1, hairpin 2, hairpin 3.
        //
        // The assertion functions are the file's own, in upstream's own style: a wrong
        // number calls ly:error, which loses the book and leaves no SVG behind.
        BatchRunResult correct = Run(TwoStaves(0, 2, 1, 3), "sticky-order");

        //Act
        // The control asserts the numbering the X-offset defect produced — the staves
        // interleaved, beam 0, hairpin 1, beam 2, hairpin 3 — and must come out
        // differently.
        BatchRunResult wrong = Run(TwoStaves(0, 1, 2, 3), "sticky-order-control");

        //Assert
        correct.SvgPaths.Should().NotBeEmpty();
        wrong.SvgPaths.Should().BeEmpty();

        // The control must fail for the RIGHT reason — a lost book with no footnote
        // grobs at all would also leave no SVG behind.
        string.Join("\n", wrong.Diagnostics).Should().Contain("footnote order");
    }

    /// <summary>
    /// Two staves of simultaneous footnoted music, each staff asserting which number its
    /// beam footnote and its hairpin footnote must receive.
    /// </summary>
    /// <param name="upperBeam">The number the upper staff's beam footnote must get.</param>
    /// <param name="upperHairpin">The number the upper staff's hairpin footnote must get.</param>
    /// <param name="lowerBeam">The number the lower staff's beam footnote must get.</param>
    /// <param name="lowerHairpin">The number the lower staff's hairpin footnote must get.</param>
    /// <returns>The score source.</returns>
    private static string TwoStaves(
        int upperBeam, int upperHairpin, int lowerBeam, int lowerHairpin)
        => "#(define (expect beam-number other-number)\n"
           + "  (lambda (grob)\n"
           + "    (let ((n (if (grob::has-interface (ly:grob-parent grob Y) 'beam-interface)\n"
           + "                 beam-number other-number)))\n"
           + "      (lambda (x)\n"
           + "        (if (not (= n x))\n"
           + "            (ly:error \"footnote order: expected ~a, got ~a\" n x))))))\n"
           + "\\book { \\score { <<\n"
           + "  \\new Staff \\relative {\n"
           + "    \\once \\override Footnote.numbering-assertion-function =\n"
           + "      #(expect " + upperBeam + " " + upperHairpin + ")\n"
           + "    a'8-\\footnote #'(1 . 1) \\markup { p } \\<\n"
           + "      -\\footnote #'(1 . 1) \\markup { o } [ b c d ] a4 b c\\f |\n"
           + "  }\n"
           + "  \\new Staff \\relative {\n"
           + "    \\once \\override Footnote.numbering-assertion-function =\n"
           + "      #(expect " + lowerBeam + " " + lowerHairpin + ")\n"
           + "    a'8-\\footnote #'(1 . 1) \\markup { p } \\<\n"
           + "      -\\footnote #'(1 . 1) \\markup { o } [ b c d ] a4 b c\\f |\n"
           + "  }\n"
           + ">> } }\n";
}
