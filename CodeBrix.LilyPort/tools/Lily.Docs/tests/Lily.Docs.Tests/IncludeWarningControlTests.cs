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
using Lily.Docs.Manuals;
using Lily.Docs.Rendering;
using SilverAssertions;
using Xunit;

namespace Lily.Docs.Tests;

/// <summary>
/// THE PAIRED CONTROL for every zero-include-warning gate in this suite.
/// <para>
/// A gate that asserts "this manual earned no Include warnings" cannot, on its own,
/// distinguish a manual whose includes all resolved from a warning channel that has stopped
/// reporting. <c>snippets.tely</c> settles it in the same run: it includes forty files, of
/// which thirty-nine are LSR build products that exist NOWHERE in the checkout — a document
/// with a known, countable absence. Thirty-nine warnings means the channel works; anything
/// else means the zero next door proves nothing.
/// </para>
/// <para>
/// Decision D48, ruled 2026-08-19, is what makes this a control rather than a manual: it is
/// not a Phase-5 deliverable, gets no PDF and no QA drop, and is deliberately absent from
/// <see cref="ManualCatalog.All"/>. MEASURED 2026-08-19: two <c>@node</c>s, no chapters, no
/// sections — as a manual it would render a title page and two empty nodes.
/// </para>
/// <para>
/// ⚠ THIS TEST DOES NOT GENERATE. It needs no file the port produces, so it renders straight
/// from the mirror and the vendored assets and costs a fraction of a second — which is also
/// why it is in its own class rather than hanging off the notation fixture.
/// </para>
/// </summary>
public sealed class IncludeWarningControlTests : IDisposable
{
    private readonly string _workDirectory;
    private readonly ManualHtmlRender _render;

    /// <summary>Renders the control document.</summary>
    public IncludeWarningControlTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(),
            "lily-docs-control-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        Directory.CreateDirectory(_workDirectory);

        string versionDirectory = Path.Combine(_workDirectory, "version");
        VersionItexiWriter.Write(versionDirectory);

        // The generated directory is EMPTY on purpose, and still has to exist and be named
        // 'en': this document needs none of the port's nineteen files, and supplying them
        // would let one of them stand in for an include that is supposed to be missing.
        string generatedDirectory = Path.Combine(
            _workDirectory, "generated", RenderPaths.GeneratedDirectoryName);
        Directory.CreateDirectory(generatedDirectory);

        RenderPaths paths = new RenderPaths(generatedDirectory, ToolPaths.AssetsDirectory,
            versionDirectory, ToolPaths.CorpusDirectory);
        _render = new ManualRenderer(paths).RenderHtml(
            ManualCatalog.IncludeWarningControl, Path.Combine(_workDirectory, "html"));
    }

    /// <summary>The control reports exactly thirty nine absent includes.</summary>
    [Fact]
    public void the_control_reports_exactly_thirty_nine_absent_includes()
    {
        //Arrange
        IReadOnlyList<string> warnings = _render.Warnings;

        //Act
        int includeWarnings = warnings.Count(w => WarningSummary.CategoryOf(w) == "Include");

        //Assert
        // MEASURED from the source, not remembered: snippets.tely carries forty @include
        // lines, of which thirty-nine name snippets/*.itely files that upstream generates
        // from the LSR and that exist in no checkout. Documentation/snippets/ holds .ly
        // files, so nothing there stands in for them by accident.
        includeWarnings.Should().Be(39);
    }

    /// <summary>The absent includes are the snippets files the source names.</summary>
    [Fact]
    public void the_absent_includes_are_the_snippets_files_the_source_names()
    {
        //Arrange
        string sourcePath = Path.Combine(ToolPaths.CorpusDirectory, "en", "snippets.tely");
        List<string> expected = Regex
            .Matches(File.ReadAllText(sourcePath), @"^@include\s+(snippets/\S+)",
                RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToList();

        //Act
        string reported = string.Join("\n",
            _render.Warnings.Where(w => WarningSummary.CategoryOf(w) == "Include"));

        //Assert
        // A count alone would pass if the channel reported thirty-nine of the WRONG thing.
        expected.Count.Should().Be(39);
        foreach (string name in expected)
        {
            reported.Should().Contain(name);
        }
    }

    /// <summary>The one include that can resolve does resolve.</summary>
    [Fact]
    public void the_one_include_that_can_resolve_does_resolve()
    {
        //Arrange
        string reported = string.Join("\n",
            _render.Warnings.Where(w => WarningSummary.CategoryOf(w) == "Include"));

        //Act
        bool macrosWereMissed = reported.Contains("macros.itexi", StringComparison.Ordinal);

        //Assert
        // The fortieth include is en/macros.itexi, which the vendored assets carry. Its
        // ABSENCE from the warning list is what keeps this document a control rather than a
        // broken render: the channel is reporting the files that are really missing, and not
        // the one that is really there.
        macrosWereMissed.Should().BeFalse();
    }

    /// <summary>Cleans up the rendered control.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green suite over.
        }
    }
}
