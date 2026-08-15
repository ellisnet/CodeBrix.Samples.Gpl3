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
/// The element-skyline fence: an axis group's skyline is the union of its ELEMENTS',
/// and <c>elements</c> is a grob OBJECT rather than a grob property.
/// <para>
/// This is what PARITY 9 found under D32. <c>elements</c> was read out of the property
/// table, which holds nothing by that name, so
/// <c>ly:grob::vertical-skylines-from-element-stencils</c> answered an EMPTY pair for
/// every grob that uses it — VoltaBracketSpanner, DynamicLineSpanner, the three pedal
/// line spanners and CenteredBarNumberLineSpanner. An empty pair is a legal answer that
/// <c>add_grobs_of_one_priority</c> skips, so none of those grobs was ever placed
/// outside the staff, and nothing threw, warned or left a stub.
/// </para>
/// </summary>
public class ElementSkylineTests
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

    private static object GrobBasics()
        => Alist(("meta", Alist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))));

    // A child carrying a real one-by-one skyline pair, so a parent that finds it has
    // something non-empty to merge and a parent that does not comes back empty.
    private static Item ChildWithSkyline()
    {
        Item child = new Item(GrobBasics());
        SkylinePair pair = new SkylinePair(
            new Box(new Interval(0.0, 1.0), new Interval(0.0, 1.0)), Axis.X);
        child.SetProperty(Sym("vertical-skylines"), pair.ToScheme());
        return child;
    }

    [Fact]
    public void the_elements_link_is_read_from_the_object_table_not_the_property_table()
    {
        //Arrange
        Item asObject = new Item(GrobBasics());
        GrobArray array = new GrobArray();
        array.Add(ChildWithSkyline());
        asObject.SetObject(Sym("elements"), array);

        Item asProperty = new Item(GrobBasics());
        GrobArray decoy = new GrobArray();
        decoy.Add(ChildWithSkyline());
        asProperty.SetProperty(Sym("elements"), decoy);

        //Act
        SkylinePair found = StencilIntegral.SkylinesFromElementStencils(
            asObject, Axis.X, false, 0, 0);
        SkylinePair notFound = StencilIntegral.SkylinesFromElementStencils(
            asProperty, Axis.X, false, 0, 0);

        //Assert
        // The two grobs carry the SAME child under the SAME name; only the table
        // differs. The pair is the control: a reader that consulted both tables, or the
        // wrong one, cannot make these two come out differently in this direction.
        found.IsEmpty.Should().BeFalse();
        notFound.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void a_group_with_no_elements_answers_an_empty_pair()
    {
        //Arrange
        Item childless = new Item(GrobBasics());
        Item parent = new Item(GrobBasics());
        GrobArray array = new GrobArray();
        array.Add(ChildWithSkyline());
        parent.SetObject(Sym("elements"), array);

        //Act
        SkylinePair empty = StencilIntegral.SkylinesFromElementStencils(
            childless, Axis.X, false, 0, 0);
        SkylinePair filled = StencilIntegral.SkylinesFromElementStencils(
            parent, Axis.X, false, 0, 0);

        //Assert
        // Empty is still the right answer when there is genuinely nothing to merge —
        // the defect was answering it when there WAS.
        empty.IsEmpty.Should().BeTrue();
        filled.IsEmpty.Should().BeFalse();
    }
}
