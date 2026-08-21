// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Tools;
using SilverAssertions;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The line comparison the convert-ly dialog shows before it touches a document.
/// </summary>
public class TextDiffTests
{
    [Fact]
    public void an_unchanged_document_has_no_changed_rows()
    {
        //Arrange
        const string text = "\\version \"2.14.2\"\n{ c4 d e f }\n";

        //Act
        IReadOnlyList<DiffRow> rows = TextDiff.Compare(text, text);

        //Assert
        TextDiff.ChangeCount(rows).Should().Be(0);
        rows.All(r => r.Kind == DiffKind.Same).Should().BeTrue();
    }

    [Fact]
    public void a_changed_line_is_one_removal_and_one_addition()
    {
        //Arrange
        const string before = "a\nb\nc\n";
        const string after = "a\nB\nc\n";

        //Act
        IReadOnlyList<DiffRow> rows = TextDiff.Compare(before, after);

        //Assert
        rows.Count(r => r.Kind == DiffKind.Removed).Should().Be(1);
        rows.Count(r => r.Kind == DiffKind.Added).Should().Be(1);
        rows.First(r => r.Kind == DiffKind.Removed).Left.Should().Be("b");
        rows.First(r => r.Kind == DiffKind.Added).Right.Should().Be("B");
    }

    [Fact]
    public void line_numbers_follow_each_side_independently()
    {
        //Arrange
        // The left loses a line, so from there on the two sides are one apart --
        // which is the whole reason a side-by-side view carries both numbers.
        const string before = "a\ngone\nb\n";
        const string after = "a\nb\n";

        //Act
        IReadOnlyList<DiffRow> rows = TextDiff.Compare(before, after);
        DiffRow removed = rows.First(r => r.Kind == DiffKind.Removed);
        DiffRow last = rows.Last(r => r.Kind == DiffKind.Same && r.Left == "b");

        //Assert
        removed.LeftNumber.Should().Be(2);
        removed.RightNumber.Should().Be(0);
        last.LeftNumber.Should().Be(3);
        last.RightNumber.Should().Be(2);
    }

    [Fact]
    public void an_unchanged_document_produces_no_unified_diff()
    {
        //Arrange
        const string text = "a\nb\n";

        //Act
        IReadOnlyList<DiffRow> rows = TextDiff.Unified(text, text, "before", "after");

        //Assert
        rows.Should().BeEmpty();
    }

    [Fact]
    public void the_unified_diff_carries_headers_a_hunk_and_context()
    {
        //Arrange
        const string before = "1\n2\n3\n4\n5\n6\n7\n8\n9\n";
        const string after = "1\n2\n3\n4\nFIVE\n6\n7\n8\n9\n";

        //Act
        IReadOnlyList<DiffRow> rows = TextDiff.Unified(before, after, "before", "after");
        List<string> shown = rows
            .Select(r => r.Kind == DiffKind.Added ? r.Right : r.Left)
            .ToList();

        //Assert
        shown[0].Should().Be("--- before");
        shown[1].Should().Be("+++ after");
        shown[2].Should().StartWith("@@");
        shown.Should().Contain("5");
        shown.Should().Contain("FIVE");

        //Three lines of context either side, so 1 is out and 2 is in.
        shown.Should().NotContain("1");
        shown.Should().Contain("2");
    }

    [Fact]
    public void a_document_that_only_grows_is_all_additions()
    {
        //Arrange
        const string before = "a\n";
        const string after = "a\nb\nc\n";

        //Act
        IReadOnlyList<DiffRow> rows = TextDiff.Compare(before, after);

        //Assert
        rows.Count(r => r.Kind == DiffKind.Added).Should().Be(2);
        rows.Any(r => r.Kind == DiffKind.Removed).Should().BeFalse();
    }

    [Fact]
    public void the_comparison_is_newline_agnostic()
    {
        //Arrange
        // The converter normalizes line endings, so a document that differs only in
        // them must not read as every line changed.
        const string unix = "a\nb\nc\n";
        const string windows = "a\r\nb\r\nc\r\n";

        //Act
        IReadOnlyList<DiffRow> rows = TextDiff.Compare(unix, windows);

        //Assert
        TextDiff.ChangeCount(rows).Should().Be(0);
    }
}
