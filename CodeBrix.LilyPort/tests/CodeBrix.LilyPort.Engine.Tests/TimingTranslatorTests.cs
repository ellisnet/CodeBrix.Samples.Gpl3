// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The metric heartbeat, measured directly: <c>Timing_translator</c> driven by the
/// real iterator through real context definitions, with a probe listening at note
/// delivery — the moment every other engraver reads these properties.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class TimingTranslatorTests : IDisposable
{
    private const string ProbeName = "Epg8_timing_probe_engraver";

    /// <summary>Removes the fixture translators from the process-global registry.</summary>
    public void Dispose()
    {
        Epg8TestHarness.Cleanup();
        LilyPondScheme.Registries?.Translators.Remove(Epg8TestHarness.Sym(ProbeName));
    }

    /// <summary>One probe reading per heard note.</summary>
    private readonly struct Reading
    {
        internal Reading(Moment when, Moment position, long barNumber, bool measureStart)
        {
            When = when;
            Position = position;
            BarNumber = barNumber;
            MeasureStart = measureStart;
        }

        internal Moment When { get; }

        internal Moment Position { get; }

        internal long BarNumber { get; }

        internal bool MeasureStart { get; }
    }

    private sealed class TimingProbe : Engraver
    {
        internal TimingProbe(Context context)
            : base(context)
        {
        }

        public override string ClassName => ProbeName;

        internal List<Reading> Readings { get; } = new List<Reading>();

        public override void ConnectToContext()
        {
            base.ConnectToContext();
            ListenTo("note-event", Record);
        }

        public override void DisconnectFromContext()
        {
            RemoveListeners();
            base.DisconnectFromContext();
        }

        private void Record(StreamEvent streamEvent)
        {
            object position = GetProperty("measurePosition");
            object barNumber = GetProperty("currentBarNumber");
            object start = GetProperty("measureStartNow");
            Readings.Add(new Reading(
                NowMoment,
                position is Moment moment ? moment : new Moment(-99),
                SchemeConvert.IsNumber(barNumber)
                    ? SchemeConvert.ToLong(barNumber, "probe")
                    : -99,
                start is bool flag && flag));
        }
    }

    private TimingProbe _probe;

    private Epg8TestHarness.Tree BuildTimingTree(
        params (string Name, object Value)[] extraScoreProps)
    {
        Epg8TestHarness.Loaded();

        LilyPondScheme.Registries.Translators[Epg8TestHarness.Sym(ProbeName)] =
            new TranslatorCreator(
                Epg8TestHarness.Sym(ProbeName),
                context =>
                {
                    _probe = new TimingProbe(context);
                    return _probe;
                });

        List<(string, object)> props = new List<(string, object)>
        {
            ("timeSignature", new Pair(4L, 4L)),
            ("timeSignatureSettings", Epg8TestHarness.Eval("default-time-signature-settings")),
            ("timing", true),
        };
        props.AddRange(extraScoreProps);

        return Epg8TestHarness.BuildTree(
            props.ToArray(),
            new[] { "Timing_translator" },
            Array.Empty<string>(),
            new[] { ProbeName });
    }

    [Fact]
    public void bar_numbers_and_measure_positions_advance_across_measures()
    {
        //Arrange
        // Five quarters in 4/4: the fifth crosses into measure 2. Every value here is
        // read at note delivery, which is when other engravers read it.
        Epg8TestHarness.Tree tree = BuildTimingTree();
        MusicObject music = Epg8TestHarness.QuarterNotes(5);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        _probe.Readings.Count.Should().Be(5);

        _probe.Readings[0].Position.MainPart.Should().Be(Rational.Zero);
        _probe.Readings[0].BarNumber.Should().Be(1);
        _probe.Readings[0].MeasureStart.Should().BeTrue();

        _probe.Readings[1].Position.MainPart.Should().Be(new Rational(1, 4));
        _probe.Readings[1].BarNumber.Should().Be(1);
        _probe.Readings[1].MeasureStart.Should().BeFalse();

        _probe.Readings[3].Position.MainPart.Should().Be(new Rational(3, 4));
        _probe.Readings[3].BarNumber.Should().Be(1);

        // The fifth note is the first of measure 2: position back to zero, bar
        // number advanced, measureStartNow raised for exactly this timestep.
        _probe.Readings[4].Position.MainPart.Should().Be(Rational.Zero);
        _probe.Readings[4].BarNumber.Should().Be(2);
        _probe.Readings[4].MeasureStart.Should().BeTrue();
    }

    [Fact]
    public void the_translator_stops_the_global_clock_at_measure_boundaries()
    {
        //Arrange
        // One BREVE in 4/4 produces events only at 0 and the end at 2; the bar line
        // at 1 exists as a timestep ONLY because stop_translation_timestep asked the
        // global context to stop there. The final bar number proves both boundaries
        // were processed.
        Epg8TestHarness.Tree tree = BuildTimingTree();
        MusicObject music = (MusicObject)Epg8TestHarness.Eval(
            "(make-music 'SequentialMusic 'elements (list"
            + " (make-music 'NoteEvent 'duration (ly:make-duration -1)"
            + "  'pitch (ly:make-pitch 0 0 0))))");

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        Context score = tree.FindContext("Score");
        long bar = SchemeConvert.ToLong(score.GetProperty("currentBarNumber"), "test");
        bar.Should().Be(3);
        (score.GetProperty("measurePosition") is Moment position
            ? position.MainPart
            : new Rational(-99)).Should().Be(Rational.Zero);
    }

    [Fact]
    public void a_partial_upbeat_starts_the_measure_position_negative()
    {
        //Arrange
        // \partial 4 before the first note. The REAL route — \partial's PartialSet,
        // whose make-partial-set broadcasts the partial-event through an
        // apply-context — needs the context-specced and apply-context iterators,
        // which are EPG22's (recorded under FINDINGS). The event itself is what
        // Timing_translator listens for, so the test delivers it as a zero-LENGTH
        // event carrying the same duration property.
        Epg8TestHarness.Tree tree = BuildTimingTree();
        MusicObject music = Epg8TestHarness.QuarterNotes(
            2,
            "(make-music 'PartialEvent 'duration (ly:make-duration 2)"
            + " 'length (ly:make-moment 0))");

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        _probe.Readings.Count.Should().Be(2);
        _probe.Readings[0].Position.MainPart.Should().Be(new Rational(-1, 4));
        _probe.Readings[1].Position.MainPart.Should().Be(Rational.Zero);
        _probe.Readings[1].MeasureStart.Should().BeTrue();
        _probe.Readings[1].BarNumber.Should().Be(1);
    }

    [Fact]
    public void a_narrower_time_signature_shortens_the_measure()
    {
        //Arrange
        // 3/4: the fourth quarter is measure 2. The measureLength itself is derived
        // by initialize through calc-measure-length, so this also fences the Scheme
        // seam the translator computes with.
        Epg8TestHarness.Tree tree = BuildTimingTree(
            ("timeSignature", new Pair(3L, 4L)));
        MusicObject music = Epg8TestHarness.QuarterNotes(4);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        Context score = tree.FindContext("Score");
        Rational measureLength = SchemeConvert.ToRational(
            score.GetProperty("measureLength"), "test");
        measureLength.Should().Be(new Rational(3, 4));

        _probe.Readings[2].BarNumber.Should().Be(1);
        _probe.Readings[3].BarNumber.Should().Be(2);
        _probe.Readings[3].Position.MainPart.Should().Be(Rational.Zero);
        _probe.Readings[3].MeasureStart.Should().BeTrue();
    }

    [Fact]
    public void the_containing_context_answers_to_the_timing_alias()
    {
        //Arrange
        Epg8TestHarness.Tree tree = BuildTimingTree();
        MusicObject music = Epg8TestHarness.QuarterNotes(1);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        // connect_to_context adds the alias; Bar_engraver's first_time_ decision and
        // every \set Timing.x depend on it.
        Context score = tree.FindContext("Score");
        score.IsAlias(Epg8TestHarness.Sym("Timing")).Should().BeTrue();
    }
}
