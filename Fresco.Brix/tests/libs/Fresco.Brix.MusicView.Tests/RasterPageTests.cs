using Fresco.Brix.MusicView;
using SilverAssertions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.MusicView.Tests;

/// <summary>
/// A page that is a picture rather than a drawing — the port of qpageview's
/// ImagePage, which the documentation panel's PDF pages are.
/// </summary>
public class RasterPageTests
{
    /// <summary>A source the test drives by hand.</summary>
    private sealed class Source : IPageImageSource
    {
        private readonly SKImage _image;

        internal Source(double width = 595, double height = 842, SKImage image = null)
        {
            NaturalSize = (width, height);
            _image = image;
        }

        public event EventHandler ImageReady;

        internal int Asks { get; private set; }

        internal int LastWidth { get; private set; }

        internal int LastHeight { get; private set; }

        public (double Width, double Height) NaturalSize { get; }

        public SKImage Image(int widthPixels, int heightPixels)
        {
            Asks++;
            LastWidth = widthPixels;
            LastHeight = heightPixels;
            return _image;
        }

        internal void Announce() => ImageReady?.Invoke(this, EventArgs.Empty);
    }

    private static SKImage RedImage(int width, int height)
    {
        using SKBitmap bitmap = new SKBitmap(width, height);
        using (SKCanvas canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
        }

        return SKImage.FromBitmap(bitmap);
    }

    [Fact]
    public void a_page_learns_its_size_from_its_source_before_anything_is_drawn()
    {
        //Arrange
        RasterPage page = new RasterPage(new Source(595, 842), 3);

        //Assert — the layout asks for the size first, and a page over a file
        //has not read that file yet (board trap 33).
        page.PageWidth.Should().Be(595);
        page.PageHeight.Should().Be(842);
        page.Dpi.Should().Be(RasterPage.PdfDpi);
        page.Number.Should().Be(3);
    }

    [Fact]
    public void a_source_that_answers_nothing_leaves_the_page_at_its_default_size()
    {
        //Arrange
        RasterPage page = new RasterPage(new Source(0, 0));

        //Assert — a zero size is not written over the default, so the layout
        //still has a rectangle to work with.
        page.PageWidth.Should().BeGreaterThan(0);
        page.PageHeight.Should().BeGreaterThan(0);
    }

    [Fact]
    public void one_page_is_made_per_source_and_they_are_numbered_from_one()
    {
        //Arrange
        var sources = Enumerable.Range(0, 4).Select(_ => (IPageImageSource)new Source()).ToList();

        //Act
        var pages = RasterPage.Load(sources);

        //Assert
        pages.Should().HaveCount(4);
        pages.Select(p => p.Number).Should().Equal(1, 2, 3, 4);
        RasterPage.Load(null).Should().BeEmpty();
    }

    [Fact]
    public void painting_asks_the_source_for_the_displayed_size()
    {
        //Arrange
        Source source = new Source();
        RasterPage page = new RasterPage(source);
        page.UpdateSize(72, 72, 1.5);

        using SKBitmap bitmap = new SKBitmap(page.Width, page.Height);
        using SKCanvas canvas = new SKCanvas(bitmap);

        //Act
        page.Paint(canvas, page.PageRect);

        //Assert
        source.Asks.Should().Be(1);
        source.LastWidth.Should().Be(page.Width);
        source.LastHeight.Should().Be(page.Height);
    }

    [Fact]
    public void a_page_whose_picture_has_not_arrived_is_a_sheet_of_paper()
    {
        //Arrange
        RasterPage page = new RasterPage(new Source()) { PaperColor = SKColors.White };
        page.UpdateSize(72, 72, 0.2);

        using SKBitmap bitmap = new SKBitmap(page.Width, page.Height);
        using SKCanvas canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        //Act
        page.Paint(canvas, page.PageRect);

        //Assert — paper, not a hole in the view.
        bitmap.GetPixel(page.Width / 2, page.Height / 2).Should().Be(SKColors.White);
    }

    [Fact]
    public void a_picture_of_another_size_is_scaled_into_the_page()
    {
        //Arrange — this is what a reader sees mid-zoom: the last rendering,
        //stretched, rather than a blank page.
        using SKImage image = RedImage(40, 56);
        RasterPage page = new RasterPage(new Source(595, 842, image));
        page.UpdateSize(72, 72, 0.5);

        using SKBitmap bitmap = new SKBitmap(page.Width, page.Height);
        using SKCanvas canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        //Act
        page.Paint(canvas, page.PageRect);

        //Assert
        SKColor middle = bitmap.GetPixel(page.Width / 2, page.Height / 2);
        middle.Red.Should().BeGreaterThan((byte)200);
        middle.Green.Should().BeLessThan((byte)60);
    }

    [Fact]
    public void a_raster_page_carries_no_links()
    {
        //Arrange — upstream's ImagePage has none either, and the
        //point-and-click machinery has nothing to say about a manual.
        RasterPage page = new RasterPage(new Source());

        //Assert
        page.Links().Count.Should().Be(0);
        page.LinksAt(new SKPoint(0.5f, 0.5f)).Should().BeEmpty();
    }

    [Fact]
    public void a_source_announces_a_picture_that_arrived_later()
    {
        //Arrange
        Source source = new Source();
        RasterPage page = new RasterPage(source);
        int announced = 0;
        source.ImageReady += (_, _) => announced++;

        //Act
        source.Announce();

        //Assert — this is how the view learns to repaint.
        announced.Should().Be(1);
        page.Should().NotBeNull();
    }

    [Fact]
    public void a_page_needs_a_source()
    {
        //Assert
        Assert.Throws<ArgumentNullException>(() => new RasterPage(null));
    }
}
