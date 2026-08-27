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
/// <see cref="DocInfo"/> against python-ly itself: every fixture under
/// <c>fixtures/docinfo</c> pairs a <c>.ly</c> document with everything
/// python-ly v0.9.10's <c>ly.docinfo.DocInfo</c> harvested from it
/// (regenerate with <c>tools/docinfoprobe/gen-docinfo-fixtures.py</c>).
/// Nothing here is recorded from the port's own output.
/// </summary>
public class DocInfoParityTests
{
    /// <summary>The python module each ported token namespace stands for.</summary>
    private static readonly Dictionary<string, string> PythonModules
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Fresco.Brix.Ly.Lex", "_token" },
            { "Fresco.Brix.Ly.Lex.LilyPondMode", "lilypond" },
            { "Fresco.Brix.Ly.Lex.SchemeMode", "scheme" },
            { "Fresco.Brix.Ly.Lex.HtmlMode", "html" },
            { "Fresco.Brix.Ly.Lex.TexinfoMode", "texinfo" },
            { "Fresco.Brix.Ly.Lex.LatexMode", "latex" },
            { "Fresco.Brix.Ly.Lex.DocbookMode", "docbook" },
            { "Fresco.Brix.Ly.Lex.MupMode", "mup" },
        };

    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "docinfo");

    /// <summary>Every fixture base name, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> FixtureNames()
        => Directory.GetFiles(FixturesDirectory(), "*.docinfo.json")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".docinfo.json", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void the_harvest_matches_python_ly(string name)
    {
        //Arrange
        string directory = FixturesDirectory();
        string text = File.ReadAllText(Path.Combine(directory, name + ".ly"))
            .Replace("\r", string.Empty);
        using JsonDocument expected = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, name + ".docinfo.json")));
        JsonElement e = expected.RootElement;

        //Act
        DocInfo info = new DocInfo(new Document(text));

        //Assert — one line per property so a failure names the property.
        Line(name, "mode", info.Mode())
            .Should().Be(Line(name, "mode", Text(e, "mode")));
        Line(name, "token_count", info.Tokens.Length.ToString())
            .Should().Be(Line(name, "token_count", e.GetProperty("token_count").GetInt32().ToString()));
        Line(name, "version_string", info.VersionString())
            .Should().Be(Line(name, "version_string", Text(e, "version_string")));
        Line(name, "version", string.Join(".", info.Version()))
            .Should().Be(Line(name, "version", string.Join(".", Ints(e, "version"))));
        Line(name, "include_args", string.Join("|", info.IncludeArgs()))
            .Should().Be(Line(name, "include_args", string.Join("|", Strings(e, "include_args"))));
        Line(name, "scheme_load_args", string.Join("|", info.SchemeLoadArgs()))
            .Should().Be(Line(name, "scheme_load_args", string.Join("|", Strings(e, "scheme_load_args"))));
        Line(name, "output_args", string.Join("|", info.OutputArgs().Select(a => a.Kind + "=" + a.Argument)))
            .Should().Be(Line(name, "output_args", string.Join("|", Pairs(e, "output_args"))));
        Line(name, "definitions", string.Join("|", info.Definitions().Select(t => t.Text + "@" + t.Pos)))
            .Should().Be(Line(name, "definitions", string.Join("|", Pairs(e, "definitions", "@"))));
        Line(name, "markup_definitions", string.Join("|", info.MarkupDefinitions().Select(t => t.Text + "@" + t.Pos)))
            .Should().Be(Line(name, "markup_definitions", string.Join("|", Pairs(e, "markup_definitions", "@"))));
        Line(name, "language", info.Language())
            .Should().Be(Line(name, "language", Text(e, "language")));
        Line(name, "global_staff_size", info.GlobalStaffSize()?.ToString())
            .Should().Be(Line(name, "global_staff_size", Number(e, "global_staff_size")));
        Line(name, "complete", info.Complete().ToString())
            .Should().Be(Line(name, "complete", e.GetProperty("complete").GetBoolean().ToString()));
        Line(name, "has_output", info.HasOutput().ToString())
            .Should().Be(Line(name, "has_output", e.GetProperty("has_output").GetBoolean().ToString()));
        Line(name, "counted_tokens", CountedTokens(info))
            .Should().Be(Line(name, "counted_tokens", ExpectedCounts(e)));
    }

    [Fact]
    public void counting_a_token_type_includes_its_subclasses()
    {
        //Arrange
        //Note and Rest both derive from MusicItem; Space's subclass Newline is
        //inserted between blocks.
        DocInfo info = new DocInfo(new Document("music = { c'4 r4 }\nmore = { d'4 }\n"));

        //Act
        int musicItems = info.CountTokens(typeof(Lex.LilyPondMode.MusicItem));
        int spaces = info.CountTokens(typeof(Lex.Space));
        int newlines = info.CountTokens(typeof(Lex.Newline));

        //Assert
        musicItems.Should().Be(3); //c', r, d'
        newlines.Should().Be(2);
        spaces.Should().BeGreaterThan(newlines);
    }

    [Fact]
    public void the_token_hash_ignores_comments_and_whitespace()
    {
        //Arrange
        DocInfo plain = new DocInfo(new Document("music = { c'4 d' }\n"));
        DocInfo spaced = new DocInfo(new Document("music =  {  c'4   d'  }\n% a comment\n"));
        DocInfo different = new DocInfo(new Document("music = { c'4 e' }\n"));

        //Act
        int a = plain.TokenHash();
        int b = spaced.TokenHash();
        int c = different.TokenHash();

        //Assert
        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void a_range_keeps_only_the_tokens_inside_it()
    {
        //Arrange
        DocInfo info = new DocInfo(new Document("music = { c'4 d' e' f' }\n"));
        int open = info.Tokens.First(t => t.Text == "{").Pos;

        //Act
        DocInfo tail = info.Range(open);

        //Assert
        tail.Tokens.Length.Should().BeLessThan(info.Tokens.Length);
        tail.Tokens[0].Text.Should().Be("{");
        tail.Classes.Length.Should().Be(tail.Tokens.Length);
    }

    private static string CountedTokens(DocInfo info)
        => string.Join(
            ", ",
            info.CountedTokens()
                .Select(pair => PythonName(pair.Key) + "=" + pair.Value)
                .OrderBy(s => s, StringComparer.Ordinal));

    private static string ExpectedCounts(JsonElement element)
        => string.Join(
            ", ",
            element.GetProperty("counted_tokens").EnumerateObject()
                .Select(p => p.Name + "=" + p.Value.GetInt32())
                .OrderBy(s => s, StringComparer.Ordinal));

    private static string PythonName(Type type)
    {
        //The two documented class renames: python String → StringBase and
        //Error → ErrorBase (System.String and a base-name clash).
        string name = type.Name switch
        {
            "StringBase" => "String",
            "ErrorBase" => "Error",
            _ => type.Name,
        };
        return PythonModules[type.Namespace] + "." + name;
    }

    private static string Line(string name, string property, string value)
        => $"{name}.{property} = {value ?? "<null>"}";

    private static string Text(JsonElement element, string property)
    {
        JsonElement value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static string Number(JsonElement element, string property)
    {
        JsonElement value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null
            ? null
            : value.GetInt32().ToString();
    }

    private static IEnumerable<int> Ints(JsonElement element, string property)
        => element.GetProperty(property).EnumerateArray().Select(v => v.GetInt32());

    private static IEnumerable<string> Strings(JsonElement element, string property)
        => element.GetProperty(property).EnumerateArray().Select(v => v.GetString());

    private static IEnumerable<string> Pairs(
        JsonElement element, string property, string separator = "=")
        => element.GetProperty(property).EnumerateArray()
            .Select(v =>
            {
                JsonElement[] parts = v.EnumerateArray().ToArray();
                string second = parts[1].ValueKind == JsonValueKind.Number
                    ? parts[1].GetInt32().ToString()
                    : parts[1].GetString();
                return parts[0].GetString() + separator + second;
            });
}
