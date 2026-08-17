// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <c>Staff_symbol_referencer::internal_get_position</c>'s two readings, which differ in
/// exactly one term — the ORDINARY vertical coordinate against the PURE one.
/// <para>
/// The distinction is not about the NUMBER. For a grob with no separate pure callback the
/// two agree, and the control below is that fact; it is why the port answered the ordinary
/// position from <c>PureGetPosition</c> for as long as it did without anything going red.
/// What differs is what ASKING COSTS: an ordinary vertical read forces
/// <c>Y-parent-positioning</c>, and through it the alignment's <c>positioning-done</c>,
/// from inside a callback that runs BEFORE line breaking. <c>Dot_column</c> is upstream's
/// own reason for the distinction, and <c>dot-column-vertical-positioning.ly</c> is the
/// regression test that fails when a port gets it wrong — by that name.
/// </para>
/// </summary>
public class StaffSymbolReferencerTests
{
    private static readonly Symbol StaffSymbolSymbol = Symbol.Intern("staff-symbol");
    private static readonly Symbol StaffSpaceSymbol = Symbol.Intern("staff-space");
    private static readonly Symbol YOffsetSymbol = Symbol.Intern("Y-offset");

    /// <summary>
    /// A staff symbol and a grob measured against it, with the grob parented ON the staff
    /// so that the two share a common Y reference point and the staff's own coordinate —
    /// the term upstream subtracts — is zero.
    /// </summary>
    private static (Grob Staff, Grob Referencer) Fixture(object yOffset, double staffSpace = 1.0)
    {
        Grob staff = SpacingFixtures.NewSpacingGrob(("staff-space", staffSpace));
        Grob referencer = SpacingFixtures.NewSpacingGrob(("Y-offset", yOffset));

        referencer.SetParent(staff, Axis.Y);
        referencer.SetObject(StaffSymbolSymbol, staff);
        return (staff, referencer);
    }

    [Fact]
    public void the_pure_position_reads_the_pure_half_of_an_unpure_pure_container()
    {
        //Arrange
        // Hand-computed from internal_get_position: 2.0 * (pure y - staff y) / staff space,
        // with the staff's own coordinate zero and a staff space of one, so 2 * 0.5 = 1.0.
        (Grob _, Grob referencer) = Fixture(new UnpurePureContainer(1.5, 0.5));

        //Act
        double position = StaffSymbolReferencer.PureGetPosition(referencer);

        //Assert
        position.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void the_ordinary_position_reads_the_unpure_half_of_the_same_container()
    {
        //Arrange
        // The SAME shape, on a fresh grob: an offset is cached the first time it is read,
        // and after an ordinary read the pure walk finds that cache and reuses it — upstream
        // caches identically, so the two readings are taken on two grobs rather than one.
        (Grob _, Grob referencer) = Fixture(new UnpurePureContainer(1.5, 0.5));

        //Act
        double position = StaffSymbolReferencer.GetPosition(referencer);

        //Assert
        position.Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void a_plain_number_offset_makes_both_readings_agree()
    {
        //Arrange
        // THE CONTROL, and the whole reason this defect survived: with no pure/unpure split
        // in the chain the two readings answer the same number, so a pure reader that took
        // the ordinary branch looked right everywhere the split does not exist.
        (Grob _, Grob pureSide) = Fixture(1.5);
        (Grob _, Grob ordinarySide) = Fixture(1.5);

        //Act
        double pure = StaffSymbolReferencer.PureGetPosition(pureSide);
        double ordinary = StaffSymbolReferencer.GetPosition(ordinarySide);

        //Assert
        pure.Should().BeApproximately(3.0, 1e-9);
        ordinary.Should().BeApproximately(pure, 1e-9);
    }

    [Fact]
    public void both_readings_are_measured_in_the_staffs_own_staff_space()
    {
        //Arrange
        // The divisor is the STAFF's staff space, not the referencer's: on a staff of
        // double spacing the same 0.5 offset is half a position, not one.
        (Grob _, Grob referencer) = Fixture(new UnpurePureContainer(1.5, 0.5), 2.0);

        //Act
        double position = StaffSymbolReferencer.PureGetPosition(referencer);

        //Assert
        position.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void the_rounded_pure_position_rounds_the_pure_reading_and_not_the_ordinary_one()
    {
        //Arrange
        // 2 * 0.6 = 1.2 rounds to 1; the ordinary reading of the same grob is 2 * 1.5 = 3.0,
        // which rounds to 3 — so a rounded position that answers 3 is reading the wrong half.
        (Grob _, Grob referencer) = Fixture(new UnpurePureContainer(1.5, 0.6));

        //Act
        int rounded = StaffSymbolReferencer.PureGetRoundedPosition(referencer);

        //Assert
        rounded.Should().Be(1);
    }
}
