// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 10 (2026-08-15) end to end: <c>show-vertical-skylines</c> and
/// <c>show-horizontal-skylines</c>, whose drawing block was absent from
/// <c>Grob::get_print_stencil</c> — the same function PARITY 8 found missing its
/// whiteout block.
/// <para>
/// Upstream's <c>add_skylines</c> lambda sits OUTSIDE the "does this grob have a
/// stencil" guard and draws each side of the named skyline pair as a 0.1-thick polyline
/// in one of four colours. Nothing in the port called it, so the two properties were
/// inert.
/// </para>
/// <para>
/// Two fences, both relationships (rule 33). The first is against a CONTROL render of
/// the same music with the overrides removed, so no line count is remembered — only
/// that turning the property on DRAWS something and leaving it off does not. The second
/// is what <c>Offset::is_sane</c> decides: a skyline's outermost buildings run to
/// infinity by construction, and upstream's <c>points_to_line_stencil</c> drops any
/// segment with a non-finite end, so no coordinate in the output may be infinite. The
/// port's <c>IsSane</c> tested only for NaN and let sixteen <c>x1="-Infinity"</c> line
/// ends through on one page.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class SkylineDebugDrawingEndToEndTests
{
    private const string Version = "\\version \"" + LilyVersion.CompatibleWithVersion + "\"\n";

    private const string Shown = Version
        + "{ \\override Staff.Clef.show-vertical-skylines = ##t\n"
        + "  \\override Accidental.show-horizontal-skylines = ##t\n"
        + "  cis' }\n";

    private const string Control = Version + "{ cis' }\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-skydebug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Render(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());
        result.SvgPath.Should().NotBeNull();
        return File.ReadAllText(result.SvgPath);
    }

    [Fact]
    public void showing_skylines_draws_lines_the_same_music_does_not_draw_otherwise()
    {
        //Arrange / Act
        string shown = Render(Shown, "skydebug-shown");
        string control = Render(Control, "skydebug-control");

        int shownLines = Regex.Matches(shown, "<line").Count;
        int controlLines = Regex.Matches(control, "<line").Count;

        //Assert
        // The staff lines are in both; only the skyline outlines are in one.
        shownLines.Should().BeGreaterThan(controlLines);
    }

    [Fact]
    public void no_drawn_coordinate_is_infinite()
    {
        //Arrange / Act
        string shown = Render(Shown, "skydebug-finite");

        //Assert
        shown.Should().NotContain("Infinity");
        shown.Should().NotContain("NaN");
    }
}
