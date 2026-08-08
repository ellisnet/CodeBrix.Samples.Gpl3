// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG17's first slice: the four iterator constructors whose behaviour is entirely their
/// own, plus <c>grace-music.cc</c>'s start callback.
/// <para>
/// The five that remain — volta-repeat, alternative-sequence, volta-specced,
/// percent-repeat and tuplet — are held back deliberately: three of them need
/// <c>Repeat_styler</c> and the other two read state off
/// <c>Alternative_sequence_iterator</c>, so landing them piecemeal would mean writing
/// stand-ins for exactly the machinery the group exists to port.
/// </para>
/// </summary>
public class Epg17IteratorTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static MusicObject Wrapper(Moment elementLength)
    {
        object leafImmutable = Pair.List(
            new Pair(Sym("name"), Sym("NoteEvent")),
            new Pair(Sym("types"), Pair.List(Sym("NoteEvent"), Sym("event"))));

        MusicObject element = new MusicObject(leafImmutable);
        element.SetProperty(Sym("length"), elementLength);

        object immutable = Pair.List(
            new Pair(Sym("name"), Sym("GraceMusic")),
            new Pair(Sym("types"), Pair.List(Sym("GraceMusic"), Sym("music-wrapper-music"))));

        MusicObject music = new MusicObject(immutable);
        music.SetProperty(Sym("element"), element);
        return music;
    }

    [Fact]
    public void the_four_epg17_slice_constructors_are_implemented()
    {
        //Arrange
        string[] landed =
        {
            "ly:fine-iterator::constructor",
            "ly:grace-iterator::constructor",
            "ly:measure-remainder-iterator::constructor",
            "ly:premeasure-iterator::constructor",
        };

        //Act
        IReadOnlyCollection<string> ported = IteratorPrimitives.Ported;

        //Assert
        foreach (string name in landed)
        {
            ported.Should().Contain(name);
            IteratorPrimitives.NotYetPorted.Should().NotContain(name);
        }
    }

    [Fact]
    public void the_iterators_report_their_upstream_class_names()
    {
        //Arrange & Act
        string[] names =
        {
            new FineIterator().ClassName,
            new GraceIterator().ClassName,
            new MeasureRemainderIterator().ClassName,
            new PremeasureIterator().ClassName,
        };

        //Assert
        // The class name is what a diagnostic and ly:iterator? report, so a wrong one is
        // a silently misleading message rather than a failure.
        names.Should().BeEquivalentTo(new[]
        {
            "Fine_iterator",
            "Grace_iterator",
            "Measure_remainder_iterator",
            "Premeasure_iterator",
        });
    }

    [Fact]
    public void grace_music_starts_a_whole_length_before_where_it_is_written()
    {
        //Arrange
        // \grace { c8 } hangs off the moment BEFORE the note it decorates, so its start
        // is negative -- and it is negative in the GRACE part, not the main part, which
        // is the whole reason a Moment carries two rationals.
        MusicObject grace = Wrapper(new Moment(new Rational(1, 8)));

        //Act
        Moment start = GraceMusic.StartCallback(grace);

        //Assert
        start.MainPart.Should().Be(Rational.Zero);
        start.GracePart.Should().Be(-new Rational(1, 8));
    }

    [Fact]
    public void grace_music_of_zero_length_starts_where_it_is_written()
    {
        //Arrange
        MusicObject grace = Wrapper(new Moment(Rational.Zero));

        //Act
        Moment start = GraceMusic.StartCallback(grace);

        //Assert
        start.IsNonZero.Should().BeFalse();
    }

    [Fact]
    public void the_grace_music_start_callback_is_a_registered_entry_point()
    {
        //Arrange & Act
        // Rule 3, the bindings rule: whoever ports a type owes its LY_DEFINE surface in
        // the same session. Registration is what scm/define-music-types.scm reaches, so
        // an unregistered callback leaves every \grace expression reporting a zero start
        // with no diagnostic at all.
        bool declared = false;
        foreach (EntryPoint entry in EnginePrimitives.LoadEntryPoints())
        {
            if (entry.Name == "ly:grace-music::start-callback")
            {
                declared = true;
                break;
            }
        }

        //Assert
        declared.Should().BeTrue();
    }
}
