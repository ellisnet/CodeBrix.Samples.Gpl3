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
/// <c>Caesura_engraver</c> reached through the real pipeline: a
/// <c>CaesuraEvent</c> under the default <c>caesuraType</c> makes a
/// <c>BreathingSign</c> configured by <c>breathMarkDefinitions</c> — the
/// <c>ly:breathing-sign::set-breath-properties</c> path.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class CaesuraEngraverTests : IDisposable
{
    /// <summary>Removes the fixture translators from the process-global registry.</summary>
    public void Dispose() => Epg8TestHarness.Cleanup();

    [Fact]
    public void a_caesura_makes_a_breathing_sign_configured_from_the_breath_alist()
    {
        //Arrange
        Epg8TestHarness.Tree tree = Epg8TestHarness.BuildTree(
            new (string, object)[]
            {
                ("timeSignature", new Pair(4L, 4L)),
                ("timeSignatureSettings",
                    Epg8TestHarness.Eval("default-time-signature-settings")),
                ("timing", true),
                ("caesuraType", Epg8TestHarness.Eval("'((breath . caesura))")),
                ("breathMarkDefinitions", Epg8TestHarness.Eval("default-breath-alist")),
            },
            new[] { "Timing_translator" },
            new[] { "Caesura_engraver" },
            Array.Empty<string>());
        MusicObject music = Epg8TestHarness.QuarterNotes(
            1, "(make-music 'CaesuraEvent)");

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> signs = tree.GrobsNamed("BreathingSign");
        signs.Count.Should().Be(1);

        // The 'caesura entry of default-breath-alist sets a musicglyph text markup;
        // finding it on the grob proves set-breath-properties applied the definition.
        (signs[0].GetProperty("text") is Nil).Should().BeFalse();
    }
}
