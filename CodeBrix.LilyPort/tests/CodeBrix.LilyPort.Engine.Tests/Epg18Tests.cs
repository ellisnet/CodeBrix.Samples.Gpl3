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
/// EPG18 — lyrics and melody. The ten files of the group, and the three free functions its
/// callers demanded that already-ported files had never carried.
/// <para>
/// The arithmetic-only halves are fenced here with hand-computed expectations, because
/// they are the parts a sweep cannot tell apart from "nothing drawn": a wrong melody
/// direction is a stem pointing the wrong way, not a missing glyph.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class Epg18Tests
{
    // Process-global engine state: one interpreter, under a lock, shared by the facts
    // here that need the Scheme layer -- standing rule 8.
    private static readonly object Gate = new object();

    private static EntryPointClosure _closure;

    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static EntryPointClosure Measure()
    {
        lock (Gate)
        {
            if (_closure == null)
            {
                EntryPointClosure measured = null;

                // psyntax recurses hard enough to overflow the default stack.
                CodeBrix.LilyScheme.Interpreter.RunWithLargeStack(() =>
                {
                    CodeBrix.LilyScheme.Interpreter interpreter
                        = LilyPondScheme.CreateInterpreter();

                    measured = EntryPointClosure.Measure(interpreter);
                });

                _closure = measured;
            }

            return _closure;
        }
    }

    // ----- G5: the iterator constructor closure -----

    [Fact]
    public void the_lyric_combine_constructor_is_implemented_and_g5_is_closed()
    {
        //Arrange & Act
        IReadOnlyCollection<string> ported = IteratorPrimitives.Ported;

        //Assert
        // The single name that stood between EPG22's board row and its exit criterion.
        ported.Should().Contain("ly:lyric-combine-music-iterator::constructor");
        IteratorPrimitives.NotYetPorted.Should().BeEmpty();
        ported.Count.Should().Be(28);
    }

    [Fact]
    public void the_lyric_combine_iterator_reports_its_upstream_class_name()
    {
        //Arrange & Act
        string name = new LyricCombineMusicIterator().ClassName;

        //Assert
        name.Should().Be("Lyric_combine_music_iterator");
    }

    [Fact]
    public void the_lyric_combine_iterator_runs_always_only_while_it_has_lyrics_left()
    {
        //Arrange
        // run_always () is what makes this iterator work at all: its OWN pending moment
        // says nothing about when the next syllable is due, because the melody decides.
        // With no child iterator there is nothing to advance, so it must NOT run.
        LyricCombineMusicIterator iterator = new LyricCombineMusicIterator();

        //Act
        bool runsAlways = iterator.RunAlways;

        //Assert
        runsAlways.Should().BeFalse();
        iterator.Ok.Should().BeFalse();
    }

    // ----- melody-spanner.cc: the direction interpolation -----

    [Fact]
    public void a_neutral_run_between_two_agreeing_stems_follows_them()
    {
        //Arrange
        // down _ _ down  ->  the two middle stems take DOWN.
        MelodyFixture f = new MelodyFixture(-1, 0, 0, -1);

        //Act
        long[] answers = f.AnswerForEachStem();

        //Assert
        // Only the NEUTRAL stems are answered about; the non-neutral ones are outside
        // every run, so the callback returns '() for them.
        answers[1].Should().Be(-1);
        answers[2].Should().Be(-1);
    }

    [Fact]
    public void a_neutral_run_between_two_disagreeing_stems_takes_the_neutral_direction()
    {
        //Arrange
        // down _ up  ->  the middle stem cannot follow both, so neutral-direction wins.
        MelodyFixture f = new MelodyFixture(-1, 0, 1);
        f.NeutralDirection = 1;

        //Act
        long[] answers = f.AnswerForEachStem();

        //Assert
        answers[1].Should().Be(1);
    }

    [Fact]
    public void a_neutral_run_with_only_one_neighbour_follows_that_neighbour()
    {
        //Arrange
        // _ _ up  ->  nothing on the left, so the run follows the stem on its right.
        MelodyFixture f = new MelodyFixture(0, 0, 1);
        f.NeutralDirection = -1;

        //Act
        long[] answers = f.AnswerForEachStem();

        //Assert
        // neutral-direction is DOWN here and is deliberately NOT what comes back: an
        // implementation that lost the one-sided case would answer -1 and look plausible.
        answers[0].Should().Be(1);
        answers[1].Should().Be(1);
    }

    [Fact]
    public void an_all_neutral_run_takes_the_neutral_direction()
    {
        //Arrange
        MelodyFixture f = new MelodyFixture(0, 0, 0);
        f.NeutralDirection = 1;

        //Act
        long[] answers = f.AnswerForEachStem();

        //Assert
        answers[0].Should().Be(1);
        answers[1].Should().Be(1);
        answers[2].Should().Be(1);
    }

    [Fact]
    public void a_stem_with_no_melody_spanner_points_down()
    {
        //Arrange
        Item stem = new Item(BasicProperties("Stem"));

        //Act
        object answer = MelodySpanner.CalcNeutralStemDirection(stem);

        //Assert
        // Upstream returns DOWN rather than '() here, so a stem that never reached a
        // Melody_engraver still gets a direction instead of an unset property.
        answer.Should().Be(-1L);
    }

    [Fact]
    public void adding_a_stem_links_it_both_ways_and_defers_its_direction()
    {
        //Arrange
        Item melody = new Item(BasicProperties("MelodyItem"));
        Item stem = new Item(BasicProperties("Stem"));

        //Act
        MelodySpanner.AddStem(melody, stem);

        //Assert
        // Three things at once, and all three are load-bearing: the span collects the
        // stem, the stem can find the span back (which is how the callback reaches it),
        // and neutral-direction becomes the CALLBACK rather than a value — the whole run
        // has to be known before any of its members can be answered.
        PointerGroupInterface.ExtractGrobSet(melody, Sym("stems")).Should().HaveCount(1);
        stem.GetObject(Sym("melody-spanner")).Should().BeSameAs(melody);
        stem.GetPropertyData(Sym("neutral-direction")).Should().NotBeNull();
    }

    // ----- context.cc's melisma_busy, never carried until now -----

    [Fact]
    public void a_leaf_context_is_melisma_busy_when_one_of_its_named_properties_is_set()
    {
        //Arrange
        Context voice = RealVoiceContext();
        voice.SetProperty(
            Sym("melismaBusyProperties"), Pair.List(Sym("slurMelismaBusy"), Sym("tieMelismaBusy")));

        //Act & Assert
        Context.MelismaBusy(voice).Should().BeFalse();

        voice.SetProperty(Sym("tieMelismaBusy"), true);
        Context.MelismaBusy(voice).Should().BeTrue();
    }

    [Fact]
    public void a_property_that_is_merely_non_false_does_not_make_a_melisma()
    {
        //Arrange
        // from_scm<bool> is #t-only, not Scheme truthiness. A symbol or a number here
        // must NOT read as busy, or every lyric in the score holds forever.
        Context voice = RealVoiceContext();
        voice.SetProperty(Sym("melismaBusyProperties"), Pair.List(Sym("slurMelismaBusy")));
        voice.SetProperty(Sym("slurMelismaBusy"), Sym("yes"));

        //Act & Assert
        Context.MelismaBusy(voice).Should().BeFalse();
    }

    [Fact]
    public void a_context_with_children_is_melisma_busy_only_when_all_of_them_are()
    {
        //Arrange
        // The rule that makes divided staves work: a context with children DELEGATES to
        // them and every one must be busy, so a melisma in one voice cannot hold the
        // lyrics of the whole staff.
        //
        // The properties go on the VOICE, not the staff. Context properties are inherited
        // down the tree — that is how \set Staff.something reaches a voice — so setting a
        // busy flag on the staff would ALSO make the child read it, and the test would
        // pass whether or not delegation existed.
        Context voice = RealVoiceContext();
        Context staff = voice.Parent;

        voice.SetProperty(Sym("melismaBusyProperties"), Pair.List(Sym("slurMelismaBusy")));

        //Act & Assert
        // The staff has a child and no other, so its answer IS the voice's answer.
        staff.Children.Should().Contain(voice);
        Context.MelismaBusy(staff).Should().BeFalse();

        voice.SetProperty(Sym("slurMelismaBusy"), true);
        Context.MelismaBusy(voice).Should().BeTrue();
        Context.MelismaBusy(staff).Should().BeTrue();
    }

    // ----- item.cc's spanned_time_interval, never carried until now -----

    [Fact]
    public void a_missing_end_collapses_the_spanned_time_onto_the_other_end()
    {
        //Arrange
        PaperColumn column = new PaperColumn(BasicProperties("PaperColumn"));
        column.SetProperty(Sym("when"), new Moment(new Rational(3, 4)));

        Item left = new Item(BasicProperties("Item"));
        left.XParent = column;

        //Act
        MomentInterval iv = Item.SpannedTimeInterval(left, null);

        //Assert
        // Both ends read 3/4: a zero-length span, not the empty [+inf, -inf] a
        // default-constructed interval would leave behind. Vowel_transition tests that
        // length against zero to decide whether to reserve space at all, so an empty
        // interval there would silently reserve space for every broken transition.
        iv.Left.Should().Be(new Moment(new Rational(3, 4)));
        iv.Right.Should().Be(new Moment(new Rational(3, 4)));
    }

    // ----- the entry points and translators the group owed -----

    [Fact]
    public void every_epg18_entry_point_is_implemented()
    {
        //Arrange
        string[] owed =
        {
            "ly:lyric-combine-music-iterator::constructor",
            "ly:lyric-combine-music::length-callback",
            "ly:lyric-extender::print",
            "ly:lyric-hyphen::print",
            "ly:lyric-hyphen::set-spacing-rods",
            "ly:melody-spanner::calc-neutral-stem-direction",
            "ly:vowel-transition::set-spacing-rods",

            // grob-pq-engraver.cc's one LY_DEFINE, pulled forward from EPG10 with its
            // engraver: whoever ports a type owes its binding surface the same session.
            "ly:grob-pq<?",

            // bezier-scheme.cc's second binding, pulled forward from EPG23 under the
            // same rule. Its first, ly:bezier-extract, was ALREADY implemented in
            // GeneralPrimitives.cs without the ledger row being flipped -- which is why
            // this file completes that row rather than porting it whole, and why the
            // closure moves by nine rather than ten.
            "ly:bezier-extent",
            "ly:bezier-extract",
        };

        //Act
        EntryPointClosure closure = Measure();
        List<string> implemented = new List<string>();
        foreach (EntryPoint entry in closure.Implemented)
        {
            implemented.Add(entry.Name);
        }

        //Assert
        foreach (string name in owed)
        {
            implemented.Should().Contain(name);
        }

        // NINE of the ten were stubbed before this session -- ly:bezier-extract was
        // already implemented -- so the closure moves by nine: 526 after EPG17's
        // remainder, 535 now. Asserted exactly rather than as a
        // ratchet, because this is the number the plan document quotes and a drifting
        // figure there is worse than a failing test here.
        //
        // EPG10 then added the thirteen ly:beam::* callbacks, taking it to 548. This
        // assertion is EPG18's own and is re-stated rather than loosened, for the same
        // reason it was exact in the first place.
        // EPG11 and EPG12 then added SEVENTEEN on 2026-08-08 -- the eight tie-family
        // callbacks and the nine ly:slur::* names -- taking it to 565. Three of the nine
        // are never named from Scheme: the outside-slur trio is chained onto a dodging
        // grob from C++, BY NAME, so an unregistered name would chain a stub.
        closure.Implemented.Count.Should().Be(565);
    }

    [Fact]
    public void every_epg18_translator_resolves_to_a_creator()
    {
        //Arrange
        string[] names =
        {
            "Lyric_engraver",
            "Extender_engraver",
            "Hyphen_engraver",
            "Melody_engraver",
        };

        //Act
        Epg8TestHarness.Loaded();

        //Assert
        // Registered is not the same as reachable, but UNregistered is definitely not
        // reachable: before this, ly/engraver-init.ly's Lyrics and Voice definitions
        // named these four and each one warned "unknown translator" once per file.
        foreach (string name in names)
        {
            LilyPondScheme.Registries.Translators
                .ContainsKey(Sym(name)).Should().BeTrue(name + " is not registered");
        }
    }

    private static object BasicProperties(string name)
        => Pair.List(new Pair(Sym("meta"), Pair.List(new Pair(Sym("name"), Sym(name)))));

    /// <summary>
    /// Builds the shared Global→Score→Staff→Voice tree and returns its Voice, so the
    /// context-level facts run against real contexts rather than a hand-made stand-in.
    /// </summary>
    private static Context RealVoiceContext()
    {
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            null, null, null, null);

        Epg8TestHarness.Iterate(tree, Epg8TestHarness.QuarterNotes(1));
        return tree.FindContext("Voice");
    }

    /// <summary>
    /// A melody span with a fixed run of default-directions, for asking the interpolator
    /// what each stem should do.
    /// </summary>
    private sealed class MelodyFixture
    {
        private readonly Item _melody;
        private readonly List<Item> _stems = new List<Item>();

        internal MelodyFixture(params int[] defaultDirections)
        {
            _melody = new Item(BasicProperties("MelodyItem"));
            foreach (int direction in defaultDirections)
            {
                Item stem = new Item(BasicProperties("Stem"));
                stem.SetProperty(Sym("default-direction"), (long)direction);
                PointerGroupInterface.AddGrob(_melody, Sym("stems"), stem);
                stem.SetObject(Sym("melody-spanner"), _melody);
                _stems.Add(stem);
            }
        }

        internal int NeutralDirection
        {
            set => _melody.SetProperty(Sym("neutral-direction"), (long)value);
        }

        internal long[] AnswerForEachStem()
        {
            long[] answers = new long[_stems.Count];
            for (int i = 0; i < _stems.Count; i++)
            {
                // Ask fresh each time: the callback also WRITES the answer onto the other
                // stems of the run, and reading a written value instead of a computed one
                // would make this test agree with itself rather than with upstream.
                object answer = MelodySpanner.CalcNeutralStemDirection(_stems[i]);
                answers[i] = answer is long value ? value : long.MinValue;
            }

            return answers;
        }
    }
}
