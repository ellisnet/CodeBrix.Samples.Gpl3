// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.MusicView;
using SilverAssertions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.MusicView.Tests;

/// <summary>
/// The SVG pages these read are REAL engine output: engraved by LilyPort with
/// point-and-click on, and committed as they came out (only the absolute paths
/// in the anchors were rewritten, so the fixtures do not carry the machine they
/// were made on).
/// </summary>
public class SvgPageTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void a_page_takes_its_size_from_the_file_in_css_pixels()
    {
        //Arrange
        var page = new SvgPage(Fixture("twinkle.svg"));

        //Act
        page.UpdateSize(96, 96, 1.0);

        //Assert — A4 at 96 dpi, which is what the engine wrote.
        page.Dpi.Should().Be(96.0);
        page.PageWidth.Should().BeApproximately(794.0, 1.0);
        page.PageHeight.Should().BeApproximately(1123.0, 1.0);
        page.Width.Should().Be(794);
        page.Height.Should().Be(1123);
    }

    [Fact]
    public void every_point_and_click_anchor_in_the_file_becomes_a_link()
    {
        //Arrange
        var page = new SvgPage(Fixture("twinkle.svg"));
        int inFile = File.ReadAllText(Fixture("twinkle.svg"))
            .Split("textedit://").Length - 1;

        //Act
        List<Link> links = page.Links()
            .Where(l => l.Url.StartsWith("textedit:", StringComparison.Ordinal)).ToList();

        //Assert
        inFile.Should().Be(14);
        links.Count.Should().Be(inFile);
    }

    [Fact]
    public void a_links_area_is_a_fraction_of_the_page_and_has_real_size()
    {
        //Arrange
        var page = new SvgPage(Fixture("twinkle.svg"));

        //Act
        Link link = page.Links()
            .First(l => l.Url.StartsWith("textedit:", StringComparison.Ordinal));

        //Assert
        link.Left.Should().BeInRange(0f, 1f);
        link.Top.Should().BeInRange(0f, 1f);
        link.Right.Should().BeGreaterThan(link.Left);
        link.Bottom.Should().BeGreaterThan(link.Top);
    }

    [Fact]
    public void the_middle_of_a_links_area_hits_that_link()
    {
        //Arrange
        var page = new SvgPage(Fixture("twinkle.svg"));
        page.UpdateSize(96, 96, 1.0);
        Link link = page.Links()
            .First(l => l.Url.StartsWith("textedit:", StringComparison.Ordinal));
        SKRect rect = page.LinkRect(link);

        //Act
        IReadOnlyList<Link> hit = page.LinksAt(new SKPoint(rect.MidX, rect.MidY));

        //Assert
        hit.Should().Contain(link);
    }

    [Fact]
    public void a_point_on_empty_paper_hits_no_link()
    {
        //Arrange
        var page = new SvgPage(Fixture("twinkle.svg"));
        page.UpdateSize(96, 96, 1.0);

        //Act — well below the single system, above the tagline.
        IReadOnlyList<Link> hit = page.LinksAt(new SKPoint(400, 700));

        //Assert
        hit.Should().BeEmpty();
    }

    [Fact]
    public void a_link_keeps_pointing_at_the_same_notehead_when_the_page_is_zoomed()
    {
        //Arrange
        var page = new SvgPage(Fixture("twinkle.svg"));
        page.UpdateSize(96, 96, 1.0);
        Link link = page.Links()
            .First(l => l.Url.StartsWith("textedit:", StringComparison.Ordinal));
        SKRect atOne = page.LinkRect(link);

        //Act
        page.UpdateSize(96, 96, 2.0);
        SKRect atTwo = page.LinkRect(link);

        //Assert
        atTwo.Left.Should().BeApproximately(atOne.Left * 2f, 0.5f);
        atTwo.Top.Should().BeApproximately(atOne.Top * 2f, 0.5f);
    }

    [Fact]
    public void the_engines_own_url_form_is_the_one_that_is_written()
    {
        //Arrange
        var page = new SvgPage(Fixture("twinkle.svg"));

        //Act
        Link link = page.Links()
            .First(l => l.Url.StartsWith("textedit:", StringComparison.Ordinal));

        //Assert — file, line, character index, column.
        link.Url.Should().StartWith("textedit:///scores/twinkle.ly:");
        link.Url.Split(':').Length.Should().Be(5);
    }

    [Fact]
    public void a_file_that_is_not_an_svg_fails_without_throwing()
    {
        //Arrange
        var page = new SvgPage(Fixture("twinkle.ly"));

        //Act
        bool failed = page.LoadFailed;

        //Assert
        failed.Should().BeTrue();
        page.Links().Count.Should().Be(0);
    }

    [Fact]
    public void a_missing_file_fails_without_throwing()
    {
        //Arrange
        var page = new SvgPage(Fixture("no-such-file.svg"));

        //Act
        bool failed = page.LoadFailed;

        //Assert
        failed.Should().BeTrue();
    }

    [Fact]
    public void a_document_of_several_files_is_a_page_each_in_order()
    {
        //Arrange
        string[] files =
        {
            Fixture("twopage-1.svg"), Fixture("twopage-2.svg"), Fixture("twopage-10.svg"),
        };

        //Act
        MusicDocument document = MusicDocument.LoadSvgs(files);

        //Assert
        document.Count.Should().Be(3);
        document.FileName.Should().Be(files[0]);
        document.Pages.Cast<SvgPage>().Select(p => Path.GetFileName(p.FileName))
            .Should().Equal("twopage-1.svg", "twopage-2.svg", "twopage-10.svg");
    }

    [Fact]
    public void painting_a_page_draws_something_onto_the_canvas()
    {
        //Arrange
        var page = new SvgPage(Fixture("twinkle.svg"));
        page.UpdateSize(96, 96, 0.5);
        using var bitmap = new SKBitmap(new SKImageInfo(page.Width, page.Height));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        //Act
        page.Paint(canvas, new SKRect(0, 0, page.Width, page.Height));

        //Assert — the staff lines alone guarantee some black on the paper.
        bool anyInk = false;
        for (int y = 0; y < page.Height && !anyInk; y += 2)
        {
            for (int x = 0; x < page.Width; x += 2)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White) { anyInk = true; break; }
            }
        }

        anyInk.Should().BeTrue();
    }

    [Fact]
    public void the_host_is_asked_for_every_font_family_the_page_names()
    {
        //Arrange
        var resolver = new RecordingResolver();
        var page = new SvgPage(Fixture("twinkle.svg"), resolver);
        page.UpdateSize(96, 96, 1.0);
        using var bitmap = new SKBitmap(new SKImageInfo(100, 100));
        using var canvas = new SKCanvas(bitmap);

        //Act
        page.Paint(canvas, new SKRect(0, 0, 100, 100));

        //Assert — the title, the composer and the tagline are all real text.
        resolver.Asked.Should().Contain("serif");
    }

    private sealed class RecordingResolver : IScoreTypefaceResolver
    {
        public List<string> Asked { get; } = new List<string>();

        public SKTypeface Resolve(
            string familyName, SKFontStyleWeight weight, SKFontStyleWidth width, SKFontStyleSlant slant)
        {
            Asked.Add(familyName);
            return null;
        }
    }
}
