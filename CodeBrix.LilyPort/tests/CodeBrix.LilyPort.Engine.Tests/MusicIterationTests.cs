// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The translation pipeline running end to end on REAL LilyPond music: music built by
/// <c>make-music</c> out of the vendored Scheme layer, walked by the real iterators
/// through the real <c>iterator-ctor</c> procedures, broadcast as real stream events,
/// and heard by engravers in a context tree.
/// <para>
/// This is the test that matters for Track T. The fixture-only tests in
/// <see cref="IteratorTests"/> pin individual iterators, but only this one proves the
/// pieces connect: that <c>define-music-types.scm</c>'s <c>iterator-ctor</c> resolves
/// to a ported constructor, that a music name becomes an event class through
/// <c>ly:make-event-class</c>, and that an engraver listening for
/// <c>note-event</c> actually hears a note written as <c>NoteEvent</c>.
/// </para>
/// <para>
/// It shares the process-global engine state with the load fences, so it lives in the
/// same collection and loads the Scheme layer once for the whole class.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class MusicIterationTests : IDisposable
{
    /// <summary>
    /// Removes the fixture's own translator from the engine registry after every test.
    /// The registry is process-global — the engine's state is, throughout — so leaving
    /// <c>Listening_engraver</c> in it would let this class's fixture leak into any
    /// other test that resolves a <c>\consists</c> list.
    /// </summary>
    public void Dispose() => LilyPondScheme.Registries?.Translators.Remove(Sym(ListeningEngraverName));

    private static readonly object LoadGate = new object();

    private static Interpreter _interpreter;

    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static Interpreter Loaded()
    {
        lock (LoadGate)
        {
            if (_interpreter == null || !ReferenceEquals(LilyPondScheme.Current, _interpreter))
            {
                Interpreter interpreter = null;
                Interpreter.RunWithLargeStack(() =>
                {
                    interpreter = LilyPondScheme.CreateInterpreter();
                    LilyPondScheme.LoadViaLilyScm(interpreter);
                });

                _interpreter = interpreter;
            }

            return _interpreter;
        }
    }

    private static object Eval(string source)
    {
        Interpreter interpreter = Loaded();
        object result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            foreach (object form in SchemeReader.ReadAll(source, "<test>"))
            {
                result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }
        });

        return result;
    }

    /// <summary>An engraver that records every event class it hears, in order.</summary>
    private sealed class ListeningEngraver : Engraver
    {
        public ListeningEngraver(Context context)
            : base(context)
        {
        }

        public override string ClassName => "Listening_engraver";

        public List<StreamEvent> Heard { get; } = new List<StreamEvent>();

        /// <summary>The moment each event was heard at, read off the context clock.</summary>
        public List<Moment> Moments { get; } = new List<Moment>();

        public override void ConnectToContext()
        {
            base.ConnectToContext();
            ListenTo("note-event", Record);
            ListenTo("rest-event", Record);
        }

        private void Record(StreamEvent streamEvent)
        {
            Heard.Add(streamEvent);

            // NowMoment walks to the top context, which is where the single clock
            // lives -- a Voice does not keep one of its own.
            Moments.Add(NowMoment);
        }
    }

    /// <summary>
    /// Builds the context tree the way the real pipeline does: a Global context built
    /// from a <c>Context_def</c>, with everything below created on demand from further
    /// definitions as the iterators descend.
    /// <para>
    /// Pre-building Score/Staff/Voice by hand does not work, and the reason is worth
    /// keeping. <c>get_default_interpreter</c> is CREATE_ONLY — it goes straight to
    /// creating a fresh hierarchy without looking for an existing one, and upstream's
    /// own comment remarks on how surprising that is. So a hand-built tree is simply
    /// bypassed: the events go to a second, freshly created Voice and the engraver
    /// attached to the first one hears nothing at all, with no error anywhere.
    /// </para>
    /// <para>
    /// The definitions here are the same SHAPE as <c>ly/engraver-init.ly</c>'s and a
    /// great deal smaller: four contexts and one engraver, so that what this class
    /// tests stays the iterator/event path rather than the init layer. They go through
    /// the real <c>Context_def</c>, the real acceptance sets and the real
    /// <c>\consists</c> resolution.
    /// </para>
    /// </summary>
    private sealed class Tree
    {
        public GlobalContext Global { get; set; }

        public List<ListeningEngraver> Engravers { get; } = new List<ListeningEngraver>();

        public ListeningEngraver Engraver
            => Engravers.Count > 0 ? Engravers[Engravers.Count - 1] : null;

        /// <summary>
        /// Every context announced below Global, in announcement order — which is
        /// top down, because a parent is announced before the child it creates.
        /// <para>
        /// Recorded rather than read off the live tree, because the live tree is EMPTY
        /// once interpretation finishes: Context::check_removal removes anything with no
        /// children and no clients, which after the iterators quit is everything.
        /// Upstream does the same. Reading the tree afterwards would make
        /// <see cref="CreatedNames"/> answer empty for a run that built the whole chain,
        /// so the negative test below would pass vacuously instead of proving anything.
        /// </para>
        /// </summary>
        public List<Context> Announced { get; } = new List<Context>();

        public Context Voice
            => FindByName(Global, "Voice")
               ?? Announced.Find(context => context.ContextName == "Voice");

        /// <summary>The context names created below Global, top down.</summary>
        public List<string> CreatedNames
            => Announced.ConvertAll(context => context.ContextName);

        private static Context FindByName(Context context, string name)
        {
            if (context == null)
            {
                return null;
            }

            if (context.ContextName == name)
            {
                return context;
            }

            foreach (Context child in context.Children)
            {
                Context found = FindByName(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    private const string ListeningEngraverName = "Listening_engraver";

    /// <summary>Builds a context definition out of <c>(tag argument)</c> mods.</summary>
    private static ContextDef Def(string name, params (string Tag, object Argument)[] mods)
    {
        ContextDef definition = new ContextDef();
        definition.AddContextMod(Pair.List(Sym("context-name"), Sym(name)));
        foreach ((string Tag, object Argument) mod in mods)
        {
            definition.AddContextMod(Pair.List(Sym(mod.Tag), mod.Argument));
        }

        return definition;
    }

    private static Tree BuildTree()
    {
        Loaded();

        Tree tree = new Tree();

        // The fixture's engraver announces itself the same way every C# engraver does,
        // so a \consists naming it resolves through the ordinary registry.
        LilyPondScheme.Registries.Translators[Sym(ListeningEngraverName)] =
            new TranslatorCreator(
                Sym(ListeningEngraverName),
                context =>
                {
                    ListeningEngraver engraver = new ListeningEngraver(context);
                    tree.Engravers.Add(engraver);
                    return engraver;
                });

        ContextDef globalDef = Def(
            "Global", ("accepts", Sym("Score")), ("default-child", Sym("Score")));
        ContextDef scoreDef = Def(
            "Score",
            ("translator-type", Sym("Engraver_group")),
            ("accepts", Sym("Staff")),
            ("default-child", Sym("Staff")));
        ContextDef staffDef = Def(
            "Staff",
            ("translator-type", Sym("Engraver_group")),
            ("accepts", Sym("Voice")),
            ("default-child", Sym("Voice")));
        ContextDef voiceDef = Def(
            "Voice",
            ("translator-type", Sym("Engraver_group")),
            ("consists", Sym(ListeningEngraverName)));

        OutputDef layout = new OutputDef();
        foreach (ContextDef definition in new[] { globalDef, scoreDef, staffDef, voiceDef })
        {
            layout.SetVariable((Symbol)definition.ContextName, definition);
        }

        GlobalContext global = new GlobalContext(layout, globalDef);

        // Without this the tree still builds and nothing engraves: the group's
        // AnnounceNewContext listener is what makes a child's translators.
        global.MakeGlobalTranslator();

        tree.Global = global;

        // Recorded as they are announced; see Tree.Announced for why the live tree
        // cannot be read after the run.
        global.EventsBelow.AddListener(
            tree,
            streamEvent =>
            {
                if (streamEvent.GetProperty(Sym("context")) is Context announced)
                {
                    tree.Announced.Add(announced);
                }
            },
            Sym("AnnounceNewContext"));

        return tree;
    }

    [Fact]
    public void a_note_event_built_in_scheme_becomes_a_stream_event_with_its_ancestry()
    {
        //Arrange
        // ly:make-event-class expands a leaf class into the whole ancestor chain, which
        // is why an engraver can listen for rhythmic-event and hear a note. If ToEvent
        // stored only the leaf, every general-purpose engraver would go deaf.
        Eval("(define test-note (make-music 'NoteEvent 'duration (ly:make-duration 2)))");
        MusicObject note = (MusicObject)Eval("test-note");

        //Act
        StreamEvent streamEvent = null;
        Interpreter.RunWithLargeStack(() => streamEvent = note.ToEvent());

        //Assert
        streamEvent.Should().NotBeNull();
        streamEvent.IsInEventClass("note-event").Should().BeTrue();
        streamEvent.IsInEventClass("rhythmic-event").Should().BeTrue();
        streamEvent.IsInEventClass("music-event").Should().BeTrue();
        streamEvent.IsInEventClass("no-such-event").Should().BeFalse();
    }

    [Fact]
    public void the_event_carries_the_music_that_caused_it_and_its_length()
    {
        //Arrange
        Eval("(define test-note-2 (make-music 'NoteEvent 'duration (ly:make-duration 2)))");
        MusicObject note = (MusicObject)Eval("test-note-2");

        //Act
        StreamEvent streamEvent = null;
        Interpreter.RunWithLargeStack(() => streamEvent = note.ToEvent());

        //Assert
        streamEvent.GetProperty("music-cause").Should().BeSameAs(note);
        streamEvent.GetProperty("length").Should().Be(new Moment(new Rational(1, 4)));
    }

    [Fact]
    public void real_music_types_resolve_to_the_ported_iterator_constructors()
    {
        //Arrange
        // define-music-types.scm names a constructor per type; IteratorPrimitives
        // registers the ported ones. This is the seam between the Scheme layer and the
        // iterator family, and it is invisible when it breaks -- an unregistered
        // constructor silently falls back to a default iterator.
        Eval("(define test-seq (make-music 'SequentialMusic 'elements (list)))");
        Eval("(define test-sim (make-music 'SimultaneousMusic 'elements (list)))");
        Eval("(define test-chord (make-music 'EventChord 'elements (list)))");
        Eval("(define test-note-3 (make-music 'NoteEvent 'duration (ly:make-duration 2)))");

        //Act
        MusicIterator sequential = null;
        MusicIterator simultaneous = null;
        MusicIterator chord = null;
        MusicIterator note = null;
        Interpreter.RunWithLargeStack(() =>
        {
            sequential = MusicIterator.CreateTopIterator((MusicObject)Eval("test-seq"));
            simultaneous = MusicIterator.CreateTopIterator((MusicObject)Eval("test-sim"));
            chord = MusicIterator.CreateTopIterator((MusicObject)Eval("test-chord"));
            note = MusicIterator.CreateTopIterator((MusicObject)Eval("test-note-3"));
        });

        //Assert
        sequential.Should().BeOfType<SequentialIterator>();
        simultaneous.Should().BeOfType<SimultaneousMusicIterator>();
        chord.Should().BeOfType<EventChordIterator>();
        note.Should().BeOfType<RhythmicMusicIterator>();
    }

    [Fact]
    public void iterating_a_sequence_of_notes_broadcasts_them_in_order()
    {
        //Arrange
        // The whole point of Track T: a music tree goes in, a stream of events comes
        // out at the right contexts in the right order.
        Eval(@"(define test-tune
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 0 0))
                                   (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 1 0)))))");
        MusicObject tune = (MusicObject)Eval("test-tune");
        Tree tree = BuildTree();

        //Act
        bool found = false;
        Interpreter.RunWithLargeStack(() => found = tree.Global.Iterate(tune));

        //Assert
        found.Should().BeTrue();
        tree.Engraver.Heard.Count.Should().Be(2);
        tree.Engraver.Heard[0].IsInEventClass("note-event").Should().BeTrue();

        Pitch first = tree.Engraver.Heard[0].GetProperty("pitch") as Pitch;
        Pitch second = tree.Engraver.Heard[1].GetProperty("pitch") as Pitch;
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first.NoteName.Should().Be(0);
        second.NoteName.Should().Be(1);
    }

    [Fact]
    public void the_sequence_advances_the_clock_one_element_at_a_time()
    {
        //Arrange
        // Two quarter notes must be heard at 0 and 1/4, not both at zero. An iterator
        // that reported everything at once would still produce the right events and
        // completely wrong music.
        Eval(@"(define test-timed
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent 'duration (ly:make-duration 2))
                                   (make-music 'NoteEvent 'duration (ly:make-duration 2)))))");
        MusicObject tune = (MusicObject)Eval("test-timed");
        Tree tree = BuildTree();

        //Act
        Interpreter.RunWithLargeStack(() => tree.Global.Iterate(tune));

        //Assert
        tree.Engraver.Moments.Should().Equal(
            Moment.Zero,
            new Moment(new Rational(1, 4)));
    }

    [Fact]
    public void an_event_chord_reports_every_note_at_the_same_moment()
    {
        //Arrange
        Eval(@"(define test-chord-2
                 (make-music 'EventChord
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 0 0))
                                   (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 2 0)))))");
        MusicObject chord = (MusicObject)Eval("test-chord-2");
        Tree tree = BuildTree();

        //Act
        Interpreter.RunWithLargeStack(() => tree.Global.Iterate(chord));

        //Assert
        tree.Engraver.Heard.Count.Should().Be(2);
        tree.Engraver.Moments.Should().Equal(Moment.Zero, Moment.Zero);
    }

    [Fact]
    public void iterating_empty_music_finds_nothing_and_runs_no_timesteps()
    {
        //Arrange
        // Zero-length music must not start the loop. Upstream guards on
        // length && ok, and without the guard the loop's first iteration would
        // initialise contexts for music that does not exist.
        Eval("(define test-empty (make-music 'SequentialMusic 'elements (list)))");
        MusicObject empty = (MusicObject)Eval("test-empty");
        Tree tree = BuildTree();

        //Act
        bool found = true;
        Interpreter.RunWithLargeStack(() => found = tree.Global.Iterate(empty));

        //Assert
        // Nothing ran at all: no timestep, and therefore not even a Score context.
        found.Should().BeFalse();
        tree.CreatedNames.Should().BeEmpty();
        tree.Engraver.Should().BeNull();
    }

    [Fact]
    public void a_note_reaches_a_voice_created_on_demand_below_the_staff()
    {
        //Arrange
        // descend_to_bottom_context: music addressed at a Staff has to reach a Voice,
        // and one is created if none exists. Without it the event is broadcast at the
        // Staff, where no note engraver is listening -- and nothing complains.
        Eval("(define test-descend (make-music 'NoteEvent 'duration (ly:make-duration 2)))");
        MusicObject note = (MusicObject)Eval("test-descend");
        Tree tree = BuildTree();

        //Act
        Interpreter.RunWithLargeStack(() => tree.Global.Iterate(note));

        //Assert
        // The whole chain gets built from Global downward, and the note is heard in the
        // Voice at the bottom of it.
        tree.CreatedNames.Should().Equal("Score", "Staff", "Voice");
        tree.Voice.Should().NotBeNull();
        tree.Engraver.Heard.Should().ContainSingle();
        tree.Engraver.Heard[0].IsInEventClass("note-event").Should().BeTrue();
    }
}
