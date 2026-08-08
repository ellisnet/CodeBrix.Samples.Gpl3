// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The system-start delimiter's drawn styles and its collapse rule, over hand-built
/// spanners. The brace goes through the markup layer and is exercised end to end by
/// the engraver tests instead.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SystemStartDelimiterTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static object Alist(params (string Key, object Value)[] entries)
    {
        object result = Nil.Instance;
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            result = new Pair(new Pair(Sym(entries[i].Key), entries[i].Value), result);
        }

        return result;
    }

    private static object GrobBasics(params (string Key, object Value)[] extra)
    {
        List<(string, object)> entries = new List<(string, object)>
        {
            ("meta", Alist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
        };
        entries.AddRange(extra);
        return Alist(entries.ToArray());
    }

    /// <summary>Builds a delimiter and two staves sharing its left bound and a Y parent.</summary>
    private static Spanner DelimiterOverStaves(
        params (string Key, object Value)[] delimiterExtra)
    {
        Item parent = new Item(GrobBasics());
        Item leftColumn = new Item(GrobBasics());

        Spanner delimiter = new Spanner(GrobBasics(delimiterExtra));
        delimiter.YParent = parent;
        delimiter.SetBound(Direction.Negative, leftColumn);

        OutputDef layout = new OutputDef();
        layout.SetVariable(Sym("line-thickness"), 0.1);
        delimiter.Layout = layout;

        foreach ((double Bottom, double Top) span in new[] { (-5.0, -3.0), (-1.0, 1.0) })
        {
            Spanner staff = new Spanner(GrobBasics(
                ("Y-extent", (object)new Pair(span.Bottom, span.Top))));
            staff.YParent = parent;
            staff.SetBound(Direction.Negative, leftColumn);
            PointerGroupInterface.AddGrob(delimiter, Sym("elements"), staff);
        }

        return delimiter;
    }

    [Fact]
    public void a_simple_bar_covers_the_full_span_of_the_staves()
    {
        //Arrange
        Spanner delimiter = DelimiterOverStaves(("style", Sym("bar-line")));

        //Act
        Stencil? stencil = SystemStartDelimiter.Print(delimiter);

        //Assert
        // The staves together span y in (-5, 1); the bar is drawn h = 6 tall around
        // zero and then translated to the span's centre. Its width is
        // line-thickness * thickness (default 1).
        stencil.HasValue.Should().BeTrue();
        stencil.Value.Extent(Axis.Y).Left.Should().BeApproximately(-5.0, 1e-9);
        stencil.Value.Extent(Axis.Y).Right.Should().BeApproximately(1.0, 1e-9);
        stencil.Value.Extent(Axis.X).Left.Should().BeApproximately(0.0, 1e-9);
        stencil.Value.Extent(Axis.X).Right.Should().BeApproximately(0.1, 1e-9);
        delimiter.IsLive.Should().BeTrue();
    }

    [Fact]
    public void a_line_bracket_reaches_beyond_the_bar_position()
    {
        //Arrange
        Spanner delimiter = DelimiterOverStaves(("style", Sym("line-bracket")));

        //Act
        Stencil? stencil = SystemStartDelimiter.Print(delimiter);

        //Assert
        // The square bracket is drawn 0.8 wide and translated left by its width.
        stencil.HasValue.Should().BeTrue();
        stencil.Value.Extent(Axis.X).Left.Should().BeLessThan(-0.7);
    }

    [Fact]
    public void a_collapsed_span_removes_the_delimiter()
    {
        //Arrange
        // A single 4-space staff against collapse-height 5 — SystemStartBar's own
        // default — is exactly the "no delimiter in front of one staff" rule.
        Item parent = new Item(GrobBasics());
        Item leftColumn = new Item(GrobBasics());
        Spanner delimiter = new Spanner(GrobBasics(
            ("style", Sym("bar-line")), ("collapse-height", 5.0)));
        delimiter.YParent = parent;
        delimiter.SetBound(Direction.Negative, leftColumn);

        Spanner staff = new Spanner(GrobBasics(("Y-extent", (object)new Pair(-2.0, 2.0))));
        staff.YParent = parent;
        staff.SetBound(Direction.Negative, leftColumn);
        PointerGroupInterface.AddGrob(delimiter, Sym("elements"), staff);

        //Act
        Stencil? stencil = SystemStartDelimiter.Print(delimiter);

        //Assert
        stencil.HasValue.Should().BeFalse();
        delimiter.IsLive.Should().BeFalse();
    }

    [Fact]
    public void an_element_with_a_different_left_bound_is_not_measured()
    {
        //Arrange
        // Only spanners STARTING with the delimiter count: a staff that begins at a
        // later column belongs to a later piece of the line.
        Item parent = new Item(GrobBasics());
        Item leftColumn = new Item(GrobBasics());
        Item otherColumn = new Item(GrobBasics());
        Spanner delimiter = new Spanner(GrobBasics(("style", Sym("bar-line"))));
        delimiter.YParent = parent;
        delimiter.SetBound(Direction.Negative, leftColumn);

        OutputDef layout = new OutputDef();
        layout.SetVariable(Sym("line-thickness"), 0.1);
        delimiter.Layout = layout;

        Spanner near = new Spanner(GrobBasics(("Y-extent", (object)new Pair(-1.0, 1.0))));
        near.YParent = parent;
        near.SetBound(Direction.Negative, leftColumn);
        PointerGroupInterface.AddGrob(delimiter, Sym("elements"), near);

        Spanner far = new Spanner(GrobBasics(("Y-extent", (object)new Pair(-9.0, 9.0))));
        far.YParent = parent;
        far.SetBound(Direction.Negative, otherColumn);
        PointerGroupInterface.AddGrob(delimiter, Sym("elements"), far);

        //Act
        Stencil? stencil = SystemStartDelimiter.Print(delimiter);

        //Assert
        stencil.HasValue.Should().BeTrue();
        stencil.Value.Extent(Axis.Y).Length.Should().BeApproximately(2.0, 1e-9);
    }
}
