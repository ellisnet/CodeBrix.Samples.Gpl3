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
/// EPG5's engravers running REACHABLY: real music through the real iterators into a
/// context tree whose <c>\consists</c> lists resolve the new translators out of the
/// ordinary registry, with the real grob definitions from the vendored Scheme layer.
/// <para>
/// Registered is not behaving and behaving is not reachable — the Track T sessions
/// recorded that lesson repeatedly, so every engraver here is exercised through the
/// pipeline rather than by direct calls wherever the pipeline can carry it. The two
/// direct-drive tests at the end cover what the pipeline cannot reach yet: two
/// simultaneous voices need context-specced music (iterator unported), and the
/// rest-collision chain needs the <c>busyGrobs</c> queue (Grob_pq_engraver, EPG10).
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class Epg5EngraverTests : IDisposable
{
    private const string RecorderName = "Epg5_recorder_engraver";

    private static readonly object LoadGate = new object();

    private static Interpreter _interpreter;

    /// <summary>Removes the fixture's recorder from the process-global registry.</summary>
    public void Dispose() => LilyPondScheme.Registries?.Translators.Remove(Sym(RecorderName));

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
            foreach (object form in SchemeReader.ReadAll(source, "<epg5-test>"))
            {
                result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }
        });

        return result;
    }

    /// <summary>Records every grob announced in its context or below, in order.</summary>
    private sealed class GrobRecorder : Engraver
    {
        public GrobRecorder(Context context)
            : base(context)
        {
        }

        public override string ClassName => RecorderName;

        public List<Grob> Made { get; } = new List<Grob>();

        public override void AcknowledgeGrob(GrobInfo info) => Made.Add(info.Grob);

        public List<Grob> Named(string name)
        {
            List<Grob> found = new List<Grob>();
            foreach (Grob grob in Made)
            {
                if (grob.Name == name)
                {
                    found.Add(grob);
                }
            }

            return found;
        }
    }

    private sealed class Tree
    {
        public GlobalContext Global { get; set; }

        public List<GrobRecorder> Recorders { get; } = new List<GrobRecorder>();

        public GrobRecorder Recorder
            => Recorders.Count > 0 ? Recorders[Recorders.Count - 1] : null;
    }

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

    /// <summary>
    /// Builds Global → Score → Staff → Voice from context definitions, with the given
    /// translators consisted into the Staff and the Voice, the recorder consisted into
    /// the Staff so it hears the Voice's announcements too, and the REAL grob
    /// descriptions installed so <c>MakeItem</c> resolves real definitions.
    /// </summary>
    private static Tree BuildTree(string[] staffConsists, string[] voiceConsists)
    {
        Loaded();

        Tree tree = new Tree();

        LilyPondScheme.Registries.Translators[Sym(RecorderName)] =
            new TranslatorCreator(
                Sym(RecorderName),
                context =>
                {
                    GrobRecorder recorder = new GrobRecorder(context);
                    tree.Recorders.Add(recorder);
                    return recorder;
                });

        List<(string, object)> staffMods = new List<(string, object)>
        {
            ("translator-type", Sym("Engraver_group")),
            ("accepts", Sym("Voice")),
            ("default-child", Sym("Voice")),
            ("consists", Sym(RecorderName)),
        };
        foreach (string name in staffConsists)
        {
            staffMods.Add(("consists", Sym(name)));
        }

        List<(string, object)> voiceMods = new List<(string, object)>
        {
            ("translator-type", Sym("Engraver_group")),
        };
        foreach (string name in voiceConsists)
        {
            voiceMods.Add(("consists", Sym(name)));
        }

        ContextDef globalDef = Def(
            "Global", ("accepts", Sym("Score")), ("default-child", Sym("Score")));
        // Score_engraver, not a plain Engraver_group: OneTimeStep, the announce
        // round and the timestep phases are all driven from the Score group, so a
        // plain group would leave every ProcessMusic unrun — silently.
        ContextDef scoreDef = Def(
            "Score",
            ("translator-type", Sym("Score_engraver")),
            ("accepts", Sym("Staff")),
            ("default-child", Sym("Staff")));
        ContextDef staffDef = Def("Staff", staffMods.ToArray());
        ContextDef voiceDef = Def("Voice", voiceMods.ToArray());

        OutputDef layout = new OutputDef();
        foreach (ContextDef definition in new[] { globalDef, scoreDef, staffDef, voiceDef })
        {
            layout.SetVariable((Symbol)definition.ContextName, definition);
        }

        GlobalContext global = new GlobalContext(layout, globalDef);
        global.MakeGlobalTranslator();

        // The definitions above carry no \grobdescriptions; take the table straight
        // from the Scheme layer so the engravers make REAL grobs.
        global.InitializeGrobProperties();

        tree.Global = global;
        return tree;
    }

    private static void Iterate(Tree tree, string musicDefinition, string musicName)
    {
        Eval(musicDefinition);
        MusicObject music = (MusicObject)Eval(musicName);
        Interpreter.RunWithLargeStack(() => tree.Global.Iterate(music));
    }

    [Fact]
    public void rest_engraver_makes_a_rest_and_a_pitched_rest_takes_its_position()
    {
        //Arrange
        Tree tree = BuildTree(
            Array.Empty<string>(),
            new[] { "Rest_engraver" });

        //Act
        Iterate(
            tree,
            @"(define epg5-rest
                (make-music 'RestEvent
                  'duration (ly:make-duration 2)
                  'pitch (ly:make-pitch 0 2 0)))",
            "epg5-rest");

        //Assert
        List<Grob> rests = tree.Recorder.Named("Rest");
        rests.Count.Should().Be(1);

        // With a pitch and no middleCPosition the staff position is the pitch's own
        // step count.
        rests[0].GetProperty("staff-position").Should().Be(2L);
    }

    [Fact]
    public void dots_engraver_gives_a_dotted_note_its_dots_and_the_column_collects_them()
    {
        //Arrange
        Tree tree = BuildTree(
            Array.Empty<string>(),
            new[] { "Note_heads_engraver", "Dots_engraver", "Dot_column_engraver" });

        //Act
        Iterate(
            tree,
            @"(define epg5-dotted
                (make-music 'NoteEvent
                  'duration (ly:make-duration 2 1)
                  'pitch (ly:make-pitch 0 0 0)))",
            "epg5-dotted");

        //Assert
        List<Grob> heads = tree.Recorder.Named("NoteHead");
        List<Grob> dots = tree.Recorder.Named("Dots");
        List<Grob> columns = tree.Recorder.Named("DotColumn");
        heads.Count.Should().Be(1);
        dots.Count.Should().Be(1);
        columns.Count.Should().Be(1);

        // Dots_engraver links the dot to its head and parents it vertically there.
        RhythmicHead.GetDots(heads[0]).Should().BeSameAs(dots[0]);
        dots[0].YParent.Should().BeSameAs(heads[0]);

        // Dot_column_engraver collects the dot and supports against the head.
        IReadOnlyList<Grob> collected
            = PointerGroupInterface.ExtractGrobSet(columns[0], Sym("dots"));
        collected.Should().Equal(dots[0]);
        PointerGroupInterface.ExtractGrobSet(columns[0], Sym("side-support-elements"))
            .Should().Contain(heads[0]);
    }

    [Fact]
    public void rhythmic_column_engraver_glues_a_chords_heads_into_one_note_column()
    {
        //Arrange
        Tree tree = BuildTree(
            Array.Empty<string>(),
            new[] { "Note_heads_engraver", "Rhythmic_column_engraver" });

        //Act
        Iterate(
            tree,
            @"(define epg5-chord
                (make-music 'EventChord
                  'elements (list (make-music 'NoteEvent
                                    'duration (ly:make-duration 2)
                                    'pitch (ly:make-pitch 0 0 0))
                                  (make-music 'NoteEvent
                                    'duration (ly:make-duration 2)
                                    'pitch (ly:make-pitch 0 2 0)))))",
            "epg5-chord");

        //Assert
        List<Grob> columns = tree.Recorder.Named("NoteColumn");
        columns.Count.Should().Be(1);

        IReadOnlyList<Grob> heads
            = PointerGroupInterface.ExtractGrobSet(columns[0], Sym("note-heads"));
        heads.Count.Should().Be(2);
        heads[0].XParent.Should().BeSameAs(columns[0]);
        heads[1].XParent.Should().BeSameAs(columns[0]);
    }

    [Fact]
    public void completion_heads_engraver_splits_a_note_at_the_measure_and_ties_the_halves()
    {
        //Arrange
        Tree tree = BuildTree(
            Array.Empty<string>(),
            new[] { "Completion_heads_engraver" });

        // The completion engravers read the Timing half of the context; the
        // Timing_translator itself is EPG8's, so the measure state is pinned by hand:
        // timing on, one-whole-note measures, position never advanced — which reads as
        // a measure boundary at every whole note.
        tree.Global.SetProperty(Sym("timing"), true);
        tree.Global.SetProperty(Sym("measureLength"), new Moment(Rational.One));

        //Act
        // A breve: two whole measures' worth, split into two tied whole notes.
        Iterate(
            tree,
            @"(define epg5-breve
                (make-music 'NoteEvent
                  'duration (ly:make-duration -1)
                  'pitch (ly:make-pitch 0 0 0)))",
            "epg5-breve");

        //Assert
        List<Grob> heads = tree.Recorder.Named("NoteHead");
        heads.Count.Should().Be(2);

        // The two halves are tied, and the ties get their column.
        tree.Recorder.Named("Tie").Count.Should().Be(1);
        tree.Recorder.Named("TieColumn").Count.Should().Be(1);

        // The typeset halves carry the SPLIT duration, not the original breve.
        StreamEvent firstCause = heads[0].EventCause();
        (firstCause.GetProperty("duration") as Duration?).Should().Be(Duration.WholeNote);
        SchemeUtilities.ToBool(firstCause.GetProperty("autosplit-end")).Should().BeTrue();
    }

    [Fact]
    public void completion_rest_engraver_splits_a_long_rest_at_the_measure()
    {
        //Arrange
        Tree tree = BuildTree(
            Array.Empty<string>(),
            new[] { "Completion_rest_engraver" });

        tree.Global.SetProperty(Sym("timing"), true);
        tree.Global.SetProperty(Sym("measureLength"), new Moment(Rational.One));

        //Act
        Iterate(
            tree,
            @"(define epg5-long-rest
                (make-music 'RestEvent 'duration (ly:make-duration -1)))",
            "epg5-long-rest");

        //Assert
        tree.Recorder.Named("Rest").Count.Should().Be(2);
    }

    [Fact]
    public void multi_measure_rest_engraver_makes_the_spanner_and_its_number()
    {
        //Arrange
        Tree tree = BuildTree(
            Array.Empty<string>(),
            new[] { "Multi_measure_rest_engraver" });

        //Act
        Iterate(
            tree,
            @"(define epg5-mmrest
                (make-music 'MultiMeasureRestMusic
                  'duration (ly:make-duration 0)))",
            "epg5-mmrest");

        //Assert
        List<Grob> rests = tree.Recorder.Named("MultiMeasureRest");
        List<Grob> numbers = tree.Recorder.Named("MultiMeasureRestNumber");
        rests.Count.Should().Be(1);
        numbers.Count.Should().Be(1);

        // The number hangs off the rest on both axes and supports against it.
        numbers[0].XParent.Should().BeSameAs(rests[0]);
        numbers[0].YParent.Should().BeSameAs(rests[0]);
        PointerGroupInterface.ExtractGrobSet(numbers[0], Sym("side-support-elements"))
            .Should().Contain(rests[0]);
    }

    [Fact]
    public void rest_collision_engraver_stays_quiet_while_busy_grobs_is_empty()
    {
        //Arrange
        // busyGrobs is written by Grob_pq_engraver (EPG10). Until it lands the queue
        // answers the empty list, and an empty queue makes NO collision object — that
        // is upstream's own path for it, so this fence pins the absence rather than
        // stubbing anything.
        Tree tree = BuildTree(
            new[] { "Rest_collision_engraver" },
            new[] { "Rest_engraver" });

        //Act
        Iterate(
            tree,
            @"(define epg5-lone-rest
                (make-music 'RestEvent 'duration (ly:make-duration 2)))",
            "epg5-lone-rest");

        //Assert
        tree.Recorder.Named("Rest").Count.Should().Be(1);
        tree.Recorder.Named("RestCollision").Count.Should().Be(0);
    }

    [Fact]
    public void collision_engraver_puts_two_note_columns_under_a_note_collision()
    {
        //Arrange
        // Two simultaneous VOICES need context-specced music, whose iterator is not
        // ported yet, so the pipeline cannot produce two columns in one timestep —
        // this drives the engraver through the same group protocol the pipeline uses.
        Loaded();
        Context voice = new Context(Sym("Voice"));
        EngraverGroup group = new EngraverGroup();
        voice.Implementation = group;
        group.ConnectToContext(voice);
        // The real route installs one Grob_properties context property per grob
        // name (see GlobalContext.InstallGrobDescriptions); all-grob-descriptions
        // itself is a Guile variable, not a context property, and a SetProperty of
        // it is refused by the type check.
        object descriptions = Eval("all-grob-descriptions");
        foreach (string grobName in new[] { "NoteColumn", "NoteCollision" })
        {
            voice.SetProperty(
                Sym(grobName),
                new GrobProperties(DefinitionOf(descriptions, grobName), Nil.Instance));
        }

        GrobRecorder recorder = new GrobRecorder(voice);
        group.AddTranslator(recorder);
        CollisionEngraver engraver = new CollisionEngraver(voice);
        group.AddTranslator(engraver);

        //Act
        Item first = null;
        Item second = null;
        Interpreter.RunWithLargeStack(() =>
        {
            first = recorder.MakeItem("NoteColumn", Nil.Instance);
            second = recorder.MakeItem("NoteColumn", Nil.Instance);
            group.AcknowledgeGrobs();
            group.RunPhase(TranslatorPrecomputeIndex.ProcessAcknowledged);
        });

        //Assert
        // Both columns went under ONE NoteCollision, X-parented to it and positioned
        // by ly:grob::x-parent-positioning — the callback that makes reading a
        // column's X offset trigger the collision's positioning.
        first.XParent.Should().NotBeNull();
        first.XParent.Name.Should().Be("NoteCollision");
        second.XParent.Should().BeSameAs(first.XParent);

        object positioning = LilyPondScheme.LookupProcedure(
            Sym("ly:grob::x-parent-positioning"));
        first.GetPropertyData(Sym("X-offset")).Should().BeSameAs(positioning);
    }

    [Fact]
    public void the_rest_collision_chain_reaches_the_positioning_through_the_composed_offset()
    {
        //Arrange
        // Rest_collision::add_column chains force-shift-callback-rest over the rest's
        // Y-offset through the vendored grob::compose-function; reading the offset
        // must run the chain end to end — the inner original callback, then the shift,
        // then the collision's own positioning-done — and answer the shift's zero.
        Loaded();
        object descriptions = Eval("all-grob-descriptions");

        Grob collision = new Item(DefinitionOf(descriptions, "RestCollision"));
        Grob column = new Item(DefinitionOf(descriptions, "NoteColumn"));
        Grob rest = new Item(DefinitionOf(descriptions, "Rest"));
        rest.SetProperty(Sym("duration-log"), 2L);

        NoteColumn.AddHead(column, rest);
        column.GetObject(Sym("rest")).Should().BeSameAs(rest);

        //Act
        Interpreter.RunWithLargeStack(() => RestCollision.AddColumn(collision, column));

        //Assert
        column.GetObject(Sym("rest-collision")).Should().BeSameAs(collision);

        // The chained property is an unpure-pure container, not a bare procedure:
        // the pure half passes the previous offset through untouched.
        object chained = rest.GetPropertyData(Sym("Y-offset"));
        chained.Should().BeOfType<UnpurePureContainer>();

        // Reading the offset runs the whole chain: composed lambda → ly:unpure-call →
        // force-shift (translating by the inner callback's answer, then triggering
        // positioning-done on the collision) → 0.0.
        object offset = null;
        Interpreter.RunWithLargeStack(() => offset = rest.GetProperty(Sym("Y-offset")));
        offset.Should().Be(0.0);
        SchemeUtilities.ToBool(collision.GetProperty(Sym("positioning-done")))
            .Should().BeTrue();
    }

    private static object DefinitionOf(object descriptions, string grobName)
    {
        Pair entry = SchemeUtilities.Assq(Sym(grobName), descriptions);
        entry.Should().NotBeNull();
        return entry.Cdr;
    }
}
