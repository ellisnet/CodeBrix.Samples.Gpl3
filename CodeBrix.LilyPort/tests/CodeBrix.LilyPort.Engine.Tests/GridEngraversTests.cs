// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The grid pair reached through the real pipeline: <c>Grid_point_engraver</c>
/// dropping points on grid moments, and <c>Grid_line_span_engraver</c> spanning a
/// line once it sees two points in one timestep.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class GridEngraversTests : IDisposable
{
    /// <summary>Removes the fixture translators from the process-global registry.</summary>
    public void Dispose() => Epg8TestHarness.Cleanup();

    private static (string Name, object Value)[] ScoreProps(
        params (string Name, object Value)[] extra)
    {
        List<(string, object)> props = new List<(string, object)>
        {
            ("timeSignature", new Pair(4L, 4L)),
            ("timeSignatureSettings",
                Epg8TestHarness.Eval("default-time-signature-settings")),
            ("timing", true),
        };
        props.AddRange(extra);
        return props.ToArray();
    }

    [Fact]
    public void grid_points_appear_on_grid_interval_moments()
    {
        //Arrange
        // gridInterval 1/2 over four quarters: points at 0, 1/2 and the final
        // timestep at 1 — but not at 1/4 or 3/4.
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            ScoreProps(("gridInterval", Epg8TestHarness.Eval("1/2"))),
            new[] { "Timing_translator" },
            new[] { "Grid_point_engraver" },
            Array.Empty<string>());
        MusicObject music = Epg8TestHarness.QuarterNotes(4);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        tree.GrobsNamed("GridPoint").Count.Should().Be(3);
    }

    [Fact]
    public void two_grid_points_in_one_timestep_get_a_grid_line()
    {
        //Arrange
        // Upstream needs two staves; the maker announces two GridPoint items in one
        // timestep, which is the situation the span engraver reacts to.
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            ScoreProps(),
            new[] { "Timing_translator", "Grid_line_span_engraver" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            makerGrobName: "GridPoint",
            makerCount: 2);
        MusicObject music = Epg8TestHarness.QuarterNotes(1);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> lines = tree.GrobsNamed("GridLine");
        lines.Count.Should().Be(1);
        PointerGroupInterface.ExtractGrobSet(lines[0], "elements").Count.Should().Be(2);
    }
}
