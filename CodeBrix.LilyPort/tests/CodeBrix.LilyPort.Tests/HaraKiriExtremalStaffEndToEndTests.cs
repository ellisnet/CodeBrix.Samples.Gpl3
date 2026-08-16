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
/// A staff must DECIDE whether it is removing itself before it can be asked whether it
/// is alive — <c>Staff_grouper_interface::get_extremal_staff</c>'s
/// <c>consider_suicide</c> call.
/// </summary>
/// <remarks>
/// <para>
/// Without it, an undecided <c>remove-empty</c> group answers "live",
/// <c>Side_position_interface::move_to_extremal_staff</c> reparents the bar number INTO
/// that staff, and the staff then kills itself and its children — so a line whose staves
/// have all gone lost its bar number, and the system, having nothing left, reported
/// "system with empty extent". Upstream's loop finds nothing on such a line, declines to
/// move the grob, and the bar number keeps the System as its parent and is drawn.
/// </para>
/// <para>
/// The note that stood where the call belongs said suicide was unported and every staff
/// stayed live. That stopped being true when line breaking landed — trap 18, a stale
/// named absence that nothing re-checked.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class HaraKiriExtremalStaffEndToEndTests
{
    // hara-kiri-staff.ly's own material (rule 35b). The second line is the one that
    // matters: every staff rests through it, so every staff removes itself, and the
    // texidoc of that file says in as many words that the line still carries bar
    // number 2.
    private const string RemoveEmptyLayout =
        "\\layout {\n"
        + "  ragged-right = ##t\n"
        + "  \\context { \\Staff \\RemoveEmptyStaves }\n"
        + "}\n";

    private const string EmptyMiddleLine =
        "\\transpose c c''\n"
        + "\\context GrandStaff <<\n"
        + "  \\new Staff {  c4 c c c \\break s1 \\break c4 c c c \\break c c c c}\n"
        + "  \\new Staff {  d4 d d d        s1        s1              s1 }\n"
        + "  \\new Staff {  e4 e e e        s1        e4 e e e        s1 }\n"
        + ">>\n";

    // THE CONTROL: the same four lines with the middle one occupied, so no line ever
    // loses all of its staves. It must carry the same bar numbers either way — a build
    // that drew no bar numbers at all, or drew them for a different reason, fails here.
    private const string OccupiedMiddleLine =
        "\\transpose c c''\n"
        + "\\context GrandStaff <<\n"
        + "  \\new Staff {  c4 c c c \\break c4 c c c \\break c4 c c c \\break c c c c}\n"
        + "  \\new Staff {  d4 d d d        s1        s1              s1 }\n"
        + "  \\new Staff {  e4 e e e        s1        e4 e e e        s1 }\n"
        + ">>\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-harakiri-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BatchRunResult Render(string music, string name)
        => BatchRunner.RunText(
            "\\version \"2.16.0\"\n" + RemoveEmptyLayout + music,
            name,
            null,
            ScratchDirectory());

    private static bool HasBarNumber(string svg, string number)
        => svg.Contains("<tspan>" + number + "</tspan>", StringComparison.Ordinal);

    [Fact]
    public void a_line_whose_staves_all_removed_themselves_keeps_its_bar_number()
    {
        //Arrange / Act
        BatchRunResult emptied = Render(EmptyMiddleLine, "harakiri-empty-line");
        BatchRunResult occupied = Render(OccupiedMiddleLine, "harakiri-occupied-line");

        string emptiedSvg = File.ReadAllText(emptied.SvgPath);
        string occupiedSvg = File.ReadAllText(occupied.SvgPath);

        //Assert
        // THE RELATIONSHIP: the four lines start at bars 1, 2, 3 and 4 in both scores,
        // so both must carry bar numbers 2, 3 and 4. (Bar 1's number is suppressed by
        // LilyPond's default bar-number-visibility, in both.)
        foreach (string number in new[] { "2", "3", "4" })
        {
            HasBarNumber(occupiedSvg, number).Should().BeTrue();
            HasBarNumber(emptiedSvg, number).Should().BeTrue();
        }
    }

    [Fact]
    public void a_line_whose_staves_all_removed_themselves_still_has_an_extent()
    {
        //Arrange / Act
        BatchRunResult emptied = Render(EmptyMiddleLine, "harakiri-extent");

        //Assert
        // The bar number is the only thing left on that line, so it is also the only
        // thing giving the system a vertical extent. Reparenting it into a dying staff
        // therefore produced this report as well as losing the number, and the two are
        // one defect rather than two.
        string.Join(" || ", emptied.Diagnostics)
            .Should().NotContain("system with empty extent");
    }
}
