// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The walk from a spacing wish out to the break-aligned symbol its distance is
/// measured from.
/// <para>
/// The case worth fencing is the LINE START. By the time a wish asks, every grob it
/// names has already been substituted to its prebroken piece, so the column reached
/// through that piece is ITSELF a prebroken piece and has no prebroken piece of its
/// own. Upstream writes the column lookup as one unchecked chain and hands the
/// resulting null straight to <c>extent ()</c>, which reads against the ROOT;
/// <c>Grob::relative_coordinate</c> is written to tolerate exactly that and says so
/// (upstream issue #6149). Treating the null as "nothing here" instead throws the
/// whole line start away, and every system silently loses the prefatory spacing its
/// clef and time signature asked for.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SpacingInterfaceTests
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

    /// <summary>
    /// Builds the state a wish is in at a line start: a non-musical item on a
    /// non-musical column, both taken through the real
    /// <see cref="SystemGrob.PreProcessing"/>, with the wish naming the item's RIGHT
    /// prebroken piece — the piece whose own column can no longer be prebroken.
    /// </summary>
    private static (Item Wish, Item Piece, PaperColumn PieceColumn) LineStart(bool withSpaceAlist)
    {
        SystemGrob system = new SystemGrob(GrobBasics(("axes", Pair.List(0L, 1L))));
        system.Layout = new Layout.OutputDef();

        PaperColumn column = new PaperColumn(
            GrobBasics(("non-musical", true), ("line-break-permission", Sym("allow"))));
        system.TypesetGrob(column);
        system.AddColumn(column);

        List<(string, object)> itemProperties = new List<(string, object)>
        {
            ("non-musical", true),
            ("X-extent", new Pair(0.0, 2.0)),
        };

        if (withSpaceAlist)
        {
            // The shape upstream's Clef carries: one entry, keyed by what sits to the
            // right of it.
            itemProperties.Add((
                "space-alist",
                Alist(("first-note", new Pair(Sym("minimum-fixed-space"), 5.0)))));
        }

        Item breakAligned = new Item(GrobBasics(itemProperties.ToArray()));
        breakAligned.XParent = column;
        system.TypesetGrob(breakAligned);

        system.PreProcessing();

        Item piece = breakAligned.FindPrebrokenPiece(Direction.Positive);

        Item wish = new Item(GrobBasics());
        PointerGroupInterface.AddGrob(wish, Sym("left-break-aligned"), piece);

        return (wish, piece, piece?.GetColumn());
    }

    [Fact]
    public void the_fixture_really_reproduces_the_line_start_dead_end()
    {
        //Arrange
        (Item _, Item piece, PaperColumn pieceColumn) = LineStart(withSpaceAlist: true);

        //Act
        Item columnPiece = pieceColumn?.FindPrebrokenPiece(Direction.Positive);

        //Assert
        // Unless all three hold, the test below proves nothing: the wish must name a
        // piece that is ALREADY broken to the right, whose column is already broken
        // too, and which therefore has no further piece to be asked for.
        piece.BreakStatusDirection().Should().Be(Direction.Positive);
        pieceColumn.BreakStatusDirection().Should().Be(Direction.Positive);
        columnPiece.Should().BeNull();
    }

    [Fact]
    public void a_line_start_symbol_is_found_although_its_column_cannot_be_prebroken()
    {
        //Arrange
        (Item wish, Item piece, PaperColumn _) = LineStart(withSpaceAlist: true);
        Interval extent = default;

        //Act
        Grob found = SpacingInterface.ExtremalBreakAlignedGrob(
            wish, Direction.Negative, Direction.Positive, ref extent);

        //Assert
        found.Should().BeSameAs(piece);
        extent.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void a_line_start_symbol_with_no_space_alist_is_not_found()
    {
        //Arrange
        // The CONTROL for the test above: same fixture, same dead-end column, and the
        // one thing that changes is the property the walk actually filters on. Without
        // it the answer must be null — otherwise "found" above says nothing about
        // whether the filter still works.
        (Item wish, Item _, PaperColumn __) = LineStart(withSpaceAlist: false);
        Interval extent = default;

        //Act
        Grob found = SpacingInterface.ExtremalBreakAlignedGrob(
            wish, Direction.Negative, Direction.Positive, ref extent);

        //Assert
        found.Should().BeNull();
    }
}
