// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The glyph-outline flattening rule — upstream's <c>Path_interpreter</c>, the one place
/// in the engine that reads a glyph's OUTLINE rather than its declared dimensions.
/// <para>
/// THIS IS THE CORRECTNESS CLAIM FOR THAT MECHANISM, and after ruling R9 (2026-08-16)
/// it is the only thing carrying it. The six regression pages that draw a skyline
/// (<c>show-skylines</c> and the five <c>skyline-*</c> files) cannot be graded against
/// the oracle, because the port builds Emmentaler from the Metafont sources on purpose
/// and that build is not outline-identical: they are graded against a committed
/// PORT-GENERATED baseline instead, which claims NO DRIFT and nothing else. A baseline
/// is a regression instrument; rule 33 forbids reading one as a correctness result.
/// So the claim lives here, where the expectation comes from upstream's own expression
/// rather than from the port's output:
/// </para>
/// <code>
///     quantization = max (2, (int) (chord_length / 0.2))
/// </code>
/// <para>
/// with the chord measured in OUTPUT units — AFTER the transform — so the same glyph
/// set larger is flattened into more segments. That scale dependence is the natural
/// control, and it is what a table returning one constant could not fake. It is paired
/// with its opposite: a glyph drawn with no curves at all must give the SAME count at
/// every scale, which is what shows the dependence comes from the curve rule and not
/// from something incidental to drawing bigger.
/// </para>
/// <para>
/// R15 (2026-08-17) added the two cases that ask which SOURCE the extent comes from —
/// the ink or the LILC-declared box — because the segment-count cases above fence the
/// flattening rule and say nothing about that, and it is the claim `accidental-ancient'
/// and `markup-with-true-dimensions' need while they are graded against R9's baseline.
/// </para>
/// <para>
/// ⚠ AND THOSE TWO CASES ARE WHY THIS CLASS SERIALIZES. They build an interpreter, which
/// rule 8 requires be serialized through <see cref="EngineGlobalStateCollection"/>; the
/// class needed no collection until then, so ADDING an interpreter-building case to a
/// class that had none silently opted it out of that rule. It passed run alone and
/// filtered, and failed intermittently in the full-solution run — which is the only
/// symptom that class of mistake has.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class GlyphOutlineSkylineTests
{
    private const string FontName = "emmentaler-20";

    // Upstream's constant, restated here rather than read from the port: an expectation
    // that imports the value it is checking cannot fail when the value moves.
    private const double QuantizationUnit = 0.2;

    // A curved glyph and a straight-edged one. The straight one was MEASURED rather
    // than guessed, and the first guess was wrong: accidentals.sharp looks like a
    // figure drawn with four strokes and carries twenty curves. Exactly ten glyphs in
    // emmentaler-20 are curve-free, and this is the one with the most segments.
    // (accidentals.sharp is still PORT-COVERAGE's control for a different claim — its
    // OUTLINE is byte-identical between the two Emmentaler builds — which is not the
    // same property and does not make it useful here.)
    private const string CurvedGlyph = "clefs.G";
    private const string StraightGlyph = "noteheads.sM1kievan";

    /// <summary>One drawing command, as the charstring reports it.</summary>
    private sealed class Command
    {
        internal string Kind { get; init; }

        internal Offset From { get; init; }

        internal Offset To { get; init; }
    }

    /// <summary>
    /// Records a glyph's drawing commands without measuring anything, so the expected
    /// segment count can be computed from upstream's expression independently of the
    /// code under test.
    /// </summary>
    private sealed class Recorder : IGlyphPathSink
    {
        private Offset _current;
        private Offset _start;
        private bool _open;

        internal List<Command> Commands { get; } = new List<Command>();

        public void MoveTo(double x, double y)
        {
            Close();
            _current = new Offset(x, y);
            _start = _current;
            _open = true;
        }

        public void LineTo(double x, double y)
        {
            Offset destination = new Offset(x, y);
            Commands.Add(new Command { Kind = "line", From = _current, To = destination });
            _current = destination;
        }

        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            Offset destination = new Offset(x3, y3);
            Commands.Add(new Command { Kind = "curve", From = _current, To = destination });
            _current = destination;
        }

        public void ClosePath() => Close();

        internal void Close()
        {
            if (_open && _current != _start)
            {
                Commands.Add(new Command { Kind = "close", From = _current, To = _start });
            }

            _open = false;
        }
    }

    private static CffFont LoadFont(out List<string> names)
    {
        SfntReader reader = new SfntReader(FontAssets.MusicFont(FontName));
        names = reader.ReadCffGlyphNames();
        return new CffFont(reader.GetTable("CFF "));
    }

    private static List<Command> Commands(CffFont font, int index)
    {
        Recorder recorder = new Recorder();
        CharstringRun run = new CharstringRun(font, index) { Sink = recorder };
        run.Execute();
        recorder.Close();
        return recorder.Commands;
    }

    private static Transform Scale(double scale) => new Transform(scale, 0, 0, scale, 0, 0);

    /// <summary>
    /// Upstream's rule, written out: a line and a closing segment are one segment each,
    /// and a curve is <c>max (2, chord / 0.2)</c> of them, the chord measured after the
    /// transform.
    /// </summary>
    private static int ExpectedSegments(IEnumerable<Command> commands, Transform transform)
    {
        int total = 0;
        foreach (Command command in commands)
        {
            if (command.Kind == "curve")
            {
                Offset chord = transform.Apply(command.To) - transform.Apply(command.From);
                total += Math.Max(2, (int)(chord.Length / QuantizationUnit));
            }
            else
            {
                total++;
            }
        }

        return total;
    }

    private static int TracedSegments(CffFont font, int index, Transform transform)
    {
        LazySkylinePair skyline = new LazySkylinePair(Axis.X);
        GlyphOutlineSkyline.AddOutline(font, skyline, transform, index);
        return skyline.PendingSegmentCount;
    }

    /// <summary>Loads the music font as a metric, the way a markup reaches it.</summary>
    /// <returns>The metric.</returns>
    private static OpenTypeFontMetric LoadMetric()
    {
        byte[] bytes = FontAssets.MusicFont(FontName);
        bytes.Should().NotBeNull();

        Interpreter interpreter = new Interpreter();
        SchemeBootstrap.LoadCore(interpreter);
        return new OpenTypeFontMetric(new OpenTypeFont(bytes, FontName, interpreter), FontName);
    }

    /// <summary>
    /// <c>stencil-true-extent</c>, written out from the vendored expression rather than
    /// called through it: <c>lily/stencil.scm:1096</c> asks
    /// <c>(ly:skylines-for-stencil stencil (other-axis axis))</c> and takes the pair's two
    /// max heights. The OTHER-axis flip is upstream's and is easy to get backwards.
    /// </summary>
    /// <param name="stencil">The stencil to measure.</param>
    /// <param name="axis">The axis whose true extent is wanted.</param>
    /// <returns>The extent of the actual printed ink.</returns>
    private static Interval TrueExtent(Stencil stencil, Axis axis)
    {
        SkylinePair pair = StencilIntegral.SkylinesFromStencil(
            stencil, false, axis == Axis.X ? Axis.Y : Axis.X);
        return new Interval(pair.Down.MaxHeight(), pair.Up.MaxHeight());
    }

    [Fact]
    public void a_curved_glyph_is_flattened_by_upstreams_own_expression()
    {
        //Arrange
        CffFont font = LoadFont(out List<string> names);
        int index = names.IndexOf(CurvedGlyph);
        index.Should().BeGreaterThanOrEqualTo(0);
        List<Command> commands = Commands(font, index);

        // The fixture has to be what it claims: a rule about curves proves nothing on a
        // glyph that has none.
        commands.Should().Contain(command => command.Kind == "curve");

        Transform transform = Scale(0.004);

        //Act
        int traced = TracedSegments(font, index, transform);

        //Assert
        traced.Should().Be(ExpectedSegments(commands, transform));
    }

    [Fact]
    public void the_same_glyph_drawn_larger_is_flattened_into_more_segments()
    {
        //Arrange
        CffFont font = LoadFont(out List<string> names);
        int index = names.IndexOf(CurvedGlyph);
        List<Command> commands = Commands(font, index);
        Transform small = Scale(0.004);
        Transform large = Scale(0.016);

        //Act
        int tracedSmall = TracedSegments(font, index, small);
        int tracedLarge = TracedSegments(font, index, large);

        //Assert
        tracedLarge.Should().BeGreaterThan(tracedSmall);
        tracedSmall.Should().Be(ExpectedSegments(commands, small));
        tracedLarge.Should().Be(ExpectedSegments(commands, large));
    }

    [Fact]
    public void a_glyph_with_no_curves_is_flattened_the_same_way_at_every_scale()
    {
        //Arrange
        CffFont font = LoadFont(out List<string> names);
        int index = names.IndexOf(StraightGlyph);
        index.Should().BeGreaterThanOrEqualTo(0);
        List<Command> commands = Commands(font, index);

        // The control's own premise, asserted rather than assumed.
        commands.Should().NotContain(command => command.Kind == "curve");

        //Act
        int tracedSmall = TracedSegments(font, index, Scale(0.004));
        int tracedLarge = TracedSegments(font, index, Scale(0.016));

        //Assert
        tracedSmall.Should().Be(commands.Count);
        tracedLarge.Should().Be(tracedSmall);
    }

    [Fact]
    public void a_glyphs_true_extent_is_read_from_its_outline_and_not_from_its_declared_box()
    {
        //Arrange
        // Ruling R15's two rows, each measured on its OWN glyph (rule 35b):
        // markup-with-true-dimensions boxes `scripts.trill' four ways, and
        // accidental-ancient's moving mark is `accidentals.hufnagelM1'.
        //
        // These two rows are graded against R9's port-generated baseline, which claims NO
        // DRIFT and nothing else (rule 33). THIS is their correctness claim: that the
        // quantity the baseline freezes is read from the OUTLINE. It has to be asserted
        // separately, because GlyphOutlineSkylineTests' other cases fence how many
        // SEGMENTS the walk produces and say nothing about which SOURCE the extent has.
        OpenTypeFontMetric metric = LoadMetric();

        //Act & Assert
        foreach (string name in new[] { "scripts.trill", "accidentals.hufnagelM1" })
        {
            int index = metric.NameToIndex(name);
            index.Should().NotBe(FontMetric.GlyphIndexInvalid);

            Box declared = metric.GetIndexedCharDimensions(index);
            Box ink = metric.GetIndexedInkDimensions(index);
            Stencil stencil = metric.FindByName(name);

            foreach (Axis axis in new[] { Axis.X, Axis.Y })
            {
                // The fixture's own premise, asserted rather than assumed: LILC and the
                // outline must actually disagree here, by more than the comparator's
                // 0.0100 tolerance, or the test proves nothing about which was read.
                declared[axis].Left.Should().NotBeApproximately(ink[axis].Left, 0.01);

                Interval trueExtent = TrueExtent(stencil, axis);

                // THE CLAIM: the true extent is the ink.
                trueExtent.Left.Should().BeApproximately(ink[axis].Left, 1e-4);
                trueExtent.Right.Should().BeApproximately(ink[axis].Right, 1e-4);

                // THE CONTROL, and the half that makes it a claim about SOURCES: the
                // ORDINARY extent of the very same stencil is the LILC-declared box,
                // exactly. Two readings of one glyph, two different tables.
                stencil.Extent(axis).Left.Should().BeApproximately(declared[axis].Left, 1e-9);
                stencil.Extent(axis).Right.Should().BeApproximately(declared[axis].Right, 1e-9);
            }
        }
    }

    [Fact]
    public void a_glyph_whose_outline_fills_its_declared_box_reads_the_same_either_way()
    {
        //Arrange
        // The control for the test above, and it had to be MEASURED: of the glyphs to
        // hand, `accidentals.sharp' is the one whose declared box and ink box are equal
        // to four decimals on BOTH axes. (It is already PORT-COVERAGE's control for a
        // different claim -- its outline is byte-identical between the two Emmentaler
        // builds -- which is a separate property and not why it is used here.)
        OpenTypeFontMetric metric = LoadMetric();
        int index = metric.NameToIndex("accidentals.sharp");
        Box declared = metric.GetIndexedCharDimensions(index);
        Box ink = metric.GetIndexedInkDimensions(index);
        Stencil stencil = metric.FindByName("accidentals.sharp");

        //Act
        Interval trueX = TrueExtent(stencil, Axis.X);
        Interval trueY = TrueExtent(stencil, Axis.Y);

        //Assert
        // The premise: for THIS glyph the two sources agree.
        ink[Axis.X].Left.Should().BeApproximately(declared[Axis.X].Left, 1e-4);
        ink[Axis.X].Right.Should().BeApproximately(declared[Axis.X].Right, 1e-4);
        ink[Axis.Y].Right.Should().BeApproximately(declared[Axis.Y].Right, 1e-4);

        // So the true extent agrees with the declared box too — which is what shows the
        // discrimination in the previous test is a property of THOSE GLYPHS and not an
        // artifact of the mechanism reporting something unrelated to the box.
        trueX.Right.Should().BeApproximately(declared[Axis.X].Right, 1e-4);
        trueY.Right.Should().BeApproximately(declared[Axis.Y].Right, 1e-4);
        trueY.Left.Should().BeApproximately(declared[Axis.Y].Left, 1e-4);
    }

    [Fact]
    public void a_curve_shorter_than_two_steps_still_gives_two_segments()
    {
        //Arrange
        CffFont font = LoadFont(out List<string> names);
        int index = names.IndexOf(CurvedGlyph);
        List<Command> commands = Commands(font, index);

        // Small enough that every chord is far under one 0.2 step, so max() decides
        // every curve and the count stops depending on the outline's size at all.
        Transform tiny = Scale(0.000001);
        int curves = 0;
        int straight = 0;
        foreach (Command command in commands)
        {
            if (command.Kind == "curve")
            {
                curves++;
            }
            else
            {
                straight++;
            }
        }

        //Act
        int traced = TracedSegments(font, index, tiny);

        //Assert
        curves.Should().BeGreaterThan(0);
        traced.Should().Be(straight + (2 * curves));
    }
}
