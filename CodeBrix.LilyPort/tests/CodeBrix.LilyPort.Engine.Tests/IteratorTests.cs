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
/// The iterator family: turning a music tree into the stream of events the translation
/// layer consumes.
/// <para>
/// Upstream has no unit tests for any of this, so these are written from the behaviour
/// the C++ describes. What they pin is the part that would otherwise fail silently:
/// that a sequence advances one element at a time in the right order, that a chord
/// reports everything at one moment, and that an iterator stops when its music is
/// exhausted rather than spinning.
/// </para>
/// </summary>
public class IteratorTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    /// <summary>
    /// Builds music with the given type tags and length, without needing the Scheme
    /// layer. Real music gets its immutable alist from define-music-types.scm; a
    /// fixture supplies one directly so the iterators can be exercised on their own.
    /// </summary>
    private static MusicObject Music(string name, Rational length, params string[] types)
    {
        List<object> tags = new List<object> { Sym(name) };
        foreach (string type in types)
        {
            tags.Add(Sym(type));
        }

        object immutable = Pair.List(
            new Pair(Sym("name"), Sym(name)),
            new Pair(Sym("types"), Pair.ListFrom(tags)));

        MusicObject music = new MusicObject(immutable);
        music.SetProperty(Sym("length"), new Moment(length));
        return music;
    }

    private static MusicObject Sequence(params MusicObject[] elements)
    {
        object immutable = Pair.List(
            new Pair(Sym("name"), Sym("SequentialMusic")),
            new Pair(Sym("types"), Pair.List(Sym("SequentialMusic"), Sym("music-wrapper-music"))));

        MusicObject music = new MusicObject(immutable);
        music.SetProperty(Sym("elements"), Pair.ListFrom(elements));

        Moment total = Moment.Zero;
        foreach (MusicObject element in elements)
        {
            total += element.GetLength();
        }

        music.SetProperty(Sym("length"), total);
        return music;
    }

    private sealed class CountingIterator : SimpleMusicIterator
    {
        public List<Moment> Processed { get; } = new List<Moment>();

        public override void Process(Moment until)
        {
            Processed.Add(until);
            base.Process(until);
        }
    }

    [Fact]
    public void a_simple_iterator_comes_due_at_the_start_and_retires_after_the_music()
    {
        //Arrange
        MusicObject note = Music("NoteEvent", new Rational(1, 4), "event", "rhythmic-event");
        MusicIterator iterator = MusicIterator.CreateTopIterator(note);

        //Act
        Moment before = iterator.PendingMoment;
        bool okBefore = iterator.Ok;
        iterator.Process(new Moment(new Rational(1, 4)));
        Moment after = iterator.PendingMoment;

        //Assert
        before.Should().Be(Moment.Zero);
        okBefore.Should().BeTrue();
        after.Should().Be(Moment.Infinity);
        iterator.Ok.Should().BeFalse();
    }

    [Fact]
    public void a_simple_iterator_stays_due_until_the_music_is_over()
    {
        //Arrange
        // Processing to a moment INSIDE the music must leave the iterator due at the
        // music's end, not retire it -- getting this backwards would drop everything
        // after the first timestep of any multi-timestep element.
        MusicObject note = Music("NoteEvent", new Rational(1, 2), "event", "rhythmic-event");
        MusicIterator iterator = MusicIterator.CreateTopIterator(note);

        //Act
        iterator.Process(new Moment(new Rational(1, 4)));

        //Assert
        iterator.PendingMoment.Should().Be(new Moment(new Rational(1, 2)));
        iterator.Ok.Should().BeTrue();
    }

    [Fact]
    public void the_iterator_records_the_length_and_start_of_its_music()
    {
        //Arrange
        MusicObject note = Music("NoteEvent", new Rational(3, 8), "event", "rhythmic-event");

        //Act
        MusicIterator iterator = MusicIterator.CreateTopIterator(note);

        //Assert
        iterator.MusicLength.Should().Be(new Moment(new Rational(3, 8)));
        iterator.MusicStartMoment.Should().Be(Moment.Zero);
        iterator.Music.Should().BeSameAs(note);
    }

    [Fact]
    public void an_iterator_with_no_registered_constructor_falls_back_the_way_upstream_does()
    {
        //Arrange
        // No iterator-ctor property at all. Upstream picks a wrapper for music with an
        // element, an event iterator for an event, and a simple iterator otherwise --
        // and so must the port, because that is what makes an unported music type
        // merely limited rather than fatal.
        MusicObject wrapped = Music("NoteEvent", new Rational(1, 4), "event");
        MusicObject wrapper = Music("UnfoldedRepeatedMusic", new Rational(1, 4));
        wrapper.SetProperty(Sym("element"), wrapped);
        MusicObject plain = Music("SomeUnportedMusic", new Rational(1, 4));

        //Act & Assert
        MusicIterator.CreateTopIterator(wrapper).Should().BeOfType<MusicWrapperIterator>();
        MusicIterator.CreateTopIterator(wrapped).Should().BeOfType<EventIterator>();
        MusicIterator.CreateTopIterator(plain).Should().BeOfType<SimpleMusicIterator>();
    }

    [Fact]
    public void a_wrapper_iterator_delegates_its_timing_to_its_child()
    {
        //Arrange
        MusicObject inner = Music("NoteEvent", new Rational(1, 4), "event");
        MusicObject wrapper = Music("UnfoldedRepeatedMusic", new Rational(1, 4));
        wrapper.SetProperty(Sym("element"), inner);

        //Act
        MusicIterator iterator = MusicIterator.CreateTopIterator(wrapper);
        Moment pending = iterator.PendingMoment;
        iterator.Process(new Moment(new Rational(1, 4)));

        //Assert
        pending.Should().Be(Moment.Zero);
        iterator.PendingMoment.Should().Be(Moment.Infinity);
    }

    [Fact]
    public void music_length_is_read_from_the_music_when_the_iterator_is_built()
    {
        //Arrange
        // The iterator caches length and start ONCE, at construction. Sequential and
        // simultaneous behaviour needs real iterator-ctor procedures and therefore the
        // Scheme layer -- MusicIterationTests exercises those end to end.
        MusicObject first = Music("NoteEvent", new Rational(1, 4), "event");
        MusicObject second = Music("NoteEvent", new Rational(1, 4), "event");
        MusicObject sequence = Sequence(first, second);

        //Act
        MusicIterator iterator = MusicIterator.CreateTopIterator(sequence);

        //Assert
        iterator.MusicLength.Should().Be(new Moment(new Rational(1, 2)));
    }

    [Fact]
    public void the_iterator_property_search_walks_up_the_ancestors()
    {
        //Arrange
        // A property set on enclosing music is visible to a nested iterator. This is
        // what \tag and friends rely on, and it searches iterators, not contexts.
        MusicObject inner = Music("NoteEvent", new Rational(1, 4), "event");
        MusicObject wrapper = Music("UnfoldedRepeatedMusic", new Rational(1, 4));
        wrapper.SetProperty(Sym("element"), inner);
        wrapper.SetProperty(Sym("tags"), Pair.List(Sym("partOne")));

        MusicWrapperIterator iterator = (MusicWrapperIterator)MusicIterator.CreateTopIterator(wrapper);

        //Act
        object found = iterator.GetProperty(Sym("tags"));
        object missing = iterator.GetProperty(Sym("no-such-property"));

        //Assert
        found.Should().BeOfType<Pair>();
        missing.Should().BeOfType<Nil>();
    }

    [Fact]
    public void find_above_by_music_type_reports_the_enclosing_iterator()
    {
        //Arrange
        MusicObject inner = Music("NoteEvent", new Rational(1, 4), "event");
        MusicObject wrapper = Music("UnfoldedRepeatedMusic", new Rational(1, 4), "repeated-music");
        wrapper.SetProperty(Sym("element"), inner);

        MusicIterator top = MusicIterator.CreateTopIterator(wrapper);

        //Act
        MusicIterator found = top.FindAboveByMusicType(Sym("repeated-music"));
        MusicIterator absent = top.FindAboveByMusicType(Sym("no-such-type"));

        //Assert
        found.Should().BeSameAs(top);
        absent.Should().BeNull();
    }

    [Fact]
    public void a_context_is_recognised_as_a_descendant_of_itself_and_its_ancestors()
    {
        //Arrange
        // is_child_context decides whether an iterator follows its child down into a
        // deeper context. Answering it wrongly leaves events broadcast at the wrong
        // level, which produces no error at all.
        Context score = new Context(Sym("Score"));
        Context staff = new Context(Sym("Staff"));
        Context voice = new Context(Sym("Voice"));
        Context other = new Context(Sym("Lyrics"));
        score.AddContext(staff);
        staff.AddContext(voice);

        //Act & Assert
        MusicIterator.IsChildContext(score, voice).Should().BeTrue();
        MusicIterator.IsChildContext(staff, staff).Should().BeTrue();
        MusicIterator.IsChildContext(voice, score).Should().BeFalse();
        MusicIterator.IsChildContext(score, other).Should().BeFalse();
    }

    [Fact]
    public void the_ported_iterator_constructors_and_the_worklist_together_cover_upstream()
    {
        //Arrange
        // Every ly:*-iterator::constructor upstream declares is either implemented or
        // recorded as outstanding. Nothing may fall between the two: a constructor
        // missing from both lists is a music type that would silently get a default
        // iterator and lose its meaning.
        // Read the vendored table directly rather than EnginePrimitives.All, which is
        // only populated once an interpreter has installed the stubs -- this test must
        // not depend on whether some other test built one first.
        HashSet<string> declared = new HashSet<string>();
        foreach (EntryPoint entry in EnginePrimitives.LoadEntryPoints())
        {
            if (entry.Name.EndsWith("-iterator::constructor", System.StringComparison.Ordinal))
            {
                declared.Add(entry.Name);
            }
        }

        //Act
        HashSet<string> accounted = new HashSet<string>(IteratorPrimitives.Ported);
        accounted.UnionWith(IteratorPrimitives.NotYetPorted);

        //Assert
        declared.Should().NotBeEmpty();
        accounted.Should().BeEquivalentTo(declared);
    }
}
