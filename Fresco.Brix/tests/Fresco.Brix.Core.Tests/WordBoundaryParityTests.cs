// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Editor;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// <see cref="WordBoundary"/> against Frescobaldi's own expression:
/// <c>fixtures/wordboundary.json</c> holds the spans python found with the
/// very regex lifted out of <c>wordboundary.py</c> (regenerate with
/// <c>tools/varprobe/gen-wordboundary-fixtures.py</c>). Nothing here is
/// recorded from the port's own output.
/// </summary>
public class WordBoundaryParityTests
{
    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "wordboundary.json");

    /// <summary>Every probe index, as test data.</summary>
    /// <returns>The indexes.</returns>
    public static IEnumerable<object[]> ProbeIndexes()
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        return Enumerable.Range(0, fixture.RootElement.GetArrayLength())
            .Select(i => new object[] { i })
            .ToList();
    }

    [Theory]
    [MemberData(nameof(ProbeIndexes))]
    public void the_word_spans_match_frescobaldi(int index)
    {
        //Arrange
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        JsonElement probe = fixture.RootElement[index];
        string text = probe.GetProperty("text").GetString();
        List<string> expected = probe.GetProperty("spans").EnumerateArray()
            .Select(s => $"{s[0].GetInt32()},{s[1].GetInt32()}")
            .ToList();

        //Act
        List<string> actual = WordBoundary.Boundaries(text)
            .Select(w => $"{w.Start},{w.End}")
            .ToList();

        //Assert
        string.Join(" ", actual).Should().Be(string.Join(" ", expected));
    }
}
