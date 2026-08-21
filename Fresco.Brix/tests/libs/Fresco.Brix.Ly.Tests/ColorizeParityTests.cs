// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Colorizing;
using Fresco.Brix.Ly.Lex;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// <see cref="Colorize"/> against python-ly itself: <c>fixtures/colorize</c>
/// holds the css_class python-ly v0.9.10's own <c>css_mapper()</c> resolved
/// for every token of every probe document, plus its whole
/// <c>default_mapping()</c> and <c>default_scheme</c> flattened
/// (regenerate with <c>tools/colorizeprobe/gen-colorize-fixtures.py</c>).
/// Nothing here is recorded from the port's own output.
/// </summary>
public class ColorizeParityTests
{
    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "colorize");

    /// <summary>Every fixture base name, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> FixtureNames()
        => Directory.GetFiles(FixturesDirectory(), "*.colorize.tsv")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".colorize.tsv", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void every_token_maps_to_the_same_style_as_python_ly(string name)
    {
        //Arrange
        string[] fixture = File.ReadAllLines(
            Path.Combine(FixturesDirectory(), name + ".colorize.tsv"));
        string mode = fixture[0].Substring("# mode: ".Length).Trim();
        string text = File.ReadAllText(
            Path.Combine(FixturesDirectory(), name + ".ly"))
            .Replace("\r", string.Empty);
        TokenMapper<CssClass> mapper = Colorize.CssMapper();

        //Act
        List<string> actual = Modes.CreateState(mode).Tokens(text)
            .Select(t => Format(t.Pos, t.End, PythonName(t.GetType()), mapper.ValueFor(t)))
            .ToList();

        //Assert
        List<string> expected = fixture.Skip(1)
            .Select(line => line.Split('\t'))
            .Select(c => c[0] + "\t" + c[1] + "\t" + c[2] + "\t" + c[3] + "\t"
                + c[4] + "\t" + c[5])
            .ToList();
        actual.Count.Should().Be(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            actual[i].Should().Be(expected[i]);
        }
    }

    [Fact]
    public void the_default_mapping_matches_python_ly()
    {
        //Arrange
        string[] fixture = File.ReadAllLines(
            Path.Combine(FixturesDirectory(), "_mapping.tsv"));

        //Act
        List<string> actual = Colorize.DefaultMapping()
            .SelectMany(g => g.Styles.SelectMany(s => s.Classes.Select(c =>
                $"{g.Mode}\t{s.Name}\t{s.Base ?? "None"}\t{PythonName(c)}")))
            .ToList();

        //Assert
        actual.Count.Should().Be(fixture.Length);
        for (int i = 0; i < fixture.Length; i++)
        {
            actual[i].Should().Be(fixture[i]);
        }
    }

    [Fact]
    public void the_default_scheme_matches_python_ly()
    {
        //Arrange
        string[] fixture = File.ReadAllLines(
            Path.Combine(FixturesDirectory(), "_scheme.tsv"));
        CssScheme scheme = CssScheme.Default;

        //Act
        List<string> actual = new List<string>();
        foreach (var style in scheme.BaseStyles.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            foreach (var p in style.Value.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                actual.Add($"\t{style.Key}\t{p.Key}\t{p.Value}");
            }
        }

        foreach (var mode in scheme.Modes)
        {
            var styles = scheme.ModeStyles(mode);
            foreach (var style in styles.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                foreach (var p in style.Value.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    actual.Add($"{mode}\t{style.Key}\t{p.Key}\t{p.Value}");
                }
            }
        }

        //Assert — order-insensitive: python iterates a dict, we iterate ours.
        actual.OrderBy(l => l, StringComparer.Ordinal).ToList()
            .Should().BeEquivalentTo(
                fixture.OrderBy(l => l, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void the_base_style_merges_under_the_mode_style()
    {
        //Arrange — lilypond markup inherits 'function' (bold, blue) and
        //overrides both properties in the lilypond group.
        CssClass markup = new CssClass("lilypond", "markup", "function");

        //Act
        IDictionary<string, string> css = Colorize.CssDict(markup);

        //Assert
        css["font-weight"].Should().Be("normal");
        css["color"].Should().Be("#008000");
    }

    [Fact]
    public void an_unmapped_token_class_answers_null()
    {
        //Arrange
        TokenMapper<CssClass> mapper = Colorize.CssMapper();

        //Act
        CssClass style = mapper.ValueForClass(typeof(Space));

        //Assert
        style.Should().BeNull();
    }

    /// <summary>The python class name for a ported token class.</summary>
    /// <param name="type">The ported class.</param>
    /// <returns>The upstream name.</returns>
    private static string PythonName(Type type)
        //The two documented lex renames: python String → StringBase (System.String
        //is taken) and python Error → ErrorBase.
        => type.Name switch
        {
            "StringBase" => "String",
            "ErrorBase" => "Error",
            _ => type.Name,
        };

    private static string Format(int pos, int end, string className, CssClass style)
        => style == null
            ? $"{pos}\t{end}\t{className}\tNone\tNone\tNone"
            : $"{pos}\t{end}\t{className}\t{style.Mode}\t{style.Name}\t{style.Base ?? "None"}";
}
