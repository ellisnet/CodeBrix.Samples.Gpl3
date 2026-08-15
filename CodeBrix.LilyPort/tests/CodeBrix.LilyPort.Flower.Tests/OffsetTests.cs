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
