// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Runtime.InteropServices;
using CodeBrix.LilyPort.Engine.Layout;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The storage seam under <c>Page_layout_problem::append_system</c>: what upstream's
/// <c>springs_.back ().foo ()</c> means once <c>Spring</c> is a C# STRUCT.
/// </summary>
/// <remarks>
/// <para>
/// These exist because three mutations in <c>append_system</c> were silently discarded
/// for the whole life of EPG16 — the "leave room for loose lines" floor, the
/// <c>ensure_min_distance</c> on EVERY spring between two staves, and the entire
/// <c>alignment-distances</c> block, which meant a score's explicitly requested staff
/// offsets were ignored outright.
/// </para>
/// <para>
/// ⚠ THE DEFECT COMPILED WITH ZERO WARNINGS AND CHANGED A REAL VALUE — just not the one
/// in the list. That is why the fact below is paired with a control: an implementation
/// that "sensibly" made the indexer hand back a reference would pass a test that only
/// asserted the ref case, and an implementation that quietly turned <c>Spring</c> into a
/// class would pass one that only asserted the copy case. The two together pin the
/// language seam the port actually stands on, in both directions.
/// </para>
/// <para>
/// This is EPG21's <c>Bezier</c>-is-a-class finding in the mirror. There it was a C++
/// value being aliased by a C# reference; here it is a C++ reference being copied by a
/// C# value. Both are silent, and neither is visible in the diff of a faithful port.
/// </para>
/// </remarks>
public class SpringStorageTests
{
    /// <summary>
    /// THE CONTROL, and it is the defect: mutating through a <see cref="List{T}"/>
    /// indexer changes a temporary copy and leaves the stored spring alone.
    /// </summary>
    [Fact]
    public void mutating_a_stored_spring_through_the_list_indexer_is_discarded()
    {
        //Arrange
        List<Spring> springs = new List<Spring> { new Spring(1.0, 0.0) };

        //Act
        springs[0].EnsureMinDistance(5.0);

        //Assert
        // The mutation went to a copy. If this ever comes out as 5.0, Spring has stopped
        // being a struct and PageLayoutProblem.LastSpring() has become unnecessary --
        // which is a change to review, not a change to absorb.
        springs[0].MinDistance.Should().Be(0.0);
    }

    /// <summary>
    /// THE FACT: mutating through a span reference — which is what
    /// <c>PageLayoutProblem.LastSpring()</c> answers — changes the stored spring, the
    /// way upstream's <c>springs_.back ()</c> does.
    /// </summary>
    [Fact]
    public void mutating_a_stored_spring_through_a_span_reference_is_kept()
    {
        //Arrange
        List<Spring> springs = new List<Spring> { new Spring(1.0, 0.0) };

        //Act
        CollectionsMarshal.AsSpan(springs)[0].EnsureMinDistance(5.0);

        //Assert
        springs[0].MinDistance.Should().Be(5.0);
    }

    /// <summary>
    /// The same seam for the <c>alignment-distances</c> trio, which is the mutation whose
    /// loss was total rather than partial: all three setters ran against a copy.
    /// </summary>
    [Fact]
    public void the_alignment_distances_trio_through_a_span_reference_is_kept()
    {
        //Arrange
        // Upstream sets ideal, min and inverse-stretch together so a manually placed
        // staff is pinned rather than merely floored -- a spring that kept its min but
        // lost its stretch strength would still drift.
        List<Spring> springs = new List<Spring> { new Spring(0.5, 0.0) };

        //Act
        ref Spring back = ref CollectionsMarshal.AsSpan(springs)[0];
        back.SetIdealDistance(30.0);
        back.SetMinDistance(30.0);
        back.SetInverseStretchStrength(0);

        //Assert
        springs[0].IdealDistance.Should().Be(30.0);
        springs[0].MinDistance.Should().Be(30.0);
    }
}
