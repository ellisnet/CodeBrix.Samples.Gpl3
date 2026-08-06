// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Backends;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Backends.Tests;

/// <summary>
/// The stencil interpreter and the headless SVG backend.
/// <para>
/// Two things are pinned throughout: the interpreter FLATTENS the tree so that every
/// drawing command sits between an explicit translation and its reset, and the SVG
/// writer negates Y exactly once, because LilyPond measures upward and SVG measures
/// downward.
/// </para>
/// </summary>
public class SvgBackendTests
{
    private sealed class RecordingSink : IStencilSink
    {
        public List<string> Heads { get; } = new List<string>();

        public List<object> Commands { get; } = new List<object>();

        public HashSet<string> Unhandled { get; } = new HashSet<string>();

        public object Output(object expression)
        {
            Commands.Add(expression);
            if (expression is Pair pair && pair.Car is Symbol head)
            {
                Heads.Add(head.Name);
                return !Unhandled.Contains(head.Name);
            }

            return false;
        }
    }

    private static Box UnitBox(double left, double right, double down, double up)
        => new Box(new Interval(left, right), new Interval(down, up));

    [Fact]
    public void a_bare_drawing_command_is_bracketed_by_a_translation()
    {
        //Arrange
        Stencil stencil = Lookup.FilledBox(UnitBox(0, 1, 0, 1));
        RecordingSink sink = new RecordingSink();

        //Act
        StencilInterpreter.Interpret(stencil.Expression, sink, Offset.Zero);

        //Assert
        sink.Heads.Should().Equal(new List<string>
        {
            "settranslation",
            "round-filled-box",
            "resettranslation",
        });
    }

    [Fact]
    public void a_translation_accumulates_into_the_offset_rather_than_nesting()
    {
        //Arrange
        Stencil stencil = Lookup.FilledBox(UnitBox(0, 1, 0, 1));
        stencil.Translate(new Offset(2.0, 3.0));
        stencil.Translate(new Offset(1.0, 1.0));
        RecordingSink sink = new RecordingSink();

        //Act
        StencilInterpreter.Interpret(stencil.Expression, sink, Offset.Zero);

        //Assert
        // One settranslation, carrying the SUM. The tree had two translate-stencil
        // nodes; the backend never sees either.
        sink.Heads.Should().Equal(new List<string>
        {
            "settranslation",
            "round-filled-box",
            "resettranslation",
        });

        List<object> translation = Pair.ToList(sink.Commands[0]);
        translation[1].Should().Be(3.0);
        translation[2].Should().Be(4.0);
    }

    [Fact]
    public void a_combination_is_flattened_into_its_parts()
    {
        //Arrange
        Stencil first = Lookup.FilledBox(UnitBox(0, 1, 0, 1));
        Stencil second = Lookup.FilledBox(UnitBox(2, 3, 0, 1));
        first.AddStencil(second);
        RecordingSink sink = new RecordingSink();

        //Act
        StencilInterpreter.Interpret(first.Expression, sink, Offset.Zero);

        //Assert
        sink.Heads.Should().Equal(new List<string>
        {
            "settranslation",
            "round-filled-box",
            "resettranslation",
            "settranslation",
            "round-filled-box",
            "resettranslation",
        });
    }

    [Fact]
    public void a_colour_scope_becomes_a_setcolor_resetcolor_pair()
    {
        //Arrange
        Stencil stencil = Lookup.FilledBox(UnitBox(0, 1, 0, 1)).InColor(1.0, 0.0, 0.0);
        RecordingSink sink = new RecordingSink();

        //Act
        StencilInterpreter.Interpret(stencil.Expression, sink, Offset.Zero);

        //Assert
        sink.Heads.Should().Equal(new List<string>
        {
            "setcolor",
            "settranslation",
            "round-filled-box",
            "resettranslation",
            "resetcolor",
        });
    }

    [Fact]
    public void a_rotation_scope_carries_the_centre_in_absolute_coordinates()
    {
        //Arrange
        Stencil stencil = Lookup.FilledBox(UnitBox(0, 2, 0, 2));
        stencil.RotateDegreesAbsolute(90.0, new Offset(1.0, 1.0));
        stencil.Translate(new Offset(10.0, 20.0));
        RecordingSink sink = new RecordingSink();

        //Act
        StencilInterpreter.Interpret(stencil.Expression, sink, Offset.Zero);

        //Assert
        List<object> rotation = Pair.ToList(sink.Commands[0]);
        ((Symbol)rotation[0]).Name.Should().Be("setrotation");
        rotation[1].Should().Be(90.0);

        // The centre is the accumulated translation plus the stencil-local centre.
        rotation[2].Should().Be(11.0);
        rotation[3].Should().Be(21.0);
    }

    [Fact]
    public void a_scale_scope_unscales_the_accumulated_offset()
    {
        //Arrange
        // Everything inside a scale is drawn in the scaled coordinate system, so the
        // offset that got us here has to be divided out before recursing.
        Stencil stencil = Lookup.FilledBox(UnitBox(0, 1, 0, 1));
        stencil.Scale(2.0, 4.0);
        stencil.Translate(new Offset(8.0, 8.0));
        RecordingSink sink = new RecordingSink();

        //Act
        StencilInterpreter.Interpret(stencil.Expression, sink, Offset.Zero);

        //Assert
        sink.Heads[0].Should().Be("setscale");
        List<object> translation = Pair.ToList(sink.Commands[1]);
        ((Symbol)translation[0]).Name.Should().Be("settranslation");
        translation[1].Should().Be(4.0);
        translation[2].Should().Be(2.0);
    }

    [Fact]
    public void an_outline_wrapper_draws_the_real_contents_not_the_outline()
    {
        //Arrange
        Stencil real = Lookup.FilledBox(UnitBox(0, 1, 0, 1));
        Stencil outline = Lookup.Circle(5.0, 0.0, true);
        Stencil combined = real.WithOutline(outline);
        RecordingSink sink = new RecordingSink();

        //Act
        StencilInterpreter.Interpret(combined.Expression, sink, Offset.Zero);

        //Assert
        sink.Heads.Should().Contain("round-filled-box");
        sink.Heads.Should().NotContain("circle");
    }

    [Fact]
    public void a_footnote_draws_nothing()
    {
        //Arrange
        object expression = Pair.List(Symbol.Intern("footnote"), "text");
        RecordingSink sink = new RecordingSink();

        //Act
        StencilInterpreter.Interpret(expression, sink, Offset.Zero);

        //Assert
        sink.Heads.Should().BeEmpty();
    }

    [Fact]
    public void an_unrenderable_utf8_string_falls_back_to_its_stencil()
    {
        //Arrange
        // The interpreter's ONLY use of the sink's return value: a backend that
        // cannot render text says so, and the fourth element is drawn instead.
        Stencil fallback = Lookup.FilledBox(UnitBox(0, 1, 0, 1));
        object expression = Pair.List(
            Symbol.Intern("utf-8-string"),
            "font",
            "hello",
            fallback.Expression);

        RecordingSink sink = new RecordingSink();
        sink.Unhandled.Add("utf-8-string");

        //Act
        StencilInterpreter.Interpret(expression, sink, Offset.Zero);

        //Assert
        sink.Heads.Should().Contain("round-filled-box");
    }

    [Fact]
    public void a_grob_cause_is_reported_and_then_the_contents_are_drawn()
    {
        //Arrange
        object grob = new object();
        Stencil shape = Lookup.FilledBox(UnitBox(0, 1, 0, 1));
        object expression = Pair.List(Symbol.Intern("grob-cause"), grob, shape.Expression);
        SvgBackend backend = new SvgBackend();

        //Act
        StencilInterpreter.Interpret(expression, backend, new Offset(3.0, 4.0));

        //Assert
        backend.Causes.Count.Should().Be(1);
        backend.Causes[0].Grob.Should().BeSameAs(grob);
        backend.Causes[0].At.Should().Be(new Offset(3.0, 4.0));
        backend.Body.Should().Contain("<rect");
    }

    [Fact]
    public void a_filled_box_becomes_a_rect_with_y_measured_downward()
    {
        //Arrange
        // round-filled-box carries (breadth width depth height blot) where breadth and
        // depth are already negated by Lookup. SVG's y is the TOP edge, so it is the
        // negated height.
        Stencil stencil = Lookup.FilledBox(UnitBox(-1, 3, -2, 5));
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("<rect x=\"-1.0000\" y=\"-5.0000\" width=\"4.0000\" height=\"7.0000\"");
    }

    [Fact]
    public void a_line_has_its_endpoints_flipped_vertically()
    {
        //Arrange
        Stencil stencil = LineInterface.MakeLine(0.2, new Offset(0, 0), new Offset(4, 3));
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("x1=\"0.0000\" y1=\"-0.0000\" x2=\"4.0000\" y2=\"-3.0000\"");
        svg.Should().Contain("stroke-width=\"0.2000\"");
    }

    [Fact]
    public void a_polygon_emits_its_points_with_y_flipped()
    {
        //Arrange
        List<Offset> points = new List<Offset>
        {
            new Offset(0, 0),
            new Offset(2, 0),
            new Offset(2, 1),
            new Offset(0, 1),
        };

        Stencil stencil = Lookup.RoundPolygon(points, 0.0, 0.0, true);
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("<polygon");

        // REVERSED relative to the input. Lookup conses each vertex onto the FRONT of
        // the coordinate list, exactly as upstream's lookup.cc does, so the emitted
        // order is the input order backwards. For a closed polygon that only changes
        // the winding, which is why upstream can do it without consequence — but it
        // has to be preserved, because the comparator diffs the text.
        svg.Should().Contain("points=\"0.0000 -1.0000 2.0000 -1.0000 2.0000 -0.0000 0.0000 -0.0000\"");
        svg.Should().Contain("fill=\"currentColor\"");
    }

    [Fact]
    public void a_circle_carries_its_radius_and_fill()
    {
        //Arrange
        Stencil stencil = Lookup.Circle(1.5, 0.1, false);
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("<circle");
        svg.Should().Contain("r=\"1.5000\"");
        svg.Should().Contain("fill=\"none\"");
    }

    [Fact]
    public void a_bezier_sandwich_becomes_a_path()
    {
        //Arrange
        Bezier top = new Bezier(new List<Offset>
        {
            new Offset(0, 0),
            new Offset(1, 1),
            new Offset(2, 1),
            new Offset(3, 0),
        });

        Bezier bottom = new Bezier(new List<Offset>
        {
            new Offset(0, 0),
            new Offset(1, 0.5),
            new Offset(2, 0.5),
            new Offset(3, 0),
        });

        Stencil stencil = Lookup.BezierSandwich(top, bottom, 0.1);
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("<path");
        svg.Should().Contain("d=\"M 0.0000 -0.0000 C 1.0000 -1.0000");
        svg.Should().Contain("z\"");
    }

    [Fact]
    public void a_colour_scope_opens_and_closes_a_group()
    {
        //Arrange
        Stencil stencil = Lookup.FilledBox(UnitBox(0, 1, 0, 1)).InColor(1.0, 0.5, 0.0, 1.0);
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().StartWith("<g color=\"rgba(100.0000%, 50.0000%, 0.0000%, 100.0000%)\">");
        svg.TrimEnd().Should().EndWith("</g>");
    }

    [Fact]
    public void a_translated_stencil_opens_a_translate_group_with_y_flipped()
    {
        //Arrange
        Stencil stencil = Lookup.FilledBox(UnitBox(0, 1, 0, 1));
        stencil.Translate(new Offset(2.0, 3.0));
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(stencil);

        //Assert
        svg.Should().Contain("<g transform=\"translate(2.0000, -3.0000)\">");
    }

    [Fact]
    public void a_document_is_sized_from_the_stencil_extents()
    {
        //Arrange
        Stencil stencil = Lookup.FilledBox(UnitBox(0, 10, 0, 4));
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderDocument(stencil);

        //Assert
        svg.Should().StartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        svg.Should().Contain("width=\"10.0000mm\" height=\"4.0000mm\"");
        svg.Should().Contain("viewBox=\"0.0000 -4.0000 10.0000 4.0000\"");
        svg.TrimEnd().Should().EndWith("</svg>");
    }

    [Fact]
    public void an_empty_stencil_still_produces_a_valid_document()
    {
        //Arrange
        Stencil stencil = Stencil.Empty;
        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderDocument(stencil);

        //Assert
        svg.Should().Contain("<svg");
        svg.Should().Contain("width=\"0.0000mm\"");
    }

    [Fact]
    public void an_unknown_command_is_recorded_rather_than_silently_dropped()
    {
        //Arrange
        object expression = Pair.List(Symbol.Intern("no-such-primitive"), 1.0);
        SvgBackend backend = new SvgBackend();

        //Act
        StencilInterpreter.Interpret(expression, backend, Offset.Zero);

        //Assert
        backend.UnhandledCommands.Should().Contain("no-such-primitive");
    }

    [Fact]
    public void staff_lines_render_as_five_evenly_spaced_lines()
    {
        //Arrange
        // The smallest thing that looks like real engraving output: a staff.
        Stencil staff = Stencil.Empty;
        for (int line = -2; line <= 2; line++)
        {
            Stencil rule = Lookup.HorizontalLine(new Interval(0, 20), 0.1);
            rule.Translate(new Offset(0, line));
            staff.AddStencil(rule);
        }

        SvgBackend backend = new SvgBackend();

        //Act
        string svg = backend.RenderFragment(staff);

        //Assert
        CountOccurrences(svg, "<line").Should().Be(5);
        svg.Should().Contain("<g transform=\"translate(0.0000, 2.0000)\">");
        svg.Should().Contain("<g transform=\"translate(0.0000, -2.0000)\">");
        staff.YExtent.Should().Be(new Interval(-2.05, 2.05));
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    [Fact]
    public void a_rendered_document_is_well_formed_xml_with_the_xlink_namespace_bound()
    {
        //Arrange
        // Found by the regression comparator on the port's very first output: a
        // document using the xlink prefix without binding the namespace is not
        // well-formed XML. It renders fine in a browser and fails in an XML parser, so
        // nothing catches it until something parses the output -- which is exactly what
        // the comparator does. The binding stays even though EPG13 retired the
        // `use xlink:href` glyph stand-in that first needed it, because the declaration
        // is what upstream's own SVG carries and the documents are compared.
        SvgBackend backend = new SvgBackend();
        Stencil glyph = new Stencil(
            Pair.List(Symbol.Intern("named-glyph"), new MutableString("font"), new MutableString("clefs.G")),
            new Interval(0, 1),
            new Interval(0, 1));

        //Act
        string document = backend.RenderDocument(glyph);

        //Assert
        document.Should().Contain("xmlns:xlink=\"http://www.w3.org/1999/xlink\"");

        // A glyph whose first element is not a real font draws nothing at all rather
        // than a reference to a name no document defines.
        document.Should().NotContain("xlink:href=");

        System.Action parse = () => System.Xml.Linq.XDocument.Parse(document);
        parse.Should().NotThrow();
    }
}
