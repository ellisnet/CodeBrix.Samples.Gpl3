// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.ConvertLy;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// The conversion rules against LilyPond's own <c>convert-ly</c>: every fixture under
/// <c>fixtures/convertly</c> pairs a real input file with the text, the messages and
/// the version stamp upstream's <c>convertrules.py</c> produced from it.
/// <para>
/// Each case is recorded TWICE — once converted from before the first rule, so that all
/// 326 rules run over the file, and once from the version the file itself declares,
/// which is what a user gets and what exercises the version selection and the
/// <c>\version</c> rewrite. Nothing here is recorded from the port's own output;
/// regenerate with <c>tools/convertlyprobe/gen-convertly-fixtures.py</c>.
/// </para>
/// </summary>
public class ConvertLyParityTests
{
    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "convertly");

    /// <summary>Every recorded case, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> CaseNames()
        => Directory.GetFiles(FixturesDirectory(), "*.convertly.json")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".convertly.json", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    private static JsonDocument Load(string name)
        => JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(FixturesDirectory(), name + ".convertly.json")));

    private static ConversionVersion Version(JsonElement element)
    {
        int[] parts = element.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        return new ConversionVersion(parts[0], parts[1], parts[2]);
    }

    private static void AssertRun(string input, JsonElement expected)
    {
        //Arrange
        ConversionVersion from = Version(expected.GetProperty("from"));
        ConversionVersion to = Version(expected.GetProperty("to"));

        //Act
        ConversionResult result = DocumentConverter.Convert(input, from, to);

        //Assert
        result.Text.Should().Be(expected.GetProperty("output").GetString());

        JsonElement stamp = expected.GetProperty("stamp");
        if (stamp.ValueKind == JsonValueKind.Null)
        {
            result.StampedVersion.Should().BeNull();
        }
        else
        {
            result.StampedVersion.Should().NotBeNull();
            result.StampedVersion.Value.ToString().Should().Be(Version(stamp).ToString());
        }

        JsonElement lastChange = expected.GetProperty("last_change");
        if (lastChange.ValueKind == JsonValueKind.Null)
        {
            result.LastChange.Should().BeNull();
        }
        else
        {
            result.LastChange.Value.ToString().Should()
                .Be(Version(lastChange).ToString());
        }

        result.Errors.Should().Be(expected.GetProperty("errors").GetInt32());

        string[] expectedMessages = expected.GetProperty("messages")
            .EnumerateArray().Select(m => m.GetString()).ToArray();
        string.Join("\n---\n", result.Messages).Should()
            .Be(string.Join("\n---\n", expectedMessages));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void every_rule_over_a_real_file_matches_convert_ly(string name)
    {
        //Arrange
        using JsonDocument fixture = Load(name);

        //Act + Assert
        AssertRun(
            fixture.RootElement.GetProperty("input").GetString(),
            fixture.RootElement.GetProperty("full"));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void converting_from_the_declared_version_matches_convert_ly(string name)
    {
        //Arrange
        using JsonDocument fixture = Load(name);
        if (!fixture.RootElement.TryGetProperty("declared_run", out JsonElement expected))
        {
            //The file declares no usable version; the other test still covers it.
            return;
        }

        //Act + Assert
        AssertRun(fixture.RootElement.GetProperty("input").GetString(), expected);
    }

    [Fact]
    public void the_rule_table_matches_upstreams()
    {
        //Arrange
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FixturesDirectory(), "manifest.json")));

        //Act + Assert
        DocumentConverter.Rules.Count.Should()
            .Be(manifest.RootElement.GetProperty("rules").GetInt32());
        DocumentConverter.LatestVersion.ToString().Should()
            .Be(Version(manifest.RootElement.GetProperty("latest")).ToString());
    }

    [Fact]
    public void the_rules_are_in_ascending_version_order()
    {
        //Arrange + Act
        IReadOnlyList<ConversionRule> rules = DocumentConverter.Rules;

        //Assert
        for (int i = 1; i < rules.Count; i++)
        {
            (rules[i].Version >= rules[i - 1].Version).Should()
                .BeTrue($"rule {i} ({rules[i].Version}) follows {rules[i - 1].Version}");
        }
    }

    [Fact]
    public void a_document_with_no_version_is_reported_rather_than_converted()
    {
        //Arrange
        const string text = "{ c4 d e f }\n";

        //Act
        ConversionResult result = DocumentConverter.Convert(text);

        //Assert
        result.VersionUnknown.Should().BeTrue();
        result.Text.Should().Be(text);
    }

    [Fact]
    public void an_odd_minor_version_without_a_patch_is_refused()
    {
        //Arrange
        // convert-ly.py: a missing third component is accepted only when the second is
        // EVEN, because the syntax does not change inside a stable series.
        //Act
        bool even = DocumentConverter.TryReadDeclaredVersion(
            "\\version \"2.14\"\n", out ConversionVersion parsed);
        bool odd = DocumentConverter.TryReadDeclaredVersion(
            "\\version \"2.15\"\n", out ConversionVersion _, out bool malformed);

        //Assert
        even.Should().BeTrue();
        parsed.ToString().Should().Be("2.14.0");
        odd.Should().BeFalse();
        malformed.Should().BeTrue();
    }
}
