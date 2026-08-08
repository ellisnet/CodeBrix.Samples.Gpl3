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
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG22: the iterator layer and the music plumbing around it.
/// <para>
/// The group's whole purpose was to make already-landed machinery REACHABLE, so these
/// fence reachability rather than internals: that the wrapper callbacks are registered
/// AND answer for the wrapped music, that every iterator constructor a music type can
/// name now exists, and that the two pieces pulled forward from other groups
/// (<c>ly:add-listener</c> from EPG23, break substitution's Direction half from EPG15)
/// do the one job that demanded them.
/// </para>
/// </summary>
public class Epg22IteratorTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static MusicObject Wrapper(MusicObject element, Moment? elementLength = null)
    {
        object immutable = Pair.List(
            new Pair(Sym("name"), Sym("RelativeOctaveMusic")),
            new Pair(Sym("types"), Pair.List(Sym("RelativeOctaveMusic"), Sym("music-wrapper-music"))));

        MusicObject music = new MusicObject(immutable);
        if (element != null)
        {
            if (elementLength.HasValue)
            {
                element.SetProperty(Sym("length"), elementLength.Value);
            }

            music.SetProperty(Sym("element"), element);
        }

        return music;
    }

    private static MusicObject Leaf(Moment length)
    {
        object immutable = Pair.List(
            new Pair(Sym("name"), Sym("NoteEvent")),
            new Pair(Sym("types"), Pair.List(Sym("NoteEvent"), Sym("event"))));

        MusicObject music = new MusicObject(immutable);
        music.SetProperty(Sym("length"), length);
        return music;
    }

    // ----- music-wrapper.cc: the file that unmasked the sweep -----

    [Fact]
    public void music_wrapper_length_callback_answers_for_the_wrapped_element()
    {
        //Arrange
        MusicObject element = Leaf(new Moment(new Rational(1, 4)));
        MusicObject wrapper = Wrapper(element);

        //Act
        Moment length = MusicWrapper.LengthCallback(wrapper);

        //Assert
        // This is the whole point of the file: without it a wrapper reports ZERO, and an
        // expression of zero length engraves an empty page. 19,940 failures in one sweep.
        length.Should().Be(new Moment(new Rational(1, 4)));
    }

    [Fact]
    public void music_wrapper_length_callback_answers_zero_with_no_element()
    {
        //Arrange
        MusicObject wrapper = Wrapper(null);

        //Act
        Moment length = MusicWrapper.LengthCallback(wrapper);

        //Assert
        length.Should().Be(new Moment(0));
    }

    [Fact]
    public void music_wrapper_start_callback_answers_for_the_wrapped_element()
    {
        //Arrange
        MusicObject element = Leaf(new Moment(new Rational(1, 4)));
        MusicObject wrapper = Wrapper(null);

        //Act
        Moment withoutElement = MusicWrapper.StartCallback(wrapper);
        wrapper.SetProperty(Sym("element"), element);
        Moment withElement = MusicWrapper.StartCallback(wrapper);

        //Assert
        withoutElement.Should().Be(new Moment(0));
        withElement.Should().Be(element.StartMoment());
    }

    // ----- the iterator constructor closure -----

    [Fact]
    public void every_epg22_iterator_constructor_is_implemented()
    {
        //Arrange
        string[] owed =
        {
            "ly:context-specced-music-iterator::constructor",
            "ly:initial-context-music-iterator::constructor",
            "ly:change-iterator::constructor",
            "ly:apply-context-iterator::constructor",
            "ly:property-iterator::constructor",
            "ly:property-unset-iterator::constructor",
            "ly:push-property-iterator::constructor",
            "ly:pop-property-iterator::constructor",
            "ly:quote-iterator::constructor",
            "ly:part-combine-iterator::constructor",
        };

        //Act
        IReadOnlyCollection<string> ported = IteratorPrimitives.Ported;

        //Assert
        foreach (string name in owed)
        {
            ported.Should().Contain(name);
            IteratorPrimitives.NotYetPorted.Should().NotContain(name);
        }
    }

    [Fact]
    public void no_iterator_constructor_remains_owed()
    {
        //Arrange
        // G5. This list is the group's exit criterion and it is now EMPTY: EPG22 landed
        // ten of the twenty-eight constructors, EPG17 landed nine (four in its first slice
        // and the five Repeat_styler-bound ones together), and EPG18 landed the last,
        // ly:lyric-combine-music-iterator::constructor. The fence is assert-empty from
        // here on, so any constructor that ever leaves the ported table fails this test
        // rather than silently falling back to a default iterator.

        //Act
        IReadOnlyList<string> owed = IteratorPrimitives.NotYetPorted;

        //Assert
        owed.Should().BeEmpty();
    }

    [Fact]
    public void the_iterator_constructor_denominator_is_the_upstream_twenty_eight()
    {
        //Arrange / Act
        int total = IteratorPrimitives.Ported.Count + IteratorPrimitives.NotYetPorted.Count;

        //Assert
        // G5's denominator. It is asserted here rather than remembered, because the only
        // way this number moves is a constructor being dropped from one list without
        // arriving in the other.
        total.Should().Be(28);
    }

    // ----- break-substitution.cc's Direction half (pulled forward from EPG15) -----

    [Fact]
    public void a_prebroken_piece_takes_its_originals_object_links()
    {
        //Arrange
        Item original = new Item(BasicProperties());
        Item linked = new Item(BasicProperties());
        original.SetObject(Sym("elements"), Grobs(linked));

        Item left = (Item)original.Clone();
        original.SetPrebrokenPiece(Direction.Negative, left);

        //Act
        left.HandlePrebrokenDependencies();

        //Assert
        // The link ITSELF is what matters here, not its contents. Before EPG22 this
        // answered '(), and ly:span-bar::before-line-breaking -- which reads elements
        // with NO default -- threw on every span bar: 87 files in one sweep. An ARRAY,
        // even an empty one, is all that call site needs.
        left.GetObject(Sym("elements")).Should().BeOfType<GrobArray>();

        // Empty, and faithfully so: substitute_grob answers the linked item's piece for
        // THIS side of the break, and an item that was never broken has none, so
        // upstream drops it from the new array. The next test covers the case where it
        // does have one.
        ((GrobArray)left.GetObject(Sym("elements"))).Count.Should().Be(0);
    }

    [Fact]
    public void a_prebroken_piece_prefers_the_linked_grobs_own_piece_for_the_same_side()
    {
        //Arrange
        Item original = new Item(BasicProperties());
        Item linked = new Item(BasicProperties());
        Item linkedLeft = (Item)linked.Clone();
        linked.SetPrebrokenPiece(Direction.Negative, linkedLeft);

        original.SetObject(Sym("elements"), Grobs(linked));
        Item left = (Item)original.Clone();
        original.SetPrebrokenPiece(Direction.Negative, left);

        //Act
        left.HandlePrebrokenDependencies();

        //Assert
        // The substitution, not just the copy: the left-hand clone must see the left-hand
        // clone of what it links to, which is what makes a broken span bar span the
        // broken bar lines.
        GrobArray elements = (GrobArray)left.GetObject(Sym("elements"));
        elements.Count.Should().Be(1);
        object first = null;
        foreach (Grob grob in elements)
        {
            first = grob;
            break;
        }

        ReferenceEquals(first, linkedLeft).Should().BeTrue();
    }

    [Fact]
    public void break_substitution_passes_plain_values_through_untouched()
    {
        //Arrange
        object value = Pair.List(Sym("a"), 3L);

        //Act
        object substituted = BreakSubstitution.DoBreakSubstitution(Direction.Negative, value);

        //Assert
        // A pair is rebuilt rather than shared, so the check is on CONTENT: nothing in a
        // list of non-grobs may be dropped or altered on the way through.
        List<object> rebuilt = Pair.ToList(substituted);
        rebuilt.Count.Should().Be(2);
        ReferenceEquals(rebuilt[0], Sym("a")).Should().BeTrue();
        rebuilt[1].Should().Be(3L);
    }

    // ----- articulations.cc -----

    [Fact]
    public void articulation_list_matches_one_articulation_per_note()
    {
        //Arrange
        Symbol className = Sym("string-number-event");
        StreamEvent inChord = Event(className);
        StreamEvent note1 = Event(Sym("note-event"));
        note1.SetProperty(Sym("articulations"), Pair.List(inChord));
        StreamEvent note2 = Event(Sym("note-event"));
        StreamEvent freeStanding = Event(className);

        //Act
        object result = Articulations.ArticulationList(
            new List<StreamEvent> { note1, note2 },
            new List<StreamEvent> { freeStanding },
            className);

        //Assert
        // Note 1 takes the articulation written INSIDE its chord, note 2 the one written
        // outside -- the whole reason the function exists.
        List<object> list = Pair.ToList(result);
        list.Count.Should().Be(2);
        ReferenceEquals(list[0], inChord).Should().BeTrue();
        ReferenceEquals(list[1], freeStanding).Should().BeTrue();
    }

    [Fact]
    public void articulation_list_answers_the_empty_list_for_a_note_with_none()
    {
        //Arrange
        StreamEvent note = Event(Sym("note-event"));

        //Act
        object result = Articulations.ArticulationList(
            new List<StreamEvent> { note },
            new List<StreamEvent>(),
            Sym("string-number-event"));

        //Assert
        List<object> list = Pair.ToList(result);
        list.Count.Should().Be(1);
        list[0].Should().BeOfType<Nil>();
    }

    // ----- grob-interface.cc -----

    [Theory]
    [InlineData("Slur", "slur-interface")]
    [InlineData("NoteHead", "note-head-interface")]
    [InlineData("SpanBar", "span-bar-interface")]
    public void an_interface_name_is_derived_from_the_cxx_class_name(string cxxName, string expected)
    {
        //Act
        Symbol name = GrobInterface.InterfaceName(cxxName);

        //Assert
        name.Name.Should().Be(expected);
    }

    [Fact]
    public void an_interface_name_already_ending_in_interface_is_left_alone()
    {
        //Act
        Symbol name = GrobInterface.InterfaceName("StaffSymbolInterface");

        //Assert
        // camel_case_to_lisp_identifier yields staff-symbol-interface, which already
        // carries the suffix -- appending a second one is the bug the check prevents.
        name.Name.Should().Be("staff-symbol-interface");
    }

    /// <summary>
    /// A minimal non-empty basic-property alist. It has to be non-empty: Grob.IsLive
    /// tests the IMMUTABLE alist, and SetObject silently refuses on a grob that is not
    /// live -- so a fixture built from Nil would look like a dead grob and quietly store
    /// nothing.
    /// </summary>
    private static object BasicProperties()
        => Pair.List(new Pair(Sym("meta"), Pair.List(new Pair(Sym("name"), Sym("TestItem")))));

    private static GrobArray Grobs(params Grob[] grobs)
    {
        GrobArray array = new GrobArray();
        foreach (Grob grob in grobs)
        {
            array.Add(grob);
        }

        return array;
    }

    private static StreamEvent Event(Symbol className)
        => new StreamEvent(Pair.List(className), Nil.Instance);
}
