// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG4's arithmetic: the duration-to-space rule, the springs the spacing spanner
/// states from it, and the rod conversion that turns an item-to-item distance into a
/// column-to-column constraint.
/// <para>
/// The numbers are worked out by hand from <c>lily/spacing-options.cc</c> and
/// <c>lily/spacing-basic.cc</c>, since upstream ships no tests for either.
/// </para>
/// </summary>
public class SpacingSpannerTests
{
    [Fact]
    public void the_reference_duration_gets_shortest_duration_space_increments()
    {
        //Arrange
        // At the reference duration the ratio is 1, its log2 is 0, and the space is
        // exactly shortest-duration-space increments wide.
        SpacingOptions options = new SpacingOptions();

        //Act
        double space = options.GetDurationSpace(new Rational(1, 8));

        //Assert
        space.Should().BeApproximately(2.0 * 1.2, 1e-12);
    }

    [Fact]
    public void doubling_a_duration_adds_exactly_one_increment()
    {
        //Arrange
        SpacingOptions options = new SpacingOptions();

        //Act
        double eighth = options.GetDurationSpace(new Rational(1, 8));
        double quarter = options.GetDurationSpace(new Rational(1, 4));

        //Assert
        // Gourlay's rule: the space grows with the LOG of the duration, so a doubling
        // is one increment rather than twice the space.
        (quarter - eighth).Should().BeApproximately(1.2, 1e-12);
    }

    [Fact]
    public void durations_below_the_reference_grow_linearly_not_logarithmically()
    {
        //Arrange
        SpacingOptions options = new SpacingOptions();

        //Act
        double sixteenth = options.GetDurationSpace(new Rational(1, 16));

        //Assert
        // (shortest_duration_space + ratio - 1) * increment, ratio = 0.5.
        sixteenth.Should().BeApproximately((2.0 + 0.5 - 1.0) * 1.2, 1e-12);

        // Logarithmic shrinkage would have gone a whole increment below the reference
        // instead; that is the case upstream deliberately does not use, because it
        // stretches the long notes out of proportion.
        double logarithmic = (2.0 + Math.Log2(0.5)) * 1.2;
        sixteenth.Should().NotBe(logarithmic);
    }

    [Fact]
    public void spacing_options_read_off_a_grob_and_fall_back_when_unset()
    {
        //Arrange
        Grob grob = SpacingFixtures.NewSpacingGrob(
            ("spacing-increment", 2.5),
            ("shortest-duration-space", 3.0),
            ("packed-spacing", true));
        SpacingOptions options = new SpacingOptions();

        //Act
        options.InitFromGrob(grob);

        //Assert
        options.Increment.Should().Be(2.5);
        options.ShortestDurationSpace.Should().Be(3.0);
        options.Packed.Should().BeTrue();

        // common-shortest-duration is unset, so the built-in default applies -- and its
        // MAIN part wins over its grace part.
        options.GlobalShortest.Should().Be(new Rational(1, 8));
        options.StretchUniformly.Should().BeFalse();
    }

    [Fact]
    public void an_unset_boolean_spacing_option_is_false_not_true()
    {
        //Arrange
        // from_scm<bool> without a fallback is scm_is_eq (s, SCM_BOOL_T), NOT Scheme's
        // "anything but #f". An unset property answers '(), which is TRUE in Scheme and
        // must still read as false here.
        Grob grob = SpacingFixtures.NewSpacingGrob();
        SpacingOptions options = new SpacingOptions { Packed = true };

        //Act
        options.InitFromGrob(grob);

        //Assert
        options.Packed.Should().BeFalse();
        options.FloatNonmusicalColumns.Should().BeFalse();
        options.FloatGraceColumns.Should().BeFalse();
    }

    [Fact]
    public void a_rod_between_two_items_becomes_a_constraint_between_their_columns()
    {
        //Arrange
        (PaperColumn Left, PaperColumn Right, Item LeftItem, Item RightItem) f
            = SpacingFixtures.TwoColumnsWithItems();

        // The left item sits 0.75 into its column and the right one 0.25 into its own.
        f.LeftItem.TranslateAxis(0.75, Axis.X);
        f.RightItem.TranslateAxis(0.25, Axis.X);

        Rod rod = new Rod(f.LeftItem, f.RightItem) { Distance = 3.0 };

        //Act
        rod.AddToColumns();

        //Assert
        // Upstream folds each item's offset within its column into the distance, with
        // the sign of the side it is on: +0.75 from the left, -0.25 from the right.
        SpacingFixtures.RodDistance(f.Left, f.Right)
            .Should().BeApproximately(3.0 + 0.75 - 0.25, 1e-12);
    }

    [Fact]
    public void a_rod_whose_ends_share_a_column_states_nothing()
    {
        //Arrange
        (PaperColumn Left, PaperColumn Right, Item LeftItem, Item RightItem) f
            = SpacingFixtures.TwoColumnsWithItems();
        Item second = SpacingFixtures.AddItemTo(f.Left);
        Rod rod = new Rod(f.LeftItem, second) { Distance = 3.0 };

        //Act
        rod.AddToColumns();

        //Assert
        // A column cannot constrain itself, and the spacing problem is stated between
        // columns -- so both ends landing in one column means the rod says nothing.
        SpaceableGrob.GetMinimumDistances(f.Left).Should().BeOfType<Nil>();
    }

    [Fact]
    public void two_breakable_columns_get_a_measure_wide_spring()
    {
        //Arrange
        // A whole measure at the reference duration is 8 eighths; upstream's rule is
        // increment * (measure / global_shortest) * 0.8.
        (PaperColumn Left, PaperColumn Right) f = SpacingFixtures.TwoBreakableColumns(
            new Moment(new Rational(1, 1)));
        SpacingOptions options = new SpacingOptions();
        Grob spanner = SpacingFixtures.NewSpacingGrob(("spacing-increment", 1.2));

        //Act
        Spring spring = SpacingSpanner.StandardBreakableColumnSpacing(
            spanner, f.Left, f.Right, options);

        //Assert
        double space = 1.2 * 8.0 * 0.8;
        spring.IdealDistance.Should().BeApproximately(space, 1e-12);

        // The stretchability deliberately EXCLUDES the minimum distance: a first
        // measure carrying a clef has a large minimum, and letting that inflate the
        // stretch would make it grow far more than a later empty measure.
        spring.InverseStretchStrength.Should().BeApproximately(space, 1e-12);
    }

    [Fact]
    public void simultaneous_columns_get_the_half_unit_spring_staff_spacing_will_replace()
    {
        //Arrange
        // Same moment on both sides: upstream says using dt here is silly and leaves
        // the real answer to Staff_spacing.
        (PaperColumn Left, PaperColumn Right) f = SpacingFixtures.TwoColumnsAtMoments(
            new Moment(new Rational(0)), new Moment(new Rational(0)));
        SpacingOptions options = new SpacingOptions();
        Grob spanner = SpacingFixtures.NewSpacingGrob();

        //Act
        Spring spring = SpacingSpanner.StandardBreakableColumnSpacing(
            spanner, f.Left, f.Right, options);

        //Assert
        spring.IdealDistance.Should().BeApproximately(0.5, 1e-12);
    }

    [Fact]
    public void a_note_spring_is_the_fraction_of_the_ruling_notes_space_it_covers()
    {
        //Arrange
        // A quarter note rules, and the gap covers half of it: the spring is half the
        // space the whole quarter would get.
        (PaperColumn Left, PaperColumn Right) f = SpacingFixtures.TwoMusicalColumns(
            ruling: new Rational(1, 4),
            leftWhen: new Moment(new Rational(0)),
            rightWhen: new Moment(new Rational(1, 8)));
        SpacingOptions options = new SpacingOptions();

        //Act
        Spring spring = SpacingSpanner.NoteSpacingSpring(
            SpacingFixtures.NewSpacingGrob(), f.Left, f.Right, options);

        //Assert
        double quarterSpace = options.GetDurationSpace(new Rational(1, 4));
        spring.IdealDistance.Should().BeApproximately(0.5 * quarterSpace, 1e-12);
        spring.MinDistance.Should().BeApproximately(0.5 * options.Increment, 1e-12);
    }
}

/// <summary>
/// The Scheme representation of a skyline pair, and the separation-item callback that
/// produces it.
/// <para>
/// A skyline pair has no object of its own in Scheme: <c>ly:skyline-pair?</c> is
/// "a pair whose car and cdr are both skylines". A grob property holding anything else
/// fails its own type check and breaks every Scheme reader that expects to
/// <c>car</c> it, which is a defect that stays invisible until something WRITES the
/// property for the first time.
/// </para>
/// </summary>
public class SkylinePairSchemeTests
{
    [Fact]
    public void a_skyline_pair_is_a_cons_of_two_skylines()
    {
        //Arrange
        SkylinePair pair = new SkylinePair(new Box(new Interval(0, 1), new Interval(0, 2)), Axis.Y);

        //Act
        object scheme = pair.ToScheme();

        //Assert
        scheme.Should().BeOfType<Pair>();
        ((Pair)scheme).Car.Should().BeOfType<Skyline>();
        ((Pair)scheme).Cdr.Should().BeOfType<Skyline>();
    }

    [Fact]
    public void a_skyline_pair_round_trips_through_its_scheme_form()
    {
        //Arrange
        SkylinePair pair = new SkylinePair(new Box(new Interval(0, 1), new Interval(0, 2)), Axis.Y);

        //Act
        SkylinePair read = SkylinePair.FromScheme(pair.ToScheme());

        //Assert
        read.Should().NotBeNull();
        read[Direction.Negative].Sky.Should().Be(Direction.Negative);
        read[Direction.Positive].Sky.Should().Be(Direction.Positive);
        read[Direction.Positive].MaxHeight().Should().Be(pair[Direction.Positive].MaxHeight());
    }

    [Fact]
    public void anything_that_is_not_a_pair_of_skylines_is_not_a_skyline_pair()
    {
        //Arrange
        object notAPair = Nil.Instance;

        //Act
        SkylinePair read = SkylinePair.FromScheme(notAPair);

        //Assert
        read.Should().BeNull();
        SkylinePair.FromScheme(new Pair(1L, 2L)).Should().BeNull();
    }

    [Fact]
    public void a_skyline_pair_whose_sides_face_the_wrong_way_is_refused()
    {
        //Arrange
        // Upstream raises rather than silently swapping them, because a pair read the
        // wrong way round measures every distance backwards.
        SkylinePair pair = new SkylinePair(new Box(new Interval(0, 1), new Interval(0, 2)), Axis.Y);
        object swapped = new Pair(pair[Direction.Positive], pair[Direction.Negative]);

        //Act
        Action act = () => SkylinePair.FromScheme(swapped);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }
}
