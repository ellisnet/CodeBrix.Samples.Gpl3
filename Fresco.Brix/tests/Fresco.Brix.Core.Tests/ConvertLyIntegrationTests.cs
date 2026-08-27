// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.ConvertLy;
using Fresco.Brix.Tools;
using SilverAssertions;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The engine's convert-ly component as the application drives it: an old
/// document in, an engravable one out, and the version the editor will show.
/// </summary>
/// <remarks>
/// The RULES themselves are verified against LilyPond's own <c>convert-ly</c> in
/// CodeBrix.LilyPort's own suite (302 parity cases). What is checked here is the
/// application's side of the seam — that the version this engine reads is the
/// target, that a wild old file arrives somewhere useful, and that the dialog's
/// diff has something to show.
/// </remarks>
public class ConvertLyIntegrationTests
{
    //A real 2.14-era document: the old \times tuplet spelling, the old
    //\compressFullBarRests name and a two-part version number.
    private const string OldDocument =
        "\\version \"2.14.2\"\n"
        + "\\score {\n"
        + "  \\relative c' {\n"
        + "    \\times 2/3 { c8 d e }\n"
        + "    \\compressFullBarRests R1*4\n"
        + "  }\n"
        + "}\n";

    [Fact]
    public void an_old_document_converts_to_the_version_this_engine_reads()
    {
        //Arrange
        string target = Fresco.Brix.Engrave.LilyPortEngine.CompatibleWithVersion;
        ConversionVersion.TryParse(target, out ConversionVersion to).Should().BeTrue();

        //Act
        ConversionResult result = DocumentConverter.Convert(OldDocument, null, to);

        //Assert
        result.VersionUnknown.Should().BeFalse();
        result.FromVersion.ToString().Should().Be("2.14.2");
        result.Changed.Should().BeTrue();
        result.Text.Should().Contain("\\tuplet 3/2");
        result.Text.Should().NotContain("\\times");
        result.Text.Should().Contain("\\compressEmptyMeasures");
    }

    [Fact]
    public void the_converted_document_declares_a_newer_version_than_it_had()
    {
        //Arrange
        ConversionVersion.TryParse(
            Fresco.Brix.Engrave.LilyPortEngine.CompatibleWithVersion,
            out ConversionVersion to);

        //Act
        ConversionResult result = DocumentConverter.Convert(OldDocument, null, to);

        //Assert
        result.StampedVersion.Should().NotBeNull();
        (result.StampedVersion.Value > result.FromVersion).Should().BeTrue();
        result.Text.Should().Contain(
            "\\version \"" + result.StampedVersion.Value + "\"");
    }

    [Fact]
    public void the_dialog_has_a_diff_to_show_for_an_old_document()
    {
        //Arrange
        ConversionVersion.TryParse(
            Fresco.Brix.Engrave.LilyPortEngine.CompatibleWithVersion,
            out ConversionVersion to);
        ConversionResult result = DocumentConverter.Convert(OldDocument, null, to);

        //Act
        IReadOnlyList<DiffRow> rows = TextDiff.Compare(OldDocument, result.Text);
        IReadOnlyList<DiffRow> unified = TextDiff.Unified(
            OldDocument, result.Text, "current", "converted");

        //Assert
        TextDiff.ChangeCount(rows).Should().BeGreaterThan(0);
        unified.Should().NotBeEmpty();
        unified.Any(r => r.Kind == DiffKind.Added
            && r.Right.Contains("\\tuplet")).Should().BeTrue();
    }

    [Fact]
    public void a_document_the_engine_already_reads_is_left_alone()
    {
        //Arrange
        string current = "\\version \""
            + Fresco.Brix.Engrave.LilyPortEngine.CompatibleWithVersion
            + "\"\n\\relative c' { \\tuplet 3/2 { c8 d e } }\n";

        //Act
        ConversionResult result = DocumentConverter.Convert(current);

        //Assert
        result.Changed.Should().BeFalse();
        result.Text.Should().Be(current);
    }

    [Fact]
    public void the_rules_a_run_will_apply_can_be_listed_before_it_runs()
    {
        //Arrange
        ConversionVersion.TryParse("2.14.2", out ConversionVersion from);
        ConversionVersion.TryParse(
            Fresco.Brix.Engrave.LilyPortEngine.CompatibleWithVersion,
            out ConversionVersion to);

        //Act
        IReadOnlyList<ConversionRule> rules = DocumentConverter.RulesBetween(from, to);

        //Assert
        rules.Should().NotBeEmpty();
        rules.All(r => r.Version > from && r.Version <= to).Should().BeTrue();
        rules.All(r => !string.IsNullOrWhiteSpace(r.Message)).Should().BeTrue();
    }
}
