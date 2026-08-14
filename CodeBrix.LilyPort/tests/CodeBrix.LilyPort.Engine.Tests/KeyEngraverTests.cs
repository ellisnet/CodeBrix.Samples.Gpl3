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
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <c>Key_engraver</c> reached through the real pipeline: a
/// <c>KeyChangeEvent</c> becomes a <c>KeySignature</c> grob, and the
/// <c>keyAlterations</c> the engraver seeds is what EPG9's
/// <c>Accidental_engraver</c> will read.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class KeyEngraverTests : IDisposable
{
    /// <summary>Removes the fixture translators from the process-global registry.</summary>
    public void Dispose() => Epg8TestHarness.Cleanup();

    private const string OrderLiteral =
        "'((6 . -1/2) (2 . -1/2) (5 . -1/2) (1 . -1/2) (4 . -1/2) (0 . -1/2) (3 . -1/2)"
        + " (3 . 1/2) (0 . 1/2) (4 . 1/2) (1 . 1/2) (5 . 1/2) (2 . 1/2) (6 . 1/2))";

    private const string GMajorEvent =
        "(make-music 'KeyChangeEvent"
        + " 'tonic (ly:make-pitch 0 4 0)"
        + " 'pitch-alist '((0 . 0) (1 . 0) (2 . 0) (3 . 1/2) (4 . 0) (5 . 0) (6 . 0)))";

    private Epg8TestHarness.Tree BuildKeyTree()
        => Epg8TestHarness.BuildTree(
            new (string, object)[]
            {
                ("timeSignature", new Pair(4L, 4L)),
                ("timeSignatureSettings",
                    Epg8TestHarness.Eval("default-time-signature-settings")),
                ("timing", true),
                ("keyAlterationOrder", Epg8TestHarness.Eval(OrderLiteral)),
            },
            new[] { "Timing_translator" },
            new[] { "Key_engraver" },
            Array.Empty<string>());

    [Fact]
    public void a_key_change_builds_a_key_signature_with_its_alterations()
    {
        //Arrange
        Epg8TestHarness.Tree tree = BuildKeyTree();
        MusicObject music = Epg8TestHarness.QuarterNotes(1, GMajorEvent);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        List<Grob> keys = tree.GrobsNamed("KeySignature");
        keys.Count.Should().BeGreaterThan(0);

        // G major is one sharp: fis, notename 3, +1/2.
        object alist = keys[0].GetProperty("alteration-alist");
        (alist is Pair).Should().BeTrue();
        Pair first = (Pair)((Pair)alist).Car;
        CodeBrix.LilyScheme.Primitives.CorePrimitives.SchemeEqual(first.Car, 3L)
            .Should().BeTrue();
    }

    [Fact]
    public void the_engraver_seeds_key_alterations_for_the_accidental_engraver()
    {
        //Arrange
        Epg8TestHarness.Tree tree = BuildKeyTree();
        MusicObject music = Epg8TestHarness.QuarterNotes(1, GMajorEvent);

        //Act
        Epg8TestHarness.Iterate(tree, music);

        //Assert
        // keyAlterations is written where the engraver lives; EPG9's
        // Accidental_engraver reads it from there.
        Context staff = tree.FindContext("Staff");
        object alterations = staff.GetProperty("keyAlterations");
        (alterations is Pair).Should().BeTrue();

        Pair entry = (Pair)((Pair)alterations).Car;
        CodeBrix.LilyScheme.Primitives.CorePrimitives.SchemeEqual(entry.Car, 3L)
            .Should().BeTrue();
        TranslatorSchemeHelpers.ToRational(entry.Cdr, Rational.Zero).Should().Be(new Rational(1, 2));

        // The tonic travels with it.
        (staff.GetProperty("tonic") is Pitch).Should().BeTrue();
    }
}
