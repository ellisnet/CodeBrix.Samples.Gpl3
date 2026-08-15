// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Text.RegularExpressions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// PARITY 6 (2026-08-14) end to end: <c>Keep_alive_together_engraver</c> must actually
/// RUN, which it never had.
/// <para>
/// Upstream guards its finalize loop with <c>scm_is_false (this_layer)</c>
/// (<c>keep-alive-together-engraver.cc:56</c>) — skip only the layer that opted out with
/// <c>#f</c>. The port wrote <c>!SchemeUtilities.ToBool (thisLayer)</c>, which is
/// exactly-<c>#t</c>, and <c>remove-layer</c> is a <c>key?</c> property whose real values
/// are positive integers, the symbols <c>any</c>/<c>above</c>/<c>below</c>, and <c>'()</c>
/// when unset. <c>#t</c> is not among them and is not even documented, so the guard fired
/// on EVERY spanner and neither <c>keep-alive-with</c> nor <c>make-dead-when</c> was ever
/// written. Trap 14's second half — the same shape as D1, in a different engraver.
/// </para>
/// <para>
/// Read off the ORACLE before it was asserted (rule 35):
/// <c>define-grob-properties.scm:1013</c> — "the <c>Keep_alive_together_engraver</c>
/// removes all <c>VerticalAxisGroup</c> grobs with a <c>remove-layer</c> larger than the
/// smallest retained <c>remove-layer</c>", and equal layers keep each other alive
/// (<c>scm_num_eq_p</c> pushes onto <c>live</c>, <c>scm_less_p</c> onto <c>dead</c>).
/// </para>
/// <para>
/// This is a RELATIONSHIP fence with a CONTROL RENDER (rules 33, 34), deliberately with
/// no literal count in it: the two renders differ ONLY in one integer, and before the fix
/// they came out identical, because an engraver that never runs cannot tell them apart.
/// </para>
/// </summary>
[Collection("engine-global-state")]
public class RemoveLayerEndToEndTests
{
    /// <summary>
    /// Two staves in one <c>Keep_alive_together_engraver</c> group. The second carries
    /// real notes but an empty <c>keepAliveInterfaces</c>, so nothing of its own keeps it
    /// alive and the layering decision is the only thing that can.
    /// </summary>
    /// <param name="secondLayer">The second staff's <c>remove-layer</c>.</param>
    /// <returns>The score source.</returns>
    private static string Source(string secondLayer) =>
        "\\version \"2.27.2\"\n"
        + "\\score {\n"
        + "  \\new StaffGroup \\with { \\consists Keep_alive_together_engraver } <<\n"
        + "    \\new Staff \\with {\n"
        + "      \\override VerticalAxisGroup.remove-empty = ##t\n"
        + "      \\override VerticalAxisGroup.remove-first = ##t\n"
        + "      \\override VerticalAxisGroup.remove-layer = #1\n"
        + "    } { c'4 c'4 c'4 c'4 }\n"
        + "    \\new Staff \\with {\n"
        + "      keepAliveInterfaces = #'()\n"
        + "      \\override VerticalAxisGroup.remove-empty = ##t\n"
        + "      \\override VerticalAxisGroup.remove-first = ##t\n"
        + "      \\override VerticalAxisGroup.remove-layer = #" + secondLayer + "\n"
        + "    } { e''4 e''4 e''4 e''4 }\n"
        + "  >>\n"
        + "}\n";

    private static string ScratchDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "lilyport-removelayer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int StaffLineCount(BatchRunResult result)
    {
        result.SvgPath.Should().NotBeNull();
        string svg = File.ReadAllText(result.SvgPath);
        return Regex.Matches(svg, "<line ").Count;
    }

    [Fact]
    public void an_equal_layer_is_kept_alive_where_a_greater_layer_is_removed()
    {
        //Arrange & Act
        // Layer 1 == layer 1: the engraver writes keep-alive-with, so the second staff
        // survives having no keepAliveInterfaces of its own.
        BatchRunResult equal = BatchRunner.RunText(
            Source("1"), "removelayerequal", null, ScratchDirectory());

        // Layer 2 > the smallest retained layer 1: the engraver writes make-dead-when
        // and the second staff goes.
        BatchRunResult greater = BatchRunner.RunText(
            Source("2"), "removelayergreater", null, ScratchDirectory());

        //Assert
        // Before the fix the guard skipped both, so both renders drew the same staff and
        // this comparison was an equality. It is the whole fence.
        StaffLineCount(equal).Should().BeGreaterThan(StaffLineCount(greater));
    }

    [Fact]
    public void opting_out_with_hash_f_still_leaves_the_layer_alone()
    {
        //Arrange & Act
        // #f is the ONE value upstream's guard does skip — "make a layer independent of
        // the Keep_alive_together_engraver". This is the control that keeps the fix from
        // being a blanket "always run": it must still behave like the removed case, since
        // an independent layer gets no keep-alive-with either.
        BatchRunResult independent = BatchRunner.RunText(
            Source("#f"), "removelayerfalse", null, ScratchDirectory());
        BatchRunResult greater = BatchRunner.RunText(
            Source("2"), "removelayergreater2", null, ScratchDirectory());

        //Assert
        StaffLineCount(independent).Should().Be(StaffLineCount(greater));
    }
}
