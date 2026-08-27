// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Music;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;
using LyDocument = Fresco.Brix.Ly.Document;
using MusicDocument = Fresco.Brix.Ly.Music.Document;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// The <c>ly.music</c> tree against python-ly itself: every fixture under
/// <c>fixtures/music</c> pairs a <c>.ly</c> document with the whole item tree
/// python-ly v0.9.10 built from it — class, position, end position, tokens,
/// musical length and plain text of every node — plus the node and the time
/// position it answers across the document (regenerate with
/// <c>tools/musicprobe/gen-music-fixtures.py</c>). Nothing here is recorded
/// from the port's own output.
/// </summary>
public class MusicParityTests
{
    /// <summary>The upstream name of the item classes the port renames.</summary>
    private static readonly Dictionary<string, string> UpstreamNames
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "TokenItem", "Token" },
            { "DurationItem", "Duration" },
            { "StringItem", "String" },
            { "ContextItem", "Context" },
        };

    /// <summary>
    /// The FR14 divergences the music oracle is generated with, mirroring
    /// <c>tools/musicprobe/gen-music-fixtures.py</c>'s <c>KNOWN_FIXES</c> entry for
    /// entry. The oracle answers what python-ly produces with its demonstrable
    /// defects fixed, because that is what the port implements; every fixture
    /// records the list it was generated with, and
    /// <see cref="every_fixture_declares_the_known_fixes_it_was_generated_with"/>
    /// holds the two together. A fixture regenerated without a fix, a fix added to
    /// the tool and not declared here, or a declaration that drifts from the tool
    /// all fail that test.
    /// </summary>
    private static readonly (string Module, string Old, string New, string Why)[] KnownFixes
        = new[]
        {
            (
                "ly.music.read",
                "elif not item.specifier and isinstance(t, lex.StringStart):",
                "elif not item._specifier and isinstance(t, lex.StringStart):",
                "handle_repeat guards on the bound method item.specifier instead "
                + "of the field item._specifier, so a quoted repeat specifier is "
                + "never read and the repeat ends at the string (FR14)"
            ),
        };

    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "music");

    /// <summary>Every fixture base name, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> FixtureNames()
        => Directory.GetFiles(FixturesDirectory(), "*.music.json")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".music.json", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void the_item_tree_matches_python_ly(string name)
    {
        //Arrange
        (string text, JsonDocument fixture) = Load(name);
        using JsonDocument expected = fixture;

        //Act
        MusicDocument music = MusicReader.ReadDocument(new LyDocument(text));

        //Assert
        var produced = new StringBuilder();
        Describe(music, 0, produced);
        var reference = new StringBuilder();
        DescribeRow(expected.RootElement.GetProperty("tree"), 0, reference);
        produced.ToString().Should().Be(reference.ToString());
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void the_positions_match_python_ly(string name)
    {
        //Arrange
        (string text, JsonDocument fixture) = Load(name);
        using JsonDocument expected = fixture;
        MusicDocument music = MusicReader.ReadDocument(new LyDocument(text));

        //Act + Assert
        var producedNodes = new List<string>();
        var referenceNodes = new List<string>();
        foreach (JsonElement row in expected.RootElement.GetProperty("node_positions")
            .EnumerateArray())
        {
            JsonElement[] parts = row.EnumerateArray().ToArray();
            int position = parts[0].GetInt32();
            Item node = music.NodeAt(position);
            producedNodes.Add(
                $"{position}: {UpstreamName(node)}@{node.Position}");
            referenceNodes.Add(
                $"{position}: {parts[1].GetString()}@{parts[2].GetInt32()}");
        }

        string.Join("\n", producedNodes).Should().Be(string.Join("\n", referenceNodes));

        var producedTimes = new List<string>();
        var referenceTimes = new List<string>();
        foreach (JsonElement row in expected.RootElement.GetProperty("time_positions")
            .EnumerateArray())
        {
            JsonElement[] parts = row.EnumerateArray().ToArray();
            int position = parts[0].GetInt32();
            Fraction? time = music.TimePosition(position);
            producedTimes.Add($"{position}: {Show(time)}");
            referenceTimes.Add(
                $"{position}: {(parts[1].ValueKind == JsonValueKind.Null ? "<null>" : parts[1].GetString())}");
        }

        string.Join("\n", producedTimes).Should().Be(string.Join("\n", referenceTimes));

        music.HasOutput().Should().Be(expected.RootElement.GetProperty("has_output").GetBoolean());
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void every_fixture_declares_the_known_fixes_it_was_generated_with(string name)
    {
        //Arrange
        (string _, JsonDocument fixture) = Load(name);
        using JsonDocument expected = fixture;

        //Act
        var declared = new List<string>();
        foreach (JsonElement fix in expected.RootElement.GetProperty("known_fixes")
            .EnumerateArray())
        {
            declared.Add(string.Join("\n", new[]
            {
                fix.GetProperty("module").GetString(),
                fix.GetProperty("old").GetString(),
                fix.GetProperty("new").GetString(),
                fix.GetProperty("why").GetString(),
            }));
        }

        //Assert
        string.Join("\n\n", declared).Should().Be(string.Join("\n\n",
            KnownFixes.Select(f => string.Join("\n", new[] { f.Module, f.Old, f.New, f.Why }))));
    }

    /// <summary>
    /// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14) — the corrected parse
    /// this wave's first item exists for. As python-ly v0.9.10 ships, this document
    /// gives a <c>Repeat</c> with no specifier, a count of 1 and a
    /// <c>String</c> for its only child, with the number and the whole body left as
    /// SIBLINGS of the repeat and its length 0; see the note in
    /// <c>Reader.HandleRepeat</c>. The fixture corpus covers the same fix through
    /// <c>drums</c>, whose four repeats are spelled this way.
    /// </summary>
    [Fact]
    public void a_quoted_repeat_specifier_is_read_with_its_count_and_its_body()
    {
        //Arrange
        var document = new LyDocument("\\relative c' { \\repeat \"unfold\" 5 { c4 d e f } }\n");

        //Act
        MusicDocument music = MusicReader.ReadDocument(document);
        Repeat repeat = music.Find<Repeat>(depth: -1).Single();

        //Assert
        repeat.Specifier().Should().Be("unfold");
        repeat.RepeatCount().Should().Be(5);
        repeat.Count.Should().Be(1);
        repeat[0].Should().BeOfType<MusicList>();
        //Five times a body of four quarter notes: 5 x 1, where upstream answers 0.
        repeat.Length().Should().Be(new Fraction(5));
    }

    [Fact]
    public void a_music_list_reports_the_duration_of_its_contents()
    {
        //Arrange
        var document = new LyDocument("music = { c'4 d'4 e'4 f'4 }\n");
        MusicDocument music = MusicReader.ReadDocument(document);

        //Act
        MusicList list = music.Find<MusicList>().First();

        //Assert
        list.Length().Should().Be(Fraction.One);
    }

    [Fact]
    public void a_simultaneous_list_takes_the_longest_branch()
    {
        //Arrange
        var document = new LyDocument("music = << { c'1 } { d'4 } >>\n");
        MusicDocument music = MusicReader.ReadDocument(document);

        //Act
        MusicList outer = music.Find<MusicList>().First(l => l.Simultaneous);

        //Assert
        outer.Length().Should().Be(Fraction.One);
    }

    [Fact]
    public void a_user_command_follows_its_assignment()
    {
        //Arrange
        var document = new LyDocument("music = { c'4 d'4 }\nother = { \\music \\music }\n");
        MusicDocument music = MusicReader.ReadDocument(document);

        //Act
        UserCommand command = music.Find<UserCommand>().First();

        //Assert
        command.Name().Should().Be("music");
        command.Value().Should().NotBeNull();
        command.Length().Should().Be(new Fraction(1, 2));
    }

    private static string Show(Fraction? value)
        => value == null
            ? "<null>"
            : $"{value.Value.Numerator}/{value.Value.Denominator}";

    private static string UpstreamName(Item item)
    {
        string name = item.GetType().Name;
        return UpstreamNames.TryGetValue(name, out string upstream) ? upstream : name;
    }

    private static void Describe(Item item, int depth, StringBuilder into)
    {
        into.Append(new string(' ', depth * 2))
            .Append(UpstreamName(item))
            .Append(" pos=").Append(item.Position)
            .Append(" end=").Append(item.EndPosition())
            .Append(" token=").Append(item.Token?.Text ?? "<null>")
            .Append(" tokens=[").Append(string.Join(",", item.Tokens.Select(t => t.Text)))
            .Append("] length=").Append(Show(item.Length()))
            .Append(" text=").Append(item.PlainText())
            .Append('\n');
        foreach (Node child in item)
        {
            Describe((Item)child, depth + 1, into);
        }
    }

    private static void DescribeRow(JsonElement row, int depth, StringBuilder into)
    {
        into.Append(new string(' ', depth * 2))
            .Append(row.GetProperty("cls").GetString())
            .Append(" pos=").Append(row.GetProperty("position").GetInt32())
            .Append(" end=").Append(row.GetProperty("end").GetInt32())
            .Append(" token=")
            .Append(row.GetProperty("token").ValueKind == JsonValueKind.Null
                ? "<null>"
                : row.GetProperty("token").GetString())
            .Append(" tokens=[")
            .Append(string.Join(
                ",", row.GetProperty("tokens").EnumerateArray().Select(t => t.GetString())))
            .Append("] length=")
            .Append(row.GetProperty("length").ValueKind == JsonValueKind.Null
                ? "<null>"
                : row.GetProperty("length").GetString())
            .Append(" text=").Append(row.GetProperty("plaintext").GetString())
            .Append('\n');
        foreach (JsonElement child in row.GetProperty("children").EnumerateArray())
        {
            DescribeRow(child, depth + 1, into);
        }
    }

    private static (string Text, JsonDocument Fixture) Load(string name)
    {
        string directory = FixturesDirectory();
        string text = File.ReadAllText(Path.Combine(directory, name + ".ly"))
            .Replace("\r", string.Empty);
        JsonDocument fixture = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, name + ".music.json")));
        return (text, fixture);
    }
}
