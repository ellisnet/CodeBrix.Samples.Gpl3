/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2020--2026 Daniel Eble <nine.fierce.ballads@gmail.com>

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

// was previously: flower/test-rational.cc
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - translated from C++/yaffut to C#/xUnit v3 with SilverAssertions
//   - the assertions and the cases they cover are upstream's; testing this port
//     against LilyPond's own test data is the point, since it verifies the
//     Rational edge cases (infinities, NaN, sign handling) rather than my reading
//     of them

using System;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Flower.Tests;

public class RationalTests
{
    [Fact]
    public void init_default_is_zero()
    {
        //Arrange / Act
        Rational r = default;

        //Assert
        r.IsFinite.Should().BeTrue();
        r.IsNonZero.Should().BeFalse();
        r.Numerator.Should().Be(0L);
        r.Denominator.Should().Be(1L);
    }

    [Fact]
    public void init_zero_over_zero_is_nan()
    {
        //Arrange / Act
        Rational r = new Rational(0, 0);

        //Assert -- upstream notes the sign here is merely what the implementation does
        r.IsNegative.Should().BeFalse();
        r.IsFinite.Should().BeFalse();
        r.IsInfinite.Should().BeFalse();
        r.IsNaN.Should().BeTrue();
        r.IsNonZero.Should().BeTrue();
        double.IsNaN(r.ToDouble()).Should().BeTrue();
    }

    [Fact]
    public void init_positive_over_zero_is_positive_infinity()
    {
        //Arrange / Act
        Rational r = new Rational(123, 0);

        //Assert
        r.IsNegative.Should().BeFalse();
        r.IsInfinite.Should().BeTrue();
        double.IsInfinity(r.ToDouble()).Should().BeTrue();
        double.IsNegative(r.ToDouble()).Should().BeFalse();
    }

    [Fact]
    public void init_negative_over_zero_is_negative_infinity()
    {
        //Arrange / Act
        Rational r = new Rational(-123, 0);

        //Assert
        r.IsNegative.Should().BeTrue();
        r.IsInfinite.Should().BeTrue();
        double.IsNegativeInfinity(r.ToDouble()).Should().BeTrue();
    }

    [Fact]
    public void infinity_is_positive_and_infinite()
    {
        //Arrange / Act
        Rational r = Rational.Infinity;

        //Assert
        r.IsNegative.Should().BeFalse();
        r.IsInfinite.Should().BeTrue();
        (r == Rational.FromDouble(double.PositiveInfinity)).Should().BeTrue();
    }

    [Fact]
    public void negated_infinity_is_negative_and_infinite()
    {
        //Arrange / Act
        Rational r = -Rational.Infinity;

        //Assert
        r.IsNegative.Should().BeTrue();
        r.IsInfinite.Should().BeTrue();
        (r == Rational.FromDouble(double.NegativeInfinity)).Should().BeTrue();
    }

    [Fact]
    public void nan_carries_a_sign_but_is_not_finite()
    {
        //Arrange / Act
        Rational positive = Rational.NaN;
        Rational negative = -Rational.NaN;

        //Assert
        positive.IsNaN.Should().BeTrue();
        positive.IsNegative.Should().BeFalse();
        negative.IsNaN.Should().BeTrue();
        negative.IsNegative.Should().BeTrue();
    }

    [Fact]
    public void addition_follows_upstreams_infinity_and_nan_rules()
    {
        //Arrange
        Rational r = new Rational(1, 2);
        Rational s = new Rational(2, 3);
        Rational z = new Rational(0);
        Rational infinity = Rational.Infinity;
        Rational nan = Rational.NaN;

        //Act / Assert
        (r + s).Should().Be(new Rational(7, 6));
        (r + z).Should().Be(r);
        (z + r).Should().Be(r);
        (z + infinity).Should().Be(infinity);
        (infinity + z).Should().Be(infinity);

        //Opposite infinities cancel to NaN
        (-infinity + infinity).IsNaN.Should().BeTrue();
        (infinity + -infinity).IsNaN.Should().BeTrue();

        //NaN is absorbing from either side
        (nan + r).IsNaN.Should().BeTrue();
        (r + nan).IsNaN.Should().BeTrue();
        (nan + z).IsNaN.Should().BeTrue();
        (z + nan).IsNaN.Should().BeTrue();
        (nan + -infinity).IsNaN.Should().BeTrue();
        (-infinity + nan).IsNaN.Should().BeTrue();
        (nan + infinity).IsNaN.Should().BeTrue();
        (infinity + nan).IsNaN.Should().BeTrue();
        (nan + nan).IsNaN.Should().BeTrue();
    }

    [Fact]
    public void multiplication_handles_infinities()
    {
        //Arrange
        Rational half = new Rational(1, 2);
        Rational infinity = Rational.Infinity;

        //Act / Assert
        (half * new Rational(2, 3)).Should().Be(new Rational(1, 3));
        (half * infinity).IsInfinite.Should().BeTrue();
        (half * infinity).IsNegative.Should().BeFalse();
        ((-half) * infinity).IsNegative.Should().BeTrue();
    }

    [Fact]
    public void division_by_infinity_is_zero()
    {
        //Arrange / Act
        Rational result = new Rational(3, 4) / Rational.Infinity;

        //Assert
        result.IsNonZero.Should().BeFalse();
    }

    [Fact]
    public void division_reduces_to_lowest_terms()
        => (new Rational(3, 4) / new Rational(6, 8)).Should().Be(Rational.One);

    [Fact]
    public void arithmetic_stays_exact_across_many_operations()
    {
        //Arrange -- a run of thirds and sevenths that binary floating point cannot
        //represent; musical durations depend on this staying exact
        Rational total = Rational.Zero;

        //Act
        for (int i = 0; i < 21; i++)
        {
            total += new Rational(1, 21);
        }

        //Assert
        total.Should().Be(Rational.One);
    }

    [Theory]
    [InlineData(52, 1, 17, 1, 1, 1)]
    [InlineData(5, 4, 1, 5, 1, 20)]
    [InlineData(-1, 4, 1, 1, 3, 4)]
    // a negative divisor has its sign ignored
    [InlineData(5, 4, -1, 5, 1, 20)]
    [InlineData(-1, 4, -1, 1, 3, 4)]
    // zero modulo anything non-zero is zero
    [InlineData(0, 1, 123, 1, 0, 1)]
    public void euclidean_remainder_matches_upstream_cases(
        int dividendNumerator,
        int dividendDenominator,
        int divisorNumerator,
        int divisorDenominator,
        int expectedNumerator,
        int expectedDenominator)
    {
        //Arrange
        Rational dividend = new Rational(dividendNumerator, dividendDenominator);
        Rational divisor = new Rational(divisorNumerator, divisorDenominator);
        Rational expected = new Rational(expectedNumerator, expectedDenominator);

        //Act
        Rational remainder = Rational.EuclideanRemainder(dividend, divisor);

        //Assert
        remainder.Should().Be(expected);
    }

    [Fact]
    public void euclidean_remainder_by_a_non_finite_divisor_is_nan()
    {
        //Arrange / Act / Assert -- upstream requires NaN for every infinite divisor
        Rational.EuclideanRemainder(new Rational(-2), -Rational.Infinity).IsNaN.Should().BeTrue();
        Rational.EuclideanRemainder(new Rational(-2), Rational.Infinity).IsNaN.Should().BeTrue();
        Rational.EuclideanRemainder(Rational.Zero, Rational.Infinity).IsNaN.Should().BeTrue();
        Rational.EuclideanRemainder(new Rational(3), Rational.Infinity).IsNaN.Should().BeTrue();

        //zero over zero is NaN, and NaN is not finite, so this is NaN too
        Rational.EuclideanRemainder(Rational.Zero, Rational.Zero).IsNaN.Should().BeTrue();
    }

    [Fact]
    public void truncated_integer_matches_integer_division()
    {
        //Arrange / Act / Assert -- upstream loops -6..6 over halves and thirds
        for (int i = -6; i <= 6; i++)
        {
            new Rational(i, 2).TruncatedInteger().Should().Be(i / 2);
            new Rational(i, 3).TruncatedInteger().Should().Be(i / 3);
        }
    }

    [Fact]
    public void truncated_integer_handles_very_large_values()
    {
        //Arrange -- upstream uses lowest+1 because the true lowest trips UBSan there
        long low = long.MinValue + 1;
        long high = long.MaxValue;

        //Act / Assert
        for (int i = 1; i <= 3; i++)
        {
            new Rational(low, i).TruncatedInteger().Should().Be(low / i);
            new Rational(high, i).TruncatedInteger().Should().Be(high / i);
        }
    }

    [Fact]
    public void comparison_orders_negative_infinity_below_everything_finite()
    {
        //Arrange
        Rational r = new Rational(1, 2);

        //Act / Assert
        (-Rational.Infinity < r).Should().BeTrue();
        (r < Rational.Infinity).Should().BeTrue();
        (-Rational.Infinity < Rational.Infinity).Should().BeTrue();
    }

    [Fact]
    public void equal_infinities_compare_equal()
        => Rational.Compare(Rational.Infinity, Rational.Infinity).Should().Be(0);

    [Fact]
    public void comparison_is_exact_for_close_values()
    {
        //Arrange -- 1/3 and 100000000/300000001 differ by a hair
        Rational a = new Rational(1, 3);
        Rational b = new Rational(100000000, 300000001);

        //Act / Assert
        (b < a).Should().BeTrue();
    }

    [Fact]
    public void from_double_round_trips_a_dyadic_value()
    {
        //Arrange / Act
        Rational r = Rational.FromDouble(0.375);

        //Assert -- 0.375 is exactly 3/8
        r.Should().Be(new Rational(3, 8));
    }

    [Fact]
    public void to_string_matches_upstream_formatting()
    {
        //Arrange / Act / Assert
        new Rational(7, 6).ToString().Should().Be("7/6");
        new Rational(4, 2).ToString().Should().Be("2");
        new Rational(0).ToString().Should().Be("0");
        Rational.Infinity.ToString().Should().Be("infinity");
        (-Rational.Infinity).ToString().Should().Be("-infinity");
        Rational.NaN.ToString().Should().Be("nan");
        (-Rational.NaN).ToString().Should().Be("-nan");
    }

    [Fact]
    public void a_negative_denominator_moves_the_sign_to_the_numerator()
    {
        //Arrange / Act
        Rational r = new Rational(1, -2);

        //Assert
        r.Numerator.Should().Be(-1L);
        r.Denominator.Should().Be(2L);
    }
}
