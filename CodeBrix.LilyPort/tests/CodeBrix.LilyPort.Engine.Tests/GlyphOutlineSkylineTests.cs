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
/// </summary>
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
