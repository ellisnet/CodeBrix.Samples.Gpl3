// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The stencil expression walk — where a grob's skyline comes from when it is traced
/// out of the grob's own drawing rather than taken from its bounding box.
/// <para>
/// Every test here asks the same question in a different shape: does the skyline follow
/// the INK, or has it quietly collapsed back to the rectangle? A box is always a
/// correct-looking answer and almost always a wrong one, so the assertions are written
/// to fail if the walk stops working — a sloping edge must slope, a round edge must
/// curve, and only an expression that genuinely cannot be decomposed may come back
/// square.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class StencilIntegralTests
{
    private static readonly Symbol RoundFilledBox = Symbol.Intern("round-filled-box");
    private static readonly Symbol DrawLine = Symbol.Intern("draw-line");
    private static readonly Symbol Circle = Symbol.Intern("circle");
    private static readonly Symbol Polygon = Symbol.Intern("polygon");
    private static readonly Symbol Path = Symbol.Intern("path");
    private static readonly Symbol CombineStencil = Symbol.Intern("combine-stencil");
    private static readonly Symbol TranslateStencil = Symbol.Intern("translate-stencil");
    private static readonly Symbol EmbeddedPs = Symbol.Intern("embedded-ps");
    private static readonly Symbol Utf8String = Symbol.Intern("utf-8-string");
    private static readonly Symbol MoveTo = Symbol.Intern("moveto");
    private static readonly Symbol LineTo = Symbol.Intern("lineto");
    private static readonly Symbol RLineTo = Symbol.Intern("rlineto");
    private static readonly Symbol ClosePath = Symbol.Intern("closepath");

    private static Stencil Make(object expression, Interval x, Interval y)
        => new Stencil(new Box(x, y), expression);

    private static SkylinePair Trace(Stencil stencil, Axis axis)
        => StencilIntegral.SkylinesFromStencil(stencil, false, axis);

    [Fact]
    public void a_filled_box_traces_its_own_edges()
    {
        //Arrange
        Stencil stencil = Make(
            Pair.List(RoundFilledBox, 1.0, 2.0, 0.5, 3.0, 0.0),
            new Interval(-1.0, 2.0),
            new Interval(-0.5, 3.0));

        //Act
        SkylinePair pair = Trace(stencil, Axis.X);

        //Assert
        pair.Up.Height(0.0).Should().BeApproximately(3.0, 1e-9);
        pair.Down.Height(0.0).Should().BeApproximately(-0.5, 1e-9);
        pair.Left().Should().BeApproximately(-1.0, 1e-9);
        pair.Right().Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void a_diagonal_line_gives_a_sloping_skyline_not_a_rectangle()
    {
        //Arrange
        Stencil stencil = Make(
            Pair.List(DrawLine, 0.1, 0.0, 0.0, 10.0, 5.0),
            new Interval(0.0, 10.0),
            new Interval(0.0, 5.0));

        //Act
        SkylinePair pair = Trace(stencil, Axis.X);

        //Assert
        // The whole point of the walk: at the left end the line is near the bottom, and
        // the bounding box would say it reaches 5 everywhere.
        pair.Up.Height(0.5).Should().BeLessThan(1.0);
        pair.Up.Height(9.5).Should().BeGreaterThan(4.0);
        pair.Down.Height(0.5).Should().BeLessThan(1.0);
    }

    [Fact]
    public void a_circle_is_traced_round_rather_than_square()
    {
        //Arrange
        Stencil stencil = Make(
            Pair.List(Circle, 5.0, 0.0, true),
            new Interval(-5.0, 5.0),
            new Interval(-5.0, 5.0));

        //Act
        SkylinePair pair = Trace(stencil, Axis.X);

        //Assert
        pair.Up.Height(0.0).Should().BeApproximately(5.0, 0.05);

        // At four fifths of the radius a circle has dropped to 3; a square has not.
        pair.Up.Height(4.0).Should().BeApproximately(3.0, 0.2);
        pair.Down.Height(4.0).Should().BeApproximately(-3.0, 0.2);
    }

    [Fact]
    public void a_polygon_traces_the_edges_between_its_points()
    {
        //Arrange
        // A right triangle with the hypotenuse rising left to right.
        Stencil stencil = Make(
            Pair.List(Polygon, Pair.List(0.0, 0.0, 4.0, 0.0, 4.0, 4.0), 0.0, true),
            new Interval(0.0, 4.0),
            new Interval(0.0, 4.0));

        //Act
        SkylinePair pair = Trace(stencil, Axis.X);

        //Assert
        pair.Up.Height(1.0).Should().BeApproximately(1.0, 0.05);
        pair.Up.Height(3.0).Should().BeApproximately(3.0, 0.05);
    }

    [Fact]
    public void relative_path_commands_trace_the_same_outline_as_absolute_ones()
    {
        //Arrange
        object absolute = Pair.List(
            Path, 0.0,
            Pair.List(MoveTo, 0.0, 0.0, LineTo, 4.0, 4.0, LineTo, 8.0, 0.0, ClosePath));
        object relative = Pair.List(
            Path, 0.0,
            Pair.List(MoveTo, 0.0, 0.0, RLineTo, 4.0, 4.0, RLineTo, 4.0, -4.0, ClosePath));

        Interval x = new Interval(0.0, 8.0);
        Interval y = new Interval(0.0, 4.0);

        //Act
        SkylinePair fromAbsolute = Trace(Make(absolute, x, y), Axis.X);
        SkylinePair fromRelative = Trace(Make(relative, x, y), Axis.X);

        //Assert
        foreach (double sample in new[] { 0.5, 2.0, 4.0, 6.0, 7.5 })
        {
            fromRelative.Up.Height(sample).Should()
                .BeApproximately(fromAbsolute.Up.Height(sample), 1e-9);
        }

        // And it is a peak, not a plateau — the grouping really did produce two edges.
        fromAbsolute.Up.Height(4.0).Should().BeApproximately(4.0, 0.05);
        fromAbsolute.Up.Height(1.0).Should().BeApproximately(1.0, 0.05);
    }

    [Fact]
    public void a_combination_unions_what_its_parts_trace()
    {
        //Arrange
        object low = Pair.List(RoundFilledBox, 0.0, 2.0, 0.0, 1.0, 0.0);
        object high = Pair.List(
            TranslateStencil,
            new Pair(2.0, 0.0),
            Pair.List(RoundFilledBox, 0.0, 2.0, 0.0, 3.0, 0.0));

        Stencil stencil = Make(
            Pair.List(CombineStencil, low, high),
            new Interval(0.0, 4.0),
            new Interval(0.0, 3.0));

        //Act
        SkylinePair pair = Trace(stencil, Axis.X);

        //Assert
        pair.Up.Height(1.0).Should().BeApproximately(1.0, 1e-9);
        pair.Up.Height(3.0).Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void an_expression_that_cannot_be_decomposed_falls_back_to_the_extent_box()
    {
        //Arrange
        // embedded-ps is upstream's own example of an expression the walk knows nothing
        // about. The fallback is what keeps such a grob from claiming no room at all.
        Stencil stencil = Make(
            Pair.List(EmbeddedPs, new MutableString("0 0 moveto")),
            new Interval(-1.0, 1.0),
            new Interval(-2.0, 4.0));

        //Act
        SkylinePair pair = Trace(stencil, Axis.X);

        //Assert
        pair.Up.Height(0.0).Should().BeApproximately(4.0, 1e-9);
        pair.Down.Height(0.0).Should().BeApproximately(-2.0, 1e-9);
    }

    [Fact]
    public void a_text_node_with_no_shaped_run_contributes_nothing_but_does_not_stop_the_walk()
    {
        //Arrange
        // EPG14 CLOSED the divergence this test used to fence. Until then the walk
        // refused to descend into ANY stencil containing text and took the whole extent
        // box instead -- over-reserving, which was the only safe direction while the
        // port's utf-8-string carried no inner drawing. It carries the shaped run now
        // (see THE STENCIL EXPRESSION WALK in Engine/PORT-COVERAGE.txt), so text is
        // graded like any other ink.
        //
        // This hand-built node has an EMPTY fourth element, which is what a run of
        // characters no face in the chain covers would produce. The walk must descend,
        // find nothing to trace, and report only what was really drawn -- the line's own
        // half-thickness of 0.05 -- rather than inventing the 3.0 box.
        object text = Pair.List(
            Utf8String, new MutableString("serif 12"), new MutableString("Allegro"), Nil.Instance);
        object drawn = Pair.List(DrawLine, 0.1, 0.0, 0.0, 10.0, 0.0);

        Stencil stencil = Make(
            Pair.List(CombineStencil, drawn, text),
            new Interval(0.0, 10.0),
            new Interval(0.0, 3.0));

        //Act
        SkylinePair pair = Trace(stencil, Axis.X);

        //Assert
        // Half the 0.1 line thickness, above the y = 0 the line is drawn along.
        pair.Up.Height(5.0).Should().BeApproximately(0.05, 1e-9);
    }

    [Fact]
    public void a_real_text_stencil_traces_the_ink_of_its_glyphs()
    {
        //Arrange
        // The other half, and the one that proves the close is worth anything: a stencil
        // built by TextFontMetric itself, whose fourth element holds the run it resolved.
        // "Allegro" has ascenders and a descender, so its traced ink must have real
        // height in BOTH directions -- and must stay INSIDE the advance-and-ink box the
        // metric measured, because tracing outlines can only ever report less than the
        // box that was computed to contain them.
        TextFontMetric metric = new TextFontMetric("serif", false, false, false, 12.0, 1.0);
        Stencil stencil = metric.TextStencil("Allegro");
        stencil.IsEmpty.Should().BeFalse();

        //Act
        SkylinePair pair = Trace(stencil, Axis.X);

        //Assert
        Interval box = stencil.YExtent;
        pair.Up.MaxHeight().Should().BeGreaterThan(0.0);
        pair.Up.MaxHeight().Should().BeLessThanOrEqualTo(box.Right + 1e-6);
        pair.Down.MaxHeight().Should().BeGreaterThanOrEqualTo(box.Left - 1e-6);
    }

    [Fact]
    public void a_music_glyph_traces_its_real_outline()
    {
        //Arrange
        OpenTypeFontMetric metric = LoadMusicFont();
        Stencil stencil = metric.FindByName("noteheads.s2");
        stencil.IsEmpty.Should().BeFalse();

        //Act
        SkylinePair pair = Trace(stencil, Axis.X);

        //Assert
        // The traced outline agrees with the extents the font's own metadata declares.
        Interval y = stencil.YExtent;
        // MaxHeight is signed by the skyline's direction, so the downward one answers
        // with the lowest y the outline reaches.
        pair.Up.MaxHeight().Should().BeApproximately(y.Right, 0.05);
        pair.Down.MaxHeight().Should().BeApproximately(y.Left, 0.05);

        // And it is an OUTLINE, not the box: a note head is an oval, so near its left
        // edge it is markedly lower than at its middle.
        double middle = stencil.XExtent.Center;
        double nearEdge = stencil.XExtent.Left + (stencil.XExtent.Length * 0.05);
        pair.Up.Height(nearEdge).Should().BeLessThan(pair.Up.Height(middle) - 0.05);
    }

    [Fact]
    public void the_collector_takes_a_box_the_same_way_the_skyline_pair_does()
    {
        //Arrange
        Box box = new Box(new Interval(-1.0, 3.0), new Interval(-2.0, 5.0));
        LazySkylinePair lazy = new LazySkylinePair(Axis.X);

        //Act
        lazy.AddBox(Transform.Identity, box);
        SkylinePair traced = lazy.ToPair();
        SkylinePair expected = new SkylinePair(box, Axis.X);

        //Assert
        foreach (double sample in new[] { -0.5, 0.0, 1.0, 2.5 })
        {
            traced.Up.Height(sample).Should().BeApproximately(expected.Up.Height(sample), 1e-9);
            traced.Down.Height(sample).Should().BeApproximately(expected.Down.Height(sample), 1e-9);
        }
    }

    [Fact]
    public void the_collector_reports_itself_empty_until_something_is_added()
    {
        //Arrange
        LazySkylinePair lazy = new LazySkylinePair(Axis.Y);

        //Act
        bool emptyBefore = lazy.IsEmpty;
        lazy.AddSegment(Transform.Identity, Offset.Zero, new Offset(1.0, 1.0));

        //Assert
        emptyBefore.Should().BeTrue();
        lazy.IsEmpty.Should().BeFalse();
    }

    private static OpenTypeFontMetric LoadMusicFont()
    {
        byte[] bytes = FontAssets.MusicFont("emmentaler-20");
        bytes.Should().NotBeNull();

        Interpreter interpreter = new Interpreter();
        SchemeBootstrap.LoadCore(interpreter);

        OpenTypeFont font = new OpenTypeFont(bytes, "emmentaler-20", interpreter);
        return new OpenTypeFontMetric(font, "emmentaler-20");
    }
}
