// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Pins the accounting half of <see cref="AccidentalPlacement"/>: how accidentals are
/// FILED — by note name, octaves of one name sharing a column — and how they are split
/// into break reminders against real accidentals. The geometric half (skylines,
/// stagger, packing) is exercised through the regression smoke set, where extents and
/// fonts exist.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class AccidentalPlacementTests
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

    /// <summary>Builds an accidental item whose Y parent is a head caused by a note
    /// event carrying the given pitch — the shape <c>accidental_pitch</c> reads. The
    /// properties ride in the IMMUTABLE alists so no ambient interpreter is consulted.</summary>
    private static Item MakeAccidental(Pitch pitch, bool forced = false)
    {
        StreamEvent cause = new StreamEvent(Nil.Instance, Alist(("pitch", pitch)));

        Item head = new Item(GrobBasics("TestHead", ("cause", cause)));

        Item accidental = forced
            ? new Item(GrobBasics("TestAccidental", ("forced", true)))
            : new Item(GrobBasics("TestAccidental"));
        accidental.YParent = head;
        return accidental;
    }

    [Fact]
    public void accidentals_of_one_note_name_share_a_column_across_octaves()
    {
        //Arrange
        Item placement = new Item(GrobBasics("TestPlacement"));
        Item low = MakeAccidental(new Pitch(0, 3, new Rational(1, 2)));
        Item high = MakeAccidental(new Pitch(1, 3, new Rational(1, 2)));

        //Act
        AccidentalPlacement.AddAccidental(placement, low, false, null);
        AccidentalPlacement.AddAccidental(placement, high, false, null);

        //Assert
        // One entry, keyed (notename . 1), holding BOTH grobs -- that is what keeps
        // octaves of one name vertically aligned in one packing column.
        object accs = placement.GetObject(Sym("accidental-grobs"));
        Pair entries = (Pair)accs;
        entries.Cdr.Should().BeSameAs(Nil.Instance);

        Pair entry = (Pair)entries.Car;
        SchemeUtilities.IsEqual(entry.Car, new Pair(3L, 1L)).Should().BeTrue();

        List<object> grobs = Pair.ToList(entry.Cdr);
        grobs.Should().HaveCount(2);
        grobs.Should().Contain(high);
        grobs.Should().Contain(low);

        low.XParent.Should().BeSameAs(placement);
        high.XParent.Should().BeSameAs(placement);
    }

    [Fact]
    public void different_note_names_get_their_own_columns()
    {
        //Arrange
        Item placement = new Item(GrobBasics("TestPlacement"));
        Item d = MakeAccidental(new Pitch(0, 1, new Rational(1, 2)));
        Item g = MakeAccidental(new Pitch(0, 4, new Rational(-1, 2)));

        //Act
        AccidentalPlacement.AddAccidental(placement, d, false, null);
        AccidentalPlacement.AddAccidental(placement, g, false, null);

        //Assert
        List<object> entries = Pair.ToList(placement.GetObject(Sym("accidental-grobs")));
        entries.Should().HaveCount(2);
    }

    [Fact]
    public void staggering_separates_same_name_accidentals_from_different_sources()
    {
        //Arrange
        Item placement = new Item(GrobBasics("TestPlacement"));
        Item one = MakeAccidental(new Pitch(0, 3, new Rational(1, 2)));
        Item two = MakeAccidental(new Pitch(0, 3, new Rational(1, 2)));
        object sourceA = new object();
        object sourceB = new object();

        //Act
        AccidentalPlacement.AddAccidental(placement, one, true, sourceA);
        AccidentalPlacement.AddAccidental(placement, two, true, sourceB);

        //Assert
        // With stagger on, the key carries the source's identity hash instead of the
        // constant 1, so the same note name from different voices forms two columns.
        List<object> entries = Pair.ToList(placement.GetObject(Sym("accidental-grobs")));
        entries.Should().HaveCount(2);
    }

    [Fact]
    public void split_accidentals_sends_tied_unforced_ones_to_the_break_reminders()
    {
        //Arrange
        Item placement = new Item(GrobBasics("TestPlacement"));
        Item plain = MakeAccidental(new Pitch(0, 0, new Rational(1, 2)));
        Item tied = MakeAccidental(new Pitch(0, 1, new Rational(1, 2)));
        Item tiedButForced = MakeAccidental(new Pitch(0, 2, new Rational(1, 2)), forced: true);

        Item tie = new Item(GrobBasics("TestTie"));
        tied.SetObject(Sym("tie"), tie);
        tiedButForced.SetObject(Sym("tie"), tie);

        AccidentalPlacement.AddAccidental(placement, plain, false, null);
        AccidentalPlacement.AddAccidental(placement, tied, false, null);
        AccidentalPlacement.AddAccidental(placement, tiedButForced, false, null);

        List<Grob> breakReminders = new List<Grob>();
        List<Grob> realAccidentals = new List<Grob>();

        //Act
        AccidentalPlacement.SplitAccidentals(placement, breakReminders, realAccidentals);

        //Assert
        breakReminders.Should().Equal(tied);
        realAccidentals.Should().HaveCount(2);
        realAccidentals.Should().Contain(plain);
        realAccidentals.Should().Contain(tiedButForced);
    }

    [Fact]
    public void relevant_accidentals_drop_break_reminders_mid_line()
    {
        //Arrange
        Item placement = new Item(GrobBasics("TestPlacement"));
        Item plain = MakeAccidental(new Pitch(0, 0, new Rational(1, 2)));
        Item tied = MakeAccidental(new Pitch(0, 1, new Rational(1, 2)));
        Item tie = new Item(GrobBasics("TestTie"));
        tied.SetObject(Sym("tie"), tie);

        AccidentalPlacement.AddAccidental(placement, plain, false, null);
        AccidentalPlacement.AddAccidental(placement, tied, false, null);

        Item left = new Item(GrobBasics("TestColumn"));

        //Act
        List<Grob> relevant = AccidentalPlacement.GetRelevantAccidentals(
            new List<Grob> { placement }, left);

        //Assert
        // An unbroken item is not at a line start, so the tied reminder is invisible
        // to spacing there -- only the real accidental counts.
        relevant.Should().Equal(plain);
    }
}
