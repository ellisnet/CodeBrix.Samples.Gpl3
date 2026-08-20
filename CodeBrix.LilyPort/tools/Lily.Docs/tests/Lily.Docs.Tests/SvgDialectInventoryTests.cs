// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lily.Docs;
using Lily.Docs.Snippets;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// The SVG dialect gate: what the port's engine actually emits for a documentation snippet,
/// fenced so that a NEW requirement on a downstream renderer cannot appear unnoticed.
/// <para>
/// The frozen vocabulary is <c>tools/Lily.Docs/svg-dialect/inventory.tsv</c> and the
/// specification written from it is <c>README.txt</c> beside it. Both live in THIS
/// repository only: they are measured over engraving derived from the GFDL corpus mirror, so
/// the MIT-licensed packages that implement the specification read it and never take a copy
/// of the pictures.
/// </para>
/// <para>
/// ⚠ THE SET IS ASSERTED, THE COUNTS ARE NOT. An unrecognized element, attribute or font
/// family is a demand on a renderer that nobody agreed to, and it goes red here. The counts
/// move whenever any snippet moves and are recorded for information — see
/// <see cref="SvgDialectInventory"/> for why freezing them would make this gate cry wolf.
/// </para>
/// </summary>
public sealed class SvgDialectInventoryTests : IClassFixture<NotationReferenceFixture>
{
    private readonly NotationReferenceFixture _fixture;

    /// <summary>Creates the gate over the notation fixture's engraved output.</summary>
    /// <param name="fixture">The shared notation render.</param>
    public SvgDialectInventoryTests(NotationReferenceFixture fixture)
    {
        _fixture = fixture;
    }

    private static string BaselinePath =>
        Path.Combine(ToolPaths.SvgDialectDirectory, "inventory.tsv");

    /// <summary>
    /// Eleven elements, and a twelfth is a renderer requirement nobody agreed to.
    /// </summary>
    [Fact]
    public void the_engraved_svg_uses_only_the_elements_the_frozen_dialect_names()
    {
        //Arrange
        SvgDialectInventory baseline = SvgDialectInventory.ReadBaseline(BaselinePath);

        //Act
        SvgDialectInventory measured = SvgDialectInventory.Scan(_fixture.Snippets.ScratchRoot);

        //Assert
        measured.NamesOf(SvgDialectInventory.ElementKind)
            .Should().BeEquivalentTo(baseline.NamesOf(SvgDialectInventory.ElementKind));
    }

    /// <summary>
    /// The attribute vocabulary is closed, which is what lets the specification promise a
    /// renderer that there are no gradients, filters, clip paths or CSS to implement.
    /// </summary>
    [Fact]
    public void the_engraved_svg_uses_only_the_attributes_the_frozen_dialect_names()
    {
        //Arrange
        SvgDialectInventory baseline = SvgDialectInventory.ReadBaseline(BaselinePath);

        //Act
        SvgDialectInventory measured = SvgDialectInventory.Scan(_fixture.Snippets.ScratchRoot);

        //Assert
        measured.NamesOf(SvgDialectInventory.AttributeKind)
            .Should().BeEquivalentTo(baseline.NamesOf(SvgDialectInventory.AttributeKind));
    }

    /// <summary>
    /// ⚠ This is the row a downstream PDF renderer cares about most: every family named here
    /// is one it must resolve or deliberately decline. The generics carry 99.5% of the runs;
    /// the tail names real text and TeX faces, which the family rule says must NEVER be
    /// resolved from the machine's own fonts.
    /// </summary>
    [Fact]
    public void the_engraved_svg_asks_for_only_the_font_families_the_frozen_dialect_names()
    {
        //Arrange
        SvgDialectInventory baseline = SvgDialectInventory.ReadBaseline(BaselinePath);

        //Act
        SvgDialectInventory measured = SvgDialectInventory.Scan(_fixture.Snippets.ScratchRoot);

        //Assert
        measured.NamesOf(SvgDialectInventory.FontFamilyKind)
            .Should().BeEquivalentTo(baseline.NamesOf(SvgDialectInventory.FontFamilyKind));
    }

    /// <summary>
    /// Every member the baseline records is still PRESENT. Counts are not asserted, but a
    /// member falling to zero means a whole construct stopped being emitted, which is a
    /// dialect change in the quiet direction and is exactly the kind of thing an
    /// only-additions gate would miss.
    /// </summary>
    [Fact]
    public void every_frozen_dialect_member_is_still_emitted()
    {
        //Arrange
        SvgDialectInventory baseline = SvgDialectInventory.ReadBaseline(BaselinePath);
        SvgDialectInventory measured = SvgDialectInventory.Scan(_fixture.Snippets.ScratchRoot);

        //Act
        List<string> absent = new List<string>();
        foreach (string kind in new[]
                 {
                     SvgDialectInventory.ElementKind, SvgDialectInventory.AttributeKind,
                     SvgDialectInventory.FontFamilyKind,
                 })
        {
            IReadOnlyList<string> measuredNames = measured.NamesOf(kind);
            foreach (string name in baseline.NamesOf(kind))
            {
                if (!measuredNames.Contains(name, StringComparer.Ordinal))
                {
                    absent.Add(kind + " " + name);
                }
            }
        }

        //Assert
        absent.Should().BeEmpty();
    }

    /// <summary>
    /// ⚠ THE CONTROL (standing rule 7). A gate that reads a fixed vocabulary out of real
    /// output and finds it unchanged proves nothing unless the instrument can be shown to
    /// SEE a change. This feeds the scanner a document carrying constructs the port has never
    /// emitted — a gradient, a filter, a <c>style</c> attribute and an unknown family — and
    /// requires every one of them to surface as a member the baseline does not name.
    /// </summary>
    [Fact]
    public void the_scanner_reports_constructs_the_frozen_dialect_does_not_name()
    {
        //Arrange
        SvgDialectInventory baseline = SvgDialectInventory.ReadBaseline(BaselinePath);
        string directory = Path.Combine(Path.GetTempPath(),
            "lily-docs-dialect-control-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "control.svg"),
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10mm\" height=\"10mm\">\n"
                + "<defs><linearGradient id=\"g\"><stop offset=\"0\"/></linearGradient>\n"
                + "<filter id=\"f\"><feGaussianBlur stdDeviation=\"1\"/></filter></defs>\n"
                + "<rect style=\"fill:red\" clip-path=\"url(#c)\" width=\"5\" height=\"5\"/>\n"
                + "<text font-family=\"Nonesuch Display\">x</text>\n"
                + "</svg>\n");

            //Act
            SvgDialectInventory control = SvgDialectInventory.Scan(directory);
            HashSet<string> newElements = new HashSet<string>(
                control.NamesOf(SvgDialectInventory.ElementKind), StringComparer.Ordinal);
            newElements.ExceptWith(baseline.NamesOf(SvgDialectInventory.ElementKind));
            HashSet<string> newAttributes = new HashSet<string>(
                control.NamesOf(SvgDialectInventory.AttributeKind), StringComparer.Ordinal);
            newAttributes.ExceptWith(baseline.NamesOf(SvgDialectInventory.AttributeKind));
            HashSet<string> newFamilies = new HashSet<string>(
                control.NamesOf(SvgDialectInventory.FontFamilyKind), StringComparer.Ordinal);
            newFamilies.ExceptWith(baseline.NamesOf(SvgDialectInventory.FontFamilyKind));

            //Assert
            newElements.Should().Contain("linearGradient");
            newElements.Should().Contain("filter");
            newElements.Should().Contain("defs");
            newAttributes.Should().Contain("style");
            newAttributes.Should().Contain("clip-path");
            newFamilies.Should().Contain("Nonesuch Display");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// The frozen file and the engraved output describe the same corpus. Guards the case
    /// where the gate reads a baseline that was frozen over a different set of pictures —
    /// the numbers would then be meaningless even though every set comparison passed.
    /// </summary>
    [Fact]
    public void the_frozen_inventory_was_taken_over_the_manuals_full_snippet_output()
    {
        //Arrange
        SvgDialectInventory baseline = SvgDialectInventory.ReadBaseline(BaselinePath);

        //Act
        SvgDialectInventory measured = SvgDialectInventory.Scan(_fixture.Snippets.ScratchRoot);

        //Assert
        measured.FileCount.Should().Be(baseline.FileCount);
    }
}
