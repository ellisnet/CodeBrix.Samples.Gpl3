// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.IO;
using Lily.Docs.Rendering;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// The instrument's own tests. A baseline gate is only worth what its reader is worth,
/// and the first failure of a new gate is as likely to be the gate as the render — so
/// the counting and the round-trip are fenced here, with controls, before any render
/// gate leans on them.
/// </summary>
public sealed class WarningSummaryTests
{
    /// <summary>Category is read from the message prefix.</summary>
    [Fact]
    public void category_is_read_from_the_message_prefix()
    {
        //Arrange
        string message = "Include: Include file 'en/cyrillic.itexi' was not found on the search path.";

        //Act
        string category = WarningSummary.CategoryOf(message);

        //Assert
        category.Should().Be("Include");
    }

    /// <summary>An unfamiliar prefix lands in its own bucket rather than a neighbours.</summary>
    [Fact]
    public void an_unfamiliar_prefix_lands_in_its_own_bucket_rather_than_a_neighbours()
    {
        //Arrange
        string message = "Sideways: something the packages did not used to say.";

        //Act
        string category = WarningSummary.CategoryOf(message);

        //Assert
        // THE CONTROL FOR SILENT FOLDING. If an unknown category were quietly counted as
        // a known one, a new warning class could appear without moving any baseline row.
        category.Should().Be(WarningSummary.UncategorizedName);
    }

    /// <summary>A category name appearing mid message is not mistaken for a prefix.</summary>
    [Fact]
    public void a_category_name_appearing_mid_message_is_not_mistaken_for_a_prefix()
    {
        //Arrange
        string message = "Emit: the Include directive was fine.";

        //Act
        string category = WarningSummary.CategoryOf(message);

        //Assert
        category.Should().Be("Emit");
    }

    /// <summary>Counts are grouped by category.</summary>
    [Fact]
    public void counts_are_grouped_by_category()
    {
        //Arrange
        string[] messages =
        {
            "Include: one", "Include: two", "Emit: three", "RawBlockSkipped: four",
        };

        //Act
        SortedDictionary<string, int> counts = WarningSummary.Count(messages);

        //Assert
        counts["Include"].Should().Be(2);
        counts["Emit"].Should().Be(1);
        counts["RawBlockSkipped"].Should().Be(1);
        counts.Count.Should().Be(3);
    }

    /// <summary>A baseline round trips through the file.</summary>
    [Fact]
    public void a_baseline_round_trips_through_the_file()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(),
            "lily-docs-baseline-" + System.Guid.NewGuid().ToString("N").Substring(0, 8) + ".tsv");
        SortedDictionary<string, int> written = WarningSummary.Count(
            new[] { "Include: a", "Include: b", "Emit: c" });

        //Act
        WarningSummary.WriteBaseline(path, written);
        SortedDictionary<string, int> read = WarningSummary.ReadBaseline(path);

        //Assert
        try
        {
            read.Should().BeEquivalentTo(written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A mutated count does not compare equal to its baseline.</summary>
    [Fact]
    public void a_mutated_count_does_not_compare_equal_to_its_baseline()
    {
        //Arrange
        SortedDictionary<string, int> baseline = WarningSummary.Count(
            new[] { "Include: a", "Emit: b" });
        SortedDictionary<string, int> mutated = WarningSummary.Count(
            new[] { "Include: a", "Include: extra", "Emit: b" });

        //Act
        bool same = baseline.Count == mutated.Count
            && baseline["Include"] == mutated["Include"];

        //Assert
        // THE CONTROL FOR THE BASELINE GATE ITSELF. The render gate asserts equivalence
        // against a frozen file; this is the paired mutation proving that assertion can
        // actually go red, so a passing baseline test means the counts matched rather
        // than that nothing was compared.
        same.Should().BeFalse();
        mutated["Include"].Should().Be(2);
        baseline["Include"].Should().Be(1);
    }

    /// <summary>The total line is not read back as a category.</summary>
    [Fact]
    public void the_total_line_is_not_read_back_as_a_category()
    {
        //Arrange
        string path = Path.Combine(Path.GetTempPath(),
            "lily-docs-total-" + System.Guid.NewGuid().ToString("N").Substring(0, 8) + ".tsv");
        WarningSummary.WriteBaseline(path, WarningSummary.Count(new[] { "Emit: a" }));

        //Act
        SortedDictionary<string, int> read = WarningSummary.ReadBaseline(path);

        //Assert
        try
        {
            read.ContainsKey("TOTAL").Should().BeFalse();
            read.Count.Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
