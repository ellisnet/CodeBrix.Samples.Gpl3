// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using Lily.Docs.Snippets;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// The option DERIVATIONS — the half of lilypond-book's option handling that computes
/// rather than reads, and therefore the half a port gets wrong quietly.
/// <para>
/// Every case here is paired with a control, because most of these derivations show up in
/// the composed source as an ABSENCE (a paper block that lost its line width, a fragment
/// wrapper that did not appear), and an absence is what a renderer that did nothing at all
/// would also produce.
/// </para>
/// </summary>
public sealed class SnippetOptionSetTests
{
    private static SnippetOptionSet For(params string[] options)
        => SnippetOptionSet.For(options, TexinfoPageGeometry.AfourPaper);

    /// <summary>A snippet with no options still carries the format's own defaults.</summary>
    [Fact]
    public void a_snippet_with_no_options_carries_the_format_defaults()
    {
        //Arrange
        //Act
        SnippetOptionSet options = For();

        //Assert
        options.Value(SnippetOptionNames.LineWidth).Should().Be(@"160\mm");
        options.Value(SnippetOptionNames.ExampleIndent).Should().Be(@"10.16\mm");
        options.Value(SnippetOptionNames.Indent).Should().Be(@"0\mm");
        options.Value(SnippetOptionNames.PaperWidth).Should().Be(@"597.508\pt");
        options.Value(SnippetOptionNames.PaperHeight).Should().Be(@"845.047\pt");
        options.Value(SnippetOptionNames.PaperSize)
            .Should().Be("'(cons (* 597.508 pt) (* 845.047 pt))");
        options.IsFragment.Should().BeFalse();
    }

    /// <summary><c>relative</c> implies <c>fragment</c>; the control is that nothing else does.</summary>
    [Fact]
    public void relative_implies_fragment()
    {
        //Arrange
        //Act
        SnippetOptionSet withRelative = For("relative=2");
        SnippetOptionSet withoutRelative = For("staffsize=16");

        //Assert
        withRelative.IsFragment.Should().BeTrue();
        withoutRelative.IsFragment.Should().BeFalse();
    }

    /// <summary><c>nofragment</c> cancels <c>fragment</c> and survives in the option set.</summary>
    [Fact]
    public void nofragment_cancels_fragment_and_remains_listed()
    {
        //Arrange
        //Act
        SnippetOptionSet cancelled = For("fragment", "nofragment");
        SnippetOptionSet uncancelled = For("fragment");

        //Assert
        cancelled.IsFragment.Should().BeFalse();
        cancelled.Has(SnippetOptionNames.NoFragment).Should().BeTrue();
        uncancelled.IsFragment.Should().BeTrue();
    }

    /// <summary>A named paper size is quoted so it reaches LilyPond as a string.</summary>
    [Fact]
    public void a_named_paper_size_is_quoted()
    {
        //Arrange
        //Act
        SnippetOptionSet named = For("papersize=a6");

        //Assert
        named.Value(SnippetOptionNames.PaperSize).Should().Be("\"a6\"");
    }

    /// <summary>
    /// An explicit paper width fills the missing height from the format default and builds
    /// the pair; the control is a snippet that names neither and gets the default pair.
    /// </summary>
    [Fact]
    public void an_explicit_paper_width_constructs_the_paper_size_pair()
    {
        //Arrange
        //Act
        SnippetOptionSet sized = For(@"paper-width=10\cm");
        SnippetOptionSet unsized = For();

        //Assert
        sized.Value(SnippetOptionNames.PaperSize)
            .Should().Be("'(cons (* 10 cm) (* 845.047 pt))");
        sized.Value(SnippetOptionNames.PaperHeight).Should().Be(@"845.047\pt");
        unsized.Value(SnippetOptionNames.PaperSize)
            .Should().Be("'(cons (* 597.508 pt) (* 845.047 pt))");
    }

    /// <summary>
    /// A DERIVED paper size counts as one the snippet asked for, which is what drops the
    /// default line width. The control is the same snippet without the paper option, which
    /// must keep it.
    /// </summary>
    [Fact]
    public void a_derived_paper_size_counts_as_explicitly_given()
    {
        //Arrange
        //Act
        SnippetOptionSet derived = For(@"paper-width=10\cm");
        SnippetOptionSet plain = For();

        //Assert
        derived.WasGivenExplicitly(SnippetOptionNames.PaperSize).Should().BeTrue();
        plain.WasGivenExplicitly(SnippetOptionNames.PaperSize).Should().BeFalse();
    }

    /// <summary>Processing-independent options are kept but never reach the engraver.</summary>
    [Fact]
    public void processing_independent_options_are_excluded_from_the_output_relevant_list()
    {
        //Arrange
        //Act
        SnippetOptionSet independent = For("verbatim", "texidoc", "doctitle");
        SnippetOptionSet relevant = For("ragged-right");

        //Assert
        independent.Has(SnippetOptionNames.Verbatim).Should().BeTrue();
        string.Join(",", independent.OutputRelevant).Should().NotContain("verbatim");
        string.Join(",", independent.OutputRelevant).Should().NotContain("texidoc");
        string.Join(",", independent.OutputRelevant).Should().NotContain("doctitle");
        string.Join(",", relevant.OutputRelevant).Should().Contain("ragged-right");
    }

    /// <summary>
    /// The output-relevant list is what a document's provenance comment shows and what
    /// upstream hashes, so its ORDER is part of the composed bytes.
    /// </summary>
    [Fact]
    public void the_output_relevant_list_is_sorted()
    {
        //Arrange
        //Act
        SnippetOptionSet options = For("ragged-right", "staffsize=16");

        //Assert
        string joined = string.Join(",", options.OutputRelevant);
        joined.Should().Be(@"exampleindent=10.16\mm,indent=0\mm,line-width=160\mm,"
            + @"paper-height=845.047\pt,paper-width=597.508\pt,"
            + "papersize='(cons (* 597.508 pt) (* 845.047 pt)),ragged-right,staffsize=16");
    }

    /// <summary>
    /// No option in the measured corpus vocabulary is unknown to the composer, and none
    /// triggers a deprecated-spelling translation.
    /// </summary>
    /// <param name="option">The option to offer.</param>
    [Theory]
    [InlineData("quote")]
    [InlineData("verbatim")]
    [InlineData("inline")]
    [InlineData("notime")]
    [InlineData("texidoc")]
    [InlineData("doctitle")]
    [InlineData("noindent")]
    [InlineData("ragged-right")]
    [InlineData("noragged-right")]
    [InlineData("fragment")]
    [InlineData("nofragment")]
    [InlineData("relative=2")]
    [InlineData("staffsize=16")]
    [InlineData(@"line-width=8\cm")]
    [InlineData(@"indent=1\cm")]
    [InlineData("papersize=a6")]
    [InlineData(@"paper-width=10\cm")]
    [InlineData(@"paper-height=5\cm")]
    public void a_measured_vocabulary_option_is_neither_unknown_nor_deprecated(string option)
    {
        //Arrange
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);

        //Act
        ComposedSnippet composed = composer.Compose("{ c'4 }", new[] { option }, 1);

        //Assert
        composed.UnknownOptions.Should().BeEmpty();
        composed.Options.DeprecatedOptions.Should().BeEmpty();
    }

    /// <summary>
    /// THE CONTROL for the theory above: an option outside the vocabulary IS reported, so
    /// "no unknown options" means the check ran rather than that nothing can be unknown.
    /// </summary>
    [Fact]
    public void an_option_outside_the_vocabulary_is_reported_as_unknown()
    {
        //Arrange
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);

        //Act
        ComposedSnippet composed = composer.Compose("{ c'4 }", new[] { "nosuchoption" }, 1);

        //Assert
        composed.UnknownOptions.Should().ContainSingle();
        composed.UnknownOptions[0].Should().Be("nosuchoption");
    }

    /// <summary>A deprecated spelling is translated AND reported.</summary>
    [Fact]
    public void a_deprecated_spelling_is_translated_and_reported()
    {
        //Arrange
        //Act
        SnippetOptionSet classic = For("lilyquote");

        //Assert
        classic.Has(SnippetOptionNames.Quote).Should().BeTrue();
        classic.DeprecatedOptions.Should().ContainSingle();
        classic.DeprecatedOptions[0].Should().Contain("lilyquote");
    }

    /// <summary>
    /// The page geometry is derived from the manual's own page-size command, and every
    /// manual in D48's scope declares <c>@afourpaper</c>.
    /// </summary>
    [Fact]
    public void the_page_geometry_follows_the_documents_page_size_command()
    {
        //Arrange
        //Act
        TexinfoPageGeometry afour = TexinfoPageGeometry.ForSource("...\n@afourpaper\n...");
        TexinfoPageGeometry letter = TexinfoPageGeometry.ForSource("...\n@letterpaper\n...");
        TexinfoPageGeometry none = TexinfoPageGeometry.ForSource("no page size here");

        //Assert
        afour.LineWidth.Should().Be(@"160\mm");
        afour.ExampleIndent.Should().Be(@"10.16\mm");
        letter.LineWidth.Should().Be(@"6\in");
        letter.ExampleIndent.Should().Be(@"0.4\in");
        none.LineWidth.Should().Be(@"6\in");
    }
}
