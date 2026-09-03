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
/// A stand-in view, so the selection arithmetic can be exercised without a
/// window. It answers the four questions <see cref="IOverlayHost"/> asks and
/// counts the repaints, which is all either overlay uses it for.
/// </summary>
internal sealed class FakeOverlayHost : IOverlayHost
{
    internal FakeOverlayHost(PageLayout layout) => Layout = layout;

    public SKPointI ViewOffset { get; set; }

    public double ZoomFactor { get; set; } = 1.0;

    public PageLayout Layout { get; }

    public SKColor PaperColor { get; set; } = SKColors.White;

    public int Repaints { get; private set; }

    public void Invalidate() => Repaints++;
}

/// <summary>Selecting a rectangular region over the pages.</summary>
public class RubberBandTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static (RubberBand Band, FakeOverlayHost Host) Band()
    {
        var layout = new PageLayout();
        layout.SetPages(new ScorePage[]
        {
            new SvgPage(Fixture("twopage-1.svg")), new SvgPage(Fixture("twopage-2.svg")),
        });
        layout.Update();
        var host = new FakeOverlayHost(layout);
        return (new RubberBand(host), host);
    }

    [Fact]
    public void a_new_band_starts_empty_and_selects_nothing()
    {
        //Arrange
        var (band, _) = Band();

        //Assert
        band.HasSelection.Should().BeFalse();
        band.SelectedPages().Should().BeEmpty();
        band.SelectedPage().Page.Should().BeNull();
    }

    [Fact]
    public void dragging_out_a_band_selects_what_was_dragged_over()
    {
        //Arrange
        var (band, _) = Band();
        SKRectI announced = SKRectI.Empty;
        band.SelectionChanged += (_, rect) => announced = rect;

        //Act
        band.BeginNew(new SKPointI(40, 50));
        band.Drag(new SKPointI(240, 350));
        band.EndDrag();

        //Assert
        band.HasSelection.Should().BeTrue();
        band.Selection.Left.Should().Be(40);
        band.Selection.Top.Should().Be(50);
        band.Selection.Width.Should().Be(200);
        band.Selection.Height.Should().Be(300);
        announced.Should().Be(band.Selection);
    }

    [Fact]
    public void a_drag_of_a_few_pixels_is_a_click_that_missed_and_selects_nothing()
    {
        //Arrange — upstream's own threshold: under eight pixels each way.
        var (band, _) = Band();

        //Act
        band.BeginNew(new SKPointI(100, 100));
        band.Drag(new SKPointI(104, 103));
        band.EndDrag();

        //Assert
        band.HasSelection.Should().BeFalse();
    }

    [Fact]
    public void nothing_is_announced_until_the_drag_ends_unless_it_is_tracked()
    {
        //Arrange
        var (quiet, _) = Band();
        var (tracked, _) = Band();
        tracked.TrackSelection = true;
        int quietChanges = 0;
        int trackedChanges = 0;
        quiet.SelectionChanged += (_, _) => quietChanges++;
        tracked.SelectionChanged += (_, _) => trackedChanges++;

        //Act
        foreach (RubberBand band in new[] { quiet, tracked })
        {
            band.BeginNew(new SKPointI(10, 10));
            band.Drag(new SKPointI(60, 60));
            band.Drag(new SKPointI(110, 110));
            band.Drag(new SKPointI(160, 160));
        }

        //Assert — mid-drag.
        quietChanges.Should().Be(0);
        trackedChanges.Should().Be(3);
    }

    [Theory]
    [InlineData(2, 2, RubberBandEdge.Left | RubberBandEdge.Top)]
    [InlineData(100, 2, RubberBandEdge.Top)]
    [InlineData(198, 2, RubberBandEdge.Right | RubberBandEdge.Top)]
    [InlineData(2, 100, RubberBandEdge.Left)]
    [InlineData(100, 100, RubberBandEdge.Inside)]
    [InlineData(198, 198, RubberBandEdge.Right | RubberBandEdge.Bottom)]
    [InlineData(400, 400, RubberBandEdge.Outside)]
    public void the_edge_a_point_touches_is_the_edge_it_is_within_eight_pixels_of(
        int x, int y, RubberBandEdge expected)
    {
        //Arrange — a band from (0,0) to (200,200) with the view unscrolled.
        var (band, _) = Band();
        band.SetSelection(new SKRectI(0, 0, 200, 200));

        //Act
        RubberBandEdge edge = band.EdgeAt(new SKPointI(x, y));

        //Assert
        edge.Should().Be(expected);
    }

    [Theory]
    [InlineData(RubberBandEdge.Outside, true)]
    [InlineData(RubberBandEdge.Left, true)]
    [InlineData(RubberBandEdge.Right | RubberBandEdge.Bottom, true)]
    [InlineData(RubberBandEdge.Inside, false)]
    public void a_right_press_inside_the_band_starts_nothing(
        RubberBandEdge edge, bool expected)
    {
        //Arrange, Act, Assert — upstream's own rule, one line of it:
        //qpageview/rubberband.py:400-402 starts a drag for the show button only
        //when the press is NOT inside the band, so right-clicking a selection
        //reaches the context menu with the selection still there. Without it,
        //the click wiped what the user had just selected and Copy to Image
        //never appeared (found on X11 at board wave W15; the Music View had the
        //same fault).
        MusicViewControl.StartsNewBand(edge).Should().Be(expected);
    }

    [Fact]
    public void dragging_an_edge_moves_only_that_edge()
    {
        //Arrange
        var (band, _) = Band();
        band.SetSelection(new SKRectI(100, 100, 300, 300));

        //Act — take hold of the right edge and pull it out by 50.
        band.BeginDrag(new SKPointI(296, 200)).Should().BeTrue();
        band.Drag(new SKPointI(346, 200));
        band.EndDrag();

        //Assert
        band.Selection.Left.Should().Be(100);
        band.Selection.Right.Should().Be(350);
        band.Selection.Top.Should().Be(100);
        band.Selection.Bottom.Should().Be(300);
    }

    [Fact]
    public void dragging_the_middle_moves_the_whole_band()
    {
        //Arrange
        var (band, _) = Band();
        band.SetSelection(new SKRectI(100, 100, 300, 300));

        //Act
        band.BeginDrag(new SKPointI(200, 200)).Should().BeTrue();
        band.Drag(new SKPointI(230, 260));
        band.EndDrag();

        //Assert
        band.Selection.Should().Be(new SKRectI(130, 160, 330, 360));
    }

    [Fact]
    public void a_press_nowhere_near_the_band_starts_no_drag()
    {
        //Arrange
        var (band, _) = Band();
        band.SetSelection(new SKRectI(100, 100, 300, 300));

        //Act
        bool started = band.BeginDrag(new SKPointI(500, 500));

        //Assert
        started.Should().BeFalse();
        band.IsDragging.Should().BeFalse();
    }

    [Fact]
    public void the_selection_is_in_layout_coordinates_so_scrolling_does_not_move_it()
    {
        //Arrange
        var (band, host) = Band();
        band.SetSelection(new SKRectI(100, 100, 300, 300));

        //Act
        host.ViewOffset = new SKPointI(0, 250);

        //Assert — the selection is where it was; where it is DRAWN moved.
        band.Selection.Should().Be(new SKRectI(100, 100, 300, 300));
        band.ViewRect().Should().Be(new SKRectI(100, -150, 300, 50));
    }

    [Fact]
    public void zooming_scales_the_band_with_the_music_under_it()
    {
        //Arrange
        var (band, host) = Band();
        band.SetSelection(new SKRectI(100, 100, 300, 300));

        //Act
        host.ZoomFactor = 2.0;
        band.ZoomChanged(2.0);

        //Assert
        band.Selection.Should().Be(new SKRectI(200, 200, 600, 600));
    }

    [Fact]
    public void a_selection_reaching_two_pages_names_both_and_picks_the_bigger()
    {
        //Arrange — the layout stacks the two pages, so a tall band spans them.
        var (band, host) = Band();
        ScorePage first = host.Layout[0];
        ScorePage second = host.Layout[1];
        int boundary = second.Y;
        band.SetSelection(new SKRectI(
            first.X + 50, boundary - 60, first.X + 250, boundary + 200));

        //Act
        List<(ScorePage Page, SKRect Rect)> pages = band.SelectedPages().ToList();
        var (biggest, rect) = band.SelectedPage();

        //Assert
        pages.Count.Should().Be(2);
        biggest.Should().BeSameAs(second);
        ((double)rect.Height).Should().BeApproximately(200.0, 1.0);
    }

    [Fact]
    public void the_selected_region_renders_at_the_resolution_it_is_asked_for()
    {
        //Arrange
        var (band, host) = Band();
        ScorePage page = host.Layout[0];
        band.SetSelection(new SKRectI(page.X + 20, page.Y + 30, page.X + 220, page.Y + 180));

        //Act
        using SKImage image = band.SelectedImage(192.0, SKColors.White);

        //Assert — 200 by 150 displayed pixels at 96 dpi, asked for at 192.
        image.Should().NotBeNull();
        image.Width.Should().Be(400);
        image.Height.Should().Be(300);
    }

    [Fact]
    public void the_links_inside_the_selection_are_the_ones_it_wholly_contains()
    {
        //Arrange — the whole of the first page, so every link on it qualifies.
        var (band, host) = Band();
        ScorePage page = host.Layout[0];
        band.SetSelection(new SKRectI(page.X, page.Y, page.X + page.Width, page.Y + page.Height));

        //Act
        List<(ScorePage Page, IReadOnlyList<Link> Links)> found = band.SelectedLinks().ToList();

        //Assert
        found.Count.Should().Be(1);
        found[0].Links.Count.Should().Be(page.Links().Count);
    }
}

/// <summary>The magnifying glass's own arithmetic.</summary>
public class MagnifierTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static (Magnifier Glass, FakeOverlayHost Host) Glass()
    {
        var layout = new PageLayout();
        layout.SetPages(new ScorePage[] { new SvgPage(Fixture("twinkle.svg")) });
        layout.Update();
        var host = new FakeOverlayHost(layout);
        return (new Magnifier(host), host);
    }

    [Fact]
    public void the_glass_is_hidden_until_it_is_shown_and_hides_again_after()
    {
        //Arrange
        var (glass, _) = Glass();

        //Act & Assert
        glass.IsVisible.Should().BeFalse();
        glass.Show(new SKPointI(100, 100));
        glass.IsVisible.Should().BeTrue();
        glass.Hide();
        glass.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void dragging_the_second_button_grows_the_glass_by_twice_the_drop()
    {
        //Arrange
        var (glass, _) = Glass();
        glass.Show(new SKPointI(200, 200));
        glass.Size = 200;

        //Act — the first move only records where the resize began.
        glass.Resize(new SKPointI(200, 200));
        glass.Resize(new SKPointI(200, 240));

        //Assert
        glass.Size.Should().Be(280);
    }

    [Fact]
    public void the_glass_never_grows_past_its_limits()
    {
        //Arrange
        var (glass, _) = Glass();

        //Act & Assert
        glass.Size = 10;
        glass.Size.Should().Be(Magnifier.MinimumSize);
        glass.Size = 10000;
        glass.Size.Should().Be(Magnifier.MaximumSize);
    }

    [Fact]
    public void wheeling_zooms_the_glass_and_not_past_what_the_view_would_allow()
    {
        //Arrange
        var (glass, host) = Glass();
        host.ZoomFactor = 1.0;
        glass.Scale = 3.0;

        //Act
        glass.ZoomBy(1);
        double zoomedIn = glass.Scale;
        glass.ZoomBy(200);

        //Assert
        zoomedIn.Should().BeApproximately(3.3, 0.001);
        glass.Scale.Should().BeApproximately(
            MusicViewControl.MaxZoom * Magnifier.MaxExtraZoom, 0.001);
    }

    [Fact]
    public void moving_the_glass_only_does_anything_while_it_is_up()
    {
        //Arrange
        var (glass, host) = Glass();

        //Act
        glass.MoveCenter(new SKPointI(50, 50));
        int whileHidden = host.Repaints;
        glass.Show(new SKPointI(10, 10));
        glass.MoveCenter(new SKPointI(50, 50));

        //Assert
        whileHidden.Should().Be(0);
        glass.Center.Should().Be(new SKPointI(50, 50));
    }
}
