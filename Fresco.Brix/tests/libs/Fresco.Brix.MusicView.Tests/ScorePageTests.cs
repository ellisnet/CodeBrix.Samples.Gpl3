// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.MusicView;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace Fresco.Brix.MusicView.Tests;

public class ScorePageTests
{
    private sealed class TestPage : ScorePage
    {
        public TestPage(double width = 595.28, double height = 841.89)
        {
            SetPageSize(width, height);
        }

        public override void Paint(SKCanvas canvas, SKRect rect)
        {
        }
    }

    [Fact]
    public void update_size_takes_the_natural_size_through_dpi_and_zoom()
    {
        //Arrange
        var page = new TestPage(720, 360); //ten inches by five at 72 dpi

        //Act
        page.UpdateSize(72, 72, 2.0);

        //Assert
        page.Width.Should().Be(1440);
        page.Height.Should().Be(720);
    }

    [Fact]
    public void update_size_honours_a_view_resolution_other_than_the_pages()
    {
        //Arrange
        var page = new TestPage(720, 360);

        //Act
        page.UpdateSize(96, 96, 1.0);

        //Assert
        page.Width.Should().Be(960);
        page.Height.Should().Be(480);
    }

    [Fact]
    public void a_quarter_turn_swaps_the_displayed_width_and_height()
    {
        //Arrange
        var page = new TestPage(720, 360) { ComputedRotation = Rotation.Rotate90 };

        //Act
        page.UpdateSize(72, 72, 1.0);

        //Assert
        page.Width.Should().Be(360);
        page.Height.Should().Be(720);
    }

    [Fact]
    public void zoom_for_width_is_the_zoom_that_makes_the_page_that_wide()
    {
        //Arrange
        var page = new TestPage(720, 360);

        //Act
        double zoom = page.ZoomForWidth(1440, Rotation.Rotate0, 72);

        //Assert
        zoom.Should().Be(2.0);
    }

    [Fact]
    public void zoom_for_height_is_the_zoom_that_makes_the_page_that_high()
    {
        //Arrange
        var page = new TestPage(720, 360);

        //Act
        double zoom = page.ZoomForHeight(180, Rotation.Rotate0, 72);

        //Assert
        zoom.Should().Be(0.5);
    }

    [Fact]
    public void map_to_page_turns_a_fraction_of_the_page_into_pixels()
    {
        //Arrange
        var page = new TestPage(720, 360);
        page.UpdateSize(72, 72, 1.0);

        //Act
        SKPoint middle = page.MapToPage(1, 1).MapPoint(new SKPoint(0.5f, 0.25f));

        //Assert
        middle.X.Should().BeApproximately(360f, 0.01f);
        middle.Y.Should().BeApproximately(90f, 0.01f);
    }

    [Fact]
    public void map_from_page_undoes_map_to_page()
    {
        //Arrange
        var page = new TestPage(720, 360);
        page.UpdateSize(96, 96, 1.5);
        var original = new SKPoint(0.3f, 0.8f);

        //Act
        SKPoint round = page.MapFromPage(1, 1).MapPoint(page.MapToPage(1, 1).MapPoint(original));

        //Assert
        round.X.Should().BeApproximately(original.X, 0.001f);
        round.Y.Should().BeApproximately(original.Y, 0.001f);
    }

    [Fact]
    public void a_link_rect_lands_where_the_area_says_it_should()
    {
        //Arrange
        var page = new TestPage(720, 360);
        page.UpdateSize(72, 72, 1.0);
        var link = new Link(0.25f, 0.5f, 0.75f, 0.75f, "textedit:///x.ly:1:0:0");

        //Act
        SKRect rect = page.LinkRect(link);

        //Assert
        rect.Left.Should().BeApproximately(180f, 0.01f);
        rect.Top.Should().BeApproximately(180f, 0.01f);
        rect.Right.Should().BeApproximately(540f, 0.01f);
        rect.Bottom.Should().BeApproximately(270f, 0.01f);
    }

    [Fact]
    public void geometry_follows_the_position_the_layout_gave_the_page()
    {
        //Arrange
        var page = new TestPage(720, 360);
        page.UpdateSize(72, 72, 1.0);

        //Act
        page.X = 15;
        page.Y = 25;

        //Assert
        page.Geometry.Should().Be(new SKRectI(15, 25, 735, 385));
    }
}
