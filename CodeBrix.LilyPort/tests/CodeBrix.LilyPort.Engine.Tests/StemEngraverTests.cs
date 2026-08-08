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
/// EPG6 reachability: real music through the real listeners, the real
/// announce/acknowledge round and the real grob definitions, into
/// <c>Stem_engraver</c> — registered is not behaving, and behaving is not reachable.
/// <para>
/// The tree is the <c>MusicIterationTests</c> fixture shape — hand context
/// definitions, because the full <c>ly/engraver-init.ly</c> definitions arrive
/// through the parser session, which belongs to the outer assembly — but the Score
/// context is a real <c>Score_engraver</c> and the grob definitions are the real
/// <c>all-grob-descriptions</c>, so what the stems are made FROM is not a fixture.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class StemEngraverTests
{
    private static readonly object LoadGate = new object();

    private static Interpreter _interpreter;

    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static Interpreter Loaded()
    {
        lock (LoadGate)
        {
            // The canonical per-class loader every collection class uses. Piggybacking
            // on the AMBIENT interpreter instead does not work: a preceding collection
            // class may leave a bootstrap-only interpreter (no scm layer) as Current.
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
            foreach (object form in SchemeReader.ReadAll(source, "<stem-tests>"))
            {
                result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }
        });

        return result;
    }

    /// <summary>Collects every grob announced in its context, in announcement order.</summary>
    private sealed class CollectingEngraver : Engraver
    {
        public CollectingEngraver(Context context)
            : base(context)
        {
        }

        public override string ClassName => "Collecting_engraver";

        public List<Grob> Grobs { get; } = new List<Grob>();

        public override void AcknowledgeGrob(GrobInfo info) => Grobs.Add(info.Grob);
    }

    private const string CollectorName = "Collecting_engraver";

    private sealed class Tree
    {
        public GlobalContext Global { get; set; }

        public List<CollectingEngraver> Collectors { get; } = new List<CollectingEngraver>();

        public List<Grob> Grobs
        {
            get
            {
                List<Grob> all = new List<Grob>();
                foreach (CollectingEngraver collector in Collectors)
                {
                    all.AddRange(collector.Grobs);
                }

                return all;
            }
        }

        public List<Grob> Named(string name)
            => Grobs.FindAll(g => string.Equals(g.Name, name, StringComparison.Ordinal));

        public Grob One(string name)
        {
            List<Grob> found = Named(name);
            found.Count.Should().Be(1);
            return found[0];
        }
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

    private static Tree BuildTree()
    {
        Loaded();

        Tree tree = new Tree();

        LilyPondScheme.Registries.Translators[Sym(CollectorName)] =
            new TranslatorCreator(
                Sym(CollectorName),
                context =>
                {
                    CollectingEngraver collector = new CollectingEngraver(context);
                    tree.Collectors.Add(collector);
                    return collector;
                });

        ContextDef globalDef = Def(
            "Global", ("accepts", Sym("Score")), ("default-child", Sym("Score")));
        ContextDef scoreDef = Def(
            "Score",
            ("translator-type", Sym("Score_engraver")),
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
            ("consists", Sym("Note_heads_engraver")),
            ("consists", Sym("Stem_engraver")),
            ("consists", Sym(CollectorName)));

        OutputDef layout = PaperDefaults.Create();
        foreach (ContextDef definition in new[] { globalDef, scoreDef, staffDef, voiceDef })
        {
            layout.SetVariable((Symbol)definition.ContextName, definition);
        }

        GlobalContext global = new GlobalContext(layout, globalDef);

        // The hand definitions above carry no \grobdescriptions, so install the REAL
        // all-grob-descriptions the loaded Scheme layer built — the stems these tests
        // inspect are made from the same definitions the real pipeline uses.
        global.InitializeGrobProperties();

        global.MakeGlobalTranslator();

        tree.Global = global;
        return tree;
    }

    private static Tree Engrave(string musicSource)
    {
        Tree tree = BuildTree();
        MusicObject music = (MusicObject)Eval(musicSource);
        Interpreter.RunWithLargeStack(() => tree.Global.Iterate(music));
        return tree;
    }

    private static string Note(int durationLog, string extra = "")
        => "(make-music 'SequentialMusic 'elements (list"
           + " (make-music 'NoteEvent 'duration (ly:make-duration " + durationLog + ")"
           + " 'pitch (ly:make-pitch 0 0 0)" + extra + ")))";

    [Fact]
    public void a_quarter_note_gets_a_stem_and_a_stem_stub_but_no_flag()
    {
        //Arrange / Act
        Tree tree = Engrave(Note(2));

        //Assert
        Grob stem = tree.One("Stem");
        tree.One("StemStub").Should().NotBeNull();
        tree.Named("Flag").Should().BeEmpty();

        // The head is on the stem and the stem is on the head — both halves of
        // Stem::add_head.
        Grob head = tree.One("NoteHead");
        Stem.HeadCount(stem).Should().Be(1);
        PointerGroupInterface.ExtractGrobSet(stem, Sym("note-heads"))[0].Should().BeSameAs(head);
        head.GetObject("stem").Should().BeSameAs(stem);

        Stem.IsNormalStem(stem).Should().BeTrue();
        Stem.IsInvisible(stem).Should().BeFalse();
    }

    [Fact]
    public void the_stem_computes_real_direction_and_length_through_its_scheme_callbacks()
    {
        //Arrange
        Tree tree = Engrave(Note(2));
        Grob stem = tree.One("Stem");

        long direction = 0;
        double length = 0;

        //Act
        // Property reads run the registered ly:stem::* callbacks through the
        // interpreter — which is exactly the route define-grobs.scm reaches them by.
        Interpreter.RunWithLargeStack(() =>
        {
            direction = SchemeConvert.ToLong(stem.GetProperty("direction"), "direction");
            length = SchemeConvert.ToDouble(stem.GetProperty("length"), "length");
        });

        //Assert
        // Middle-line note: default-direction answers CENTER, so ly:stem::calc-direction
        // falls back on neutral-direction, which is DOWN.
        direction.Should().Be(-1);

        // 2 * 3.5 from details.lengths, minus the forced-direction shortening step of
        // 1/3 — upstream's numbers, reached through the real details alist.
        (length > 6.0 && length < 7.5).Should().BeTrue();
    }

    [Fact]
    public void an_eighth_note_grows_a_flag_attached_to_its_stem()
    {
        //Arrange / Act
        Tree tree = Engrave(Note(3));

        //Assert
        Grob stem = tree.One("Stem");
        Grob flag = tree.One("Flag");

        stem.GetObject("flag").Should().BeSameAs(flag);
        flag.XParent.Should().BeSameAs(stem);

        //Act again: the glyph name comes from ly:flag::glyph-name through the
        // interpreter — style "", down stem, duration log 3.
        object glyphName = null;
        Interpreter.RunWithLargeStack(() => glyphName = flag.GetProperty("glyph-name"));

        //Assert
        glyphName.ToString().Should().Be("flags.d3");
    }

    [Fact]
    public void a_whole_note_still_makes_an_invisible_stem()
    {
        //Arrange / Act
        Tree tree = Engrave(Note(0));

        //Assert
        // Downstream spacing depends on the invisible stem EXISTING — upstream makes
        // one for every rhythmic head, whole notes included.
        Grob stem = tree.One("Stem");
        tree.Named("Flag").Should().BeEmpty();

        Stem.IsInvisible(stem).Should().BeTrue();
        Stem.IsNormalStem(stem).Should().BeFalse();

        Interval width = Interval.Empty;
        Stencil? stencil = null;
        Interpreter.RunWithLargeStack(() =>
        {
            width = Stem.Width(stem);
            stencil = stem.GetStencil();
        });

        width.IsEmpty.Should().BeTrue();
        stencil.HasValue.Should().BeFalse();
    }

    [Fact]
    public void chord_heads_share_one_stem()
    {
        //Arrange / Act
        Tree tree = Engrave(
            "(make-music 'SequentialMusic 'elements (list"
            + " (make-music 'EventChord 'elements (list"
            + " (make-music 'NoteEvent 'duration (ly:make-duration 2)"
            + " 'pitch (ly:make-pitch 0 0 0))"
            + " (make-music 'NoteEvent 'duration (ly:make-duration 2)"
            + " 'pitch (ly:make-pitch 0 2 0))))))");

        //Assert
        Grob stem = tree.One("Stem");
        tree.Named("NoteHead").Count.Should().Be(2);
        Stem.HeadCount(stem).Should().Be(2);
    }

    [Fact]
    public void a_tremolo_request_makes_a_stem_tremolo_wired_to_the_stem()
    {
        //Arrange / Act
        Tree tree = Engrave(Note(
            2, " 'articulations (list (make-music 'TremoloEvent 'tremolo-type 8))"));

        //Assert
        Grob stem = tree.One("Stem");
        Grob tremolo = tree.One("StemTremolo");

        // c4:8 — one tremolo flag: intlog2(8) - 2.
        SchemeConvert.ToLong(tremolo.GetProperty("flag-count"), "flag-count").Should().Be(1);
        stem.GetObject("tremolo-flag").Should().BeSameAs(tremolo);
        tremolo.GetObject("stem").Should().BeSameAs(stem);
        tremolo.XParent.Should().BeSameAs(stem);

        object shape = null;
        double slope = 0;
        long direction = 0;
        Interpreter.RunWithLargeStack(() =>
        {
            shape = tremolo.GetProperty("shape");
            slope = SchemeConvert.ToDouble(tremolo.GetProperty("slope"), "slope");
            direction = SchemeConvert.ToLong(tremolo.GetProperty("direction"), "direction");
        });

        // Unbeamed quarter: no flag in the way, so a beam-like stroke with the
        // gentle slope, taking the stem's own (DOWN) direction.
        shape.Should().BeSameAs(Sym("beam-like"));
        slope.Should().Be(0.25);
        direction.Should().Be(-1);
    }

    [Fact]
    public void a_tremolo_longer_than_the_note_warns_and_is_dropped()
    {
        //Arrange
        // Warn is process-global. Snapshot-diff rather than ClearMessages, so a test
        // recording in a PARALLEL collection (OriginTests) never loses its messages.
        bool recorded = Warn.RecordMessages;
        Warn.RecordMessages = true;
        int start = Warn.Messages.Count;

        try
        {
            //Act
            Tree tree = Engrave(Note(
                2, " 'articulations (list (make-music 'TremoloEvent 'tremolo-type 4))"));

            //Assert
            tree.Named("StemTremolo").Should().BeEmpty();
            string all = string.Join("\n", MessagesFrom(start));
            all.Should().Contain("tremolo duration is too long");
        }
        finally
        {
            Warn.RecordMessages = recorded;
        }
    }

    [Fact]
    public void mixing_incompatible_durations_on_one_stem_warns_with_upstream_text()
    {
        //Arrange
        bool recorded = Warn.RecordMessages;
        Warn.RecordMessages = true;
        int start = Warn.Messages.Count;

        try
        {
            //Act — a quarter and a whole note in ONE chord share a timestep, so the
            // second head lands on the first head's (quarter) stem.
            Engrave(
                "(make-music 'SequentialMusic 'elements (list"
                + " (make-music 'EventChord 'elements (list"
                + " (make-music 'NoteEvent 'duration (ly:make-duration 2)"
                + " 'pitch (ly:make-pitch 0 0 0))"
                + " (make-music 'NoteEvent 'duration (ly:make-duration 0)"
                + " 'pitch (ly:make-pitch 0 2 0))))))");

            //Assert
            string all = string.Join("\n", MessagesFrom(start));
            all.Should().Contain("adding note head to incompatible stem (type = 1/4)");
            all.Should().Contain("maybe input should specify polyphonic voices");
        }
        finally
        {
            Warn.RecordMessages = recorded;
        }
    }

    private static List<string> MessagesFrom(int start)
    {
        IReadOnlyList<string> messages = Warn.Messages;
        List<string> tail = new List<string>();
        for (int i = start; i < messages.Count; i++)
        {
            tail.Add(messages[i]);
        }

        return tail;
    }

    [Fact]
    public void set_beaming_and_beam_multiplicity_follow_upstream_shape()
    {
        //Arrange — a real stem off a real engrave, so the beaming property carries the
        // real type predicate.
        Tree tree = Engrave(Note(3));
        Grob stem = tree.One("Stem");

        //Act / Assert — unset answers zero on both sides.
        Stem.GetBeaming(stem, Direction.Negative).Should().Be(0);

        Stem.SetBeaming(stem, 2, Direction.Negative);
        Stem.GetBeaming(stem, Direction.Negative).Should().Be(2);
        Stem.GetBeaming(stem, Direction.Positive).Should().Be(0);

        // beam_multiplicity unites both sides' position lists.
        Stem.SetBeaming(stem, 1, Direction.Positive);
        Slice multiplicity = Stem.BeamMultiplicity(stem);
        multiplicity.Left.Should().Be(0);
        multiplicity.Right.Should().Be(1);
    }
}
