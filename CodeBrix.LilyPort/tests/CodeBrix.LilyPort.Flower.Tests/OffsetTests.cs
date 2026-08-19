/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

// New-in-family tests for the Offset and Polynomial ports.
//
// NOTE: unlike Rational, Interval and Direction, upstream ships NO test-offset.cc
// or test-polynomial.cc. These cases are written against the behaviour of the
// upstream implementation, which was read directly while porting -- in particular
// the infinity handling in Offset::direction and the three discriminant branches of
// Cardano's formula in Polynomial::solve_cubic.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Flower.Tests;

public class OffsetTests
{
    [Fact]
    public void default_offset_is_the_origin()
    {
        //Arrange / Act
        Offset o = default;

        //Assert
        o.X.Should().Be(0.0);
        o.Y.Should().Be(0.0);
    }

    [Fact]
    public void indexes_by_axis()
    {
        //Arrange
        Offset o = new Offset(3, 4);

        //Act / Assert
        o[Axis.X].Should().Be(3.0);
        o[Axis.Y].Should().Be(4.0);
    }

    [Fact]
    public void arithmetic_works_componentwise()
    {
        //Arrange
        Offset a = new Offset(1, 2);
        Offset b = new Offset(10, 20);

        //Act / Assert
        (a + b).Should().Be(new Offset(11, 22));
        (b - a).Should().Be(new Offset(9, 18));
        (-a).Should().Be(new Offset(-1, -2));
        (a * 3).Should().Be(new Offset(3, 6));
        (b / 2).Should().Be(new Offset(5, 10));
    }

    [Fact]
    public void length_is_the_euclidean_norm()
        => new Offset(3, 4).Length.Should().Be(5.0);

    [Fact]
    public void direction_returns_a_unit_vector()
    {
        //Arrange / Act
        Offset d = new Offset(3, 4).Direction();

        //Assert
        d.X.Should().BeApproximately(0.6, 1e-12);
        d.Y.Should().BeApproximately(0.8, 1e-12);
    }

    [Fact]
    public void direction_of_the_zero_vector_is_itself_not_nan()
    {
        //Arrange / Act -- dividing by a zero length would give NaN, so upstream
        //special-cases this
        Offset d = Offset.Zero.Direction();

        //Assert
        d.Should().Be(Offset.Zero);
    }

    [Fact]
    public void direction_handles_a_single_infinite_coordinate()
    {
        //Arrange / Act / Assert
        new Offset(double.PositiveInfinity, 5).Direction().Should().Be(new Offset(1, 0));
        new Offset(double.NegativeInfinity, 5).Direction().Should().Be(new Offset(-1, 0));
        new Offset(5, double.PositiveInfinity).Direction().Should().Be(new Offset(0, 1));
        new Offset(5, double.NegativeInfinity).Direction().Should().Be(new Offset(0, -1));
    }

    [Fact]
    public void mirror_negates_one_axis_only()
    {
        //Arrange / Act / Assert
        new Offset(3, 4).Mirror(Axis.X).Should().Be(new Offset(-3, 4));
        new Offset(3, 4).Mirror(Axis.Y).Should().Be(new Offset(3, -4));
    }

    [Fact]
    public void swapped_exchanges_the_coordinates()
        => new Offset(3, 4).Swapped().Should().Be(new Offset(4, 3));

    [Fact]
    public void complex_multiply_follows_complex_arithmetic()
    {
        //Arrange -- (1 + 2i)(3 + 4i) = 3 + 4i + 6i + 8i^2 = -5 + 10i
        Offset z1 = new Offset(1, 2);
        Offset z2 = new Offset(3, 4);

        //Act
        Offset product = Offset.ComplexMultiply(z1, z2);

        //Assert
        product.Should().Be(new Offset(-5, 10));
    }

    [Fact]
    public void angle_degrees_measures_from_the_positive_x_axis()
    {
        //Arrange / Act / Assert
        new Offset(1, 0).AngleDegrees().Should().BeApproximately(0.0, 1e-9);
        new Offset(0, 1).AngleDegrees().Should().BeApproximately(90.0, 1e-9);
        new Offset(-1, 0).AngleDegrees().Should().BeApproximately(180.0, 1e-9);
        new Offset(0, -1).AngleDegrees().Should().BeApproximately(-90.0, 1e-9);
    }

    [Fact]
    public void rotating_by_ninety_degrees_maps_x_onto_y()
    {
        //Arrange / Act
        Offset rotated = new Offset(1, 0).Rotated(90);

        //Assert
        rotated.X.Should().BeApproximately(0.0, 1e-12);
        rotated.Y.Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void is_sane_rejects_nan()
    {
        //Arrange / Act / Assert
        new Offset(1, 2).IsSane.Should().BeTrue();
        new Offset(double.NaN, 2).IsSane.Should().BeFalse();
    }

    [Fact]
    public void directed_groups_the_degree_conversion_the_way_upstream_writes_it()
    {
        //Arrange
        // upstream: `sin ((90 - angle) * M_PI / 180.0)', which C evaluates LEFT TO
        // RIGHT -- `(x * M_PI) / 180.0'. Folding the constant into `x * (M_PI / 180.0)'
        // is a DIFFERENT floating-point expression, and -357 (folded to +3) is one of
        // the 208 whole-degree angles in (-360, 360) where the two disagree.
        double upstreamGrouping = Math.Sin(3.0 * Math.PI / 180.0);
        double foldedConstant = Math.Sin(3.0 * (Math.PI / 180.0));

        //Act
        Offset directed = Offset.Directed(-357.0);

        //Assert
        // THE CONTROL, and it carries the whole point: if these two ever became equal
        // this fence would pass with the grouping wrong, so it is asserted rather than
        // assumed.
        foldedConstant.Should().NotBe(upstreamGrouping);
        directed.Y.Should().Be(upstreamGrouping);
        directed.Y.Should().NotBe(foldedConstant);
    }

    [Fact]
    public void directed_gives_exactly_equal_magnitudes_at_odd_multiples_of_45()
    {
        //Arrange / Act / Assert
        // upstream's comment says this is what the all-sines arrangement buys, "at the
        // cost of losing some less obvious invariants" -- so it is the invariant to
        // fence, and it is exact equality, not a tolerance.
        foreach (double angle in new[] { 45.0, 135.0, -45.0, -135.0, 225.0, -225.0 })
        {
            Offset d = Offset.Directed(angle);
            Math.Abs(d.X).Should().Be(Math.Abs(d.Y));
        }

        // THE CONTROL: an angle that is NOT an odd multiple of 45 must come out with
        // unequal magnitudes, or the assertion above would pass on a broken Directed
        // that always answered the same number twice.
        Offset control = Offset.Directed(30.0);
        Math.Abs(control.X).Should().NotBe(Math.Abs(control.Y));
    }

    [Fact]
    public void directed_produces_no_negative_zero_at_the_quadrant_handovers()
    {
        //Arrange / Act / Assert
        // upstream: "Sign of the sine is chosen to avoid -0.0 in results." A -0.0 in a
        // transform's xy term is invisible in every printed form and changes the sign of
        // a product, so it is the kind of thing only an explicit fence catches. What the
        // arrangement buys is the HANDOVERS -- the angles where a component is zero.
        foreach (double angle in new[] { 0.0, 90.0, 180.0, -90.0, -180.0, 360.0 })
        {
            Offset d = Offset.Directed(angle);
            IsNegativeZero(d.X).Should().BeFalse();
            IsNegativeZero(d.Y).Should().BeFalse();
        }
    }

    [Fact]
    public void directed_of_minus_360_does_produce_a_negative_zero_and_upstream_does_too()
    {
        //Arrange / Act
        Offset d = Offset.Directed(-360.0);

        //Assert
        // THE ONE ANGLE THE ARRANGEMENT DOES NOT COVER, and it is not a port defect:
        // `fmod' keeps the sign of its FIRST argument, so folding -360 gives -0.0 rather
        // than 0.0, and every later test (-0.0 <= -180, -0.0 > 180, -0.0 > 0) is false,
        // so the angle reaches `sin (-0.0 * M_PI / 180.0)' -- which is -0.0. Upstream
        // takes the identical path and answers the identical pair. Fenced BECAUSE it
        // looks like a defect: a later reader tempted to "fix" it would be diverging.
        d.X.Should().Be(1.0);
        IsNegativeZero(d.Y).Should().BeTrue();

        // THE CONTROL: +360 folds to +0.0 and comes out the other way.
        IsNegativeZero(Offset.Directed(360.0).Y).Should().BeFalse();
    }

    private static bool IsNegativeZero(double value)
        => value == 0.0 && double.IsNegative(value);

    [Fact]
    public void directed_folds_an_out_of_range_angle_onto_its_in_range_twin()
    {
        //Arrange / Act / Assert
        // The folding is `fmod' (truncating), then one adjustment into (-180, 180].
        // Math.IEEERemainder rounds to nearest and would fold 359 to -1 by a different
        // route, so the two are not interchangeable however alike the names look.
        Offset.Directed(363.0).X.Should().Be(Offset.Directed(3.0).X);
        Offset.Directed(363.0).Y.Should().Be(Offset.Directed(3.0).Y);
        Offset.Directed(-357.0).X.Should().Be(Offset.Directed(3.0).X);
        Offset.Directed(-357.0).Y.Should().Be(Offset.Directed(3.0).Y);

        // THE CONTROL: a fold that collapsed everything onto one answer would pass the
        // three above.
        Offset.Directed(363.0).Y.Should().NotBe(Offset.Directed(7.0).Y);
    }
}

public class PolynomialTests
{
    [Fact]
    public void evaluates_by_horners_method()
    {
        //Arrange -- 1 + 2x + 3x^2
        Polynomial p = new Polynomial(new double[] { 1, 2, 3 });

        //Act / Assert
        p.Evaluate(0).Should().Be(1.0);
        p.Evaluate(1).Should().Be(6.0);
        p.Evaluate(2).Should().Be(17.0);
    }

    [Fact]
    public void degree_is_one_less_than_the_coefficient_count()
        => new Polynomial(new double[] { 1, 2, 3 }).Degree.Should().Be(2);

    [Fact]
    public void multiplication_convolves_the_coefficients()
    {
        //Arrange -- (1 + x)(1 + x) = 1 + 2x + x^2
        Polynomial p = new Polynomial(1, 1);

        //Act
        Polynomial product = Polynomial.Multiply(p, p);

        //Assert
        product.Coefficients.Count.Should().Be(3);
        product.Coefficients[0].Should().Be(1.0);
        product.Coefficients[1].Should().Be(2.0);
        product.Coefficients[2].Should().Be(1.0);
    }

    [Fact]
    public void power_repeats_multiplication()
    {
        //Arrange -- (1 + x)^3 = 1 + 3x + 3x^2 + x^3
        Polynomial cubed = Polynomial.Power(3, new Polynomial(1, 1));

        //Act / Assert
        cubed.Coefficients[0].Should().Be(1.0);
        cubed.Coefficients[1].Should().Be(3.0);
        cubed.Coefficients[2].Should().Be(3.0);
        cubed.Coefficients[3].Should().Be(1.0);
    }

    [Fact]
    public void differentiation_lowers_the_degree()
    {
        //Arrange -- d/dx (1 + 2x + 3x^2) = 2 + 6x
        Polynomial p = new Polynomial(new double[] { 1, 2, 3 });

        //Act
        p.Differentiate();

        //Assert
        p.Coefficients.Count.Should().Be(2);
        p.Coefficients[0].Should().Be(2.0);
        p.Coefficients[1].Should().Be(6.0);
    }

    [Fact]
    public void solves_a_linear_polynomial()
    {
        //Arrange -- 2x - 4 = 0
        Polynomial p = new Polynomial(-4, 2);

        //Act
        List<double> roots = p.Solve();

        //Assert
        roots.Count.Should().Be(1);
        roots[0].Should().BeApproximately(2.0, 1e-12);
    }

    [Fact]
    public void solves_a_quadratic_with_two_real_roots()
    {
        //Arrange -- x^2 - 5x + 6 = 0, roots 2 and 3
        Polynomial p = new Polynomial(new double[] { 6, -5, 1 });

        //Act
        List<double> roots = p.Solve();
        roots.Sort();

        //Assert
        roots.Count.Should().Be(2);
        roots[0].Should().BeApproximately(2.0, 1e-9);
        roots[1].Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void a_quadratic_with_no_real_roots_yields_nothing()
    {
        //Arrange -- x^2 + 1 = 0
        Polynomial p = new Polynomial(new double[] { 1, 0, 1 });

        //Act / Assert
        p.Solve().Count.Should().Be(0);
    }

    [Fact]
    public void solves_a_cubic_with_three_real_roots()
    {
        //Arrange -- (x-1)(x-2)(x-3) = -6 + 11x - 6x^2 + x^3
        //This is the casus irreducibilis branch of Cardano's formula
        Polynomial p = new Polynomial(new double[] { -6, 11, -6, 1 });

        //Act
        List<double> roots = p.Solve();
        roots.Sort();

        //Assert
        roots.Count.Should().Be(3);
        roots[0].Should().BeApproximately(1.0, 1e-9);
        roots[1].Should().BeApproximately(2.0, 1e-9);
        roots[2].Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void solves_a_cubic_with_one_real_root()
    {
        //Arrange -- x^3 + x + 1 = 0 has a single real root near -0.6823
        Polynomial p = new Polynomial(new double[] { 1, 1, 0, 1 });

        //Act
        List<double> roots = p.Solve();

        //Assert
        roots.Count.Should().Be(1);
        p.Evaluate(roots[0]).Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void solves_a_cubic_with_a_triple_root()
    {
        //Arrange -- x^3 = 0
        Polynomial p = new Polynomial(new double[] { 0, 0, 0, 1 });

        //Act
        List<double> roots = p.Solve();

        //Assert
        roots.Count.Should().Be(3);
        foreach (double root in roots)
        {
            root.Should().BeApproximately(0.0, 1e-12);
        }
    }

    [Fact]
    public void clean_drops_a_negligible_leading_coefficient_relatively()
    {
        //Arrange -- the x^2 term is tiny relative to the x term, so it goes
        Polynomial p = new Polynomial(new double[] { 1, 1, 1e-15 });

        //Act
        p.Clean();

        //Assert
        p.Degree.Should().Be(1);
    }

    [Fact]
    public void addition_and_subtraction_align_by_degree()
    {
        //Arrange
        Polynomial a = new Polynomial(new double[] { 1, 2, 3 });
        Polynomial b = new Polynomial(new double[] { 10, 20 });

        //Act
        Polynomial sum = a + b;
        Polynomial difference = a - b;

        //Assert
        sum.Coefficients[0].Should().Be(11.0);
        sum.Coefficients[1].Should().Be(22.0);
        sum.Coefficients[2].Should().Be(3.0);
        difference.Coefficients[0].Should().Be(-9.0);
    }

    // Offset::is_sane rejects NaN AND infinity -- four tests, not two. Expected values
    // read off flower/offset.cc:124-129, not off this port. The finite case is the
    // CONTROL: without it "everything is insane" would pass every infinity case here.
    [Fact]
    public void is_sane_accepts_a_finite_offset()
    {
        //Arrange / Act
        Offset finite = new Offset(-3.5, 1e9);

        //Assert
        finite.IsSane.Should().BeTrue();
    }

    [Fact]
    public void is_sane_rejects_a_not_a_number_coordinate_on_either_axis()
    {
        //Arrange / Act / Assert
        new Offset(double.NaN, 0.0).IsSane.Should().BeFalse();
        new Offset(0.0, double.NaN).IsSane.Should().BeFalse();
    }

    [Fact]
    public void is_sane_rejects_an_infinite_coordinate_on_either_axis()
    {
        //Arrange / Act / Assert
        // A skyline's outermost buildings run to infinity by construction, so this is
        // the half that decides whether they reach the output as line ends.
        new Offset(double.PositiveInfinity, 0.0).IsSane.Should().BeFalse();
        new Offset(double.NegativeInfinity, 0.0).IsSane.Should().BeFalse();
        new Offset(0.0, double.PositiveInfinity).IsSane.Should().BeFalse();
        new Offset(0.0, double.NegativeInfinity).IsSane.Should().BeFalse();
    }
}
