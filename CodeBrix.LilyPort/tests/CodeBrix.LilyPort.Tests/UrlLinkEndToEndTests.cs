// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// GLYPH-PARITY follow-up (2026-08-12) end to end: <c>\with-url</c> must emit its
/// hyperlink hot-zone.
/// <para>
/// The defect these fence: the SVG backend had no <c>url-link</c> case at all, so the
/// <c>&lt;a&gt;</c> and the invisible <c>&lt;rect&gt;</c> upstream's
/// <c>output-svg.scm</c> writes were simply dropped. Because the rect draws nothing —
/// <c>fill="none" stroke="none"</c> — nothing looked wrong on any page, and the
/// omission survived the whole port. It was found only by measuring inventory
/// differences after named-glyph identity landed, where it turned out to be the single
/// largest remaining difference: present on 2,098 of the 2,316 reference pages, because
/// the "Music engraving by LilyPond" tagline carries one.
/// </para>
/// <para>
/// Expected geometry is hand-computed from upstream's own expression
/// (<c>output-svg.scm</c>'s <c>url-link</c>), which writes
/// <c>x = (car x)</c>, <c>y = (car y)</c>, <c>width = (cdr x) - (car x)</c> and
/// <c>height = (cdr y) - (car y)</c> — the linked markup's extents, with NO Y negation.
/// Nothing here is recorded from the port's own output: the facts are RELATIONSHIPS.
/// </para>
/// <para>
/// The Y-sign fact is the one worth having. Every other coordinate in this backend goes
/// through <c>FormatY</c>, so writing this one raw looks like an oversight and invites a
/// later "fix". Upstream measures upward and does not negate here, so the linked text's
/// extent — which starts below its baseline whenever it has a descender — must reach the
/// attribute NEGATIVE with a POSITIVE height. Had the coordinate been negated, both
/// signs would flip together, which is exactly what these assertions catch.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class UrlLinkEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    /// <summary>A URL distinct from the tagline's, so it can be found unambiguously.</summary>
    private const string Url = "https://example.org/linked";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-urllink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Render(string markup, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            Version + "\\markup { " + markup + " }\n", name, null, ScratchDirectory());

        result.SvgPath.Should().NotBeNull();
        return File.ReadAllText(result.SvgPath);
    }

    /// <summary>
    /// Every <c>xlink:href</c> on the page. A page normally carries ONE already — the
    /// engraving tagline's own link — so these tests compare counts against a control
    /// rather than asserting an absolute number.
    /// </summary>
    private static List<string> Hrefs(string svg)
    {
        List<string> hrefs = new List<string>();
        foreach (Match match in Regex.Matches(svg, "<a xlink:href=\"([^\"]*)\">"))
        {
            hrefs.Add(match.Groups[1].Value);
        }

        return hrefs;
    }

    /// <summary>
    /// The invisible rect wrapped by the anchor carrying <see cref="Url"/>, as
    /// (x, y, width, height). Fails the test if that anchor does not wrap exactly one
    /// such rect.
    /// </summary>
    private static (double X, double Y, double Width, double Height) LinkedRect(string svg)
    {
        Match match = Regex.Match(
            svg,
            "<a xlink:href=\"" + Regex.Escape(Url) + "\">\\s*"
            + "<rect x=\"(?<x>[-0-9.]+)\" y=\"(?<y>[-0-9.]+)\""
            + " width=\"(?<w>[-0-9.]+)\" height=\"(?<h>[-0-9.]+)\""
            + " fill=\"none\" stroke=\"none\" stroke-width=\"0.0\"/>\\s*</a>");

        match.Success.Should().BeTrue(
            "the anchor for " + Url + " must wrap exactly one invisible rect");

        double Value(string group)
            => double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

        return (Value("x"), Value("y"), Value("w"), Value("h"));
    }

    [Fact]
    public void with_url_adds_one_anchor_around_an_invisible_rect()
    {
        //Arrange
        // The control is the SAME markup without \with-url. It is not an empty page: the
        // engraving tagline carries a link of its own, so the fact is that the linked
        // page has exactly ONE MORE anchor, and that the extra one is ours.

        //Act
        string linked = Render("\\with-url \"" + Url + "\" \"pay\"", "urllink-linked");
        string control = Render("\"pay\"", "urllink-control");

        //Assert
        List<string> linkedHrefs = Hrefs(linked);
        List<string> controlHrefs = Hrefs(control);

        linkedHrefs.Should().Contain(Url);
        controlHrefs.Should().NotContain(Url);
        linkedHrefs.Count.Should().Be(
            controlHrefs.Count + 1,
            "\\with-url adds exactly one anchor to whatever the page already had");

        // The rect is a hot-zone, never ink: it must be findable AND invisible.
        LinkedRect(linked).Width.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void the_anchor_rect_keeps_lilypond_s_upward_y_rather_than_svg_s()
    {
        //Arrange
        // "pay" has a descender, so the linked markup's Y extent starts BELOW its
        // baseline. Upstream writes (car y) raw, so that reaches the attribute negative
        // with a positive height. Negating it -- which every other coordinate in this
        // backend does -- would flip BOTH signs, so the two assertions are each other's
        // control.

        //Act
        (double _, double y, double _, double height) = LinkedRect(
            Render("\\with-url \"" + Url + "\" \"pay\"", "urllink-ysign"));

        //Assert
        y.Should().BeLessThan(
            0.0,
            "a descender puts the linked markup's Y extent below the baseline, and"
            + " url-link writes that coordinate WITHOUT negating it (got "
            + y.ToString("F4", CultureInfo.InvariantCulture) + ")");
        height.Should().BeGreaterThan(
            0.0,
            "height is (cdr y) - (car y) over a non-empty interval (got "
            + height.ToString("F4", CultureInfo.InvariantCulture) + ")");
    }

    [Fact]
    public void the_anchor_rect_widens_with_the_markup_it_links()
    {
        //Arrange
        // width is (cdr x) - (car x) -- the linked markup's OWN extent -- so a longer
        // run must produce a wider hot-zone. The control that would pass for the wrong
        // reason is a fixed-size rect, which this catches; changing only the URL and not
        // the text must NOT change the geometry, which pins it to the markup rather than
        // to the link.

        //Act
        (double _, double _, double narrow, double _) = LinkedRect(
            Render("\\with-url \"" + Url + "\" \"ii\"", "urllink-narrow"));
        (double _, double _, double wide, double _) = LinkedRect(
            Render("\\with-url \"" + Url + "\" \"iiiiiiiiiiiiiiii\"", "urllink-wide"));

        //Assert
        wide.Should().BeGreaterThan(
            narrow,
            "sixteen characters must claim a wider hot-zone than two (got "
            + wide.ToString("F4", CultureInfo.InvariantCulture) + " vs "
            + narrow.ToString("F4", CultureInfo.InvariantCulture) + ")");
    }
}
