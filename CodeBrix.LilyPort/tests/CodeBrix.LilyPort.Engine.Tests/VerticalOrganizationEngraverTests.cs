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
/// EPG7's engravers running REACHABLY: real registry resolution, real context
/// definitions, real iteration, real grob definitions out of the vendored Scheme
/// layer — registered is not behaving, and behaving is not reachable, which is the
/// distinction these tests exist to pin.
/// <para>
/// The context tree is the <see cref="MusicIterationTests"/> fixture shape with a
/// real <c>Score_engraver</c> group at Score, so announced grobs are typeset into a
/// real system, and with the EPG7 engravers in the <c>\consists</c> lists exactly
/// where <c>ly/engraver-init.ly</c> puts them.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class VerticalOrganizationEngraverTests : IDisposable
{
    private const string ProbeEngraverName = "Vertical_probe_engraver";

    private static readonly object LoadGate = new object();

    private static Interpreter _interpreter;

    /// <summary>Removes the fixture's probe from the process-global registry.</summary>
    public void Dispose()
        => LilyPondScheme.Registries?.Translators.Remove(Sym(ProbeEngraverName));

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

    /// <summary>Records how many staves <c>stavesFound</c> holds at each timestep's end.</summary>
    internal sealed class ProbeEngraver : Engraver
    {
        public ProbeEngraver(Context context)
            : base(context)
        {
        }

        public override string ClassName => ProbeEngraverName;

        public List<int> StavesFoundCounts { get; } = new List<int>();

        public override void StopTranslationTimestep()
        {
            int count = 0;
            object cursor = GetProperty("stavesFound");
            while (cursor is Pair pair)
            {
                count++;
                cursor = pair.Cdr;
            }

            StavesFoundCounts.Add(count);
        }
    }

    internal sealed class EngraveRun
    {
        public GlobalContext Global { get; set; }

        public Context Score { get; set; }

        public ScoreEngraver ScoreEngraver { get; set; }

        public SystemGrob System { get; set; }

        public ProbeEngraver Probe { get; set; }

        public Grob Find(string grobName) => FindOn(System, grobName);

        /// <summary>
        /// The system to read grobs from AFTER line breaking has run: the first broken
        /// piece if there is one, the root otherwise. EPG15 made this distinction real —
        /// <c>System::break_into_pieces</c> clones the root into one piece per line and the
        /// root then suicides, so a test that calls <c>GetPaperSystems</c> and keeps reading
        /// the root finds nothing.
        /// </summary>
        public SystemGrob EngravedLine =>
            System != null && System.BrokenIntos.Count > 0 ? System.BrokenSystems()[0] : System;

        public static Grob FindOn(SystemGrob system, string grobName)
        {
            foreach (Grob grob in system.AllElements)
            {
                if (grob.Name == grobName)
                {
                    return grob;
                }
            }

            return null;
        }
    }

    private static ContextDef Def(string name, params object[][] mods)
    {
        ContextDef definition = new ContextDef();
        definition.AddContextMod(Pair.List(Sym("context-name"), Sym(name)));
        foreach (object[] mod in mods)
        {
            definition.AddContextMod(Pair.List(mod));
        }

        return definition;
    }

    private static object[] Mod(string tag, object argument) => new[] { (object)Sym(tag), argument };

    internal static EngraveRun EngraveOneNote()
    {
        Loaded();

        EngraveRun run = new EngraveRun();

        LilyPondScheme.Registries.Translators[Sym(ProbeEngraverName)] =
            new TranslatorCreator(
                Sym(ProbeEngraverName),
                context =>
                {
                    ProbeEngraver probe = new ProbeEngraver(context);
                    run.Probe = probe;
                    return probe;
                });

        ContextDef globalDef = Def(
            "Global",
            Mod("accepts", Sym("Score")),
            Mod("default-child", Sym("Score")));
        ContextDef scoreDef = Def(
            "Score",
            Mod("translator-type", Sym("Score_engraver")),
            Mod("accepts", Sym("Staff")),
            Mod("default-child", Sym("Staff")),
            Mod("consists", Sym("Paper_column_engraver")),
            Mod("consists", Sym("Vertical_align_engraver")),
            Mod("consists", Sym("System_start_delimiter_engraver")),
            Mod("consists", Sym("Staff_collecting_engraver")),
            Mod("consists", Sym(ProbeEngraverName)));
        ContextDef staffDef = Def(
            "Staff",
            Mod("translator-type", Sym("Engraver_group")),
            Mod("accepts", Sym("Voice")),
            Mod("default-child", Sym("Voice")),
            Mod("consists", Sym("Axis_group_engraver")),
            Mod("consists", Sym("Staff_symbol_engraver")));
        ContextDef voiceDef = Def(
            "Voice",
            Mod("translator-type", Sym("Engraver_group")),
            Mod("consists", Sym("Note_heads_engraver")));

        // PaperDefaults carries what font selection and line thicknesses read —
        // the same stand-in the real pipeline uses until Track P's \paper arrives.
        OutputDef layout = PaperDefaults.Create();
        foreach (ContextDef definition in new[] { globalDef, scoreDef, staffDef, voiceDef })
        {
            layout.SetVariable((Symbol)definition.ContextName, definition);
        }

        GlobalContext global = new GlobalContext(layout, globalDef);

        // The fixture definitions carry no \grobdescriptions, so the descriptions come
        // straight from the Scheme layer — the recorded EPG2 shortcut.
        global.InitializeGrobProperties();
        global.MakeGlobalTranslator();

        // In ly/engraver-init.ly these are Score properties; a fixture may put them on
        // Global because context property reads walk upward.
        global.SetProperty(Sym("topLevelAlignment"), true);
        global.SetProperty(Sym("systemStartDelimiter"), Sym("SystemStartBar"));

        Eval(@"(define vertical-organization-music
                 (make-music 'SequentialMusic
                   'elements (list (make-music 'NoteEvent
                                     'duration (ly:make-duration 2)
                                     'pitch (ly:make-pitch 0 0 0)))))");
        MusicObject music = (MusicObject)Eval("vertical-organization-music");

        Interpreter.RunWithLargeStack(() => global.Iterate(music));

        run.Global = global;
        run.Score = global.Children.Count > 0 ? global.Children[0] : null;
        run.ScoreEngraver = run.Score?.Implementation as ScoreEngraver;
        run.System = run.ScoreEngraver?.System;
        return run;
    }

    [Fact]
    public void the_three_translators_answer_from_the_registry()
    {
        //Arrange
        Loaded();

        //Act
        IReadOnlyList<string> missing = TranslatorRegistry.MissingTranslators(
            LilyPondScheme.Registries,
            new[]
            {
                "Vertical_align_engraver",
                "System_start_delimiter_engraver",
                "Staff_collecting_engraver",
            });

        //Assert
        missing.Should().BeEmpty();
    }

    [Fact]
    public void a_score_grows_a_vertical_alignment_that_owns_the_staff_axis_group()
    {
        //Arrange / Act
        EngraveRun run = EngraveOneNote();

        //Assert
        run.System.Should().NotBeNull();
        Grob alignment = run.Find("VerticalAlignment");
        Grob axisGroup = run.Find("VerticalAxisGroup");
        alignment.Should().NotBeNull();
        axisGroup.Should().NotBeNull();

        // Align_interface::add_element reparents the group onto the alignment and
        // plants the parent-positioning procedure as its Y offset — reading the
        // group's position is then what runs the whole alignment.
        axisGroup.GetParent(Axis.Y).Should().BeSameAs(alignment);
        AxisGroupInterface.Elements(alignment).Should().Contain(axisGroup);
        axisGroup.GetPropertyData(Sym("Y-offset"))
            .Should().BeSameAs(LilyPondScheme.LookupProcedure(
                Sym("ly:grob::y-parent-positioning")));
    }

    [Fact]
    public void the_system_start_bar_collects_the_staff_symbol()
    {
        //Arrange / Act
        EngraveRun run = EngraveOneNote();

        //Assert
        Grob delimiter = run.Find("SystemStartBar");
        Grob staffSymbol = run.Find("StaffSymbol");
        delimiter.Should().NotBeNull();
        staffSymbol.Should().NotBeNull();

        PointerGroupInterface.ExtractGrobSet(delimiter, Sym("elements"))
            .Should().Contain(staffSymbol);

        // The engraver bound the delimiter to the command columns at both ends.
        ((Spanner)delimiter).GetBound(Direction.Negative).Should().NotBeNull();
        ((Spanner)delimiter).GetBound(Direction.Positive).Should().NotBeNull();
    }

    [Fact]
    public void staves_found_holds_the_staff_while_it_is_alive()
    {
        //Arrange / Act
        EngraveRun run = EngraveOneNote();

        //Assert
        // The probe reads stavesFound at the end of every timestep; the staff symbol
        // is announced at the first one, so every recorded count is 1.
        run.Probe.Should().NotBeNull();
        run.Probe.StavesFoundCounts.Should().NotBeEmpty();
        run.Probe.StavesFoundCounts.Should().Contain(1);
    }

    [Fact]
    public void reading_the_staff_position_runs_the_alignment_through_the_real_chain()
    {
        //Arrange
        EngraveRun run = EngraveOneNote();
        run.ScoreEngraver.PaperScore.Process();
        run.ScoreEngraver.PaperScore.GetPaperSystems();

        // EPG15: the grobs to read are the BROKEN PIECE's, not the root's. The root is
        // cloned into one piece per line and then suicides, so both Finds answered null
        // here and the test died on a NullReferenceException that named nothing.
        SystemGrob line = run.EngravedLine;
        line.Should().NotBeSameAs(run.System);
        Grob alignment = EngraveRun.FindOn(line, "VerticalAlignment");
        Grob axisGroup = EngraveRun.FindOn(line, "VerticalAxisGroup");
        alignment.Should().NotBeNull();
        axisGroup.Should().NotBeNull();

        //Act
        // This read is the trigger: Y-offset resolves to y-parent-positioning, which
        // reads the alignment's positioning-done, which is
        // ly:align-interface::align-to-ideal-distances in the real grob definition.
        double y = axisGroup.RelativeCoordinate(line, Axis.Y);

        //Assert
        // The staff is pushed DOWN so that its top skyline touches the alignment's
        // origin — a 4-space staff reaches about 2 staff spaces up, so the group must
        // move by at least that.
        y.Should().BeLessThan(-1.0);
        alignment.GetPropertyData(Sym("positioning-done")).Should().Be(true);
    }

    [Fact]
    public void align_two_groups_through_the_scheme_callback_chain()
    {
        //Arrange
        // A hand-built alignment whose positioning-done is the REGISTERED Scheme
        // callback, elements added through the real AddElement: reading one element's
        // offset must run the whole chain through the interpreter and move both.
        Loaded();
        object alignToMinimum = LilyPondScheme.LookupProcedure(
            Sym("ly:align-interface::align-to-minimum-distances"));
        alignToMinimum.Should().NotBeNull();

        object skylines = new SkylinePair(
            new[] { new Box(new Interval(0, 4), new Interval(-1, 1)) },
            Axis.X).ToScheme();

        Item me = new Item(TestAlist(
            ("meta", TestAlist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
            ("axes", Pair.List(1L)),
            ("stacking-dir", -1L),
            ("positioning-done", alignToMinimum)));
        Item first = new Item(TestAlist(
            ("meta", TestAlist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
            ("vertical-skylines", skylines)));
        Item second = new Item(TestAlist(
            ("meta", TestAlist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
            ("vertical-skylines", skylines)));

        AlignInterface.AddElement(me, first);
        AlignInterface.AddElement(me, second);

        //Act
        double firstY = 0;
        double secondY = 0;
        Interpreter.RunWithLargeStack(() =>
        {
            firstY = first.GetOffset(Axis.Y);
            secondY = second.GetOffset(Axis.Y);
        });

        //Assert
        firstY.Should().BeApproximately(-1.0, 1e-12);
        secondY.Should().BeApproximately(-3.0, 1e-12);
    }

    [Fact]
    public void set_axis_chains_an_unpure_pure_offset_callback()
    {
        //Arrange
        Loaded();
        Item grob = new Item(TestAlist(
            ("meta", TestAlist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance)))));

        //Act
        Interpreter.RunWithLargeStack(() => SidePositionInterface.SetAxis(grob, Axis.Y));

        //Assert
        // side-axis is declared, and the Y offset is now an unpure/pure pair built by
        // scm/output-lib.scm's grob::compose-function — proof the ly:unpure-call
        // route is live. Reading the offset answers 0 for a directionless grob.
        grob.GetPropertyData(Sym("side-axis")).Should().Be(1L);
        grob.GetPropertyData(Sym("Y-offset")).Should().BeOfType<UnpurePureContainer>();

        double offset = 0;
        Interpreter.RunWithLargeStack(() => offset = grob.GetOffset(Axis.Y));
        offset.Should().Be(0.0);
    }

    [Fact]
    public void staff_refpoint_extent_carries_the_spaceable_staff_offset()
    {
        //Arrange
        EngraveRun run = EngraveOneNote();
        run.ScoreEngraver.PaperScore.Process();
        run.ScoreEngraver.PaperScore.GetPaperSystems();
        SystemGrob line = run.EngravedLine;
        Grob axisGroup = EngraveRun.FindOn(line, "VerticalAxisGroup");
        axisGroup.Should().NotBeNull();

        //Act
        // Hand-computed from System::get_paper_system: the extent collects
        // relative_coordinate(system, Y) for each LIVE SPACEABLE stave. One staff means
        // one point, so upstream's interval is that offset on both ends.
        double expected = 0;
        Interpreter.RunWithLargeStack(() => expected = axisGroup.RelativeCoordinate(line, Axis.Y));
        Prob paperSystem = null;
        Interpreter.RunWithLargeStack(() => paperSystem = line.GetPaperSystem());

        //Assert
        object extent = paperSystem.GetProperty(Sym("staff-refpoint-extent"));
        extent.Should().BeOfType<Pair>();
        Pair pair = (Pair)extent;
        ((double)pair.Car).Should().Be(expected);
        ((double)pair.Cdr).Should().Be(expected);

        // CONTROL. The divergence this replaced argued the only choices were "absent" or
        // "a zero interval claiming the staves sit at the origin". Both are refuted by
        // this same run: the property is present, and the offset it carries is not zero.
        expected.Should().NotBe(0.0);
    }

    [Fact]
    public void staff_refpoint_extent_is_empty_not_zero_without_spaceable_staves()
    {
        //Arrange
        Loaded();
        SystemGrob bare = new SystemGrob(TestAlist(
            ("meta", TestAlist(("name", Sym("System")), ("interfaces", Nil.Instance))),
            ("axes", Pair.List(0L, 1L))));

        //Act
        // No vertical-alignment object, so upstream's loop adds no point and the interval
        // stays as Interval () left it: EMPTY.
        Prob paperSystem = null;
        Interpreter.RunWithLargeStack(() => paperSystem = bare.GetPaperSystem());

        //Assert
        // Empty is (+inf . -inf) on BOTH engines, which is what makes an unset extent
        // read as "no constraint" rather than as a real position. This is the fence the
        // old absent-or-zero reasoning needed and did not have.
        object extent = paperSystem.GetProperty(Sym("staff-refpoint-extent"));
        extent.Should().BeOfType<Pair>();
        Pair pair = (Pair)extent;
        ((double)pair.Car).Should().Be(double.PositiveInfinity);
        ((double)pair.Cdr).Should().Be(double.NegativeInfinity);

        // CONTROL: an empty interval is not the zero interval, and the difference is the
        // whole point — a reader guarding on "is the car a number" treats these opposite ways.
        ((double)pair.Car).Should().NotBe(0.0);
        (pair.Car.Equals(pair.Cdr)).Should().BeFalse();
    }

    [Fact]
    public void whiteout_puts_a_white_background_under_the_grob_and_transparent_suppresses_it()
    {
        //Arrange
        // Grob::get_print_stencil processes whiteout BEFORE colour and grob-cause, and
        // only when the grob is not transparent — upstream's own comment says an
        // invisible grob's whiteout has no effect.
        Loaded();
        Item grob = new Item(TestAlist(
            ("meta", TestAlist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance)))));
        grob.Layout = new OutputDef();
        grob.Layout.SetVariable("line-thickness", 0.1);
        Stencil shape = Lookup.FilledBox(new Box(new Interval(0, 1), new Interval(0, 1)));
        grob.SetProperty("stencil", shape);
        grob.SetProperty("whiteout", true);

        //Act
        Stencil printed = Stencil.Empty;
        Interpreter.RunWithLargeStack(() => printed = grob.GetPrintStencil());

        //Assert
        // stencil-whiteout with no style is stencil-whiteout-box, which ADDS a
        // round-filled-box under the original in the whiteout colour. Both must be in the
        // expression: the background, and the shape it sits behind.
        // COUNT, not presence: the grob's own stencil here IS a round-filled-box, so
        // "contains one" is true either way. What whiteout adds is one MORE of them,
        // wrapped in a colour.
        int whiteoutBoxes = CountSymbol(printed.Expression, "round-filled-box");
        MentionsSymbol(printed.Expression, "color").Should().BeTrue();

        //Arrange — CONTROL. The same grob with whiteout off draws no background at all.
        Item plain = new Item(TestAlist(("meta", TestAlist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance)))));
        plain.Layout = grob.Layout;
        plain.SetProperty("stencil", shape);

        //Act
        Stencil plainPrinted = Stencil.Empty;
        Interpreter.RunWithLargeStack(() => plainPrinted = plain.GetPrintStencil());

        //Assert
        int plainBoxes = CountSymbol(plainPrinted.Expression, "round-filled-box");
        whiteoutBoxes.Should().Be(plainBoxes + 1);
        MentionsSymbol(plainPrinted.Expression, "color").Should().BeFalse();

        //Arrange — SECOND CONTROL: transparent wins, because the whiteout branch is
        //guarded on visibility rather than applied and then thrown away.
        Item invisible = new Item(TestAlist(("meta", TestAlist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance)))));
        invisible.Layout = grob.Layout;
        invisible.SetProperty("stencil", shape);
        invisible.SetProperty("whiteout", true);
        invisible.SetProperty("transparent", true);

        //Act
        Stencil invisiblePrinted = Stencil.Empty;
        Interpreter.RunWithLargeStack(() => invisiblePrinted = invisible.GetPrintStencil());

        //Assert
        CountSymbol(invisiblePrinted.Expression, "round-filled-box").Should().Be(0);
    }

    /// <summary>
    /// Whether a stencil expression tree mentions the named symbol anywhere. Asks about
    /// the TREE rather than a printed form, so it cannot be fooled by a symbol's name
    /// appearing inside a string.
    /// </summary>
    private static int CountSymbol(object expression, string name)
    {
        if (expression is Pair pair)
        {
            return CountSymbol(pair.Car, name) + CountSymbol(pair.Cdr, name);
        }

        return ReferenceEquals(expression, Sym(name)) ? 1 : 0;
    }

    private static bool MentionsSymbol(object expression, string name)
    {
        if (expression is Pair pair)
        {
            return MentionsSymbol(pair.Car, name) || MentionsSymbol(pair.Cdr, name);
        }

        return ReferenceEquals(expression, Sym(name));
    }

    private static object TestAlist(params (string Key, object Value)[] entries)
    {
        object result = Nil.Instance;
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            result = new Pair(new Pair(Sym(entries[i].Key), entries[i].Value), result);
        }

        return result;
    }
}
