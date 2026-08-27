// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Pitching;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// The <c>ly.pitch</c> tools against python-ly itself: every fixture under
/// <c>fixtures/pitch</c> pairs a <c>.ly</c> document with the text every pitch
/// operation of python-ly v0.9.10 produced from it — transposition (interval,
/// simplify, modal, mode-shift), relative/absolute conversion both ways,
/// retrograde, inversion and language translation (regenerate with
/// <c>tools/pitchprobe/gen-pitch-fixtures.py</c>). Nothing here is recorded
/// from the port's own output.
/// </summary>
public class PitchParityTests
{
    //The mode definitions Frescobaldi's pitch dialog offers, matching the
    //probe tool's copies.
    private static readonly (int Step, Fraction Alter)[] Major =
    {
        (0, new Fraction(0)), (1, new Fraction(1)), (2, new Fraction(2)),
        (3, new Fraction(5, 2)), (4, new Fraction(7, 2)), (5, new Fraction(9, 2)),
        (6, new Fraction(11, 2)),
    };

    private static readonly (int Step, Fraction Alter)[] MinorHarmonic =
    {
        (0, new Fraction(0)), (1, new Fraction(1)), (2, new Fraction(3, 2)),
        (3, new Fraction(5, 2)), (4, new Fraction(7, 2)), (5, new Fraction(4)),
        (6, new Fraction(11, 2)),
    };

    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "pitch");

    /// <summary>Every fixture base name, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> FixtureNames()
        => Directory.GetFiles(FixturesDirectory(), "*.pitch.json")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".pitch.json", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void every_transposition_matches_python_ly(string name)
    {
        //Arrange
        (string text, JsonDocument expected) = Load(name);
        using JsonDocument fixture = expected;
        JsonElement results = fixture.RootElement.GetProperty("results");

        var operations = new Dictionary<string, Func<TransposerBase>>(StringComparer.Ordinal)
        {
            //c -> d (a whole tone up) and c -> a, (a minor third down)
            { "transpose_c_d", () => new Transposer(P(0), P(1)) },
            { "transpose_c_a_down", () => new Transposer(P(0), P(5, 0, -1)) },
            { "transpose_c_ees", () => new Transposer(P(0), P(2, -1)) },
            { "simplify", () => new Simplifier() },
            { "modal_up_two_c_major",
                () => new ModalTransposer(2, ModalTransposer.GetKeyIndex("C")) },
            { "modal_down_three_g_major",
                () => new ModalTransposer(-3, ModalTransposer.GetKeyIndex("G")) },
            { "mode_shift_c_major", () => new ModeShifter(P(0), Major) },
            { "mode_shift_a_minor_harmonic", () => new ModeShifter(P(5), MinorHarmonic) },
        };

        //Act + Assert
        foreach (KeyValuePair<string, Func<TransposerBase>> operation in operations)
        {
            var document = new Document(text);
            Transposing.Transpose(new Cursor(document), operation.Value());
            Label(name, operation.Key, document.PlainText())
                .Should().Be(Label(
                    name, operation.Key, results.GetProperty(operation.Key).GetString()));
        }

        var relativeAbsolute = new Document(text);
        Transposing.Transpose(
            new Cursor(relativeAbsolute),
            new Transposer(P(0), P(1)),
            relativeFirstPitchAbsolute: true);
        Label(name, "transpose_c_d_relative_absolute", relativeAbsolute.PlainText())
            .Should().Be(Label(
                name,
                "transpose_c_d_relative_absolute",
                results.GetProperty("transpose_c_d_relative_absolute").GetString()));
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void relative_and_absolute_conversion_matches_python_ly(string name)
    {
        //Arrange
        (string text, JsonDocument expected) = Load(name);
        using JsonDocument fixture = expected;
        JsonElement results = fixture.RootElement.GetProperty("results");

        //Act + Assert
        var toAbsolute = new Document(text);
        Rel2Abs.Convert(new Cursor(toAbsolute));
        Label(name, "rel2abs", toAbsolute.PlainText())
            .Should().Be(Label(name, "rel2abs", results.GetProperty("rel2abs").GetString()));

        var toAbsoluteFirst = new Document(text);
        Rel2Abs.Convert(new Cursor(toAbsoluteFirst), firstPitchAbsolute: true);
        Label(name, "rel2abs_first_absolute", toAbsoluteFirst.PlainText())
            .Should().Be(Label(
                name,
                "rel2abs_first_absolute",
                results.GetProperty("rel2abs_first_absolute").GetString()));

        var toRelative = new Document(text);
        Abs2Rel.Convert(new Cursor(toRelative));
        Label(name, "abs2rel", toRelative.PlainText())
            .Should().Be(Label(name, "abs2rel", results.GetProperty("abs2rel").GetString()));

        var toRelativeNoStart = new Document(text);
        Abs2Rel.Convert(new Cursor(toRelativeNoStart), startPitch: false);
        Label(name, "abs2rel_no_startpitch", toRelativeNoStart.PlainText())
            .Should().Be(Label(
                name,
                "abs2rel_no_startpitch",
                results.GetProperty("abs2rel_no_startpitch").GetString()));

        var toRelativeFirst = new Document(text);
        Abs2Rel.Convert(
            new Cursor(toRelativeFirst), startPitch: false, firstPitchAbsolute: true);
        Label(name, "abs2rel_no_startpitch_first_absolute", toRelativeFirst.PlainText())
            .Should().Be(Label(
                name,
                "abs2rel_no_startpitch_first_absolute",
                results.GetProperty("abs2rel_no_startpitch_first_absolute").GetString()));
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void transform_and_translation_match_python_ly(string name)
    {
        //Arrange
        (string text, JsonDocument expected) = Load(name);
        using JsonDocument fixture = expected;
        JsonElement results = fixture.RootElement.GetProperty("results");

        //Act + Assert
        var reversed = new Document(text);
        PitchTransform.Retrograde(new Cursor(reversed));
        Label(name, "retrograde", reversed.PlainText())
            .Should().Be(Label(name, "retrograde", results.GetProperty("retrograde").GetString()));

        var inverted = new Document(text);
        PitchTransform.Inversion(new Cursor(inverted));
        Label(name, "inversion", inverted.PlainText())
            .Should().Be(Label(name, "inversion", results.GetProperty("inversion").GetString()));

        foreach (JsonProperty translation in
            fixture.RootElement.GetProperty("translations").EnumerateObject())
        {
            var document = new Document(text);
            bool changed = Translating.Translate(new Cursor(document), translation.Name);

            Label(name, "translate_" + translation.Name, document.PlainText())
                .Should().Be(Label(
                    name,
                    "translate_" + translation.Name,
                    translation.Value.GetProperty("text").GetString()));
            Label(name, "translate_" + translation.Name + "_changed", changed.ToString())
                .Should().Be(Label(
                    name,
                    "translate_" + translation.Name + "_changed",
                    translation.Value.GetProperty("changed").GetBoolean().ToString()));
        }
    }

    [Fact]
    public void the_circle_of_fifths_indexes_keys_as_upstream_documents()
    {
        //Arrange + Act + Assert
        ModalTransposer.GetKeyIndex("Cb").Should().Be(0);
        ModalTransposer.GetKeyIndex("C").Should().Be(7);

        //Upstream's docstring says B# is 14; its own list ends at C#, and
        //getKeyIndex('B#') raises there too — verified against the checkout.
        ModalTransposer.GetKeyIndex("C#").Should().Be(14);
        Assert.Throws<ArgumentException>(() => ModalTransposer.GetKeyIndex("B#"));

        //The name is capitalized first, so any casing resolves.
        ModalTransposer.GetKeyIndex("eb").Should().Be(4);
        ModalTransposer.GetKeyIndex("F#").Should().Be(13);
    }

    [Fact]
    public void a_language_command_is_inserted_below_the_version_line()
    {
        //Arrange
        //The number here is DELIBERATELY not the release LilyPort is compatible
        //with: this case exercises a document operation on arbitrary .ly text, and
        //Fresco.Brix.Ly does not reference LilyPort (plan §5.1) so it could not read
        //that release anyway. Any version at or above the \language cutoff will do.
        var document = new Document("\\version \"2.24.0\"\nmusic = { c'4 }\n");

        //Act
        Translating.InsertLanguage(document, "deutsch");

        //Assert
        document.PlainText()
            .Should().Be("\\version \"2.24.0\"\n\\language \"deutsch\"\nmusic = { c'4 }\n");
    }

    [Fact]
    public void an_old_version_gets_the_include_form_instead()
    {
        //Arrange
        var document = new Document("\\version \"2.12.0\"\nmusic = { c'4 }\n");

        //Act
        Translating.InsertLanguage(document, "italiano", new[] { 2, 12, 0 });

        //Assert
        document.PlainText()
            .Should().Be("\\version \"2.12.0\"\n\\include \"italiano.ly\"\nmusic = { c'4 }\n");
    }

    private static (string Text, JsonDocument Fixture) Load(string name)
    {
        string directory = FixturesDirectory();
        string text = File.ReadAllText(Path.Combine(directory, name + ".ly"))
            .Replace("\r", string.Empty);
        JsonDocument fixture = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, name + ".pitch.json")));
        return (text, fixture);
    }

    private static Pitch P(int note, int alter = 0, int octave = 0)
        => new Pitch(note, new Fraction(alter), octave);

    private static string Label(string name, string operation, string value)
        => $"--- {name}.{operation} ---\n{value}";
}
