// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// PARITY 4 (2026-08-14): the Scheme boundary of a skyline pair, which upstream crosses
/// BY VALUE.
/// <para>
/// The defect these fence: <c>scm_conversions&lt;Skyline_pair&gt;::from_scm</c>
/// (<c>lily/lily-guile.cc</c>) ends <c>return Skyline_pair (*left, *right);</c> — it
/// DEREFERENCES both smobs, and <c>Skyline_pair (Skyline, Skyline)</c> takes its two
/// arguments by value, so what a caller receives is a private copy. Nothing it does to
/// that copy can reach the grob's stored skylines. <see cref="Skyline"/> is a CLASS in
/// this port, so returning the stored instances handed every caller an ALIAS instead —
/// and the callers are all of the form "read, translate into a common refpoint, then
/// measure" (side-position-interface, axis-group skyline combination and outside-staff
/// placement, align-interface, horizontal spacing). Each of them was permanently
/// moving the skylines it had only meant to measure. Trap 19.
/// </para>
/// <para>
/// The relationship asserted, not a recorded value: two independent reads of one stored
/// pair must not be able to move each other, and the stored pair must not move either.
/// Each is paired with the control that the read which WAS shifted really did move — a
/// <c>Shift</c> that quietly did nothing would satisfy every "unchanged" half on its
/// own.
/// </para>
/// </summary>
public class SkylinePairTests
{
    private const double Probe = 0.5;
    private const double Far = 10.0;

    private static SkylinePair StoredPair()
        => new SkylinePair(
            new Box(new Interval(0.0, 1.0), new Interval(-2.0, 3.0)), Axis.X);

    [Fact]
    public void reading_a_pair_out_of_scheme_answers_a_copy_the_caller_may_move()
    {
        //Arrange
        object stored = StoredPair().ToScheme();
        SkylinePair first = SkylinePair.FromScheme(stored);
        SkylinePair second = SkylinePair.FromScheme(stored);

        //Act
        second.Shift(Far);

        //Assert
        // The control: the read that was shifted must really have moved, or every
        // "unchanged" assertion below is satisfied by a Shift that does nothing.
        second.Up.Height(Probe).Should().Be(double.NegativeInfinity);
        second.Up.Height(Probe + Far).Should().Be(3.0);

        // The other read, and the pair still sitting in the property, are untouched.
        first.Up.Height(Probe).Should().Be(3.0);
        SkylinePair.FromScheme(stored).Up.Height(Probe).Should().Be(3.0);
    }

    [Fact]
    public void raising_a_pair_read_out_of_scheme_leaves_the_stored_pair_alone()
    {
        //Arrange
        object stored = StoredPair().ToScheme();
        SkylinePair read = SkylinePair.FromScheme(stored);

        //Act
        read.Raise(Far);

        //Assert
        // Control first: raising moved the copy's roof by exactly the amount asked for.
        read.Up.Height(Probe).Should().Be(3.0 + Far);

        // And the stored pair kept the roof it was built with.
        SkylinePair.FromScheme(stored).Up.Height(Probe).Should().Be(3.0);
    }
}
