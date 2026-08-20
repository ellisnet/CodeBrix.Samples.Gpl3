// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 3 (2026-08-14) end to end: a property declared <c>,ly:dimension?</c> must
/// accept an EXACT RATIONAL and lay out with it.
/// <para>
/// The defect this fences: <c>ly:dimension?</c> is <c>return scm_number_p (d);</c>
/// upstream (<c>lily/general-scheme.cc</c>) but had been ported as a C# type pattern,
/// <c>a[0] is double || a[0] is long || a[0] is int</c> — trap 10. Exact rationals were
/// therefore REFUSED by the type check, and the property silently kept its previous
/// value with only a programming error to show for it. It mattered because
/// <c>\magnifyStaff</c> scales every grob's <c>baseline-skip</c>, <c>word-space</c> and
/// <c>space-alist</c> by its factor, and the regression suite calls it with
/// <c>#3/4</c>, <c>#5/4</c> and <c>#1/2</c>: 3 × 3/4 = 9/4, an exact rational, so the
/// scaling was dropped on the floor. 2,243 refusals across the suite.
/// </para>
/// <para>
/// Read off the ORACLE before it was asserted (rule 35): pinned LilyPond 2.27.2 renders
/// <c>baseline-skip = #9/4</c> BYTE-IDENTICALLY to <c>#2.25</c>, and renders both
/// DIFFERENTLY from the unoverridden default of 3. Both halves are needed. The first
/// alone would pass on a port that ignored the override in both spellings; the second
/// alone would pass on a port that merely did something arbitrary with it. The broken
/// port failed the pair in a specific way worth naming — it rendered <c>#9/4</c>
/// identically to the DEFAULT, which is what "refused, and quietly" looks like.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class DimensionPropertyEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    /// <summary>
    /// One score whose only variable is the <c>baseline-skip</c> override, or its
    /// absence. A three-line column is used because <c>baseline-skip</c> is the
    /// distance BETWEEN base lines: with two lines one gap moves, with three, two do.
    /// </summary>
    private static string Source(string overrideOrEmpty)
        => Version
            + "\\score { \\new Staff { "
            + overrideOrEmpty
            + "c'1^\\markup \\column { \"one\" \"two\" \"three\" } } \\layout { } }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-dimension-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Renders a source and answers its SVG text.
    /// <para>
    /// Every render is given the SAME base name, in a directory of its own, so that
    /// nothing name-derived can differ between two renders being compared. The
    /// comparison here is of whole documents, so one leaked file name would make every
    /// pair differ and the test would pass for the wrong reason. Point-and-click is
    /// OFF for the same reason the sweep turns it off: an anchor embeds the render's
    /// own scratch directory, and this class compares LAYOUT, not anchors.
    /// </para>
    /// </summary>
    private static string Render(string source)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, "dimension-probe", null, ScratchDirectory(),
            new BatchRunOptions { PointAndClick = false });
        result.SvgPath.Should().NotBeNull();
        return File.ReadAllText(result.SvgPath);
    }

    [Fact]
    public void an_exact_rational_baseline_skip_lays_out_as_its_value_not_as_the_default()
    {
        //Arrange
        string exactRational = Source("\\override TextScript.baseline-skip = #9/4\n");
        string equivalentReal = Source("\\override TextScript.baseline-skip = #2.25\n");
        string unoverridden = Source(string.Empty);

        //Act
        string fromRational = Render(exactRational);
        string fromReal = Render(equivalentReal);
        string fromDefault = Render(unoverridden);

        //Assert
        // The control first, so a failure says which half broke: the override has to
        // MOVE something, or the equality below is satisfied by nothing happening.
        fromReal.Should().NotBe(fromDefault);
        fromRational.Should().Be(fromReal);
        fromRational.Should().NotBe(fromDefault);
    }
}
