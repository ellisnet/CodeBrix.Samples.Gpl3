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
using System.Text.RegularExpressions;
using Lily.Docs;
using Lily.Docs.Generation;
using Lily.Docs.Rendering;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// The LD1 gates: the Internals Reference generates, renders to both formats, and does
/// so with exactly the warnings that were measured and reviewed.
/// </summary>
public sealed class InternalsReferenceRenderTests : IClassFixture<InternalsReferenceFixture>
{
    private readonly InternalsReferenceFixture _fixture;

    /// <summary>Creates the test class over the shared render.</summary>
    /// <param name="fixture">The one generation-and-render run.</param>
    public InternalsReferenceRenderTests(InternalsReferenceFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Generation writes all nineteen documentation files.</summary>
    [Fact]
    public void generation_writes_all_nineteen_documentation_files()
    {
        //Arrange
        DocumentationGenerationResult generation = _fixture.Generation;

        //Act
        IReadOnlyList<string> missing = generation.MissingFiles;

        //Assert
        missing.Should().BeEmpty();
        DocumentationGenerator.ExpectedOutputs.Count.Should().Be(19);
    }

    /// <summary>Html render warnings match the frozen baseline exactly.</summary>
    [Fact]
    public void html_render_warnings_match_the_frozen_baseline_exactly()
    {
        //Arrange
        string baselinePath = Path.Combine(
            ToolPaths.ExpectedWarningsDirectory, _fixture.Manual.Name + ".tsv");
        SortedDictionary<string, int> expected = WarningSummary.ReadBaseline(baselinePath);

        //Act
        SortedDictionary<string, int> actual = WarningSummary.Count(_fixture.Html.Warnings);

        //Assert
        // Asserted in BOTH directions on purpose. A category that disappeared is as much
        // a change to look at as one that grew: the baseline is a description of a run
        // that was read, not a ceiling.
        actual.Should().BeEquivalentTo(expected);
    }

    /// <summary>Html render produces no include warnings.</summary>
    [Fact]
    public void html_render_produces_no_include_warnings()
    {
        //Arrange
        IReadOnlyList<string> warnings = _fixture.Html.Warnings;

        //Act
        int includeWarnings = warnings.Count(w => WarningSummary.CategoryOf(w) == "Include");

        //Assert
        // The Internals Reference has exactly one @include, which reaches three vendored
        // files transitively. A single Include warning means one of them did not resolve
        // and the manual rendered without its macros.
        includeWarnings.Should().Be(0);
    }

    /// <summary>Html render produces no unresolved reference warnings.</summary>
    [Fact]
    public void html_render_produces_no_unresolved_reference_warnings()
    {
        //Arrange
        IReadOnlyList<string> warnings = _fixture.Html.Warnings;

        //Act
        int referenceWarnings = warnings.Count(w => WarningSummary.CategoryOf(w) == "Reference");

        //Assert
        // The manual carries 11,075 @iref and 766 @anchor; the packages aggregate
        // unresolved references into one Reference warning, so its absence is the claim
        // that every one of them resolved.
        referenceWarnings.Should().Be(0);
    }

    /// <summary>Every internal link in the html points at an id that exists.</summary>
    [Fact]
    public void every_internal_link_in_the_html_points_at_an_id_that_exists()
    {
        //Arrange
        string html = _fixture.HtmlText;
        HashSet<string> ids = new HashSet<string>(
            Regex.Matches(html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value));

        //Act
        List<string> targets = Regex.Matches(html, "href=\"#([^\"]+)\"")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        List<string> dangling = targets.Where(t => !ids.Contains(t)).ToList();

        //Assert
        // The strongest structural claim available without a fidelity target: not "the
        // render finished" but "every link it wrote goes somewhere".
        dangling.Should().BeEmpty();
        targets.Count.Should().BeGreaterThan(1000);
        ids.Count.Should().BeGreaterThan(2000);
    }

    /// <summary>Html carries one heading for every node in the source.</summary>
    [Fact]
    public void html_carries_one_heading_for_every_node_in_the_source()
    {
        //Arrange
        string sourcePath = Path.Combine(_fixture.Generation.OutputDirectory, "internals.texi");
        string source = File.ReadAllText(sourcePath);
        int nodeCount = Regex.Matches(source, "^@node ", RegexOptions.Multiline).Count;

        //Act
        int headingCount = Regex.Matches(_fixture.HtmlText, "<h[1-6][ >]").Count;

        //Assert
        // Counted against the SOURCE rather than against a remembered number, so a
        // generation change that adds or drops nodes moves both sides together and this
        // gate keeps meaning the same thing.
        nodeCount.Should().Be(810);
        headingCount.Should().Be(nodeCount);
    }

    /// <summary>Html carries the gfdl notice.</summary>
    [Fact]
    public void html_carries_the_gfdl_notice()
    {
        //Arrange
        string html = _fixture.HtmlText;

        //Act
        bool hasNotice = html.Contains("GNU Free Documentation License");

        //Assert
        // The notice is IN the generated content (macros.itexi emits it); this gate is
        // that rendering did not drop it on the way through.
        hasNotice.Should().BeTrue();
    }

    /// <summary>Pdf render drops no characters.</summary>
    [Fact]
    public void pdf_render_drops_no_characters()
    {
        //Arrange
        IReadOnlyList<string> pdfWarnings = _fixture.Pdf.PdfWarnings;

        //Act
        int count = pdfWarnings.Count;

        //Assert
        // The manual's entire non-ASCII inventory is Latin-1 plus one dash, all of it
        // inside the Roboto/Merriweather range the Html2Pdf chain ships. A dropped
        // character would be warned by the PDF stage, so any PDF-stage warning at all is
        // a red gate here rather than a tolerated one.
        count.Should().Be(0);
    }

    /// <summary>Pdf facts match the recorded baseline.</summary>
    [Fact]
    public void pdf_facts_match_the_recorded_baseline()
    {
        //Arrange
        string baselinePath = Path.Combine(
            ToolPaths.ExpectedWarningsDirectory, _fixture.Manual.Name + "-pdf.tsv");
        SortedDictionary<string, string> baseline =
            WarningSummary.ReadPdfBaselineValues(baselinePath);

        //Act
        SortedDictionary<string, string> actual = _fixture.Pdf.BaselineValues();

        //Assert
        // Recorded rather than reasoned about: a page count that moves is a signal that
        // something upstream of the layout changed, and it is cheap to notice.
        //
        // ⚠ IT MOVED AT WAVE LD4, 1,349 -> 1,266, FOR TWO KNOWN REASONS AT ONCE — the page
        // size became A4 and the packages' line-metrics fix rode along in the same pin bump.
        // Two known reasons is one too many to tell apart after the fact, which is why the
        // page SIZE is now frozen in this same file: the next time this number moves, the
        // size row says whether the page changed or the layout did.
        actual.Should().BeEquivalentTo(baseline);
    }

    /// <summary>Every page of the pdf is a4.</summary>
    [Fact]
    public void every_page_of_the_pdf_is_a4()
    {
        //Arrange
        SortedSet<string> sizes = PdfPageBoxes.DistinctPageSizes(_fixture.PdfBytes);

        //Act
        int pagesMeasured = PdfPageBoxes.ReadMediaBoxes(_fixture.PdfBytes).Count;

        //Assert
        // ⚠ ASKED OF THE FILE. Wave LD1 shipped this manual at US Letter for a day with every
        // gate green, because nothing in the suite ever looked at the paper — the size was
        // settable, was left at the package default, and no count depends on it.
        pagesMeasured.Should().Be(_fixture.Pdf.PageCount);
        sizes.Should().BeEquivalentTo(new SortedSet<string>(StringComparer.Ordinal) { "595x842" });
    }

    /// <summary>The manual contains no engraved snippets.</summary>
    [Fact]
    public void the_manual_contains_no_engraved_snippets()
    {
        //Arrange
        string sourcePath = Path.Combine(_fixture.Generation.OutputDirectory, "internals.texi");
        string source = File.ReadAllText(sourcePath);

        //Act
        int snippets = Regex.Matches(source, "^@lilypond", RegexOptions.Multiline).Count;

        //Assert
        // The Internals Reference states this about itself ("@c @lilypond is not allowed
        // in the IR."), and LD1 depends on it: this is the one manual that renders with
        // no engraving seam. If this ever goes non-zero, LD1's gates stop covering the
        // whole manual and the seam from LD2 is required here too.
        snippets.Should().Be(0);
        _fixture.Html.Result.Images.Count.Should().Be(0);
    }
}
