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
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// REACHABILITY fences for the EPG9 engravers: each one is driven end to end — real
/// music built by <c>make-music</c>, real iterators, real stream events, a real context
/// tree resolving real <c>\consists</c> lists — and its grobs are then inspected for
/// real property values. Registered is not behaving, and behaving is not reachable;
/// these tests pin the third of those.
/// <para>
/// The tree-building pattern is <see cref="MusicIterationTests"/>'s, for the reason
/// recorded there: <c>get_default_interpreter</c> is CREATE_ONLY, so events must flow
/// into a tree built from context DEFINITIONS rather than into hand-made contexts.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class Epg9EngraverTests : IDisposable
{
    private const string CollectorName = "Epg9_collector_engraver";
    private const string TrillMakerName = "Epg9_trill_maker_engraver";

    /// <summary>
    /// Removes the fixture's own translators from the process-global registry after
    /// every test, so the fixtures cannot leak into any other test that resolves a
    /// <c>\consists</c> list.
    /// </summary>
    public void Dispose()
    {
        LilyPondScheme.Registries?.Translators.Remove(Sym(CollectorName));
        LilyPondScheme.Registries?.Translators.Remove(Sym(TrillMakerName));
    }

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
            foreach (object form in SchemeReader.ReadAll(source, "<epg9-test>"))
            {
                result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }
        });

        return result;
    }

    /// <summary>An engraver that records every grob announced at Score level.</summary>
    private sealed class CollectorEngraver : Engraver
    {
        public CollectorEngraver(Context context)
            : base(context)
        {
        }

        public override string ClassName => CollectorName;

        public List<Grob> Grobs { get; } = new List<Grob>();

        public override void AcknowledgeGrob(GrobInfo info) => Grobs.Add(info.Grob);
    }

    /// <summary>
    /// Stands in for the trill spanner machinery (EPG10-12): hears a trill span event
    /// and announces a real <c>TrillSpanner</c> with that event as its cause, which is
    /// all <see cref="PitchedTrillEngraver"/> needs to see.
    /// </summary>
    private sealed class TrillMakerEngraver : Engraver
    {
        private StreamEvent _event;

        public TrillMakerEngraver(Context context)
            : base(context)
        {
        }

        public override string ClassName => TrillMakerName;

        public override void ConnectToContext()
        {
            base.ConnectToContext();
            ListenTo("trill-span-event", ev => _event = ev);
        }

        public override void DisconnectFromContext()
        {
            RemoveListeners();
            base.DisconnectFromContext();
        }

        public override void ProcessMusic()
        {
            if (_event != null)
            {
                MakeSpanner("TrillSpanner", _event);
            }
        }

        public override void StopTranslationTimestep() => _event = null;
    }

    private sealed class Tree
    {
        public GlobalContext Global { get; set; }

        public CollectorEngraver Collector { get; set; }

        /// <summary>
        /// Every context announced under Global, kept because the LIVE TREE IS EMPTY
        /// once interpretation finishes: Context::check_removal removes any context with
        /// no children and no clients, which after the iterators quit is all of them.
        /// Upstream does the same, so this is not a port quirk to work around.
        /// </summary>
        public List<Context> Announced { get; } = new List<Context>();

        public Context Find(string name)
            => FindByName(Global, name) ?? Announced.Find(
                context => string.Equals(context.ContextName, name, StringComparison.Ordinal));

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
    /// Builds Global → Score → Staff → Voice from definitions, with a grob collector
    /// at Score level and the given extra <c>\consists</c> lists below it.
    /// </summary>
    private static Tree BuildTree(string[] staffConsists, string[] voiceConsists)
    {
        Loaded();

        Tree tree = new Tree();

        LilyPondScheme.Registries.Translators[Sym(CollectorName)] =
            new TranslatorCreator(Sym(CollectorName), context =>
            {
                CollectorEngraver collector = new CollectorEngraver(context);
                tree.Collector = collector;
                return collector;
            });

        LilyPondScheme.Registries.Translators[Sym(TrillMakerName)] =
            new TranslatorCreator(Sym(TrillMakerName), context => new TrillMakerEngraver(context));

        List<(string, object)> staffMods = new List<(string, object)>
        {
            ("translator-type", Sym("Engraver_group")),
            ("accepts", Sym("Voice")),
            ("default-child", Sym("Voice")),
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
        // Score_engraver, not a plain Engraver_group: the SCORE context's group is
        // what drives the timestep -- its OneTimeStep listener runs ProcessMusic and
        // DoAnnounces. Under a plain group nothing engraves and nothing says so.
        ContextDef scoreDef = Def(
            "Score",
            ("translator-type", Sym("Score_engraver")),
            ("accepts", Sym("Staff")),
            ("default-child", Sym("Staff")),
            ("consists", Sym(CollectorName)));
        ContextDef staffDef = Def("Staff", staffMods.ToArray());
        ContextDef voiceDef = Def("Voice", voiceMods.ToArray());

        OutputDef layout = new OutputDef();
        foreach (ContextDef definition in new[] { globalDef, scoreDef, staffDef, voiceDef })
        {
            layout.SetVariable((Symbol)definition.ContextName, definition);
        }

        GlobalContext global = new GlobalContext(layout, globalDef);
        global.MakeGlobalTranslator();

        // The real pipeline's Global gets its Grob_properties from the parsed
        // \grobdescriptions operation; a hand-built definition has none, so take the
        // shortcut GlobalContext keeps for exactly this: read all-grob-descriptions
        // out of the loaded Scheme layer. Without it MakeItem answers "No grob
        // definition found" for every grob.
        global.InitializeGrobProperties();

        tree.Global = global;

        // Recorded as they are announced, so Find still answers after the tree has been
        // torn down. See Tree.Announced.
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

    private static void Iterate(Tree tree, MusicObject music)
        => Interpreter.RunWithLargeStack(() => tree.Global.Iterate(music));

    private static List<Grob> Named(Tree tree, string grobName)
    {
        List<Grob> found = new List<Grob>();
        foreach (Grob grob in tree.Collector.Grobs)
        {
            if (grob.Name == grobName)
            {
                found.Add(grob);
            }
        }

        return found;
    }

    [Fact]
    public void accidental_engraver_creates_an_accidental_for_a_forced_note()
    {
        //Arrange
        Eval(@"(define epg9-forced
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'force-accidental #t
                                     'pitch (ly:make-pitch 0 0 0)))))");
        MusicObject music = (MusicObject)Eval("epg9-forced");
        Tree tree = BuildTree(
            new[] { "Accidental_engraver" }, new[] { "Note_heads_engraver" });
        tree.Global.SetProperty(Sym("localAlterations"), Nil.Instance);

        //Act
        Iterate(tree, music);

        //Assert
        // The accidental is made through the VOICE engraver (upstream's comment about
        // reading Accidental settings at Voice level), parented to its head, filed
        // under an AccidentalPlacement, and linked back from the head.
        List<Grob> accidentals = Named(tree, "Accidental");
        accidentals.Should().HaveCount(1);
        accidentals[0].GetProperty(Sym("forced")).Should().Be(true);

        List<Grob> heads = Named(tree, "NoteHead");
        heads.Should().HaveCount(1);
        heads[0].GetObject(Sym("accidental-grob")).Should().BeSameAs(accidentals[0]);
        accidentals[0].YParent.Should().BeSameAs(heads[0]);

        List<Grob> placements = Named(tree, "AccidentalPlacement");
        placements.Should().HaveCount(1);
        accidentals[0].XParent.Should().BeSameAs(placements[0]);
        placements[0].GetObject(Sym("accidental-grobs")).Should().BeAssignableTo<Pair>();
    }

    [Fact]
    public void accidental_engraver_applies_the_same_octave_rule()
    {
        //Arrange
        // cis then c: the sharped note needs its accidental; the natural that follows
        // needs a restoring one under the same-octave rule, because the measure now
        // carries the sharp in localAlterations.
        Eval(@"(define epg9-ruled
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 0 1/2))
                                   (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 0 0)))))");
        MusicObject music = (MusicObject)Eval("epg9-ruled");
        object rules = Eval("(list 'Staff (make-accidental-rule 'same-octave 0))");
        Tree tree = BuildTree(
            new[] { "Accidental_engraver" }, new[] { "Note_heads_engraver" });
        tree.Global.SetProperty(Sym("localAlterations"), Nil.Instance);
        tree.Global.SetProperty(Sym("autoAccidentals"), rules);

        //Act
        Iterate(tree, music);

        //Assert
        Named(tree, "Accidental").Should().HaveCount(2);
    }

    [Fact]
    public void accidental_engraver_records_the_alteration_in_local_alterations()
    {
        //Arrange
        Eval(@"(define epg9-alt
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 3 1/2)))))");
        MusicObject music = (MusicObject)Eval("epg9-alt");
        Tree tree = BuildTree(
            new[] { "Accidental_engraver" }, new[] { "Note_heads_engraver" });
        tree.Global.SetProperty(Sym("localAlterations"), Nil.Instance);

        //Act
        Iterate(tree, music);

        //Assert
        // The entry is written on the sounding context and every one above it that
        // carries localAlterations; the key is (octave . notename) and the value opens
        // with the alteration as a SCHEME number.
        Context voice = tree.Find("Voice");
        voice.Should().NotBeNull();
        object alterations = voice.GetProperty(Sym("localAlterations"));
        alterations.Should().BeAssignableTo<Pair>();

        Pair entry = (Pair)((Pair)alterations).Car;
        SchemeUtilities.IsEqual(entry.Car, new Pair(0L, 3L)).Should().BeTrue();
        object alteration = ((Pair)entry.Cdr).Car;
        SchemeConvert.ToRational(alteration, "alteration").Should().Be(new Rational(1, 2));
    }

    [Fact]
    public void ambitus_engraver_builds_the_range_with_one_live_accidental()
    {
        //Arrange
        // c' up to gis': the natural bound's accidental is redundant against an empty
        // key signature and commits suicide; the sharped bound keeps its alteration.
        Eval(@"(define epg9-ambitus
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 0 0))
                                   (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 5 1/2)))))");
        MusicObject music = (MusicObject)Eval("epg9-ambitus");
        Tree tree = BuildTree(
            new[] { "Ambitus_engraver" }, new[] { "Note_heads_engraver" });
        tree.Global.SetProperty(Sym("middleCPosition"), -6L);

        //Act
        Iterate(tree, music);

        //Assert
        List<Grob> ambitusHeads = Named(tree, "AmbitusNoteHead");
        ambitusHeads.Should().HaveCount(2);

        List<Grob> lines = Named(tree, "AmbitusLine");
        lines.Should().HaveCount(1);
        (lines[0].GetObject(Sym("note-heads")) as GrobArray).Count.Should().Be(2);

        List<long> positions = new List<long>();
        foreach (Grob head in ambitusHeads)
        {
            positions.Add(SchemeConvert.ToLong(
                head.GetProperty(Sym("staff-position")), "staff-position"));
        }

        positions.Should().Contain(-6L);
        positions.Should().Contain(-1L);

        // A suicided grob loses every property, its NAME included, so only the LIVE
        // accidental still answers to AmbitusAccidental -- and there must be exactly
        // one, carrying the sharp.
        List<Grob> accidentals = Named(tree, "AmbitusAccidental");
        accidentals.Should().HaveCount(1);
        accidentals[0].IsLive.Should().BeTrue();
        SchemeConvert.ToRational(
            accidentals[0].GetProperty(Sym("alteration")), "alteration")
            .Should().Be(new Rational(1, 2));

        // The natural bound's link was cleared when its accidental died.
        int linked = 0;
        foreach (Grob head in ambitusHeads)
        {
            if (head.GetObject(Sym("accidental-grob")) is Grob)
            {
                linked++;
            }
        }

        linked.Should().Be(1);
    }

    [Fact]
    public void pitched_trill_engraver_prints_the_bracketed_auxiliary_head()
    {
        //Arrange
        Eval(@"(define epg9-trill
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'SimultaneousMusic
                     'elements (list (make-music 'NoteEvent
                                       'duration (ly:make-duration 2)
                                       'pitch (ly:make-pitch 0 0 0))
                                     (make-music 'TrillSpanEvent
                                       'span-direction -1
                                       'pitch (ly:make-pitch 0 1 0)))))))");
        MusicObject music = (MusicObject)Eval("epg9-trill");
        Tree tree = BuildTree(
            new[] { "Pitched_trill_engraver" },
            new[] { "Note_heads_engraver", TrillMakerName });
        tree.Global.SetProperty(Sym("localAlterations"), Nil.Instance);
        tree.Global.SetProperty(Sym("middleCPosition"), -6L);

        //Act
        Iterate(tree, music);

        //Assert
        List<Grob> trillHeads = Named(tree, "TrillPitchHead");
        trillHeads.Should().HaveCount(1);

        // d' steps() is 1, so the auxiliary head sits at -6 + 1.
        SchemeConvert.ToLong(
            trillHeads[0].GetProperty(Sym("staff-position")), "staff-position")
            .Should().Be(-5L);

        // A natural against an empty measure record still prints, and upstream
        // deliberately makes the accidental for it (alteration 0 forces print_acc).
        List<Grob> trillAccidentals = Named(tree, "TrillPitchAccidental");
        trillAccidentals.Should().HaveCount(1);
        trillHeads[0].GetObject(Sym("accidental-grob")).Should().BeSameAs(trillAccidentals[0]);

        Named(tree, "TrillPitchGroup").Should().HaveCount(1);

        List<Grob> parentheses = Named(tree, "TrillPitchParentheses");
        parentheses.Should().HaveCount(1);
        parentheses[0].XParent.Should().BeSameAs(trillHeads[0]);
    }

    [Fact]
    public void pitch_squash_engraver_moves_note_heads_to_the_squashed_position()
    {
        //Arrange
        Eval(@"(define epg9-squash
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 2 0)))))");
        MusicObject music = (MusicObject)Eval("epg9-squash");
        Tree tree = BuildTree(
            Array.Empty<string>(),
            new[] { "Note_heads_engraver", "Pitch_squash_engraver" });
        tree.Global.SetProperty(Sym("middleCPosition"), -6L);
        tree.Global.SetProperty(Sym("squashedPosition"), 3L);

        //Act
        Iterate(tree, music);

        //Assert
        // Note_heads_engraver put e' at -4; the squash engraver overwrote it when the
        // head was acknowledged.
        List<Grob> heads = Named(tree, "NoteHead");
        heads.Should().HaveCount(1);
        SchemeConvert.ToLong(heads[0].GetProperty(Sym("staff-position")), "staff-position")
            .Should().Be(3L);
    }

    [Fact]
    public void note_name_engraver_makes_a_note_name_with_concatenated_text()
    {
        //Arrange
        Eval(@"(define epg9-names
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 4 0)))))");
        MusicObject music = (MusicObject)Eval("epg9-names");
        object nameFunction = Eval("(lambda (p ctx) \"nn\")");
        Tree tree = BuildTree(
            Array.Empty<string>(), new[] { "Note_name_engraver" });
        tree.Global.SetProperty(Sym("noteNameFunction"), nameFunction);

        //Act
        Iterate(tree, music);

        //Assert
        List<Grob> names = Named(tree, "NoteName");
        names.Should().HaveCount(1);

        // make-concat-markup wraps the collected names; the single name is in there.
        object text = names[0].GetProperty(Sym("text"));
        text.Should().BeAssignableTo<Pair>();
        Printer.Write(text).Should().Contain("nn");
    }

    [Fact]
    public void cue_clef_engraver_creates_the_cue_clef_and_its_modifier()
    {
        //Arrange
        Eval(@"(define epg9-cue
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 0 0)))))");
        MusicObject music = (MusicObject)Eval("epg9-cue");
        Tree tree = BuildTree(
            new[] { "Cue_clef_engraver" }, new[] { "Note_heads_engraver" });
        tree.Global.SetProperty(Sym("cueClefGlyph"), new MutableString("clefs.treble"));
        tree.Global.SetProperty(Sym("cueClefPosition"), -2L);
        tree.Global.SetProperty(Sym("cueClefTransposition"), -7L);

        //Act
        Iterate(tree, music);

        //Assert
        List<Grob> clefs = Named(tree, "CueClef");
        clefs.Should().HaveCountGreaterThanOrEqualTo(1);
        SchemeConvert.ToLong(clefs[0].GetProperty(Sym("staff-position")), "staff-position")
            .Should().Be(-2L);
        clefs[0].GetProperty(Sym("non-default")).Should().Be(true);

        // Transposition -7 puts an "8" BELOW the clef: direction -1.
        List<Grob> modifiers = Named(tree, "ClefModifier");
        modifiers.Should().HaveCountGreaterThanOrEqualTo(1);
        SchemeConvert.ToLong(modifiers[0].GetProperty(Sym("direction")), "direction")
            .Should().Be(-1L);
        modifiers[0].XParent.Should().BeSameAs(clefs[0]);
        modifiers[0].YParent.Should().BeSameAs(clefs[0]);
    }
}
