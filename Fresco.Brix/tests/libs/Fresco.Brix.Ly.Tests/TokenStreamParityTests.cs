// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Lex;
using Fresco.Brix.Ly.Slexing;
using State = Fresco.Brix.Ly.Lex.State;
using Token = Fresco.Brix.Ly.Slexing.Token;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// The tokenizer against python-ly itself: every fixture under
/// <c>fixtures/tokens</c> pairs a real <c>.ly</c> document with the token
/// stream python-ly v0.9.10 produced for it (position, end, class, text —
/// regenerate with <c>tools/lexprobe/gen-token-fixtures.py</c>). The port must
/// reproduce the stream token for token; nothing here is recorded from the
/// port's own output.
/// </summary>
public class TokenStreamParityTests
{
    private static readonly Dictionary<string, string> ModuleNamespaces
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "_token", "Fresco.Brix.Ly.Lex" },
            { "lilypond", "Fresco.Brix.Ly.Lex.LilyPondMode" },
            { "scheme", "Fresco.Brix.Ly.Lex.SchemeMode" },
            { "html", "Fresco.Brix.Ly.Lex.HtmlMode" },
            { "texinfo", "Fresco.Brix.Ly.Lex.TexinfoMode" },
            { "latex", "Fresco.Brix.Ly.Lex.LatexMode" },
            { "docbook", "Fresco.Brix.Ly.Lex.DocbookMode" },
            { "mup", "Fresco.Brix.Ly.Lex.MupMode" },
        };

    private static string FixturesDirectory()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "tokens");

    /// <summary>Every fixture base name, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> FixtureNames()
        => Directory.GetFiles(FixturesDirectory(), "*.tokens.tsv")
            .Select(p => new object[]
                { Path.GetFileName(p).Replace(".tokens.tsv", string.Empty) })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void the_token_stream_matches_python_ly(string name)
    {
        //Arrange
        string directory = FixturesDirectory();
        string text = File.ReadAllText(Path.Combine(directory, name + ".ly"))
            .Replace("\r", string.Empty);
        string[] lines = File.ReadAllLines(Path.Combine(directory, name + ".tokens.tsv"));
        string mode = lines[0].Substring("# mode: ".Length);

        //Act
        State state = Modes.CreateState(mode);
        List<Token> tokens = state.Tokens(text).ToList();

        //Assert
        int expectedCount = lines.Length - 1;
        for (int i = 0; i < Math.Min(expectedCount, tokens.Count); i++)
        {
            string[] fields = lines[i + 1].Split('\t');

            //The two documented class renames (System.String / a base-name
            //clash): python String → StringBase, Error → ErrorBase.
            string className = fields[3] switch
            {
                "String" => "StringBase",
                "Error" => "ErrorBase",
                _ => fields[3],
            };
            string expectedType = ModuleNamespaces[fields[2]] + "." + className;
            string expectedText = JsonSerializer.Deserialize<string>(fields[4]);

            //One composite line per token so a mismatch names position, class
            //and text at once.
            Token actual = tokens[i];
            string actualLine =
                $"{name}[{i}] {actual.Pos}:{actual.End} {actual.GetType().FullName} |{actual.Text}|";
            string expectedLine =
                $"{name}[{i}] {fields[0]}:{fields[1]} {expectedType} |{expectedText}|";
            actualLine.Should().Be(expectedLine);
        }

        tokens.Count.Should().Be(expectedCount);
    }
}
