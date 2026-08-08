// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The engrave-and-inspect harness the EPG8 translator tests share: a
/// Global→Score→Staff→Voice tree built from real <see cref="ContextDef"/>s — the
/// MusicIterationTests pattern — with a grob RECORDER in the Voice and the Score-side
/// properties each test needs assigned through real <c>(assign …)</c> property ops.
/// <para>
/// Registered ≠ behaving ≠ reachable: these fixtures exist so every EPG8 engraver is
/// driven by the real iterator/event/timestep machinery rather than by hand-called
/// methods.
/// </para>
/// </summary>
internal static class Epg8TestHarness
{
    private static readonly object LoadGate = new object();

    private static Interpreter _interpreter;

    internal const string RecorderName = "Epg8_recorder_engraver";
    internal const string MakerName = "Epg8_maker_engraver";

    internal static Symbol Sym(string name) => Symbol.Intern(name);

    /// <summary>Loads the full Scheme layer once, MusicIterationTests-style.</summary>
    internal static Interpreter Loaded()
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

    /// <summary>Evaluates Scheme source in the loaded interpreter.</summary>
    internal static object Eval(string source)
    {
        Interpreter interpreter = Loaded();
        object result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            foreach (object form in SchemeReader.ReadAll(source, "<epg8-test>"))
            {
                result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }
        });

        return result;
    }

    /// <summary>Removes the harness's fixture translators from the global registry.</summary>
    internal static void Cleanup()
    {
        LilyPondScheme.Registries?.Translators.Remove(Sym(RecorderName));
        LilyPondScheme.Registries?.Translators.Remove(Sym(MakerName));
    }

    /// <summary>The tree a test engraves in, and what its probes saw.</summary>
    internal sealed class Tree
    {
        internal GlobalContext Global { get; set; }

        internal List<GrobRecorder> Recorders { get; } = new List<GrobRecorder>();

        internal List<Grob> AcknowledgedGrobs
        {
            get
            {
                List<Grob> all = new List<Grob>();
                foreach (GrobRecorder recorder in Recorders)
                {
                    all.AddRange(recorder.Acknowledged);
                }

                return all;
            }
        }

        internal List<Grob> GrobsNamed(string name)
            => AcknowledgedGrobs.FindAll(
                grob => string.Equals(grob.Name, name, StringComparison.Ordinal));

        internal Context FindContext(string name) => Find(Global, name);

        private static Context Find(Context context, string name)
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
                Context found = Find(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    /// <summary>Records every grob announced where it lives.</summary>
    internal sealed class GrobRecorder : Engraver
    {
        internal GrobRecorder(Context context)
            : base(context)
        {
        }

        public override string ClassName => RecorderName;

        internal List<Grob> Acknowledged { get; } = new List<Grob>();

        public override void AcknowledgeGrob(GrobInfo info) => Acknowledged.Add(info.Grob);
    }

    /// <summary>
    /// Makes a fixed set of named grobs in its first <c>process-music</c> — the stand-in
    /// for the multi-staff arrangements (two bar lines, two grid points) that the
    /// cross-staff engravers react to.
    /// </summary>
    internal sealed class GrobMaker : Engraver
    {
        private readonly string _grobName;
        private readonly int _count;
        private bool _made;

        internal GrobMaker(Context context, string grobName, int count)
            : base(context)
        {
            _grobName = grobName;
            _count = count;
        }

        public override string ClassName => MakerName;

        public override void ProcessMusic()
        {
            if (_made)
            {
                return;
            }

            _made = true;
            for (int i = 0; i < _count; i++)
            {
                MakeItem(_grobName, Nil.Instance);
            }
        }
    }

    /// <summary>Builds the four-context tree with the requested translators and props.</summary>
    /// <param name="scoreProps">Property assignments for the Score definition.</param>
    /// <param name="scoreConsists">Translator names consisted at Score.</param>
    /// <param name="staffConsists">Translator names consisted at Staff.</param>
    /// <param name="voiceConsists">Translator names consisted at Voice; the recorder
    /// is always added last.</param>
    /// <param name="makerGrobName">When set, a <see cref="GrobMaker"/> in the Staff
    /// makes this many of this grob in its first timestep.</param>
    /// <param name="makerCount">How many grobs the maker makes.</param>
    internal static Tree BuildTree(
        (string Name, object Value)[] scoreProps,
        string[] scoreConsists,
        string[] staffConsists,
        string[] voiceConsists,
        string makerGrobName = null,
        int makerCount = 0)
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

        if (makerGrobName != null)
        {
            LilyPondScheme.Registries.Translators[Sym(MakerName)] =
                new TranslatorCreator(
                    Sym(MakerName),
                    context => new GrobMaker(context, makerGrobName, makerCount));
        }

        ContextDef globalDef = Def(
            "Global", null, ("accepts", Sym("Score")), ("default-child", Sym("Score")));

        // Score_engraver rather than a plain Engraver_group: the timestep phases
        // (start/pre/process/stop) are driven by ITS OneTimeStep/Prepare listeners,
        // and every EPG8 translator does its work in those phases.
        List<(string, object)> scoreMods = new List<(string, object)>
        {
            ("translator-type", Sym("Score_engraver")),
            ("accepts", Sym("Staff")),
            ("default-child", Sym("Staff")),
        };
        foreach (string name in scoreConsists ?? Array.Empty<string>())
        {
            scoreMods.Add(("consists", Sym(name)));
        }

        // The recorder sits at Score too, so Score-level grobs (bar numbers, marks)
        // are seen without relying on announcement bubbling settings.
        scoreMods.Add(("consists", Sym(RecorderName)));
        ContextDef scoreDef = Def("Score", scoreProps, scoreMods.ToArray());

        List<(string, object)> staffMods = new List<(string, object)>
        {
            ("translator-type", Sym("Engraver_group")),
            ("accepts", Sym("Voice")),
            ("default-child", Sym("Voice")),
        };
        foreach (string name in staffConsists ?? Array.Empty<string>())
        {
            staffMods.Add(("consists", Sym(name)));
        }

        if (makerGrobName != null)
        {
            staffMods.Add(("consists", Sym(MakerName)));
        }

        ContextDef staffDef = Def("Staff", null, staffMods.ToArray());

        List<(string, object)> voiceMods = new List<(string, object)>
        {
            ("translator-type", Sym("Engraver_group")),
        };
        foreach (string name in voiceConsists ?? Array.Empty<string>())
        {
            voiceMods.Add(("consists", Sym(name)));
        }

        ContextDef voiceDef = Def("Voice", null, voiceMods.ToArray());

        Layout.OutputDef layout = new Layout.OutputDef();
        foreach (ContextDef definition in new[] { globalDef, scoreDef, staffDef, voiceDef })
        {
            layout.SetVariable((Symbol)definition.ContextName, definition);
        }

        GlobalContext global = new GlobalContext(layout, globalDef);

        // The hand-built Global definition carries no \grobdescriptions, so take the
        // defaults straight from the loaded Scheme layer's all-grob-descriptions —
        // the shortcut GlobalContext.InitializeGrobProperties exists for.
        global.InitializeGrobProperties();
        global.MakeGlobalTranslator();

        tree.Global = global;
        return tree;
    }

    /// <summary>Runs the main translation loop over a music expression.</summary>
    internal static void Iterate(Tree tree, Music.MusicObject music)
        => Interpreter.RunWithLargeStack(() => tree.Global.Iterate(music));

    /// <summary>Builds sequential music of quarter-note c' events, plus extras.</summary>
    /// <param name="count">How many quarter notes.</param>
    /// <param name="extraElements">Scheme source for extra music elements, placed
    /// after the notes (or interleaved when it starts with <c>@</c>).</param>
    internal static Music.MusicObject QuarterNotes(int count, string extraElements = "")
    {
        string notes = string.Empty;
        for (int i = 0; i < count; i++)
        {
            notes += "(make-music 'NoteEvent 'duration (ly:make-duration 2)"
                + " 'pitch (ly:make-pitch 0 0 0)) ";
        }

        return (Music.MusicObject)Eval(
            "(make-music 'SequentialMusic 'elements (list " + extraElements + " "
            + notes + "))");
    }

    private static ContextDef Def(
        string name,
        (string Name, object Value)[] assigns,
        params (string Tag, object Argument)[] mods)
    {
        ContextDef definition = new ContextDef();
        definition.AddContextMod(Pair.List(Sym("context-name"), Sym(name)));
        foreach ((string Tag, object Argument) mod in mods)
        {
            definition.AddContextMod(Pair.List(Sym(mod.Tag), mod.Argument));
        }

        foreach ((string Name, object Value) assign in assigns
            ?? Array.Empty<(string, object)>())
        {
            definition.AddContextMod(
                Pair.List(Sym("assign"), Sym(assign.Name), assign.Value));
        }

        return definition;
    }
}
