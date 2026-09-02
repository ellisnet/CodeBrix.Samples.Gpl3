// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.PdfDocuments.Pdf;
using CodeBrix.PdfDocuments.Pdf.IO;
using Fresco.Brix.MusicView;
using SilverAssertions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace Fresco.Brix.MusicView.Tests;

/// <summary>
/// Writing a page out: to a PDF that is still vector, to a picture, and to an
/// SVG. The pages are real engine output, so what these check is what a user
/// would get out of File &gt; Export.
/// </summary>
public class ScorePdfTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static SvgPage SizedPage(string name)
    {
        var page = new SvgPage(Fixture(name));
        page.UpdateSize(96, 96, 1.0);
        return page;
    }

    [Fact]
    public void a_page_writes_a_pdf_with_no_raster_image_anywhere_in_it()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");

        //Act
        byte[] pdf = ScorePdf.ToBytes(new ScorePage[] { page });
        string bytes = Encoding.Latin1.GetString(pdf);

        //Assert — board FD13 as ruled (b) under FR7: the engine's SVG is placed
        //as vector content through Html2Pdf, so nothing is rasterised. (With no
        //fonts given the text is set in Html2Pdf's packaged TrueType faces, so
        //nothing here is a CFF subset and the file stays PDF 1.4; the engine's
        //faces, and the 1.6 their subsetting declares, are Core's — see
        //Fresco.Brix.Core.Tests.)
        pdf.Should().NotBeNull();
        Encoding.Latin1.GetString(pdf, 0, 5).Should().Be("%PDF-");
        bytes.Should().NotContain("/Subtype /Image");
        bytes.Should().NotContain("/Subtype/Image");
    }

    [Fact]
    public void the_scores_own_faces_are_embedded_so_the_text_is_still_text()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");

        //Act
        byte[] pdf = ScorePdf.ToBytes(new ScorePage[] { page });
        string bytes = Encoding.Latin1.GetString(pdf);

        //Assert — the text is TEXT: a font program is embedded and a ToUnicode
        //map says what its glyphs mean, so the title can be selected and
        //searched in a reader. WHICH face gets embedded is the host's answer
        //and not this library's — the view is handed an IScoreTypefaceResolver
        //and there is none here — so that half is asserted in
        //Fresco.Brix.Core.Tests, where the engine's own faces are (board trap
        //60).
        bytes.Should().Contain("/FontFile");
        bytes.Should().Contain("/ToUnicode");
    }

    [Fact]
    public void the_page_box_is_the_paper_the_engine_declared_not_the_pixels_it_rounded_to()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");

        //Act
        var (width, height) = ScorePdf.PageSizePoints(page);

        //Assert — board trap 61. A4 is 595.276 by 841.890 points; the SVG's
        //794 by 1123 CSS pixels come to 595.5 by 842.25, which is 0.04% out.
        width.Should().BeApproximately(595.276, 0.01);
        height.Should().BeApproximately(841.890, 0.01);
    }

    [Fact]
    public void every_page_given_becomes_a_page_of_the_document_in_order()
    {
        //Arrange
        using var first = new SvgPage(Fixture("twopage-1.svg"));
        using var second = new SvgPage(Fixture("twopage-2.svg"));
        first.UpdateSize(96, 96, 1.0);
        second.UpdateSize(96, 96, 1.0);

        //Act
        byte[] pdf = ScorePdf.ToBytes(new ScorePage[] { first, second });
        string bytes = Encoding.Latin1.GetString(pdf);

        //Assert — read back rather than counted in the bytes, because the
        //writer is free to serialise a page dictionary either way.
        bytes.Length.Should().BeGreaterThan(1000);
        PageCount(pdf).Should().Be(2);
    }

    [Fact]
    public void what_the_document_says_about_itself_is_what_it_was_told()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        var info = new ScorePdfInfo
        {
            Title = "Twinkle", Author = "Traditional", Creator = "Fresco.Brix",
        };

        //Act
        byte[] pdf = ScorePdf.ToBytes(new ScorePage[] { page }, info);
        using PdfDocument document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

        //Assert — read back rather than searched for, because the information
        //dictionary's strings may be stored as UTF-16 hex.
        document.Info.Title.Should().Be("Twinkle");
        document.Info.Author.Should().Be("Traditional");
        document.Info.Creator.Should().Be("Fresco.Brix");
    }

    [Fact]
    public void writing_to_a_file_writes_the_same_bytes_as_writing_to_memory()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        string path = Path.Combine(Path.GetTempPath(), "fresco-pdf-" + Guid.NewGuid().ToString("N") + ".pdf");

        try
        {
            //Act
            ScorePdf.Write(path, new ScorePage[] { page });

            //Assert
            var written = new FileInfo(path);
            written.Exists.Should().BeTrue();
            written.Length.Should().BeGreaterThan(1000L);
            Encoding.Latin1.GetString(File.ReadAllBytes(path), 0, 5).Should().Be("%PDF-");
        }
        finally
        {
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void a_painted_paper_is_still_one_page_and_still_vector()
    {
        //Arrange — the paper colour is a filled box under the picture; it must
        //not push the picture onto a second page.
        using SvgPage page = SizedPage("twinkle.svg");

        //Act
        byte[] pdf = ScorePdf.ToBytes(new ScorePage[] { page }, null, SKColors.LightYellow);
        string bytes = Encoding.Latin1.GetString(pdf);

        //Assert
        PageCount(pdf).Should().Be(1);
        bytes.Should().NotContain("/Subtype /Image");
        bytes.Should().NotContain("/Subtype/Image");
    }

    [Fact]
    public void a_region_is_written_as_the_same_vectors_narrowed_to_it()
    {
        //Arrange — the lower-left quarter of the page, in displayed pixels.
        using SvgPage page = SizedPage("twinkle.svg");
        var region = new SKRect(0, page.Height / 2f, page.Width / 2f, page.Height);
        var exporter = new PdfExporter(page, region);

        //Act
        byte[] pdf = exporter.Data();
        using PdfDocument document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

        //Assert — a quarter of A4, as vectors: the engine's file with its viewBox
        //narrowed, not a rendering of the corner.
        document.PageCount.Should().Be(1);
        document.Pages[0].Width.Point.Should().BeApproximately(595.276 / 2, 0.5);
        document.Pages[0].Height.Point.Should().BeApproximately(841.890 / 2, 0.5);
        Encoding.Latin1.GetString(pdf).Should().NotContain("/Subtype /Image");
    }

    [Fact]
    public void a_turned_page_keeps_its_paper_and_gets_a_rotate_entry()
    {
        //Arrange — the view turned the page a quarter turn clockwise.
        using SvgPage page = SizedPage("twinkle.svg");
        page.ComputedRotation = Rotation.Rotate90;
        page.UpdateSize(96, 96, 1.0);

        //Act
        byte[] pdf = ScorePdf.ToBytes(new ScorePage[] { page });
        using PdfDocument document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

        //Assert — the box is the unrotated paper and /Rotate says how to show it,
        //which is what a PDF reader expects rotation to be. (PdfPage.Width and
        //.Height report the box AS TURNED, so the MediaBox is read directly.)
        document.PageCount.Should().Be(1);
        document.Pages[0].Rotate.Should().Be(90);
        document.Pages[0].MediaBox.Width.Should().BeApproximately(595.276, 0.5);
        document.Pages[0].MediaBox.Height.Should().BeApproximately(841.890, 0.5);
        document.Pages[0].Width.Point.Should().BeApproximately(841.890, 0.5);
    }

    [Fact]
    public void a_page_that_is_not_over_an_svg_file_is_refused_not_rasterised()
    {
        //Arrange
        var page = new MemoryImageSourcePage();

        //Act
        Action act = () => ScorePdf.ToBytes(new ScorePage[] { page });

        //Assert
        act.Should().Throw<NotSupportedException>();
    }

    private static int PageCount(byte[] pdf)
    {
        using PdfDocument document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>A page of no file at all.</summary>
    private sealed class MemoryImageSourcePage : ScorePage
    {
        public MemoryImageSourcePage() { SetPageSize(100, 100); }

        public override void Paint(SKCanvas canvas, SKRect rect) { }
    }
}

/// <summary>Rendering a page, or a piece of one, to a picture.</summary>
public class ImageExporterTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static SvgPage SizedPage(string name)
    {
        var page = new SvgPage(Fixture(name));
        page.UpdateSize(96, 96, 1.0);
        return page;
    }

    [Fact]
    public void a_whole_page_renders_at_the_resolution_it_was_asked_for()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        using var exporter = new ImageExporter(page) { Resolution = 300.0 };

        //Act
        using SKImage image = exporter.Image();

        //Assert — A4's 794 by 1123 CSS pixels at 300 dpi.
        image.Width.Should().Be((int)Math.Round(794 * 300.0 / 96.0));
        image.Height.Should().Be((int)Math.Round(1123 * 300.0 / 96.0));
    }

    [Fact]
    public void a_region_renders_only_that_region()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        var region = new SKRect(100f, 200f, 300f, 500f);
        using var exporter = new ImageExporter(page, region) { Resolution = 96.0 };

        //Act
        using SKImage image = exporter.Image();

        //Assert
        image.Width.Should().Be(200);
        image.Height.Should().Be(300);
    }

    [Fact]
    public void the_bytes_are_a_png()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        using var exporter = new ImageExporter(page) { Resolution = 96.0 };

        //Act
        byte[] data = exporter.Data();

        //Assert
        exporter.Successful().Should().BeTrue();
        data[0].Should().Be((byte)0x89);
        data[1].Should().Be((byte)'P');
        data[2].Should().Be((byte)'N');
        data[3].Should().Be((byte)'G');
    }

    [Fact]
    public void rendering_twice_as_large_and_scaling_back_gives_the_asked_for_size()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        using var exporter = new ImageExporter(page) { Resolution = 96.0, Oversample = 2 };

        //Act
        using SKImage image = exporter.Image();

        //Assert
        image.Width.Should().Be(794);
        image.Height.Should().Be(1123);
    }

    [Fact]
    public void auto_crop_trims_the_paper_off_and_leaves_the_ink()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        using var whole = new ImageExporter(page) { Resolution = 96.0, PaperColor = SKColors.White };
        using var cropped = new ImageExporter(page)
        {
            Resolution = 96.0, PaperColor = SKColors.White, AutoCrop = true,
        };

        //Act
        using SKImage wholeImage = whole.Image();
        using SKImage croppedImage = cropped.Image();

        //Assert — a page of music has margins, so the crop must bite; and it
        //must not bite so hard that nothing is left.
        croppedImage.Width.Should().BeLessThan(wholeImage.Width);
        croppedImage.Height.Should().BeLessThan(wholeImage.Height);
        croppedImage.Width.Should().BeGreaterThan(wholeImage.Width / 2);
    }

    [Fact]
    public void auto_crop_works_when_the_page_is_not_displayed_at_one_to_one()
    {
        //Arrange — the page as the Music View actually holds it: fitted into a
        //dock panel, so its DISPLAYED size is nothing like its natural one.
        //The probe that finds the ink has to render at THAT resolution, or the
        //rectangle it answers is in the probe's pixels and the caller reads it
        //as page coordinates — which asks Skia for a surface it cannot
        //allocate. Found on X11; the 1:1 tests above could not see it.
        using SvgPage page = new SvgPage(Fixture("twinkle.svg"));
        page.UpdateSize(96, 96, 0.125);
        using var exporter = new ImageExporter(page)
        {
            Resolution = 300.0, PaperColor = SKColors.White, AutoCrop = true,
        };

        //Act
        using SKImage image = exporter.Image();

        //Assert — a sane picture, not a null and not a gigapixel.
        image.Should().NotBeNull();
        image.Width.Should().BeGreaterThan(100);
        image.Width.Should().BeLessThan(4000);
        image.Height.Should().BeLessThan(6000);
    }

    [Fact]
    public void a_page_asked_for_at_an_impossible_size_answers_nothing_rather_than_throwing()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");

        //Act — Skia answers null rather than throwing when it cannot allocate.
        using SKImage image = page.Image(null, 4_000_000.0, 4_000_000.0, SKColors.White);

        //Assert
        image.Should().BeNull();
    }

    [Fact]
    public void grey_is_grey_on_every_pixel()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        using var exporter = new ImageExporter(page)
        {
            Resolution = 48.0, Grayscale = true, PaperColor = SKColors.White,
        };

        //Act
        using SKImage image = exporter.Image();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        //Assert
        var coloured = new List<SKColor>();
        for (int y = 0; y < bitmap.Height; y += 7)
        {
            for (int x = 0; x < bitmap.Width; x += 7)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Red != pixel.Green || pixel.Green != pixel.Blue) { coloured.Add(pixel); }
            }
        }

        coloured.Should().BeEmpty();
    }

    [Fact]
    public void the_suggested_name_is_never_the_name_it_came_from()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        using var exporter = new ImageExporter(page);

        //Act
        exporter.FileName = "/scores/twinkle.svg";
        string fromSvg = exporter.SuggestedFileName();
        exporter.FileName = "/scores/twinkle.png";
        string fromPng = exporter.SuggestedFileName();

        //Assert
        fromSvg.Should().Be("/scores/twinkle.png");
        fromPng.Should().Be("/scores/twinkle-export.png");
    }

    [Fact]
    public void the_preview_page_is_the_picture_at_its_own_size()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        using var exporter = new ImageExporter(page) { Resolution = 192.0 };

        //Act
        ScorePage preview = exporter.PreviewPage();

        //Assert — the preview's natural size in inches is the file's own.
        preview.Dpi.Should().Be(192.0);
        (preview.PageWidth / preview.Dpi).Should().BeApproximately(794.0 / 96.0, 0.01);
    }
}

/// <summary>Writing a page, or a piece of one, back out as SVG.</summary>
public class SvgExporterTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static SvgPage SizedPage(string name)
    {
        var page = new SvgPage(Fixture(name));
        page.UpdateSize(96, 96, 1.0);
        return page;
    }

    [Fact]
    public void the_bytes_are_an_svg_document()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        var exporter = new SvgExporter(page) { Resolution = 96.0 };

        //Act
        byte[] data = exporter.Data();
        string text = Encoding.UTF8.GetString(data);

        //Assert
        text.Should().Contain("<svg");
        text.Should().Contain("</svg>");
    }

    [Fact]
    public void a_region_is_written_at_the_regions_size()
    {
        //Arrange
        using SvgPage page = SizedPage("twinkle.svg");
        var exporter = new SvgExporter(page, new SKRect(50f, 60f, 250f, 360f)) { Resolution = 96.0 };

        //Act
        string text = Encoding.UTF8.GetString(exporter.Data());

        //Assert
        text.Should().Contain("width=\"200\"");
        text.Should().Contain("height=\"300\"");
    }
}

/// <summary>Finding the ink in a picture.</summary>
public class AutoCroppingTests
{
    private static SKImage Painted(int width, int height, SKColor background, SKRectI ink)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        surface.Canvas.Clear(background);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        surface.Canvas.DrawRect(
            new SKRect(ink.Left, ink.Top, ink.Right, ink.Bottom), paint);
        return surface.Snapshot();
    }

    [Fact]
    public void the_ink_rectangle_is_exactly_what_was_drawn()
    {
        //Arrange
        using SKImage image = Painted(100, 80, SKColors.White, new SKRectI(20, 30, 60, 50));

        //Act
        SKRectI? ink = AutoCropping.InkRect(image);

        //Assert
        ink.Should().NotBeNull();
        ink.Value.Left.Should().Be(20);
        ink.Value.Top.Should().Be(30);
        ink.Value.Right.Should().Be(60);
        ink.Value.Bottom.Should().Be(50);
    }

    [Fact]
    public void a_picture_of_one_colour_has_no_ink_at_all()
    {
        //Arrange
        var info = new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using SKImage image = surface.Snapshot();

        //Act
        SKRectI? ink = AutoCropping.InkRect(image);

        //Assert
        ink.Should().BeNull();
    }

    [Fact]
    public void the_background_is_the_colour_most_of_the_corners_agree_on()
    {
        //Arrange — one dark corner, which a top-left-only reading would take
        //for the background and then crop nothing.
        using SKImage image = Painted(100, 80, SKColors.White, new SKRectI(0, 0, 5, 5));

        //Act
        SKRectI? ink = AutoCropping.InkRect(image);

        //Assert
        ink.Should().NotBeNull();
        ink.Value.Right.Should().Be(5);
        ink.Value.Bottom.Should().Be(5);
    }
}
