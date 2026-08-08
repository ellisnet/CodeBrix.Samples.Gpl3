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
/// EPG14's arithmetic and ordering, asserted against HAND-COMPUTED values and against
/// properties that are derivable rather than recorded.
/// </summary>
/// <remarks>
/// Same rule EPG10, EPG11 and EPG12 set: never assert what the port happens to produce.
/// Every expected value below was computed from upstream's own expression by hand, so
/// the test is able to disagree with the code.
/// </remarks>
[Collection(EngineGlobalStateCollection.Name)]
public class Epg14Tests
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

    private static Item MakeItem(params (string Key, object Value)[] extra)
        => new Item(GrobBasics(extra));

    /// <summary>A system at a given rank — what find_broken_piece indexes by.</summary>
    private static SystemGrob MakeSystem(int rank)
    {
        SystemGrob system = new SystemGrob(GrobBasics(("axes", Pair.List(0L, 1L))));
        system.Rank = rank;
        return system;
    }

    // An item finds its system through its X PARENT chain, and a paper column is what
    // ends that chain — so a fixture that only sets a Y parent has no system at all.
    private static Item MakeItemOn(SystemGrob system)
    {
        PaperColumn column = new PaperColumn(GrobBasics());
        column.System = system;
        Item item = MakeItem();
        item.XParent = column;
        return item;
    }

    // A spanner's system is its two bounds' system, and only when they agree.
    private static Spanner MakeBoundSpanner(SystemGrob system)
    {
        Spanner spanner = new Spanner(GrobBasics());
        spanner.SetBound(Direction.Negative, MakeItemOn(system));
        spanner.SetBound(Direction.Positive, MakeItemOn(system));
        return spanner;
    }

    // ----- script-interface.cc / script-column.cc -----

    [Fact]
    public void script_priority_orders_by_the_property_and_treats_a_missing_one_as_zero()
    {
        //Arrange
        // Upstream reads both sides through from_scm<int>, which answers 0 for a value
        // that is not a number rather than raising. So an undefined priority sorts as 0:
        // it is BELOW 5 and ABOVE -5, and never below itself.
        Item five = MakeItem(("script-priority", 5L));
        Item minusFive = MakeItem(("script-priority", -5L));
        Item unset = MakeItem();

        //Act / Assert
        ScriptInterface.ScriptPriorityLess(minusFive, five).Should().BeTrue();
        ScriptInterface.ScriptPriorityLess(five, minusFive).Should().BeFalse();

        ScriptInterface.ScriptPriorityLess(unset, five).Should().BeTrue();
        ScriptInterface.ScriptPriorityLess(minusFive, unset).Should().BeTrue();

        // Strict: a value is never less than itself, so equal priorities do not swap.
        ScriptInterface.ScriptPriorityLess(five, five).Should().BeFalse();
        ScriptInterface.ScriptPriorityLess(unset, unset).Should().BeFalse();
    }

    [Fact]
    public void ordering_bumps_each_equal_outside_staff_priority_by_a_tenth()
    {
        //Arrange
        // Three scripts, all UP, priorities 1, 2, 3 so the sort order is already known,
        // and all three carrying outside-staff-priority 100.
        //
        // Upstream walks the sorted list keeping the PREVIOUS grob's ORIGINAL priority:
        //   grob 1: first in the list, untouched                      -> 100
        //   grob 2: last-initial 100, its own initial 100, equal      -> last's CURRENT
        //           (100) + 0.1                                       -> 100.1
        //   grob 3: last-initial is grob 2's ORIGINAL 100, its own is
        //           100, equal -> grob 2's CURRENT (100.1) + 0.1      -> 100.2
        //
        // The distinction between "last initial" and "last current" is the whole point:
        // reading the current value on both sides would compare 100.1 against 100 and
        // stop bumping.
        Item a = MakeItem(("direction", 1L), ("script-priority", 1L),
            ("outside-staff-priority", 100.0));
        Item b = MakeItem(("direction", 1L), ("script-priority", 2L),
            ("outside-staff-priority", 100.0));
        Item c = MakeItem(("direction", 1L), ("script-priority", 3L),
            ("outside-staff-priority", 100.0));

        //Act
        ScriptColumn.OrderGrobs(new List<Grob> { a, b, c });

        //Assert
        a.GetProperty(Sym("outside-staff-priority")).Should().Be(100.0);
        ((double)b.GetProperty(Sym("outside-staff-priority")))
            .Should().BeApproximately(100.1, 1e-12);
        ((double)c.GetProperty(Sym("outside-staff-priority")))
            .Should().BeApproximately(100.2, 1e-12);
    }

    [Fact]
    public void ordering_leaves_already_distinct_outside_staff_priorities_alone()
    {
        //Arrange
        // Same three scripts, but the priorities differ by more than the 0.001 tolerance
        // upstream compares with. Nothing is equal to its predecessor's original, so the
        // bump never fires and every value is left exactly as written.
        Item a = MakeItem(("direction", 1L), ("script-priority", 1L),
            ("outside-staff-priority", 100.0));
        Item b = MakeItem(("direction", 1L), ("script-priority", 2L),
            ("outside-staff-priority", 250.0));
        Item c = MakeItem(("direction", 1L), ("script-priority", 3L),
            ("outside-staff-priority", 400.0));

        //Act
        ScriptColumn.OrderGrobs(new List<Grob> { a, b, c });

        //Assert
        a.GetProperty(Sym("outside-staff-priority")).Should().Be(100.0);
        b.GetProperty(Sym("outside-staff-priority")).Should().Be(250.0);
        c.GetProperty(Sym("outside-staff-priority")).Should().Be(400.0);
    }

    [Fact]
    public void ordering_keeps_up_and_down_scripts_in_separate_stacks()
    {
        //Arrange
        // One UP and one DOWN script, both with the same outside-staff-priority. They are
        // stacked independently, so NEITHER is the other's predecessor and neither is
        // bumped — which is the property that makes an accent above and a staccato below
        // a note independent of each other.
        Item up = MakeItem(("direction", 1L), ("script-priority", 1L),
            ("outside-staff-priority", 100.0));
        Item down = MakeItem(("direction", -1L), ("script-priority", 2L),
            ("outside-staff-priority", 100.0));

        //Act
        ScriptColumn.OrderGrobs(new List<Grob> { up, down });

        //Assert
        up.GetProperty(Sym("outside-staff-priority")).Should().Be(100.0);
        down.GetProperty(Sym("outside-staff-priority")).Should().Be(100.0);
    }

    [Fact]
    public void a_script_with_no_numeric_priority_is_not_added_to_a_column()
    {
        //Arrange
        // Script_column::add_side_positioned returns early unless script-priority is a
        // NUMBER: a script with no place in the ordering has nothing to say to the column,
        // and adding it would give the sort an element it cannot position.
        Item column = MakeItem();
        Item withPriority = MakeItem(("script-priority", 3L));
        Item without = MakeItem();

        //Act
        ScriptColumn.AddSidePositioned(column, withPriority);
        ScriptColumn.AddSidePositioned(column, without);

        //Assert
        PointerGroupInterface.Count(column, Sym("scripts")).Should().Be(1);
        withPriority.GetObject(Sym("script-column")).Should().BeSameAs(column);
        without.GetObject(Sym("script-column")).Should().Be(Nil.Instance);
    }

    // ----- line-spanner.cc -----

    [Fact]
    public void offsets_maybe_measures_both_grobs_against_their_common_refpoint()
    {
        //Arrange
        // Two items parented onto a shared grob at Y = 3 and Y = -2. Their common
        // reference point is the parent, so the coordinates come back as written and the
        // parent is what is handed out.
        Item parent = MakeItem();
        Item left = MakeItem(("Y-offset", 3.0));
        Item right = MakeItem(("Y-offset", -2.0));
        left.YParent = parent;
        right.YParent = parent;

        //Act
        DrulArray<double> offsets = LineSpanner.OffsetsMaybe(
            new DrulArray<Grob>(left, right), out Grob common);

        //Assert
        common.Should().BeSameAs(parent);
        offsets[Direction.Negative].Should().BeApproximately(3.0, 1e-12);
        offsets[Direction.Positive].Should().BeApproximately(-2.0, 1e-12);
    }

    [Fact]
    public void offsets_maybe_answers_zero_and_no_refpoint_when_both_grobs_are_absent()
    {
        //Arrange
        // The branch that matters when a glissando's staff has vanished on this system:
        // upstream answers a null common refpoint rather than dereferencing, and the
        // caller's `y_needed` guard is what decides whether the spanner suicides.

        //Act
        DrulArray<double> offsets = LineSpanner.OffsetsMaybe(
            new DrulArray<Grob>(null, null), out Grob common);

        //Assert
        common.Should().BeNull();
        offsets[Direction.Negative].Should().Be(0.0);
        offsets[Direction.Positive].Should().Be(0.0);
    }

    [Fact]
    public void offsets_maybe_uses_the_surviving_grob_as_its_own_refpoint()
    {
        //Arrange
        // With only one grob there is nothing to be common WITH, so upstream measures it
        // against itself — which is zero by definition — and reports it as the refpoint.
        // The other slot is documented upstream as "the 0.0 shouldn't get used".
        Item only = MakeItem(("Y-offset", 7.5));

        //Act
        DrulArray<double> left = LineSpanner.OffsetsMaybe(
            new DrulArray<Grob>(only, null), out Grob leftCommon);
        DrulArray<double> right = LineSpanner.OffsetsMaybe(
            new DrulArray<Grob>(null, only), out Grob rightCommon);

        //Assert
        leftCommon.Should().BeSameAs(only);
        left[Direction.Negative].Should().Be(0.0);

        rightCommon.Should().BeSameAs(only);
        right[Direction.Positive].Should().Be(0.0);
    }

    // ----- grob.cc / item.cc / spanner.cc: find_broken_piece -----

    [Fact]
    public void a_spanner_finds_its_piece_on_a_system_by_rank_arithmetic()
    {
        //Arrange
        // Upstream indexes broken_intos_ by (wanted rank - first piece's rank), which is
        // only correct because the pieces are contiguous and in system order. Three
        // pieces starting at rank 4 therefore live on ranks 4, 5 and 6.
        SystemGrob s4 = MakeSystem(4);
        SystemGrob s5 = MakeSystem(5);
        SystemGrob s6 = MakeSystem(6);

        Spanner original = new Spanner(GrobBasics());
        Spanner p4 = MakeBoundSpanner(s4);
        Spanner p5 = MakeBoundSpanner(s5);
        Spanner p6 = MakeBoundSpanner(s6);
        original.BrokenIntos.Add(p4);
        original.BrokenIntos.Add(p5);
        original.BrokenIntos.Add(p6);

        //Act / Assert
        original.FindBrokenPiece(s4).Should().BeSameAs(p4);
        original.FindBrokenPiece(s5).Should().BeSameAs(p5);
        original.FindBrokenPiece(s6).Should().BeSameAs(p6);
    }

    [Fact]
    public void an_unbroken_spanner_has_no_piece_anywhere()
    {
        //Arrange
        // The guard that matters before EPG15 lands: with no pieces at all the index
        // arithmetic has nothing to index, and upstream answers null rather than reading
        // broken_intos_.front ().
        SystemGrob system = MakeSystem(0);
        Spanner lonely = new Spanner(GrobBasics());

        //Act / Assert
        lonely.FindBrokenPiece(system).Should().BeNull();
    }

    [Fact]
    public void an_item_finds_itself_or_one_of_its_two_prebroken_copies()
    {
        //Arrange
        // Item::find_broken_piece searches exactly three candidates — itself and the drul
        // pair — because that is all an item ever has. A system holding none of them
        // answers null.
        SystemGrob home = MakeSystem(0);
        SystemGrob other = MakeSystem(1);
        SystemGrob stranger = MakeSystem(2);

        Item item = MakeItemOn(home);
        Item leftCopy = MakeItemOn(other);
        item.SetPrebrokenPiece(Direction.Negative, leftCopy);

        //Act / Assert
        item.FindBrokenPiece(home).Should().BeSameAs(item);
        item.FindBrokenPiece(other).Should().BeSameAs(leftCopy);
        item.FindBrokenPiece(stranger).Should().BeNull();
    }

    // ----- lily-guile.cc: the two defects EPG14 found in already-ported code -----

    [Fact]
    public void assq_matches_a_numeric_key_by_value_the_way_guile_does()
    {
        //Arrange
        // THE DEFECT THIS FENCES: Guile fixnums are IMMEDIATES, so (eq? 1 1) is true and
        // (assq 1 '((1 . a))) finds its entry. The port compared with ReferenceEquals,
        // under which two boxed longs of the same value are never equal — so every
        // numerically-keyed alist lookup in the engine silently missed.
        // `ottavationMarkups` is keyed by octave count, which is how it surfaced.
        object alist = new Pair(
            new Pair(1L, Sym("one")),
            new Pair(new Pair(2L, Sym("two")), Nil.Instance));

        //Act
        Pair one = SchemeUtilities.Assq(1L, alist);
        Pair two = SchemeUtilities.Assq(2L, alist);
        Pair missing = SchemeUtilities.Assq(3L, alist);

        //Assert
        one.Should().NotBeNull();
        one.Cdr.Should().BeSameAs(Sym("one"));
        two.Should().NotBeNull();
        two.Cdr.Should().BeSameAs(Sym("two"));
        missing.Should().BeNull();
    }

    [Fact]
    public void assq_still_compares_a_symbol_key_by_identity()
    {
        //Arrange
        // The other half of the same contract: symbols are interned, so identity IS
        // value for them, and widening the comparison must not have widened it to
        // equal?. Two DISTINCT lists with the same elements are not eq?.
        object key = Pair.List(1L, 2L);
        object otherButEqual = Pair.List(1L, 2L);
        object alist = new Pair(new Pair(key, Sym("hit")), Nil.Instance);

        //Act / Assert
        SchemeUtilities.Assq(key, alist).Should().NotBeNull();
        SchemeUtilities.Assq(otherButEqual, alist).Should().BeNull();
    }

    // ----- pitch.cc: set_middle_C -----

    [Fact]
    public void middle_c_is_the_clef_position_plus_the_octave_offset()
    {
        //Arrange
        // set_middle_C is clef_pos + offset, and \ottava #1 sets middleCOffset to
        // -7 * 1 = -7. From a treble clef's middleCClefPosition of -6 that gives
        // -6 + -7 = -13.
        // Loaded() FIRST: a context type-checks every property write against
        // scm/define-context-properties.scm, so without the Scheme layer the write is
        // refused and middleCPosition reads back as '(). Depending on whether some other
        // test happened to build an interpreter first is exactly the kind of accident
        // this makes explicit.
        Epg8TestHarness.Loaded();
        Translation.Context context = new Translation.Context(Sym("Voice"));
        context.SetProperty(Sym("middleCClefPosition"), -6L);
        context.SetProperty(Sym("middleCOffset"), -7L);

        //Act
        Music.Pitch.SetMiddleC(context);

        //Assert
        context.GetProperty(Sym("middleCPosition")).Should().Be(-13L);
    }

    [Fact]
    public void a_cue_position_overrides_the_clef_but_not_the_octave_offset()
    {
        //Arrange
        // Upstream's comment says it outright: "middleCCuePosition overrides the clef!"
        // It replaces clef_pos and nothing else, so the offset is still added:
        // 2 + -7 = -5, NOT 2.
        Epg8TestHarness.Loaded();
        Translation.Context context = new Translation.Context(Sym("Voice"));
        context.SetProperty(Sym("middleCClefPosition"), -6L);
        context.SetProperty(Sym("middleCOffset"), -7L);
        context.SetProperty(Sym("middleCCuePosition"), 2L);

        //Act
        Music.Pitch.SetMiddleC(context);

        //Assert
        context.GetProperty(Sym("middleCPosition")).Should().Be(-5L);
    }
}
