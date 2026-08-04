// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The core object model: property alists, probs, music, events, grobs.
/// <para>
/// These run WITHOUT a bootstrapped interpreter, which is deliberate: with no
/// interpreter the property type check has nothing to check against and allows the
/// assignment, so the storage behaviour can be pinned on its own. The type check
/// itself is exercised where the Scheme layer is loaded.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class ObjectModelTests
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

    [Fact]
    public void assq_finds_an_entry_by_identity()
    {
        //Arrange
        object alist = Alist(("a", 1L), ("b", 2L));

        //Act
        Pair found = SchemeUtilities.Assq(Sym("b"), alist);

        //Assert
        found.Should().NotBeNull();
        found.Cdr.Should().Be(2L);
        SchemeUtilities.Assq(Sym("c"), alist).Should().BeNull();
    }

    [Fact]
    public void assq_set_mutates_an_existing_entry_in_place()
    {
        //Arrange
        // Upstream's scm_assq_set_x mutates when present, so the returned list is the
        // SAME list. Callers that assume a fresh list would silently lose the write.
        object alist = Alist(("a", 1L));

        //Act
        object updated = SchemeUtilities.AssqSet(alist, Sym("a"), 9L);

        //Assert
        updated.Should().BeSameAs(alist);
        SchemeUtilities.Assq(Sym("a"), updated).Cdr.Should().Be(9L);
    }

    [Fact]
    public void assq_set_conses_a_new_entry_onto_the_front()
    {
        //Arrange
        object alist = Alist(("a", 1L));

        //Act
        object updated = SchemeUtilities.AssqSet(alist, Sym("b"), 2L);

        //Assert
        updated.Should().NotBeSameAs(alist);
        SchemeUtilities.Assq(Sym("b"), updated).Cdr.Should().Be(2L);
        SchemeUtilities.Assq(Sym("a"), updated).Cdr.Should().Be(1L);
    }

    [Fact]
    public void deep_copy_rebuilds_pairs_but_shares_leaves()
    {
        //Arrange
        Pair inner = new Pair(1L, 2L);
        object list = Pair.List(inner, "text");

        //Act
        object copy = SchemeUtilities.DeepCopy(list);

        //Assert
        copy.Should().NotBeSameAs(list);
        Pair copiedInner = (Pair)((Pair)copy).Car;
        copiedInner.Should().NotBeSameAs(inner);
        copiedInner.Car.Should().Be(1L);
    }

    [Fact]
    public void deep_copy_walks_an_improper_tail()
    {
        //Arrange
        object improper = new Pair(1L, new Pair(2L, 3L));

        //Act
        object copy = SchemeUtilities.DeepCopy(improper);

        //Assert
        Pair second = (Pair)((Pair)copy).Cdr;
        second.Cdr.Should().Be(3L);
    }

    [Fact]
    public void a_prob_reads_the_mutable_alist_before_the_immutable_one()
    {
        //Arrange
        Prob prob = new Prob(Sym("Test"), Alist(("colour", "red")));

        //Act
        object before = prob.GetProperty("colour");
        prob.SetProperty("colour", "blue");
        object after = prob.GetProperty("colour");

        //Assert
        before.Should().Be("red");
        after.Should().Be("blue");
    }

    [Fact]
    public void an_unset_prob_property_reads_as_the_empty_list()
    {
        //Arrange
        Prob prob = new Prob(Sym("Test"), Nil.Instance);

        //Act
        object value = prob.GetProperty("missing");

        //Assert
        value.Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void unsetting_a_prob_property_falls_back_to_the_immutable_value()
    {
        //Arrange
        Prob prob = new Prob(Sym("Test"), Alist(("colour", "red")));
        prob.SetProperty("colour", "blue");

        //Act
        prob.UnsetProperty(Sym("colour"));

        //Assert
        prob.GetProperty("colour").Should().Be("red");
    }

    [Fact]
    public void a_prob_takes_its_name_from_its_name_property()
    {
        //Arrange
        Prob named = new Prob(Sym("Test"), Alist(("name", Sym("NoteEvent"))));
        Prob unnamed = new Prob(Sym("Test"), Nil.Instance);

        //Act
        string name = named.Name;

        //Assert
        name.Should().Be("NoteEvent");
        unnamed.Name.Should().Be("Prob");
    }

    [Fact]
    public void music_carries_its_type_tags_in_the_types_property()
    {
        //Arrange
        object types = Pair.List(Sym("note-event"), Sym("rhythmic-event"), Sym("event"));
        MusicObject music = new MusicObject(Alist(("types", types)));

        //Act
        bool isNote = music.IsMusicType("note-event");

        //Assert
        isNote.Should().BeTrue();
        music.IsMusicType("rest-event").Should().BeFalse();
    }

    [Fact]
    public void music_length_falls_back_to_its_duration()
    {
        //Arrange
        // No length-callback in the immutable alist, so the port installs upstream's
        // default: the duration expressed as a moment.
        MusicObject music = new MusicObject(Nil.Instance);
        music.SetProperty("duration", new Duration(2, 0));

        //Act
        Moment length = music.GetLength();

        //Assert
        length.Should().Be(new Moment(new Rational(1, 4)));
    }

    [Fact]
    public void music_length_prefers_an_explicit_length_property()
    {
        //Arrange
        MusicObject music = new MusicObject(Nil.Instance);
        music.SetProperty("duration", new Duration(2, 0));
        music.SetProperty("length", new Moment(new Rational(1, 2)));

        //Act
        Moment length = music.GetLength();

        //Assert
        length.Should().Be(new Moment(new Rational(1, 2)));
    }

    [Fact]
    public void a_dotted_duration_lasts_half_again_as_long()
    {
        //Arrange
        MusicObject music = new MusicObject(Nil.Instance);
        music.SetProperty("duration", new Duration(2, 1));

        //Act
        Moment length = music.GetLength();

        //Assert
        length.Should().Be(new Moment(new Rational(3, 8)));
    }

    [Fact]
    public void cloning_music_deep_copies_its_mutable_properties()
    {
        //Arrange
        MusicObject inner = new MusicObject(Nil.Instance);
        inner.SetProperty("duration", new Duration(2, 0));

        MusicObject outer = new MusicObject(Nil.Instance);
        outer.SetProperty("element", inner);

        //Act
        MusicObject clone = outer.Clone();
        MusicObject clonedInner = (MusicObject)clone.GetProperty("element");
        clonedInner.SetProperty("duration", new Duration(0, 0));

        //Assert
        clonedInner.Should().NotBeSameAs(inner);
        ((Duration)inner.GetProperty("duration")).DurationLog.Should().Be(2);
    }

    [Fact]
    public void transposing_music_moves_every_pitch_it_holds()
    {
        //Arrange
        MusicObject music = new MusicObject(Nil.Instance);
        music.SetProperty("pitch", new Pitch(0, 0, Rational.Zero));

        //Act
        // Up a major second: from central C to D.
        music.Transpose(new Pitch(0, 1, Rational.Zero));

        //Assert
        Pitch result = (Pitch)music.GetProperty("pitch");
        result.NoteName.Should().Be(1);
        result.Octave.Should().Be(0);
    }

    [Fact]
    public void untransposable_music_is_left_alone()
    {
        //Arrange
        MusicObject music = new MusicObject(Nil.Instance);
        music.SetProperty("pitch", new Pitch(0, 0, Rational.Zero));
        music.SetProperty("untransposable", true);

        //Act
        music.Transpose(new Pitch(0, 1, Rational.Zero));

        //Assert
        ((Pitch)music.GetProperty("pitch")).NoteName.Should().Be(0);
    }

    [Fact]
    public void transposing_recurses_into_the_element_and_the_elements_list()
    {
        //Arrange
        MusicObject inner = new MusicObject(Nil.Instance);
        inner.SetProperty("pitch", new Pitch(0, 0, Rational.Zero));

        MusicObject listed = new MusicObject(Nil.Instance);
        listed.SetProperty("pitch", new Pitch(0, 2, Rational.Zero));

        MusicObject outer = new MusicObject(Nil.Instance);
        outer.SetProperty("element", inner);
        outer.SetProperty("elements", Pair.List(listed));

        //Act
        outer.Transpose(new Pitch(1, 0, Rational.Zero));

        //Assert
        ((Pitch)inner.GetProperty("pitch")).Octave.Should().Be(1);
        ((Pitch)listed.GetProperty("pitch")).Octave.Should().Be(1);
    }

    [Fact]
    public void cumulative_length_lays_a_music_list_end_to_end()
    {
        //Arrange
        List<object> notes = new List<object>();
        for (int i = 0; i < 3; i++)
        {
            MusicObject note = new MusicObject(Nil.Instance);
            note.SetProperty("duration", new Duration(2, 0));
            notes.Add(note);
        }

        //Act
        Moment total = MusicSequence.CumulativeLength(Pair.ListFrom(notes));

        //Assert
        total.Should().Be(new Moment(new Rational(3, 4)));
    }

    [Fact]
    public void maximum_length_takes_the_longest_element()
    {
        //Arrange
        MusicObject quarter = new MusicObject(Nil.Instance);
        quarter.SetProperty("duration", new Duration(2, 0));
        MusicObject half = new MusicObject(Nil.Instance);
        half.SetProperty("duration", new Duration(1, 0));

        //Act
        Moment longest = MusicSequence.MaximumLength(Pair.List(quarter, half));

        //Assert
        longest.Should().Be(new Moment(new Rational(1, 2)));
    }

    [Fact]
    public void an_event_belongs_to_every_class_in_its_class_list()
    {
        //Arrange
        object classes = Pair.List(Sym("note-event"), Sym("rhythmic-event"), Sym("event"));

        //Act
        StreamEvent ev = new StreamEvent(classes, Nil.Instance);

        //Assert
        ev.IsInEventClass("note-event").Should().BeTrue();
        ev.IsInEventClass("event").Should().BeTrue();
        ev.IsInEventClass("rest-event").Should().BeFalse();
    }

    [Fact]
    public void making_an_event_transposable_lifts_pitches_into_the_mutable_alist()
    {
        //Arrange
        // A pitch stored immutably is shared with every event of the type, so it must
        // be copied down before a transposition is allowed to touch it.
        StreamEvent ev = new StreamEvent(
            Pair.List(Sym("note-event")),
            Alist(("pitch", new Pitch(0, 0, Rational.Zero))));

        //Act
        ev.MakeTransposable();

        //Assert
        SchemeUtilities.Assq(Sym("pitch"), ev.MutablePropertyAlist).Should().NotBeNull();
    }

    [Fact]
    public void camel_case_becomes_a_hyphenated_lisp_identifier()
    {
        //Arrange
        string name = "NoteEvent";

        //Act
        string converted = Misc.CamelCaseToLispIdentifier(name);

        //Assert
        converted.Should().Be("note-event");
        
        // NOTE: upstream's own docstring claims FooBar_Bla -> foo-bar-bla, but its
        // code inserts a hyphen before the capital AND then converts the underscore,
        // so the real answer has two. Ported faithfully; the docstring is wrong.
        Misc.CamelCaseToLispIdentifier("FooBar_Bla").Should().Be("foo-bar--bla");
    }

    [Fact]
    public void a_grob_takes_its_interfaces_from_its_meta_property()
    {
        //Arrange
        object meta = Alist(
            ("name", Sym("NoteHead")),
            ("interfaces", Pair.List(Sym("note-head-interface"), Sym("grob-interface"))));

        //Act
        Item grob = new Item(Alist(("meta", meta)));

        //Assert
        grob.Name.Should().Be("NoteHead");
        grob.HasInterface("note-head-interface").Should().BeTrue();
        grob.HasInterface("stem-interface").Should().BeFalse();
    }

    [Fact]
    public void a_grob_reads_the_mutable_alist_before_the_immutable_one()
    {
        //Arrange
        Item grob = new Item(Alist(("staff-position", 3L)));

        //Act
        object before = grob.GetProperty("staff-position");
        grob.SetProperty("staff-position", 5L);

        //Assert
        before.Should().Be(3L);
        grob.GetProperty("staff-position").Should().Be(5L);
    }

    [Fact]
    public void grob_objects_are_a_separate_namespace_from_grob_properties()
    {
        //Arrange
        Item head = new Item(GrobBasics());
        Item stem = new Item(GrobBasics());

        //Act
        head.SetObject("stem", stem);

        //Assert
        head.GetObject("stem").Should().BeSameAs(stem);
        head.GetProperty("stem").Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void translating_a_grob_accumulates_its_offset()
    {
        //Arrange
        Item grob = new Item(GrobBasics());

        //Act
        grob.TranslateAxis(2.0, Axis.X);
        grob.TranslateAxis(3.0, Axis.X);

        //Assert
        grob.GetOffset(Axis.X).Should().Be(5.0);
    }

    [Fact]
    public void an_infinite_translation_is_reported_and_ignored()
    {
        //Arrange
        Item grob = new Item(GrobBasics());

        //Act
        grob.TranslateAxis(double.PositiveInfinity, Axis.X);

        //Assert
        grob.GetOffset(Axis.X).Should().Be(0.0);
    }

    [Fact]
    public void a_relative_coordinate_sums_the_offsets_up_the_parent_chain()
    {
        //Arrange
        Item root = new Item(GrobBasics());
        Item middle = new Item(GrobBasics());
        Item leaf = new Item(GrobBasics());

        middle.SetParent(root, Axis.X);
        leaf.SetParent(middle, Axis.X);

        middle.TranslateAxis(2.0, Axis.X);
        leaf.TranslateAxis(3.0, Axis.X);

        //Act
        double offset = leaf.RelativeCoordinate(root, Axis.X);

        //Assert
        offset.Should().Be(5.0);
        leaf.RelativeCoordinate(middle, Axis.X).Should().Be(3.0);
        leaf.RelativeCoordinate(leaf, Axis.X).Should().Be(0.0);
    }

    [Fact]
    public void the_common_refpoint_of_two_grobs_is_their_nearest_shared_ancestor()
    {
        //Arrange
        Item root = new Item(GrobBasics());
        Item left = new Item(GrobBasics());
        Item right = new Item(GrobBasics());
        Item deepLeft = new Item(GrobBasics());

        left.SetParent(root, Axis.X);
        right.SetParent(root, Axis.X);
        deepLeft.SetParent(left, Axis.X);

        //Act
        Grob common = deepLeft.CommonRefpoint(right, Axis.X);

        //Assert
        common.Should().BeSameAs(root);
        deepLeft.CommonRefpoint(left, Axis.X).Should().BeSameAs(left);
    }

    [Fact]
    public void an_extent_is_read_from_the_extent_property_and_shifted_by_the_offset()
    {
        //Arrange
        Item root = new Item(GrobBasics());
        Item grob = new Item(GrobBasics());
        grob.SetParent(root, Axis.X);
        grob.SetProperty("X-extent", new Pair(-1.0, 1.0));
        grob.TranslateAxis(4.0, Axis.X);

        //Act
        Interval extent = grob.Extent(root, Axis.X);

        //Assert
        extent.Should().Be(new Interval(3.0, 5.0));
        grob.Extent(grob, Axis.X).Should().Be(new Interval(-1.0, 1.0));
    }

    [Fact]
    public void a_minimum_extent_widens_the_measured_one()
    {
        //Arrange
        Item grob = new Item(GrobBasics());
        grob.SetProperty("X-extent", new Pair(-1.0, 1.0));
        grob.SetProperty("minimum-X-extent", new Pair(-3.0, 0.5));

        //Act
        Interval extent = grob.Extent(grob, Axis.X);

        //Assert
        extent.Should().Be(new Interval(-3.0, 1.0));
    }

    [Fact]
    public void a_grob_that_commits_suicide_keeps_only_its_cause()
    {
        //Arrange
        StreamEvent cause = new StreamEvent(Pair.List(Sym("note-event")), Nil.Instance);
        Item grob = new Item(Alist(("staff-position", 3L)));
        grob.SetProperty("cause", cause);

        //Act
        grob.Suicide();

        //Assert
        grob.IsLive.Should().BeFalse();
        grob.GetProperty("staff-position").Should().BeSameAs(Nil.Instance);
        grob.GetProperty("cause").Should().BeSameAs(cause);
    }

    [Fact]
    public void a_dead_grob_refuses_further_property_writes()
    {
        //Arrange
        Item grob = new Item(GrobBasics());
        grob.Suicide();

        //Act
        grob.SetProperty("staff-position", 5L);

        //Assert
        grob.GetProperty("staff-position").Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void an_item_knows_which_side_of_a_break_it_is_on()
    {
        //Arrange
        Item original = new Item(GrobBasics());
        Item left = (Item)original.Clone();
        Item right = (Item)original.Clone();
        original.SetPrebrokenPiece(Direction.Negative, left);
        original.SetPrebrokenPiece(Direction.Positive, right);

        //Act
        Direction leftSide = left.BreakStatusDirection();

        //Assert
        leftSide.Should().Be(Direction.Negative);
        right.BreakStatusDirection().Should().Be(Direction.Positive);
        original.BreakStatusDirection().Should().Be(Direction.Center);
        original.IsBroken.Should().BeTrue();
    }

    [Fact]
    public void find_prebroken_piece_returns_the_item_itself_for_the_centre()
    {
        //Arrange
        Item original = new Item(GrobBasics());
        Item left = (Item)original.Clone();
        original.SetPrebrokenPiece(Direction.Negative, left);

        //Act
        Item centre = original.FindPrebrokenPiece(Direction.Center);

        //Assert
        centre.Should().BeSameAs(original);
        original.FindPrebrokenPiece(Direction.Negative).Should().BeSameAs(left);
    }

    [Fact]
    public void non_musical_is_inherited_from_the_horizontal_parent()
    {
        //Arrange
        Item column = new Item(Alist(("non-musical", true)));
        Item child = new Item(GrobBasics());
        child.SetParent(column, Axis.X);

        //Act
        bool nonMusical = Item.IsNonMusical(child);

        //Assert
        nonMusical.Should().BeTrue();
        Item.IsNonMusical(new Item(GrobBasics())).Should().BeFalse();
    }

    [Fact]
    public void setting_a_spanner_bound_also_makes_it_the_horizontal_parent()
    {
        //Arrange
        Spanner spanner = new Spanner(GrobBasics());
        Item left = new Item(GrobBasics());
        Item right = new Item(GrobBasics());

        //Act
        spanner.SetBound(Direction.Negative, left);
        spanner.SetBound(Direction.Positive, right);

        //Assert
        spanner.GetBound(Direction.Negative).Should().BeSameAs(left);
        spanner.GetBound(Direction.Positive).Should().BeSameAs(right);
        spanner.GetParent(Axis.X).Should().BeSameAs(left);
    }

    [Fact]
    public void spanner_length_is_the_distance_between_its_bounds()
    {
        //Arrange
        Item root = new Item(GrobBasics());
        Item left = new Item(GrobBasics());
        Item right = new Item(GrobBasics());
        left.SetParent(root, Axis.X);
        right.SetParent(root, Axis.X);
        left.TranslateAxis(2.0, Axis.X);
        right.TranslateAxis(7.0, Axis.X);

        Spanner spanner = new Spanner(GrobBasics());
        spanner.SetBound(Direction.Negative, left);
        spanner.SetBound(Direction.Positive, right);

        //Act
        double length = spanner.SpannerLength();

        //Assert
        length.Should().Be(5.0);
    }

    [Fact]
    public void the_print_stencil_wraps_the_grob_as_its_own_cause()
    {
        //Arrange
        // grob-cause is what makes point-and-click work: the backend is handed the
        // originating grob at draw time.
        Item grob = new Item(GrobBasics());
        Stencil shape = Lookup.FilledBox(new Box(new Interval(0, 1), new Interval(0, 1)));
        grob.SetProperty("stencil", shape);

        //Act
        Stencil printed = grob.GetPrintStencil();

        //Assert
        Pair head = (Pair)printed.Expression;
        head.Car.Should().BeSameAs(Symbol.Intern("grob-cause"));
        ((Pair)head.Cdr).Car.Should().BeSameAs(grob);
    }

    [Fact]
    public void a_transparent_grob_prints_nothing_but_keeps_its_extents()
    {
        //Arrange
        Item grob = new Item(GrobBasics());
        Stencil shape = Lookup.FilledBox(new Box(new Interval(0, 2), new Interval(0, 1)));
        grob.SetProperty("stencil", shape);
        grob.SetProperty("transparent", true);

        //Act
        Stencil printed = grob.GetPrintStencil();

        //Assert
        printed.Expression.Should().BeSameAs(Nil.Instance);
        printed.XExtent.Should().Be(new Interval(0, 2));
    }

    [Fact]
    public void a_cloned_grob_records_the_original_and_does_not_share_writes()
    {
        //Arrange
        Item original = new Item(GrobBasics());
        original.SetProperty("staff-position", 3L);

        //Act
        Item clone = (Item)original.Clone();
        clone.SetProperty("staff-position", 5L);

        //Assert
        clone.Original.Should().BeSameAs(original);
        original.GetProperty("staff-position").Should().Be(3L);
        clone.GetProperty("staff-position").Should().Be(5L);
    }

    [Fact]
    public void a_nested_property_path_reads_through_nested_alists()
    {
        //Arrange
        // nested_property, the READ half of the nested-override machinery — ported
        // on demand from the parser's `lookup` rules (RAG1).
        object inner = Pair.List(new Pair(Symbol.Intern("beamed-stem-lengths"), 4L));
        object alist = Pair.List(new Pair(Symbol.Intern("details"), inner));

        //Act
        object found = NestedProperty.Get(
            alist,
            Pair.List(Symbol.Intern("details"), Symbol.Intern("beamed-stem-lengths")));
        object wholeSublist = NestedProperty.Get(alist, Pair.List(Symbol.Intern("details")));

        //Assert
        found.Should().Be(4L);
        wholeSublist.Should().BeSameAs(inner);
    }

    [Fact]
    public void a_nested_property_path_that_misses_returns_the_fallback()
    {
        //Arrange
        object alist = Pair.List(new Pair(Symbol.Intern("details"), Nil.Instance));

        //Act
        object missing = NestedProperty.Get(alist, Pair.List(Symbol.Intern("absent")));
        object fallback = NestedProperty.Get(
            alist, Pair.List(Symbol.Intern("absent")), "fallback");

        //Assert
        missing.Should().BeSameAs(Nil.Instance);
        fallback.Should().Be("fallback");
    }
}
