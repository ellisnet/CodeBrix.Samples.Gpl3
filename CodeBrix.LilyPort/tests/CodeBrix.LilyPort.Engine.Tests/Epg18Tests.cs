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
        //
        // Asserted from INSIDE the run, at the Voice's finalize: check_removal empties
        // the tree before Iterate returns, so a parent/child link cannot be inspected
        // afterwards — see Epg8TestHarness.TreeCapture.
        int ran = 0;

        //Act & Assert
        Epg8TestHarness.InspectLiveVoice(
            Epg8TestHarness.QuarterNotes(1),
            voice =>
            {
                ran++;
                Context staff = voice.Parent;
                voice.SetProperty(
                    Sym("melismaBusyProperties"), Pair.List(Sym("slurMelismaBusy")));

                // The staff has a child and no other, so its answer IS the voice's.
                staff.Children.Should().Contain(voice);
                Context.MelismaBusy(staff).Should().BeFalse();

                voice.SetProperty(Sym("slurMelismaBusy"), true);
                Context.MelismaBusy(voice).Should().BeTrue();
                Context.MelismaBusy(staff).Should().BeTrue();
            });

        // A callback that never ran would make every assertion above vacuous.
        ran.Should().Be(1);
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
        // 565 until EPG14, which added FORTY-SEVEN: its own twenty-eight, plus nineteen
        // the demand loop forced forward from EPG23 -- all eleven of skyline-scheme.cc,
        // five stencil-scheme.cc leaves, and one each from line-interface-scheme.cc,
        // item-scheme.cc and note-head-scheme.cc.
        // 612 until EPG19 (2026-08-08), which added THREE and only three: performance-
        // scheme.cc's two (ly:performance-headers, ly:performance-write) and one leaf
        // pulled forward from music-scheme.cc, ly:transpose-key-alist, which Key_performer
        // needs to emit a MIDI key signature. THIRTY .cc files landed with EPG19 and moved
        // this number by three, which is the expected shape: performers and audio elements
        // have no Scheme surface at all -- they are reached by ly/performer-init.ly naming
        // them in a \consists list, which TranslatorRegistry answers, not by a binding.
        // 615 until EPG20 (2026-08-08), which added FOURTEEN: arpeggio.cc's nine (five
        // ly:arpeggio::*, two ly:chord-bracket::*, two ly:chord-slur::*), cluster.cc's
        // three, ly:chord-name::after-line-breaking, and
        // ly:figured-bass-continuation::center-on-figures. That last file contributes ONE
        // name and not two: upstream DECLARES Figured_bass_continuation::print and never
        // defines it anywhere, so it is not an entry point -- the stencil comes from
        // Scheme's figured-bass-continuation::print, which carries no ly: prefix. Same
        // shape as the Slur::vertical_skylines declaration EPG12 recorded.
        // 629 until the CARRY-FORWARD session (2026-08-08) added THREE leaves the
        // demand loop forced forward from EPG23 -- ly:make-music-relative!,
        // ly:modules-lookup and ly:duration->moment -- taking it to 632. All three had
        // been POLITE STUBS whose placeholder answers their callers silently absorbed.
        // 632 until EPG15's close-out (2026-08-08), which added NINETEEN, taking it to
        // 651. SEVENTEEN are the group's own: Bootstrap/Epg15Callbacks.cs registers
        // TWENTY-SEVEN names and the net is seventeen, because the other ten already had
        // implementations from earlier groups and are re-registered there beside their
        // siblings. The two that matter most are ly:spanner::calc-normalized-endpoints
        // and ly:spanner::set-spacing-rods, which were the two most demanded unported
        // names anywhere in the project at 2,991 and 961 calls per sweep.
        //
        // The other TWO are grob-scheme.cc leaves the demand loop FORCED forward from
        // EPG23 -- ly:grob-pure-property and ly:grob-pure-relative-coordinate -- and that
        // file keeps its EPG23 disposition, the stencil-scheme.cc pattern. A THIRD leaf,
        // ly:grob-pure-height, came forward with them and does NOT move this number: it
        // was already implemented as an EPG8 stand-in that answered the ordinary extent,
        // so re-pointing it at Grob.PureYExtent changes what it computes and not whether
        // it exists. That distinction is why this count is not a measure of correctness.
        //
        // 651 until EPG16 (2026-08-08), which added SEVENTEEN, taking it to 668: the six
        // page-breaking strategies (ly:optimal-breaking, ly:minimal-breaking,
        // ly:page-turn-breaking, ly:one-page-breaking, ly:one-line-breaking,
        // ly:one-line-auto-height-breaking), the seven Paper_book names (ly:paper-book?
        // plus its six accessors), ly:get-spacing-spec, the two ly:book-process entry
        // points, and ONE leaf the demand loop FORCED forward -- ly:paper-get-number from
        // output-def-scheme.cc, which lily/page.scm asks for on the very first page it
        // builds and which was simply absent while that file's row read `ported'. As with
        // the grob-scheme.cc leaves above, that file keeps its own disposition.
        //
        // READ SEVENTEEN AGAINST SEVENTEEN LEDGER ROWS AS A COINCIDENCE, not a pattern:
        // eleven of EPG16's rows contribute NO Scheme surface at all (the strategies'
        // .cc files, the two engravers, page-spacing, page-spacing-result,
        // page-layout-problem), and the bindings come from the four *-scheme.cc files
        // and book-scheme.cc instead.
        //
        // 668 until EPG16's CLOSE-OUT (2026-08-09), which added FIVE more, taking it to
        // 673 — and every one of the five was already in the entry-point table, answering
        // the inert UnportedValue, while its FILE's ledger row read `ported'. That is the
        // shape worth reading, not the number: context-def.cc's ly:context-def-modify and
        // ly:context-def-lookup, and system.cc's ly:system::get-staves,
        // ly:system::get-spaceable-staves and ly:system::get-nonspaceable-staves. None of
        // them is new code — Context_def and System were ported whole, and each binding is
        // a one-line call onto a method that already existed. What was missing was the
        // BINDING, and PORT-COVERAGE had recorded the risk in as many words: those stubs
        // were "one Scheme call away from being actively wrong". They were. A toplevel
        // \layout block destroyed its own context definitions and `annotate-spacing = ##t'
        // killed the book.
        //
        // ⚠ THIS COUNT MEASURES SURFACE, NEVER CORRECTNESS, and these five are the
        // sharpest illustration the project has: for as long as they sat unregistered,
        // every ledger row involved said `ported' and every C# caller worked.
        //
        // 678 after EPG21 (2026-08-09), which added the ancient-notation group's whole
        // Scheme surface: ly:kievan-ligature::print, ly:mensural-ligature::print and
        // ::brew-ligature-primitive, ly:vaticana-ligature::print and
        // ::brew-ligature-primitive. Three of those five ANSWER '() -- the ligature
        // spanners draw nothing themselves -- which is precisely why they had to be
        // registered rather than left as stubs: the stub answers the inert UnportedValue,
        // and an UnportedValue in a `stencil' property is TRUTHY where '() is skipped.
        // The other twelve ledger rows the group closed carry no Scheme surface at all;
        // they are translators, which ly/engraver-init.ly reaches by NAME through
        // TranslatorRegistry and which this number cannot see.
        //
        // 679 after the fine-vertical-geometry session (2026-08-12) registered
        // ly:set-middle-C! -- an even sharper illustration of surface-not-correctness
        // than the five above: pitch.cc's set_middle_C had been faithfully ported since
        // EPG14 and the ottava engraver called it correctly, while the Scheme binding
        // parser-clef.scm applies after EVERY \clef stayed a stub. middleCPosition never
        // left the treble context default, so every clef change in the whole port was a
        // silent note-placement no-op.
        //
        // 737 -- ALL of them -- after EPG23 (2026-08-12), which closed gate G3: 36 real
        // implementations plus 17 D25 N/A bindings, and an N/A binding counts as
        // implemented here because it IS a real binding that raises with its reason. The
        // number cannot rise again; from here it can only FALL, and a fall means a
        // registration was lost.
        closure.Implemented.Count.Should().Be(737);
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
