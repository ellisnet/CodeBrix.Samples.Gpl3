// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Engine.Bootstrap;
using Lily.Docs;
using Lily.Docs.Snippets;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// The composed-source parity gate: Lily.Docs composes a documentation snippet into the
/// same LilyPond source lilypond-book composes it into.
/// <para>
/// This is the cheap, textual check the Phase-5 plan asks LD2 for. Engraving parity is
/// already a closed claim (gate G1); what is NEW in Phase 5 is the WRAPPER, and the
/// wrapper's whole output is a text file. Comparing that text against the oracle's own
/// costs one frozen reference and catches every option the composer mishandles, without
/// comparing a single pixel.
/// </para>
/// </summary>
public sealed class LilypondSourceComposerTests
{
    private static readonly IReadOnlyList<ComposedReferenceCase> Cases =
        ComposedReferenceCase.ReadAll();

    /// <summary>Every frozen case is present as a reference file.</summary>
    [Fact]
    public void every_frozen_case_has_a_reference_file()
    {
        //Arrange
        List<string> missing = new List<string>();

        //Act
        foreach (ComposedReferenceCase reference in Cases)
        {
            string path = Path.Combine(
                ToolPaths.ComposedReferenceDirectory, reference.ReferenceFile);
            if (!File.Exists(path))
            {
                missing.Add(reference.Name + " -> " + reference.ReferenceFile);
            }
        }

        //Assert
        missing.Should().BeEmpty();
        Cases.Count.Should().Be(28);
    }

    /// <summary>Each case composes to the oracle's own composed source.</summary>
    /// <param name="caseName">The case to compose.</param>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void case_composes_to_the_same_source_the_oracle_composed(string caseName)
    {
        //Arrange
        ComposedReferenceCase reference = Find(caseName);
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);

        //Act
        ComposedSnippet composed = composer.Compose(
            reference.Code, reference.Options, reference.DirectiveLine);

        //Assert
        composed.RelevantContents.Should().Be(
            LilypondSourceComposer.RelevantContents(reference.ReferenceSource));
        composed.UnknownOptions.Should().BeEmpty();
        composed.Options.DeprecatedOptions.Should().BeEmpty();
    }

    /// <summary>
    /// Every case that the oracle wrote its own file for also matches BYTE FOR BYTE,
    /// including the <c>\sourcefileline</c> the relevant-contents comparison drops.
    /// <para>
    /// This is a second and separate claim from the one above, and it is worth holding on
    /// its own: it says the Texinfo package reports a snippet's line number on the SAME
    /// BASE lilypond-book does. Nothing else in the suite would notice if that drifted,
    /// because relevant contents deliberately cannot see it.
    /// </para>
    /// </summary>
    /// <param name="caseName">The case to compose.</param>
    [Theory]
    [MemberData(nameof(OwnReferenceCaseNames))]
    public void case_with_its_own_reference_composes_byte_for_byte(string caseName)
    {
        //Arrange
        ComposedReferenceCase reference = Find(caseName);
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);

        //Act
        ComposedSnippet composed = composer.Compose(
            reference.Code, reference.Options, reference.DirectiveLine);

        //Assert
        composed.Source.Should().Be(reference.ReferenceSource);
    }

    /// <summary>
    /// The oracle deduplicated <c>verbatim</c> against <c>bare</c>, and it should stay
    /// deduplicated: <c>verbatim</c> changes what the DOCUMENT shows and nothing about
    /// what LilyPond draws.
    /// </summary>
    [Fact]
    public void a_processing_independent_option_composes_identically_to_no_option()
    {
        //Arrange
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);
        ComposedReferenceCase plain = Find("dedup-plain");
        ComposedReferenceCase verbatim = Find("dedup-verbatim");

        // Exactly one of the pair was deduplicated; WHICH one is lilypond-book's own
        // processing order and is read, not assumed.
        (plain.WasDeduplicated ^ verbatim.WasDeduplicated).Should().BeTrue();

        //Act
        ComposedSnippet plainComposed = composer.Compose(
            plain.Code, plain.Options, plain.DirectiveLine);
        ComposedSnippet verbatimComposed = composer.Compose(
            verbatim.Code, verbatim.Options, verbatim.DirectiveLine);

        //Assert
        verbatimComposed.RelevantContents.Should().Be(plainComposed.RelevantContents);
    }

    /// <summary>
    /// THE CONTROL for the parity theory. An option that DOES change the engraving must
    /// change the composed source — otherwise a composer that ignored every option would
    /// pass the theory above for every case whose reference happens to be the bare one.
    /// </summary>
    [Fact]
    public void a_processing_relevant_option_changes_the_composed_source()
    {
        //Arrange
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);
        ComposedReferenceCase bare = Find("bare");

        //Act
        ComposedSnippet unmodified = composer.Compose(
            bare.Code, bare.Options, bare.DirectiveLine);
        ComposedSnippet mutated = composer.Compose(
            bare.Code, new[] { "ragged-right" }, bare.DirectiveLine);

        //Assert
        mutated.RelevantContents.Should().NotBe(unmodified.RelevantContents);
        mutated.Source.Should().Contain("ragged-right = ##t");
        unmodified.Source.Should().NotContain("ragged-right");
    }

    /// <summary>
    /// THE CONTROL that says the frozen references are being read at all. A composition
    /// compared against the WRONG reference must fail; if it passed, the theory would be
    /// asserting nothing.
    /// </summary>
    [Fact]
    public void a_case_does_not_match_another_cases_reference()
    {
        //Arrange
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);
        ComposedReferenceCase notime = Find("notime");
        ComposedReferenceCase indent = Find("indent");

        //Act
        ComposedSnippet composed = composer.Compose(
            notime.Code, notime.Options, notime.DirectiveLine);

        //Assert
        composed.RelevantContents.Should().NotBe(
            LilypondSourceComposer.RelevantContents(indent.ReferenceSource));
    }

    /// <summary>
    /// The <c>%%</c> escapes reduce exactly once. A doubled percent is one percent, and a
    /// value already substituted is never rescanned.
    /// </summary>
    [Fact]
    public void percent_escapes_reduce_exactly_once()
    {
        //Arrange
        Dictionary<string, string> values = new Dictionary<string, string>
        {
            { "here", "100%% still doubled" },
        };

        //Act
        string formatted = LilypondSourceComposer.Format("%%%% and %% and %(here)s", values);

        //Assert
        formatted.Should().Be("%% and % and 100%% still doubled");
    }

    /// <summary>
    /// A template naming a value the option set does not supply THROWS rather than
    /// composing a blank. A blank paper block engraves and looks plausible, which is the
    /// failure this refuses to have.
    /// </summary>
    [Fact]
    public void a_template_naming_an_unsupplied_value_throws()
    {
        //Arrange
        Dictionary<string, string> values = new Dictionary<string, string>();

        //Act
        Action act = () => LilypondSourceComposer.Format("%(absent)s", values);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Relevant contents drops exactly the three line kinds lilypond-book drops, and
    /// nothing else.
    /// </summary>
    [Fact]
    public void relevant_contents_drops_position_lines_and_keeps_the_music()
    {
        //Arrange
        string source = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n\\sourcefilename \"a.ly\"\n"
            + "\\sourcefileline 0\nc'4 d'4\n";

        //Act
        string relevant = LilypondSourceComposer.RelevantContents(source);

        //Assert
        relevant.Should().Be("c'4 d'4\n");
    }

    /// <summary>An <c>@lilypondfile</c> names its file rather than a line in the manual.</summary>
    [Fact]
    public void a_file_snippet_names_its_file_in_the_composed_source()
    {
        //Arrange
        LilypondSourceComposer composer =
            new LilypondSourceComposer(TexinfoPageGeometry.AfourPaper);

        //Act
        ComposedSnippet composed = composer.ComposeFile(
            "example.ly", "{ c'4 }\n", Array.Empty<string>());

        //Assert
        composed.Source.Should().Contain("\\sourcefilename \"example.ly\"");
        composed.Source.Should().Contain("\\sourcefileline 0");
        composed.RelevantContents.Should().Contain("{ c'4 }");
    }

    /// <summary>The case names, for the parity theory.</summary>
    /// <returns>One row per frozen case.</returns>
    public static TheoryData<string> CaseNames()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (ComposedReferenceCase reference in Cases)
        {
            data.Add(reference.Name);
        }

        return data;
    }

    /// <summary>The case names the oracle wrote their own reference for.</summary>
    /// <returns>One row per non-deduplicated case.</returns>
    public static TheoryData<string> OwnReferenceCaseNames()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (ComposedReferenceCase reference in Cases)
        {
            if (!reference.WasDeduplicated)
            {
                data.Add(reference.Name);
            }
        }

        return data;
    }

    private static ComposedReferenceCase Find(string name)
    {
        foreach (ComposedReferenceCase reference in Cases)
        {
            if (string.Equals(reference.Name, name, StringComparison.Ordinal))
            {
                return reference;
            }
        }

        throw new InvalidOperationException("no frozen case named '" + name + "'");
    }
}
