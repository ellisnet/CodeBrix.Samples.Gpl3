// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// <see cref="PluralExpression"/> against Frescobaldi's own
/// <c>parse_plural_expr</c>: <c>fixtures/i18n/plurals.json</c> holds what
/// <c>i18n/mofile.py</c> ITSELF rewrote each <c>Plural-Forms</c> expression
/// into, and what its compiled rule answered for every count from 0 to 200 and
/// a spread of larger ones. Regenerate with
/// <c>tools/i18nharvest/gen-i18n-fixtures.py</c>. Nothing here is recorded from
/// the port's own output.
/// </summary>
/// <remarks>
/// Two things are asserted, not one: the PYTHON SOURCE the rewrite produces —
/// captured out of upstream's own call to <c>compile()</c>, so it is upstream's
/// string and not a guess at it — and the ANSWERS that source gives. A port
/// that got the same answers by a different route would pass the second and
/// fail the first, which is the point: the rewrite is the odd part.
/// </remarks>
public class PluralExpressionParityTests
{
    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "i18n", "plurals.json");

    private static JsonDocument Fixture()
        => JsonDocument.Parse(File.ReadAllText(FixturePath()));

    /// <summary>The thirteen catalogs' own plural rules.</summary>
    /// <returns>The language codes.</returns>
    public static IEnumerable<object[]> Catalogs()
    {
        using JsonDocument fixture = Fixture();
        return fixture.RootElement.EnumerateObject()
            .Where(p => !p.Name.StartsWith("#", StringComparison.Ordinal))
            .Select(p => new object[] { p.Name })
            .ToList();
    }

    /// <summary>The extra expressions the probe pinned the parser's shape with.</summary>
    /// <returns>The expressions.</returns>
    public static IEnumerable<object[]> Expressions()
    {
        using JsonDocument fixture = Fixture();
        return fixture.RootElement.GetProperty("#expressions").EnumerateObject()
            .Select(p => new object[] { p.Name })
            .ToList();
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void a_catalogs_rewritten_expression_is_frescobaldis(string language)
    {
        //Arrange
        using JsonDocument fixture = Fixture();
        JsonElement entry = fixture.RootElement.GetProperty(language);
        string expression = entry.GetProperty("expression").GetString();

        //Act
        PluralExpression plural = PluralExpression.Parse(expression);

        //Assert — upstream compiles "lambda n: int(<the rewritten tokens>)".
        ("lambda n: int(" + plural.Source + ")")
            .Should().Be(entry.GetProperty("python").GetString());
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void a_catalogs_answers_are_frescobaldis(string language)
    {
        //Arrange
        using JsonDocument fixture = Fixture();
        JsonElement entry = fixture.RootElement.GetProperty(language);
        PluralExpression plural
            = PluralExpression.Parse(entry.GetProperty("expression").GetString());
        long forms = entry.GetProperty("nplurals").GetInt64();

        //Act, Assert
        foreach (var answer in entry.GetProperty("answers").EnumerateObject())
        {
            long count = long.Parse(answer.Name, CultureInfo.InvariantCulture);
            long expected = answer.Value.GetInt64();

            plural.Evaluate(count).Should().Be(
                expected,
                $"{language} n={count} must answer as Frescobaldi's rule does");

            //And the answer is a form the catalog declares it has.
            plural.Evaluate(count).Should().BeLessThan(forms);
        }
    }

    [Theory]
    [MemberData(nameof(Expressions))]
    public void an_expression_rewrites_and_answers_as_frescobaldis_does(string expression)
    {
        //Arrange
        using JsonDocument fixture = Fixture();
        JsonElement entry = fixture.RootElement
            .GetProperty("#expressions").GetProperty(expression);

        //Act
        PluralExpression plural = PluralExpression.Parse(expression);

        //Assert
        ("lambda n: int(" + plural.Source + ")")
            .Should().Be(entry.GetProperty("python").GetString());

        foreach (var answer in entry.GetProperty("answers").EnumerateObject())
        {
            long count = long.Parse(answer.Name, CultureInfo.InvariantCulture);
            plural.Evaluate(count).Should().Be(
                answer.Value.GetInt64(),
                $"'{expression}' n={count} must answer as Frescobaldi's rule does");
        }
    }

    [Fact]
    public void an_empty_expression_has_nothing_to_parse()
    {
        //Arrange, Act, Assert — upstream returns None, which leaves the
        //catalog on its default rule.
        PluralExpression.Parse(null).Should().BeNull();
        PluralExpression.Parse(string.Empty).Should().BeNull();
        PluralExpression.Parse("   ").Should().BeNull();
    }

    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(1L, 0L)]
    [InlineData(2L, 1L)]
    [InlineData(100L, 1L)]
    public void the_default_rule_is_one_against_the_rest(long count, long form)
    {
        //Arrange, Act, Assert — upstream's `lambda n: int(n != 1)'.
        PluralExpression.Default.Evaluate(count).Should().Be(form);
    }
}
