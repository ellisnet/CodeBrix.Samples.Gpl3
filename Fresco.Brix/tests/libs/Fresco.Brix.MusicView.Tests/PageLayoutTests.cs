// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.MusicView;
using SilverAssertions;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.MusicView.Tests;

public class PageLayoutTests
{
    private sealed class TestPage : ScorePage
    {
        public TestPage(double width = 720, double height = 360)
        {
            SetPageSize(width, height);
        }

        public override void Paint(SKCanvas canvas, SKRect rect)
        {
        }
    }

    private static PageLayout LayoutOf(int count, double width = 720, double height = 360)
    {
        var layout = new PageLayout { DpiX = 72, DpiY = 72, Margins = new PageMargins(6), Spacing = 8 };
        layout.SetPages(Enumerable.Range(0, count).Select(_ => (ScorePage)new TestPage(width, height)));
        return layout;
    }

    [Fact]
    public void a_column_of_pages_is_spaced_and_centred()
    {
        //Arrange
        var layout = LayoutOf(3);

        //Act
        layout.Update();

        //Assert
        layout[0].Y.Should().Be(6);
        layout[1].Y.Should().Be(6 + 360 + 8);
        layout[2].Y.Should().Be(6 + (2 * (360 + 8)));
        layout.Height.Should().Be(6 + (3 * 360) + (2 * 8) + 6);
    }

    [Fact]
    public void an_empty_layout_is_just_its_margins()
    {
        //Arrange
        var layout = LayoutOf(0);

        //Act
        layout.Update();

        //Assert
        layout.Width.Should().Be(12);
        layout.Height.Should().Be(12);
    }

    [Fact]
    public void fitting_the_width_sets_the_zoom_so_the_widest_page_fills_it()
    {
        //Arrange
        var layout = LayoutOf(2);

        //Act
        layout.Fit(new SKSizeI(372, 1000), ViewMode.FitWidth);
        layout.Update();

        //Assert
        layout.ZoomFactor.Should().BeApproximately(0.5, 0.0001);
        layout[0].Width.Should().Be(360);
    }

    [Fact]
    public void fitting_the_page_takes_the_smaller_of_the_two_zooms()
    {
        //Arrange
        var layout = LayoutOf(1);

        //Act
        layout.Fit(new SKSizeI(732, 192), ViewMode.FitBoth);

        //Assert
        layout.ZoomFactor.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void two_pages_a_row_with_one_in_the_first_row_leaves_the_first_cell_empty()
    {
        //Arrange
        var layout = LayoutOf(3);
        layout.Engine = new RowLayoutEngine { PagesPerRow = 2, PagesFirstRow = 1 };

        //Act
        layout.Update();

        //Assert — page 1 sits in the RIGHT column of the first row, as a
        //title page does in a bound score.
        layout[0].X.Should().BeGreaterThan(layout[1].X);
        layout[1].Y.Should().BeGreaterThan(layout[0].Y);
        layout[2].Y.Should().Be(layout[1].Y);
    }

    [Fact]
    public void two_pages_a_row_starting_left_puts_the_first_two_side_by_side()
    {
        //Arrange
        var layout = LayoutOf(3);
        layout.Engine = new RowLayoutEngine { PagesPerRow = 2, PagesFirstRow = 2 };

        //Act
        layout.Update();

        //Assert
        layout[0].Y.Should().Be(layout[1].Y);
        layout[0].X.Should().BeLessThan(layout[1].X);
        layout[2].Y.Should().BeGreaterThan(layout[1].Y);
    }

    [Fact]
    public void page_at_finds_the_page_under_a_point()
    {
        //Arrange
        var layout = LayoutOf(3);
        layout.Update();

        //Act
        ScorePage page = layout.PageAt(new SKPoint(layout[1].X + 5, layout[1].Y + 5));

        //Assert
        layout.IndexOf(page).Should().Be(1);
    }

    [Fact]
    public void pages_at_finds_every_page_a_rectangle_touches()
    {
        //Arrange
        var layout = LayoutOf(3);
        layout.Update();
        var rect = new SKRect(0, layout[0].Y + 300, 1000, layout[1].Y + 5);

        //Act
        List<ScorePage> pages = layout.PagesAt(rect).ToList();

        //Assert
        pages.Count.Should().Be(2);
    }

    [Fact]
    public void a_recorded_position_survives_a_change_of_zoom()
    {
        //Arrange
        var layout = LayoutOf(3);
        layout.Update();
        var spot = new SKPoint(layout[1].X + 180, layout[1].Y + 90); //middle of page two

        //Act
        var offset = layout.PositionToOffset(spot);
        layout.ZoomFactor = 2.0;
        layout.Update();
        SKPointI restored = layout.OffsetToPosition(offset);

        //Assert
        offset.Index.Should().Be(1);
        restored.X.Should().Be(layout[1].X + 360);
        restored.Y.Should().Be(layout[1].Y + 180);
    }

    [Fact]
    public void out_of_continuous_mode_only_the_current_page_set_is_shown()
    {
        //Arrange
        var layout = LayoutOf(4);
        layout.ContinuousMode = false;
        layout.CurrentPageSet = 2;

        //Act
        layout.Update();

        //Assert
        layout.DisplayPages().Count.Should().Be(1);
        layout.DisplayPages()[0].Should().BeSameAs(layout[2]);
        layout.PageSetCount().Should().Be(4);
    }

    [Fact]
    public void a_two_page_engine_shows_a_whole_spread_at_a_time()
    {
        //Arrange
        var layout = LayoutOf(5);
        layout.Engine = new RowLayoutEngine { PagesPerRow = 2, PagesFirstRow = 1 };
        layout.ContinuousMode = false;
        layout.CurrentPageSet = 1;

        //Act
        layout.Update();

        //Assert — the first set is the lone first page, the second a spread.
        layout.DisplayPages().Count.Should().Be(2);
        layout.PageSet(0).Should().Be(0);
        layout.PageSet(2).Should().Be(1);
    }

    [Fact]
    public void the_raster_engine_fills_the_width_with_columns_instead_of_zooming()
    {
        //Arrange
        var layout = LayoutOf(6, 100, 100);
        layout.Engine = new RasterLayoutEngine();

        //Act
        layout.Fit(new SKSizeI(360, 400), ViewMode.FitWidth);
        layout.Update();

        //Assert
        layout.ZoomFactor.Should().Be(1.0);
        layout.Count(p => p.Y == layout[0].Y).Should().Be(3);
    }

    [Fact]
    public void rotating_the_layout_turns_every_page()
    {
        //Arrange
        var layout = LayoutOf(2);
        layout.Rotation = Rotation.Rotate90;

        //Act
        layout.Update();

        //Assert
        layout[0].ComputedRotation.Should().Be(Rotation.Rotate90);
        layout[0].Width.Should().Be(360);
        layout[0].Height.Should().Be(720);
    }
}
