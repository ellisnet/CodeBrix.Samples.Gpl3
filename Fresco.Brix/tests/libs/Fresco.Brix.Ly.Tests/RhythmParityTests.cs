// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// <see cref="Rhythm"/> against python-ly itself: every fixture under
/// <c>fixtures/rhythm</c> pairs a <c>.ly</c> document with the text every
/// <c>ly.rhythm</c> operation of python-ly v0.9.10 produced from it, plus the
/// music items it saw and the durations it extracted (regenerate with
/// <c>tools/rhythmprobe/gen-rhythm-fixtures.py</c>). Nothing here is recorded
/// from the port's own output.
/// </summary>
public class RhythmParityTests
{
    private static readonly Dictionary<string, Action<Cursor>> Operations
        = new Dictionary<string, Action<Cursor>>(StringComparer.Ordinal)
        {
            { "double", Rhythm.Double },
            { "halve", Rhythm.Halve },
            { "dot", Rhythm.Dot },
            { "undot", Rhythm.Undot },
            { "remove_scaling", Rhythm.RemoveScaling },
            { "remove_fraction_scaling", Rhythm.RemoveFractionScaling },
            { "remove", Rhythm.Remove },
            { "implicit", Rhythm.Implicit },
            { "implicit_per_line", Rhythm.ImplicitPerLine },
            { "explicit", Rhythm.Explicit },
        };

    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "rhythm");

    /// <summary>Every fixture base name, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> FixtureNames()
        => Directory.GetFiles(FixturesDirectory(), "*.rhythm.json")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".rhythm.json", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void every_operation_matches_python_ly(string name)
    {
        //Arrange
        string directory = FixturesDirectory();
        string text = File.ReadAllText(Path.Combine(directory, name + ".ly"))
            .Replace("\r", string.Empty);
        using JsonDocument expected = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, name + ".rhythm.json")));
        JsonElement root = expected.RootElement;

        //Act + Assert — each operation on a fresh document over the whole file.
        foreach (KeyValuePair<string, Action<Cursor>> operation in Operations)
        {
            var document = new Document(text);
            operation.Value(new Cursor(document));

            string produced = document.PlainText();
            string reference = root.GetProperty("results")
                .GetProperty(operation.Key).GetString();
            Label(name, operation.Key, produced)
                .Should().Be(Label(name, operation.Key, reference));
        }

        var overwriteDocument = new Document(text);
        Rhythm.Overwrite(
            new Cursor(overwriteDocument),
            root.GetProperty("overwrite_durations").EnumerateArray()
                .Select(v => v.GetString()).ToList());
        Label(name, "overwrite", overwriteDocument.PlainText())
            .Should().Be(Label(
                name, "overwrite", root.GetProperty("results").GetProperty("overwrite").GetString()));

        var extractDocument = new Document(text);
        Label(name, "extract", string.Join("|", Rhythm.Extract(new Cursor(extractDocument))))
            .Should().Be(Label(
                name,
                "extract",
                string.Join(
                    "|",
                    root.GetProperty("extract").EnumerateArray().Select(v => v.GetString()))));
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void the_music_items_match_python_ly(string name)
    {
        //Arrange
        string directory = FixturesDirectory();
        string text = File.ReadAllText(Path.Combine(directory, name + ".ly"))
            .Replace("\r", string.Empty);
        using JsonDocument expected = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, name + ".rhythm.json")));
        JsonElement rows = expected.RootElement.GetProperty("music_items");

        //Act
        var document = new Document(text);
        List<MusicItem> items = Rhythm.MusicItems(new Cursor(document)).ToList();

        //Assert
        List<string> produced = items.Select(Describe).ToList();
        List<string> reference = rows.EnumerateArray().Select(DescribeRow).ToList();
        for (int i = 0; i < Math.Min(produced.Count, reference.Count); i++)
        {
            $"{name}[{i}] {produced[i]}".Should().Be($"{name}[{i}] {reference[i]}");
        }

        produced.Count.Should().Be(reference.Count);
    }

    [Fact]
    public void an_empty_range_leaves_the_document_alone()
    {
        //Arrange
        //The number here is DELIBERATELY not the release LilyPort is compatible
        //with: this case exercises a document operation on arbitrary .ly text, and
        //Fresco.Brix.Ly does not reference LilyPort (plan §5.1) so it could not read
        //that release anyway. Any version at or above the \language cutoff will do.
        var document = new Document("\\version \"2.24.0\"\n");

        //Act
        Rhythm.Implicit(new Cursor(document));
        Rhythm.Explicit(new Cursor(document));
        Rhythm.ImplicitPerLine(new Cursor(document));
        Rhythm.Overwrite(new Cursor(document), new[] { "4" });

        //Assert
        document.PlainText().Should().Be("\\version \"2.24.0\"\n");
    }

    [Fact]
    public void extract_falls_back_to_a_quarter_when_the_first_duration_is_implied()
    {
        //Arrange
        var document = new Document("music = { c' d'8 e' }\n");

        //Act
        IReadOnlyList<string> durations = Rhythm.Extract(new Cursor(document));

        //Assert
        durations.Should().Equal(new[] { "4", "8", string.Empty });
    }

    private static string Describe(MusicItem item)
        => string.Format(
            "{0}:{1} insert={2} mayRemove={3} tokens=[{4}] durations=[{5}]",
            item.Pos,
            item.End,
            item.InsertPos,
            item.MayRemove ? "True" : "False",
            string.Join(",", item.Tokens.Select(t => t.Text)),
            string.Join(",", item.DurationTokens.Select(t => t.Text)));

    private static string DescribeRow(JsonElement row)
    {
        JsonElement[] parts = row.EnumerateArray().ToArray();
        return string.Format(
            "{0}:{1} insert={2} mayRemove={3} tokens=[{4}] durations=[{5}]",
            parts[0].GetInt32(),
            parts[1].GetInt32(),
            parts[2].GetInt32(),
            parts[3].GetBoolean() ? "True" : "False",
            string.Join(",", parts[4].EnumerateArray().Select(v => v.GetString())),
            string.Join(",", parts[5].EnumerateArray().Select(v => v.GetString())));
    }

    private static string Label(string name, string operation, string value)
        => $"--- {name}.{operation} ---\n{value}";
}
