// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG17's remainder: <c>Repeat_styler</c>, the five iterators built on it, the tuplet
/// and volta grobs, and the eight engravers.
/// <para>
/// The styler's own bookkeeping is tested through a recording subclass rather than
/// through the three concrete stylers, because that bookkeeping — the alternative depth
/// and the nested-return suppression — is what the concrete stylers all rely on and what
/// no single one of them exercises fully.
/// </para>
/// </summary>
public class Epg17RemainderTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    [Fact]
    public void every_epg17_iterator_constructor_is_implemented()
    {
        //Arrange
        string[] landed =
        {
            "ly:alternative-sequence-iterator::constructor",
            "ly:percent-repeat-iterator::constructor",
            "ly:tuplet-iterator::constructor",
            "ly:volta-repeat-iterator::constructor",
            "ly:volta-specced-music-iterator::constructor",
        };

        //Act
        IReadOnlyCollection<string> ported = IteratorPrimitives.Ported;

        //Assert
        foreach (string name in landed)
        {
            ported.Should().Contain(name);
            IteratorPrimitives.NotYetPorted.Should().NotContain(name);
        }
    }

    [Fact]
    public void no_iterator_constructor_remains_unported()
    {
        //Arrange & Act
        // G5 is CLOSED at 28/28. EPG17 owed nine and landed all nine; EPG18 landed the
        // single name that was left after them.
        IReadOnlyList<string> remaining = IteratorPrimitives.NotYetPorted;

        //Assert
        remaining.Should().BeEmpty();
    }

    [Fact]
    public void the_iterators_report_their_upstream_class_names()
    {
        //Arrange & Act
        string[] names =
        {
            new VoltaRepeatIterator().ClassName,
            new AlternativeSequenceIterator().ClassName,
            new VoltaSpeccedMusicIterator().ClassName,
            new TupletIterator().ClassName,
            new PercentRepeatIterator().ClassName,
        };

        //Assert
        names.Should().BeEquivalentTo(new[]
        {
            "Volta_repeat_iterator",
            "Alternative_sequence_iterator",
            "Volta_specced_music_iterator",
            "Tuplet_iterator",
            "Percent_repeat_iterator",
        });
    }

    [Fact]
    public void a_fresh_styler_spans_no_known_time()
    {
        //Arrange & Act
        // Upstream initialises spanned_time_ to {infinity, infinity}, NOT to the empty
        // interval. Alternative_sequence_iterator compares the current moment against
        // both ends to decide whether the alternatives are aligned with the repeat, so
        // an empty interval — whose right end is MINUS infinity — would make every
        // alternative group read as end-aligned before report_start ever ran.
        RepeatStyler styler = RepeatStyler.CreateNull(null);

        //Assert
        styler.SpannedTime.Left.Should().Be(Moment.Infinity);
        styler.SpannedTime.Right.Should().Be(Moment.Infinity);
    }

    [Fact]
    public void the_null_styler_disables_volta_brackets()
    {
        //Arrange
        RepeatStyler styler = RepeatStyler.CreateNull(null);

        //Act
        bool enabled = styler.ReportAlternativeGroupStart(
            Direction.Negative, Direction.Positive, true);

        //Assert
        enabled.Should().BeFalse();
    }

    [Fact]
    public void reporting_a_return_is_remembered()
    {
        //Arrange
        // Volta_repeat_iterator asks reported_return() at the end of the repeat and only
        // issues its own end-repeat when nobody else has: a styler that forgot would
        // produce a doubled return.
        RepeatStyler styler = RepeatStyler.CreateNull(null);

        //Act & Assert
        styler.ReportedReturn.Should().BeFalse();
        styler.ReportReturn(1, 1);
        styler.ReportedReturn.Should().BeTrue();
    }

    [Fact]
    public void an_outer_alternative_group_stays_silent_after_a_deeper_one_returned()
    {
        //Arrange
        // Upstream: "When two \alternative groups are nested and both are end-aligned,
        // we report returns for the deeper one and then remain silent when the outer one
        // tries to report." Getting this wrong prints two D.S. instructions.
        RecordingStyler styler = new RecordingStyler();
        styler.ReportAlternativeGroupStart(Direction.Center, Direction.Positive, true);
        styler.ReportAlternativeGroupStart(Direction.Center, Direction.Positive, true);

        //Act
        styler.ReportReturn(2, 1);              // the inner group, at depth 2
        styler.ReportAlternativeGroupEnd(null, 2);
        styler.ReportReturn(1, 1);              // the outer group, now at depth 1

        //Assert
        styler.Returns.Should().BeEquivalentTo(new[] { 2L });
    }

    [Fact]
    public void closing_every_alternative_group_lets_a_later_return_be_reported_again()
    {
        //Arrange
        RecordingStyler styler = new RecordingStyler();
        styler.ReportAlternativeGroupStart(Direction.Center, Direction.Positive, true);
        styler.ReportReturn(1, 1);
        styler.ReportAlternativeGroupEnd(null, 1);

        //Act
        // Depth is back to zero, so the suppression depth resets and the next repeat's
        // return is reported normally.
        styler.ReportReturn(2, 1);

        //Assert
        styler.Returns.Should().BeEquivalentTo(new[] { 1L, 2L });
    }

    [Fact]
    public void the_alternative_depth_counts_open_groups()
    {
        //Arrange
        RecordingStyler styler = new RecordingStyler();

        //Act & Assert
        styler.Depth.Should().Be(0);
        styler.ReportAlternativeGroupStart(Direction.Center, Direction.Center, true);
        styler.Depth.Should().Be(1);
        styler.ReportAlternativeGroupStart(Direction.Center, Direction.Center, true);
        styler.Depth.Should().Be(2);
        styler.ReportAlternativeGroupEnd(null, 2);
        styler.Depth.Should().Be(1);
        styler.ReportAlternativeGroupEnd(null, 1);
        styler.Depth.Should().Be(0);
    }

    [Fact]
    public void a_default_moment_interval_is_empty_not_a_point_at_zero()
    {
        //Arrange & Act
        // The same load-bearing trap Flower's Interval documents: C# zeroes the fields
        // of a default struct and bypasses every constructor, so without the assigned
        // flag this would read as a zero-length interval AT MOMENT ZERO and every
        // accumulated span would silently include the origin.
        MomentInterval fresh = default;

        //Assert
        fresh.IsEmpty.Should().BeTrue();
        fresh.Left.Should().Be(MomentInterval.MaxSentinel);
        fresh.Right.Should().Be(MomentInterval.MinSentinel);
    }

    [Fact]
    public void adding_points_to_a_default_moment_interval_accumulates_correctly()
    {
        //Arrange
        MomentInterval span = default;

        //Act
        span.AddPoint(new Moment(new Rational(1, 4)));
        span.AddPoint(new Moment(new Rational(3, 4)));

        //Assert
        span.Left.Should().Be(new Moment(new Rational(1, 4)));
        span.Right.Should().Be(new Moment(new Rational(3, 4)));
    }

    [Fact]
    public void a_tuplet_in_grace_time_is_measured_on_the_grace_clock()
    {
        //Arrange
        // A tuplet that starts in grace time but whose length carries no grace part is
        // measured in grace time all the same. Reading the main part instead would put
        // its end a whole note away from its start.
        StreamEvent ev = MakeTupletSpanEvent(new Moment(new Rational(1, 4)));
        Moment now = new Moment(Rational.Zero, new Rational(-1, 8));

        //Act
        TupletDescription description = new TupletDescription(ev, now);

        //Assert
        description.TupletStart.Should().Be(new Rational(-1, 8));
        description.TupletLength.Should().Be(new Rational(1, 4));
    }

    [Fact]
    public void a_tuplet_in_main_time_is_measured_on_the_main_clock()
    {
        //Arrange
        StreamEvent ev = MakeTupletSpanEvent(new Moment(new Rational(1, 4)));
        Moment now = new Moment(new Rational(1, 2));

        //Act
        TupletDescription description = new TupletDescription(ev, now);

        //Assert
        description.TupletStart.Should().Be(new Rational(1, 2));
        description.TupletStop.Should().Be(new Rational(3, 4));
    }

    [Fact]
    public void two_tuplet_descriptions_of_the_same_span_and_ratio_are_equal()
    {
        //Arrange
        // Tuplet_engraver drops a duplicate start event by comparing descriptions, so
        // equality that compared identity would create two brackets for one tuplet in
        // every score that repeats its structure across voices.
        Moment now = new Moment(Rational.Zero);
        TupletDescription first = new TupletDescription(
            MakeTupletSpanEvent(new Moment(new Rational(1, 4)), 3, 2), now);

        TupletDescription second = new TupletDescription(
            MakeTupletSpanEvent(new Moment(new Rational(1, 4)), 3, 2), now);

        //Act & Assert
        (first == second).Should().BeTrue();
    }

    [Fact]
    public void tuplet_descriptions_with_different_ratios_are_not_equal()
    {
        //Arrange
        Moment now = new Moment(Rational.Zero);
        TupletDescription triplet = new TupletDescription(
            MakeTupletSpanEvent(new Moment(new Rational(1, 4)), 3, 2), now);

        TupletDescription quintuplet = new TupletDescription(
            MakeTupletSpanEvent(new Moment(new Rational(1, 4)), 5, 4), now);

        //Act & Assert
        (triplet == quintuplet).Should().BeFalse();
    }

    [Fact]
    public void the_slur_shape_rises_with_width_but_never_past_its_limit()
    {
        //Arrange
        // bezier-bow.cc's closed form, pulled forward for \tupletSlur: the height is
        // h_inf * F (w * r_0 / h_inf) with F (0) = 0 and F (inf) = 1, so it grows
        // monotonically and is bounded by h_inf.
        const double HeightLimit = 1.5;
        const double Ratio = 0.33;

        //Act
        double atZero = BezierBow.SlurHeight(0.0, HeightLimit, Ratio);
        double atFour = BezierBow.SlurHeight(4.0, HeightLimit, Ratio);
        double atForty = BezierBow.SlurHeight(40.0, HeightLimit, Ratio);

        //Assert
        atZero.Should().Be(0.0);
        atFour.Should().BeGreaterThan(atZero);
        atForty.Should().BeGreaterThan(atFour);
        atForty.Should().BeLessThan(HeightLimit);
    }

    [Fact]
    public void the_slur_curve_starts_and_ends_on_the_baseline()
    {
        //Arrange & Act
        Bezier curve = BezierBow.SlurShape(6.0, 1.5, 0.33);

        //Assert
        curve[0].Should().Be(new Offset(0, 0));
        curve[3].Should().Be(new Offset(6.0, 0));
    }

    private static StreamEvent MakeTupletSpanEvent(
        Moment length, long numerator = 3, long denominator = 2)
    {
        StreamEvent ev = new StreamEvent(
            StreamEvent.MakeEventClass(Sym("TupletSpanEvent")), Nil.Instance);

        ev.SetProperty(Sym("length"), length);
        ev.SetProperty(Sym("numerator"), numerator);
        ev.SetProperty(Sym("denominator"), denominator);
        return ev;
    }

    /// <summary>
    /// A styler that records what it was told instead of announcing anything, so the base
    /// class's depth and return bookkeeping can be observed directly.
    /// </summary>
    private sealed class RecordingStyler : RepeatStyler
    {
        public RecordingStyler()
            : base(null)
        {
        }

        public List<long> Returns { get; } = new List<long>();

        public int Depth => AlternativeDepth;

        protected override void DerivedReportStart()
        {
        }

        protected override bool DerivedReportAlternativeGroupStart(
            Direction start, Direction end, bool inOrder) => true;

        protected override void DerivedReportAlternativeStart(
            MusicObject alternative, long alternativeNumber, int voltaDepth, object voltaNumbers)
        {
        }

        protected override void DerivedReportReturn(long alternativeNumber, long returnCount)
            => Returns.Add(alternativeNumber);

        protected override void DerivedReportAlternativeGroupEnd(
            MusicObject alternative, int voltaDepth)
        {
        }
    }
}
