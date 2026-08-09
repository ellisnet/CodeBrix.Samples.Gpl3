// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG20's arithmetic and lookup rules, asserted against HAND-COMPUTED values and against
/// properties that are derivable rather than recorded.
/// </summary>
/// <remarks>
/// Same rule EPG10, EPG11, EPG12 and EPG14 set: never assert what the port happens to
/// produce. Every expected value below was computed from upstream's own expression by
/// hand, so the test is able to disagree with the code.
/// </remarks>
[Collection(EngineGlobalStateCollection.Name)]
public class Epg20Tests
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

    // ----- SchemeConvert.ToInterval: the pair -> Interval reader EPG20 added -----

    [Fact]
    public void a_pair_of_numbers_reads_as_the_interval_it_spells()
    {
        //Arrange
        // from_scm (value, Interval ()): LEFT from the car, RIGHT from the cdr, which is
        // the same order ToDrulDouble already uses.
        object pair = new Pair(-2.5, 4.0);

        //Act
        Interval result = SchemeConvert.ToInterval(pair, Interval.Empty);

        //Assert
        result.Left.Should().Be(-2.5);
        result.Right.Should().Be(4.0);
    }

    [Fact]
    public void a_missing_value_answers_the_fallback_and_an_empty_fallback_stays_empty()
    {
        //Arrange
        // The distinction that matters: Interval () is the EMPTY interval, not [0, 0].
        // Arpeggio::print branches on is_empty() and takes a DIFFERENT arm -- it warns and
        // suicides -- so answering [0, 0] here would silently draw a squiggle where
        // upstream junks the grob.
        object notAPair = false;

        //Act
        Interval result = SchemeConvert.ToInterval(notAPair, Interval.Empty);

        //Assert
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void a_zero_length_fallback_is_not_empty_and_is_answered_as_written()
    {
        //Arrange
        // Cluster_beacon::height passes Interval (0, 0) and NOT the empty interval, which
        // is why the reader has to take the fallback from its caller rather than assume
        // one. A beacon with no positions still contributes a point.
        object notAPair = Nil.Instance;

        //Act
        Interval result = SchemeConvert.ToInterval(notAPair, new Interval(0, 0));

        //Assert
        result.IsEmpty.Should().BeFalse();
        result.Left.Should().Be(0.0);
        result.Right.Should().Be(0.0);
    }

    [Fact]
    public void a_pair_whose_halves_are_not_numbers_is_not_an_interval()
    {
        //Arrange
        object pair = new Pair(Sym("up"), Sym("down"));

        //Act
        Interval result = SchemeConvert.ToInterval(pair, new Interval(7, 9));

        //Assert
        result.Left.Should().Be(7.0);
        result.Right.Should().Be(9.0);
    }

    // ----- Cluster_beacon::height -----

    [Fact]
    public void a_beacon_reports_half_a_staff_space_per_position()
    {
        //Arrange
        // Upstream: staff_space (me) * 0.5 * v. A grob on no staff is measured in a staff
        // space of one, so (-4 . 6) becomes (-2 . 3) -- hand-computed, not recorded.
        Item beacon = MakeItem(("positions", new Pair(-4L, 6L)));

        //Act
        object height = ClusterBeacon.Height(beacon);

        //Assert
        Pair result = height.Should().BeOfType<Pair>().Subject;
        result.Car.Should().Be(-2.0);
        result.Cdr.Should().Be(3.0);
    }

    [Fact]
    public void a_beacon_with_no_positions_reports_a_point_rather_than_nothing()
    {
        //Arrange
        // The control for the test above, and the reason Cluster_beacon is the one caller
        // in this group that does NOT pass the empty interval as its fallback. An empty
        // answer here would drop the beacon out of the cluster's vertical extent.
        Item beacon = MakeItem();

        //Act
        object height = ClusterBeacon.Height(beacon);

        //Assert
        Pair result = height.Should().BeOfType<Pair>().Subject;
        result.Car.Should().Be(0.0);
        result.Cdr.Should().Be(0.0);
    }

    // ----- Arpeggio -----

    [Fact]
    public void an_arpeggio_that_spans_no_stems_is_not_cross_staff()
    {
        //Arrange
        // Upstream's loop never runs and the function falls through to SCM_BOOL_F.
        // Derivable from the code rather than measured: with nothing to compare, there is
        // no second axis group to disagree with.
        Item arpeggio = MakeItem();

        //Act
        object crossStaff = Arpeggio.CalcCrossStaff(arpeggio);

        //Assert
        crossStaff.Should().Be(false);
    }

    // ----- Figured_bass_continuation -----

    [Fact]
    public void a_continuation_line_over_no_figures_is_not_offset()
    {
        //Arrange
        // Upstream returns to_scm (0.0) on the empty-figures arm, BEFORE asking for a
        // common reference point -- which is what keeps it from dereferencing a common
        // refpoint that does not exist.
        Item line = MakeItem();

        //Act
        object offset = FiguredBassContinuation.CenterOnFigures(line);

        //Assert
        offset.Should().Be(0.0);
    }

    // ----- ly_assoc: the branch EPG20 implemented, and the narrowing it closed -----

    [Fact]
    public void a_symbol_key_is_looked_up_by_identity()
    {
        //Arrange
        // scm_is_symbol (key) -> scm_assq. Upstream's own first branch.
        object alist = Alist(("hihat", 1L), ("snare", 2L));

        //Act
        Pair found = SchemeUtilities.LyAssoc(Sym("snare"), alist);

        //Assert
        found.Should().NotBeNull();
        found.Cdr.Should().Be(2L);
    }

    [Fact]
    public void an_integer_key_is_an_immediate_and_is_found_by_value()
    {
        //Arrange
        // SCM_IMP (key) -> scm_assq, and a Guile fixnum is an immediate, so (eq? 3 3) is
        // true. This is the same rule EPG14 had to correct in Assq itself.
        object alist = new Pair(new Pair(3L, Sym("three")), Nil.Instance);

        //Act
        Pair found = SchemeUtilities.LyAssoc(3L, alist);

        //Assert
        found.Should().NotBeNull();
        found.Cdr.Should().Be(Sym("three"));
    }

    [Fact]
    public void a_string_key_falls_to_the_equal_branch_and_is_found()
    {
        //Arrange
        // The case the port did NOT handle before EPG20: a key that is neither a symbol
        // nor an immediate goes to scm_assoc, which compares with equal?. Two distinct
        // string objects holding the same characters must match, and under the old
        // assq-only code they never did.
        object alist = new Pair(
            new Pair(new MutableString("cymbal"), 9L), Nil.Instance);

        //Act
        Pair found = SchemeUtilities.LyAssoc(new MutableString("cymbal"), alist);

        //Assert
        found.Should().NotBeNull();
        found.Cdr.Should().Be(9L);
    }

    [Fact]
    public void a_key_that_is_absent_answers_nothing()
    {
        //Arrange
        // The control for the three above: a lookup that finds something for every key is
        // not a lookup.
        object alist = Alist(("hihat", 1L));

        //Act
        Pair found = SchemeUtilities.LyAssoc(Sym("triangle"), alist);

        //Assert
        found.Should().BeNull();
    }

    [Fact]
    public void assoc_get_now_reaches_a_string_key_instead_of_answering_the_fallback()
    {
        //Arrange
        // ly_assoc_get goes through ly_assoc upstream, so closing the branch closes this
        // too. Before EPG20 this answered the fallback, which is a MISS that looks exactly
        // like an absent key.
        object alist = new Pair(
            new Pair(new MutableString("ride"), 4L), Nil.Instance);

        //Act
        object value = SchemeUtilities.LyAssocGet(
            new MutableString("ride"), alist, Sym("fallback"));

        //Assert
        value.Should().Be(4L);
    }

    // ----- ly_is_equal: the second, incomplete copy of equal? EPG20 retired -----

    [Fact]
    public void two_distinct_strings_holding_the_same_characters_are_equal()
    {
        //Arrange
        // Guile compares strings by CONTENT. The engine's own IsEqual used to walk pairs
        // and vectors and then fall through to object.Equals, and MutableString does not
        // override it -- so this was FALSE. Clef_engraver decides whether the clef changed
        // by comparing GLYPH NAMES, which are strings.
        object a = new MutableString("clefs.G");
        object b = new MutableString("clefs.G");

        //Act
        bool equal = SchemeUtilities.IsEqual(a, b);

        //Assert
        equal.Should().BeTrue();
    }

    [Fact]
    public void two_distinct_strings_holding_different_characters_are_not_equal()
    {
        //Arrange
        // The control. Comparing by content must not collapse into comparing nothing.
        object a = new MutableString("clefs.G");
        object b = new MutableString("clefs.F");

        //Act
        bool equal = SchemeUtilities.IsEqual(a, b);

        //Assert
        equal.Should().BeFalse();
    }

    [Fact]
    public void two_distinct_pitches_of_the_same_pitch_are_equal()
    {
        //Arrange
        // scm_equal_p ends by dispatching to a host object's own equality handler, which
        // is what RATCHET-FIX added as ISchemeEqual on 2026-08-08 -- and the engine's copy
        // of equal? never reached it, so the fix applied to Scheme's equal? and not to the
        // engine's. Tie_engraver compares PITCHES through this path.
        object a = new Pitch(0, 2, Rational.Zero);
        object b = new Pitch(0, 2, Rational.Zero);

        //Act
        bool equal = SchemeUtilities.IsEqual(a, b);

        //Assert
        ReferenceEquals(a, b).Should().BeFalse();
        equal.Should().BeTrue();
    }

    [Fact]
    public void two_different_pitches_are_not_equal()
    {
        //Arrange
        // The control for the test above, and the one that would catch a handler that
        // answered true unconditionally.
        object a = new Pitch(0, 2, Rational.Zero);
        object b = new Pitch(0, 3, Rational.Zero);

        //Act
        bool equal = SchemeUtilities.IsEqual(a, b);

        //Assert
        equal.Should().BeFalse();
    }

    [Fact]
    public void assoc_get_still_answers_the_fallback_when_the_key_really_is_absent()
    {
        //Arrange
        // The control: widening the comparison must not make every lookup succeed.
        object alist = new Pair(
            new Pair(new MutableString("ride"), 4L), Nil.Instance);

        //Act
        object value = SchemeUtilities.LyAssocGet(
            new MutableString("crash"), alist, Sym("fallback"));

        //Assert
        value.Should().Be(Sym("fallback"));
    }
}
