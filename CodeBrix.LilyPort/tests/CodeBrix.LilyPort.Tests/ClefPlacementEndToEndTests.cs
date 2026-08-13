// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// Fine-vertical-geometry session (2026-08-12) end to end: a <c>\clef</c> change must
/// reach note placement.
/// <para>
/// The defect these fence: <c>parser-clef.scm</c> sets <c>middleCClefPosition</c> on
/// every <c>\clef</c> and then applies <c>ly:set-middle-C!</c> to fold it into
/// <c>middleCPosition</c> — the property the note-heads engraver actually reads. While
/// that binding was a stub, the apply-context was a silent no-op and every staff in the
/// port placed notes with the treble context default: bass-clef notes sat six staff
/// spaces low, and the octavated <c>G_8</c> clef sat a full octave low, which is what
/// pushed <c>bend-spanner-simple</c> and
/// <c>ssaattbb-template-with-men-women-and-descant</c> onto spurious second pages.
/// </para>
/// <para>
/// Expected positions are hand-computed from <c>parser-clef.scm</c>'s expression
/// <c>middleCClefPosition = oct + clefPosition + c0-pitch</c> with <c>c0-pitch</c> −4
/// for <c>clefs.G</c> and +4 for <c>clefs.F</c>: treble −6, bass +6, G_8 −6 + 7 = +1.
/// A staff position converts to page units at half a staff space, and SVG y grows
/// DOWNWARD, so position p puts a notehead at (middle line − p/2). The explicit
/// <c>\clef treble</c> case is the control that would pass for the wrong reason alone —
/// the broken port answered treble for every clef — which is why each fact here asserts
/// a clef that must come out DIFFERENTLY from treble's +3.0.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class ClefPlacementEndToEndTests
{
    private const string Version = "\\version \"2.27.2\"\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-clefplacement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BatchRunResult Run(string source, string name)
    {
        BatchRunResult result = BatchRunner.RunText(
            source, name, null, ScratchDirectory());

        result.SvgPath.Should().NotBeNull();
        return result;
    }

    /// <summary>
    /// The translate-y of the staff's middle line: the five distinct horizontal
    /// <c>&lt;line&gt;</c> rows, middle one. Vertical lines (x1 == x2) must not be
    /// counted.
    /// </summary>
    private static double MiddleLineY(string svg)
    {
        List<double> ys = new List<double>();
        foreach (Match m in Regex.Matches(
            svg, "<g transform=\"translate\\(([-0-9.]+), ([-0-9.]+)\\)\"[^>]*>\\s*(<line [^>]*>)"))
        {
            Dictionary<string, double> attrs = Regex
                .Matches(m.Groups[3].Value, "\\b(x1|x2|y1|y2)=\"([-0-9.]+)\"")
                .ToDictionary(
                    a => a.Groups[1].Value,
                    a => double.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture));
            if (attrs.Count == 4
                && Math.Abs(attrs["y1"] - attrs["y2"]) < 1e-6
                && Math.Abs(attrs["x2"] - attrs["x1"]) > 5.0)
            {
                ys.Add(Math.Round(
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                    + attrs["y1"], 3));
            }
        }

        List<double> rows = ys.Distinct().OrderBy(y => y).ToList();
        rows.Count.Should().Be(5);
        return rows[2];
    }

    /// <summary>
    /// The notehead's translate-y: of all music-glyph paths (the pure-scale transform
    /// only <c>dump-path</c> emits), the right-most one — the clef and any time
    /// signature digits all sit left of the first note.
    /// </summary>
    private static double NoteheadY(string svg)
    {
        List<(double X, double Y)> glyphs = new List<(double, double)>();
        foreach (Match m in Regex.Matches(
            svg, "<g transform=\"translate\\(([-0-9.]+), ([-0-9.]+)\\)\">\\s*<path transform=\"scale\\("))
        {
            glyphs.Add((
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        }

        glyphs.Count.Should().BeGreaterThan(0);
        return glyphs.OrderBy(g => g.X).Last().Y;
    }

    private static double MiddleCOffsetFromMiddleLine(string clef, string name)
    {
        BatchRunResult result = Run(
            Version + "\\score { \\new Staff { \\clef " + clef + " c'1 } }\n", name);
        string svg = File.ReadAllText(result.SvgPath);
        return Math.Round(NoteheadY(svg) - MiddleLineY(svg), 3);
    }

    [Fact]
    public void a_bass_clef_places_middle_c_three_spaces_above_the_middle_line()
    {
        //Arrange
        // treble: position -6, notehead 3.0 BELOW the middle line (SVG y grows down);
        // bass: position +6, notehead 3.0 ABOVE it. The treble half is the control the
        // broken port answered for every clef; the two must come out differently.

        //Act
        double treble = MiddleCOffsetFromMiddleLine("treble", "clefplacement-treble");
        double bass = MiddleCOffsetFromMiddleLine("bass", "clefplacement-bass");

        //Assert
        (Math.Abs(treble - 3.0) < 1e-3).Should().BeTrue(
            "middle C under a treble clef sits on the first ledger line below"
            + " (got " + treble.ToString("F4", CultureInfo.InvariantCulture) + ")");
        (Math.Abs(bass - -3.0) < 1e-3).Should().BeTrue(
            "middle C under a bass clef sits on the first ledger line above"
            + " (got " + bass.ToString("F4", CultureInfo.InvariantCulture) + ")");
        (bass < treble).Should().BeTrue(
            "a clef change must move the notehead, not restate the treble default");
    }

    [Fact]
    public void a_g8_clef_writes_the_sounding_pitch_an_octave_higher()
    {
        //Arrange
        // G_8: middleCClefPosition = 7 + (-2) + (-4) = +1, so middle C sits just above
        // the middle line at -0.5 — seven positions (one written octave) above treble's
        // +3.0. This is the octavation half the bend-spanner-simple TabStaff pairing
        // exercised, and it must come out differently from plain treble.

        //Act
        double octavated = MiddleCOffsetFromMiddleLine("\"G_8\"", "clefplacement-g8");

        //Assert
        (Math.Abs(octavated - -0.5) < 1e-3).Should().BeTrue(
            "middle C under G_8 is written an octave above sounding"
            + " (got " + octavated.ToString("F4", CultureInfo.InvariantCulture) + ")");
    }
}
