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
/// <c>Time_signature_engraver</c> reached through the real pipeline: the
/// <c>timeSignature</c> the context definitions carry becomes exactly one
/// <c>TimeSignature</c> grob carrying the same specification.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class TimeSignatureEngraverTests : IDisposable
{
    /// <summary>Removes the fixture translators from the process-global registry.</summary>
    public void Dispose() => Epg8TestHarness.Cleanup();

    [Fact]
    public void the_initial_time_signature_becomes_one_grob_with_the_spec()
    {
        //Arrange
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            new (string, object)[]
            {
                ("timeSignature", new Pair(3L, 4L)),
                ("timeSignatureSettings",
                    Epg8TestHarness.Eval("default-time-signature-settings")),
                ("timing", true),
            },
            new[] { "Timing_translator" },
            new[] { "Time_signature_engraver" },
            Array.Empty<string>());
        MusicObject music = Epg8TestHarness.QuarterNotes(3);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> signatures = tree.GrobsNamed("TimeSignature");

        // Exactly one: process_music compares against last_spec_ by IDENTITY, so an
        // unchanged property never re-creates the grob at later timesteps.
        signatures.Count.Should().Be(1);

        object spec = signatures[0].GetProperty("time-signature");
        CodeBrix.LilyScheme.Primitives.CorePrimitives
            .SchemeEqual(spec, new Pair(3L, 4L)).Should().BeTrue();
    }
}
