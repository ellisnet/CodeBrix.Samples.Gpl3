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

// was previously: flower/test-direction.cc, flower/test-drul-array.cc
// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port:
//   - translated from C++/yaffut to C#/xUnit v3 with SilverAssertions

using System;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Flower.Tests;

public class DirectionTests
{
    [Fact]
    public void default_direction_is_zero()
        => default(Direction).Should().Be(Direction.Zero);

    [Fact]
    public void integers_collapse_to_their_sign()
    {
        //Arrange / Act / Assert
        new Direction(-5L).Should().Be(Direction.Negative);
        new Direction(-1L).Should().Be(Direction.Negative);
        new Direction(0L).Should().Be(Direction.Zero);
        new Direction(1L).Should().Be(Direction.Positive);
        new Direction(5L).Should().Be(Direction.Positive);
    }

    [Fact]
    public void reals_collapse_to_their_sign_including_the_edge_cases()
    {
        //Arrange / Act / Assert -- upstream's init_float test
        new Direction(-5.5).Should().Be(Direction.Negative);
        new Direction(double.NegativeInfinity).Should().Be(Direction.Negative);

        //Both zeroes are ZERO, including negative zero
        new Direction(-0.0).Should().Be(Direction.Zero);
        new Direction(0.0).Should().Be(Direction.Zero);

        new Direction(5.5).Should().Be(Direction.Positive);
        new Direction(double.PositiveInfinity).Should().Be(Direction.Positive);
    }

    [Fact]
    public void nan_direction_follows_the_sign_bit_not_the_platform_constant()
    {
        //Arrange -- THIS IS A REAL C-TO-.NET PORTING HAZARD.
        //
        //Upstream's test asserts Direction(NAN) == POSITIVE and
        //Direction(-NAN) == NEGATIVE. That holds in C because the NAN macro is
        //POSITIVE-signed: signbit(NAN) is 0.
        //
        //.NET's double.NaN is the opposite -- its bits are 0xFFF8000000000000, so
        //double.IsNegative(double.NaN) is TRUE. Writing the upstream assertion
        //literally with double.NaN would therefore fail, not because the port is
        //wrong but because the two languages' default NaN constants differ in sign.
        //
        //The contract the implementation actually honours, in both languages, is
        //"follow the sign bit". These assertions state that directly.
        double positiveNaN = BitConverter.Int64BitsToDouble(0x7FF8000000000000L);
        double negativeNaN = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000000UL));

        //Act / Assert
        new Direction(positiveNaN).Should().Be(Direction.Positive);
        new Direction(negativeNaN).Should().Be(Direction.Negative);

        //And the platform constants, recorded so the difference is not re-discovered
        double.IsNegative(double.NaN).Should().BeTrue();
        new Direction(double.NaN).Should().Be(Direction.Negative);
    }

    [Fact]
    public void negation_reverses_and_leaves_zero_alone()
    {
        //Arrange / Act / Assert
        (-Direction.Positive).Should().Be(Direction.Negative);
        (-Direction.Negative).Should().Be(Direction.Positive);
        (-Direction.Zero).Should().Be(Direction.Zero);
    }

    [Fact]
    public void multiplication_combines_signs()
    {
        //Arrange / Act / Assert
        (Direction.Positive * Direction.Positive).Should().Be(Direction.Positive);
        (Direction.Negative * Direction.Negative).Should().Be(Direction.Positive);
        (Direction.Positive * Direction.Negative).Should().Be(Direction.Negative);
        (Direction.Positive * Direction.Zero).Should().Be(Direction.Zero);
    }

    [Fact]
    public void directed_same_and_opposite_agree_with_the_product()
    {
        //Arrange / Act / Assert
        Direction.DirectedSame(Direction.Positive, Direction.Positive).Should().BeTrue();
        Direction.DirectedSame(Direction.Negative, Direction.Negative).Should().BeTrue();
        Direction.DirectedOpposite(Direction.Positive, Direction.Negative).Should().BeTrue();

        //Zero is neither same nor opposite
        Direction.DirectedSame(Direction.Positive, Direction.Zero).Should().BeFalse();
        Direction.DirectedOpposite(Direction.Positive, Direction.Zero).Should().BeFalse();
    }

    [Fact]
    public void minmax_selects_by_direction()
    {
        //Arrange / Act / Assert -- positive picks the max, anything else the min
        Direction.MinMax(Direction.Positive, 3, 7).Should().Be(7);
        Direction.MinMax(Direction.Negative, 3, 7).Should().Be(3);
        Direction.MinMax(Direction.Zero, 3, 7).Should().Be(3);
    }

    [Fact]
    public void to_index_maps_the_three_directions_to_zero_one_two()
    {
        //Arrange / Act / Assert
        Direction.Negative.ToIndex.Should().Be(0);
        Direction.Zero.ToIndex.Should().Be(1);
        Direction.Positive.ToIndex.Should().Be(2);
    }

    [Fact]
    public void converts_implicitly_to_int()
    {
        //Arrange / Act
        int value = Direction.Negative;

        //Assert
        value.Should().Be(-1);
    }
}

public class DrulArrayTests
{
    [Fact]
    public void indexes_by_direction()
    {
        //Arrange
        DrulArray<string> array = new DrulArray<string>("down", "up");

        //Act / Assert
        array[Direction.Negative].Should().Be("down");
        array[Direction.Positive].Should().Be("up");
    }

    [Fact]
    public void assignment_by_direction_updates_the_right_side()
    {
        //Arrange
        DrulArray<int> array = new DrulArray<int>(1, 2);

        //Act
        array[Direction.Negative] = 10;
        array[Direction.Positive] = 20;

        //Assert
        array.Negative.Should().Be(10);
        array.Positive.Should().Be(20);
    }

    [Fact]
    public void swap_exchanges_the_two_sides()
    {
        //Arrange
        DrulArray<int> array = new DrulArray<int>(1, 2);

        //Act
        array.Swap();

        //Assert
        array.Negative.Should().Be(2);
        array.Positive.Should().Be(1);
    }

    [Fact]
    public void equality_compares_both_sides()
    {
        //Arrange / Act / Assert
        new DrulArray<int>(1, 2).Should().Be(new DrulArray<int>(1, 2));
        (new DrulArray<int>(1, 2) == new DrulArray<int>(1, 3)).Should().BeFalse();
    }

    [Fact]
    public void a_centre_direction_reads_the_negative_side()
    {
        //Arrange -- upstream asserts on a centre direction; we resolve it to the
        //negative side rather than throwing, since the indexer has no way to signal

        //Act
        DrulArray<int> array = new DrulArray<int>(1, 2);

        //Assert
        array[Direction.Zero].Should().Be(1);
    }
}
