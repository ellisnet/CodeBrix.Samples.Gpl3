// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using SilverAssertions;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The document variables a user writes in a comment, per upstream's
/// <c>variables.py</c> semantics.
/// </summary>
public class DocumentVariablesTests
{
    [Fact]
    public void variables_are_read_from_a_marked_comment()
    {
        //Arrange
        string text = "% -*- indent-width: 4; coding: utf-8; -*-\n\\relative c' { c d e }\n";

        //Act
        IReadOnlyDictionary<string, string> variables = DocumentVariables.Read(text);

        //Assert
        variables["indent-width"].Should().Be("4");
        variables["coding"].Should().Be("utf-8");
    }

    [Fact]
    public void a_line_without_the_marker_declares_nothing()
    {
        //Arrange
        string text = "% indent-width: 4;\n\\relative c' { c d e }\n";

        //Act
        IReadOnlyDictionary<string, string> variables = DocumentVariables.Read(text);

        //Assert
        variables.Should().BeEmpty();
    }

    [Fact]
    public void the_last_lines_are_scanned_too()
    {
        //Arrange — more than ten lines, so only the first five and last five
        //are looked at; the marker is on the very last one.
        List<string> lines = Enumerable.Range(1, 20)
            .Select(n => "c" + n.ToString())
            .ToList();
        lines.Add("% -*- indent-width: 8; -*-");
        string text = string.Join("\n", lines);

        //Act
        string value = DocumentVariables.Get(text, "indent-width");

        //Assert
        value.Should().Be("8");
    }

    [Fact]
    public void the_middle_of_a_long_document_is_not_scanned()
    {
        //Arrange
        List<string> lines = Enumerable.Range(1, 20).Select(n => "c" + n).ToList();
        lines.Insert(10, "% -*- indent-width: 8; -*-");
        string text = string.Join("\n", lines);

        //Act
        string value = DocumentVariables.Get(text, "indent-width");

        //Assert
        value.Should().BeNull();
    }

    [Fact]
    public void the_run_continues_over_lines_repeating_the_comment_prefix()
    {
        //Arrange
        string text = "% -*- indent-width: 4;\n% coding: latin1;\n\\relative c' { c }\n";

        //Act
        IReadOnlyDictionary<string, string> variables = DocumentVariables.Read(text);

        //Assert
        variables["indent-width"].Should().Be("4");
        variables["coding"].Should().Be("latin1");
    }

    [Fact]
    public void real_content_on_a_line_ends_the_run()
    {
        //Arrange
        string text = "% -*- indent-width: 4;\n\\relative c' { c }\n% coding: latin1;\n";

        //Act
        IReadOnlyDictionary<string, string> variables = DocumentVariables.Read(text);

        //Assert
        variables.Should().ContainKey("indent-width");
        variables.Should().NotContainKey("coding");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("ON", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    public void a_flag_reads_the_words_upstream_accepts(string written, bool expected)
    {
        //Arrange
        string text = "% -*- document-tabs: " + written + "; -*-\n";

        //Act
        bool value = DocumentVariables.GetBool(text, "document-tabs", !expected);

        //Assert
        value.Should().Be(expected);
    }

    [Fact]
    public void an_unreadable_flag_keeps_the_default()
    {
        //Arrange
        string text = "% -*- document-tabs: perhaps; -*-\n";

        //Act
        bool value = DocumentVariables.GetBool(text, "document-tabs", true);

        //Assert
        value.Should().BeTrue();
    }

    [Fact]
    public void an_unreadable_number_keeps_the_default()
    {
        //Arrange
        string text = "% -*- indent-width: wide; -*-\n";

        //Act
        int value = DocumentVariables.GetInt(text, "indent-width", 2);

        //Assert
        value.Should().Be(2);
    }

    [Fact]
    public void several_variables_share_one_line()
    {
        //Arrange
        string text = "%% -*- indent-tabs: no; indent-width: 3; tab-width: 4; -*-\n";

        //Act
        IReadOnlyDictionary<string, string> variables = DocumentVariables.Read(text);

        //Assert
        variables.Count.Should().Be(3);
        variables["tab-width"].Should().Be("4");
    }

    [Fact]
    public void a_later_declaration_wins()
    {
        //Arrange — the top five lines are read first, then the bottom five.
        List<string> lines = new List<string> { "% -*- indent-width: 2; -*-" };
        lines.AddRange(Enumerable.Range(1, 20).Select(n => "c" + n));
        lines.Add("% -*- indent-width: 6; -*-");

        //Act
        string value = DocumentVariables.Get(string.Join("\n", lines), "indent-width");

        //Assert
        value.Should().Be("6");
    }
}
