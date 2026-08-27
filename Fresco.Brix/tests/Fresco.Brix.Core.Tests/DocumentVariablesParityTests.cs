// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// <see cref="DocumentVariables"/> against Frescobaldi's own scanner:
/// <c>fixtures/variables.json</c> holds what upstream's <c>variables()</c>
/// answered for each probe document (regenerate with
/// <c>tools/varprobe/gen-variables-fixtures.py</c>, which lifts the pure
/// functions straight out of the read-only checkout and runs them). Nothing
/// here is recorded from the port's own output.
/// </summary>
public class DocumentVariablesParityTests
{
    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "variables.json");

    /// <summary>Every probe name, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> ProbeNames()
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        return fixture.RootElement.EnumerateObject()
            .Select(p => new object[] { p.Name })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal)
            .ToList();
    }

    [Theory]
    [MemberData(nameof(ProbeNames))]
    public void the_variables_match_frescobaldi(string name)
    {
        //Arrange
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        JsonElement probe = fixture.RootElement.GetProperty(name);
        string text = probe.GetProperty("text").GetString();
        Dictionary<string, string> expected = probe.GetProperty("variables")
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString(), StringComparer.Ordinal);

        //Act
        IReadOnlyDictionary<string, string> actual = DocumentVariables.Read(text);

        //Assert — one line per variable so a failure names it.
        actual.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList()
            .Should().BeEquivalentTo(
                expected.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
        foreach (var pair in expected)
        {
            (name + "/" + pair.Key + "=" + actual[pair.Key])
                .Should().Be(name + "/" + pair.Key + "=" + pair.Value);
        }
    }
}
