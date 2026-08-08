// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Pins <see cref="ClefModifier.CalcParentAlignment"/>: the short clef name is looked
/// up in <c>clef-alignments</c>, the alist value's car serves the modifier BELOW the
/// clef and its cdr the one above, and an unknown clef centres.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class ClefModifierTests
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

    private static object GrobBasics(string name, params (string Key, object Value)[] extra)
    {
        List<(string, object)> entries = new List<(string, object)>
        {
            ("meta", Alist(("name", Sym(name)), ("interfaces", Nil.Instance))),
        };
        entries.AddRange(extra);
        return Alist(entries.ToArray());
    }

    private static Item MakeModifier(string clefGlyph, long direction)
    {
        Item clef = new Item(GrobBasics("TestClef", ("glyph", new MutableString(clefGlyph))));
        Item modifier = new Item(GrobBasics(
            "TestModifier",
            ("clef-alignments", Alist(("G", new Pair(-0.2, 0.1)), ("F", new Pair(-0.3, -0.2)))),
            ("direction", direction)));
        modifier.XParent = clef;
        return modifier;
    }

    [Fact]
    public void a_modifier_below_the_clef_takes_the_alist_entry_car()
    {
        //Arrange
        Item modifier = MakeModifier("clefs.G", -1L);

        //Act
        object alignment = ClefModifier.CalcParentAlignment(modifier);

        //Assert
        alignment.Should().Be(-0.2);
    }

    [Fact]
    public void a_modifier_above_the_clef_takes_the_alist_entry_cdr()
    {
        //Arrange
        Item modifier = MakeModifier("clefs.G", 1L);

        //Act
        object alignment = ClefModifier.CalcParentAlignment(modifier);

        //Assert
        alignment.Should().Be(0.1);
    }

    [Fact]
    public void an_unknown_clef_centres_the_modifier()
    {
        //Arrange
        Item modifier = MakeModifier("clefs.percussion", -1L);

        //Act
        object alignment = ClefModifier.CalcParentAlignment(modifier);

        //Assert
        alignment.Should().Be(0L);
    }
}
